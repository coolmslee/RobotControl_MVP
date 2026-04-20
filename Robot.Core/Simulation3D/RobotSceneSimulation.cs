using System.Numerics;

namespace Robot.Core.Simulation3D;

public sealed class Ur5LikeRobotModel
{
    private static readonly LinkDefinition[] LinkDefinitions =
    [
        // Visual collision/rendering radii (UR5-like approximation, not CAD-exact values).
        new("Link1", 65),
        new("Link2", 58),
        new("Link3", 50),
        new("Link4", 42),
        new("Link5", 35),
        new("Link6", 30),
    ];

    // Approximate UR5 dimensions in millimeters (standard DH-like layout)
    private static readonly DhParameter[] DhParameters =
    [
        new(0, 162.5, Math.PI / 2),
        new(-425, 0, 0),
        new(-392.2, 0, 0),
        new(0, 133.3, Math.PI / 2),
        new(0, 99.7, -Math.PI / 2),
        new(0, 99.6, 0),
    ];

    public RobotPoseState Compute(IReadOnlyList<double> jointAnglesDeg)
    {
        if (jointAnglesDeg.Count < 6)
        {
            throw new ArgumentException("Six joint angles are required.", nameof(jointAnglesDeg));
        }

        var origins = new Vector3[7];
        var current = Matrix4x4.Identity;
        origins[0] = Vector3.Zero;
        for (var i = 0; i < 6; i++)
        {
            var theta = DegreesToRadians(jointAnglesDeg[i]);
            current = current * CreateDhTransform(DhParameters[i], theta);
            origins[i + 1] = ExtractTranslation(current);
        }

        var links = new RobotLinkTransform[6];
        for (var i = 0; i < links.Length; i++)
        {
            links[i] = CreateLinkTransform(LinkDefinitions[i], origins[i], origins[i + 1]);
        }

        return new RobotPoseState(links, origins[^1]);
    }

    private static RobotLinkTransform CreateLinkTransform(LinkDefinition definition, Vector3 start, Vector3 end)
    {
        var axis = end - start;
        var length = axis.Length();
        if (length < 1e-6f)
        {
            axis = Vector3.UnitZ;
            length = 1e-6f;
        }

        var direction = Vector3.Normalize(axis);
        var rotation = QuaternionFromTo(Vector3.UnitZ, direction);
        return new RobotLinkTransform(
            definition.Name,
            (start + end) * 0.5f,
            rotation,
            length,
            definition.RadiusMm,
            start,
            end);
    }

    private static Matrix4x4 CreateDhTransform(DhParameter p, double theta)
    {
        return Matrix4x4.CreateRotationZ((float)theta)
               * Matrix4x4.CreateTranslation(0, 0, (float)p.D)
               * Matrix4x4.CreateTranslation((float)p.A, 0, 0)
               * Matrix4x4.CreateRotationX((float)p.AlphaRad);
    }

    private static Vector3 ExtractTranslation(Matrix4x4 matrix)
        => new(matrix.M41, matrix.M42, matrix.M43);

    private static Quaternion QuaternionFromTo(Vector3 from, Vector3 to)
    {
        var dot = Vector3.Dot(from, to);
        if (dot > 0.999999f)
        {
            return Quaternion.Identity;
        }

        if (dot < -0.999999f)
        {
            var orthogonal = Math.Abs(from.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            var axis = Vector3.Normalize(Vector3.Cross(from, orthogonal));
            return Quaternion.CreateFromAxisAngle(axis, (float)Math.PI);
        }

        var cross = Vector3.Cross(from, to);
        var s = MathF.Sqrt((1f + dot) * 2f);
        var invS = 1f / s;
        var q = new Quaternion(cross.X * invS, cross.Y * invS, cross.Z * invS, s * 0.5f);
        return Quaternion.Normalize(q);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    private readonly record struct DhParameter(double A, double D, double AlphaRad);

    private readonly record struct LinkDefinition(string Name, double RadiusMm);
}

public sealed class ScenePlacementService
{
    public SceneBox CreateRandomWorkpiece(bool useFixedSeed, int seed)
    {
        var random = useFixedSeed ? new Random(seed) : Random.Shared;
        var x = Lerp(300, 900, random.NextDouble());
        var y = Lerp(-400, 400, random.NextDouble());
        return new SceneBox(
            "Workpiece",
            new Vector3((float)x, (float)y, 150),
            new Vector3(500, 300, 300),
            0);
    }

    public SceneBox CreateFenceOppositeWorkpiece(SceneBox workpiece)
    {
        var p = new Vector2(workpiece.Center.X, workpiece.Center.Y);
        if (p.LengthSquared() < 1e-6f)
        {
            p = Vector2.UnitX;
        }

        var direction = -Vector2.Normalize(p);
        var center = direction * 1200;
        var normalToOrigin = -direction;
        var yaw = Math.Atan2(normalToOrigin.Y, normalToOrigin.X) * 180d / Math.PI;

        return new SceneBox(
            "SafetyFence",
            new Vector3(center, 900),
            new Vector3(2500, 50, 1800),
            yaw);
    }

    private static double Lerp(double min, double max, double t) => min + ((max - min) * t);
}

public static class CollisionDetector
{
    public static IReadOnlyList<string> DetectCollidingLinks(IReadOnlyList<RobotLinkTransform> links, IReadOnlyList<SceneBox> obstacles)
    {
        var colliding = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in links)
        {
            foreach (var obstacle in obstacles)
            {
                if (CapsuleIntersectsBox(link.Start, link.End, (float)link.RadiusMm, obstacle))
                {
                    colliding.Add(link.Name);
                }
            }
        }

        return colliding.ToList();
    }

    private static bool CapsuleIntersectsBox(Vector3 start, Vector3 end, float radius, SceneBox box)
    {
        var localStart = ToBoxLocal(start, box);
        var localEnd = ToBoxLocal(end, box);
        var half = box.SizeMm * 0.5f;
        var min = -half - new Vector3(radius, radius, radius);
        var max = half + new Vector3(radius, radius, radius);
        return SegmentIntersectsAabb(localStart, localEnd, min, max);
    }

    private static Vector3 ToBoxLocal(Vector3 world, SceneBox box)
    {
        var translated = world - box.Center;
        var yaw = (float)(-box.YawDeg * Math.PI / 180d);
        var cos = MathF.Cos(yaw);
        var sin = MathF.Sin(yaw);
        var x = (translated.X * cos) - (translated.Y * sin);
        var y = (translated.X * sin) + (translated.Y * cos);
        return new Vector3(x, y, translated.Z);
    }

    private static bool SegmentIntersectsAabb(Vector3 p0, Vector3 p1, Vector3 min, Vector3 max)
    {
        var tMin = 0f;
        var tMax = 1f;
        var d = p1 - p0;

        for (var axis = 0; axis < 3; axis++)
        {
            var p0Axis = axis switch
            {
                0 => p0.X,
                1 => p0.Y,
                _ => p0.Z,
            };
            var dAxis = axis switch
            {
                0 => d.X,
                1 => d.Y,
                _ => d.Z,
            };
            var minAxis = axis switch
            {
                0 => min.X,
                1 => min.Y,
                _ => min.Z,
            };
            var maxAxis = axis switch
            {
                0 => max.X,
                1 => max.Y,
                _ => max.Z,
            };

            if (Math.Abs(dAxis) < 1e-6f)
            {
                if (p0Axis < minAxis || p0Axis > maxAxis)
                {
                    return false;
                }

                continue;
            }

            var inv = 1f / dAxis;
            var t1 = (minAxis - p0Axis) * inv;
            var t2 = (maxAxis - p0Axis) * inv;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
            if (tMin > tMax)
            {
                return false;
            }
        }

        return true;
    }
}

public readonly record struct RobotPoseState(IReadOnlyList<RobotLinkTransform> Links, Vector3 TcpPosition);

public readonly record struct RobotLinkTransform(
    string Name,
    Vector3 Center,
    Quaternion Rotation,
    double LengthMm,
    double RadiusMm,
    Vector3 Start,
    Vector3 End);

public readonly record struct SceneBox(string Name, Vector3 Center, Vector3 SizeMm, double YawDeg);
