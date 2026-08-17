// Assertions for SuiteConfig: tool config management and suite state persistence.
//
//   dotnet run --project tests/Triumvirate.Tests 2>&1
//
// Exit code is 0 when everything passes.

using Triumvirate;
using System.Text.Json;
using System.Text.Json.Nodes;

int failed = 0, passed = 0;

void Check(string name, bool ok, string? detail = null)
{
    if (ok) { passed++; return; }
    failed++;
    Console.WriteLine($"FAIL  {name}{(detail is null ? "" : "  -> " + detail)}");
}

void Eq(string name, object? actual, object? expected) =>
    Check(name, Equals(actual, expected), $"expected <{expected}>, got <{actual}>");

// Test 1: SetToolValue on a fresh path creates the file with exactly the one key, correct JSON type
{
    var tempDir = Path.Combine(Path.GetTempPath(), "Triumvirate.Test1");
    Directory.CreateDirectory(tempDir);
    try
    {
        var configPath = Path.Combine(tempDir, "config.json");

        // bool type
        SuiteConfig.SetToolValue(configPath, new Setting("testBool", "Test Bool", "bool"), "True");
        var doc = JsonNode.Parse(File.ReadAllText(configPath));
        Check("bool value created as true",
            doc?["testBool"]?.GetValue<bool>() == true);

        // choiceInt type
        File.Delete(configPath);
        SuiteConfig.SetToolValue(configPath, new Setting("testInt", "Test Int", "choiceInt"), "60");
        doc = JsonNode.Parse(File.ReadAllText(configPath));
        Check("choiceInt value created as 60",
            doc?["testInt"]?.GetValue<int>() == 60);

        // string/text type
        File.Delete(configPath);
        SuiteConfig.SetToolValue(configPath, new Setting("testText", "Test Text", "text"), "auto");
        doc = JsonNode.Parse(File.ReadAllText(configPath));
        Check("text value created as 'auto'",
            doc?["testText"]?.GetValue<string>() == "auto");
    }
    finally
    {
        try { Directory.Delete(tempDir, true); } catch { }
    }
}

// Test 2: SetToolValue on existing config.json edits only target key and preserves others
{
    var tempDir = Path.Combine(Path.GetTempPath(), "Triumvirate.Test2");
    Directory.CreateDirectory(tempDir);
    try
    {
        var configPath = Path.Combine(tempDir, "config.json");

        // Create initial file with multiple keys
        var initialDoc = new JsonObject
        {
            ["key1"] = JsonValue.Create("value1"),
            ["key2"] = JsonValue.Create(42),
            ["key3"] = JsonValue.Create(true),
        };
        File.WriteAllText(configPath, initialDoc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        // Edit only key2
        SuiteConfig.SetToolValue(configPath, new Setting("key2", "Key 2", "choiceInt"), "99");

        // Re-parse and verify
        var doc = JsonNode.Parse(File.ReadAllText(configPath));
        Eq("key1 preserved", doc?["key1"]?.GetValue<string>(), "value1");
        Eq("key2 edited to 99", doc?["key2"]?.GetValue<int>(), 99);
        Eq("key3 preserved", doc?["key3"]?.GetValue<bool>(), true);
        Check("exactly three keys exist", doc is JsonObject obj && obj.Count == 3);
    }
    finally
    {
        try { Directory.Delete(tempDir, true); } catch { }
    }
}

// Test 3: GetToolValue round-trips what SetToolValue wrote
{
    var tempDir = Path.Combine(Path.GetTempPath(), "Triumvirate.Test3");
    Directory.CreateDirectory(tempDir);
    try
    {
        var configPath = Path.Combine(tempDir, "config.json");
        var setting = new Setting("testKey", "Test Key", "text");

        // Set a value
        SuiteConfig.SetToolValue(configPath, setting, "testValue");

        // Get it back
        var retrieved = SuiteConfig.GetToolValue(configPath, setting);
        Eq("GetToolValue retrieves SetToolValue result", retrieved, "testValue");

        // Missing file returns null
        File.Delete(configPath);
        var missing = SuiteConfig.GetToolValue(configPath, setting);
        Eq("GetToolValue returns null for missing file", missing, null);

        // Missing key returns null
        File.WriteAllText(configPath, "{}");
        var missingKey = SuiteConfig.GetToolValue(configPath, setting);
        Eq("GetToolValue returns null for missing key", missingKey, null);
    }
    finally
    {
        try { Directory.Delete(tempDir, true); } catch { }
    }
}

// Test 4: SetToolValue tolerates corrupt existing file
{
    var tempDir = Path.Combine(Path.GetTempPath(), "Triumvirate.Test4");
    Directory.CreateDirectory(tempDir);
    try
    {
        var configPath = Path.Combine(tempDir, "config.json");

        // Write corrupt JSON
        File.WriteAllText(configPath, "not json{{");

        // SetToolValue must not throw and must create valid JSON
        bool noThrow = false;
        try
        {
            SuiteConfig.SetToolValue(configPath, new Setting("newKey", "New Key", "text"), "newValue");
            noThrow = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DETAIL: SetToolValue threw: {ex.Message}");
        }

        Check("SetToolValue tolerates corrupt JSON", noThrow);

        // Verify the new key exists
        if (File.Exists(configPath))
        {
            var doc = JsonNode.Parse(File.ReadAllText(configPath));
            Check("corrupt file recovery creates key", doc?["newKey"] is not null);
        }
    }
    finally
    {
        try { Directory.Delete(tempDir, true); } catch { }
    }
}

// Test 5: SuiteConfig.Load/Save round-trip with protection of existing config
{
    // Get the hardcoded config path
    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    var configPath = Path.Combine(appDataPath, "Triumvirate", "config.json");

    // Backup existing file if it exists
    byte[]? backup = null;
    bool existedBefore = File.Exists(configPath);
    if (existedBefore)
    {
        backup = File.ReadAllBytes(configPath);
    }

    try
    {
        // Test: Load with no file returns empty set
        if (File.Exists(configPath))
        {
            File.Delete(configPath);
        }

        var config1 = SuiteConfig.Load();
        Check("Load with no file returns empty Enabled set", config1.Enabled.Count == 0);

        // Test: Save and Load round-trip
        config1.Enabled.Add("Tool1");
        config1.Enabled.Add("Tool2");
        config1.Enabled.Add("Tool3");
        config1.Save();

        var config2 = SuiteConfig.Load();
        Check("Load retrieves saved 'Tool1'", config2.Enabled.Contains("Tool1"));
        Check("Load retrieves saved 'Tool2'", config2.Enabled.Contains("Tool2"));
        Check("Load retrieves saved 'Tool3'", config2.Enabled.Contains("Tool3"));
        Eq("Load retrieves exactly 3 tools", config2.Enabled.Count, 3);
    }
    finally
    {
        // Restore original file or delete if it didn't exist
        try
        {
            if (existedBefore && backup is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                File.WriteAllBytes(configPath, backup);
            }
            else if (!existedBefore && File.Exists(configPath))
            {
                File.Delete(configPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Failed to restore config.json: {ex.Message}");
        }
    }
}

// Test 6: the elevation probe behind "restart it the way it was running"
{
    // Read through the token, our own process must agree with the identity API. A probe
    // that is quietly always-false is the dangerous outcome: it would restart every
    // elevated tool as a normal one and kill its hotkey in games without a word.
    Check("own process elevation matches WindowsPrincipal",
        Elevation.IsProcessElevated(Environment.ProcessId) == Elevation.IsElevated,
        $"probe said {Elevation.IsProcessElevated(Environment.ProcessId)}, "
            + $"identity says {Elevation.IsElevated}");

    // A pid that cannot exist stands in for "the process left while we looked": from a
    // normal suite that is indistinguishable from a refusal, and biasing toward elevated
    // costs a UAC prompt where the other way costs a silently broken hotkey.
    bool threw = false;
    bool gone = false;
    try
    {
        gone = Elevation.IsProcessElevated(-1);
    }
    catch (Exception ex)
    {
        threw = true;
        Console.WriteLine($"DETAIL: IsProcessElevated threw: {ex.Message}");
    }

    Check("a dead pid does not throw", !threw);
    Check("a dead pid answers, biased away from silent de-elevation", threw || gone != Elevation.IsElevated);
}

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
