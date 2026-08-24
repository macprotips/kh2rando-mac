using System.IO.Compression;

namespace Kh2RandoMac.Core;

/// <summary>
/// Installs Panacea (the in-game mod loader DLL) into the game folder inside the bottle.
/// Panacea itself stays a Windows DLL, the game loads it under CrossOver via the
/// version.dll hijack + a registry DLL override. Only its installation is done natively.
/// </summary>
public class PanaceaService
{
    public const string PanaceaDllName = "OpenKH.Panacea.dll";

    public static readonly string[] DependencyDlls =
    {
        "avcodec-vgmstream-59.dll",
        "avformat-vgmstream-59.dll",
        "avutil-vgmstream-57.dll",
        "bass.dll",
        "bass_vgmstream.dll",
        "libatrac9.dll",
        "libcelt-0061.dll",
        "libcelt-0110.dll",
        "libg719_decode.dll",
        "libmpg123-0.dll",
        "libspeex-1.dll",
        "libvorbis.dll",
        "swresample-vgmstream-4.dll",
    };

    private static string PayloadDir => Path.Combine(AppPaths.CacheDir, "openkh");
    private static string PayloadZip => Path.Combine(AppPaths.CacheDir, "openkh.zip");

    private static string VersionMarker => Path.Combine(PayloadDir, "VERSION.txt");

    /// <summary>The OpenKH release tag the cached payload came from, if any.</summary>
    public static string? PayloadVersion =>
        File.Exists(VersionMarker) ? File.ReadAllText(VersionMarker).Trim() : null;

    private static IEnumerable<string> AllPayloadFiles => DependencyDlls.Append(PanaceaDllName);

    /// <summary>The cache is only usable if every file made it, a partial cache must re-download.</summary>
    private static bool PayloadComplete =>
        AllPayloadFiles.All(f => File.Exists(Path.Combine(PayloadDir, f)));

    /// <summary>Download the official openkh release zip (which contains the prebuilt Panacea DLLs) if we don't have it.</summary>
    public async Task<string> EnsurePayload(Action<string>? progress = null)
    {
        if (PayloadComplete)
        {
            progress?.Invoke($"Using cached Panacea payload (OpenKH {PayloadVersion ?? "unknown version"}).");
            return PayloadDir;
        }

        progress?.Invoke("Fetching latest OpenKH release info...");
        var release = await GitHubApi.GetLatestRelease("OpenKH", "OpenKh");
        var asset = release.Assets.FirstOrDefault(a => a.Name.Equals("openkh.zip", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("openkh.zip not found in the latest OpenKH release.");

        progress?.Invoke($"Downloading OpenKH {release.Tag} ({asset.Size / 1024 / 1024} MB)...");
        await GitHubApi.DownloadFile(asset.DownloadUrl, PayloadZip);

        progress?.Invoke("Extracting Panacea files...");
        // Stage into a temp dir and validate before committing, so a bad or interrupted
        // extraction can never leave a half-cache that later runs mistake for complete.
        var staging = PayloadDir + ".staging";
        if (Directory.Exists(staging))
            Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        using (var zip = ZipFile.OpenRead(PayloadZip))
        {
            var wanted = AllPayloadFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries.Where(e => wanted.Contains(Path.GetFileName(e.FullName))))
                entry.ExtractToFile(Path.Combine(staging, Path.GetFileName(entry.FullName)), true);

            var missing = wanted.Where(w => !File.Exists(Path.Combine(staging, w))).ToList();
            if (missing.Count > 0)
            {
                Directory.Delete(staging, true);
                throw new InvalidOperationException($"openkh.zip is missing expected files: {string.Join(", ", missing)}");
            }
        }
        File.WriteAllText(Path.Combine(staging, "VERSION.txt"), release.Tag);
        if (Directory.Exists(PayloadDir))
            Directory.Delete(PayloadDir, true);
        Directory.Move(staging, PayloadDir);
        File.Delete(PayloadZip);
        return PayloadDir;
    }

    /// <summary>
    /// Copy Panacea into the game folder as version.dll (the loader name that works under
    /// Wine/CrossOver), its dependencies, and panacea_settings.txt pointing at the compiled
    /// mod folder, as a Windows path visible inside the bottle.
    /// </summary>
    public void Install(string gameDir, Bottle bottle, Workspace workspace)
    {
        var payload = PayloadDir;
        if (!PayloadComplete)
            throw new InvalidOperationException("Panacea payload not downloaded (or incomplete), run EnsurePayload first.");

        File.Copy(Path.Combine(payload, PanaceaDllName), Path.Combine(gameDir, "version.dll"), true);
        // Remove the alternate name if a previous install used it, so only one copy loads.
        var dbghelp = Path.Combine(gameDir, "DBGHELP.dll");
        if (File.Exists(dbghelp))
            File.Delete(dbghelp);

        var depDir = Path.Combine(gameDir, "dependencies");
        Directory.CreateDirectory(depDir);
        foreach (var dll in DependencyDlls)
            File.Copy(Path.Combine(payload, dll), Path.Combine(depDir, dll), true);

        var modPathWin = bottle.ToWindowsPath(workspace.CompiledModRoot);
        File.WriteAllLines(Path.Combine(gameDir, "panacea_settings.txt"), new[]
        {
            $"mod_path={modPathWin}",
            "show_console=false",
        });
    }

    public void Uninstall(string gameDir)
    {
        foreach (var f in new[] { "version.dll", "DBGHELP.dll", "panacea_settings.txt" })
        {
            var path = Path.Combine(gameDir, f);
            if (File.Exists(path))
                File.Delete(path);
        }
        var depDir = Path.Combine(gameDir, "dependencies");
        if (Directory.Exists(depDir))
            Directory.Delete(depDir, true);
    }

    public static bool IsInstalled(string gameDir) =>
        (File.Exists(Path.Combine(gameDir, "version.dll")) || File.Exists(Path.Combine(gameDir, "DBGHELP.dll")))
        && File.Exists(Path.Combine(gameDir, "panacea_settings.txt"));
}
