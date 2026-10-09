using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.ML.Tokenizers;
using SherpaOnnx;
using Wyspa.Core.Services;

internal static class WakeOptimisationResearch
{
    private sealed record Fixture(string file, string text, bool wake, string voice, int rate);
    public static async Task Live(string output, string fixtures, bool noisy = false)
    {
        var clips = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(fixtures, "manifest.json")))!;
        using var detector = new WakeKeywordEngine(); await detector.PrepareAsync(null, default); var rows = new List<object>();
        foreach (var clip in clips)
        {
            detector.Reset(); await using var input = File.OpenRead(Path.Combine(fixtures, clip.file)); var original = await new Whisper.net.Wave.WaveParser(input).GetAvgSamplesAsync();
            if (noisy)
            {
                var random = new Random(42);
                original = original.Select(s => (float)(s * .4 + (random.NextDouble() * 2 - 1) * .004)).ToArray();
            }
            var audio = new float[original.Length + 16000]; original.CopyTo(audio, 0); var found = false; var watch = Stopwatch.StartNew(); var at = 0;
            for (var o = 0; o < audio.Length; o += 960)
                if (await detector.ProcessAsync(audio.AsSpan(o, Math.Min(960, audio.Length - o)).ToArray(), "hey whisper", .45, default)) { found = true; at = o + 960; break; }
            rows.Add(new { clip.file, clip.text, clip.wake, clip.voice, clip.rate, found, at=at/16000d, processingMs=watch.Elapsed.TotalMilliseconds, verificationSamples=detector.LastVerificationSamples });
        }
        File.WriteAllText(Path.Combine(output,noisy ? "optimised-noisy-corpus.json" : "optimised-corpus.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Processed " + rows.Count + " controlled fixtures through the production wake pipeline.");
    }
    public static async Task Run(string output, string fixtures, string newModel, string bundled)
    {
        var clips = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(fixtures, "manifest.json")))!;
        using var storeHttp = new HttpClient(); var store = new OptionalDependencyStore(storeHttp);
        var paths = new List<string>(); foreach (var f in WakeKeywordModel.Files) paths.Add(await store.EnsureAsync(f, null, null, default));
        using var bpe = KeywordTokenizerCompatibility.WithoutBosEos(File.ReadAllBytes(paths[3]));
        var tokenizer = SentencePieceTokenizer.Create(bpe, false, false);
        var tokens = tokenizer.EncodeToTokens("HEY WHISPER", out _, false, false);
        var oldKeywords = string.Join(" ", tokens.Select(t => t.Value)) + " :1.5 #0.460 @hey_whisper";
        var dictionary = File.ReadLines(Path.Combine(newModel, "en.phone")).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(w => w.Length > 1).GroupBy(w => w[0].ToLowerInvariant()).ToDictionary(g => g.Key, g => string.Join(" ", g.First().Skip(1)));
        var newKeywords = dictionary["hey"] + " " + dictionary["whisper"] + " :1.5 #0.460 @hey_whisper";
        KeywordSpotter Create(string encoder, string decoder, string joiner, string tokenFile, int blanks)
        {
            var empty = Path.Combine(output, "empty-keywords.txt"); File.WriteAllText(empty, "");
            var c = new KeywordSpotterConfig(); c.FeatConfig.SampleRate = 16000; c.FeatConfig.FeatureDim = 80;
            c.ModelConfig.Transducer.Encoder = encoder; c.ModelConfig.Transducer.Decoder = decoder; c.ModelConfig.Transducer.Joiner = joiner;
            c.ModelConfig.Tokens = tokenFile; c.ModelConfig.NumThreads = 1; c.ModelConfig.Provider = "cpu"; c.KeywordsFile = empty; c.MaxActivePaths = 8; c.NumTrailingBlanks = blanks;
            return new KeywordSpotter(c);
        }
        using var old = Create(paths[0], paths[1], paths[2], paths[4], 2);
        var timingConfig = new KeywordSpotterConfig(); timingConfig.FeatConfig.SampleRate = 16000; timingConfig.FeatConfig.FeatureDim = 80; timingConfig.ModelConfig.Transducer.Encoder=paths[0];timingConfig.ModelConfig.Transducer.Decoder=paths[1];timingConfig.ModelConfig.Transducer.Joiner=paths[2];timingConfig.ModelConfig.Tokens=paths[4];timingConfig.ModelConfig.NumThreads=1;timingConfig.ModelConfig.Provider="cpu";timingConfig.KeywordsFile=Path.Combine(output,"empty-keywords.txt");timingConfig.MaxActivePaths=8;timingConfig.NumTrailingBlanks=2;
        using var timed = new WakeKeywordSpotter(timingConfig);
        using var newer = Create(Path.Combine(newModel, "encoder-epoch-13-avg-2-chunk-8-left-64.int8.onnx"), Path.Combine(newModel,"decoder-epoch-13-avg-2-chunk-8-left-64.onnx"),Path.Combine(newModel,"joiner-epoch-13-avg-2-chunk-8-left-64.int8.onnx"),Path.Combine(newModel,"tokens.txt"),1);
        var config = new OnlineRecognizerConfig(); config.FeatConfig.SampleRate = 16000; config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = Path.Combine(bundled, ZipformerModel.Files[0].Name); config.ModelConfig.Transducer.Decoder = Path.Combine(bundled, ZipformerModel.Files[1].Name);
        config.ModelConfig.Transducer.Joiner = Path.Combine(bundled, ZipformerModel.Files[2].Name); config.ModelConfig.Tokens = Path.Combine(bundled, "tokens.txt");
        config.ModelConfig.NumThreads = 1; config.ModelConfig.Provider = "cpu"; config.DecodingMethod = "greedy_search";
        using var zip = new OnlineRecognizer(config); using var whisper = new WakePhraseVerifier(); await whisper.PrepareAsync(default);
        var rows = new List<object>();
        (bool Found, int At, double Ms) Spot(KeywordSpotter model, string keywords, float[] audio)
        {
            using var stream = model.CreateStream(keywords); var watch = Stopwatch.StartNew();
            for (var o = 0; o < audio.Length; o += 960)
            {
                stream.AcceptWaveform(16000, audio.AsSpan(o, Math.Min(960, audio.Length - o)).ToArray());
                while (model.IsReady(stream)) { model.Decode(stream); if (!string.IsNullOrEmpty(model.GetResult(stream).Keyword)) return (true, Math.Min(audio.Length, o + 960), watch.Elapsed.TotalMilliseconds); }
            }
            return (false, audio.Length, watch.Elapsed.TotalMilliseconds);
        }
        foreach (var clip in clips)
        {
            await using var input = File.OpenRead(Path.Combine(fixtures,clip.file)); var original = await new Whisper.net.Wave.WaveParser(input).GetAvgSamplesAsync();
            var audio = new float[original.Length + 16000]; original.CopyTo(audio,0);
            var a = Spot(old,oldKeywords,audio); var b = Spot(newer,newKeywords,audio);
            var candidate = audio.Take(a.Found ? a.At : audio.Length).TakeLast(64000).ToArray();
            var watch = Stopwatch.StartNew(); var tinyText = await whisper.RecognizeAsync(candidate, default); var tinyMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart(); using var session = zip.CreateStream(); session.AcceptWaveform(16000,candidate); session.AcceptWaveform(16000,new float[8000]); session.InputFinished();
            while (zip.IsReady(session)) zip.Decode(session);
            var zipText = zip.GetResult(session).Text; var zipMs = watch.Elapsed.TotalMilliseconds;
            using var timingStream = timed.CreateStream(oldKeywords); timed.GetResult(timingStream);
            timingStream.AcceptWaveform(16000,audio.Take(a.At).ToArray());
            WakeKeywordSpotter.Result? timing = null;
            while (timed.IsReady(timingStream)) { timed.Decode(timingStream); var value = timed.GetResult(timingStream); if (!string.IsNullOrEmpty(value.Keyword)) timing=value; }
            var isolated = timing is null ? candidate : WakeVerificationWindow.Extract(audio.Take(a.At).TakeLast(64000).ToArray(),a.At,timing.StartTime,timing.Timestamps,timing.Tokens);
            var isolatedText = await whisper.RecognizeAsync(isolated,default,13);
            var padded = new float[candidate.Length+3200];candidate.CopyTo(padded,0);
            var paddedText = await whisper.RecognizeAsync(padded,default,13);
            var extended = audio.Take(Math.Min(audio.Length,a.At+3200)).TakeLast(64000).ToArray();
            var extendedText = await whisper.RecognizeAsync(extended,default,13);
            rows.Add(new {clip.file,clip.text,clip.wake,clip.voice,clip.rate,timing,isolatedText,paddedText,extendedText,oldFound=a.Found,newFound=b.Found,oldAt=a.At/16000d,newAt=b.At/16000d,oldMs=a.Ms,newMs=b.Ms,tinyText,tinyMatch=WakePhraseVerifier.Matches(tinyText,"hey whisper"),tinyMs,zipText,zipMatch=WakePhraseVerifier.Matches(zipText,"hey whisper"),zipMs});
        }
        File.WriteAllText(Path.Combine(output,"model-comparison.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Compared " + rows.Count + " synthesized voice/rate/sentence fixtures.");
    }
}
