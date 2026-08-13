using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace Triumvirate;

/// <summary>One setting the suite can edit in a tool's config.json.</summary>
internal sealed record Setting(string Key, string Label, string Kind, string[]? Choices = null)
{
    // Kind: "bool", "choice" (string), "choiceInt", "text", "folder"
}

/// <summary>
/// A managed tool: where it lives, how to run it, how to stop it cleanly, and which
/// settings its config.json carries. The suite never merges the apps — each stays its
/// own process, its own crash domain, its own privileges. This is a front desk.
/// </summary>
internal sealed class Tool
{
    public required string Name { get; init; }
    public required string Repo { get; init; }
    public required string Exe { get; init; }
    public required string Blurb { get; init; }
    public required string ConfigPath { get; init; }
    public required Setting[] Settings { get; init; }

    /// <summary>Set when saving this tool's settings needs a caveat in the UI.</summary>
    public string? RestartWarning { get; init; }

    public string ProcessName => Path.GetFileNameWithoutExtension(Exe);
    private string QuitEventName => $@"Local\{ProcessName}.Quit";

    public static readonly Tool[] All =
    [
        new()
        {
            Name = "DejaVu",
            Repo = "blancodagoat/DejaVu",
            Exe = "DejaVu.exe",
            Blurb = "Instant replay. A rolling buffer of your screen; one key saves the clip.",
            ConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DejaVu", "config.json"),
            RestartWarning = "Applying restarts DejaVu, which resets the in-flight buffer.",
            Settings =
            [
                new("bufferMinutes", "Buffer length (minutes)", "choiceInt", ["5", "10", "15", "20", "25"]),
                new("quality", "Quality", "choice", ["Low", "Medium", "High"]),
                new("fps", "Frame rate", "choiceInt", ["30", "60", "90", "120", "144", "165", "240"]),
                new("saveHotkey", "Save hotkey", "text"),
                new("saveRoot", "Replays folder", "folder"),
                new("captureTarget", "Capture target (auto or \\\\.\\DISPLAY2)", "text"),
                new("systemAudio", "Record system audio", "bool"),
                new("appAudioOnly", "Captured app audio only", "bool"),
                new("saveSound", "Save sound", "bool"),
                new("clipCapGB", "Clip folder cap (GB, 0 = off)", "choiceInt", ["0", "10", "25", "50"]),
                new("showIndicator", "Corner indicator", "bool"),
                new("indicatorStyle", "Indicator style", "choice", ["dot", "icon"]),
                new("updateNotify", "Notify about new versions", "bool"),
            ],
        },
        new()
        {
            Name = "Memento",
            Repo = "blancodagoat/memento",
            Exe = "Memento.exe",
            Blurb = "Screenshots. Print Screen for a region, F8 for the display.",
            ConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Memento", "config.json"),
            Settings =
            [
                new("regionHotkey", "Region capture hotkey", "text"),
                new("fullDisplayHotkey", "Full display hotkey", "text"),
                new("saveRoot", "Screenshots folder", "folder"),
                new("updateNotify", "Notify about new versions", "bool"),
            ],
        },
        new()
        {
            Name = "Recite",
            Repo = "blancodagoat/recite",
            Exe = "Recite.exe",
            Blurb = "Copy text from anything on screen, offline OCR.",
            ConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Recite", "config.json"),
            Settings =
            [
                new("grabHotkey", "Grab hotkey", "text"),
                new("useWindows11Ocr", "Use the Windows 11 OCR model when present", "bool"),
                new("updateNotify", "Notify about new versions", "bool"),
            ],
        },
    ];

    /// <summary>Where downloads land when the tool isn't installed any other way.</summary>
    public string ManagedDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Triumvirate", "apps", Name);

    /// <summary>Scoop install first (it has its own updater), then our managed copy.</summary>
    public string? InstalledExe()
    {
        var scoop = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "scoop", "apps", Name.ToLowerInvariant(), "current", Exe);
        if (File.Exists(scoop))
        {
            return scoop;
        }

        var managed = Path.Combine(ManagedDir, Exe);
        return File.Exists(managed) ? managed : null;
    }

    public bool IsRunning() => Process.GetProcessesByName(ProcessName).Length > 0;

    public void Start()
    {
        var exe = InstalledExe();
        if (exe is not null && !IsRunning())
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
    }

    /// <summary>Clean exit through the tool's named quit event; a kill is the fallback
    /// (elevated tools deny the event to a non-elevated suite). Waits for the process
    /// to actually leave, because a restart that races the old instance goes nowhere.</summary>
    public async Task StopAsync()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        if (processes.Length == 0)
        {
            return;
        }

        bool signaled = false;
        try
        {
            if (EventWaitHandle.TryOpenExisting(QuitEventName, out var quit))
            {
                using (quit)
                {
                    quit.Set();
                }

                signaled = true;
            }
        }
        catch
        {
            // Access denied (elevated tool, unelevated suite) or a pre-quit-event build.
        }

        foreach (var process in processes)
        {
            using (process)
            {
                if (signaled && await Task.Run(() => process.WaitForExit(10000)))
                {
                    continue;
                }

                try
                {
                    process.Kill();
                    await Task.Run(() => process.WaitForExit(5000));
                }
                catch
                {
                    // Already gone, or elevated beyond our reach.
                }
            }
        }
    }

    public bool IsScoopInstall() =>
        InstalledExe()?.Contains(@"\scoop\apps\", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Newest released version, or null when GitHub is unreachable.</summary>
    public async Task<Version?> LatestVersionAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Triumvirate");
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString();
            return tag is ['v', .. var rest] && Version.TryParse(rest, out var version)
                ? Normalize(version)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public Version? InstalledVersionParsed()
    {
        var exe = InstalledExe();
        if (exe is null)
        {
            return null;
        }

        try
        {
            return Version.TryParse(FileVersionInfo.GetVersionInfo(exe).FileVersion, out var v)
                ? Normalize(v)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build);

    /// <summary>
    /// The whole update story with a human answer at the end: skips scoop-managed
    /// copies (their copy shadows anything we download), reports "up to date" instead
    /// of silently re-downloading, and restarts the tool when it replaced a running one.
    /// </summary>
    public async Task<string> UpdateAsync()
    {
        if (IsScoopInstall())
        {
            return "Managed by scoop; update with \"scoop update\"";
        }

        var latest = await LatestVersionAsync();
        if (latest is null)
        {
            return "Couldn't reach GitHub";
        }

        var installed = InstalledVersionParsed();
        if (installed is not null && installed >= latest)
        {
            return $"Up to date (v{latest})";
        }

        bool wasRunning = IsRunning();
        if (!await DownloadLatestAsync())
        {
            return "Download failed";
        }

        if (wasRunning)
        {
            await StopAsync();
            Start();
        }

        return installed is null ? $"Installed v{latest}" : $"Updated to v{latest}";
    }

    /// <summary>Downloads the newest release exe into the managed folder.</summary>
    public async Task<bool> DownloadLatestAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Triumvirate");
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() == Exe)
                {
                    var url = asset.GetProperty("browser_download_url").GetString()!;
                    Directory.CreateDirectory(ManagedDir);
                    var target = Path.Combine(ManagedDir, Exe);
                    var temp = target + ".new";
                    await using (var output = File.Create(temp))
                    await using (var input = await http.GetStreamAsync(url))
                    {
                        await input.CopyToAsync(output);
                    }

                    File.Move(temp, target, overwrite: true);
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public string? InstalledVersion()
    {
        var exe = InstalledExe();
        if (exe is null)
        {
            return null;
        }

        try
        {
            var v = FileVersionInfo.GetVersionInfo(exe).FileVersion;
            return Version.TryParse(v, out var parsed)
                ? $"{parsed.Major}.{parsed.Minor}.{parsed.Build}"
                : v;
        }
        catch
        {
            return null;
        }
    }
}
