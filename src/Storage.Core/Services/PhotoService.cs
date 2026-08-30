using Microsoft.Extensions.Logging;

namespace Storage.Core.Services;

/// <summary>
/// Stores item photos as compressed JPEGs under <c>{baseDirectory}/photos/</c>.
/// The base directory is injected rather than read from MAUI's FileSystem so the
/// service can be pointed at a temp folder in tests.
/// </summary>
public sealed class PhotoService : IPhotoService
{
    /// <summary>Longest edge of a stored photo. Phone screens are ~1080px wide;
    /// 1440 leaves headroom for detail without storing pixels nobody will see.</summary>
    public const int MaxEdgePixels = 1440;

    /// <summary>Target ceiling. Measured on a Pixel 8 over six real captures, quality
    /// 0.75 lands at 81–151 KB — worst case under 15% of this ceiling, so every real
    /// photo so far has been satisfied by the ladder's first rung. The lower rungs
    /// exist only to stop a pathological input.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    /// <summary>
    /// How recently written a file has to be for the sweep to leave it alone. A photo
    /// staged in the edit form is not referenced by any item yet, so to the sweep it is
    /// indistinguishable from an orphan — and the sweep runs unawaited at launch, which
    /// can overlap a capture. A genuine orphan is left behind by an earlier session and
    /// is never this new, so skipping recent files costs nothing and stops the sweep
    /// deleting a photo out from under the user.
    /// </summary>
    public static readonly TimeSpan OrphanGracePeriod = TimeSpan.FromMinutes(5);

    private static readonly float[] QualityLadder = [0.75f, 0.65f, 0.55f, 0.45f];

    private readonly string _photosDirectory;
    private readonly IImageCompressor _compressor;
    private readonly IGalleryPicker _galleryPicker;
    private readonly ILogger<PhotoService> _logger;

    public PhotoService(
        string baseDirectory,
        IImageCompressor compressor,
        IGalleryPicker galleryPicker,
        ILogger<PhotoService> logger)
    {
        _photosDirectory = Path.Combine(baseDirectory, "photos");
        _compressor = compressor;
        _galleryPicker = galleryPicker;
        _logger = logger;
    }

    public async Task<string> SaveAsync(Stream imageStream)
    {
        // Buffer first: a camera stream is forward-only, and both the orientation
        // read and the quality ladder need to go over the source more than once.
        using var source = new MemoryStream();
        await imageStream.CopyToAsync(source);

        var (compressed, _) = await CompressAsync(source);

        try
        {
            Directory.CreateDirectory(_photosDirectory);

            var fileName = $"{Guid.NewGuid():N}.jpg";
            using (var file = File.Create(Path.Combine(_photosDirectory, fileName)))
            {
                await compressed.CopyToAsync(file);
            }

            return fileName;
        }
        finally
        {
            compressed.Dispose();
        }
    }

    public async Task<string?> PickFromGalleryAsync()
    {
        using var picked = await _galleryPicker.PickAsync();
        return picked is null ? null : await SaveAsync(picked);
    }

    public string? GetFullPath(string? fileName) =>
        string.IsNullOrWhiteSpace(fileName) ? null : Path.Combine(_photosDirectory, fileName);

    public Task DeleteAsync(string? fileName)
    {
        if (GetFullPath(fileName) is string path)
            TryDelete(path);

        return Task.CompletedTask;
    }

    public async Task DeleteAllAsync(IEnumerable<string> fileNames)
    {
        foreach (var fileName in fileNames)
            await DeleteAsync(fileName);
    }

    public Task<int> CleanupOrphansAsync(IEnumerable<string> knownFileNames)
    {
        if (!Directory.Exists(_photosDirectory))
            return Task.FromResult(0);

        var known = new HashSet<string>(knownFileNames, StringComparer.OrdinalIgnoreCase);
        var cutoff = DateTime.UtcNow - OrphanGracePeriod;
        var removed = 0;

        foreach (var path in Directory.EnumerateFiles(_photosDirectory))
        {
            if (known.Contains(Path.GetFileName(path)))
                continue;

            // Too new to be an orphan — most likely a capture the user is still holding.
            if (File.GetLastWriteTimeUtc(path) > cutoff)
                continue;

            if (TryDelete(path))
                removed++;
        }

        return Task.FromResult(removed);
    }

    // Walks the quality ladder and keeps the smallest encode. Normally the first rung
    // already fits and the loop stops there. If even the lowest rung overshoots the
    // ceiling we still store the smallest candidate: an oversized photo is a far better
    // outcome than throwing away the one the user just took.
    private async Task<(Stream Stream, float Quality)> CompressAsync(MemoryStream source)
    {
        Stream? best = null;
        var bestQuality = 0f;

        foreach (var quality in QualityLadder)
        {
            source.Position = 0;
            var candidate = await _compressor.CompressAsync(source, MaxEdgePixels, quality);

            if (best is null || candidate.Length < best.Length)
            {
                best?.Dispose();
                best = candidate;
                bestQuality = quality;
            }
            else
            {
                // A lower quality that somehow encoded larger is no use to us.
                candidate.Dispose();
            }

            if (best.Length <= MaxFileBytes)
                break;
        }

        if (best!.Length > MaxFileBytes)
        {
            _logger.LogWarning(
                "Photo is still {Bytes} bytes after the lowest quality rung ({Quality}), over the {Ceiling} byte ceiling. Storing it anyway.",
                best.Length, bestQuality, MaxFileBytes);
        }

        best.Position = 0;
        return (best, bestQuality);
    }

    // A photo we cannot delete is not worth failing a save or a startup over.
    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
