using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Storage.App.ViewModels;

public enum CreateKind
{
    Item,
    Location
}

// The one place anything new is made: a switch between an item and a location, over the
// same forms the edit pages use. Both forms are kept for the whole visit, so flipping the
// switch back does not lose what was already typed into the other one.
//
// Shell passes an optional "kind" (item or location) for which form to open on, and an
// optional "locationId" for the location the user is in, which becomes the new item's
// location or the new location's parent.
public partial class CreateViewModel : ObservableObject, IQueryAttributable
{
    private bool _isOpened;

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

    // Shell calls this before the page first appears, and again when the camera pops
    // back with a photo. Each form is handed only what it needs, through its own
    // methods, so neither can be pushed into edit mode by a key meant for the page.
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        // Only on the way in. When the camera pops back, Shell sends the route's original
        // query again along with the photo, so a visit that opened on "kind=location"
        // would be flipped back to the location form, away from the item form the photo
        // was taken on.
        if (!_isOpened &&
            query.TryGetValue("kind", out var kind) &&
            ParseKind(Convert.ToString(kind)) is CreateKind parsedKind)
        {
            Kind = parsedKind;
        }

        _isOpened = true;

        if (query.TryGetValue("locationId", out var locationId) &&
            int.TryParse(Convert.ToString(locationId), out var parsedLocationId))
        {
            Item.PreselectLocation(parsedLocationId);
            Location.PreselectParent(parsedLocationId);
        }

        if (query.TryGetValue("photoFileName", out var photoFileName) &&
            Convert.ToString(photoFileName) is { Length: > 0 } capturedFileName)
        {
            Item.AcceptCapturedPhoto(capturedFileName);
        }
    }

    // The two names only. Enum.TryParse would also take a number, and one outside the
    // enum would leave neither form showing.
    private static CreateKind? ParseKind(string? value) => value?.ToLowerInvariant() switch
    {
        "item" => CreateKind.Item,
        "location" => CreateKind.Location,
        _ => null
    };

    // Concurrent, so the switch stays tappable while the form it just showed loads.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SetKindAsync(CreateKind kind)
    {
        Kind = kind;
        await LoadShownFormAsync();
    }

    [RelayCommand]
    private Task LoadAsync() => LoadShownFormAsync();

    // Each form loads the first time it is shown, so a visit that only adds a location
    // never reads the tags, and a form that fails to load does not stop the other one.
    // The item form also runs again on every return, which is when it picks up a photo
    // from the camera — only reachable from that form, so it is the one showing.
    private async Task LoadShownFormAsync()
    {
        try
        {
            if (IsItem)
                await Item.LoadCommand.ExecuteAsync(null);
            else
                await Location.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            // Caught here because the page calls this from OnAppearing, an async void
            // handler, where an exception would take the app down.
            await Shell.Current.DisplayAlertAsync("Couldn't open the form", ex.Message, "OK");
        }
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
