using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Storage.Core.Models;
using Storage.Core.Repositories;

namespace Storage.App.ViewModels;

/// <summary>One swatch in the palette grid. Tightly coupled to the page that shows it.</summary>
public partial class PaletteSwatch : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public required string Color { get; init; }
}

// Backs both "add" and "edit" for a tag. Shell passes an optional "tagId" query
// parameter to edit an existing tag.
public partial class EditTagViewModel : ObservableObject, IQueryAttributable
{
    private readonly ITagRepository _tagRepository;

    private int _tagId;
    private bool _isLoaded;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PaletteSwatch> _swatches = [];

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private string _pageTitle = "Add Tag";

    [ObservableProperty]
    private string _selectedColor = TagPalette.Default;

    // The custom section stays closed until asked for, so the common case is still
    // "tap one of ten swatches" rather than "operate three sliders".
    [ObservableProperty]
    private bool _isCustomColorVisible;

    [ObservableProperty]
    private double _red;

    [ObservableProperty]
    private double _green;

    [ObservableProperty]
    private double _blue;

    // Guards the sliders <-> SelectedColor round trip from feeding back on itself,
    // the same way NumericStepper guards its value/text pair.
    private bool _syncingColor;

    public EditTagViewModel(ITagRepository tagRepository)
    {
        _tagRepository = tagRepository;
    }

    // Shell calls this on the page's BindingContext before the page appears.
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("tagId", out var tagId) &&
            int.TryParse(Convert.ToString(tagId), out var parsedTagId))
        {
            _tagId = parsedTagId;
        }

        IsEditMode = _tagId != 0;
        PageTitle = IsEditMode ? "Edit Tag" : "Add Tag";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        // OnAppearing fires again when a pushed page is popped — don't discard edits in progress.
        if (_isLoaded)
            return;

        _isLoaded = true;

        if (IsEditMode)
        {
            var tag = await _tagRepository.GetByIdAsync(_tagId);
            if (tag is null)
            {
                await Shell.Current.DisplayAlertAsync("Error", "This tag no longer exists", "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }

            Name = tag.Name;
            SelectedColor = tag.Color;
        }
        else
        {
            // Start a new tag on the next colour around, matching what typing a tag
            // straight onto an item would have given it.
            var existing = await _tagRepository.GetAllAsync();
            SelectedColor = TagPalette.ForIndex(existing.Count());
        }

        Swatches = new ObservableCollection<PaletteSwatch>(
            TagPalette.Colors.Select(c => new PaletteSwatch { Color = c }));

        MarkSelectedSwatch();
        SyncSlidersFromSelectedColor();

        // A tag already on a colour of its own opens with the custom section showing,
        // so the current value is visible rather than hidden behind a button.
        IsCustomColorVisible = !IsPaletteColor(SelectedColor);
    }

    [RelayCommand]
    private void SelectColor(PaletteSwatch swatch)
    {
        SelectedColor = swatch.Color;
        MarkSelectedSwatch();
        SyncSlidersFromSelectedColor();
    }

    [RelayCommand]
    private void ToggleCustomColor() => IsCustomColorVisible = !IsCustomColorVisible;

    partial void OnRedChanged(double value) => UpdateColorFromSliders();

    partial void OnGreenChanged(double value) => UpdateColorFromSliders();

    partial void OnBlueChanged(double value) => UpdateColorFromSliders();

    private void UpdateColorFromSliders()
    {
        if (_syncingColor)
            return;

        SelectedColor = $"#{(int)Red:X2}{(int)Green:X2}{(int)Blue:X2}";

        // Dragging off a palette colour drops the ring; landing back on one restores it.
        MarkSelectedSwatch();
    }

    private void SyncSlidersFromSelectedColor()
    {
        var color = ParseOrDefault(SelectedColor);

        _syncingColor = true;
        Red = Math.Round(color.Red * 255);
        Green = Math.Round(color.Green * 255);
        Blue = Math.Round(color.Blue * 255);
        _syncingColor = false;
    }

    private void MarkSelectedSwatch()
    {
        foreach (var swatch in Swatches)
            swatch.IsSelected = string.Equals(swatch.Color, SelectedColor, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPaletteColor(string hex) =>
        TagPalette.Colors.Any(c => string.Equals(c, hex, StringComparison.OrdinalIgnoreCase));

    private static Color ParseOrDefault(string hex)
    {
        try
        {
            return Color.FromArgb(hex);
        }
        catch (Exception)
        {
            return Color.FromArgb(TagPalette.Default);
        }
    }

    [RelayCommand]
    private async Task SaveTagAsync()
    {
        var name = Name.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            await Shell.Current.DisplayAlertAsync("Error", "Please enter a tag name", "OK");
            return;
        }

        // Checked here so a clash reads as a message rather than surfacing from the
        // unique index as a SqliteException. Editing keeps its own name.
        if (await _tagRepository.NameExistsAsync(name, IsEditMode ? _tagId : null))
        {
            await Shell.Current.DisplayAlertAsync(
                "Name already used",
                $"A tag called '{name}' already exists. Tag names are matched without regard to case.",
                "OK");
            return;
        }

        if (IsEditMode)
        {
            var tag = await _tagRepository.GetByIdAsync(_tagId);
            if (tag is null)
            {
                await Shell.Current.DisplayAlertAsync("Error", "This tag no longer exists", "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }

            tag.Name = name;
            tag.Color = SelectedColor;

            await _tagRepository.UpdateAsync(tag);
        }
        else
        {
            await _tagRepository.AddAsync(name, SelectedColor);
        }

        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..");
    }
}
