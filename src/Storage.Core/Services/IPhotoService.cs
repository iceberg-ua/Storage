namespace Storage.Core.Services;

// ItemPhoto.FileName holds a bare filename, never a path. The directory is an
// implementation detail of this service, so a photo stays findable even if the
// app's data directory moves between installs or OS versions.
public interface IPhotoService
{
    /// <summary>Compresses and writes the image. Returns the stored filename.</summary>
    Task<string> SaveAsync(Stream imageStream);

    /// <summary>Picks from the device gallery and stores it. Null if cancelled.</summary>
    Task<string?> PickFromGalleryAsync();

    /// <summary>Resolves a stored filename to an absolute path. Null in, null out.</summary>
    string? GetFullPath(string? fileName);

    /// <summary>
    /// The small copy of a stored photo, for lists and strips; the full photo when it has
    /// none yet. Null in, null out.
    /// </summary>
    string? GetThumbnailPath(string? fileName);

    /// <summary>
    /// Makes the thumbnail for each named photo that has none, such as photos stored
    /// before thumbnails existed. Names whose photo is missing are skipped.
    /// </summary>
    /// <returns>How many thumbnails were made.</returns>
    Task<int> CreateMissingThumbnailsAsync(IEnumerable<string> fileNames);

    /// <summary>Deletes a stored photo and its thumbnail. No-op if the name is null or the file is gone.</summary>
    Task DeleteAsync(string? fileName);

    /// <summary>Deletes several stored photos — an item's whole set, or the ones an
    /// edit dropped. Names that are blank or already gone are skipped. Named apart
    /// from the single-photo overload so that passing a literal null stays unambiguous.
    /// </summary>
    Task DeleteAllAsync(IEnumerable<string> fileNames);

    /// <summary>
    /// Deletes every file in the photo directory that no item references, with its
    /// thumbnail, and any thumbnail whose photo is gone.
    /// </summary>
    /// <returns>How many files were removed.</returns>
    Task<int> CleanupOrphansAsync(IEnumerable<string> knownFileNames);
}
