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

		// Same page as the Items tab, pushed with a "locationId" to scope it to one location
		Routing.RegisterRoute("locationitems", typeof(ItemsPage));
	}
}
