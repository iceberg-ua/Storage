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

		// Pushed with a "locationId" to scope the items view to one location. Must be its
		// own type, not ItemsPage, or Shell resolves the route to the Items tab instead.
		Routing.RegisterRoute("locationitems", typeof(LocationItemsPage));
	}
}
