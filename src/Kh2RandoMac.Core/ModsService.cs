using System.IO.Compression;
using LibGit2Sharp;
using OpenKh.Patcher;

namespace Kh2RandoMac.Core;

public class ModInfo
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required bool Enabled { get; init; }
    public Metadata? Metadata { get; init; }
}

public class ModsService
{
    private readonly Workspace _workspace;

    public ModsService(Workspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>
    /// Install from a GitHub "author/repo" reference, cloning like Mods Manager does.
    /// Returns the normalized mod name callers must use for enable/remove.
    /// </summary>
    public string InstallFromGit(string repo, Action<string>? progress = null)
    {
        repo = repo.Trim().TrimEnd('/');
        if (!System.Text.RegularExpressions.Regex.IsMatch(repo, @"^[A-Za-z0-9_.\-]+/[A-Za-z0-9_.\-]+$")
            || repo.Contains(".."))
            throw new ArgumentException($"'{repo}' is not an 'author/repo' reference (e.g. KH2FM-Mods-Num/GoA-ROM-Edition).");

        var modPath = _workspace.ModPath(repo);
        if (Directory.Exists(modPath))
            throw new InvalidOperationException($"Mod '{repo}' is already installed. Remove it first to reinstall.");

        Directory.CreateDirectory(Path.GetDirectoryName(modPath)!);
        progress?.Invoke($"Cloning https://github.com/{repo} ...");
        Repository.Clone($"https://github.com/{repo}.git", modPath, new CloneOptions { RecurseSubmodules = true });

        if (!File.Exists(Path.Combine(modPath, "mod.yml")))
        {
            Directory.Delete(modPath, true);
            throw new InvalidOperationException($"'{repo}' has no mod.yml, not an OpenKH mod.");
        }
        progress?.Invoke($"Installed {repo}.");
        return repo;
    }

    private static readonly string[] PcPatchExtensions = { ".kh2pcpatch" };

    /// <summary>
    /// Install a mod, randomizer seed, or .kh2pcpatch archive. Returns the mod name
    /// callers must use for enable/remove.
    /// </summary>
    public string InstallFromZip(string zipPath, Action<string>? progress = null)
    {
        var modName = Path.GetFileNameWithoutExtension(zipPath);
        var isPcPatch = PcPatchExtensions.Any(e => zipPath.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        using var zip = ZipFile.OpenRead(zipPath);
        if (!isPcPatch && zip.GetEntry("mod.yml") == null)
            throw new InvalidOperationException($"'{zipPath}' has no mod.yml at its root, not an OpenKH mod zip.");

        var modPath = _workspace.ModPath(modName);
        ReplaceExistingModDir(modPath, modName, progress);
        Directory.CreateDirectory(modPath);

        var modRoot = Path.GetFullPath(modPath);
        var patchAssets = new List<AssetFile>();
        foreach (var entry in zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
        {
            var relative = entry.FullName.Replace('\\', '/');
            if (isPcPatch)
            {
                // .kh2pcpatch layout: <package>/(original/)?<game path>. The package folder
                // becomes the asset's Package; "original" is skipped. Mirrors Mods Manager.
                var parts = relative.Split('/');
                var package = parts[0];
                var inner = string.Join('/', parts.Skip(parts.Length > 2 && parts[1] == "original" ? 2 : 1));
                if (inner.Length == 0)
                    continue;
                patchAssets.Add(new AssetFile
                {
                    Method = "copy",
                    Name = inner,
                    Package = package,
                    Platform = "pc",
                    Source = new List<AssetFile> { new() { Name = inner } },
                });
                relative = inner;
            }
            var dest = Path.GetFullPath(Path.Combine(modPath, relative));
            // Zip-slip guard: never extract outside the mod's own folder.
            if (!dest.StartsWith(modRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException($"Zip entry '{entry.FullName}' escapes the mod folder, refusing to extract.");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, true);
        }

        if (isPcPatch)
        {
            var metadata = new Metadata
            {
                Title = modName + " (KH2PCPATCH)",
                Game = "kh2",
                OriginalAuthor = "Unknown",
                Description = "Automatically generated metadata for this KH2PCPATCH modification.",
                Assets = patchAssets,
            };
            using var stream = File.Create(Path.Combine(modPath, "mod.yml"));
            metadata.Write(stream);
        }
        progress?.Invoke($"Installed {modName}.");
        return modName;
    }

    /// <summary>Install a standalone .lua script as a mod, like Mods Manager does.</summary>
    public string InstallFromLua(string luaPath, Action<string>? progress = null)
    {
        if (!luaPath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{luaPath}' is not a .lua file.");
        var modName = Path.GetFileNameWithoutExtension(luaPath);
        var modPath = _workspace.ModPath(modName);
        ReplaceExistingModDir(modPath, modName, progress);
        Directory.CreateDirectory(modPath);

        var luaFileName = Path.GetFileName(luaPath);
        File.Copy(luaPath, Path.Combine(modPath, luaFileName));

        // Pull title/author/description from LUAGUI_* headers when present.
        string title = modName;
        string? author = null, description = null;
        foreach (var line in File.ReadLines(Path.Combine(modPath, luaFileName)).Take(100))
        {
            if (!line.Contains("LUAGUI"))
                continue;
            var value = line.Substring(line.IndexOf('=') + 1).Replace("\"", "").Replace("'", "").Trim();
            if (line.StartsWith("LUAGUI_NAME")) title = value;
            else if (line.StartsWith("LUAGUI_AUTH")) author = value;
            else if (line.StartsWith("LUAGUI_DESC")) description = value;
        }

        var metadata = new Metadata
        {
            Title = title,
            Game = "kh2",
            OriginalAuthor = author,
            Description = description ?? "Automatically generated metadata for a standalone Lua script mod.",
            Assets = new List<AssetFile>
            {
                new()
                {
                    Name = $"scripts/{luaFileName}",
                    Method = "copy",
                    Source = new List<AssetFile> { new() { Name = luaFileName } },
                },
            },
        };
        using var stream = File.Create(Path.Combine(modPath, "mod.yml"));
        metadata.Write(stream);
        progress?.Invoke($"Installed {title}.");
        return modName;
    }

    /// <summary>
    /// Delete an existing install of this mod before reinstalling, but never delete a
    /// directory that isn't actually a mod (e.g. an author folder from git installs whose
    /// name happens to collide with a zip's filename).
    /// </summary>
    private void ReplaceExistingModDir(string modPath, string modName, Action<string>? progress)
    {
        if (!Directory.Exists(modPath))
            return;
        if (!File.Exists(Path.Combine(modPath, "mod.yml")))
            throw new InvalidOperationException(
                $"'{modName}' collides with an existing folder that isn't a mod (an author folder from a GitHub " +
                "install, most likely). Rename the file and try again.");
        progress?.Invoke($"Replacing existing '{modName}'...");
        Directory.Delete(modPath, true);
    }

    /// <summary>Git-installed mods with new commits on their remote (fetches each one).</summary>
    public List<(string Name, int CommitsBehind)> CheckForUpdates(Action<string>? progress = null)
    {
        var result = new List<(string, int)>();
        foreach (var name in _workspace.InstalledMods())
        {
            var path = _workspace.ModPath(name);
            if (!Repository.IsValid(path))
                continue;
            try
            {
                using var repo = new Repository(path);
                if (repo.Info.IsHeadDetached)
                    continue;
                FetchOrigin(repo);
                var behind = repo.Head.TrackingDetails.BehindBy ?? 0;
                if (behind > 0)
                    result.Add((name, behind));
            }
            catch (Exception ex)
            {
                progress?.Invoke($"Could not check {name}: {ex.Message}");
            }
        }
        return result;
    }

    /// <summary>Update a git-installed mod to the latest remote commit, like Mods Manager does.</summary>
    public void Update(string modName, Action<string>? progress = null)
    {
        var path = _workspace.ModPath(modName);
        if (!Repository.IsValid(path))
            throw new InvalidOperationException($"'{modName}' was not installed from GitHub, so it can't be updated this way.");
        using var repo = new Repository(path);
        if (repo.Info.IsHeadDetached)
            throw new InvalidOperationException($"'{modName}' is not on a branch; remove and reinstall it.");
        progress?.Invoke($"Updating {modName}...");
        FetchOrigin(repo);
        repo.Reset(ResetMode.Hard, repo.Head.TrackedBranch.Tip, new CheckoutOptions
        {
            CheckoutModifiers = CheckoutModifiers.Force,
        });
        progress?.Invoke($"{modName} updated.");
    }

    private static void FetchOrigin(Repository repo)
    {
        var remote = repo.Network.Remotes["origin"];
        Commands.Fetch(repo, remote.Name,
            remote.FetchRefSpecs.Select(r => r.Specification), new FetchOptions(), null);
    }

    public void Remove(string modName)
    {
        var modPath = _workspace.ModPath(modName);
        if (!Directory.Exists(modPath))
            throw new InvalidOperationException($"Mod '{modName}' is not installed.");
        Directory.Delete(modPath, true);
        // Drop from enabled list too.
        var enabled = _workspace.EnabledMods();
        if (enabled.RemoveAll(m => string.Equals(m, modName, StringComparison.OrdinalIgnoreCase)) > 0)
            _workspace.SaveEnabledMods(enabled);

        // Clean now-empty author folder.
        var parent = Path.GetDirectoryName(modPath)!;
        if (parent != _workspace.ModsDir && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
            Directory.Delete(parent);
    }

    public void SetEnabled(string modName, bool enabled)
    {
        var installed = _workspace.InstalledMods();
        var match = installed.FirstOrDefault(m => string.Equals(m, modName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Mod '{modName}' is not installed. Installed: {string.Join(", ", installed)}");

        var list = _workspace.EnabledMods();
        list.RemoveAll(m => string.Equals(m, match, StringComparison.OrdinalIgnoreCase));
        if (enabled)
            list.Insert(0, match);
        _workspace.SaveEnabledMods(list);
    }

    public List<ModInfo> List()
    {
        var enabled = _workspace.EnabledMods();
        var result = new List<ModInfo>();
        // Enabled mods first, in load order, then the rest.
        var installed = _workspace.InstalledMods();
        var ordered = enabled.Where(e => installed.Contains(e, StringComparer.OrdinalIgnoreCase))
            .Concat(installed.Where(i => !enabled.Contains(i, StringComparer.OrdinalIgnoreCase)));
        foreach (var name in ordered)
        {
            var path = _workspace.ModPath(name);
            Metadata? metadata = null;
            var ymlPath = Path.Combine(path, "mod.yml");
            if (File.Exists(ymlPath))
            {
                try
                {
                    using var stream = File.OpenRead(ymlPath);
                    metadata = Metadata.Read(stream);
                }
                catch
                {
                    // Leave metadata null; the mod list should still render.
                }
            }
            result.Add(new ModInfo
            {
                Name = name,
                Path = path,
                Enabled = enabled.Contains(name, StringComparer.OrdinalIgnoreCase),
                Metadata = metadata,
            });
        }
        return result;
    }
}
