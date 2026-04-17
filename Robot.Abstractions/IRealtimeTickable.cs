namespace Robot.Abstractions;

public interface IRealtimeTickable
{
    void Tick(double deltaTimeSeconds);
}
