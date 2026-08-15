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

		// XAML can't construct a converter that needs a service, so this one is
		// added to the app resources by hand before any page is parsed.
		Resources["PhotoSourceConverter"] =
			new PhotoSourceConverter(serviceProvider.GetRequiredService<IPhotoService>());

		// Initialize database
		InitializeDatabase(serviceProvider);

		// Startup is the one moment we can be sure nothing is mid-edit, so it's
		// where files left behind by a crash get swept up.
		_ = CleanupOrphanPhotosAsync();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}

	private static void InitializeDatabase(IServiceProvider serviceProvider)
	{
		using var scope = serviceProvider.CreateScope();
		var dbContext = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
		dbContext.Database.EnsureCreated();
	}

	private async Task CleanupOrphanPhotosAsync()
	{
		var logger = _serviceProvider.GetRequiredService<ILogger<App>>();

		try
		{
			using var scope = _serviceProvider.CreateScope();
			var dbContext = scope.ServiceProvider.GetRequiredService<StorageDbContext>();

			var referenced = await dbContext.Items
				.Where(i => i.PhotoPath != null)
				.Select(i => i.PhotoPath!)
				.ToListAsync();

			var photoService = _serviceProvider.GetRequiredService<IPhotoService>();
			var removed = await photoService.CleanupOrphansAsync(referenced);

			logger.LogInformation(
				"Photo sweep finished: {Removed} orphan(s) removed, {Referenced} referenced.",
				removed, referenced.Count);
		}
		catch (Exception ex)
		{
			// Housekeeping must never take the app down on launch — but a failure that
			// nobody can see is how photo leaks go unnoticed.
			logger.LogError(ex, "Photo sweep failed. Orphaned photo files may remain on disk.");
		}
	}
}
