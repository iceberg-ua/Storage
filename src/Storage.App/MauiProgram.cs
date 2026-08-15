using CommunityToolkit.Maui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Storage.App.Services;
using Storage.Core.Data;
using Storage.Core.Repositories;
using Storage.Core.Services;

namespace Storage.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkitCamera()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// Configure SQLite database
		var dbPath = Path.Combine(FileSystem.AppDataDirectory, "storage.db");
		builder.Services.AddDbContext<StorageDbContext>(options =>
			options.UseSqlite($"Data Source={dbPath}"));

		// Register repositories
		builder.Services.AddScoped<IItemRepository, ItemRepository>();
		builder.Services.AddScoped<ILocationRepository, LocationRepository>();

		// Register services
		builder.Services.AddSingleton<IImageCompressor, MauiImageCompressor>();
		builder.Services.AddSingleton<IGalleryPicker, MauiGalleryPicker>();
		builder.Services.AddSingleton<IPhotoService>(sp => new PhotoService(
			FileSystem.AppDataDirectory,
			sp.GetRequiredService<IImageCompressor>(),
			sp.GetRequiredService<IGalleryPicker>()));

		// Register ViewModels
		builder.Services.AddTransient<ViewModels.ItemsViewModel>();
		builder.Services.AddTransient<ViewModels.AddItemViewModel>();
		builder.Services.AddTransient<ViewModels.LocationsViewModel>();
		builder.Services.AddTransient<ViewModels.AddLocationViewModel>();

		// Register Pages
		builder.Services.AddTransient<Views.ItemsPage>();
		builder.Services.AddTransient<Views.LocationItemsPage>();
		builder.Services.AddTransient<Views.AddItemPage>();
		builder.Services.AddTransient<Views.LocationsPage>();
		builder.Services.AddTransient<Views.AddLocationPage>();
		builder.Services.AddTransient<Views.CameraPage>();

		var app = builder.Build();

		// Apply any pending migrations (creates the SQLite DB on first run)
		using (var scope = app.Services.CreateScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<StorageDbContext>();
			db.Database.Migrate();
		}

		return app;
	}
}
