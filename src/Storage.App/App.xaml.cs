using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Storage.Core.Data;

namespace Storage.App;

public partial class App : Application
{
	public App(IServiceProvider serviceProvider)
	{
		InitializeComponent();

		// Initialize database
		InitializeDatabase(serviceProvider);
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
}