namespace Wyspa.Core.Services;

internal static class KeywordTokenizerCompatibility
{
    // The upstream speech tokenizer disables BOS/EOS with -1. ML.Tokenizers' unigram
    // reader indexes those IDs even when emission is disabled. Map only that metadata
    // to the unknown-token slot; pieces, scores and normalization stay byte-identical.
    public static Stream WithoutBosEos(byte[] model) => new MemoryStream(Rewrite(model, false), false);
    private static byte[] Rewrite(byte[] source, bool trainer)
    {
        using var output = new MemoryStream(); var offset = 0;
        while (offset < source.Length)
        {
            var start = offset; var tag = Read(source, ref offset); var field = tag >> 3; var wire = tag & 7;
            if (!trainer && field == 2 && wire == 2)
            {
                var length = checked((int)Read(source, ref offset)); var value = Rewrite(source.AsSpan(offset, length).ToArray(), true); offset += length;
                Write(output, tag); Write(output, (ulong)value.Length); output.Write(value); continue;
            }
            switch (wire)
            {
                case 0: Read(source, ref offset); break;
                case 1: offset += 8; break;
                case 2: var length = checked((int)Read(source, ref offset)); offset += length; break;
                case 5: offset += 4; break;
                default: throw new InvalidDataException("Unsupported tokenizer metadata.");
            }
            if (offset > source.Length) throw new InvalidDataException("Truncated tokenizer metadata.");
            if (trainer && field is 41 or 42) { Write(output, tag); Write(output, 0); }
            else output.Write(source.AsSpan(start, offset - start));
        }
        return output.ToArray();
    }
    private static ulong Read(byte[] input, ref int offset)
    {
        ulong value = 0;
        for (var shift = 0; shift < 70; shift += 7)
        {
            if (offset >= input.Length) throw new InvalidDataException("Truncated tokenizer metadata.");
            var next = input[offset++]; value |= (ulong)(next & 127) << shift;
            if (next < 128) return value;
        }
        throw new InvalidDataException("Invalid tokenizer metadata.");
    }
    private static void Write(Stream output, ulong value)
    {
        while (value >= 128) { output.WriteByte((byte)((value & 127) | 128)); value >>= 7; }
        output.WriteByte((byte)value);
    }
}
