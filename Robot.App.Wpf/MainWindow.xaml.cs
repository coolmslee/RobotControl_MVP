using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using HelixToolkit.Wpf;
using Robot.Abstractions;
using Robot.Core;
using Robot.Core.Simulation3D;

namespace Robot.App.Wpf;

public partial class MainWindow : Window
{
    private const int TrailDurationSeconds = 10;
    private const int MaxTrailPoints = 2000;
    private const double MinimumAxisRangeForMapping = 1e-6;

    private readonly DriverPluginLoader _pluginLoader = new();
    private readonly List<TrailPoint> _trail = new();
    private readonly Ur5LikeRobotModel _robotModel = new();
    private readonly ScenePlacementService _scenePlacementService = new();
    private readonly List<LinkVisualBinding> _linkVisuals = new();
    private readonly List<JointVisualBinding> _jointVisuals = new();

    private MotionEngine? _engine;
    private IRobotDevice? _device;
    private MachineConfig? _config;
    private Pose6 _latestPose;
    private Model3DGroup? _sceneGroup;
    private GeometryModel3D? _workpieceModel;
    private GeometryModel3D? _fenceModel;
    private SceneBox _workpiece;
    private SceneBox _fence;
    private bool _is3DInitialized;
    private LinesVisual3D? _originToTcpLine;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RenderAllViews();
            Initialize3DScene();
            RandomizeScenePlacement();
            ApplyCameraPreset(CameraPreset.Work);
            UpdateRobot3D();
        };
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

    private void Initialize3DScene()
    {
        if (_is3DInitialized)
        {
            return;
        }

        _sceneGroup = new Model3DGroup();
        _sceneGroup.Children.Add(CreateLightingGroup());
        _sceneGroup.Children.Add(CreateFloorModel());

        var unitCapsule = CreateUnitLinkMesh();
        foreach (var linkName in new[] { "Link1", "Link2", "Link3", "Link4", "Link5", "Link6" })
        {
            var model = new GeometryModel3D(unitCapsule, CreateLinkMaterial(isCollision: false));
            _sceneGroup.Children.Add(model);
            _linkVisuals.Add(new LinkVisualBinding(linkName, model));

            var housingModel = new GeometryModel3D(unitCapsule, CreateJointHousingMaterial());
            _sceneGroup.Children.Add(housingModel);
            var capModel = new GeometryModel3D(unitCapsule, CreateJointCapMaterial());
            _sceneGroup.Children.Add(capModel);
            _jointVisuals.Add(new JointVisualBinding(linkName, housingModel, capModel));
        }

        _workpieceModel = CreateBoxModel(new Vector3(500, 300, 300), Color.FromRgb(77, 118, 189));
        _fenceModel = CreateBoxModel(new Vector3(2500, 50, 1800), Color.FromRgb(120, 120, 120));
        _sceneGroup.Children.Add(_workpieceModel);
        _sceneGroup.Children.Add(_fenceModel);

        var axisLength = (float)Simulation3DVisualStyle.TriadAxisLengthMm;
        SimViewport.Children.Add(CreateOriginAxisLine(Vector3.Zero, new Vector3(axisLength, 0, 0), Simulation3DVisualStyle.AxisXColor));
        SimViewport.Children.Add(CreateOriginAxisLine(Vector3.Zero, new Vector3(0, axisLength, 0), Simulation3DVisualStyle.AxisYColor));
        SimViewport.Children.Add(CreateOriginAxisLine(Vector3.Zero, new Vector3(0, 0, axisLength), Simulation3DVisualStyle.AxisZColor));
        _originToTcpLine = CreateOriginAxisLine(Vector3.Zero, Vector3.Zero, Simulation3DVisualStyle.OriginToTcpLineColor, Simulation3DVisualStyle.OriginToTcpLineThickness);
        SimViewport.Children.Add(_originToTcpLine);

        SimViewport.Children.Add(new ModelVisual3D { Content = _sceneGroup });
        SimViewport.Camera = CreateCamera(Simulation3DVisualStyle.WorkCameraPosition, Simulation3DVisualStyle.CameraLookAt, Simulation3DVisualStyle.WorkCameraFieldOfViewDeg);
        _is3DInitialized = true;
    }

    private static Model3DGroup CreateLightingGroup()
    {
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(52, 58, 72)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(242, 246, 255), new Vector3D(-0.35, -0.55, -1)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(122, 136, 168), new Vector3D(0.42, 0.58, -0.62)));
        return lights;
    }

    private static GeometryModel3D CreateFloorModel()
    {
        var model = new GeometryModel3D(
            CreateBoxMesh(3000, 3000, 20),
            CreateMaterial(Simulation3DVisualStyle.FloorDiffuseColor, Simulation3DVisualStyle.FloorSpecularColor, 35));
        model.Transform = new TranslateTransform3D(0, 0, -10);
        return model;
    }

    private static System.Windows.Media.Media3D.MeshGeometry3D CreateUnitLinkMesh()
    {
        return CreateCylinderMesh(1, 1, 24);
    }

    private static GeometryModel3D CreateBoxModel(Vector3 size, Color color)
    {
        return new GeometryModel3D(CreateBoxMesh(size.X, size.Y, size.Z), CreateMaterial(color, Colors.White, 70));
    }

    private static MeshGeometry3D CreateBoxMesh(double width, double depth, double height)
    {
        var hx = width * 0.5;
        var hy = depth * 0.5;
        var hz = height * 0.5;
        var positions = new Point3DCollection
        {
            new(-hx, -hy, -hz), new(hx, -hy, -hz), new(hx, hy, -hz), new(-hx, hy, -hz),
            new(-hx, -hy, hz), new(hx, -hy, hz), new(hx, hy, hz), new(-hx, hy, hz),
        };
        var indices = new Int32Collection
        {
            0, 1, 2, 0, 2, 3, // bottom
            4, 6, 5, 4, 7, 6, // top
            0, 4, 5, 0, 5, 1, // front
            1, 5, 6, 1, 6, 2, // right
            2, 6, 7, 2, 7, 3, // back
            3, 7, 4, 3, 4, 0, // left
        };
        return new MeshGeometry3D { Positions = positions, TriangleIndices = indices };
    }

    private static MeshGeometry3D CreateCylinderMesh(double radius, double height, int segments)
    {
        var positions = new Point3DCollection();
        var indices = new Int32Collection();
        var normals = new Vector3DCollection();
        var halfHeight = height * 0.5;

        for (var i = 0; i <= segments; i++)
        {
            var angle = (2 * Math.PI * i) / segments;
            var x = radius * Math.Cos(angle);
            var y = radius * Math.Sin(angle);
            positions.Add(new Point3D(x, y, -halfHeight));
            positions.Add(new Point3D(x, y, halfHeight));
            var normal = new Vector3D(x, y, 0);
            normal.Normalize();
            normals.Add(normal);
            normals.Add(normal);
        }

        for (var i = 0; i < segments; i++)
        {
            var b0 = i * 2;
            var t0 = b0 + 1;
            var b1 = ((i + 1) * 2);
            var t1 = b1 + 1;
            indices.Add(b0);
            indices.Add(t0);
            indices.Add(t1);
            indices.Add(b0);
            indices.Add(t1);
            indices.Add(b1);
        }

        var bottomCenter = positions.Count;
        positions.Add(new Point3D(0, 0, -halfHeight));
        normals.Add(new Vector3D(0, 0, -1));
        var topCenter = positions.Count;
        positions.Add(new Point3D(0, 0, halfHeight));
        normals.Add(new Vector3D(0, 0, 1));

        for (var i = 0; i < segments; i++)
        {
            var b0 = i * 2;
            var b1 = ((i + 1) * 2);
            indices.Add(bottomCenter);
            indices.Add(b1);
            indices.Add(b0);

            var t0 = b0 + 1;
            var t1 = b1 + 1;
            indices.Add(topCenter);
            indices.Add(t0);
            indices.Add(t1);
        }

        return new MeshGeometry3D
        {
            Positions = positions,
            TriangleIndices = indices,
            Normals = normals,
        };
    }

    private static MaterialGroup CreateLinkMaterial(bool isCollision)
    {
        return isCollision
            ? CreateMaterial(Simulation3DVisualStyle.LinkCollisionColor, Colors.White, 110)
            : CreateMaterial(Simulation3DVisualStyle.LinkDiffuseColor, Simulation3DVisualStyle.LinkSpecularColor, 115);
    }

    private static MaterialGroup CreateJointHousingMaterial()
        => CreateMaterial(Simulation3DVisualStyle.JointHousingColor, Simulation3DVisualStyle.JointHousingSpecularColor, 120);

    private static MaterialGroup CreateJointCapMaterial()
        => CreateMaterial(Simulation3DVisualStyle.JointCapColor, Simulation3DVisualStyle.JointCapSpecularColor, 140);

    private static MaterialGroup CreateMaterial(Color diffuse, Color specular, double specularPower)
    {
        return new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(new SolidColorBrush(diffuse)),
                new SpecularMaterial(new SolidColorBrush(specular), specularPower),
            },
        };
    }

    private static LinesVisual3D CreateOriginAxisLine(Vector3 start, Vector3 end, Color color, double? thickness = null)
    {
        return new LinesVisual3D
        {
            Color = color,
            Thickness = thickness ?? Simulation3DVisualStyle.TriadAxisThickness,
            Points = new Point3DCollection
            {
                new(start.X, start.Y, start.Z),
                new(end.X, end.Y, end.Z),
            },
        };
    }

    private void JointSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_is3DInitialized)
        {
            return;
        }

        Joint1ValueTextBlock.Text = $"{Joint1Slider.Value:F1}°";
        Joint2ValueTextBlock.Text = $"{Joint2Slider.Value:F1}°";
        Joint3ValueTextBlock.Text = $"{Joint3Slider.Value:F1}°";
        Joint4ValueTextBlock.Text = $"{Joint4Slider.Value:F1}°";
        Joint5ValueTextBlock.Text = $"{Joint5Slider.Value:F1}°";
        Joint6ValueTextBlock.Text = $"{Joint6Slider.Value:F1}°";
        UpdateRobot3D();
    }

    private void RandomizeWorkpieceButton_OnClick(object sender, RoutedEventArgs e)
    {
        RandomizeScenePlacement();
        UpdateRobot3D();
    }

    private void RandomizeScenePlacement()
    {
        var useFixedSeed = FixedSeedCheckBox.IsChecked == true;
        var seed = 1234;
        if (useFixedSeed && !int.TryParse(SeedTextBox.Text, out seed))
        {
            SetAlarmText("Alarm: Fixed seed must be a valid integer.", true);
            WorkpieceTextBlock.Text = "Workpiece: fixed seed input invalid";
            return;
        }

        _workpiece = _scenePlacementService.CreateRandomWorkpiece(useFixedSeed, seed);
        _fence = _scenePlacementService.CreateFenceOppositeWorkpiece(_workpiece);

        if (_workpieceModel is not null)
        {
            _workpieceModel.Transform = CreateBoxTransform(_workpiece.Center, _workpiece.YawDeg);
        }

        if (_fenceModel is not null)
        {
            _fenceModel.Transform = CreateBoxTransform(_fence.Center, _fence.YawDeg);
        }

        WorkpieceTextBlock.Text = $"Workpiece: X={_workpiece.Center.X:F0}, Y={_workpiece.Center.Y:F0}, Z={_workpiece.Center.Z:F0}";
    }

    private static Transform3D CreateBoxTransform(Vector3 center, double yawDeg)
    {
        var rotate = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), yawDeg));
        var translate = new TranslateTransform3D(center.X, center.Y, center.Z);
        return new Transform3DGroup
        {
            Children =
            {
                rotate,
                translate,
            },
        };
    }

    private void UpdateRobot3D()
    {
        if (!_is3DInitialized)
        {
            return;
        }

        var joints = new[]
        {
            Joint1Slider.Value,
            Joint2Slider.Value,
            Joint3Slider.Value,
            Joint4Slider.Value,
            Joint5Slider.Value,
            Joint6Slider.Value,
        };

        var robotState = _robotModel.Compute(joints);
        var collidingLinks = CollisionDetector.DetectCollidingLinks(robotState.Links, [_workpiece, _fence]);
        var collidingSet = new HashSet<string>(collidingLinks, StringComparer.Ordinal);
        foreach (var (name, model) in _linkVisuals)
        {
            var link = robotState.Links.First(x => x.Name.Equals(name, StringComparison.Ordinal));
            model.Transform = CreateLinkTransform(link);
            model.Material = CreateLinkMaterial(collidingSet.Contains(name));
        }

        foreach (var (name, housingModel, capModel) in _jointVisuals)
        {
            var link = robotState.Links.First(x => x.Name.Equals(name, StringComparison.Ordinal));
            housingModel.Transform = CreateJointHousingTransform(link);
            capModel.Transform = CreateJointCapTransform(link);
        }

        if (_originToTcpLine is not null)
        {
            _originToTcpLine.Points = new Point3DCollection
            {
                new Point3D(0, 0, 0),
                new Point3D(robotState.TcpPosition.X, robotState.TcpPosition.Y, robotState.TcpPosition.Z),
            };
        }

        if (collidingLinks.Count > 0)
        {
            CollisionTextBlock.Text = $"Collision: {string.Join(", ", collidingLinks)}";
            CollisionTextBlock.Foreground = Brushes.Red;
        }
        else
        {
            CollisionTextBlock.Text = "Collision: None";
            CollisionTextBlock.Foreground = Brushes.LimeGreen;
        }
    }

    private static Transform3D CreateLinkTransform(RobotLinkTransform link)
    {
        var rotation = new System.Windows.Media.Media3D.Quaternion(link.Rotation.X, link.Rotation.Y, link.Rotation.Z, link.Rotation.W);
        return new Transform3DGroup
        {
            Children =
            {
                new ScaleTransform3D(link.RadiusMm, link.RadiusMm, link.LengthMm),
                new RotateTransform3D(new QuaternionRotation3D(rotation)),
                new TranslateTransform3D(link.Center.X, link.Center.Y, link.Center.Z),
            },
        };
    }

    private static Transform3D CreateJointHousingTransform(RobotLinkTransform link)
    {
        var rotation = new System.Windows.Media.Media3D.Quaternion(link.Rotation.X, link.Rotation.Y, link.Rotation.Z, link.Rotation.W);
        var housingLength = Math.Max(1.0, link.RadiusMm * Simulation3DVisualStyle.JointHousingLengthScale);
        return new Transform3DGroup
        {
            Children =
            {
                new ScaleTransform3D(link.RadiusMm * Simulation3DVisualStyle.JointHousingRadiusScale, link.RadiusMm * Simulation3DVisualStyle.JointHousingRadiusScale, housingLength),
                new RotateTransform3D(new QuaternionRotation3D(rotation)),
                new TranslateTransform3D(link.Start.X, link.Start.Y, link.Start.Z),
            },
        };
    }

    private static Transform3D CreateJointCapTransform(RobotLinkTransform link)
    {
        var rotation = new System.Windows.Media.Media3D.Quaternion(link.Rotation.X, link.Rotation.Y, link.Rotation.Z, link.Rotation.W);
        var axis = link.End - link.Start;
        if (axis.LengthSquared() < 1e-6f)
        {
            axis = Vector3.UnitZ;
        }

        axis = Vector3.Normalize(axis);
        var capCenter = link.Start + (axis * (float)(Simulation3DVisualStyle.JointCapThicknessMm * 0.5));
        return new Transform3DGroup
        {
            Children =
            {
                new ScaleTransform3D(link.RadiusMm * Simulation3DVisualStyle.JointCapRadiusScale, link.RadiusMm * Simulation3DVisualStyle.JointCapRadiusScale, Simulation3DVisualStyle.JointCapThicknessMm),
                new RotateTransform3D(new QuaternionRotation3D(rotation)),
                new TranslateTransform3D(capCenter.X, capCenter.Y, capCenter.Z),
            },
        };
    }

    private void FrontCameraButton_OnClick(object sender, RoutedEventArgs e) => ApplyCameraPreset(CameraPreset.Front);

    private void SideCameraButton_OnClick(object sender, RoutedEventArgs e) => ApplyCameraPreset(CameraPreset.Side);

    private void WorkCameraButton_OnClick(object sender, RoutedEventArgs e) => ApplyCameraPreset(CameraPreset.Work);

    private void CaptureFrontButton_OnClick(object sender, RoutedEventArgs e) => CapturePresetScreenshot(CameraPreset.Front);

    private void CaptureSideButton_OnClick(object sender, RoutedEventArgs e) => CapturePresetScreenshot(CameraPreset.Side);

    private void CaptureWorkButton_OnClick(object sender, RoutedEventArgs e) => CapturePresetScreenshot(CameraPreset.Work);

    private void ApplyCameraPreset(CameraPreset preset)
    {
        var (position, lookAt, fov) = preset switch
        {
            CameraPreset.Front => (new Point3D(2300, 0, 1100), new Point3D(0, 0, 600), 45d),
            CameraPreset.Side => (new Point3D(0, 2300, 1100), new Point3D(0, 0, 600), 45d),
            _ => (Simulation3DVisualStyle.WorkCameraPosition, Simulation3DVisualStyle.CameraLookAt, Simulation3DVisualStyle.WorkCameraFieldOfViewDeg),
        };

        SimViewport.Camera = CreateCamera(position, lookAt, fov);
    }

    private static PerspectiveCamera CreateCamera(Point3D position, Point3D lookAt, double fieldOfView)
    {
        var direction = lookAt - position;
        return new PerspectiveCamera(position, direction, new Vector3D(0, 0, 1), fieldOfView);
    }

    private void CapturePresetScreenshot(CameraPreset preset)
    {
        ApplyCameraPreset(preset);
        SimViewport.UpdateLayout();

        var width = Math.Max(1, (int)SimViewport.ActualWidth);
        var height = Math.Max(1, (int)SimViewport.ActualHeight);
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(SimViewport);

        var screenshotsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Screenshots");
        System.IO.Directory.CreateDirectory(screenshotsDir);
        var filePath = System.IO.Path.Combine(screenshotsDir, $"{preset}-{DateTime.UtcNow:yyyyMMdd-HHmmss-ffffff}.png");

        using var stream = System.IO.File.Create(filePath);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        encoder.Save(stream);
        WorkpieceTextBlock.Text = $"Workpiece: X={_workpiece.Center.X:F0}, Y={_workpiece.Center.Y:F0}, Z={_workpiece.Center.Z:F0} | Saved: {System.IO.Path.GetFileName(filePath)}";
    }

    protected override void OnClosed(EventArgs e)
    {
        _engine?.Dispose();
        base.OnClosed(e);
    }

    private enum CameraPreset
    {
        Front,
        Side,
        Work,
    }

    private readonly record struct LinkVisualBinding(string Name, GeometryModel3D Model);
    private readonly record struct JointVisualBinding(string Name, GeometryModel3D HousingModel, GeometryModel3D CapModel);
    private readonly record struct TrailPoint(DateTime Timestamp, Pose6 Pose);
}
