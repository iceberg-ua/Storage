using Storage.App.Services;
using Storage.App.Views;

namespace Storage.App;

public partial class AppShell : Shell
{
	private readonly LocationContext _locationContext;

	public AppShell(LocationContext locationContext)
	{
		InitializeComponent();

		_locationContext = locationContext;

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
	}

	// Android's Back button, on the Locations or Items tab itself: does what the
	// title's back arrow does — up one level, onto the Locations tab — and only leaves
	// the app from the top. On a pushed page (detail, edit, camera) it pops that page
	// as usual. iOS has no Back button; the arrow covers it there.
	protected override bool OnBackButtonPressed()
	{
		if (CurrentPage is LocationsPage or ItemsPage && _locationContext.IsInside)
		{
			_locationContext.BackCommand.Execute(null);
			return true;
		}

		return base.OnBackButtonPressed();
	}
}
