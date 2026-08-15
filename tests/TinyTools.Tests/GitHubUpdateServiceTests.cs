using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using TinyTools.Core.Updates;

namespace TinyTools.Tests;

public sealed class GitHubUpdateServiceTests
{
    [Fact]
    public async Task CheckSelectsOnlyWinUiReleasePackage()
    {
        byte[] packageBytes = Encoding.UTF8.GetBytes("verified WinUI package");
        string hash = Convert.ToHexString(SHA256.HashData(packageBytes));
        using var client = new HttpClient(new StubHandler(request =>
        {
            string json = $$"""
                {
                  "tag_name": "v2.0.0",
                  "html_url": "https://github.com/Wind134/SSH-Tunnel-Manager/releases/tag/v2.0.0",
                  "body": "release notes",
                  "assets": [
                    { "name": "TinyTools-v2.0.0-win-x64.zip", "browser_download_url": "https://example.test/wpf.zip", "size": 1 },
                    { "name": "TinyTools-WinUI-v2.0.0-win-x64.zip", "browser_download_url": "https://example.test/winui.zip", "size": {{packageBytes.Length}}, "digest": "sha256:{{hash}}" }
                  ]
                }
                """;
            return JsonResponse(json);
        }));
        using var service = new GitHubUpdateService(client);

        UpdateCheckResult result = await service.CheckAsync(new Version(1, 1, 0));

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(new Version(2, 0, 0), result.LatestVersion);
        Assert.Equal("TinyTools-WinUI-v2.0.0-win-x64.zip", result.Package?.Name);
        Assert.Equal(hash, result.Package?.ExpectedSha256);
    }

    [Fact]
    public async Task CheckPrefersWinUiInstallerAndItsChecksum()
    {
        using var client = new HttpClient(new StubHandler(_ => JsonResponse("""
            {
              "tag_name": "v1.1.0",
              "html_url": "https://github.com/Wind134/SSH-Tunnel-Manager/releases/tag/v1.1.0",
              "assets": [
                { "name": "TinyTools-WinUI-v1.1.0-win-x64.zip", "browser_download_url": "https://example.test/winui.zip", "size": 100 },
                { "name": "TinyTools-WinUI-v1.1.0-win-x64-Setup.exe", "browser_download_url": "https://example.test/setup.exe", "size": 200 },
                { "name": "TinyTools-WinUI-v1.1.0-win-x64-Setup.exe.sha256", "browser_download_url": "https://example.test/setup.exe.sha256", "size": 64 }
              ]
            }
            """)));
        using var service = new GitHubUpdateService(client);

        UpdateCheckResult result = await service.CheckAsync(new Version(1, 0, 0));

        Assert.Equal("TinyTools-WinUI-v1.1.0-win-x64-Setup.exe", result.Package?.Name);
        Assert.Equal(new Uri("https://example.test/setup.exe.sha256"), result.Package?.ChecksumUri);
    }

    [Fact]
    public async Task DownloadRejectsUnverifiedOrModifiedPackage()
    {
        byte[] packageBytes = Encoding.UTF8.GetBytes("modified package");
        var package = new ReleasePackage(
            "TinyTools-WinUI-v2.0.0-win-x64.zip",
            new Uri("https://example.test/winui.zip"),
            packageBytes.Length,
            new string('0', 64),
            null);
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(packageBytes),
            }));
        using var service = new GitHubUpdateService(client);
        string directory = CreateTemporaryDirectory();

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.DownloadAsync(package, directory));
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadVerifiesSha256BeforePublishingFile()
    {
        byte[] packageBytes = Encoding.UTF8.GetBytes("verified WinUI package");
        string hash = Convert.ToHexString(SHA256.HashData(packageBytes));
        var package = new ReleasePackage(
            "TinyTools-WinUI-v2.0.0-win-x64.zip",
            new Uri("https://example.test/winui.zip"),
            packageBytes.Length,
            hash,
            null);
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(packageBytes),
            }));
        using var service = new GitHubUpdateService(client);
        string directory = CreateTemporaryDirectory();

        try
        {
            UpdateDownloadResult result = await service.DownloadAsync(package, directory);

            Assert.True(result.IntegrityVerified);
            Assert.Equal(packageBytes, await File.ReadAllBytesAsync(result.FilePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "TinyTools.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
