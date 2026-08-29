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

		// Pushed with an "itemId". Tapping an item in a list opens this read-only view;
		// the edit form is reached from here, so "additem" is never the tap target.
		Routing.RegisterRoute("itemdetail", typeof(ItemDetailPage));

		// Pushed with an optional "tagId" to edit an existing tag instead of adding one.
		Routing.RegisterRoute("edittag", typeof(EditTagPage));

		// Pops back with a "photoFileName" for the edit page to pick up.
		Routing.RegisterRoute("camera", typeof(CameraPage));

		// Pushed with a "locationId" to scope the items view to one location. Must be its
		// own type, not ItemsPage, or Shell resolves the route to the Items tab instead.
		Routing.RegisterRoute("locationitems", typeof(LocationItemsPage));
	}
}
