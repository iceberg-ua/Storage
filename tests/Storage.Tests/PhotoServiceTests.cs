using Microsoft.Extensions.Logging;
using Storage.Core.Services;

namespace Storage.Tests;

// PhotoService owns the naming, the size ceiling and the filesystem housekeeping;
// the actual pixel work is the platform's job and is stubbed here. The stub reports
// the bound it was asked for and produces payloads sized off the quality, which is
// what makes the step-down ladder observable without a device.
public class PhotoServiceTests : IDisposable
{
    private readonly string _baseDirectory;
    private readonly string _photosDirectory;
    private readonly StubCompressor _compressor = new();
    private readonly StubGalleryPicker _picker = new();
    private readonly RecordingLogger _logger = new();
    private readonly PhotoService _service;

    public PhotoServiceTests()
    {
        _baseDirectory = Path.Combine(Path.GetTempPath(), "storage-photo-tests", Guid.NewGuid().ToString("N"));
        _photosDirectory = Path.Combine(_baseDirectory, "photos");
        Directory.CreateDirectory(_baseDirectory);

        _service = new PhotoService(_baseDirectory, _compressor, _picker, _logger);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
            Directory.Delete(_baseDirectory, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SaveCreatesThePhotoDirectoryOnFirstWrite()
    {
        Assert.False(Directory.Exists(_photosDirectory));

        var fileName = await _service.SaveAsync(SourceImage());

        Assert.True(File.Exists(Path.Combine(_photosDirectory, fileName)));
    }

    [Fact]
    public async Task SaveReturnsABareUniqueJpegFileName()
    {
        var first = await _service.SaveAsync(SourceImage());
        var second = await _service.SaveAsync(SourceImage());

        Assert.NotEqual(first, second);

        foreach (var fileName in new[] { first, second })
        {
            // Item.PhotoPath must stay a filename — a stored path would break the
            // moment the OS moves the app's data directory.
            Assert.DoesNotContain(Path.DirectorySeparatorChar, fileName);
            Assert.DoesNotContain(Path.AltDirectorySeparatorChar, fileName);
            Assert.EndsWith(".jpg", fileName);
        }

        Assert.Equal(2, Directory.GetFiles(_photosDirectory).Length);
    }

    [Fact]
    public async Task SaveRequestsTheDocumentedPixelBound()
    {
        await _service.SaveAsync(SourceImage());

        Assert.All(_compressor.Calls, call => Assert.Equal(PhotoService.MaxEdgePixels, call.MaxEdgePixels));
    }

    [Fact]
    public async Task SaveKeepsTheFirstEncodeWhenItIsAlreadyUnderTheCeiling()
    {
        var fileName = await _service.SaveAsync(SourceImage());

        var call = Assert.Single(_compressor.Calls);
        Assert.Equal(0.75f, call.Quality);
        Assert.True(new FileInfo(Path.Combine(_photosDirectory, fileName)).Length <= PhotoService.MaxFileBytes);
    }

    [Fact]
    public async Task SaveStepsQualityDownUntilTheFileFitsUnderOneMegabyte()
    {
        // Only the third rung of the ladder gets under the ceiling.
        _compressor.SizeFor = quality => quality > 0.6f ? 4_000_000 : 300_000;

        var fileName = await _service.SaveAsync(SourceImage());

        Assert.Equal(new[] { 0.75f, 0.65f, 0.55f }, _compressor.Calls.Select(c => c.Quality).ToArray());

        var written = new FileInfo(Path.Combine(_photosDirectory, fileName)).Length;
        Assert.True(written <= PhotoService.MaxFileBytes, $"Stored photo was {written} bytes");
    }

    [Fact]
    public async Task SaveReadsAForwardOnlySourceStream()
    {
        // The camera hands over a stream that can only be read once, so the ladder
        // has to work off a buffered copy rather than rewinding the original.
        _compressor.SizeFor = quality => quality > 0.6f ? 4_000_000 : 300_000;

        var fileName = await _service.SaveAsync(new ForwardOnlyStream(new byte[512]));

        Assert.Equal(3, _compressor.Calls.Count);
        Assert.True(File.Exists(Path.Combine(_photosDirectory, fileName)));
    }

    [Fact]
    public async Task SaveKeepsTheSmallestEncodeWhenNoRungGetsUnderTheCeiling()
    {
        // Losing the photo the user just took is worse than storing an oversized one,
        // so an exhausted ladder still has to produce a file.
        _compressor.SizeFor = quality => quality switch
        {
            > 0.7f => 5_000_000,
            > 0.6f => 4_000_000,
            > 0.5f => 3_000_000,
            _ => 3_500_000, // lower quality, larger output — the ladder must not fall for it
        };

        var fileName = await _service.SaveAsync(SourceImage());

        Assert.Equal(4, _compressor.Calls.Count);

        var written = new FileInfo(Path.Combine(_photosDirectory, fileName)).Length;
        Assert.Equal(3_000_000, written);

        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("3000000", warning.Message);
    }

    [Fact]
    public void GetFullPathResolvesUnderThePhotoDirectoryAndIsNullSafe()
    {
        Assert.Null(_service.GetFullPath(null));
        Assert.Null(_service.GetFullPath(string.Empty));
        Assert.Null(_service.GetFullPath("   "));

        Assert.Equal(Path.Combine(_photosDirectory, "abc.jpg"), _service.GetFullPath("abc.jpg"));
    }

    [Fact]
    public async Task DeleteIsIdempotentAndNullSafe()
    {
        var fileName = await _service.SaveAsync(SourceImage());

        await _service.DeleteAsync(fileName);
        Assert.False(File.Exists(Path.Combine(_photosDirectory, fileName)));

        // Deleting the same photo again, an unknown one, or nothing at all is a no-op.
        await _service.DeleteAsync(fileName);
        await _service.DeleteAsync("never-existed.jpg");
        await _service.DeleteAsync(null);
    }

    [Fact]
    public async Task CleanupOrphansRemovesOnlyUnreferencedFiles()
    {
        var referenced = await _service.SaveAsync(SourceImage());
        var orphan = await _service.SaveAsync(SourceImage());

        var removed = await _service.CleanupOrphansAsync([referenced]);

        Assert.Equal(1, removed);
        Assert.True(File.Exists(Path.Combine(_photosDirectory, referenced)));
        Assert.False(File.Exists(Path.Combine(_photosDirectory, orphan)));
    }

    [Fact]
    public async Task CleanupOrphansToleratesAMissingPhotoDirectory()
    {
        Assert.False(Directory.Exists(_photosDirectory));

        Assert.Equal(0, await _service.CleanupOrphansAsync(["something.jpg"]));
    }

    [Fact]
    public async Task PickFromGalleryStoresThePickedImage()
    {
        _picker.NextImage = () => SourceImage();

        var fileName = await _service.PickFromGalleryAsync();

        Assert.NotNull(fileName);
        Assert.True(File.Exists(Path.Combine(_photosDirectory, fileName!)));
    }

    [Fact]
    public async Task PickFromGalleryReturnsNullWhenCancelled()
    {
        _picker.NextImage = () => null;

        Assert.Null(await _service.PickFromGalleryAsync());
        Assert.False(Directory.Exists(_photosDirectory));
    }

    private static MemoryStream SourceImage() => new(new byte[1024]);

    private sealed record CompressorCall(int MaxEdgePixels, float Quality);

    private sealed class StubCompressor : IImageCompressor
    {
        public List<CompressorCall> Calls { get; } = [];

        // Defaults to the realistic case: the first encode already fits.
        public Func<float, int> SizeFor { get; set; } = _ => 200_000;

        public Task<Stream> CompressAsync(Stream source, int maxEdgePixels, float quality)
        {
            // A source the service failed to rewind would read as empty here.
            using var probe = new MemoryStream();
            source.CopyTo(probe);
            Assert.NotEqual(0, probe.Length);

            Calls.Add(new CompressorCall(maxEdgePixels, quality));

            return Task.FromResult<Stream>(new MemoryStream(new byte[SizeFor(quality)]));
        }
    }

    private sealed class StubGalleryPicker : IGalleryPicker
    {
        public Func<Stream?> NextImage { get; set; } = () => null;

        public Task<Stream?> PickAsync() => Task.FromResult(NextImage());
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    // Enough of ILogger to assert that the service said something, and at what level.
    private sealed class RecordingLogger : ILogger<PhotoService>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    // Stands in for the camera's capture stream: readable once, not seekable.
    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
