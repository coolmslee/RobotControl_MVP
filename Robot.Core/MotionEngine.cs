using System.Collections.Concurrent;
using System.Numerics;
using Robot.Abstractions;

namespace Robot.Core;

public sealed class MotionEngine : IDisposable
{
    private readonly object _sync = new();
    private readonly ConcurrentQueue<MotionRequest> _queue = new();

    private Thread? _loopThread;
    private bool _runLoop;
    private MotionSegment? _activeSegment;
    private double _activeDistanceMm;
    private long _lastUiPublishTicks;

    private IRobotDevice? _device;
    private MachineConfig _config = MachineConfig.CreateDefault();

    public bool AlarmActive { get; private set; }

    public string AlarmMessage { get; private set; } = string.Empty;

    public Pose6 CurrentPose { get; private set; }

    public event EventHandler<Pose6>? PoseUpdated;

    public event EventHandler<string>? AlarmRaised;

    public event EventHandler<bool>? AlarmStateChanged;

    public void Configure(MachineConfig config)
    {
        _config = config;
    }

    public void AttachDevice(IRobotDevice device)
    {
        lock (_sync)
        {
            _device = device;
            CurrentPose = Pose6.FromArray(device.AxisPositions);
        }
    }

    public void Start()
    {
        if (_loopThread is { IsAlive: true })
        {
            return;
        }

        _runLoop = true;
        _loopThread = new Thread(RunLoop)
        {
            IsBackground = true,
            Name = "MotionEngineRealtimeLoop",
            Priority = ThreadPriority.Highest,
        };
        _loopThread.Start();
    }

    public void Stop()
    {
        _runLoop = false;
        _loopThread?.Join(TimeSpan.FromSeconds(2));
        _loopThread = null;
    }

    public bool MoveLinear(Pose6 target, double feedMmPerSec)
        => Enqueue(MotionType.Linear, null, target, feedMmPerSec);

    public bool MoveArc3D_3Point(Pose6 viaPosition, Pose6 target, double feedMmPerSec)
        => Enqueue(MotionType.Arc3D3Point, viaPosition, target, feedMmPerSec);

    public void ResetAlarm()
    {
        AlarmActive = false;
        AlarmMessage = string.Empty;
        AlarmStateChanged?.Invoke(this, false);
    }

    public void Dispose()
    {
        Stop();
    }

    private bool Enqueue(MotionType type, Pose6? via, Pose6 target, double feedMmPerSec)
    {
        if (feedMmPerSec <= 0)
        {
            return false;
        }

        if (AlarmActive)
        {
            return false;
        }

        _queue.Enqueue(new MotionRequest(type, via, target, feedMmPerSec));
        return true;
    }

    private void RunLoop()
    {
        var tickMs = Math.Max(1, _config.TickMs);
        var tickSpan = TimeSpan.FromMilliseconds(tickMs);
        var lastTick = DateTime.UtcNow;

        while (_runLoop)
        {
            var start = DateTime.UtcNow;
            var delta = start - lastTick;
            lastTick = start;

            Tick(Math.Max(0.0001, delta.TotalSeconds));

            var elapsed = DateTime.UtcNow - start;
            var remaining = tickSpan - elapsed;
            if (remaining > TimeSpan.Zero)
            {
                Thread.Sleep(remaining);
            }
        }
    }

    private void Tick(double deltaSeconds)
    {
        lock (_sync)
        {
            if (_device is null)
            {
                return;
            }

            if (_device.SafetyInputs.EStop && !AlarmActive)
            {
                TriggerAlarm("E-Stop active.");
            }

            if (!AlarmActive)
            {
                _activeSegment ??= DequeueSegment(CurrentPose);
                if (_activeSegment is not null)
                {
                    _activeDistanceMm += _activeSegment.FeedMmPerSec * deltaSeconds;
                    if (_activeDistanceMm >= _activeSegment.LengthMm)
                    {
                        CurrentPose = _activeSegment.Target;
                        _activeSegment = null;
                        _activeDistanceMm = 0;
                    }
                    else
                    {
                        CurrentPose = _activeSegment.Evaluate(_activeDistanceMm);
                    }

                    if (!CheckSoftLimits(CurrentPose))
                    {
                        TriggerAlarm("Soft limit violation.");
                    }
                }

                if (!AlarmActive)
                {
                    _device.SetAxisSetpoints(CurrentPose.ToArray(), _activeSegment?.FeedMmPerSec ?? 0);
                }
            }

            if (_device is IRealtimeTickable tickable)
            {
                tickable.Tick(deltaSeconds);
            }

            CurrentPose = Pose6.FromArray(_device.AxisPositions);
            PublishUiUpdateIfNeeded();
        }
    }

    private MotionSegment? DequeueSegment(Pose6 startPose)
    {
        if (!_queue.TryDequeue(out var request))
        {
            return null;
        }

        return request.Type switch
        {
            MotionType.Linear => MotionSegment.CreateLinear(startPose, request.Target, request.FeedMmPerSec),
            MotionType.Arc3D3Point => MotionSegment.CreateArc3D3Point(startPose, request.Via ?? startPose, request.Target, request.FeedMmPerSec),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private bool CheckSoftLimits(Pose6 pose)
    {
        foreach (AxisId axis in Enum.GetValues<AxisId>())
        {
            var value = pose.GetAxis(axis);
            var limit = _config.GetAxisLimit(axis);
            if (value < limit.Min || value > limit.Max)
            {
                return false;
            }
        }

        return true;
    }

    private void TriggerAlarm(string message)
    {
        AlarmActive = true;
        AlarmMessage = message;
        _activeSegment = null;
        _activeDistanceMm = 0;
        while (_queue.TryDequeue(out _))
        {
        }

        _device?.EmergencyStop();
        AlarmRaised?.Invoke(this, message);
        AlarmStateChanged?.Invoke(this, true);
    }

    private void PublishUiUpdateIfNeeded()
    {
        var uiHz = Math.Max(1, _config.UiUpdateHz);
        var nowTicks = DateTime.UtcNow.Ticks;
        var minTicks = TimeSpan.TicksPerSecond / uiHz;
        if (nowTicks - _lastUiPublishTicks < minTicks)
        {
            return;
        }

        _lastUiPublishTicks = nowTicks;
        PoseUpdated?.Invoke(this, CurrentPose);
    }

    private readonly record struct MotionRequest(MotionType Type, Pose6? Via, Pose6 Target, double FeedMmPerSec);

    private enum MotionType
    {
        Linear,
        Arc3D3Point,
    }

    private sealed class MotionSegment
    {
        private readonly Func<double, Pose6> _evaluate;

        private MotionSegment(double lengthMm, double feedMmPerSec, Pose6 target, Func<double, Pose6> evaluate)
        {
            LengthMm = Math.Max(0.000001, lengthMm);
            FeedMmPerSec = feedMmPerSec;
            Target = target;
            _evaluate = evaluate;
        }

        public double LengthMm { get; }

        public double FeedMmPerSec { get; }

        public Pose6 Target { get; }

        public Pose6 Evaluate(double distanceMm)
        {
            var normalized = Math.Clamp(distanceMm / LengthMm, 0, 1);
            return _evaluate(normalized);
        }

        public static MotionSegment CreateLinear(Pose6 start, Pose6 target, double feedMmPerSec)
        {
            var startPos = new Vector3((float)start.X, (float)start.Y, (float)start.Z);
            var targetPos = new Vector3((float)target.X, (float)target.Y, (float)target.Z);
            var length = Vector3.Distance(startPos, targetPos);

            return new MotionSegment(length, feedMmPerSec, target, t => new Pose6(
                Lerp(start.X, target.X, t),
                Lerp(start.Y, target.Y, t),
                Lerp(start.Z, target.Z, t),
                InterpolateShortestAngle(start.Rx, target.Rx, t),
                InterpolateShortestAngle(start.Ry, target.Ry, t),
                InterpolateShortestAngle(start.Rz, target.Rz, t)));
        }

        public static MotionSegment CreateArc3D3Point(Pose6 start, Pose6 via, Pose6 target, double feedMmPerSec)
        {
            var a = new Vector3((float)start.X, (float)start.Y, (float)start.Z);
            var b = new Vector3((float)via.X, (float)via.Y, (float)via.Z);
            var c = new Vector3((float)target.X, (float)target.Y, (float)target.Z);

            if (!TryBuildArc(a, b, c, out var arc))
            {
                return CreateLinear(start, target, feedMmPerSec);
            }

            return new MotionSegment(arc.Length, feedMmPerSec, target, t =>
            {
                var point = arc.Evaluate(t);
                return new Pose6(
                    point.X,
                    point.Y,
                    point.Z,
                    InterpolateShortestAngle(start.Rx, target.Rx, t),
                    InterpolateShortestAngle(start.Ry, target.Ry, t),
                    InterpolateShortestAngle(start.Rz, target.Rz, t));
            });
        }

        private static bool TryBuildArc(Vector3 start, Vector3 via, Vector3 target, out ArcData arc)
        {
            arc = default;

            var u = via - start;
            var v = target - start;
            var w = Vector3.Cross(u, v);
            var wLenSq = w.LengthSquared();
            if (wLenSq < 1e-8f)
            {
                return false;
            }

            var center = start
                         + ((u.LengthSquared() * Vector3.Cross(w, v))
                            + (v.LengthSquared() * Vector3.Cross(u, w)))
                         / (2f * wLenSq);

            var radius = Vector3.Distance(center, start);
            if (radius < 1e-6f)
            {
                return false;
            }

            var normal = Vector3.Normalize(w);
            var e1 = Vector3.Normalize(start - center);
            var e2 = Vector3.Normalize(Vector3.Cross(normal, e1));

            var startAngle = 0d;
            var viaAngle = GetAngle(via - center, e1, e2);
            var targetAngle = GetAngle(target - center, e1, e2);

            var ccwToVia = NormalizeToTwoPi(viaAngle - startAngle);
            var ccwToTarget = NormalizeToTwoPi(targetAngle - startAngle);
            var sweep = ccwToVia <= ccwToTarget
                ? ccwToTarget
                : ccwToTarget - (2 * Math.PI);

            arc = new ArcData(center, e1, e2, radius, startAngle, sweep);
            return true;
        }

        private static double GetAngle(Vector3 point, Vector3 e1, Vector3 e2)
            => Math.Atan2(Vector3.Dot(point, e2), Vector3.Dot(point, e1));

        private static double NormalizeToTwoPi(double angle)
        {
            var normalized = angle % (2 * Math.PI);
            return normalized < 0 ? normalized + (2 * Math.PI) : normalized;
        }

        private static double Lerp(double start, double end, double t) => start + ((end - start) * t);

        private static double InterpolateShortestAngle(double start, double end, double t)
        {
            var delta = end - start;
            while (delta > 180)
            {
                delta -= 360;
            }

            while (delta < -180)
            {
                delta += 360;
            }

            return start + (delta * t);
        }

        private readonly record struct ArcData(Vector3 Center, Vector3 E1, Vector3 E2, double Radius, double StartAngle, double Sweep)
        {
            public double Length => Math.Abs(Sweep) * Radius;

            public Vector3 Evaluate(double normalized)
            {
                var angle = StartAngle + (Sweep * normalized);
                return Center + (E1 * (float)(Math.Cos(angle) * Radius)) + (E2 * (float)(Math.Sin(angle) * Radius));
            }
        }
    }
}
