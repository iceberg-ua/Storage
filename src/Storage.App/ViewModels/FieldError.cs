using CommunityToolkit.Mvvm.ComponentModel;

namespace Storage.App.ViewModels;

/// <summary>
/// The message under one form field. A view model owns one per validated field; the
/// form binds <see cref="Text"/> to a FieldError label and to FocusOnError on the entry.
/// </summary>
public partial class FieldError : ObservableObject
{
    // Null hides the label, so there is no separate "has error" flag to keep in step.
    [ObservableProperty]
    private string? _text;

    public void Show(string message)
    {
        // Through null first, so refusing the same save twice still counts as a new
        // error: FocusOnError reacts to a change, and the text alone would not change.
        Text = null;
        Text = message;

        // A label changing says nothing to TalkBack or VoiceOver, unlike the alert
        // this replaced.
        SemanticScreenReader.Default.Announce(message);
    }

    public void Clear() => Text = null;
}
