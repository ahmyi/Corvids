using System.IO;
using System.Text.Json;

namespace Corvids.Services;

/// <summary>Reads package.json in a folder to suggest a name and runnable script commands.</summary>
public static class PackageJsonReader
{
    public record Info(string? Name, IReadOnlyList<string> Commands);

    public static Info? Read(string directory)
    {
        var path = Path.Combine(directory, "package.json");
        if (!File.Exists(path)) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            var runner = DetectPackageManager(directory);
            var commands = new List<string>();

            if (root.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Object)
            {
                foreach (var script in scripts.EnumerateObject())
                {
                    commands.Add(script.Name == "start" && runner == "npm"
                        ? "npm start"
                        : $"{runner} run {script.Name}");
                }
            }

            return new Info(name, commands);
        }
        catch
        {
            return null;
        }
    }

    private static string DetectPackageManager(string directory)
    {
        if (File.Exists(Path.Combine(directory, "pnpm-lock.yaml"))) return "pnpm";
        if (File.Exists(Path.Combine(directory, "yarn.lock"))) return "yarn";
        if (File.Exists(Path.Combine(directory, "bun.lockb")) || File.Exists(Path.Combine(directory, "bun.lock")))
            return "bun";
        return "npm";
    }
}
