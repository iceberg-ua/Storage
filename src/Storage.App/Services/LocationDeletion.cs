using Storage.Core.Repositories;
using Storage.Core.Services;

namespace Storage.App.Services;

/// <summary>
/// Asks, then deletes a location and its photo. Shared by the Locations tab's swipe and
/// a location's own page, so both ask the same question and handle a refusal the same way.
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
        var confirm = await Shell.Current.DisplayAlertAsync(
            "Delete Location",
            $"Are you sure you want to delete '{name}'?",
            "Delete",
            "Cancel");

        if (!confirm)
            return false;

        // The file goes only after the row: a delete that fails must not leave the
        // location pointing at a photo that is no longer there.
        Core.Models.Location? deleted;
        try
        {
            deleted = await _locationRepository.DeleteAsync(locationId);
        }
        catch (Exception ex)
        {
            // Most likely it still has locations inside it, which the schema refuses.
            await Shell.Current.DisplayAlertAsync(
                "Couldn't delete location", $"Move or delete what is inside it first.\n\n{ex.Message}".TrimEnd(), "OK");
            return false;
        }

        await _photoService.DeleteAsync(deleted?.PhotoFileName);
        return true;
    }
}
