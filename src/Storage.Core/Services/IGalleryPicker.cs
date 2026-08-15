namespace Storage.Core.Services;

// Wraps the platform's photo picker so PhotoService stays free of MAUI types.
public interface IGalleryPicker
{
    /// <returns>The picked image, or null if the user cancelled.</returns>
    Task<Stream?> PickAsync();
}
