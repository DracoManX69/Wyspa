using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

/// <summary>Lossless PCM compression; already compressed audio is uploaded unchanged.</summary>
public sealed class AudioFilePreparationService(string encoderPath) : IAudioFilePreparationService
{
    // Leave space below Groq's 25 MB attachment limit for multipart headers.
    public const long MaxUploadBytes = 24_000_000;
    public static readonly string[] SupportedExtensions =
        [".wav", ".flac", ".mp3", ".m4a", ".ogg", ".webm", ".mpga", ".aif", ".aiff"];

    public async Task<PreparedAudio> PrepareAsync(string path, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
            throw new InvalidOperationException("Choose a WAV, FLAC, MP3, M4A, OGG, WebM, MPGA, or AIFF audio file.");
        var originalBytes = new FileInfo(path).Length;
        if (originalBytes == 0)
            throw new InvalidOperationException("This audio file is empty. Choose another file.");

        if (extension is not (".wav" or ".aif" or ".aiff" or ".flac"))
        {
            CheckUploadSize(originalBytes);
            return new PreparedAudio([path], originalBytes, originalBytes, "Already compressed; original audio preserved.");
        }

        if (extension == ".flac" && originalBytes <= MaxUploadBytes)
            return new PreparedAudio([path], originalBytes, originalBytes, "Already lossless FLAC; no conversion needed.");

        if (!File.Exists(encoderPath))
            throw new InvalidOperationException("The bundled audio compressor is missing. Reinstall Wyspa to restore it.");

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "Wyspa", "FileTranscription", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var compressed = path;
            if (extension != ".flac")
            {
                progress?.Report("Compressing locally without losing audio quality…");
                compressed = Path.Combine(temporaryDirectory, "audio.flac");
                var success = await EncodeAsync(path, compressed, [], cancellationToken);
                if (!success)
                {
                    // FLAC cannot represent floating-point or non-PCM WAV losslessly.
                    // Never quantize or resample it just to obtain a smaller upload.
                    if (extension == ".wav" && originalBytes <= MaxUploadBytes)
                    {
                        Directory.Delete(temporaryDirectory, recursive: true);
                        return new PreparedAudio([path], originalBytes, originalBytes,
                            "This WAV could not be compressed losslessly; original audio preserved.");
                    }
                    throw new InvalidOperationException("This file could not be compressed losslessly. Use an integer PCM WAV/AIFF or FLAC file, or a WAV under 24 MB.");
                }
            }

            var compressedBytes = new FileInfo(compressed).Length;
            if (compressedBytes <= MaxUploadBytes)
            {
                if (extension == ".wav" && originalBytes <= compressedBytes && originalBytes <= MaxUploadBytes)
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                    return new PreparedAudio([path], originalBytes, originalBytes, "Original WAV is smaller; audio preserved.");
                }
                return new PreparedAudio([compressed], originalBytes, compressedBytes,
                    "Lossless FLAC • original sample rate, bit depth, and channels.", temporaryDirectory);
            }

            progress?.Report("Splitting lossless audio into upload-sized parts…");
            var (totalSamples, bytesPerFrame) = ReadFlacInfo(compressed);
            // Bound each part by uncompressed size, including incompressible audio.
            var samplesPerPart = 20_000_000L / bytesPerFrame;
            var paths = new List<string>();
            for (long start = 0; start < totalSamples; start += samplesPerPart)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var end = Math.Min(totalSamples, start + samplesPerPart);
                var part = Path.Combine(temporaryDirectory, $"part-{paths.Count + 1:D5}.flac");
                progress?.Report($"Preparing lossless part {paths.Count + 1} of {(totalSamples + samplesPerPart - 1) / samplesPerPart}…");
                if (!await EncodeAsync(compressed, part,
                    [$"--skip={start.ToString(CultureInfo.InvariantCulture)}", $"--until={end.ToString(CultureInfo.InvariantCulture)}"], cancellationToken))
                    throw new InvalidOperationException("The audio could not be split. No incomplete file was uploaded.");
                CheckUploadSize(new FileInfo(part).Length);
                paths.Add(part);
            }

            if (compressed != path) File.Delete(compressed);
            return new PreparedAudio(paths, originalBytes, paths.Sum(p => new FileInfo(p).Length),
                $"Lossless FLAC • {paths.Count} parts • all audio preserved.", temporaryDirectory);
        }
        catch
        {
            Directory.Delete(temporaryDirectory, recursive: true);
            throw;
        }
    }

    private async Task<bool> EncodeAsync(string input, string output, string[] range, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(encoderPath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        // Verification compares decoded output with the original PCM as it encodes.
        foreach (var argument in new[] { "--silent", "--verify", "-5", "--no-padding", "--no-seektable", "--output-name=" + output }
            .Concat(range).Concat(["--", input]))
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the audio compressor.");
        // Drain diagnostics, but never log local paths or audio metadata.
        var error = process.StandardError.ReadToEndAsync();
        var outputText = process.StandardOutput.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(error, outputText);
            cancellationToken.ThrowIfCancellationRequested();
            return process.ExitCode == 0;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static (long Samples, int BytesPerFrame) ReadFlacInfo(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[42];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("fLaC"u8) || (header[4] & 0x7f) != 0 || header[7] != 34)
            throw new InvalidOperationException("The FLAC file has an invalid audio header.");
        var packed = BinaryPrimitives.ReadUInt64BigEndian(header.Slice(18, 8));
        var samples = (long)(packed & 0xfffffffffUL);
        var channels = (int)((packed >> 41) & 7) + 1;
        var bits = (int)((packed >> 36) & 31) + 1;
        if (samples == 0) throw new InvalidOperationException("This FLAC file has no known length. Export it again before transcribing.");
        return (samples, channels * ((bits + 7) / 8));
    }

    private static void CheckUploadSize(long bytes)
    {
        if (bytes > MaxUploadBytes)
            throw new InvalidOperationException("This compressed file exceeds the 24 MB upload limit. Split it locally, or select a PCM WAV/AIFF or FLAC file for automatic lossless splitting.");
    }
}
