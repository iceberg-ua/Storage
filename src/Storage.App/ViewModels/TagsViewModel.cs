using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.App.Services;
using Storage.Core.Models;
using Storage.Core.Repositories;

namespace Storage.App.ViewModels;

// Backs the "Tags" tab: every tag in the database, with the number of items it is on.
// A tag's row opens its items; the pencil beside it opens the tag itself.
public partial class TagsViewModel : ObservableObject
{
    private readonly ITagRepository _tagRepository;
    private readonly TagDeletion _deletion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTags))]
    [NotifyPropertyChangedFor(nameof(HasNoTags))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private ObservableCollection<TagRow> _tags = [];

    public bool HasTags => Tags.Count > 0;

    /// <summary>Only once loaded, so "No tags yet" does not flash by.</summary>
    public bool HasNoTags => IsLoaded && !HasTags;

    /// <summary>"7 tags · tap one to see its items", above the title. Empty until loaded.</summary>
    public string Summary => !IsLoaded ? string.Empty : Tags.Count switch
    {
        0 => "Group items across locations",
        1 => "1 tag · tap it to see its items",
        var n => $"{n} tags · tap one to see its items"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoTags))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isLoading;

    public TagsViewModel(ITagRepository tagRepository, TagDeletion deletion)
    {
        _tagRepository = tagRepository;
        _deletion = deletion;
    }

    [RelayCommand]
    private async Task LoadTagsAsync()
    {
        IsLoading = true;
        try
        {
            var tags = await _tagRepository.GetAllWithItemCountsAsync();
            Tags = Rows(tags);
            IsLoaded = true;
        }
        catch (Exception ex)
        {
            // Called from OnAppearing, an async void handler, where an exception would
            // end the app.
            await Shell.Current.DisplayAlertAsync(
                "Couldn't load your tags", $"Try again in a moment.\n\n{ex.Message}".TrimEnd(), "OK");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task NewTagAsync() => Shell.Current.GoToAsync(Routes.EditTag());

    // The tab bar's camera, as on the other tabs.
    [RelayCommand]
    private Task NewItemAsync() => Shell.Current.GoToAsync(Routes.Snap());

    [RelayCommand]
    private Task OpenTagAsync(TagRow row) => Shell.Current.GoToAsync(Routes.TaggedItems(row.Tag.Id));

    [RelayCommand]
    private Task EditTagAsync(TagRow row) => Shell.Current.GoToAsync(Routes.EditTag(row.Tag.Id));

    [RelayCommand]
    private async Task DeleteTagAsync(TagRow row)
    {
        if (!await _deletion.ConfirmAndDeleteAsync(row.Tag.Id, row.Tag.Name, row.ItemCount))
            return;

        // Rebuilt rather than removed from, so whichever row is now first loses its divider.
        Tags = Rows(Tags.Where(r => r != row).Select(r => r.Tag));
    }

    private static ObservableCollection<TagRow> Rows(IEnumerable<Tag> tags) =>
        new(tags.Select((t, i) => new TagRow(t, IsFirst: i == 0)));
}

/// <summary>
/// One row of the Tags tab: the tag, and how many items it is on, in words. The divider
/// sits above each row but the first, so none is left against the card's bottom edge.
/// </summary>
public sealed record TagRow(Tag Tag, bool IsFirst)
{
    public int ItemCount => Tag.Items.Count;

    public string CountText => ItemCount == 1 ? "1 item" : $"{ItemCount} items";

    public bool HasDivider => !IsFirst;

    public string Description => $"{Tag.Name}, {CountText}";
}
