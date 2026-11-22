namespace Storage.App.Services;

/// <summary>What the place picker is asked for.</summary>
/// <param name="Title">"Move to…", "Choose a location".</param>
/// <param name="Subject">One line under the title saying what is being placed, or null.</param>
/// <param name="CurrentId">Where it is now, marked and preselected; null for nowhere.</param>
/// <param name="ConfirmFormat">The confirm button, with {0} for the chosen place: "Move to {0}".</param>
/// <param name="AllowNone">Whether choosing nothing is one of the choices.</param>
/// <param name="NoneName">The row for choosing nothing: "No location", or "Top level" for a parent.</param>
/// <param name="NoneMeta">The line under that row.</param>
/// <param name="NoneInSentence">That choice inside <paramref name="ConfirmFormat"/>: "Use no location".</param>
/// <param name="ExcludedIds">Locations that may not be chosen, left out of the list altogether.</param>
public sealed record PlacePickRequest(
    string Title,
    string? Subject,
    int? CurrentId,
    string ConfirmFormat,
    bool AllowNone,
    string NoneName = "No location",
    string NoneMeta = "Not stored anywhere yet",
    string NoneInSentence = "no location",
    IReadOnlySet<int>? ExcludedIds = null);

/// <summary>The place chosen; a null id is "no location".</summary>
public sealed record PlacePick(int? LocationId);

/// <summary>
/// One opening of the picker: its request, and the answer its caller is waiting for.
/// The page holds on to the session it was opened for, so completing it can never
/// answer a later request by mistake.
/// </summary>
public sealed class PlacePickSession
{
    private readonly TaskCompletionSource<PlacePick?> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PlacePickSession(PlacePickRequest request) => Request = request;

    public PlacePickRequest Request { get; }

    internal Task<PlacePick?> Result => _result.Task;

    /// <summary>Only the first call counts; later ones (the page going away after a confirm) do nothing.</summary>
    public void Complete(PlacePick? pick) => _result.TrySetResult(pick);
}

/// <summary>
/// Opens the place picker and hands back what was chosen, so a caller writes
/// <c>var pick = await picker.PickAsync(request)</c> instead of passing a route
/// parameter out and reading it back when its page reappears.
/// </summary>
/// <remarks>
/// A singleton, so the picker page can find the session it was opened for. A picker
/// left any other way than by confirming — the close button, Back — answers null.
/// </remarks>
public sealed class PlacePicker
{
    /// <summary>The session the picker page about to open should show; taken by that page.</summary>
    public PlacePickSession? Opening { get; private set; }

    /// <summary>Returns the place chosen, or null if the picker was closed without one.</summary>
    public async Task<PlacePick?> PickAsync(PlacePickRequest request)
    {
        // A session still waiting from before has lost its page; answer it with nothing.
        Opening?.Complete(null);

        var session = new PlacePickSession(request);
        Opening = session;

        try
        {
            await Shell.Current.GoToAsync("pickplace");
        }
        catch (Exception ex)
        {
            // Most likely another navigation was already under way. Nothing opened, so
            // there is nothing to wait for — and an exception escaping a command would
            // end the app.
            session.Complete(null);
            Opening = null;
            await Shell.Current.DisplayAlertAsync(
                "Couldn't open the location list", $"Try again in a moment.\n\n{ex.Message}".TrimEnd(), "OK");
        }

        return await session.Result;
    }

    /// <summary>Hands the opening session to the page that shows it, once.</summary>
    public PlacePickSession? TakeOpening()
    {
        var session = Opening;
        Opening = null;
        return session;
    }
}
