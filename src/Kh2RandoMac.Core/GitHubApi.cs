using System.Net.Http.Headers;
using System.Text.Json;

namespace Kh2RandoMac.Core;

public record ReleaseAsset(string Name, string DownloadUrl, long Size);
public record Release(string Tag, List<ReleaseAsset> Assets);

public static class GitHubApi
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        // Downloads (openkh.zip is ~75 MB) easily exceed HttpClient's default 100s
        // whole-request timeout on slow connections; disable it.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("kh2rando-mac", "1.0"));
        return client;
    }

    public static async Task<Release> GetLatestRelease(string owner, string repo)
    {
        var json = await Http.GetStringAsync($"https://api.github.com/repos/{owner}/{repo}/releases/latest");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(a => new ReleaseAsset(
                a.GetProperty("name").GetString()!,
                a.GetProperty("browser_download_url").GetString()!,
                a.GetProperty("size").GetInt64()))
            .ToList();
        return new Release(root.GetProperty("tag_name").GetString() ?? "?", assets);
    }

    /// <summary>
    /// Download to a temporary file and move it into place only once it is complete.
    /// Callers treat File.Exists(destination) as "already downloaded", so a connection
    /// dropped mid-download used to leave a truncated file that every retry accepted.
    /// </summary>
    /// <summary>
    /// Raised while a file downloads: the file name, bytes so far, and the total when
    /// the server declares one. An event rather than a callback argument because the
    /// services in between (Panacea, the tracker, Re:Fined) would each have to carry a
    /// parameter through solely to hand it back; only one download runs at a time.
    /// </summary>
    public static event Action<string, long, long?>? DownloadProgress;

    /// <summary>Report at most every quarter megabyte; the UI cannot use more.</summary>
    private const long ProgressStep = 256 * 1024;

    public static async Task DownloadFile(string url, string destination)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".part";
        try
        {
            var total = response.Content.Headers.ContentLength;
            var name = Path.GetFileName(destination);
            await using (var output = File.Create(partial))
            await using (var input = await response.Content.ReadAsStreamAsync())
            {
                var buffer = new byte[81920];
                long done = 0, reported = 0;
                int read;
                DownloadProgress?.Invoke(name, 0, total);
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    if (done - reported < ProgressStep)
                        continue;
                    reported = done;
                    DownloadProgress?.Invoke(name, done, total);
                }
                DownloadProgress?.Invoke(name, done, total);
            }

            var expected = response.Content.Headers.ContentLength;
            var actual = new FileInfo(partial).Length;
            if (expected.HasValue && actual != expected.Value)
                throw new IOException(
                    $"Download of {Path.GetFileName(destination)} was cut short " +
                    $"({actual} of {expected.Value} bytes). Check your connection and try again.");

            File.Move(partial, destination, true);
        }
        finally
        {
            if (File.Exists(partial))
                File.Delete(partial);
        }
    }
}
