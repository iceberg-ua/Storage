using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Storage.App.ViewModels;

public enum CreateKind
{
    Item,
    Location
}

// The one place anything new is made: a switch between an item and a location, over the
// same forms the edit pages use. Both forms are live for the whole visit, so flipping the
// switch back does not lose what was already typed into the other one.
//
// Shell passes an optional "kind" (item or location) for which form to open on, and an
// optional "locationId" for the location the user is in, which becomes the new item's
// location or the new location's parent.
public partial class CreateViewModel : ObservableObject, IQueryAttributable
{
    private bool _isKindApplied;

    public AddItemViewModel Item { get; }

    public AddLocationViewModel Location { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsItem))]
    [NotifyPropertyChangedFor(nameof(IsLocation))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    private CreateKind _kind = CreateKind.Item;

    public bool IsItem => Kind == CreateKind.Item;

    public bool IsLocation => Kind == CreateKind.Location;

    public string PageTitle => IsItem ? "Add Item" : "Add Location";

    public CreateViewModel(AddItemViewModel item, AddLocationViewModel location)
    {
        Item = item;
        Location = location;
    }

    // Shell calls this on the page's BindingContext, and again when the camera pops back
    // with a photo, which is why it is forwarded rather than handled here.
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        // Only on the way in: a later call is the camera returning, and the user may
        // have flipped the switch since.
        if (!_isKindApplied &&
            query.TryGetValue("kind", out var kind) &&
            Enum.TryParse<CreateKind>(Convert.ToString(kind), ignoreCase: true, out var parsedKind))
        {
            Kind = parsedKind;
        }

        _isKindApplied = true;

        // The item form reads "locationId" and "photoFileName" under those names already.
        Item.ApplyQueryAttributes(query);

        // The location form calls the same location a parent, and reads "locationId" as
        // the location to edit — so it is renamed on the way through.
        if (query.TryGetValue("locationId", out var locationId))
            Location.ApplyQueryAttributes(new Dictionary<string, object> { ["parentId"] = locationId });
    }

    [RelayCommand]
    private void SetKind(CreateKind kind) => Kind = kind;

    [RelayCommand]
    private async Task LoadAsync()
    {
        await Item.LoadCommand.ExecuteAsync(null);
        await Location.LoadCommand.ExecuteAsync(null);
    }

    // Saves whichever form is showing; each one navigates back itself once it has.
    [RelayCommand]
    private Task SaveAsync() => IsItem
        ? Item.SaveItemCommand.ExecuteAsync(null)
        : Location.SaveLocationCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");

    /// <summary>
    /// Called when the page is leaving. Photos taken on the item form and never saved are
    /// cleaned up here — including when the visit ended by saving a location instead.
    /// </summary>
    public Task LeaveAsync() => Item.ReconcilePhotosOnLeaveAsync();
}
