using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Robot.App.Wpf;

internal static class Simulation3DVisualStyle
{
    public static readonly Color LinkDiffuseColor = Color.FromRgb(196, 200, 206);
    public static readonly Color LinkSpecularColor = Color.FromRgb(255, 255, 255);
    public static readonly Color LinkCollisionColor = Color.FromRgb(220, 40, 40);
    public static readonly Color JointHousingColor = Color.FromRgb(98, 102, 108);
    public static readonly Color JointHousingSpecularColor = Color.FromRgb(230, 232, 238);
    public static readonly Color JointCapColor = Color.FromRgb(0, 218, 255);
    public static readonly Color JointCapSpecularColor = Color.FromRgb(210, 255, 255);
    public static readonly Color OriginToTcpLineColor = Color.FromRgb(245, 245, 245);

    public static readonly Color FloorDiffuseColor = Color.FromRgb(28, 34, 44);
    public static readonly Color FloorSpecularColor = Color.FromRgb(76, 92, 118);

    public static readonly Color AxisXColor = Color.FromRgb(235, 77, 87);
    public static readonly Color AxisYColor = Color.FromRgb(90, 220, 122);
    public static readonly Color AxisZColor = Color.FromRgb(66, 170, 255);

    public const double JointHousingRadiusScale = 1.23;
    public const double JointHousingLengthScale = 1.05;
    public const double JointCapRadiusScale = 0.82;
    public const double JointCapThicknessMm = 9;
    public const double TriadAxisLengthMm = 190;
    public const double TriadAxisThickness = 2.2;
    public const double OriginToTcpLineThickness = 1.3;
    public const double WorkCameraFieldOfViewDeg = 42;

    public static readonly Point3D WorkCameraPosition = new(1500, -1280, 1180);
    public static readonly Point3D CameraLookAt = new(80, 0, 530);
}
