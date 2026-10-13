using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmailAttachmentDownloader.Core;

/// <summary>The version this build identifies as, and a forgiving parser for version text.</summary>
public static class AppVersion
{
    private static readonly Regex Pattern = new(
        @"^\s*v?(\d+)\.(\d+)\.(\d+)(?:\.\d+)?(?:[-+].*)?$", RegexOptions.CultureInvariant);

    /// <summary>The version of the running app (CI stamps it from the release tag), or null if it cannot be read.</summary>
    public static Version? Current
    {
        get
        {
            Assembly assembly = typeof(AppVersion).Assembly;
            string? text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Version? parsed = Parse(text);
            if (parsed is not null)
            {
                return parsed;
            }

            Version? fallback = assembly.GetName().Version;
            return fallback is null ? null : new Version(fallback.Major, fallback.Minor, Math.Max(fallback.Build, 0));
        }
    }

    /// <summary>Reads <c>1.2.3</c>, <c>v1.2.3</c>, <c>1.2.3+abc123</c> or <c>1.2.3-beta</c> as 1.2.3. Anything else is null.</summary>
    public static Version? Parse(string? text)
    {
        if (text is null)
        {
            return null;
        }

        Match m = Pattern.Match(text);
        if (!m.Success
            || !int.TryParse(m.Groups[1].Value, out int major)
            || !int.TryParse(m.Groups[2].Value, out int minor)
            || !int.TryParse(m.Groups[3].Value, out int patch))
        {
            return null;
        }

        return new Version(major, minor, patch);
    }
}

/// <summary>A published release that can be installed.</summary>
public sealed record ReleaseInfo(
    Version Version,
    string Tag,
    bool IsPrerelease,
    string Notes,
    string PageUrl,
    Uri InstallerUrl,
    Uri ChecksumUrl);

/// <summary>What an update check found. <see cref="Update"/> is set only when a newer release exists.</summary>
public sealed record UpdateCheckResult(ReleaseInfo? Update, Version? Latest, string? Error)
{
    public bool Succeeded => Error is null;
}

/// <summary>Asks GitHub whether a newer release than the running version exists.</summary>
public sealed class UpdateChecker
{
    public const string DefaultRepository = "horinezachary/emailAttachmentDownloader";

    /// <summary>The release asset CI uploads (with a <c>.sha256</c> file next to it).</summary>
    public const string InstallerAsset = "EmailAttachmentDownloader-Setup.exe";

    private static readonly Regex TagPattern = new(@"^v\d+\.\d+\.\d+$", RegexOptions.CultureInvariant);

    private readonly HttpClient _http;
    private readonly Uri _endpoint;

    public UpdateChecker(HttpClient? http = null, string repository = DefaultRepository)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _endpoint = new Uri($"https://api.github.com/repos/{repository}/releases?per_page=30");
    }

    /// <summary>Never throws for network or format problems; they come back in <see cref="UpdateCheckResult.Error"/>.</summary>
    public async Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EmailAttachmentDownloader", current.ToString(3)));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string reason = response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests
                    ? "GitHub is limiting requests right now; try again later"
                    : $"GitHub answered {(int)response.StatusCode}";
                return new UpdateCheckResult(null, null, reason + ".");
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Evaluate(json, current);
        }
        catch (HttpRequestException ex)
        {
            return new UpdateCheckResult(null, null, "Could not reach GitHub: " + ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult(null, null, "GitHub did not answer in time.");
        }
        catch (JsonException)
        {
            return new UpdateCheckResult(null, null, "GitHub sent an answer that could not be read.");
        }
    }

    /// <summary>
    /// Picks the newest installable release. Pre-releases are only considered while the running version is itself
    /// a 0.x build, so someone on a stable 1.x release is never offered a pre-release.
    /// </summary>
    internal static UpdateCheckResult Evaluate(string json, Version current)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Expected a list of releases.");
        }

        bool allowPrerelease = current.Major == 0;
        ReleaseInfo? best = null;

        foreach (JsonElement release in document.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object || Flag(release, "draft"))
            {
                continue;
            }

            bool prerelease = Flag(release, "prerelease");
            if (prerelease && !allowPrerelease)
            {
                continue;
            }

            string? tag = Text(release, "tag_name");
            Version? version = tag is not null && TagPattern.IsMatch(tag) ? AppVersion.Parse(tag) : null;
            if (tag is null || version is null)
            {
                continue;
            }

            Uri? installer = null;
            Uri? checksum = null;
            if (release.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string? name = asset.ValueKind == JsonValueKind.Object ? Text(asset, "name") : null;
                    string? url = asset.ValueKind == JsonValueKind.Object ? Text(asset, "browser_download_url") : null;
                    if (name is null || url is null || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                    {
                        continue;
                    }

                    if (name == InstallerAsset)
                    {
                        installer = uri;
                    }
                    else if (name == InstallerAsset + ".sha256")
                    {
                        checksum = uri;
                    }
                }
            }

            // A release without both files cannot be installed safely, so it is not offered.
            if (installer is null || checksum is null)
            {
                continue;
            }

            if (best is null || version > best.Version)
            {
                best = new ReleaseInfo(
                    version, tag, prerelease, Text(release, "body") ?? "", Text(release, "html_url") ?? "", installer, checksum);
            }
        }

        return new UpdateCheckResult(best is not null && best.Version > current ? best : null, best?.Version, null);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}

public sealed class UpdateException : Exception
{
    public UpdateException(string message) : base(message)
    {
    }
}

/// <summary>Downloads a release's installer and refuses to hand it back unless it matches the published SHA-256.</summary>
public sealed class UpdateDownloader
{
    private const long MaxInstallerBytes = 300L * 1024 * 1024;

    private static readonly Regex HashPattern = new(@"^\s*([0-9a-fA-F]{64})(?:\s|$)", RegexOptions.CultureInvariant);

    private readonly HttpClient _http;

    public UpdateDownloader(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    /// <summary>Release files are only ever fetched over HTTPS from github.com (it then redirects to GitHub's file host).</summary>
    public static bool IsTrustedAssetUrl(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps && url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    /// <returns>The path of the verified installer inside <paramref name="directory"/>.</returns>
    /// <exception cref="UpdateException">The files are not trusted, malformed, too big, or do not match their checksum.</exception>
    public async Task<string> DownloadAsync(
        ReleaseInfo release, string directory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsTrustedAssetUrl(release.InstallerUrl) || !IsTrustedAssetUrl(release.ChecksumUrl))
        {
            throw new UpdateException("The release points at a download location the app does not trust, so it was not downloaded.");
        }

        string expected = await DownloadChecksumAsync(release.ChecksumUrl, cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(directory);
        string finalPath = Path.Combine(directory, UpdateChecker.InstallerAsset);
        string partialPath = finalPath + ".partial";

        string actual;
        try
        {
            using HttpResponseMessage response = await _http
                .GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException($"The installer download failed: GitHub answered {(int)response.StatusCode}.");
            }

            long? total = response.Content.Headers.ContentLength;
            if (total > MaxInstallerBytes)
            {
                throw new UpdateException("The installer is unexpectedly large, so it was not downloaded.");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long read = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    read += count;
                    if (read > MaxInstallerBytes)
                    {
                        throw new UpdateException("The installer is unexpectedly large, so the download was stopped.");
                    }

                    hash.AppendData(buffer, 0, count);
                    await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    if (total is > 0)
                    {
                        progress?.Report(Math.Min(1.0, (double)read / total.Value));
                    }
                }
            }

            actual = Convert.ToHexString(hash.GetHashAndReset());
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }

        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(partialPath);
            throw new UpdateException(
                "The downloaded installer does not match the checksum published with the release, so it was discarded.");
        }

        File.Move(partialPath, finalPath, overwrite: true);
        progress?.Report(1.0);
        return finalPath;
    }

    private async Task<string> DownloadChecksumAsync(Uri url, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new UpdateException($"The checksum download failed: GitHub answered {(int)response.StatusCode}.");
        }

        if (response.Content.Headers.ContentLength > 4096)
        {
            throw new UpdateException("The checksum file is not in the expected format.");
        }

        string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        Match m = HashPattern.Match(text);
        if (text.Length > 4096 || !m.Success)
        {
            throw new UpdateException("The checksum file is not in the expected format.");
        }

        return m.Groups[1].Value;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Update preferences, kept in their own small file.</summary>
public sealed class UpdateSettings
{
    /// <summary>Check at startup and once a day while running.</summary>
    public bool CheckAutomatically { get; set; } = true;

    /// <summary>A version the user chose "Skip this version" for; automatic checks stay quiet about it.</summary>
    public string? SkippedVersion { get; set; }
}

public sealed class UpdateSettingsStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private UpdateSettings _settings;

    public UpdateSettingsStore(string path)
    {
        _path = path;
        _settings = Read();
    }

    public UpdateSettings Current
    {
        get
        {
            lock (_gate)
            {
                return new UpdateSettings { CheckAutomatically = _settings.CheckAutomatically, SkippedVersion = _settings.SkippedVersion };
            }
        }
    }

    public void Update(Action<UpdateSettings> change)
    {
        lock (_gate)
        {
            change(_settings);
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_settings, Json.Options));
        }
    }

    private UpdateSettings Read()
    {
        if (!File.Exists(_path))
        {
            return new UpdateSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(_path), Json.Options) ?? new UpdateSettings();
        }
        catch (JsonException)
        {
            AtomicFile.QuarantineCorrupt(_path);
            return new UpdateSettings();
        }
    }
}
