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

    public static async Task DownloadFile(string url, string destination)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var output = File.Create(destination);
        await response.Content.CopyToAsync(output);
    }
}
