using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Models;
using Storage.Core.Repositories;

namespace Storage.App.ViewModels;

// Backs the "Tags" tab: every tag in the database, with the number of items it is on.
public partial class TagsViewModel : ObservableObject
{
    private readonly ITagRepository _tagRepository;

    [ObservableProperty]
    private ObservableCollection<Tag> _tags = [];

    [ObservableProperty]
    private bool _isLoading;

    public TagsViewModel(ITagRepository tagRepository)
    {
        _tagRepository = tagRepository;
    }

    [RelayCommand]
    private async Task LoadTagsAsync()
    {
        IsLoading = true;
        try
        {
            var tags = await _tagRepository.GetAllWithItemCountsAsync();
            Tags = new ObservableCollection<Tag>(tags);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToAddTagAsync()
    {
        await Shell.Current.GoToAsync("edittag");
    }

    [RelayCommand]
    private async Task EditTagAsync(Tag tag)
    {
        await Shell.Current.GoToAsync($"edittag?tagId={tag.Id}");
    }

    [RelayCommand]
    private async Task DeleteTagAsync(Tag tag)
    {
        // The cascade is invisible from this screen, so the count goes in the prompt.
        var usage = tag.Items.Count switch
        {
            0 => "It is not on any items.",
            1 => "It is on 1 item, and will be removed from it.",
            var n => $"It is on {n} items, and will be removed from them."
        };

        var confirm = await Shell.Current.DisplayAlertAsync(
            "Delete Tag",
            $"Delete '{tag.Name}'? {usage}",
            "Delete",
            "Cancel");

        if (confirm)
        {
            await _tagRepository.DeleteAsync(tag.Id);
            Tags.Remove(tag);
        }
    }
}
