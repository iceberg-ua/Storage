using Storage.Core.Services;

#if ANDROID
using Android.Graphics;
using AndroidExif = AndroidX.ExifInterface.Media.ExifInterface;
#else
using Microsoft.Maui.Graphics.Platform;
using IImage = Microsoft.Maui.Graphics.IImage;
#endif

namespace Storage.App.Services;

// Uses the platform's own image codec — on Android that is Bitmap, elsewhere the one
// that ships with Microsoft.Maui.Graphics. No SkiaSharp dependency either way.
public sealed class MauiImageCompressor : IImageCompressor
{
#if ANDROID
    // Android cameras routinely report rotation as an EXIF tag instead of rotating the
    // pixels, and re-encoding drops the tag. Left alone, a photo looks right in the
    // review step (which renders the original stream) and sideways everywhere after
    // (which renders the re-encoded file), so orientation is baked in here.
    public Task<Stream> CompressAsync(Stream source, int maxEdgePixels, float quality) =>
        Task.Run<Stream>(() =>
        {
            source.Position = 0;
            int orientation;
            using (var exif = new AndroidExif(source))
            {
                orientation = exif.GetAttributeInt(AndroidExif.TagOrientation, AndroidExif.OrientationNormal);
            }

            source.Position = 0;
            var bitmap = DecodeWithinBudget(source, maxEdgePixels)
                ?? throw new InvalidOperationException("The image could not be decoded.");

            try
            {
                bitmap = Replace(bitmap, ScaleToBound(bitmap, maxEdgePixels));
                bitmap = Replace(bitmap, ApplyOrientation(bitmap, orientation));

                var output = new MemoryStream();
                bitmap.Compress(Bitmap.CompressFormat.Jpeg!, (int)Math.Round(quality * 100), output);
                output.Position = 0;

                return output;
            }
            finally
            {
                bitmap.Recycle();
                bitmap.Dispose();
            }
        });

    // Full-resolution captures are big enough to run the app out of memory when decoded
    // whole, so let the decoder halve them until they are near the bound. This gets close;
    // ScaleToBound then lands on the exact size.
    private static Bitmap? DecodeWithinBudget(Stream source, int maxEdgePixels)
    {
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeStream(source, null, bounds);

        source.Position = 0;

        var options = new BitmapFactory.Options
        {
            InSampleSize = SampleSizeFor(bounds.OutWidth, bounds.OutHeight, maxEdgePixels)
        };

        return BitmapFactory.DecodeStream(source, null, options);
    }

    private static int SampleSizeFor(int width, int height, int maxEdgePixels)
    {
        var longestEdge = Math.Max(width, height);
        var sampleSize = 1;

        // Stop before the halving that would take us under the bound — going under would
        // mean upscaling afterwards.
        while (maxEdgePixels > 0 && longestEdge / (sampleSize * 2) >= maxEdgePixels)
            sampleSize *= 2;

        return sampleSize;
    }

    private static Bitmap ScaleToBound(Bitmap bitmap, int maxEdgePixels)
    {
        var longestEdge = Math.Max(bitmap.Width, bitmap.Height);
        if (longestEdge <= maxEdgePixels)
            return bitmap;

        var scale = (double)maxEdgePixels / longestEdge;
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

        return Bitmap.CreateScaledBitmap(bitmap, width, height, filter: true)!;
    }

    private static Bitmap ApplyOrientation(Bitmap bitmap, int orientation)
    {
        var matrix = new Matrix();

        switch (orientation)
        {
            case AndroidExif.OrientationFlipHorizontal:
                matrix.SetScale(-1, 1);
                break;
            case AndroidExif.OrientationRotate180:
                matrix.SetRotate(180);
                break;
            case AndroidExif.OrientationFlipVertical:
                matrix.SetScale(1, -1);
                break;
            case AndroidExif.OrientationTranspose:
                matrix.SetRotate(90);
                matrix.PostScale(-1, 1);
                break;
            case AndroidExif.OrientationRotate90:
                matrix.SetRotate(90);
                break;
            case AndroidExif.OrientationTransverse:
                matrix.SetRotate(270);
                matrix.PostScale(-1, 1);
                break;
            case AndroidExif.OrientationRotate270:
                matrix.SetRotate(270);
                break;
            default:
                matrix.Dispose();
                return bitmap;
        }

        using (matrix)
            return Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, filter: true)!;
    }

    // The helpers return the original when there is nothing to do, so only free it when
    // it was actually replaced.
    private static Bitmap Replace(Bitmap original, Bitmap replacement)
    {
        if (!ReferenceEquals(original, replacement))
        {
            original.Recycle();
            original.Dispose();
        }

        return replacement;
    }
#else
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
#endif
}
