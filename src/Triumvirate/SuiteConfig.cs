using System.Text.Json;
using System.Text.Json.Nodes;

namespace Triumvirate;

/// <summary>The suite's own state: which tools the user keeps enabled.</summary>
internal sealed class SuiteConfig
{
    private static string PathName => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Triumvirate", "config.json");

    public HashSet<string> Enabled { get; } = [];

    public static SuiteConfig Load()
    {
        var config = new SuiteConfig();
        try
        {
            if (File.Exists(PathName))
            {
                var doc = JsonNode.Parse(File.ReadAllText(PathName));
                if (doc?["enabled"] is JsonArray list)
                {
                    foreach (var item in list)
                    {
                        if (item?.GetValue<string>() is { Length: > 0 } name)
                        {
                            config.Enabled.Add(name);
                        }
                    }
                }
            }
        }
        catch
        {
            // Defaults: nothing enabled until the user says so.
        }

        return config;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            var doc = new JsonObject
            {
                ["enabled"] = new JsonArray(Enabled.Select(n => JsonValue.Create(n)).ToArray()),
            };
            var temp = PathName + ".tmp";
            File.WriteAllText(temp, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, PathName, overwrite: true);
        }
        catch
        {
            // A lost toggle is re-toggleable.
        }
    }

    /// <summary>
    /// Edits one value in a TOOL's config.json, touching nothing else in the file. The
    /// tools tolerate missing keys and clamp bad values, so a partial file is safe.
    /// </summary>
    public static void SetToolValue(string configPath, Setting setting, string raw)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        JsonNode doc;
        try
        {
            doc = File.Exists(configPath)
                ? JsonNode.Parse(File.ReadAllText(configPath)) ?? new JsonObject()
                : new JsonObject();
        }
        catch
        {
            doc = new JsonObject();
        }

        doc[setting.Key] = setting.Kind switch
        {
            "bool" => JsonValue.Create(raw == "True" || raw == "true"),
            "choiceInt" => JsonValue.Create(int.TryParse(raw, out var n) ? n : 0),
            _ => JsonValue.Create(raw),
        };

        var temp = configPath + ".tmp";
        File.WriteAllText(temp, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, configPath, overwrite: true);
    }

    public static string? GetToolValue(string configPath, Setting setting)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                return null;
            }

            var node = JsonNode.Parse(File.ReadAllText(configPath))?[setting.Key];
            return node?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
