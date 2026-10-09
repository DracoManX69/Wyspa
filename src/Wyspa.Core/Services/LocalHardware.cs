using System.Runtime.InteropServices;

namespace Wyspa.Core.Services;

public sealed record LocalHardware(int LogicalProcessors, ulong MemoryBytes, string? Gpu)
{
    public string CpuName { get; init; } = "Processor name unavailable";
    public ulong AvailableMemoryBytes { get; init; }
    public ulong GpuMemoryBytes { get; init; }
    public bool DiscreteGpu { get; init; }
    public uint GpuDriverVersion { get; init; }
    public string Fingerprint => $"{CpuName}|{LogicalProcessors}|{MemoryBytes}|{Gpu}|{GpuMemoryBytes}|{DiscreteGpu}|{GpuDriverVersion}";
    public int WhisperThreads => Math.Clamp(LogicalProcessors / 2, 1, MemoryBytes > 0 && MemoryBytes < 8UL * 1024 * 1024 * 1024 ? 2 : 4);
    public int StreamingThreads => Math.Clamp(LogicalProcessors / 4, 1, 2);
    public string Description => $"{CpuName} · {LogicalProcessors} logical CPU cores · {(MemoryBytes == 0 ? "RAM unavailable" : $"{MemoryBytes / (1024d * 1024 * 1024):0.#} GB RAM")} · {(Gpu is null ? "No compatible Vulkan GPU detected" : Gpu + (GpuMemoryBytes > 0 ? $" ({GpuMemoryBytes / (1024d * 1024 * 1024):0.#} GB device memory)" : ""))}. Lightweight dictation uses {StreamingThreads} CPU thread(s); Whisper uses up to {WhisperThreads} plus the GPU when enabled.";
    public static LocalHardware Detect()
    {
        ulong memory = 0, available = 0, gpuMemory = 0; string? gpu = null; var cpu = "Processor name unavailable"; var discrete = false; uint driver = 0;
        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref status)) { memory = status.TotalPhysical; available = status.AvailablePhysical; }
            try { cpu = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null)?.ToString()?.Trim() ?? cpu; }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            (gpu, gpuMemory, discrete, driver) = DetectVulkan();
        }
        return new(Environment.ProcessorCount, memory, gpu) { CpuName = cpu, AvailableMemoryBytes = available, GpuMemoryBytes = gpuMemory, DiscreteGpu = discrete, GpuDriverVersion = driver };
    }
    private static (string? Name, ulong Memory, bool Discrete, uint Driver) DetectVulkan()
    {
        nint instance = 0, properties = 0;
        try
        {
            var create = new InstanceCreateInfo { Type = 1 };
            if (vkCreateInstance(ref create, 0, out instance) != 0) return (null, 0, false, 0);
            uint count = 0;
            if (vkEnumeratePhysicalDevices(instance, ref count, null) != 0 || count == 0 || count > 32) return (null, 0, false, 0);
            var devices = new nint[count];
            if (vkEnumeratePhysicalDevices(instance, ref count, devices) != 0) return (null, 0, false, 0);
            properties = Marshal.AllocHGlobal(4096);
            (string? Name, ulong Memory, bool Discrete, uint Driver) best = (null, 0, false, 0);
            foreach (var device in devices)
            {
                vkGetPhysicalDeviceProperties(device, properties);
                var type = Marshal.ReadInt32(properties, 16);
                if (type is 1 or 2)
                {
                    var name = Marshal.PtrToStringUTF8(properties + 20);
                    var driver = (uint)Marshal.ReadInt32(properties, 4);
                    vkGetPhysicalDeviceMemoryProperties(device, properties);
                    var heapCount = Math.Clamp(Marshal.ReadInt32(properties, 260), 0, 16);
                    ulong memory = 0;
                    for (var i = 0; i < heapCount; i++)
                        if ((Marshal.ReadInt32(properties, 264 + i * 16 + 8) & 1) != 0)
                            memory = Math.Max(memory, (ulong)Marshal.ReadInt64(properties, 264 + i * 16));
                    if (best.Name is null || type == 2 && !best.Discrete) best = (name, memory, type == 2, driver);
                }
            }
            return best;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { }
        finally { if (properties != 0) Marshal.FreeHGlobal(properties); if (instance != 0) vkDestroyInstance(instance, 0); }
        return (null, 0, false, 0);
    }
    [StructLayout(LayoutKind.Sequential)] private struct InstanceCreateInfo
    { public uint Type; public nint Next; public uint Flags; public nint Application; public uint LayerCount; public nint Layers; public uint ExtensionCount; public nint Extensions; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, AvailableExtended; }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("vulkan-1.dll")] private static extern int vkCreateInstance(ref InstanceCreateInfo create, nint allocator, out nint instance);
    [DllImport("vulkan-1.dll")] private static extern void vkDestroyInstance(nint instance, nint allocator);
    [DllImport("vulkan-1.dll")] private static extern int vkEnumeratePhysicalDevices(nint instance, ref uint count, [Out] nint[]? devices);
    [DllImport("vulkan-1.dll")] private static extern void vkGetPhysicalDeviceMemoryProperties(nint device, nint properties);
    [DllImport("vulkan-1.dll")] private static extern void vkGetPhysicalDeviceProperties(nint device, nint properties);
}

internal static class LocalBackgroundWork
{
    public static Task<T> Run<T>(Func<T> work, CancellationToken token)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        new Thread(() =>
        {
            try { token.ThrowIfCancellationRequested(); result.TrySetResult(work()); }
            catch (OperationCanceledException) { result.TrySetCanceled(token); }
            catch (Exception ex) { result.TrySetException(ex); }
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Wyspa local speech" }.Start();
        return result.Task;
    }
}
