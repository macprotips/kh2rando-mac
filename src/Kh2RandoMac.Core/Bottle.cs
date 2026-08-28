using System.Diagnostics;

namespace Kh2RandoMac.Core;

/// <summary>Which Wine frontend a bottle belongs to. The prefix mechanics are identical.</summary>
public enum WinePlatform
{
    CrossOver,
    Sikarugir,
}

/// <summary>
/// A Wine prefix (a CrossOver bottle or a Sikarugir wrapper's prefix): drive mappings
/// and registry-level DLL overrides. Everything here is plain Wine and works the same
/// on both platforms.
/// </summary>
public class Bottle
{
    public required string Name { get; init; }
    public required string Root { get; init; }
    public WinePlatform Platform { get; init; } = WinePlatform.CrossOver;
    /// <summary>The wrapper .app for Sikarugir bottles; null for CrossOver.</summary>
    public string? WrapperApp { get; init; }

    public string DriveC => Path.Combine(Root, "drive_c");
    public string DosDevices => Path.Combine(Root, "dosdevices");
    public string UserReg => Path.Combine(Root, "user.reg");
    public string BottleConf => Path.Combine(Root, "cxbottle.conf");

    /// <summary>
    /// The CrossOver version that last updated this bottle, from cxbottle.conf. Used to
    /// run it with the matching CrossOver when both stable and Preview are installed.
    /// </summary>
    public string? CrossOverVersion
    {
        get
        {
            try
            {
                foreach (var line in File.ReadLines(BottleConf))
                {
                    var t = line.TrimStart();
                    if (!t.StartsWith("\"Version\"", StringComparison.Ordinal))
                        continue;
                    var parts = t.Split('=', 2);
                    if (parts.Length == 2)
                        return parts[1].Trim().Trim('"');
                }
            }
            catch
            {
                // No conf, or unreadable: fall back to the default CrossOver.
            }
            return null;
        }
    }

    /// <summary>
    /// An explicit CrossOver choice, when the user has more than one installed and
    /// picked one. Set by Resolve from the saved config.
    /// </summary>
    public string? PreferredApp { get; init; }

    /// <summary>The CrossOver app to run this bottle with.</summary>
    public string? OwningApp => CrossOverApp.AppPathForVersion(CrossOverVersion, PreferredApp);

    public static string BottlesRoot => CrossOverApp.BottlesRoot;

    /// <summary>The DLL overrides the KH2 mod stack needs, the single source of truth.</summary>
    public static readonly string[] RequiredOverrides = { "version", "dinput8", "LuaBackend" };

    /// <summary>All Wine prefixes on the system: CrossOver bottles plus Sikarugir wrappers.</summary>
    public static List<Bottle> Discover()
    {
        var result = new List<Bottle>();
        if (Directory.Exists(BottlesRoot))
        {
            result.AddRange(Directory.GetDirectories(BottlesRoot)
                .Where(d => Directory.Exists(Path.Combine(d, "drive_c")))
                .Select(d => new Bottle { Name = Path.GetFileName(d), Root = d })
                .OrderBy(b => b.Name));
        }
        result.AddRange(SikarugirApp.DiscoverWrappers());
        return result;
    }

    public static Bottle? Get(string name)
    {
        var root = Path.Combine(BottlesRoot, name);
        if (Directory.Exists(Path.Combine(root, "drive_c")))
            return new Bottle { Name = name, Root = root };
        return SikarugirApp.DiscoverWrappers()
            .FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reconstruct the configured bottle, preferring the stored wrapper path for Sikarugir.</summary>
    public static Bottle Resolve(AppConfig config)
    {
        if (config.BottleName == null)
            throw new InvalidOperationException("Not configured yet, run setup first.");
        if (config.WrapperApp != null)
        {
            var prefix = Path.Combine(config.WrapperApp, "Contents", "SharedSupport", "prefix");
            if (Directory.Exists(Path.Combine(prefix, "drive_c")))
                return new Bottle
                {
                    Name = config.BottleName,
                    Root = prefix,
                    Platform = WinePlatform.Sikarugir,
                    WrapperApp = config.WrapperApp,
                };
        }
        var bottle = Get(config.BottleName)
            ?? throw new InvalidOperationException($"Bottle '{config.BottleName}' not found.");
        return config.CrossOverAppPath == null
            ? bottle
            : new Bottle
            {
                Name = bottle.Name,
                Root = bottle.Root,
                Platform = bottle.Platform,
                WrapperApp = bottle.WrapperApp,
                PreferredApp = config.CrossOverAppPath,
            };
    }

    private Dictionary<char, string>? _driveMappings;

    /// <summary>Drive letter → mac path, resolved from the dosdevices symlinks. Cached per instance.</summary>
    public Dictionary<char, string> DriveMappings() => _driveMappings ??= ReadDriveMappings();

    private Dictionary<char, string> ReadDriveMappings()
    {
        var map = new Dictionary<char, string>();
        if (!Directory.Exists(DosDevices))
            return map;
        foreach (var entry in Directory.GetFileSystemEntries(DosDevices))
        {
            var name = Path.GetFileName(entry);
            if (name.Length != 2 || name[1] != ':')
                continue;
            var info = new FileInfo(entry);
            var target = info.LinkTarget;
            if (target == null)
                continue;
            if (!Path.IsPathRooted(target))
                target = Path.GetFullPath(Path.Combine(DosDevices, target));
            target = target.TrimEnd('/');
            map[char.ToUpper(name[0])] = target.Length == 0 ? "/" : target;
        }
        return map;
    }

    /// <summary>
    /// Translate a mac path into a Windows path visible inside the bottle,
    /// preferring the most specific (longest) drive mapping.
    /// </summary>
    public string ToWindowsPath(string macPath)
    {
        macPath = Path.GetFullPath(macPath);
        char bestDrive = '\0';
        string bestPrefix = "";
        foreach (var (drive, target) in DriveMappings())
        {
            var matches = target == "/"
                ? macPath.StartsWith("/", StringComparison.Ordinal)
                : macPath == target || macPath.StartsWith(target + "/", StringComparison.Ordinal);
            if (matches && (bestDrive == '\0' || target.Length > bestPrefix.Length))
            {
                bestDrive = drive;
                bestPrefix = target;
            }
        }
        if (bestDrive == '\0')
            throw new InvalidOperationException(
                $"No drive in bottle '{Name}' maps onto '{macPath}'. " +
                "Add a drive to the bottle in CrossOver, or move the folder under your home directory.");
        var rest = macPath.Substring(bestPrefix.Length).TrimStart('/').Replace('/', '\\');
        return rest.Length == 0 ? $"{bestDrive}:\\" : $"{bestDrive}:\\{rest}";
    }

    /// <summary>Translate a Windows path from inside the bottle to a mac path, or null if the drive is unknown.</summary>
    public string? ToMacPath(string windowsPath)
    {
        if (windowsPath.Length < 2 || windowsPath[1] != ':')
            return null;
        var map = DriveMappings();
        if (!map.TryGetValue(char.ToUpper(windowsPath[0]), out var root))
            return null;
        var rest = windowsPath.Substring(2).Replace('\\', '/').TrimStart('/');
        return rest.Length == 0 ? root : $"{root}/{rest}";
    }

    /// <summary>
    /// What to quit, for a message. The tracker runs inside the bottle and this app
    /// starts it, so it is the thing most likely to be holding a bottle open while the
    /// user is certain they quit everything: name it rather than listing Steam again.
    /// </summary>
    public string WhatIsUsingIt() =>
        TrackerService.IsTrackerRunning()
            ? "The item tracker is open, and it runs inside the bottle. Close it (and Steam and the game, if they are running)"
            : "Quit the game and Steam in CrossOver";

    public bool IsRunning()
    {
        // Wine's server keeps a unix socket at /tmp/.wine-{uid}/server-{dev:x}-{ino:x}/socket,
        // keyed by the prefix directory's device and inode, for exactly as long as the bottle
        // runs. Erring toward "running" is the safe direction: a false positive only asks the
        // user to quit the bottle; a false negative lets wineserver clobber our registry edit.
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/stat")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("%d:%i");
            psi.ArgumentList.Add(Root);
            using var p = Process.Start(psi)!;
            var parts = p.StandardOutput.ReadToEnd().Trim().Split(':');
            p.WaitForExit(3000);
            if (parts.Length == 2 && long.TryParse(parts[0], out var dev) && long.TryParse(parts[1], out var ino))
            {
                // wineserver keeps this socket for exactly as long as the bottle runs,
                // and it is keyed by the prefix's own inode, so it answers for this
                // bottle rather than any bottle. When stat worked, trust it: falling
                // through to a process scan on a quiet bottle is what produced
                // "appears to be running" that no amount of quitting would clear.
                return File.Exists($"/tmp/.wine-{GetUid()}/server-{dev:x}-{ino:x}/socket");
            }
        }
        catch
        {
            // Fall through to the process scan.
        }

        // Only reached when the socket could not be checked at all. Match a single
        // process that names this bottle, rather than looking for a path and a wine
        // process anywhere in the list: that reported every bottle as running as soon
        // as any wine process was alive, and CrossOver leaves those behind for a while
        // after Steam quits.
        try
        {
            var psi = new ProcessStartInfo("/bin/ps", "-axo command") { RedirectStandardOutput = true };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output.Split('\n').Any(line =>
                (line.Contains(Root, StringComparison.Ordinal) ||
                 line.Contains($"--bottle {Name}", StringComparison.Ordinal)) &&
                (line.Contains("wine", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains(".exe", StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            return false;
        }
    }

    private static uint GetUid()
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/id", "-u") { RedirectStandardOutput = true };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(3000);
            return uint.TryParse(output, out var uid) ? uid : 501;
        }
        catch
        {
            return 501;
        }
    }

    /// <summary>What a program left behind after running inside the bottle.</summary>
    public record RunResult(int ExitCode, string Output, string Error)
    {
        /// <summary>
        /// Some CrossOver versions print a command's output on stderr rather than
        /// stdout (seen with `uninstaller --list`), so parsers want both.
        /// </summary>
        public string Combined => Output + "\n" + Error;

        public string ErrorTail(int max = 400) =>
            (Error.Length > max ? Error[^max..] : Error).Trim();
    }

    /// <summary>
    /// Run a Wine builtin (uninstaller, winecfg, reg) inside this bottle and wait.
    /// </summary>
    public RunResult RunBuiltin(params string[] args) => Run(args);

    /// <summary>
    /// Run a Windows executable, given by its mac path, inside this bottle and wait.
    /// </summary>
    public RunResult RunProgram(string macExePath, params string[] args) =>
        Run(new[] { ToWindowsPath(macExePath) }.Concat(args).ToArray());

    private RunResult Run(string[] args)
    {
        var psi = new ProcessStartInfo(CrossOverApp.BinIn("wine", OwningApp))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--bottle");
        psi.ArgumentList.Add(Name);
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        // Both pipes have to be drained at once. Wine writes a steady stream of
        // fixme/err chatter to stderr, so reading stdout to completion first
        // deadlocks the moment that pipe fills, which hangs multi-minute installs
        // with no way out but force quit.
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return new RunResult(p.ExitCode,
            output.GetAwaiter().GetResult(),
            error.GetAwaiter().GetResult());
    }

    private const string OverridesSection = "[Software\\\\Wine\\\\DllOverrides]";

    /// <summary>DLL overrides currently present in user.reg (name → mode).</summary>
    public Dictionary<string, string> GetDllOverrides()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(UserReg))
            return result;
        var inSection = false;
        foreach (var line in File.ReadLines(UserReg))
        {
            if (line.StartsWith("["))
            {
                inSection = line.StartsWith(OverridesSection, StringComparison.Ordinal);
                continue;
            }
            if (!inSection)
                continue;
            var trimmed = line.Trim();
            if (trimmed.StartsWith("\""))
            {
                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2)
                    result[parts[0].Trim('"')] = parts[1].Trim().Trim('"');
            }
        }
        return result;
    }

    /// <summary>
    /// Ensure the given DLLs are overridden as native,builtin in the bottle registry.
    /// Edits user.reg directly; refuses while the bottle is running (wineserver would
    /// overwrite the file on shutdown).
    /// </summary>
    public void EnsureDllOverrides(IEnumerable<string> dllNames)
    {
        var wanted = dllNames.ToList();
        var current = GetDllOverrides();
        var missing = wanted.Where(w =>
            !current.TryGetValue(w, out var mode) ||
            !(mode.Contains("native"))).ToList();
        if (missing.Count == 0)
            return;

        if (IsRunning())
            throw new InvalidOperationException(
                $"{WhatIsUsingIt()}, then try again. Wine would otherwise overwrite the change.");

        var lines = File.Exists(UserReg)
            ? File.ReadAllLines(UserReg).ToList()
            : new List<string> { "WINE REGISTRY Version 2", ";; All keys relative to \\\\User\\\\S-1-5-21-0-0-0-1000", "" };

        var epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sectionIdx = lines.FindIndex(l => l.StartsWith(OverridesSection, StringComparison.Ordinal));
        if (sectionIdx < 0)
        {
            // Wine keeps sections sorted; insert alphabetically among top-level [Software\\...] keys.
            var insertAt = lines.Count;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith("[") &&
                    string.CompareOrdinal(lines[i], OverridesSection) > 0)
                {
                    insertAt = i;
                    break;
                }
            }
            var block = new List<string> { $"{OverridesSection} {epoch}" };
            block.AddRange(missing.Select(m => $"\"{m}\"=\"native,builtin\""));
            block.Add("");
            lines.InsertRange(insertAt, block);
        }
        else
        {
            // Find end of section (next section header or EOF), replace/insert values.
            var end = sectionIdx + 1;
            while (end < lines.Count && !lines[end].StartsWith("["))
                end++;
            // Keep new values inside the section, before its trailing blank separator lines.
            while (end > sectionIdx + 1 && string.IsNullOrWhiteSpace(lines[end - 1]))
                end--;
            foreach (var m in missing)
            {
                var existing = -1;
                for (var i = sectionIdx + 1; i < end; i++)
                {
                    if (lines[i].TrimStart().StartsWith($"\"{m}\"", StringComparison.OrdinalIgnoreCase))
                    {
                        existing = i;
                        break;
                    }
                }
                if (existing >= 0)
                    lines[existing] = $"\"{m}\"=\"native,builtin\"";
                else
                {
                    lines.Insert(end, $"\"{m}\"=\"native,builtin\"");
                    end++;
                }
            }
        }

        if (File.Exists(UserReg))
            File.Copy(UserReg, UserReg + ".kh2rando.bak", true);
        File.WriteAllLines(UserReg, lines);
    }

    /// <summary>Remove the given DLL overrides from the bottle registry (the reverse of EnsureDllOverrides).</summary>
    public void RemoveDllOverrides(IEnumerable<string> dllNames)
    {
        if (!File.Exists(UserReg))
            return;
        var wanted = dllNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(UserReg).ToList();
        var sectionIdx = lines.FindIndex(l => l.StartsWith(OverridesSection, StringComparison.Ordinal));
        if (sectionIdx < 0)
            return;
        var end = sectionIdx + 1;
        while (end < lines.Count && !lines[end].StartsWith("["))
            end++;
        bool IsWantedOverrideLine(int i) =>
            i > sectionIdx && i < end &&
            wanted.Any(w => lines[i].TrimStart().StartsWith($"\"{w}\"", StringComparison.OrdinalIgnoreCase));

        if (!Enumerable.Range(0, lines.Count).Any(IsWantedOverrideLine))
            return;

        if (IsRunning())
            throw new InvalidOperationException(
                $"{WhatIsUsingIt()}, then try again.");

        var kept = Enumerable.Range(0, lines.Count)
            .Where(i => !IsWantedOverrideLine(i))
            .Select(i => lines[i])
            .ToList();
        File.Copy(UserReg, UserReg + ".kh2rando.bak", true);
        File.WriteAllLines(UserReg, kept);
    }
}
