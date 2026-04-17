using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Robot.Abstractions;
using Robot.Core;

namespace Robot.App.Wpf;

public partial class MainWindow : Window
{
    private const int TrailDurationSeconds = 10;
    private const int MaxTrailPoints = 2000;
    private const double MinimumAxisRangeForMapping = 1e-6;

    private readonly DriverPluginLoader _pluginLoader = new();
    private readonly List<TrailPoint> _trail = new();

    private MotionEngine? _engine;
    private IRobotDevice? _device;
    private MachineConfig? _config;
    private Pose6 _latestPose;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RenderAllViews();
    }

    private void ConnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        var configPath = System.IO.Path.Combine(AppContext.BaseDirectory, "machine.json");
        _config = MachineConfigStore.LoadOrCreateDefault(configPath);

        var driversPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Drivers");
        var loadResult = _pluginLoader.LoadFactories(driversPath);
        DriverSummaryListBox.ItemsSource = loadResult.Summary;

        var deviceConfig = _config.Devices.FirstOrDefault();
        if (deviceConfig is null)
        {
            SetAlarmText("Alarm: Missing device config.", true);
            return;
        }

        var factory = loadResult.Factories.FirstOrDefault(x =>
            x.DriverId.Equals(deviceConfig.DriverId, StringComparison.OrdinalIgnoreCase));
        if (factory is null)
        {
            SetAlarmText($"Alarm: Driver '{deviceConfig.DriverId}' not found.", true);
            return;
        }

        _device = factory.CreateDevice(deviceConfig.Parameters);

        _engine?.Dispose();
        _engine = new MotionEngine();
        _engine.Configure(_config);
        _engine.AttachDevice(_device);
        _engine.PoseUpdated += EngineOnPoseUpdated;
        _engine.AlarmRaised += (_, message) => Dispatcher.Invoke(() => SetAlarmText($"Alarm: {message}", true));
        _engine.AlarmStateChanged += (_, active) => Dispatcher.Invoke(() =>
        {
            if (!active)
            {
                SetAlarmText("Alarm: None", false);
            }

            RenderAllViews();
        });
        _engine.Start();

        _trail.Clear();
        SetAlarmText("Alarm: None", false);
    }

    private void MoveLinearButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_engine is null)
        {
            return;
        }

        if (!TryGetPoseFromTargetInputs(out var target) || !TryGetFeed(out var feed))
        {
            return;
        }

        _engine.MoveLinear(target, feed);
    }

    private void MoveArcButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_engine is null)
        {
            return;
        }

        if (!TryGetPoseFromTargetInputs(out var target) || !TryGetFeed(out var feed))
        {
            return;
        }

        if (!TryParse(ViaXTextBox.Text, out var viaX)
            || !TryParse(ViaYTextBox.Text, out var viaY)
            || !TryParse(ViaZTextBox.Text, out var viaZ))
        {
            return;
        }

        var via = new Pose6(viaX, viaY, viaZ, _latestPose.Rx, _latestPose.Ry, _latestPose.Rz);
        _engine.MoveArc3D_3Point(via, target, feed);
    }

    private void ResetAlarmButton_OnClick(object sender, RoutedEventArgs e)
    {
        _engine?.ResetAlarm();
    }

    private void SafetyIo_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_device is not IWritableSafetyIo writable)
        {
            return;
        }

        writable.EStop = EStopCheckBox.IsChecked == true;
        writable.DoorOpen = DoorOpenCheckBox.IsChecked == true;
    }

    private void EngineOnPoseUpdated(object? sender, Pose6 pose)
    {
        Dispatcher.Invoke(() =>
        {
            _latestPose = pose;
            PoseTextBlock.Text =
                $"Pose: X={pose.X:F2}, Y={pose.Y:F2}, Z={pose.Z:F2}, Rx={pose.Rx:F1}, Ry={pose.Ry:F1}, Rz={pose.Rz:F1}";

            _trail.Add(new TrailPoint(DateTime.UtcNow, pose));
            var cutoff = DateTime.UtcNow.AddSeconds(-TrailDurationSeconds);
            _trail.RemoveAll(x => x.Timestamp < cutoff);
            if (_trail.Count > MaxTrailPoints)
            {
                _trail.RemoveRange(0, _trail.Count - MaxTrailPoints);
            }

            RenderAllViews();
        });
    }

    private bool TryGetFeed(out double feed)
    {
        if (!TryParse(FeedTextBox.Text, out feed) || feed <= 0)
        {
            SetAlarmText("Alarm: Invalid feed.", true);
            return false;
        }

        return true;
    }

    private bool TryGetPoseFromTargetInputs(out Pose6 pose)
    {
        pose = default;
        if (!TryParse(TargetXTextBox.Text, out var x)
            || !TryParse(TargetYTextBox.Text, out var y)
            || !TryParse(TargetZTextBox.Text, out var z)
            || !TryParse(TargetRxTextBox.Text, out var rx)
            || !TryParse(TargetRyTextBox.Text, out var ry)
            || !TryParse(TargetRzTextBox.Text, out var rz))
        {
            SetAlarmText("Alarm: Invalid target input.", true);
            return false;
        }

        pose = new Pose6(x, y, z, rx, ry, rz);
        return true;
    }

    private static bool TryParse(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
           || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private void RenderAllViews()
    {
        RenderCanvas(TopCanvas, p => (p.X, p.Y), AxisId.Axis1, AxisId.Axis2);
        RenderCanvas(FrontCanvas, p => (p.X, p.Z), AxisId.Axis1, AxisId.Axis3);
        RenderCanvas(SideCanvas, p => (p.Y, p.Z), AxisId.Axis2, AxisId.Axis3);
    }

    private void RenderCanvas(Canvas canvas, Func<Pose6, (double A, double B)> projection, AxisId axisA, AxisId axisB)
    {
        canvas.Children.Clear();

        var width = canvas.ActualWidth > 20 ? canvas.ActualWidth : 900;
        var height = canvas.ActualHeight > 20 ? canvas.ActualHeight : 520;

        var limitA = _config?.GetAxisLimit(axisA);
        var limitB = _config?.GetAxisLimit(axisB);
        var minA = limitA?.Min ?? -1000;
        var maxA = limitA?.Max ?? 1000;
        var minB = limitB?.Min ?? -1000;
        var maxB = limitB?.Max ?? 1000;

        Point Map(Pose6 pose)
        {
            var (a, b) = projection(pose);
            var x = (a - minA) / Math.Max(MinimumAxisRangeForMapping, maxA - minA) * width;
            var y = height - ((b - minB) / Math.Max(MinimumAxisRangeForMapping, maxB - minB) * height);
            return new Point(x, y);
        }

        var color = _engine?.AlarmActive == true ? Brushes.Red : Brushes.Lime;
        var points = _trail.Select(x => Map(x.Pose)).ToList();
        if (points.Count > 1)
        {
            var polyline = new Polyline
            {
                Stroke = color,
                StrokeThickness = 2,
                Points = new PointCollection(points),
            };
            canvas.Children.Add(polyline);
        }

        var toolPoint = Map(_latestPose);
        var ellipse = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = Brushes.DeepSkyBlue,
            Stroke = Brushes.White,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(ellipse, toolPoint.X - 5);
        Canvas.SetTop(ellipse, toolPoint.Y - 5);
        canvas.Children.Add(ellipse);
    }

    private void SetAlarmText(string text, bool isAlarm)
    {
        AlarmTextBlock.Text = text;
        AlarmTextBlock.Foreground = isAlarm ? Brushes.Red : Brushes.Green;
    }

    protected override void OnClosed(EventArgs e)
    {
        _engine?.Dispose();
        base.OnClosed(e);
    }

    private readonly record struct TrailPoint(DateTime Timestamp, Pose6 Pose);
}
