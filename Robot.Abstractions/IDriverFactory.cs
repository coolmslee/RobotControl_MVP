namespace Robot.Abstractions;

public interface IDriverFactory
{
    string DriverId { get; }

    string DisplayName { get; }

    IRobotDevice CreateDevice(IReadOnlyDictionary<string, string>? parameters = null);
}
