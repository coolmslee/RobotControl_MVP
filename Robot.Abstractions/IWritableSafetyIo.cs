namespace Robot.Abstractions;

public interface IWritableSafetyIo
{
    bool EStop { get; set; }

    bool DoorOpen { get; set; }
}
