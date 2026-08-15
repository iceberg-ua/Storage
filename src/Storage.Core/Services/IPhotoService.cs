namespace Storage.Core.Services;

// Item.PhotoPath holds a bare filename, never a path. The directory is an
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

    /// <summary>Deletes a stored photo. No-op if the name is null or the file is gone.</summary>
    Task DeleteAsync(string? fileName);

    /// <summary>Deletes every file in the photo directory that no item references.</summary>
    Task CleanupOrphansAsync(IEnumerable<string> knownFileNames);
}
