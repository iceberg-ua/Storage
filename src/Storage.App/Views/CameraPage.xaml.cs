using System.Collections.ObjectModel;
using CommunityToolkit.Maui.Core;
using Storage.App.Services;
using Storage.App.ViewModels;
using Storage.Core.Repositories;
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
//
// Two ways in. "camera": one photo for a form that is already open, reviewed, then
// handed back. "snap": the start of a new item — photos one after another, each kept as
// it is taken, a place chosen up top, then Next swaps this page for the item form with
// both filled in.
public partial class CameraPage : ContentPage, IQueryAttributable
{
    /// <summary>Ceiling on the wait for the CameraView's handler. A layout pass takes
    /// milliseconds; this only exists so a page that will never lay out shows a message
    /// instead of hanging on a black screen.</summary>
    private static readonly TimeSpan HandlerWaitTimeout = TimeSpan.FromSeconds(5);

    private readonly IPhotoService _photoService;
    private readonly ILocationRepository _locationRepository;
    private readonly PlacePicker _placePicker;

    // Snap mode: the photos taken so far, in order, and where the item will go. The
    // files are written as they are taken; until Next hands them to the form, closing
    // the camera is what deletes them.
    private readonly List<string> _snapPhotos = [];
    private bool _isSnap;
    private bool _isOpened;
    private int? _snapLocationId;
    private bool _isPicking;

    // The strip, rebuilt from _snapPhotos so each thumbnail can say which one it is.
    private readonly ObservableCollection<SnapThumb> _strip = [];

    // Photos still being written (a shot or a gallery pick being compressed). Next and
    // Cancel wait for them, so none lands after the page has handed its photos over or
    // thrown them away, and the limit counts them, so quick taps cannot pass it.
    private readonly List<Task> _pendingSaves = [];
    private int _savesInFlight;

    // Set by Cancel's "Discard": a save that finishes after it deletes its file.
    private bool _isDiscarding;

    // The system gallery picker covers the page; coming back from it is not a reason to
    // run the permission check and start the camera again.
    private bool _isPickingFromGallery;

    // Every transition that binds or unbinds the camera takes this first. Without it
    // OnAppearing and Window.Resumed — which both fire when Android brings the app
    // back — can each see _previewRunning still false and bind the camera twice.
    private readonly SemaphoreSlim _cameraGate = new(1, 1);

    // Raw capture bytes, held only between the shutter and Retake/Use photo.
    private byte[]? _pendingCapture;

    private Window? _window;
    private bool _previewRunning;
    private bool _isClosing;

    public CameraPage(IPhotoService photoService, ILocationRepository locationRepository, PlacePicker placePicker)
    {
        InitializeComponent();

        _photoService = photoService;
        _locationRepository = locationRepository;
        _placePicker = placePicker;
        BindableLayout.SetItemsSource(SnapStrip, _strip);
        Camera.MediaCaptured += OnMediaCaptured;
        Camera.MediaCaptureFailed += OnMediaCaptureFailed;
    }

    // "snap" turns on the new-item flow; "locationId" is where the item starts out. Only
    // on the way in: Shell can send the route's query again when a page on top (the place
    // picker) pops, and the place picked there must not be reset to the original one.
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (_isOpened)
            return;

        _isOpened = true;
        if (query.ContainsKey("snap"))
        {
            _isSnap = true;
            if (query.TryGetValue("locationId", out var id) && int.TryParse(Convert.ToString(id), out var locationId))
                _snapLocationId = locationId;

            SingleBar.IsVisible = false;
            SnapTopBar.IsVisible = true;
            SnapBottomBar.IsVisible = true;
            UpdateSnapCount();
            _ = ShowPlaceAsync();
        }

        query.Clear();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // A camera held by a backgrounded app will not reopen, so track the window
        // and release the preview whenever the app stops.
        AttachWindow();

        // A capture waiting on Retake/Use photo must survive the page coming back.
        if (ReviewLayer.IsVisible || _isPickingFromGallery)
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
                ShowMessage("Storage needs camera access to take photos.", offerGallery: true);
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
        ContinueButton.IsVisible = _isSnap;

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

        if (_isSnap && IsSnapFull)
        {
            await DisplayAlertAsync("That's the most for one item", $"An item takes up to {AddItemViewModel.MaxPhotosPerItem} photos. Tap Next to describe it.", "OK");
            return;
        }

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

        // Handler runs off the UI thread on Android. In Snap there is no review: the
        // photo is kept at once and the camera stays live for the next one.
        if (_isSnap)
        {
            var bytes = _pendingCapture;
            _pendingCapture = null;

            // Counted at once, on this thread, so a second tap right after sees it.
            Interlocked.Increment(ref _savesInFlight);
            Dispatcher.Dispatch(() => Track(KeepSnapAsync(bytes)));
            return;
        }

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
            await DisplayAlertAsync("Couldn't save photo", $"Try taking it again.\n\n{ex.Message}".TrimEnd(), "OK");
        }
    }

    private async void OnGalleryClicked(object? sender, EventArgs e)
    {
        if (_isSnap && IsSnapFull)
        {
            await DisplayAlertAsync("That's the most for one item", $"An item takes up to {AddItemViewModel.MaxPhotosPerItem} photos.", "OK");
            return;
        }

        SetBusy(true);
        _isPickingFromGallery = true;
        if (_isSnap)
            Interlocked.Increment(ref _savesInFlight);

        var fromMessage = MessageLayer.IsVisible;
        var pick = PickFromGalleryAsync();
        if (_isSnap)
            Track(pick);

        // Picked from the message about the camera not opening: there is no strip on
        // show to add to, so carry on to the form with it. Out here, not in the pick,
        // because Next waits for every pending save, the pick among them.
        if (await pick && _isSnap && fromMessage)
            await NextAsync();
    }

    // True when a photo was picked.
    private async Task<bool> PickFromGalleryAsync()
    {
        try
        {
            // Writes the file too, compressing it on the way, so it is a pending save.
            var fileName = await _photoService.PickFromGalleryAsync();
            if (fileName is null)
                return false;

            if (!_isSnap)
            {
                await CloseAsync(fileName);
                return true;
            }

            await KeepAsync(fileName);
            return true;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Couldn't import photo", $"Try a different image.\n\n{ex.Message}".TrimEnd(), "OK");
            return false;
        }
        finally
        {
            _isPickingFromGallery = false;
            SetBusy(false);

            // OnAppearing left the camera alone while the picker was up; it comes back on
            // here if it was showing. A no-op when it is already running or the page closing.
            if (LiveLayer.IsVisible)
                await StartPreviewAsync();
        }
    }

    private void OnSettingsClicked(object? sender, EventArgs e) => AppInfo.Current.ShowSettingsUI();

    private async void OnCancelClicked(object? sender, EventArgs e) => await CancelAsync();

    // Hardware back has to release the camera the same way the buttons do.
    protected override bool OnBackButtonPressed()
    {
        _ = CancelAsync();
        return true;
    }

    // Leaving Snap with photos taken throws them away, so it asks first.
    private async Task CancelAsync()
    {
        if (_isClosing)
            return;

        if (_isSnap)
        {
            // A shot still being written counts as taken.
            await WaitForSavesAsync();

            if (_snapPhotos.Count > 0)
            {
                var count = _snapPhotos.Count == 1 ? "the photo" : $"the {_snapPhotos.Count} photos";
                if (!await DisplayAlertAsync("Discard the photos?", $"No item was made yet; {count} will be deleted.", "Discard", "Keep taking"))
                    return;
            }

            _isDiscarding = true;
            await _photoService.DeleteAllAsync(_snapPhotos.ToList());
            _snapPhotos.Clear();
            RefreshStrip();
        }

        await CloseAsync(null);
    }

    // --- Snap ----------------------------------------------------------------

    private bool IsSnapFull => _snapPhotos.Count + Volatile.Read(ref _savesInFlight) >= AddItemViewModel.MaxPhotosPerItem;

    // Keeps the save in the list Next and Cancel wait on, and takes it off the in-flight
    // count once it is done, however it ends. The count went up when the save started.
    private async void Track(Task save)
    {
        _pendingSaves.Add(save);
        try
        {
            await save;
        }
        catch (Exception)
        {
            // Reported by the save itself.
        }
        finally
        {
            _pendingSaves.Remove(save);
            Interlocked.Decrement(ref _savesInFlight);
            UpdateSnapCount();
        }
    }

    // Goes by the count, not the list: a shot is counted the moment it is taken but only
    // joins the list once the dispatcher gets to it.
    private async Task WaitForSavesAsync()
    {
        while (Volatile.Read(ref _savesInFlight) > 0)
        {
            var pending = _pendingSaves.ToList();
            await (pending.Count > 0 ? Task.WhenAll(pending) : Task.Delay(50));
        }
    }

    private async Task KeepSnapAsync(byte[]? bytes)
    {
        if (bytes is null)
            return;

        try
        {
            using var stream = new MemoryStream(bytes);
            await KeepAsync(await _photoService.SaveAsync(stream));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Couldn't save photo", $"Try taking it again.\n\n{ex.Message}".TrimEnd(), "OK");
        }
    }

    // A file written after Discard is nobody's, so it goes; otherwise it joins the strip,
    // including one that finishes while Next is waiting for it.
    private async Task KeepAsync(string fileName)
    {
        if (_isDiscarding)
        {
            await _photoService.DeleteAsync(fileName);
            return;
        }

        _snapPhotos.Add(fileName);
        RefreshStrip();
    }

    // Asks first: a tap meant to look at a photo should not lose it.
    private async void OnSnapPhotoTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string fileName || _isClosing)
            return;

        if (!await DisplayAlertAsync("Remove this photo?", "It will be deleted.", "Remove", "Keep"))
            return;

        _snapPhotos.Remove(fileName);
        RefreshStrip();
        await _photoService.DeleteAsync(fileName);
    }

    private void RefreshStrip()
    {
        _strip.Clear();
        for (var i = 0; i < _snapPhotos.Count; i++)
            _strip.Add(new SnapThumb(_snapPhotos[i], $"Photo {i + 1} of {_snapPhotos.Count}. Double tap to remove it"));

        UpdateSnapCount();
    }

    private void UpdateSnapCount()
    {
        var count = _snapPhotos.Count;
        SnapCountLabel.Text = count == 0 ? string.Empty : $"{count} of {AddItemViewModel.MaxPhotosPerItem}";
        SemanticProperties.SetDescription(NextButton, count == 0
            ? "Next: describe the item, without a photo"
            : $"Next: describe the item, with {count} photo{(count == 1 ? "" : "s")}");
    }

    // The picker covers this page, which stops the preview as it disappears; OnAppearing
    // starts it again when the picker closes. _isClosing stays false, so nothing is torn down.
    private async void OnPlaceTapped(object? sender, TappedEventArgs e)
    {
        if (_isClosing || _isPicking)
            return;

        _isPicking = true;
        try
        {
            var pick = await _placePicker.PickAsync(new PlacePickRequest(
                "Where does it go?", null, _snapLocationId, "Put it in {0}", AllowNone: true));
            if (pick is not null)
            {
                _snapLocationId = pick.LocationId;
                await ShowPlaceAsync();
            }
        }
        finally
        {
            _isPicking = false;
        }
    }

    private async Task ShowPlaceAsync()
    {
        try
        {
            var byId = (await _locationRepository.GetAllAsync()).ToDictionary(l => l.Id);
            if (_snapLocationId is int id && !byId.ContainsKey(id))
                _snapLocationId = null;

            var path = LocationPath.Of(_snapLocationId, byId);
            PlaceLabel.Text = path.Length == 0 ? "📍  No location  ▾" : $"📍  {path}  ▾";
            SemanticProperties.SetDescription(PlacePill, path.Length == 0 ? "Where it goes: no location. Change" : $"Where it goes: {path}. Change");
        }
        catch (Exception)
        {
            // Only the label; the item can still be made, and the form shows the place too.
            PlaceLabel.Text = "📍  Choose where it goes  ▾";
        }
    }

    private async void OnNextClicked(object? sender, EventArgs e) => await NextAsync();

    // Next, or "Continue without the camera": this page gives way to the item form, which
    // takes over the photos — its own Cancel is what deletes them from here on. The form
    // is pushed first and this page taken out from under it after, so the page below
    // never shows in between (a location's edit form would take that as being left).
    private async Task NextAsync()
    {
        if (_isClosing)
            return;

        var wasLive = LiveLayer.IsVisible;
        _isClosing = true;
        await StopPreviewAsync();

        // A shot still being written goes with the others.
        await WaitForSavesAsync();

        try
        {
            await Shell.Current.GoToAsync(Routes.CreateFromSnap(_snapLocationId, _snapPhotos));
            Shell.Current.Navigation.RemovePage(this);
        }
        catch (Exception ex)
        {
            _isClosing = false;
            await DisplayAlertAsync("Couldn't open the item form", $"Try again.\n\n{ex.Message}".TrimEnd(), "OK");

            // Back to whatever was showing: a camera that would not open stays a message.
            if (wasLive)
                await ShowLiveAsync();
            else
                MessageLayer.IsVisible = true;
        }
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

/// <summary>One thumbnail in the Snap strip, with what a screen reader says for it.</summary>
public sealed record SnapThumb(string FileName, string Description);
