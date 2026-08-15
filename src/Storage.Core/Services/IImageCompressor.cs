namespace Storage.Core.Services;

// The one part of photo handling that has to be done per-platform: decoding an
// arbitrary camera/gallery image and re-encoding it as JPEG. Everything else
// (file naming, the quality ladder, cleanup) lives in PhotoService so it can be
// tested without a device.
public interface IImageCompressor
{
    /// <summary>
    /// Downsizes <paramref name="source"/> so its longest edge is at most
    /// <paramref name="maxEdgePixels"/> and encodes the result as JPEG.
    /// </summary>
    /// <returns>A seekable stream positioned at 0, owned by the caller.</returns>
    Task<Stream> CompressAsync(Stream source, int maxEdgePixels, float quality);
}
