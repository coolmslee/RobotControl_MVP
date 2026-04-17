namespace Robot.Abstractions;

public readonly record struct SafetyIoState(bool EStop, bool DoorOpen);
