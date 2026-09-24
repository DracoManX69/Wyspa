using System.Diagnostics;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class FlacFactAttribute : FactAttribute
{
    public static string Encoder => Environment.GetEnvironmentVariable("WYSPA_FLAC_PATH") ??
        Path.Combine(AppContext.BaseDirectory, "Tools", "Flac", "flac.exe");

    public FlacFactAttribute()
    {
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("WYSPA_FLAC_PATH") is null)
            Skip = "Set WYSPA_FLAC_PATH to a native FLAC encoder to run lossless integration tests on Linux/macOS.";
    }
}

public sealed class AudioFilePreparationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("wyspa-file-tests-").FullName;
    private AudioFilePreparationService Service => new(FlacFactAttribute.Encoder);

    [FlacFact]
    public async Task CompressesWav_AndDecodesToIdenticalPcm()
    {
        var (path, pcm) = CreateWav("stereo 24-bit.wav", 48000, 2, 24, 48000 * 2, random: false);
        string generated;
        using (var prepared = await Service.PrepareAsync(path, null, CancellationToken.None))
        {
            generated = Assert.Single(prepared.Paths);
            Assert.EndsWith(".flac", generated);
            Assert.True(prepared.UploadBytes < prepared.OriginalBytes);
            Assert.Equal(pcm, await DecodeRawAsync(generated));
        }
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(generated));
    }

    [FlacFact]
    public async Task SplitsLargeIncompressibleAudio_WithoutMissingOrDuplicatingSamples()
    {
        var (path, pcm) = CreateWav("large.wav", 48000, 2, 16, 6_100_000, random: true);
        string[] generated;
        using (var prepared = await Service.PrepareAsync(path, null, CancellationToken.None))
        {
            generated = prepared.Paths.ToArray();
            Assert.True(generated.Length > 1);
            using var decoded = new MemoryStream();
            foreach (var part in generated)
            {
                Assert.True(new FileInfo(part).Length <= AudioFilePreparationService.MaxUploadBytes);
                await decoded.WriteAsync(await DecodeRawAsync(part));
            }
            Assert.Equal(pcm, decoded.ToArray());
        }
        Assert.All(generated, part => Assert.False(File.Exists(part)));
        Assert.True(File.Exists(path));
    }

    [FlacFact]
    public async Task FloatWav_IsKeptUnchanged_InsteadOfReducingPrecision()
    {
        var (path, _) = CreateWav("float.wav", 48000, 1, 32, 1000, random: false, format: 3);
        var original = await File.ReadAllBytesAsync(path);
        using var prepared = await Service.PrepareAsync(path, null, CancellationToken.None);
        Assert.Equal(path, Assert.Single(prepared.Paths));
        Assert.Contains("original audio preserved", prepared.Description);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task CompressedFile_IsNotReencodedOrDeleted()
    {
        var path = Path.Combine(_directory, "already-compressed.mp3");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        using (var prepared = await Service.PrepareAsync(path, null, CancellationToken.None))
        {
            Assert.Equal(path, Assert.Single(prepared.Paths));
            Assert.Equal(3, prepared.UploadBytes);
        }
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task OversizedCompressedFile_IsRejectedBeforeUpload()
    {
        var path = Path.Combine(_directory, "large.mp3");
        using (var stream = File.Create(path)) stream.SetLength(AudioFilePreparationService.MaxUploadBytes + 1);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service.PrepareAsync(path, null, CancellationToken.None));
        Assert.Contains("24 MB", error.Message);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task CancelledPreparation_DoesNotTouchSource()
    {
        var (path, _) = CreateWav("cancel.wav", 16000, 1, 16, 16000, random: false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.PrepareAsync(path, null, cancellation.Token));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task EmptyAndUnsupportedInputs_AreRejected()
    {
        var empty = Path.Combine(_directory, "empty.wav");
        await File.WriteAllBytesAsync(empty, []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.PrepareAsync(empty, null, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.PrepareAsync("document.pdf", null, CancellationToken.None));
    }

    private (string Path, byte[] Pcm) CreateWav(string name, int rate, short channels, short bits, int frames, bool random, short format = 1)
    {
        var path = Path.Combine(_directory, name);
        var pcm = new byte[frames * channels * (bits / 8)];
        if (random) new Random(42).NextBytes(pcm);
        else
            for (var i = 0; i < pcm.Length; i++) pcm[i] = (byte)((i / 64) % 128);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + pcm.Length); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write(format); writer.Write(channels); writer.Write(rate);
        writer.Write(rate * channels * bits / 8); writer.Write((short)(channels * bits / 8)); writer.Write(bits);
        writer.Write("data"u8); writer.Write(pcm.Length); writer.Write(pcm);
        return (path, pcm);
    }

    private async Task<byte[]> DecodeRawAsync(string path)
    {
        var output = Path.Combine(_directory, Guid.NewGuid() + ".raw");
        var info = new ProcessStartInfo(FlacFactAttribute.Encoder) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-d", "--silent", "--force-raw-format", "--endian=little", "--sign=signed", "-o", output, "--", path }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await errors);
        return await File.ReadAllBytesAsync(output);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
