using CommunityToolkit.Maui.Core;
using Storage.Core.Services;

namespace Storage.App.Views;

// Deliberately code-behind rather than MVVM: nearly everything here is direct
// manipulation of the CameraView control and page lifecycle, which a ViewModel
// would only have to reach back into the view to do.
//
// The page itself is transient (see MauiProgram) and Shell's route factory builds a
// fresh one per navigation, so no state here outlives a visit. The camera *device*
// does — it is a single-owner OS resource — which is why every start and stop below
// goes through one gate and why teardown finishes before the handler is dropped.
public partial class CameraPage : ContentPage
{
    /// <summary>Ceiling on the wait for the CameraView's handler. A layout pass takes
    /// milliseconds; this only exists so a page that will never lay out shows a message
    /// instead of hanging on a black screen.</summary>
    private static readonly TimeSpan HandlerWaitTimeout = TimeSpan.FromSeconds(5);

    private readonly IPhotoService _photoService;

    // Every transition that binds or unbinds the camera takes this first. Without it
    // OnAppearing and Window.Resumed — which both fire when Android brings the app
    // back — can each see _previewRunning still false and bind the camera twice.
    private readonly SemaphoreSlim _cameraGate = new(1, 1);

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
        AttachWindow();

        // A capture waiting on Retake/Use photo must survive the page coming back.
        if (ReviewLayer.IsVisible)
            return;

        await StartAsync();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        DetachWindow();

        await StopPreviewAsync();

        if (!_isClosing)
            return;

        // On the way out for good. Drop the subscriptions before the native view goes,
        // so a capture callback already in flight cannot land on a dead handler.
        Camera.MediaCaptured -= OnMediaCaptured;
        Camera.MediaCaptureFailed -= OnMediaCaptureFailed;

        // StopCameraPreview only *starts* the unbind; it finishes on the platform's own
        // camera thread. Disconnecting the handler in the same breath disposes the native
        // view mid-unbind and leaves the device half-released — which the next visit to
        // this page pays for, not this one. Yield to the dispatcher so the queued native
        // teardown drains first.
        await Dispatcher.DispatchAsync(() => { });

        try
        {
            Camera.Handler?.DisconnectHandler();
        }
        catch (Exception)
        {
            // The handler may already be gone; there is nothing left to release.
        }
    }

    private void AttachWindow()
    {
        // OnAppearing can run more than once per visit (returning from the gallery
        // picker, for one), and double subscription means double release.
        if (_window is not null)
            return;

        _window = Application.Current?.Windows.FirstOrDefault();
        if (_window is null)
            return;

        _window.Stopped += OnWindowStopped;
        _window.Resumed += OnWindowResumed;
    }

    private void DetachWindow()
    {
        if (_window is null)
            return;

        _window.Stopped -= OnWindowStopped;
        _window.Resumed -= OnWindowResumed;
        _window = null;
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

        await StartPreviewAsync();
    }

    private async Task StartPreviewAsync()
    {
        await _cameraGate.WaitAsync();
        try
        {
            // Re-checked inside the gate: a caller that queued behind a start, or behind
            // the teardown, must not bind a second session or revive a closing page.
            if (_previewRunning || _isClosing)
                return;

            if (!await WaitForHandlerAsync())
            {
                ShowMessage(
                    "The camera did not finish starting up. Go back and try again.",
                    offerGallery: true);
                return;
            }

            await Camera.StartCameraPreview(CancellationToken.None);
            _previewRunning = true;
        }
        catch (Exception ex)
        {
            // A camera the previous owner has not finished releasing lands here. Say so
            // rather than letting it surface as an unhandled native crash.
            ShowMessage($"The camera could not be opened.\n\n{ex.Message}", offerGallery: true);
        }
        finally
        {
            _cameraGate.Release();
        }
    }

    // The CameraView lives inside LiveLayer, which starts collapsed, so its handler is
    // only built once a layout pass has run with the layer visible. Setting IsVisible
    // merely queues that pass — it has not happened by the time we get here, and the
    // toolkit throws "Unable to retrieve Handler" if we start the preview first.
    //
    // Only the first visit of a session is slow enough to hide this: the permission
    // prompt and the initial, uncached GetAvailableCameras give the layout pass room to
    // land. Once both are warm the second visit outruns it, which is exactly why the
    // failure showed up on the second item and never the first.
    private async Task<bool> WaitForHandlerAsync()
    {
        if (Camera.Handler is not null)
            return true;

        var handlerReady = new TaskCompletionSource();

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (Camera.Handler is not null)
                handlerReady.TrySetResult();
        }

        Camera.HandlerChanged += OnHandlerChanged;
        try
        {
            // The handler can land between the check above and the subscription.
            if (Camera.Handler is not null)
                return true;

            var winner = await Task.WhenAny(handlerReady.Task, Task.Delay(HandlerWaitTimeout));
            return winner == handlerReady.Task && Camera.Handler is not null;
        }
        finally
        {
            Camera.HandlerChanged -= OnHandlerChanged;
        }
    }

    private async Task StopPreviewAsync()
    {
        await _cameraGate.WaitAsync();
        try
        {
            if (!_previewRunning)
                return;

            // Cleared before the call, not after: if the stop throws, the session is gone
            // either way and a later start must be allowed to rebind.
            _previewRunning = false;
            Camera.StopCameraPreview();
        }
        catch (Exception)
        {
            // Teardown races with the handler being disconnected; nothing to recover.
        }
        finally
        {
            _cameraGate.Release();
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
        // A tap that lands while the page is closing, or after backgrounding released
        // the preview, would capture against a camera that is no longer bound.
        if (_isClosing || !_previewRunning || Camera.IsBusy)
            return;

        try
        {
            await Camera.CaptureImage(CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowMessage($"The camera could not take that photo.\n\n{ex.Message}", offerGallery: true);
        }
    }

    private void OnMediaCaptured(object? sender, MediaCapturedEventArgs e)
    {
        // Fires on the platform's camera thread, which can outrun a page already on its
        // way out. Nothing below should touch views that are about to be torn down.
        if (_isClosing)
            return;

        // The event stream is only valid inside the handler, so copy it out.
        using var buffer = new MemoryStream();
        e.Media.CopyTo(buffer);
        _pendingCapture = buffer.ToArray();

        // Handler runs off the UI thread on Android.
        Dispatcher.Dispatch(ShowReview);
    }

    private void OnMediaCaptureFailed(object? sender, MediaCaptureFailedEventArgs e)
    {
        if (_isClosing)
            return;

        Dispatcher.Dispatch(() =>
            ShowMessage($"The camera could not take that photo.\n\n{e.FailureReason}", offerGallery: true));
    }

    private void ShowReview()
    {
        var bytes = _pendingCapture;
        if (bytes is null || _isClosing)
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

        // Set before the release so anything racing us — a shutter tap, a resume, a
        // capture callback — sees that this page is done and stays off the camera.
        _isClosing = true;
        await StopPreviewAsync();

        // Shell hands these back to the previous page's IQueryAttributable.
        var parameters = fileName is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object> { ["photoFileName"] = fileName };

        await Shell.Current.GoToAsync("..", parameters);
    }

    private async void OnWindowStopped(object? sender, EventArgs e) => await StopPreviewAsync();

    private async void OnWindowResumed(object? sender, EventArgs e)
    {
        // Only the live state owns the camera; a review in progress must survive
        // backgrounding untouched.
        if (!_isClosing && LiveLayer.IsVisible)
            await ShowLiveAsync();
    }

    private void SetBusy(bool busy)
    {
        BusyIndicator.IsVisible = busy;
        BusyIndicator.IsRunning = busy;
    }
}
