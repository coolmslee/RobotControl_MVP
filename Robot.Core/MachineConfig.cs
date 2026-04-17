using System.Text.Json.Serialization;
using Robot.Abstractions;

namespace Robot.Core;

public sealed class MachineConfig
{
    public int TickMs { get; set; } = 1;

    public int UiUpdateHz { get; set; } = 30;

    public List<AxisLimitConfig> AxisLimits { get; set; } = CreateDefaultAxisLimits();

    public List<DeviceConfig> Devices { get; set; } =
    [
        new DeviceConfig
        {
            Name = "Sim Device",
            DriverId = "sim",
            Parameters = new Dictionary<string, string>(),
        },
    ];

    public AxisLimitConfig GetAxisLimit(AxisId axis)
        => AxisLimits.FirstOrDefault(x => x.Axis == axis)
            ?? throw new InvalidOperationException($"Missing soft limit for {axis}.");

    public static MachineConfig CreateDefault() => new();

    private static List<AxisLimitConfig> CreateDefaultAxisLimits() =>
    [
        new() { Axis = AxisId.Axis1, Name = "X", Unit = "mm", Min = -1000, Max = 1000 },
        new() { Axis = AxisId.Axis2, Name = "Y", Unit = "mm", Min = -1000, Max = 1000 },
        new() { Axis = AxisId.Axis3, Name = "Z", Unit = "mm", Min = -1000, Max = 1000 },
        new() { Axis = AxisId.Axis4, Name = "Rx", Unit = "deg", Min = -180, Max = 180 },
        new() { Axis = AxisId.Axis5, Name = "Ry", Unit = "deg", Min = -180, Max = 180 },
        new() { Axis = AxisId.Axis6, Name = "Rz", Unit = "deg", Min = -180, Max = 180 },
    ];
}

public sealed class AxisLimitConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AxisId Axis { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public double Min { get; set; }

    public double Max { get; set; }
}

public sealed class DeviceConfig
{
    public string Name { get; set; } = string.Empty;

    public string DriverId { get; set; } = string.Empty;

    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
