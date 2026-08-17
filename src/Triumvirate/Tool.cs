using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace Triumvirate;

/// <summary>How a tool was running, so a restart can put it back exactly that way.</summary>
internal enum RunState
{
    Stopped,
    Normal,
    Elevated,
}

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

    /// <summary>Starts the tool the way the suite itself runs.</summary>
    public void Start() => Start(Elevation.IsElevated ? RunState.Elevated : RunState.Normal);

    /// <summary>
    /// Starts the tool in a given privilege state — <see cref="RunState.Stopped"/> starts
    /// nothing, so a restart can pass back whatever <see cref="StopAsync"/> found.
    /// </summary>
    public void Start(RunState state)
    {
        var exe = InstalledExe();
        if (state == RunState.Stopped || exe is null || IsRunning())
        {
            return;
        }

        if (state == RunState.Elevated && !Elevation.IsElevated)
        {
            // A UAC prompt, which is the honest cost of putting an elevated tool back:
            // starting it normally would leave its hotkey dead under elevated windows.
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
        }
        else if (state == RunState.Normal && Elevation.IsElevated)
        {
            // A child of an elevated suite inherits its token, so going back DOWN needs a
            // different parent. ponytail: explorer is the standard trick and needs no
            // P/Invoke; it costs the child handle and exit code, neither of which we use.
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exe}\"") { UseShellExecute = true });
        }
        else
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
    }

    /// <summary>Clean exit through the tool's named quit event; a kill is the fallback
    /// (elevated tools deny the event to a non-elevated suite). Waits for the process
    /// to actually leave, because a restart that races the old instance goes nowhere.
    /// Returns how it was running so the caller can start it back the same way — check
    /// <see cref="IsRunning"/> afterwards, since an elevated tool can refuse both the
    /// event and the kill.</summary>
    public async Task<RunState> StopAsync()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        if (processes.Length == 0)
        {
            return RunState.Stopped;
        }

        var state = Elevation.IsProcessElevated(processes[0]) ? RunState.Elevated : RunState.Normal;
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

        return state;
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
    /// The whole update story with a human answer at the end: reports "up to date"
    /// instead of silently re-downloading, drives whichever installer the tool came from,
    /// and puts the tool back exactly as it was found — running or not, elevated or not.
    /// </summary>
    public async Task<string> UpdateAsync()
    {
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

        // Down first, and only then swap: a running exe is locked, so both scoop and our
        // own File.Move fail against it — the old code downloaded first and reported a
        // "download failure" for what was really "the app is running".
        var state = await StopAsync();
        if (IsRunning())
        {
            // Elevated tool, normal suite: the quit event and the kill were both refused.
            return "Needs an elevated Triumvirate";
        }

        bool updated = IsScoopInstall() ? await ScoopUpdateAsync(latest) : await DownloadLatestAsync();
        if (updated)
        {
            await RemoveOldVersionsAsync();
        }

        // Back up before the outcome is reported, even on failure: the tool was running
        // when the user clicked, and leaving it down is the one unacceptable ending.
        Start(state);

        if (!updated)
        {
            return !IsScoopInstall() ? "Download failed"
                // scoop declines to run elevated at all, which reads as a plain failure
                // unless we say so.
                : Elevation.IsElevated ? "scoop won't run as administrator" : "scoop update failed";
        }

        return installed is null ? $"Installed v{latest}" : $"Updated to v{latest}";
    }

    /// <summary>
    /// Hands a scoop-installed tool to scoop, which owns that copy — anything we download
    /// beside it is shadowed by the shim anyway. The bucket refresh comes first because
    /// scoop only sees a new version once its bucket knows about one. ponytail: that
    /// refreshes every bucket, not just ours; scoop has no narrower switch, and "Update
    /// everything" paying for it three times is a second or two.
    /// </summary>
    private async Task<bool> ScoopUpdateAsync(Version latest)
    {
        var app = Name.ToLowerInvariant();
        await PowerShellAsync($"scoop update; scoop update {app}");

        // Verified by what landed on disk rather than by scoop's exit code, which stays 0
        // through plenty of things a user would call a failure.
        return InstalledVersionParsed() is { } now && now >= latest;
    }

    /// <summary>
    /// Old copies the updaters leave behind: scoop keeps every version it has ever
    /// installed (seven stale DejaVu folders on one real machine), and an interrupted
    /// download leaves a ".new" beside the managed exe.
    /// </summary>
    public async Task RemoveOldVersionsAsync()
    {
        if (IsScoopInstall())
        {
            await PowerShellAsync($"scoop cleanup {Name.ToLowerInvariant()}");
            return;
        }

        try
        {
            foreach (var leftover in Directory.EnumerateFiles(ManagedDir, "*.new"))
            {
                try
                {
                    File.Delete(leftover);
                }
                catch
                {
                    // Locked by a scanner; the next update tries again.
                }
            }
        }
        catch
        {
            // No managed folder at all — nothing to tidy.
        }
    }

    /// <summary>Runs a scoop command through Windows PowerShell (scoop is a .ps1 behind
    /// its shim). Best-effort: the caller checks the result on disk.</summary>
    private static async Task PowerShellAsync(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"{command}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
            {
                return;
            }

            // Drained before the wait: scoop is chatty enough to fill the pipe buffer and
            // block forever against a WaitForExit that then never returns.
            await process.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            // No PowerShell, no scoop, or it timed out; the version check is the verdict.
        }
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
