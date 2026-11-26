using System.Collections.ObjectModel;
using Lynx.Operator.Core;
using Lynx.Protocol;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Lynx.Operator;

public sealed partial class MainWindow : Window
{
    private enum InteractionMode
    {
        Pan,
        Calibrate,
        Marker
    }

    private readonly long _clientId;
    private readonly ObservableCollection<MarkerRow> _markerRows = new();
    private readonly Dictionary<long, UIElement> _authoritativeMarkerVisuals = new();
    private readonly Dictionary<long, UIElement> _pendingMarkerVisuals = new();
    private readonly List<UIElement> _calibrationVisuals = new();

    private OperatorRelayClient? _relayClient;
    private CancellationTokenSource? _relayCts;
    private Task? _relayRunTask;

    private FloorplanCalibration? _calibration;
    private PixelPoint? _originPixel;
    private PixelPoint? _xReferencePixel;
    private PixelPoint? _zSidePixel;
    private int _calibrationStep;

    private InteractionMode _mode = InteractionMode.Pan;
    private double _imageWidth;
    private double _imageHeight;
    private double _zoom = 1;
    private PixelPoint _pan = new(0, 0);
    private bool _isPanning;
    private PixelPoint _panStartPointer;
    private PixelPoint _panStartOffset;

    public MainWindow()
    {
        InitializeComponent();

        _clientId = new DesktopClientIdentityStore().GetOrCreate();
        ClientIdText.Text = $"Client {_clientId}";
        MarkerList.ItemsSource = _markerRows;
        Closed += MainWindow_Closed;

        UpdateModeUi();
        UpdatePanZoomUi();
    }

    private bool HasFloorplan => _imageWidth > 0 && _imageHeight > 0;

    private async void LoadFloorplan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");

            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null) return;

            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);

            _imageWidth = bitmap.PixelWidth;
            _imageHeight = bitmap.PixelHeight;

            if (_imageWidth <= 0 || _imageHeight <= 0)
                throw new InvalidOperationException("The selected image has invalid dimensions.");

            FloorplanImage.Source = bitmap;
            FloorplanImage.Width = _imageWidth;
            FloorplanImage.Height = _imageHeight;
            FloorplanSurface.Width = _imageWidth;
            FloorplanSurface.Height = _imageHeight;
            EmptyFloorplanPanel.Visibility = Visibility.Collapsed;

            _zoom = 1;
            _pan = new PixelPoint(0, 0);
            ZoomSlider.Value = 1;
            ClearCalibrationInternal();
            UpdateRenderTransform();
            ImageInfoText.Text =
                $"{file.Name}  {_imageWidth:0} x {_imageHeight:0} px";
            SetStatus("Floorplan loaded. Start calibration or pan/zoom the image.");
        }
        catch (Exception ex)
        {
            SetStatus($"Floorplan load failed: {ex.Message}");
        }
    }

    private void PanMode_Click(object sender, RoutedEventArgs e)
    {
        _mode = InteractionMode.Pan;
        UpdateModeUi();
    }

    private void CalibrationMode_Click(object sender, RoutedEventArgs e)
    {
        _mode = InteractionMode.Calibrate;
        if (_calibrationStep == 0 && _calibration is null)
            StartCalibrationInternal();
        UpdateModeUi();
    }

    private void MarkerMode_Click(object sender, RoutedEventArgs e)
    {
        _mode = InteractionMode.Marker;
        UpdateModeUi();
    }

    private void StartCalibration_Click(object sender, RoutedEventArgs e)
    {
        StartCalibrationInternal();
        _mode = InteractionMode.Calibrate;
        UpdateModeUi();
    }

    private void ClearCalibration_Click(object sender, RoutedEventArgs e)
    {
        ClearCalibrationInternal();
        SetStatus("Floorplan calibration cleared.");
    }

    private void StartCalibrationInternal()
    {
        _calibration = null;
        _originPixel = null;
        _xReferencePixel = null;
        _zSidePixel = null;
        _calibrationStep = 0;
        ClearCalibrationVisuals();
        ClearAuthoritativeVisuals();
        CalibrationInstructionText.Text =
            "Step 1/3: click the AprilTag center / shared origin on the floorplan.";
        CalibrationResultText.Text = "Origin --\n+X --\n+Z --\nScale --";
        SetStatus("Calibration started.");
    }

    private void ClearCalibrationInternal()
    {
        _calibration = null;
        _originPixel = null;
        _xReferencePixel = null;
        _zSidePixel = null;
        _calibrationStep = 0;
        ClearCalibrationVisuals();
        ClearAuthoritativeVisuals();
        CalibrationInstructionText.Text = "Not calibrated.";
        CalibrationResultText.Text = "Origin --\n+X --\n+Z --\nScale --";
    }

    private async void ConnectRelay_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RelayPortBox.Text, out var port) || port is <= 0 or > 65535)
        {
            SetStatus("Relay port must be between 1 and 65535.");
            return;
        }

        var host = RelayHostBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(host)) host = "auto";

        await DisconnectRelayAsync();

        _relayClient = new OperatorRelayClient(_clientId);
        _relayClient.StatusChanged += RelayClient_StatusChanged;
        _relayClient.RelayEndpointChanged += RelayClient_RelayEndpointChanged;
        _relayClient.StateChanged += RelayClient_StateChanged;
        _relayCts = new CancellationTokenSource();

        ConnectionText.Text = "Connecting...";
        ConnectionText.Foreground = new SolidColorBrush(Colors.Goldenrod);

        _relayRunTask = _relayClient.RunAsync(
            new OperatorRelayClientOptions(
                RelayHost: host,
                RelayPort: port),
            _relayCts.Token);

        SetStatus($"Connecting operator client {_clientId} to {host}:{port}.");
    }

    private async void DisconnectRelay_Click(object sender, RoutedEventArgs e)
    {
        await DisconnectRelayAsync();
    }

    private void AutoRelay_Click(object sender, RoutedEventArgs e)
    {
        RelayHostBox.Text = "auto";
        SetStatus("Relay host set to auto-discovery.");
    }

    private async Task DisconnectRelayAsync()
    {
        var cts = _relayCts;
        var runTask = _relayRunTask;
        var client = _relayClient;

        _relayCts = null;
        _relayRunTask = null;
        _relayClient = null;

        cts?.Cancel();
        if (runTask is not null)
        {
            try { await runTask; }
            catch (OperationCanceledException) { }
        }

        if (client is not null)
            await client.DisposeAsync();
        cts?.Dispose();

        ConnectionText.Text = "Disconnected";
        ConnectionText.Foreground = new SolidColorBrush(Colors.Orange);
        RelayEndpointText.Text = "Relay --";
    }

    private void RelayClient_StatusChanged(string status)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            SetStatus(status);
            ConnectionText.Text = status;
        });
    }

    private void RelayClient_RelayEndpointChanged(RelayEndpoint? endpoint)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (endpoint is null)
            {
                RelayEndpointText.Text = "Relay --";
                ConnectionText.Foreground = new SolidColorBrush(Colors.Orange);
                return;
            }

            RelayEndpointText.Text = $"Relay {endpoint.Host}:{endpoint.Port}";
            ConnectionText.Text = "Connected / registered";
            ConnectionText.Foreground = new SolidColorBrush(Colors.LightGreen);
        });
    }

    private void RelayClient_StateChanged(
        IReadOnlyList<PingMessage> pings,
        long epoch,
        long version)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            VersionText.Text = $"Epoch {epoch} / Version {version}";
            ApplyAuthoritativeState(pings);
        });
    }

    private void ApplyAuthoritativeState(IReadOnlyList<PingMessage> pings)
    {
        var authoritativeIds = pings.Select(ping => ping.Id).ToHashSet();
        foreach (var pendingId in _pendingMarkerVisuals.Keys.ToArray())
        {
            if (!authoritativeIds.Contains(pendingId)) continue;
            RemovePendingVisual(pendingId);
        }

        _markerRows.Clear();
        foreach (var ping in pings.OrderBy(ping => ping.Id))
            _markerRows.Add(new MarkerRow(ping, _clientId));

        ClearAuthoritativeVisuals();
        if (_calibration is null) return;

        foreach (var ping in pings)
        {
            if (ping.BeaconId != 0) continue;

            var pixel = _calibration.SharedToPixel(
                new SharedPoint(ping.X, ping.Y, ping.Z));
            var visual = AddPointVisual(
                pixel,
                ping.SenderId == _clientId ? Colors.DeepSkyBlue : Colors.Red,
                16);
            _authoritativeMarkerVisuals[ping.Id] = visual;
        }
    }

    private void ViewportHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateRenderTransform();

    private void ZoomSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _zoom = e.NewValue;
        if (FloorplanRenderTransform is not null)
            UpdateRenderTransform();
    }

    private void ViewportHost_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!HasFloorplan) return;

        var position = e.GetCurrentPoint(ViewportHost).Position;
        var viewportPoint = new PixelPoint(position.X, position.Y);

        if (_mode == InteractionMode.Pan)
        {
            _isPanning = true;
            _panStartPointer = viewportPoint;
            _panStartOffset = _pan;
            ViewportHost.CapturePointer(e.Pointer);
            return;
        }

        var imagePixel = CurrentViewportTransform().ViewportToImage(viewportPoint);
        if (!CurrentViewportTransform().ContainsImagePixel(imagePixel))
        {
            SetStatus("Click is outside the floorplan image.");
            return;
        }

        if (_mode == InteractionMode.Calibrate)
        {
            HandleCalibrationClick(imagePixel);
            return;
        }

        if (_mode == InteractionMode.Marker)
            _ = PlaceMarkerAsync(imagePixel);
    }

    private void ViewportHost_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!HasFloorplan) return;

        var position = e.GetCurrentPoint(ViewportHost).Position;
        var viewportPoint = new PixelPoint(position.X, position.Y);

        if (_isPanning)
        {
            var delta = viewportPoint - _panStartPointer;
            _pan = _panStartOffset + delta;
            UpdateRenderTransform();
        }

        var imagePixel = CurrentViewportTransform().ViewportToImage(viewportPoint);
        if (!CurrentViewportTransform().ContainsImagePixel(imagePixel))
        {
            CursorText.Text = "PIXEL --   SHARED --";
            return;
        }

        if (_calibration is null)
        {
            CursorText.Text = $"PIXEL {imagePixel.X:0.0}, {imagePixel.Y:0.0}   SHARED --";
            return;
        }

        var shared = _calibration.PixelToShared(imagePixel);
        CursorText.Text =
            $"PIXEL {imagePixel.X:0.0}, {imagePixel.Y:0.0}   " +
            $"SHARED X {shared.X:0.00}  Z {shared.Z:0.00} m";
    }

    private void ViewportHost_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanning) return;
        _isPanning = false;
        ViewportHost.ReleasePointerCapture(e.Pointer);
    }

    private void ViewportHost_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!HasFloorplan) return;

        var point = e.GetCurrentPoint(ViewportHost);
        var cursor = new PixelPoint(point.Position.X, point.Position.Y);
        var before = CurrentViewportTransform().ViewportToImage(cursor);

        var factor = point.Properties.MouseWheelDelta > 0 ? 1.12 : 1 / 1.12;
        var nextZoom = Math.Clamp(_zoom * factor, ZoomSlider.Minimum, ZoomSlider.Maximum);
        if (Math.Abs(nextZoom - _zoom) < 1e-9) return;

        _zoom = nextZoom;
        ZoomSlider.Value = nextZoom;

        var afterTransform = CurrentViewportTransform();
        var afterViewport = afterTransform.ImageToViewport(before);
        _pan += cursor - afterViewport;
        UpdateRenderTransform();
        e.Handled = true;
    }

    private void HandleCalibrationClick(PixelPoint pixel)
    {
        switch (_calibrationStep)
        {
            case 0:
                _originPixel = pixel;
                _calibrationVisuals.Add(AddPointVisual(pixel, Colors.Yellow, 18));
                _calibrationStep = 1;
                CalibrationInstructionText.Text =
                    "Step 2/3: click a point along physical shared +X. Enter its known distance above.";
                SetStatus($"Calibration origin set at ({pixel.X:0.0}, {pixel.Y:0.0}).");
                break;

            case 1:
                _xReferencePixel = pixel;
                _calibrationVisuals.Add(AddPointVisual(pixel, Colors.Red, 18));
                if (_originPixel is not null)
                    _calibrationVisuals.Add(AddLineVisual(_originPixel.Value, pixel, Colors.Red));
                _calibrationStep = 2;
                CalibrationInstructionText.Text =
                    "Step 3/3: click anywhere on the side of the origin that represents positive shared +Z.";
                SetStatus($"+X reference set at ({pixel.X:0.0}, {pixel.Y:0.0}).");
                break;

            case 2:
                _zSidePixel = pixel;
                if (!double.TryParse(CalibrationDistanceBox.Text, out var distance) ||
                    distance <= 0)
                {
                    SetStatus("Enter a valid positive +X reference distance in metres.");
                    return;
                }

                try
                {
                    _calibration = FloorplanCalibration.Create(
                        _originPixel!.Value,
                        _xReferencePixel!.Value,
                        _zSidePixel.Value,
                        distance);

                    _calibrationVisuals.Add(AddPointVisual(pixel, Colors.LimeGreen, 18));
                    _calibrationVisuals.Add(AddLineVisual(
                        _originPixel.Value,
                        pixel,
                        Colors.LimeGreen));

                    _calibrationStep = 3;
                    CalibrationInstructionText.Text =
                        "READY — floorplan pixels now map to shared X/Z coordinates.";
                    CalibrationResultText.Text =
                        $"Origin {_calibration.OriginPixel.X:0.0}, {_calibration.OriginPixel.Y:0.0}\n" +
                        $"+X {_calibration.XAxisPixel.X:0.000}, {_calibration.XAxisPixel.Y:0.000}\n" +
                        $"+Z {_calibration.ZAxisPixel.X:0.000}, {_calibration.ZAxisPixel.Y:0.000}\n" +
                        $"Scale {_calibration.MetersPerPixel:0.000000} m/px";

                    SetStatus("Floorplan calibration complete.");
                    if (_relayClient is not null)
                        ApplyAuthoritativeState(_relayClient.StateSnapshot());
                }
                catch (Exception ex)
                {
                    SetStatus($"Calibration rejected: {ex.Message}");
                }
                break;
        }
    }

    private async Task PlaceMarkerAsync(PixelPoint imagePixel)
    {
        if (_calibration is null)
        {
            SetStatus("Calibrate the floorplan before placing markers.");
            return;
        }

        var client = _relayClient;
        if (client?.CurrentRelay is null)
        {
            SetStatus("Connect to the relay before placing markers.");
            return;
        }

        var shared = _calibration.PixelToShared(imagePixel);
        var preview = AddPointVisual(imagePixel, Colors.Gold, 14);
        LastCommandText.Text =
            $"CREATE pending  shared=({shared.X:0.000}, 0, {shared.Z:0.000})";

        try
        {
            var ping = await client.CreateMarkerAsync(shared, beaconId: 0);

            if (client.StateSnapshot().Any(existing => existing.Id == ping.Id))
            {
                FloorplanSurface.Children.Remove(preview);
            }
            else
            {
                _pendingMarkerVisuals[ping.Id] = preview;
                _ = ExpirePendingPreviewAsync(ping.Id);
            }

            LastCommandText.Text =
                $"CREATE id={ping.Id} shared=({ping.X:0.000}, {ping.Y:0.000}, {ping.Z:0.000})";
            SetStatus("Marker command sent; waiting for authoritative relay echo.");
        }
        catch (Exception ex)
        {
            FloorplanSurface.Children.Remove(preview);
            SetStatus($"Marker create failed: {ex.Message}");
        }
    }

    private async Task ExpirePendingPreviewAsync(long pingId)
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_pendingMarkerVisuals.ContainsKey(pingId)) return;
            RemovePendingVisual(pingId);
            SetStatus($"Marker {pingId} was not confirmed; preview removed.");
        });
    }

    private async void RemoveSelectedMarker_Click(object sender, RoutedEventArgs e)
    {
        if (MarkerList.SelectedItem is not MarkerRow row)
        {
            SetStatus("Select a marker to remove.");
            return;
        }

        var client = _relayClient;
        if (client is null)
        {
            SetStatus("Connect to the relay before removing markers.");
            return;
        }

        if (!row.OwnedByOperator)
        {
            SetStatus("Prototype 0.5 only allows this operator client to remove its own markers.");
            return;
        }

        try
        {
            await client.RemoveMarkerAsync(row.Ping.Id);
            LastCommandText.Text = $"REMOVE id={row.Ping.Id}";
            SetStatus("Remove command sent; waiting for authoritative relay update.");
        }
        catch (Exception ex)
        {
            SetStatus($"Marker remove failed: {ex.Message}");
        }
    }

    private FloorplanViewportTransform CurrentViewportTransform() =>
        new(
            _imageWidth,
            _imageHeight,
            Math.Max(ViewportHost.ActualWidth, 1),
            Math.Max(ViewportHost.ActualHeight, 1),
            _zoom,
            _pan);

    private void UpdateRenderTransform()
    {
        if (!HasFloorplan || ViewportHost.ActualWidth <= 0 || ViewportHost.ActualHeight <= 0)
            return;

        var transform = CurrentViewportTransform();
        var topLeft = transform.ImageToViewport(new PixelPoint(0, 0));

        FloorplanRenderTransform.ScaleX = transform.Scale;
        FloorplanRenderTransform.ScaleY = transform.Scale;
        FloorplanRenderTransform.TranslateX = topLeft.X;
        FloorplanRenderTransform.TranslateY = topLeft.Y;

        UpdatePanZoomUi();
    }

    private void UpdatePanZoomUi()
    {
        if (PanZoomText is null) return;
        PanZoomText.Text =
            $"Zoom {_zoom:0.00} / Pan ({_pan.X:0.0}, {_pan.Y:0.0})";
    }

    private void UpdateModeUi()
    {
        ModeText.Text = $"MODE: {_mode.ToString().ToUpperInvariant()}";
        SetStatus(_mode switch
        {
            InteractionMode.Pan => "Pan mode: drag the floorplan; mouse wheel zooms.",
            InteractionMode.Calibrate => "Calibration mode: follow the three-step calibration panel.",
            InteractionMode.Marker => "Marker mode: click a calibrated floorplan point to send an authoritative marker command.",
            _ => "Ready."
        });
    }

    private UIElement AddPointVisual(
        PixelPoint pixel,
        Windows.UI.Color color,
        double size)
    {
        var ellipse = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(color),
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 1.5
        };

        Canvas.SetLeft(ellipse, pixel.X - (size / 2));
        Canvas.SetTop(ellipse, pixel.Y - (size / 2));
        Canvas.SetZIndex(ellipse, 10);
        FloorplanSurface.Children.Add(ellipse);
        return ellipse;
    }

    private UIElement AddLineVisual(
        PixelPoint from,
        PixelPoint to,
        Windows.UI.Color color)
    {
        var line = new Line
        {
            X1 = from.X,
            Y1 = from.Y,
            X2 = to.X,
            Y2 = to.Y,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2
        };
        Canvas.SetZIndex(line, 5);
        FloorplanSurface.Children.Add(line);
        return line;
    }

    private void ClearCalibrationVisuals()
    {
        foreach (var visual in _calibrationVisuals)
            FloorplanSurface.Children.Remove(visual);
        _calibrationVisuals.Clear();
    }

    private void ClearAuthoritativeVisuals()
    {
        foreach (var visual in _authoritativeMarkerVisuals.Values)
            FloorplanSurface.Children.Remove(visual);
        _authoritativeMarkerVisuals.Clear();
    }

    private void RemovePendingVisual(long pingId)
    {
        if (!_pendingMarkerVisuals.Remove(pingId, out var visual)) return;
        FloorplanSurface.Children.Remove(visual);
    }

    private void SetStatus(string text)
    {
        if (StatusText is not null)
            StatusText.Text = text;
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        await DisconnectRelayAsync();
    }

    private sealed class MarkerRow
    {
        public MarkerRow(PingMessage ping, long operatorClientId)
        {
            Ping = ping;
            OwnedByOperator = ping.SenderId == operatorClientId;
        }

        public PingMessage Ping { get; }
        public bool OwnedByOperator { get; }

        public override string ToString() =>
            $"{(OwnedByOperator ? "OWN" : "REMOTE")}  {Ping.Id}  " +
            $"({Ping.X:0.00}, {Ping.Y:0.00}, {Ping.Z:0.00})  b{Ping.BeaconId}";
    }
}
