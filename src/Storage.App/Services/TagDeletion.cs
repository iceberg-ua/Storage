using Storage.Core.Repositories;

namespace Storage.App.Services;

/// <summary>
/// Asks, then deletes a tag. Shared by the Tags tab's swipe and the tag's edit page, so
/// both say what the delete takes with it the same way.
/// </summary>
public sealed class TagDeletion
{
    private readonly ITagRepository _tagRepository;

    public TagDeletion(ITagRepository tagRepository)
    {
        _tagRepository = tagRepository;
    }

    /// <summary>Returns true once the tag is gone; false if the user said no or the delete failed.</summary>
    public async Task<bool> ConfirmAndDeleteAsync(int tagId, string name, int itemCount)
    {
        // The items keep existing, but the cascade is invisible from here, so the count
        // goes in the question.
        var usage = itemCount switch
        {
            0 => "It is not on any items.",
            1 => "It is on 1 item, and will be removed from it.",
            var n => $"It is on {n} items, and will be removed from them."
        };

        if (!await Shell.Current.DisplayAlertAsync("Delete tag", $"Delete '{name}'? {usage}", "Delete", "Cancel"))
            return false;

        try
        {
            await _tagRepository.DeleteAsync(tagId);
            return true;
        }
        catch (Exception ex)
        {
            // From a command, where an escaping exception would end the app.
            await Shell.Current.DisplayAlertAsync("Couldn't delete the tag", $"Try again.\n\n{ex.Message}".TrimEnd(), "OK");
            return false;
        }
    }
}
