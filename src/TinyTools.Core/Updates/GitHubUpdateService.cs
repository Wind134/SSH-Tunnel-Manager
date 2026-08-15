using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyTools.Core.Updates;

public sealed record ReleasePackage(
    string Name,
    Uri DownloadUri,
    long Size,
    string? ExpectedSha256,
    Uri? ChecksumUri);

public sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version LatestVersion,
    string TagName,
    Uri ReleasePage,
    string ReleaseNotes,
    bool IsUpdateAvailable,
    ReleasePackage? Package);

public sealed record UpdateDownloadProgress(long BytesReceived, long TotalBytes)
{
    public double Percentage => TotalBytes <= 0 ? 0 : BytesReceived * 100d / TotalBytes;
}

public sealed record UpdateDownloadResult(string FilePath, bool IntegrityVerified);

/// <summary>
/// Checks public GitHub Releases and downloads only explicitly named WinUI packages.
/// The legacy WPF artifacts intentionally do not match the package prefix.
/// </summary>
public sealed class GitHubUpdateService : IDisposable
{
    public const string RepositoryOwner = "Wind134";
    public const string RepositoryName = "SSH-Tunnel-Manager";
    public const string WinUiPackagePrefix = "TinyTools-WinUI-";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Uri _latestReleaseUri;

    public GitHubUpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _latestReleaseUri = new Uri(
            $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest");
    }

    public async Task<UpdateCheckResult> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        currentVersion = NormalizeVersion(currentVersion);
        using var request = CreateRequest(_latestReleaseUri);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
        ReleaseDto release = await JsonSerializer.DeserializeAsync<ReleaseDto>(
            content, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("GitHub Release 响应为空。");

        string tagName = release.TagName?.Trim() ?? string.Empty;
        Version latestVersion = ParseVersion(tagName)
            ?? throw new InvalidDataException($"无法识别 Release 版本号“{tagName}”。");
        Uri releasePage = CreateHttpsUri(release.HtmlUrl)
            ?? new Uri($"https://github.com/{RepositoryOwner}/{RepositoryName}/releases");
        ReleasePackage? package = SelectPackage(release.Assets ?? []);

        return new UpdateCheckResult(
            currentVersion,
            latestVersion,
            tagName,
            releasePage,
            release.Body ?? string.Empty,
            latestVersion > currentVersion,
            package);
    }

    public async Task<UpdateDownloadResult> DownloadAsync(
        ReleasePackage package,
        string destinationDirectory,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string fileName = Path.GetFileName(package.Name);
        if (!string.Equals(fileName, package.Name, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidDataException("Release 资产名称无效。");
        }

        string? expectedSha256 = NormalizeSha256(package.ExpectedSha256);
        if (expectedSha256 is null && package.ChecksumUri is not null)
            expectedSha256 = await DownloadChecksumAsync(package.ChecksumUri, cancellationToken);
        if (expectedSha256 is null)
            throw new InvalidDataException("Release 未提供可验证的 SHA-256 摘要，已拒绝下载。");

        Directory.CreateDirectory(destinationDirectory);
        string destinationPath = Path.Combine(destinationDirectory, fileName);
        string temporaryPath = destinationPath + ".download";

        try
        {
            using var request = CreateRequest(package.DownloadUri);
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? package.Size;
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                long bytesReceived = 0;
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    bytesReceived += bytesRead;
                    progress?.Report(new UpdateDownloadProgress(bytesReceived, totalBytes));
                }
            }

            string actualSha256;
            await using (var downloaded = File.OpenRead(temporaryPath))
            {
                actualSha256 = Convert.ToHexString(
                    await SHA256.HashDataAsync(downloaded, cancellationToken));
            }

            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"更新包 SHA-256 校验失败。期望 {expectedSha256}，实际 {actualSha256}。");
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            return new UpdateDownloadResult(destinationPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private async Task<string?> DownloadChecksumAsync(Uri checksumUri, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(checksumUri);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        return NormalizeSha256(text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
    }

    private static ReleasePackage? SelectPackage(IReadOnlyList<ReleaseAssetDto> assets)
    {
        ReleaseAssetDto? selected = assets
            .Where(IsCompatiblePackage)
            .OrderBy(asset => asset.Name!.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (selected is null)
            return null;

        Uri? downloadUri = CreateHttpsUri(selected.BrowserDownloadUrl);
        if (downloadUri is null)
            return null;

        string? digest = selected.Digest;
        string? expectedSha256 = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
            ? digest["sha256:".Length..]
            : null;
        ReleaseAssetDto? checksum = assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, selected.Name + ".sha256", StringComparison.OrdinalIgnoreCase));

        return new ReleasePackage(
            selected.Name!,
            downloadUri,
            selected.Size,
            NormalizeSha256(expectedSha256),
            CreateHttpsUri(checksum?.BrowserDownloadUrl));
    }

    private static bool IsCompatiblePackage(ReleaseAssetDto asset)
        => asset.Name?.StartsWith(WinUiPackagePrefix, StringComparison.OrdinalIgnoreCase) == true
            && (asset.Name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase)
                || asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    private static Version? ParseVersion(string tagName)
    {
        string value = tagName.Trim().TrimStart('v', 'V');
        int suffixIndex = value.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
            value = value[..suffixIndex];
        return Version.TryParse(value, out Version? version) ? NormalizeVersion(version) : null;
    }

    private static Version NormalizeVersion(Version version)
        => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static string? NormalizeSha256(string? value)
    {
        value = value?.Trim();
        if (value?.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            return null;
        return value.ToUpperInvariant();
    }

    private static Uri? CreateHttpsUri(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttps
                ? uri
                : null;

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TinyTools", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private sealed class ReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("assets")]
        public List<ReleaseAssetDto>? Assets { get; init; }
    }

    private sealed class ReleaseAssetDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }
    }
}
