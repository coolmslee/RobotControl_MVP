using Robot.Abstractions;

namespace Robot.Drivers.Sim;

public sealed class SimDriverFactory : IDriverFactory
{
    public string DriverId => "sim";

    public string DisplayName => "Simulation Driver";

    public IRobotDevice CreateDevice(IReadOnlyDictionary<string, string>? parameters = null)
        => new SimRobotDevice(parameters);
}
