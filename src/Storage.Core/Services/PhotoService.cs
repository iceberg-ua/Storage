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

    /// <summary>Hard ceiling. Quality 0.75 normally lands at 150–250 KB, so the
    /// lower rungs of the ladder exist only to stop a pathological input.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    private static readonly float[] QualityLadder = [0.75f, 0.65f, 0.55f, 0.45f];

    private readonly string _photosDirectory;
    private readonly IImageCompressor _compressor;
    private readonly IGalleryPicker _galleryPicker;

    public PhotoService(string baseDirectory, IImageCompressor compressor, IGalleryPicker galleryPicker)
    {
        _photosDirectory = Path.Combine(baseDirectory, "photos");
        _compressor = compressor;
        _galleryPicker = galleryPicker;
    }

    public async Task<string> SaveAsync(Stream imageStream)
    {
        // Buffer first: a camera stream is forward-only, and the quality ladder
        // may need to read the source more than once.
        using var source = new MemoryStream();
        await imageStream.CopyToAsync(source);

        using var compressed = await CompressAsync(source);

        Directory.CreateDirectory(_photosDirectory);

        var fileName = $"{Guid.NewGuid():N}.jpg";
        using (var file = File.Create(Path.Combine(_photosDirectory, fileName)))
        {
            await compressed.CopyToAsync(file);
        }

        return fileName;
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

    public Task CleanupOrphansAsync(IEnumerable<string> knownFileNames)
    {
        if (!Directory.Exists(_photosDirectory))
            return Task.CompletedTask;

        var known = new HashSet<string>(knownFileNames, StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(_photosDirectory))
        {
            if (!known.Contains(Path.GetFileName(path)))
                TryDelete(path);
        }

        return Task.CompletedTask;
    }

    // Steps the JPEG quality down until the encoded image fits under the ceiling,
    // keeping the last attempt if even the lowest rung overshoots.
    private async Task<Stream> CompressAsync(MemoryStream source)
    {
        Stream? result = null;

        foreach (var quality in QualityLadder)
        {
            result?.Dispose();

            source.Position = 0;
            result = await _compressor.CompressAsync(source, MaxEdgePixels, quality);

            if (result.Length <= MaxFileBytes)
                break;
        }

        result!.Position = 0;
        return result;
    }

    // A photo we cannot delete is not worth failing a save or a startup over.
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
