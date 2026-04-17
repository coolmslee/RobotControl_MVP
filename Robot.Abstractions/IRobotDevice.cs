namespace Robot.Abstractions;

public interface IRobotDevice
{
    string Name { get; }

    SafetyIoState SafetyInputs { get; }

    IReadOnlyList<double> AxisPositions { get; }

    void SetAxisSetpoints(IReadOnlyList<double> axisTargets, double feedMmPerSec);

    void EmergencyStop();
}
