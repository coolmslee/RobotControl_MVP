using System.Text.Json;
using System.Text.Json.Serialization;

namespace Robot.Core;

public static class MachineConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    public static MachineConfig LoadOrCreateDefault(string path)
    {
        if (!File.Exists(path))
        {
            var defaultConfig = MachineConfig.CreateDefault();
            Save(path, defaultConfig);
            return defaultConfig;
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<MachineConfig>(stream, SerializerOptions)
               ?? throw new InvalidDataException("machine.json is invalid.");
    }

    public static void Save(string path, MachineConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        var json = JsonSerializer.Serialize(config, SerializerOptions);
        File.WriteAllText(path, json);
    }
}
