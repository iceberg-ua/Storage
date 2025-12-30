using Storage.App.Views;

namespace Storage.App;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Register routes for navigation
		Routing.RegisterRoute("additem", typeof(AddItemPage));
		Routing.RegisterRoute("addlocation", typeof(AddLocationPage));
	}
}
