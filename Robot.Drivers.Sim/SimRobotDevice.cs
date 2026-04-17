using Robot.Abstractions;

namespace Robot.Drivers.Sim;

public sealed class SimRobotDevice : IRobotDevice, IRealtimeTickable, IWritableSafetyIo
{
    private readonly double[] _actual = new double[6];
    private readonly double[] _setpoint = new double[6];
    private readonly double[] _velocity = new double[6];
    private readonly double[] _maxVelocity;
    private readonly double[] _maxAcceleration;

    public SimRobotDevice(IReadOnlyDictionary<string, string>? parameters = null)
    {
        _maxVelocity = ParseArray(parameters, "maxVel", [200, 200, 200, 90, 90, 90]);
        _maxAcceleration = ParseArray(parameters, "maxAcc", [500, 500, 500, 360, 360, 360]);
    }

    public string Name => "SimRobotDevice";

    public SafetyIoState SafetyInputs => new(EStop, DoorOpen);

    public IReadOnlyList<double> AxisPositions => _actual;

    public bool EStop { get; set; }

    public bool DoorOpen { get; set; }

    public void SetAxisSetpoints(IReadOnlyList<double> axisTargets, double feedMmPerSec)
    {
        if (axisTargets.Count < 6)
        {
            return;
        }

        for (var i = 0; i < 6; i++)
        {
            _setpoint[i] = axisTargets[i];
        }
    }

    public void EmergencyStop()
    {
        Array.Clear(_velocity);
        for (var i = 0; i < 6; i++)
        {
            _setpoint[i] = _actual[i];
        }
    }

    public void Tick(double deltaTimeSeconds)
    {
        if (deltaTimeSeconds <= 0)
        {
            return;
        }

        for (var i = 0; i < 6; i++)
        {
            var delta = _setpoint[i] - _actual[i];
            var desiredVelocity = Math.Clamp(delta / deltaTimeSeconds, -_maxVelocity[i], _maxVelocity[i]);
            var maxVelocityStep = _maxAcceleration[i] * deltaTimeSeconds;

            if (_velocity[i] < desiredVelocity)
            {
                _velocity[i] = Math.Min(_velocity[i] + maxVelocityStep, desiredVelocity);
            }
            else
            {
                _velocity[i] = Math.Max(_velocity[i] - maxVelocityStep, desiredVelocity);
            }

            _actual[i] += _velocity[i] * deltaTimeSeconds;

            var remaining = _setpoint[i] - _actual[i];
            if ((remaining > 0 && _velocity[i] < 0) || (remaining < 0 && _velocity[i] > 0) || Math.Abs(remaining) < 1e-6)
            {
                _actual[i] = _setpoint[i];
                _velocity[i] = 0;
            }
        }
    }

    private static double[] ParseArray(IReadOnlyDictionary<string, string>? parameters, string key, double[] fallback)
    {
        if (parameters is null || !parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var values = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (values.Length != 6)
        {
            return fallback;
        }

        var result = new double[6];
        for (var i = 0; i < 6; i++)
        {
            if (!double.TryParse(values[i], out result[i]))
            {
                return fallback;
            }
        }

        return result;
    }
}
