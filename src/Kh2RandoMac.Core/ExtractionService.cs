using OpenKh.Common;

namespace Kh2RandoMac.Core;

/// <summary>
/// Extracts KH2 game data from the PC release .hed/.pkg archives, a native adaptation of
/// Mods Manager's GameDataExtractionService (PC edition, KH2 only).
/// </summary>
public class ExtractionService
{
    private static readonly string[] Kh2Packages = { "first", "second", "third", "fourth", "fifth", "sixth" };

    public bool SkipRemastered { get; set; }

    public async Task ExtractKh2(
        string gameDir,
        string languageFolder,
        string gameDataLocation,
        Action<float>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        string HedPath(string name) => Path.Combine(gameDir, "Image", languageFolder, name);

        foreach (var pkg in Kh2Packages)
        {
            if (!File.Exists(HedPath($"kh2_{pkg}.hed")))
                throw new FileNotFoundException(
                    $"Missing '{HedPath($"kh2_{pkg}.hed")}'. Check the game folder and language folder settings.");
        }

        await Task.Run(() =>
        {
            var totalFiles = 0;
            foreach (var pkg in Kh2Packages)
            {
                using var stream = File.OpenRead(HedPath($"kh2_{pkg}.hed"));
                totalFiles += OpenKh.Egs.Hed.Read(stream).Count();
            }

            var processed = 0;
            var outputDir = Path.Combine(gameDataLocation, "kh2");
            foreach (var pkg in Kh2Packages)
            {
                using var hedStream = File.OpenRead(HedPath($"kh2_{pkg}.hed"));
                using var img = File.OpenRead(HedPath($"kh2_{pkg}.pkg"));

                foreach (var entry in OpenKh.Egs.Hed.Read(hedStream))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var hash = OpenKh.Egs.Helpers.ToString(entry.MD5);
                    if (!OpenKh.Egs.EgsTools.Names.TryGetValue(hash, out var fileName))
                        fileName = $"{hash}.dat";

                    var outputFileName = Path.Combine(outputDir, fileName);
                    OpenKh.Egs.EgsTools.CreateDirectoryForFile(outputFileName);

                    var hdAsset = new OpenKh.Egs.EgsHdAsset(img.SetPosition(entry.Offset));
                    File.Create(outputFileName).Using(stream => stream.Write(hdAsset.OriginalData));

                    if (!SkipRemastered)
                    {
                        var remasteredName = Path.Combine(outputDir, "remastered", fileName);
                        foreach (var asset in hdAsset.Assets)
                        {
                            var remasteredAsset = Path.Combine(OpenKh.Egs.EgsTools.GetHDAssetFolder(remasteredName), asset);
                            OpenKh.Egs.EgsTools.CreateDirectoryForFile(remasteredAsset);
                            var assetData = hdAsset.RemasteredAssetsDecompressedData[asset];
                            File.Create(remasteredAsset).Using(stream => stream.Write(assetData));
                        }
                    }

                    processed++;
                    onProgress?.Invoke((float)processed / totalFiles);
                }
            }
            onProgress?.Invoke(1f);
            WriteFingerprint(gameDir, languageFolder, gameDataLocation);
        }, cancellationToken);
    }

    /// <summary>Rough completeness check: extraction produced the KH2 system file.</summary>
    public static bool LooksExtracted(string gameDataLocation) =>
        File.Exists(Path.Combine(gameDataLocation, "kh2", "00objentry.bin")) ||
        Directory.Exists(Path.Combine(gameDataLocation, "kh2", "msg"));

    private static string FingerprintPath(string gameDataLocation) =>
        Path.Combine(gameDataLocation, "kh2", ".source-fingerprint");

    private static string ComputeFingerprint(string gameDir, string languageFolder) =>
        string.Join("\n", Kh2Packages.Select(pkg =>
        {
            var hed = Path.Combine(gameDir, "Image", languageFolder, $"kh2_{pkg}.hed");
            return $"kh2_{pkg}.hed:{(File.Exists(hed) ? new FileInfo(hed).Length : -1)}";
        }));

    /// <summary>Record which game files the extraction came from, so a game update can be detected.</summary>
    public static void WriteFingerprint(string gameDir, string languageFolder, string gameDataLocation) =>
        File.WriteAllText(FingerprintPath(gameDataLocation), ComputeFingerprint(gameDir, languageFolder));

    /// <summary>
    /// True when the game's archives no longer match the extraction they came from
    /// (a game update landed). Mods built against stale data can crash or misbehave;
    /// the fix is re-extracting. Unknown (no fingerprint) counts as not stale.
    /// </summary>
    public static bool IsExtractionStale(string gameDir, string languageFolder, string gameDataLocation)
    {
        var path = FingerprintPath(gameDataLocation);
        if (!File.Exists(path) || !Directory.Exists(Path.Combine(gameDir, "Image", languageFolder)))
            return false;
        return File.ReadAllText(path) != ComputeFingerprint(gameDir, languageFolder);
    }
}
