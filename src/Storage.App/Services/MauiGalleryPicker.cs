using Storage.Core.Services;

namespace Storage.App.Services;

// The built-in picker is enough here: it opens the system photo picker, which on
// Android needs no storage permission of its own. PickPhotoAsync is obsolete in
// MAUI 10, so this uses the multi-select API capped at one.
public sealed class MauiGalleryPicker : IGalleryPicker
{
    public async Task<Stream?> PickAsync()
    {
        var results = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { SelectionLimit = 1 });

        var picked = results.FirstOrDefault();
        return picked is null ? null : await picked.OpenReadAsync();
    }
}
