using Storage.App.Views;

namespace Storage.App;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Register routes for navigation.
		// Everything new is made on "create", pushed with a "kind" and an optional
		// "locationId". The two below are the edit forms, pushed with the id to edit.
		Routing.RegisterRoute("create", typeof(CreatePage));
		Routing.RegisterRoute("edititem", typeof(AddItemPage));
		Routing.RegisterRoute("editlocation", typeof(AddLocationPage));

		// Pushed with a "locationId": one location's page. Opening a location inside it
		// pushes another, so Back walks up the tree one level at a time.
		Routing.RegisterRoute("place", typeof(PlacePage));

		// Pushed with an "itemId". Tapping an item in a list opens this read-only view;
		// the edit form is reached from here, so "edititem" is never the tap target.
		Routing.RegisterRoute("itemdetail", typeof(ItemDetailPage));

		// Pushed with an optional "tagId" to edit an existing tag instead of adding one.
		Routing.RegisterRoute("edittag", typeof(EditTagPage));

		// The Find page pushed over a location's page or the Locations tab, with the place
		// or the Places tab already chosen; the Find tab is the same page at the root.
		Routing.RegisterRoute("search", typeof(FindPage));

		// Modal. Opened through PlacePicker, which hands the choice back to its caller.
		Routing.RegisterRoute("pickplace", typeof(PickPlacePage));

		// The same camera page, opened first for a new item: "snap", with an optional
		// "locationId". It replaces itself with the create page once the photos are taken.
		Routing.RegisterRoute("snap", typeof(CameraPage));

		// Pops back with a "photoFileName" for the item form to pick up, on the item
		// edit page or the create page.
		Routing.RegisterRoute("camera", typeof(CameraPage));
	}
}
