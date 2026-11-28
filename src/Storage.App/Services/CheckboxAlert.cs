#if ANDROID
using Android.Content;
using Android.Widget;
using Microsoft.Maui.Platform;
using AlertDialog = AndroidX.AppCompat.App.AlertDialog;
using CheckBox = Android.Widget.CheckBox;
#endif

namespace Storage.App.Services;

/// <summary>
/// A confirmation with one checkbox, which MAUI's alerts cannot hold. On Android it is the
/// same native dialog DisplayAlertAsync shows, with the box added, so it looks like every
/// other question the app asks. A see-through modal page drawn in XAML is unreliable in
/// Shell on Android (see PickPlacePage). Elsewhere it falls back to an action sheet with
/// one button per choice.
/// </summary>
public static class CheckboxAlert
{
    /// <summary>
    /// Null when the user cancelled; otherwise whether the box was ticked when they accepted.
    /// The message switches to <paramref name="checkedMessage"/> while the box is ticked,
    /// and the accept button turns the danger colour.
    /// </summary>
    public static Task<bool?> ShowAsync(
        string title, string message, string checkedMessage, string checkboxLabel, string accept, string cancel)
    {
#if ANDROID
        if (Platform.CurrentActivity is { } activity)
            return ShowNativeAsync(activity, title, message, checkedMessage, checkboxLabel, accept, cancel);
#endif
        return ShowActionSheetAsync(title, message, checkboxLabel, accept, cancel);
    }

    private static async Task<bool?> ShowActionSheetAsync(
        string title, string message, string checkboxLabel, string accept, string cancel)
    {
        var choice = await Shell.Current.DisplayActionSheetAsync($"{title}\n{message}", cancel, null, accept, checkboxLabel);
        return choice == accept ? false : choice == checkboxLabel ? true : null;
    }

#if ANDROID
    private static Task<bool?> ShowNativeAsync(
        Context context, string title, string message, string checkedMessage, string checkboxLabel, string accept, string cancel)
    {
        var result = new TaskCompletionSource<bool?>();

        var checkBox = new CheckBox(context) { Text = checkboxLabel };

        // Lined up with the dialog's message, which the theme insets by 24 dp; the box
        // itself sits about 6 dp inside its own bounds.
        var density = context.Resources!.DisplayMetrics!.Density;
        int Dp(int value) => (int)(value * density);
        var frame = new FrameLayout(context);
        frame.SetPadding(Dp(24 - 6), Dp(8), Dp(24), 0);
        frame.AddView(checkBox);

        var dialog = new AlertDialog.Builder(context)
            .SetTitle(title)!
            .SetMessage(message)!
            .SetView(frame)!
            .SetPositiveButton(accept, (_, _) => result.TrySetResult(checkBox.Checked))!
            .SetNegativeButton(cancel, (_, _) => result.TrySetResult(null))!
            .Create();

        // Back, or a tap outside.
        dialog.CancelEvent += (_, _) => result.TrySetResult(null);
        dialog.DismissEvent += (_, _) => result.TrySetResult(null);

        dialog.Show();

        var acceptButton = dialog.GetButton((int)Android.Content.DialogButtonType.Positive);
        var acceptColors = acceptButton?.TextColors;
        var danger = DangerColor()?.ToPlatform();

        checkBox.CheckedChange += (_, e) =>
        {
            dialog.SetMessage(e.IsChecked ? checkedMessage : message);
            if (acceptButton is null)
                return;

            if (e.IsChecked && danger.HasValue)
                acceptButton.SetTextColor(danger.Value);
            else if (acceptColors is not null)
                acceptButton.SetTextColor(acceptColors);
        };

        return result.Task;
    }

    private static Color? DangerColor()
    {
        var key = Application.Current?.RequestedTheme == AppTheme.Dark ? "DangerDark" : "Danger";
        return Application.Current?.Resources.TryGetValue(key, out var value) == true ? value as Color : null;
    }
#endif
}
