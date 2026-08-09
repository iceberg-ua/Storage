using Storage.App.ViewModels;

namespace Storage.App.Views;

/// <summary>
/// The location-scoped view of <see cref="ItemsPage"/>, pushed with a "locationId".
/// It exists purely to give that route its own page type: Shell resolves a registered
/// route by page type, and ItemsPage is already the ContentTemplate of the Items tab,
/// so reusing it made Shell switch tabs instead of pushing. Behaviour is inherited —
/// only the type differs.
/// </summary>
public class LocationItemsPage : ItemsPage
{
    public LocationItemsPage(ItemsViewModel viewModel) : base(viewModel)
    {
    }
}
