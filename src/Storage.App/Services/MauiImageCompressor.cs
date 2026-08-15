using Microsoft.Maui.Graphics.Platform;
using Storage.Core.Services;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Storage.App.Services;

// Uses the image codec that ships with MAUI (Microsoft.Maui.Graphics) — on Android
// that is the platform Bitmap decoder, so there is no SkiaSharp dependency to carry.
public sealed class MauiImageCompressor : IImageCompressor
{
    public async Task<Stream> CompressAsync(Stream source, int maxEdgePixels, float quality)
    {
        IImage image = PlatformImage.FromStream(source);

        // Downsize scales the longest edge to the bound and leaves smaller images alone.
        using var downsized = image.Downsize(maxEdgePixels, disposeOriginal: true);

        var output = new MemoryStream();
        await downsized.SaveAsync(output, ImageFormat.Jpeg, quality);
        output.Position = 0;

        return output;
    }
}
