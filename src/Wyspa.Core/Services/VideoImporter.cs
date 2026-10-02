using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Wyspa.Core.Abstractions;

namespace Wyspa.Core.Services;

public sealed class VideoImporter(string toolDirectory) : IVideoImporter
{
    public static string NormalizeUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Paste a public YouTube video URL.");
        var host = uri.IdnHost.ToLowerInvariant();
        string? id = null;
        if (host is "youtu.be" or "www.youtu.be") id = uri.AbsolutePath.Trim('/');
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com" or "www.youtube-nocookie.com")
        {
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length == 2 && parts[0] is "shorts" or "embed" or "live") id = parts[1];
            else if (uri.AbsolutePath == "/watch")
                id = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == "v")?.ElementAt(1);
        }
        if (id is null || !Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{11}\z"))
            throw new ArgumentException("Use a single YouTube video link, not a playlist, channel, or login page.");
        return "https://www.youtube.com/watch?v=" + id;
    }

    public async Task<PreparedVideo> PrepareAsync(string url, IProgress<string> progress, CancellationToken token)
    {
        url = NormalizeUrl(url);
        var downloader = Path.Combine(toolDirectory, "yt-dlp.exe");
        var ffmpeg = Path.Combine(toolDirectory, "ffmpeg.exe");
        var deno = Path.Combine(toolDirectory, "deno.exe");
        if (!File.Exists(downloader) || !File.Exists(ffmpeg) || !File.Exists(deno))
            throw new InvalidOperationException("YouTube tools are missing. Reinstall the complete Wyspa v7 package.");
        var directory = Path.Combine(Path.GetTempPath(), "Wyspa", "video-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var common = new[] { "--ignore-config", "--no-plugin-dirs", "--no-playlist", "--no-progress", "--no-warnings", "--no-cache-dir", "--no-remote-components",
                "--js-runtimes", "deno:" + deno, "--ffmpeg-location", toolDirectory, "--socket-timeout", "20", "--retries", "2" };
            progress.Report("Reading video details…");
            var metadata = await RunAsync(downloader, [.. common, "--dump-single-json", "--skip-download", "--", url], directory, token, 4_000_000);
            using var json = JsonDocument.Parse(metadata);
            var root = json.RootElement;
            if ((root.TryGetProperty("is_live", out var live) && live.ValueKind == JsonValueKind.True) ||
                (root.TryGetProperty("live_status", out var status) && status.GetString() is "is_live" or "is_upcoming"))
                throw new InvalidOperationException("This broadcast is still live or scheduled. Use its single video URL once it has finished.");
            var title = root.TryGetProperty("title", out var value) ? value.GetString() ?? "YouTube video" : "YouTube video";
            progress.Report("Downloading audio: " + title);
            await RunAsync(downloader, [.. common, "-f", "bestaudio/best", "-o", "source.%(ext)s", "--", url], directory, token);
            var source = Directory.GetFiles(directory, "source.*").FirstOrDefault(p => !p.EndsWith(".part") && !p.EndsWith(".ytdl"))
                ?? throw new InvalidOperationException("The video did not provide a downloadable audio stream.");
            progress.Report("Preparing audio for transcription…");
            // Fixed five-minute parts are ~9.6 MB PCM each and safely below Groq's free-tier upload limit.
            await RunAsync(ffmpeg, ["-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", source,
                "-map", "0:a:0", "-vn", "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", "-f", "segment", "-segment_time", "300", "-reset_timestamps", "1", "part-%05d.wav"], directory, token);
            File.Delete(source);
            var parts = Directory.GetFiles(directory, "part-*.wav").Order(StringComparer.Ordinal).ToArray();
            if (parts.Length == 0) throw new InvalidOperationException("No audio was found in this video.");
            return new(title, directory, parts);
        }
        catch { Directory.Delete(directory, true); throw; }
    }

    public static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, string directory, CancellationToken token, int outputLimit = 32000)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the video tools.");
        using var registration = token.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        var stdout = ReadBoundedAsync(process.StandardOutput, outputLimit);
        var stderr = ReadBoundedAsync(process.StandardError, 4000);
        await process.WaitForExitAsync(CancellationToken.None);
        var output = await stdout; var error = await stderr;
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Video import failed. The video may be unavailable, require login, or YouTube may be blocking this request. " + error.Trim());
        return output;
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit)
    {
        var result = new System.Text.StringBuilder(); var buffer = new char[4096]; int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
            if (result.Length < limit) result.Append(buffer, 0, Math.Min(read, limit - result.Length));
        return result.ToString();
    }
}
