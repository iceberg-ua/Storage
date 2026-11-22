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
				// One alias per weight. A custom font with FontAttributes="Bold" is only
				// faked by thickening the regular cut, so styles pick the weight by family.
				fonts.AddFont("PlusJakartaSans-Regular.ttf", "JakartaRegular");
				fonts.AddFont("PlusJakartaSans-SemiBold.ttf", "JakartaSemiBold");
				fonts.AddFont("PlusJakartaSans-Bold.ttf", "JakartaBold");
				fonts.AddFont("PlusJakartaSans-ExtraBold.ttf", "JakartaExtraBold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

#if ANDROID
		// Every text field sits in a FieldSurface box, which is its outline. Android draws
		// an underline of its own under an EditText; a transparent tint takes it away
		// without dropping the padding the native background gives.
		var noTint = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", (handler, _) =>
			handler.PlatformView.BackgroundTintList = noTint);
		Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("NoUnderline", (handler, _) =>
			handler.PlatformView.BackgroundTintList = noTint);
#elif IOS || MACCATALYST
		// The same on iOS, where an Entry draws a rounded border of its own instead.
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", (handler, _) =>
			handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None);
#endif

		// Configure SQLite database
		var dbPath = Path.Combine(FileSystem.AppDataDirectory, "storage.db");
		// A factory, not one shared context: each repository call takes a context of its
		// own, so pages loading at the same time don't collide on it.
		builder.Services.AddDbContextFactory<StorageDbContext>(options =>
			options
				.UseSqlite($"Data Source={dbPath}")
				// Gives every connection the unicode_lower function that search relies
				// on to ignore case outside the ASCII range.
				.AddInterceptors(new UnicodeLowerInterceptor()));

		// Register repositories — stateless now that each call opens its own context
		builder.Services.AddSingleton<IItemRepository, ItemRepository>();
		builder.Services.AddSingleton<ILocationRepository, LocationRepository>();
		builder.Services.AddSingleton<ITagRepository, TagRepository>();

		// Register services
		builder.Services.AddSingleton<IImageCompressor, MauiImageCompressor>();
		builder.Services.AddSingleton<IGalleryPicker, MauiGalleryPicker>();
		builder.Services.AddTransient<LocationDeletion>();
		// One picker request at a time, shared by whoever opens it and the page itself.
		builder.Services.AddSingleton<PlacePicker>();
		builder.Services.AddSingleton<IPhotoService>(sp => new PhotoService(
			FileSystem.AppDataDirectory,
			sp.GetRequiredService<IImageCompressor>(),
			sp.GetRequiredService<IGalleryPicker>(),
			sp.GetRequiredService<ILogger<PhotoService>>()));

		// Register ViewModels
		builder.Services.AddTransient<ViewModels.FindViewModel>();
		builder.Services.AddTransient<ViewModels.AddItemViewModel>();
		builder.Services.AddTransient<ViewModels.ItemDetailViewModel>();
		builder.Services.AddTransient<ViewModels.LocationsViewModel>();
		builder.Services.AddTransient<ViewModels.AddLocationViewModel>();
		builder.Services.AddTransient<ViewModels.TagsViewModel>();
		builder.Services.AddTransient<ViewModels.EditTagViewModel>();
		builder.Services.AddTransient<ViewModels.CreateViewModel>();
		builder.Services.AddTransient<ViewModels.PlaceViewModel>();
		builder.Services.AddTransient<ViewModels.HomeViewModel>();
		builder.Services.AddTransient<ViewModels.PickPlaceViewModel>();

		// Register Pages
		builder.Services.AddTransient<Views.FindPage>();
		builder.Services.AddTransient<Views.AddItemPage>();
		builder.Services.AddTransient<Views.ItemDetailPage>();
		builder.Services.AddTransient<Views.LocationsPage>();
		builder.Services.AddTransient<Views.AddLocationPage>();
		builder.Services.AddTransient<Views.TagsPage>();
		builder.Services.AddTransient<Views.EditTagPage>();
		builder.Services.AddTransient<Views.CameraPage>();
		builder.Services.AddTransient<Views.CreatePage>();
		builder.Services.AddTransient<Views.PlacePage>();
		builder.Services.AddTransient<Views.HomePage>();
		builder.Services.AddTransient<Views.PickPlacePage>();

		var app = builder.Build();

		// Apply any pending migrations (creates the SQLite DB on first run)
		using (var db = app.Services.GetRequiredService<IDbContextFactory<StorageDbContext>>().CreateDbContext())
		{
			db.Database.Migrate();
		}

		return app;
	}
}
