using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storage.App.Converters;
using Storage.Core.Data;
using Storage.Core.Services;

namespace Storage.App;

public partial class App : Application
{
	private readonly IServiceProvider _serviceProvider;

	public App(IServiceProvider serviceProvider)
	{
		InitializeComponent();

		_serviceProvider = serviceProvider;

		// XAML can't construct a converter that needs a service, so these two are
		// added to the app resources by hand before any page is parsed: the full photo,
		// and the small copy the lists use.
		var photoService = serviceProvider.GetRequiredService<IPhotoService>();
		Resources["PhotoSourceConverter"] = new PhotoSourceConverter(photoService);
		Resources["ThumbnailSourceConverter"] = new PhotoSourceConverter(photoService, thumbnail: true);

		// The database is created and migrated in MauiProgram. Deliberately not touched
		// here: EnsureCreated builds a schema with no migrations-history table, so having
		// both paths meant a reordering could silently stop migrations applying.

		// Files left behind by a crash get swept up on launch. The sweep spares anything
		// written in the last few minutes, so a capture racing it is safe.
		_ = CleanupOrphanPhotosAsync();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}

	private async Task CleanupOrphanPhotosAsync()
	{
		var logger = _serviceProvider.GetRequiredService<ILogger<App>>();

		try
		{
			await using var dbContext = await _serviceProvider
				.GetRequiredService<IDbContextFactory<StorageDbContext>>()
				.CreateDbContextAsync();

			// Every item photo and every location photo. An owner missing from this
			// list would have its files swept away as orphans.
			var referenced = await dbContext.GetReferencedPhotoFileNamesAsync();

			var photoService = _serviceProvider.GetRequiredService<IPhotoService>();
			var removed = await photoService.CleanupOrphansAsync(referenced);

			// Photos stored before thumbnails existed get theirs now. Lists show the full
			// photo until then, so nothing waits on this.
			// On a worker thread: the first launch after the update reads and writes a
			// file per photo, which would otherwise hop back to the UI thread each time.
			var thumbnails = await Task.Run(() => photoService.CreateMissingThumbnailsAsync(referenced));

			logger.LogInformation(
				"Photo sweep finished: {Removed} orphan(s) removed, {Thumbnails} thumbnail(s) made, {Referenced} referenced.",
				removed, thumbnails, referenced.Count);
		}
		catch (Exception ex)
		{
			// Housekeeping must never take the app down on launch — but a failure that
			// nobody can see is how photo leaks go unnoticed.
			logger.LogError(ex, "Photo sweep failed. Orphaned photo files may remain on disk.");
		}
	}
}
