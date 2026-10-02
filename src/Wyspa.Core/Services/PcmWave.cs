namespace Wyspa.Core.Services;

public static class PcmWave
{
    public static void Write(string path, float[] samples)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + samples.Length * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000);
        writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(samples.Length * 2);
        foreach (var sample in samples) writer.Write((short)Math.Clamp((int)(sample * 32767), short.MinValue, short.MaxValue));
    }
}
