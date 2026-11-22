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
            // ItemPhoto.FileName must stay a filename — a stored path would break
            // the moment the OS moves the app's data directory.
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

        Assert.All(_compressor.PhotoCalls, call => Assert.Equal(PhotoService.MaxEdgePixels, call.MaxEdgePixels));
    }

    [Fact]
    public async Task SaveKeepsTheFirstEncodeWhenItIsAlreadyUnderTheCeiling()
    {
        var fileName = await _service.SaveAsync(SourceImage());

        var call = Assert.Single(_compressor.PhotoCalls);
        Assert.Equal(0.75f, call.Quality);
        Assert.True(new FileInfo(Path.Combine(_photosDirectory, fileName)).Length <= PhotoService.MaxFileBytes);
    }

    [Fact]
    public async Task SaveStepsQualityDownUntilTheFileFitsUnderOneMegabyte()
    {
        // Only the third rung of the ladder gets under the ceiling.
        _compressor.SizeFor = quality => quality > 0.6f ? 4_000_000 : 300_000;

        var fileName = await _service.SaveAsync(SourceImage());

        Assert.Equal(new[] { 0.75f, 0.65f, 0.55f }, _compressor.PhotoCalls.Select(c => c.Quality).ToArray());

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

        Assert.Equal(3, _compressor.PhotoCalls.Count);
        Assert.True(File.Exists(Path.Combine(_photosDirectory, fileName)));
        Assert.True(File.Exists(ThumbnailOf(fileName)));
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

        Assert.Equal(4, _compressor.PhotoCalls.Count);

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
    public async Task DeleteAllRemovesEveryNamedPhoto()
    {
        var first = await _service.SaveAsync(SourceImage());
        var second = await _service.SaveAsync(SourceImage());
        var kept = await _service.SaveAsync(SourceImage());

        await _service.DeleteAllAsync([first, second]);

        Assert.False(File.Exists(Path.Combine(_photosDirectory, first)));
        Assert.False(File.Exists(Path.Combine(_photosDirectory, second)));
        Assert.True(File.Exists(Path.Combine(_photosDirectory, kept)));
    }

    [Fact]
    public async Task DeleteAllSkipsWhatIsAlreadyGoneAndKeepsGoing()
    {
        var fileName = await _service.SaveAsync(SourceImage());

        // An edit that drops several photos can name one that is already gone; that
        // must not stop the rest of the set being cleaned up.
        await _service.DeleteAllAsync(["never-existed.jpg", "   ", fileName]);

        Assert.False(File.Exists(Path.Combine(_photosDirectory, fileName)));
        Assert.Empty(Directory.GetFiles(_photosDirectory));
    }

    [Fact]
    public async Task CleanupOrphansRemovesOnlyUnreferencedFiles()
    {
        var referenced = await _service.SaveAsync(SourceImage());
        var orphan = await _service.SaveAsync(SourceImage());
        Age(orphan, PhotoService.OrphanGracePeriod + TimeSpan.FromMinutes(1));

        var removed = await _service.CleanupOrphansAsync([referenced]);

        Assert.Equal(1, removed);
        Assert.True(File.Exists(Path.Combine(_photosDirectory, referenced)));
        Assert.False(File.Exists(Path.Combine(_photosDirectory, orphan)));
    }

    [Fact]
    public async Task CleanupOrphansSparesAFileWrittenMomentsAgo()
    {
        // A photo staged in the edit form is in no item yet, so it looks exactly like an
        // orphan. The sweep runs at launch and must not delete it out from under the user.
        var justCaptured = await _service.SaveAsync(SourceImage());

        var removed = await _service.CleanupOrphansAsync([]);

        Assert.Equal(0, removed);
        Assert.True(File.Exists(Path.Combine(_photosDirectory, justCaptured)));
    }

    [Fact]
    public async Task CleanupOrphansRemovesAnUnreferencedFileOnceItIsPastTheGracePeriod()
    {
        var orphan = await _service.SaveAsync(SourceImage());
        Age(orphan, PhotoService.OrphanGracePeriod + TimeSpan.FromSeconds(1));

        var removed = await _service.CleanupOrphansAsync([]);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(Path.Combine(_photosDirectory, orphan)));
    }

    [Fact]
    public async Task CleanupOrphansToleratesAMissingPhotoDirectory()
    {
        Assert.False(Directory.Exists(_photosDirectory));

        Assert.Equal(0, await _service.CleanupOrphansAsync(["something.jpg"]));
    }

    // --- Thumbnails ----------------------------------------------------------

    [Fact]
    public async Task SaveWritesASmallThumbnailBesideThePhoto()
    {
        var fileName = await _service.SaveAsync(SourceImage());

        var call = Assert.Single(_compressor.ThumbnailCalls);
        Assert.Equal(PhotoService.ThumbnailQuality, call.Quality);
        Assert.True(File.Exists(ThumbnailOf(fileName)));
        Assert.Equal(ThumbnailOf(fileName), _service.GetThumbnailPath(fileName));
    }

    [Fact]
    public async Task SaveKeepsThePhotoWhenTheThumbnailCannotBeMade()
    {
        // The thumbnail is a convenience; losing the photo over it is not an option.
        _compressor.FailThumbnails = true;

        var fileName = await _service.SaveAsync(SourceImage());

        Assert.True(File.Exists(Path.Combine(_photosDirectory, fileName)));
        Assert.False(File.Exists(ThumbnailOf(fileName)));
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task GetThumbnailPathFallsBackToThePhotoAndIsNullSafe()
    {
        _compressor.FailThumbnails = true;
        var fileName = await _service.SaveAsync(SourceImage());

        // A photo stored before thumbnails existed still shows, full size.
        Assert.Equal(Path.Combine(_photosDirectory, fileName), _service.GetThumbnailPath(fileName));
        Assert.Null(_service.GetThumbnailPath(null));
        Assert.Null(_service.GetThumbnailPath("  "));
    }

    [Fact]
    public async Task CreateMissingThumbnailsFillsInOnlyWhatIsMissing()
    {
        _compressor.FailThumbnails = true;
        var withoutThumbnail = await _service.SaveAsync(SourceImage());
        _compressor.FailThumbnails = false;
        var withThumbnail = await _service.SaveAsync(SourceImage());
        _compressor.Calls.Clear();

        var created = await _service.CreateMissingThumbnailsAsync([withoutThumbnail, withThumbnail, "gone.jpg", ""]);

        Assert.Equal(1, created);
        Assert.Single(_compressor.ThumbnailCalls);
        Assert.True(File.Exists(ThumbnailOf(withoutThumbnail)));
        Assert.Equal(0, await _service.CreateMissingThumbnailsAsync([withoutThumbnail, withThumbnail]));
    }

    [Fact]
    public async Task DeleteRemovesTheThumbnailToo()
    {
        var fileName = await _service.SaveAsync(SourceImage());

        await _service.DeleteAsync(fileName);

        Assert.False(File.Exists(ThumbnailOf(fileName)));
    }

    [Fact]
    public async Task CleanupOrphansRemovesAnOrphansThumbnailAndStrayThumbnails()
    {
        var referenced = await _service.SaveAsync(SourceImage());
        var orphan = await _service.SaveAsync(SourceImage());
        Age(orphan, PhotoService.OrphanGracePeriod + TimeSpan.FromMinutes(1));

        // Left behind by a delete that removed the photo but not its thumbnail.
        var stray = Path.Combine(_photosDirectory, "thumbs", "stray.jpg");
        await File.WriteAllBytesAsync(stray, [1]);

        var removed = await _service.CleanupOrphansAsync([referenced]);

        Assert.Equal(1, removed);
        Assert.True(File.Exists(ThumbnailOf(referenced)));
        Assert.False(File.Exists(ThumbnailOf(orphan)));
        Assert.False(File.Exists(stray));
    }

    [Fact]
    public async Task AThumbnailThatFailsHalfwayLeavesNothingBehindAndIsMadeLater()
    {
        // A partial file under the final name would be shown as a broken tile, and the
        // backfill would skip it for good as already made.
        _compressor.BreakThumbnailStreams = true;
        var fileName = await _service.SaveAsync(SourceImage());

        Assert.False(File.Exists(ThumbnailOf(fileName)));
        Assert.Empty(Directory.GetFiles(Path.Combine(_photosDirectory, "thumbs")));
        Assert.Equal(Path.Combine(_photosDirectory, fileName), _service.GetThumbnailPath(fileName));

        _compressor.BreakThumbnailStreams = false;
        Assert.Equal(1, await _service.CreateMissingThumbnailsAsync([fileName]));
        Assert.True(File.Exists(ThumbnailOf(fileName)));
    }

    [Fact]
    public async Task CleanupOrphansSparesTheThumbnailOfAPhotoStillBeingEdited()
    {
        // A staged capture is in no item yet; its photo is spared by the grace period,
        // and its thumbnail has to be spared with it.
        var staged = await _service.SaveAsync(SourceImage());

        await _service.CleanupOrphansAsync([]);

        Assert.True(File.Exists(Path.Combine(_photosDirectory, staged)));
        Assert.True(File.Exists(ThumbnailOf(staged)));
    }

    [Fact]
    public async Task CleanupOrphansSparesAThumbnailBeingWrittenButNotOneLeftByACrash()
    {
        var thumbs = Path.Combine(_photosDirectory, "thumbs");
        Directory.CreateDirectory(thumbs);
        var writing = Path.Combine(thumbs, "writing.jpg.tmp");
        var crashed = Path.Combine(thumbs, "crashed.jpg.tmp");
        await File.WriteAllBytesAsync(writing, [1]);
        await File.WriteAllBytesAsync(crashed, [1]);
        File.SetLastWriteTimeUtc(crashed, DateTime.UtcNow - PhotoService.OrphanGracePeriod - TimeSpan.FromMinutes(1));

        await _service.CleanupOrphansAsync([]);

        Assert.True(File.Exists(writing));
        Assert.False(File.Exists(crashed));
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

    private string ThumbnailOf(string fileName) => Path.Combine(_photosDirectory, "thumbs", fileName);

    // Backdates a stored photo so it falls outside the sweep's grace period.
    private void Age(string fileName, TimeSpan by) =>
        File.SetLastWriteTimeUtc(Path.Combine(_photosDirectory, fileName), DateTime.UtcNow - by);

    private sealed record CompressorCall(int MaxEdgePixels, float Quality);

    private sealed class StubCompressor : IImageCompressor
    {
        public List<CompressorCall> Calls { get; } = [];

        // Told apart by the bound they ask for.
        public List<CompressorCall> PhotoCalls => Calls.Where(c => c.MaxEdgePixels == PhotoService.MaxEdgePixels).ToList();
        public List<CompressorCall> ThumbnailCalls => Calls.Where(c => c.MaxEdgePixels == PhotoService.ThumbnailEdgePixels).ToList();

        public bool FailThumbnails { get; set; }

        // Encodes, but the stream breaks partway through being written out.
        public bool BreakThumbnailStreams { get; set; }

        // Defaults to the realistic case: the first encode already fits.
        public Func<float, int> SizeFor { get; set; } = _ => 200_000;

        public Task<Stream> CompressAsync(Stream source, int maxEdgePixels, float quality)
        {
            // A source the service failed to rewind would read as empty here.
            using var probe = new MemoryStream();
            source.CopyTo(probe);
            Assert.NotEqual(0, probe.Length);

            Calls.Add(new CompressorCall(maxEdgePixels, quality));

            if (FailThumbnails && maxEdgePixels == PhotoService.ThumbnailEdgePixels)
                throw new InvalidOperationException("Could not decode");

            if (BreakThumbnailStreams && maxEdgePixels == PhotoService.ThumbnailEdgePixels)
                return Task.FromResult<Stream>(new BreaksAfterFirstReadStream(new byte[100_000]));

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

    // Gives one buffer's worth, then fails, as a disk that fills up mid-write would.
    private sealed class BreaksAfterFirstReadStream(byte[] data) : MemoryStream(data)
    {
        private bool _readOnce;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_readOnce)
                throw new IOException("Disk full");
            _readOnce = true;
            return base.Read(buffer, offset, Math.Min(count, 1024));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_readOnce)
                throw new IOException("Disk full");
            _readOnce = true;
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 1024)], cancellationToken);
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
