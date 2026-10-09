using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public static class LocalBackendPolicy
{
    public static bool SupportsGpu(LocalModel model, LocalHardware hardware) => hardware.Gpu is not null &&
        (!model.IsFolder || model.Bundle?.Engine == "FasterWhisper" && hardware.Gpu.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
    public static bool UseGpu(LocalModel model, LocalHardware hardware, bool allowed, SpeechPerformanceReport? report, bool? requested = null)
    {
        if (!allowed || !SupportsGpu(model, hardware)) return false;
        if (requested.HasValue) return requested.Value;
        if (!SpeechModelAdvisor.Current(report, hardware, allowed)) return true;
        var rows = report!.Results.Where(r => r.Provider == "local" && r.ModelId == model.Id).ToArray();
        var cpu = rows.FirstOrDefault(r => r.RequestedDevice == "CPU" && r.Device == "CPU" && r.Error is null);
        if (cpu is null) return true;
        var gpu = rows.FirstOrDefault(r => r.RequestedDevice == "GPU" && r.Device == "GPU" && r.Error is null);
        if (gpu is null) return !rows.Any(r => r.RequestedDevice == "GPU");
        // Prefer CPU for near ties and sample-quality regressions, reducing background GPU work.
        return gpu.WordErrors <= cpu.WordErrors + 1 && gpu.TypicalSeconds < cpu.TypicalSeconds * .9;
    }
}
