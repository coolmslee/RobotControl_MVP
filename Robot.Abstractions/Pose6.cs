namespace Robot.Abstractions;

public readonly record struct Pose6(double X, double Y, double Z, double Rx, double Ry, double Rz)
{
    public double GetAxis(AxisId axis) => axis switch
    {
        AxisId.Axis1 => X,
        AxisId.Axis2 => Y,
        AxisId.Axis3 => Z,
        AxisId.Axis4 => Rx,
        AxisId.Axis5 => Ry,
        AxisId.Axis6 => Rz,
        _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null)
    };

    public double[] ToArray() => [X, Y, Z, Rx, Ry, Rz];

    public static Pose6 FromArray(IReadOnlyList<double> values)
    {
        if (values.Count < 6)
        {
            throw new ArgumentException("Pose requires 6 axis values.", nameof(values));
        }

        return new Pose6(values[0], values[1], values[2], values[3], values[4], values[5]);
    }
}
