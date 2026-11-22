using Microsoft.Extensions.Logging;

namespace Storage.Core.Services;

/// <summary>
/// Stores item photos as compressed JPEGs under <c>{baseDirectory}/photos/</c>, each
/// with a small copy of the same name under <c>photos/thumbs/</c> for the lists.
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

    /// <summary>Longest edge of a thumbnail. The biggest list tile is about 170 × 150 dp,
    /// some 450 × 400 px on a Pixel 8; a 4:3 photo needs a short edge of 400+ to fill it
    /// without being stretched, hence 640 rather than 480.</summary>
    public const int ThumbnailEdgePixels = 640;

    /// <summary>A thumbnail is only seen small, where JPEG artefacts do not show.</summary>
    public const float ThumbnailQuality = 0.7f;

    /// <summary>
    /// How recently written a file has to be for the sweep to leave it alone. A photo
    /// staged in the edit form is not referenced by any item yet, so to the sweep it is
    /// indistinguishable from an orphan — and the sweep runs unawaited at launch, which
    /// can overlap a capture. A genuine orphan is left behind by an earlier session and
    /// is never this new, so skipping recent files costs nothing and stops the sweep
    /// deleting a photo out from under the user.
    /// </summary>
    public static readonly TimeSpan OrphanGracePeriod = TimeSpan.FromMinutes(5);

    // A thumbnail being written. Never a photo's name, since those end in .jpg.
    private const string TempSuffix = ".tmp";

    private static readonly float[] QualityLadder = [0.75f, 0.65f, 0.55f, 0.45f];

    private readonly string _photosDirectory;
    private readonly string _thumbnailsDirectory;
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

        // A folder of its own, so the orphan sweep, which looks only at the photos
        // folder's own files, never takes a thumbnail for a photo nobody owns.
        _thumbnailsDirectory = Path.Combine(_photosDirectory, "thumbs");
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

            // From the original rather than the stored copy: one generation of JPEG loss
            // fewer, and the source is in memory already.
            await TryWriteThumbnailAsync(source, fileName);

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

    public string? GetThumbnailPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        // Until the launch pass has made one, a photo saved before thumbnails existed
        // shows full size, as it always did, rather than not at all.
        var thumbnail = Path.Combine(_thumbnailsDirectory, fileName);
        return File.Exists(thumbnail) ? thumbnail : GetFullPath(fileName);
    }

    public async Task<int> CreateMissingThumbnailsAsync(IEnumerable<string> fileNames)
    {
        var created = 0;
        foreach (var fileName in fileNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var photo = Path.Combine(_photosDirectory, fileName);
            if (File.Exists(Path.Combine(_thumbnailsDirectory, fileName)) || !File.Exists(photo))
                continue;

            try
            {
                using var source = new MemoryStream();
                using (var file = File.OpenRead(photo))
                {
                    await file.CopyToAsync(source);
                }

                if (await TryWriteThumbnailAsync(source, fileName))
                    created++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Deleted, locked or unreadable between the check and the read. One photo
                // must not stop the rest; the next launch tries it again.
                _logger.LogWarning(ex, "Could not read {FileName} to make its thumbnail.", fileName);
            }
        }

        return created;
    }

    public Task DeleteAsync(string? fileName)
    {
        if (GetFullPath(fileName) is string path)
        {
            TryDelete(path);
            TryDelete(Path.Combine(_thumbnailsDirectory, fileName!));
        }

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
            {
                removed++;
                TryDelete(Path.Combine(_thumbnailsDirectory, Path.GetFileName(path)));
            }
        }

        // A thumbnail is written after its photo, so one whose photo is gone is left over
        // from a delete that only got halfway. No grace period needed.
        if (Directory.Exists(_thumbnailsDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(_thumbnailsDirectory))
            {
                // A thumbnail still being written is spared; one left by a crash is not.
                if (path.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.GetLastWriteTimeUtc(path) <= cutoff)
                        TryDelete(path);
                    continue;
                }

                if (!File.Exists(Path.Combine(_photosDirectory, Path.GetFileName(path))))
                    TryDelete(path);
            }
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

    // Best effort: a photo without a thumbnail still shows, full size, so a failure here
    // must not cost the user the photo they just took.
    private async Task<bool> TryWriteThumbnailAsync(MemoryStream source, string fileName)
    {
        var temp = Path.Combine(_thumbnailsDirectory, fileName + TempSuffix);
        try
        {
            source.Position = 0;
            using var thumbnail = await _compressor.CompressAsync(source, ThumbnailEdgePixels, ThumbnailQuality);

            // Written under a temporary name and renamed when complete. A thumbnail
            // under its final name is taken as finished: shown by the lists (and cached
            // by the image loader), and skipped by the backfill. A half-written one
            // would be a broken tile for good.
            Directory.CreateDirectory(_thumbnailsDirectory);
            using (var file = File.Create(temp))
            {
                await thumbnail.CopyToAsync(file);
            }

            File.Move(temp, Path.Combine(_thumbnailsDirectory, fileName), overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            TryDelete(temp);
            _logger.LogWarning(ex, "Could not make a thumbnail for {FileName}; lists will show the full photo.", fileName);
            return false;
        }
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
