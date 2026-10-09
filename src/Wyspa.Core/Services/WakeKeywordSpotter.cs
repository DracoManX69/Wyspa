using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SherpaOnnx;

namespace Wyspa.Core.Services;

// The pinned sherpa 1.13.8 managed result exposes text only; its documented C API also
// supplies timing. Keep the native layout here tied to that version, without reflection.
internal sealed class WakeKeywordSpotter : IDisposable
{
    private sealed class ModelHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ModelHandle(IntPtr pointer) : base(true) => SetHandle(pointer);
        protected override bool ReleaseHandle() { Destroy(handle); return true; }
    }
    internal sealed record Result(string Keyword, string[] Tokens, float[] Timestamps, float StartTime);
    private readonly ModelHandle _handle;
    public WakeKeywordSpotter(KeywordSpotterConfig config)
    {
        _handle = new(Create(ref config));
        if (_handle.IsInvalid) throw new InvalidOperationException("Could not initialize the local wake detector.");
    }
    public OnlineStream CreateStream(string keywords)
    {
        var pointer = CreateStream(_handle, Encoding.UTF8.GetBytes(keywords + "\0"));
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("Could not create the wake phrase stream.");
        return new OnlineStream(pointer);
    }
    public bool IsReady(OnlineStream stream) => Ready(_handle, stream.Handle) != 0;
    public void Decode(OnlineStream stream) => Decode(_handle, stream.Handle);
    public void Reset(OnlineStream stream) => Reset(_handle, stream.Handle);
    public Result GetResult(OnlineStream stream)
    {
        var pointer = GetResult(_handle, stream.Handle);
        if (pointer == IntPtr.Zero) return new("", [], [], 0);
        try
        {
            var value = Marshal.PtrToStructure<NativeResult>(pointer);
            var count = value.Count is > 0 and <= 64 ? value.Count : 0;
            var times = new float[count]; var tokens = new string[count];
            if (value.Timestamps != IntPtr.Zero) Marshal.Copy(value.Timestamps, times, 0, count);
            for (var i = 0; i < count; i++) tokens[i] = value.TokensArray == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(value.TokensArray, i * IntPtr.Size)) ?? "";
            return new(Marshal.PtrToStringUTF8(value.Keyword) ?? "", tokens, times, value.StartTime);
        }
        finally { DestroyResult(pointer); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeResult { public IntPtr Keyword, Tokens, TokensArray; public int Count; public IntPtr Timestamps; public float StartTime; public IntPtr Json; }
    private const string Library = "sherpa-onnx-c-api";
    [DllImport(Library, EntryPoint="SherpaOnnxCreateKeywordSpotter")] private static extern IntPtr Create(ref KeywordSpotterConfig config);
    [DllImport(Library, EntryPoint="SherpaOnnxDestroyKeywordSpotter")] private static extern void Destroy(IntPtr model);
    [DllImport(Library, EntryPoint="SherpaOnnxCreateKeywordStreamWithKeywords")] private static extern IntPtr CreateStream(ModelHandle model, byte[] keywords);
    [DllImport(Library, EntryPoint="SherpaOnnxIsKeywordStreamReady")] private static extern int Ready(ModelHandle model, IntPtr stream);
    [DllImport(Library, EntryPoint="SherpaOnnxDecodeKeywordStream")] private static extern void Decode(ModelHandle model, IntPtr stream);
    [DllImport(Library, EntryPoint="SherpaOnnxResetKeywordStream")] private static extern void Reset(ModelHandle model, IntPtr stream);
    [DllImport(Library, EntryPoint="SherpaOnnxGetKeywordResult")] private static extern IntPtr GetResult(ModelHandle model, IntPtr stream);
    [DllImport(Library, EntryPoint="SherpaOnnxDestroyKeywordResult")] private static extern void DestroyResult(IntPtr result);
    public void Dispose() => _handle.Dispose();
}
