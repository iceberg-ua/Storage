namespace Storage.App.Services;

/// <summary>What the place picker is asked for.</summary>
/// <param name="Title">"Move to…", "Choose a location".</param>
/// <param name="Subject">One line under the title saying what is being placed, or null.</param>
/// <param name="CurrentId">Where it is now, marked and preselected; null for nowhere.</param>
/// <param name="ConfirmFormat">The confirm button, with {0} for the chosen place: "Move to {0}".</param>
/// <param name="AllowNone">Whether "No location" is one of the choices.</param>
public sealed record PlacePickRequest(
    string Title,
    string? Subject,
    int? CurrentId,
    string ConfirmFormat,
    bool AllowNone);

/// <summary>The place chosen; a null id is "no location".</summary>
public sealed record PlacePick(int? LocationId);

/// <summary>
/// Opens the place picker and hands back what was chosen, so a caller writes
/// <c>var pick = await picker.PickAsync(request)</c> instead of passing a route
/// parameter out and reading it back when its page reappears.
/// </summary>
/// <remarks>
/// A singleton: the picker page reads the request from here and completes it. A picker
/// left any other way — Back, the close button — completes with null.
/// </remarks>
public sealed class PlacePicker
{
    private TaskCompletionSource<PlacePick?>? _pending;

    /// <summary>The request the open picker is showing.</summary>
    public PlacePickRequest? Request { get; private set; }

    /// <summary>Returns the place chosen, or null if the picker was closed without one.</summary>
    public async Task<PlacePick?> PickAsync(PlacePickRequest request)
    {
        // A picker still open from before has lost its caller; close that request out.
        _pending?.TrySetResult(null);

        Request = request;
        var pending = new TaskCompletionSource<PlacePick?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;

        await Shell.Current.GoToAsync("pickplace");
        return await pending.Task;
    }

    /// <summary>Called by the picker page. Only the first call for a request counts.</summary>
    public void Complete(PlacePick? pick)
    {
        _pending?.TrySetResult(pick);
        _pending = null;
    }
}
