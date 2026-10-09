using System.Globalization;
using System.Runtime.InteropServices;
using Chitra.Application.Abstractions;
using Chitra.Application.Dtos;
using Chitra.Infrastructure.Processes;
using Microsoft.Extensions.Logging;

namespace Chitra.Infrastructure.SystemInfo;

/// <summary>Reports CPU, RAM, disk and NVIDIA GPU details (via nvidia-smi) of the machine running the API.</summary>
public sealed partial class SystemInfoService(IMediaStorage storage, ILogger<SystemInfoService> logger) : ISystemInfoService
{
    private static (DateTime At, SystemInfoDto Info)? _cache;

    public async Task<SystemInfoDto> GetAsync(CancellationToken cancellationToken)
    {
        if (_cache is { } cached && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(15)) return cached.Info;

        var notes = new List<string>();
        var (gpus, smiAvailable) = await QueryNvidiaSmiAsync(cancellationToken);
        if (!smiAvailable)
        {
            notes.Add("nvidia-smi was not found, so no NVIDIA GPU was detected on the API host. If ComfyUI runs elsewhere (Docker, another PC), its GPU is shown from ComfyUI instead.");
        }

        DiskInfoDto? disk = null;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(storage.RootPath)!);
            disk = new DiskInfoDto(storage.RootPath, drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read disk info");
        }

        var info = new SystemInfoDto(
            RuntimeInformation.OSDescription,
            Environment.MachineName,
            GetCpuName(),
            Environment.ProcessorCount,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            GetAvailableMemory(),
            gpus,
            disk,
            smiAvailable,
            notes);

        _cache = (DateTime.UtcNow, info);
        return info;
    }

    private async Task<(IReadOnlyList<GpuInfoDto> Gpus, bool Available)> QueryNvidiaSmiAsync(CancellationToken ct)
    {
        try
        {
            var result = await ProcessRunner.RunAsync("nvidia-smi",
                ["--query-gpu=name,memory.total,memory.free,driver_version", "--format=csv,noheader,nounits"], ct, TimeSpan.FromSeconds(10));
            if (result.ExitCode != 0) return ([], true);
            return (ParseNvidiaSmi(result.StandardOutput), true);
        }
        catch (Exception ex) when (ex is ExecutableNotFoundException or TimeoutException)
        {
            return ([], false);
        }
    }

    internal static IReadOnlyList<GpuInfoDto> ParseNvidiaSmi(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(',', StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length >= 4)
            .Select(parts => new GpuInfoDto(
                parts[0],
                long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) ? total : 0,
                long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var free) ? free : null,
                parts[3],
                "nvidia-smi"))
            .ToList();

    private static string GetCpuName()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (key?.GetValue("ProcessorNameString") is string name) return name.Trim();
            }
            else if (File.Exists("/proc/cpuinfo"))
            {
                var line = File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal));
                if (line is not null) return line[(line.IndexOf(':') + 1)..].Trim();
            }
        }
        catch
        {
            // Fall through to a generic description.
        }

        return $"{RuntimeInformation.ProcessArchitecture} CPU";
    }

    private static long? GetAvailableMemory()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                return GlobalMemoryStatusEx(ref status) ? (long)status.AvailPhys : null;
            }

            if (File.Exists("/proc/meminfo"))
            {
                var line = File.ReadLines("/proc/meminfo").FirstOrDefault(l => l.StartsWith("MemAvailable:", StringComparison.Ordinal));
                var kb = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
                return long.TryParse(kb, out var value) ? value * 1024 : null;
            }
        }
        catch
        {
            // Not critical.
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
