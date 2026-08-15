using CommunityToolkit.Maui.Core;
using Storage.Core.Services;

namespace Storage.App.Views;

// Deliberately code-behind rather than MVVM: nearly everything here is direct
// manipulation of the CameraView control and page lifecycle, which a ViewModel
// would only have to reach back into the view to do.
public partial class CameraPage : ContentPage
{
    private readonly IPhotoService _photoService;

    // Raw capture bytes, held only between the shutter and Retake/Use photo.
    private byte[]? _pendingCapture;

    private Window? _window;
    private bool _previewRunning;
    private bool _isClosing;

    public CameraPage(IPhotoService photoService)
    {
        InitializeComponent();

        _photoService = photoService;
        Camera.MediaCaptured += OnMediaCaptured;
        Camera.MediaCaptureFailed += OnMediaCaptureFailed;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // A camera held by a backgrounded app will not reopen, so track the window
        // and release the preview whenever the app stops.
        _window = Application.Current?.Windows.FirstOrDefault();
        if (_window is not null)
        {
            _window.Stopped += OnWindowStopped;
            _window.Resumed += OnWindowResumed;
        }

        // A capture waiting on Retake/Use photo must survive the page coming back.
        if (ReviewLayer.IsVisible)
            return;

        await StartAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        if (_window is not null)
        {
            _window.Stopped -= OnWindowStopped;
            _window.Resumed -= OnWindowResumed;
            _window = null;
        }

        ReleaseCamera();

        // Stopping the preview unbinds the camera; disconnecting the handler makes
        // sure the native view goes with the page. Only on the way out, though —
        // a page that merely lost visibility still needs a working handler.
        if (_isClosing)
            Camera.Handler?.DisconnectHandler();
    }

    // MediaPicker's permission handling came for free; CameraView's does not.
    private async Task StartAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.Camera>();

        if (status != PermissionStatus.Granted)
        {
            // Android stops showing the rationale once the user has picked
            // "don't ask again" — at that point only app settings can undo it.
            if (Permissions.ShouldShowRationale<Permissions.Camera>())
            {
                ShowMessage("Storage needs camera access to take item photos.", offerGallery: true);
            }
            else
            {
                ShowMessage(
                    "Camera access is turned off for Storage. Enable it in app settings to take photos.",
                    offerGallery: true,
                    offerSettings: true);
            }

            return;
        }

        var cameras = await Camera.GetAvailableCameras(CancellationToken.None);
        if (cameras.Count == 0)
        {
            ShowMessage("No camera was found on this device.", offerGallery: true);
            return;
        }

        // Rear is the better lens for photographing a shelf; fall back to whatever exists.
        Camera.SelectedCamera = cameras.FirstOrDefault(c => c.Position == CameraPosition.Rear) ?? cameras[0];

        await ShowLiveAsync();
    }

    private async Task ShowLiveAsync()
    {
        MessageLayer.IsVisible = false;
        ReviewLayer.IsVisible = false;
        LiveLayer.IsVisible = true;

        if (!_previewRunning)
        {
            await Camera.StartCameraPreview(CancellationToken.None);
            _previewRunning = true;
        }
    }

    private void ShowMessage(string message, bool offerGallery = false, bool offerSettings = false)
    {
        MessageLabel.Text = message;
        GalleryFallbackButton.IsVisible = offerGallery;
        SettingsButton.IsVisible = offerSettings;

        LiveLayer.IsVisible = false;
        ReviewLayer.IsVisible = false;
        MessageLayer.IsVisible = true;
    }

    private async void OnShutterTapped(object? sender, TappedEventArgs e)
    {
        if (Camera.IsBusy)
            return;

        await Camera.CaptureImage(CancellationToken.None);
    }

    private void OnMediaCaptured(object? sender, MediaCapturedEventArgs e)
    {
        // The event stream is only valid inside the handler, so copy it out.
        using var buffer = new MemoryStream();
        e.Media.CopyTo(buffer);
        _pendingCapture = buffer.ToArray();

        // Handler runs off the UI thread on Android.
        Dispatcher.Dispatch(ShowReview);
    }

    private void OnMediaCaptureFailed(object? sender, MediaCaptureFailedEventArgs e)
    {
        Dispatcher.Dispatch(() =>
            ShowMessage($"The camera could not take that photo.\n\n{e.FailureReason}", offerGallery: true));
    }

    private void ShowReview()
    {
        var bytes = _pendingCapture;
        if (bytes is null)
            return;

        ReviewImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));

        LiveLayer.IsVisible = false;
        MessageLayer.IsVisible = false;
        ReviewLayer.IsVisible = true;
    }

    private async void OnRetakeClicked(object? sender, EventArgs e)
    {
        _pendingCapture = null;
        ReviewImage.Source = null;
        await ShowLiveAsync();
    }

    private async void OnUsePhotoClicked(object? sender, EventArgs e)
    {
        if (_pendingCapture is not byte[] bytes)
            return;

        SetBusy(true);
        try
        {
            using var stream = new MemoryStream(bytes);
            var fileName = await _photoService.SaveAsync(stream);
            await CloseAsync(fileName);
        }
        catch (Exception ex)
        {
            SetBusy(false);
            await DisplayAlertAsync("Error", $"The photo could not be saved.\n\n{ex.Message}", "OK");
        }
    }

    private async void OnGalleryClicked(object? sender, EventArgs e)
    {
        SetBusy(true);
        try
        {
            var fileName = await _photoService.PickFromGalleryAsync();
            if (fileName is null)
            {
                SetBusy(false);
                return;
            }

            await CloseAsync(fileName);
        }
        catch (Exception ex)
        {
            SetBusy(false);
            await DisplayAlertAsync("Error", $"That image could not be imported.\n\n{ex.Message}", "OK");
        }
    }

    private void OnSettingsClicked(object? sender, EventArgs e) => AppInfo.Current.ShowSettingsUI();

    private async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(null);

    // Hardware back has to release the camera the same way the buttons do.
    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    private async Task CloseAsync(string? fileName)
    {
        if (_isClosing)
            return;

        _isClosing = true;
        ReleaseCamera();

        // Shell hands these back to the previous page's IQueryAttributable.
        var parameters = fileName is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object> { ["photoFileName"] = fileName };

        await Shell.Current.GoToAsync("..", parameters);
    }

    private void OnWindowStopped(object? sender, EventArgs e) => ReleaseCamera();

    private async void OnWindowResumed(object? sender, EventArgs e)
    {
        // Only the live state owns the camera; a review in progress must survive
        // backgrounding untouched.
        if (!_isClosing && LiveLayer.IsVisible)
            await ShowLiveAsync();
    }

    private void ReleaseCamera()
    {
        if (!_previewRunning)
            return;

        _previewRunning = false;

        try
        {
            Camera.StopCameraPreview();
        }
        catch (Exception)
        {
            // Teardown races with the handler being disconnected; nothing to recover.
        }
    }

    private void SetBusy(bool busy)
    {
        BusyIndicator.IsVisible = busy;
        BusyIndicator.IsRunning = busy;
    }
}
