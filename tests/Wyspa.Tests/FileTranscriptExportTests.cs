using System.Globalization;
using System.Text;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class FileTranscriptExportTests
{
    [Fact]
    public void UsesProviderSegments_InTimestampOrder_AndKeepsPunctuation()
    {
        var result = FileTranscriptExport.Parse("""{"text":"Hello, world! Next sentence.","segments":[{"start":5,"end":8,"text":" Next sentence."},{"start":1.2,"end":3.4,"text":" Hello, world! "}],"words":[{"start":1,"end":2,"word":"ignored"}]}""");
        Assert.Equal("Hello, world! Next sentence.", result.Text);
        Assert.Equal(new TranscriptSegment(1.2, 3.4, "Hello, world!"), result.Segments[0]);
        Assert.Equal(2, result.Segments.Count);
    }

    [Fact]
    public void LocalWords_AreGroupedAtPausesAndSentenceEnds_UsingTheirOriginalTimes()
    {
        var speech = new SpeechResult("Hello there. Another phrase", [new(1, 1.2, "Hello"), new(1.3, 2, "there."), new(4, 4.5, "Another"), new(4.6, 5, "phrase")]);
        var result = FileTranscriptExport.Parse(TranscriptionRouter.FormatLocalResult(speech, "verbose_json"));
        Assert.Equal(new[] { new TranscriptSegment(1, 2, "Hello there."), new TranscriptSegment(4, 5, "Another phrase") }, result.Segments);
    }

    [Fact]
    public void LocalCoarseSpan_RemainsOneRow_WithoutInventingWordTimings()
    {
        var speech = new SpeechResult("Whole passage", [new(0, 18.5, "Whole passage")]);
        var result = FileTranscriptExport.Parse(TranscriptionRouter.FormatLocalResult(speech, "verbose_json"));
        Assert.Equal(new TranscriptSegment(0, 18.5, "Whole passage"), Assert.Single(result.Segments));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"text\":\"missing times\"}")]
    [InlineData("{\"text\":\"bad\",\"segments\":[{\"start\":-1,\"end\":2,\"text\":\"bad\"}]}")]
    [InlineData("{\"text\":\"bad\",\"segments\":[{\"start\":3,\"end\":2,\"text\":\"bad\"}]}")]
    [InlineData("{\"text\":\"missing end\",\"segments\":[{\"start\":0,\"text\":\"missing end\"}]}")]
    public void InvalidResponse_DoesNotProduceInventedTimestamps(string json)
    {
        Assert.Throws<InvalidOperationException>(() => FileTranscriptExport.Parse(json));
    }

    [Fact]
    public void Timestamp_RoundsToMilliseconds_AndCarriesIntoTheNextMinute()
    {
        Assert.Equal("00:01:00.000", FileTranscriptExport.Timestamp(59.9996));
        Assert.Equal("00:00:01.235", FileTranscriptExport.Timestamp(1.2346));
    }

    [Fact]
    public void Csv_QuotesCommasQuotesAndNewlines_AndKeepsHoursBeyond24_InAnyCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("start,end,text\r\n25:01:01.125,25:01:02.750,\"Café, \"\"hello\"\"\n世界\"\r\n",
                FileTranscriptExport.ToCsv([new(90061.125, 90062.75, "Café, \"hello\"\n世界")]));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task Store_PublishesUtf8WithBom_WithoutOverwritingSameNamedAudio()
    {
        var directory = Directory.CreateTempSubdirectory("wyspa-csv-store-").FullName;
        try
        {
            var store = new FileTranscriptCsvStore(directory);
            TranscriptSegment[] segments = [new(0, 1, "日本語")];
            var first = await store.SaveAsync("audio.wav", segments, CancellationToken.None);
            var second = await store.SaveAsync("audio.wav", segments, CancellationToken.None);
            Assert.NotEqual(first, second);
            var bytes = await File.ReadAllBytesAsync(first);
            Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
            Assert.Equal(FileTranscriptExport.ToCsv(segments), File.ReadAllText(first));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync("cancel.wav", segments, cancellation.Token));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
