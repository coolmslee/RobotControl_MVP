using System.Reflection;
using Robot.Abstractions;

namespace Robot.Core;

public sealed class DriverPluginLoader
{
    public DriverLoadResult LoadFactories(string driversDirectory)
    {
        var summary = new List<string>();
        var factories = new List<IDriverFactory>();

        if (!Directory.Exists(driversDirectory))
        {
            summary.Add($"Drivers directory not found: {driversDirectory}");
            return new DriverLoadResult(factories, summary);
        }

        foreach (var dllPath in Directory.GetFiles(driversDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dllPath);
                foreach (var type in assembly.GetTypes()
                             .Where(t => !t.IsAbstract && typeof(IDriverFactory).IsAssignableFrom(t)))
                {
                    if (Activator.CreateInstance(type) is IDriverFactory factory)
                    {
                        factories.Add(factory);
                        summary.Add($"{Path.GetFileName(dllPath)}: {factory.DriverId} ({factory.DisplayName})");
                    }
                }
            }
            catch (Exception ex)
            {
                summary.Add($"{Path.GetFileName(dllPath)}: load failed - {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (summary.Count == 0)
        {
            summary.Add("No driver factories discovered.");
        }

        return new DriverLoadResult(factories, summary);
    }
}

public sealed record DriverLoadResult(IReadOnlyList<IDriverFactory> Factories, IReadOnlyList<string> Summary);
