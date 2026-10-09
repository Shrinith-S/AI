using Chitra.Application.Abstractions;
using Chitra.Application.Common;
using Microsoft.Extensions.Options;

namespace Chitra.Infrastructure.Storage;

/// <summary>Stores media on the local file system under images/, videos/, audio/, projects/, thumbnails/ and uploads/.</summary>
public sealed class LocalMediaStorage : IMediaStorage
{
    public static readonly string[] Categories = ["images", "videos", "audio", "projects", "thumbnails", "uploads"];

    private readonly long _minFreeBytes;

    public LocalMediaStorage(IOptions<StorageOptions> options)
    {
        RootPath = Path.GetFullPath(options.Value.RootPath);
        _minFreeBytes = Math.Max(0, options.Value.MinFreeSpaceMb) * 1024 * 1024;
        foreach (var category in Categories)
        {
            Directory.CreateDirectory(Path.Combine(RootPath, category));
        }
    }

    public string RootPath { get; }

    public string CreatePath(string category, string extension)
    {
        if (!Categories.Contains(category)) throw new ArgumentException($"Unknown storage category '{category}'.", nameof(category));
        extension = NormalizeExtension(extension);
        var now = DateTime.UtcNow;
        var relative = $"{category}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension}";
        Directory.CreateDirectory(Path.GetDirectoryName(GetFullPath(relative))!);
        return relative;
    }

    public async Task<StoredFile> SaveAsync(Stream content, string category, string extension, CancellationToken cancellationToken)
    {
        var relative = CreatePath(category, extension);
        var fullPath = GetFullPath(relative);
        try
        {
            await using (var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            return new StoredFile(relative, Path.GetFileName(fullPath), new FileInfo(fullPath).Length);
        }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            TryDelete(fullPath);
            throw new AppException(ErrorCode.DiskFull, $"The disk holding {RootPath} is full. Free some space and try again.", ex);
        }
        catch
        {
            TryDelete(fullPath);
            throw;
        }
    }

    public string GetFullPath(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var root = RootPath.EndsWith(Path.DirectorySeparatorChar) ? RootPath : RootPath + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Path escapes the storage root.");
        }

        return full;
    }

    public bool Exists(string relativePath) => File.Exists(GetFullPath(relativePath));

    public void Delete(string relativePath)
    {
        var full = GetFullPath(relativePath);
        if (File.Exists(full)) File.Delete(full);
    }

    public void EnsureFreeSpace(long requiredBytes = 0)
    {
        var drive = new DriveInfo(Path.GetPathRoot(RootPath)!);
        if (drive.IsReady && drive.AvailableFreeSpace < _minFreeBytes + requiredBytes)
        {
            throw new AppException(ErrorCode.DiskFull,
                $"Only {drive.AvailableFreeSpace / 1024 / 1024} MB free on {drive.Name}. At least {(_minFreeBytes + requiredBytes) / 1024 / 1024} MB is required; free some space or change the storage path.");
        }
    }

    private static string NormalizeExtension(string extension)
    {
        extension = extension.Trim().ToLowerInvariant();
        if (!extension.StartsWith('.')) extension = "." + extension;
        return extension.Length > 10 || extension.Any(c => !char.IsLetterOrDigit(c) && c != '.') ? ".bin" : extension;
    }

    private static bool IsDiskFull(IOException ex) =>
        ex.HResult is unchecked((int)0x80070070) or unchecked((int)0x80070027) || ex.Message.Contains("No space left", StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }
}
