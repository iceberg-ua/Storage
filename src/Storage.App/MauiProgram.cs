using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Storage.Core.Data;
using Storage.Core.Repositories;

namespace Storage.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
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

		// Register ViewModels
		builder.Services.AddTransient<ViewModels.ItemsViewModel>();
		builder.Services.AddTransient<ViewModels.AddItemViewModel>();

		// Register Pages
		builder.Services.AddTransient<Views.ItemsPage>();
		builder.Services.AddTransient<Views.AddItemPage>();

		return builder.Build();
	}
}
