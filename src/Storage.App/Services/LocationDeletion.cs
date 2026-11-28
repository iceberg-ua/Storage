using Storage.Core.Models;
using Storage.Core.Repositories;
using Storage.Core.Services;

namespace Storage.App.Services;

/// <summary>
/// Asks, then deletes a location and the photos that go with it. Shared by the Locations
/// tab's swipe, a location's page, its form and Find, so all of them ask the same question.
/// A location with something inside asks what happens to it: moved up a level by default,
/// or deleted with it, which is asked a second time.
/// </summary>
public sealed class LocationDeletion
{
    private readonly ILocationRepository _locationRepository;
    private readonly IPhotoService _photoService;

    public LocationDeletion(ILocationRepository locationRepository, IPhotoService photoService)
    {
        _locationRepository = locationRepository;
        _photoService = photoService;
    }

    /// <summary>Returns true once the location is gone; false if the user said no or the delete failed.</summary>
    public async Task<bool> ConfirmAndDeleteAsync(int locationId, string name)
    {
        IReadOnlyList<string>? photos;
        try
        {
            if (await AskAsync(locationId, name) is not bool deleteContents)
                return false;

            photos = await _locationRepository.DeleteAsync(locationId, deleteContents);
        }
        catch (Exception ex)
        {
            // From a command, where an escaping exception would end the app. The delete is
            // one transaction, so a failure has changed nothing.
            await Shell.Current.DisplayAlertAsync(
                "Couldn't delete location", $"Nothing was changed. Try again.\n\n{ex.Message}".TrimEnd(), "OK");
            return false;
        }

        // The files go only after the rows: a delete that fails must not leave anything
        // pointing at a photo that is no longer there. Null means it was already gone.
        await _photoService.DeleteAllAsync(photos ?? []);
        return true;
    }

    // Null when the user backed out; otherwise whether to delete what is inside too.
    private async Task<bool?> AskAsync(int locationId, string name)
    {
        var contents = await _locationRepository.GetContentsAsync(locationId);

        // Gone already, or nothing inside: nothing to choose.
        if (contents is null || contents.IsEmpty)
        {
            var confirmed = await Shell.Current.DisplayAlertAsync(
                "Delete location", $"Delete '{name}'?", "Delete", "Cancel");
            return confirmed ? false : null;
        }

        var everything = Describe(contents.Locations, contents.Items);

        var deleteContents = await CheckboxAlert.ShowAsync(
            title: $"Delete '{name}'?",
            message: WhereContentsGo(contents),
            checkedMessage: $"Its {everything} will be deleted too, with their photos.",
            checkboxLabel: "Delete everything inside it",
            accept: "Delete",
            cancel: "Cancel");

        if (deleteContents != true)
            return deleteContents;

        var sure = await Shell.Current.DisplayAlertAsync(
            "Delete everything?",
            $"Are you sure you want to delete '{name}' and its {everything}? This can't be undone.",
            "Delete all",
            "Cancel");
        return sure ? true : null;
    }

    // Only the items directly in it move: items further down stay where they are, inside
    // a sub-location that moves with them. At the top level there is no parent to move
    // to, so sub-locations become top-level ones and the items lose their location.
    private static string WhereContentsGo(LocationContents contents)
    {
        var parent = contents.ParentName;
        var parts = new List<string>();

        if (contents.Locations > 0)
        {
            var destination = parent is null ? "the top level" : $"'{parent}'";
            parts.Add($"Its {Count(contents.Locations, "location")} will move to {destination}.");
        }

        if (contents.DirectItems > 0)
        {
            var items = contents.DirectItems == 1 ? "The item" : $"The {contents.DirectItems} items";
            parts.Add(parent is null
                ? $"{items} directly in it will have no location."
                : $"{items} directly in it will move to '{parent}'.");
        }

        return string.Join(" ", parts);
    }

    private static string Describe(int locations, int items) => (locations, items) switch
    {
        (0, _) => Count(items, "item"),
        (_, 0) => Count(locations, "location"),
        _ => $"{Count(locations, "location")} and {Count(items, "item")}"
    };

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
