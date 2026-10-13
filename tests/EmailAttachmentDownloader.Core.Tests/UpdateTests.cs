using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EmailAttachmentDownloader.Core.Tests;

internal sealed class RoutingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        return Task.FromResult(_respond(request));
    }
}

internal static class ReleaseJson
{
    public static object Release(
        string tag, bool prerelease = false, bool draft = false, bool installer = true, bool checksum = true, string body = "notes")
    {
        var assets = new List<object>();
        if (installer)
        {
            assets.Add(new { name = "EmailAttachmentDownloader-Setup.exe", browser_download_url = $"https://github.com/o/r/releases/download/{tag}/EmailAttachmentDownloader-Setup.exe" });
        }

        if (checksum)
        {
            assets.Add(new { name = "EmailAttachmentDownloader-Setup.exe.sha256", browser_download_url = $"https://github.com/o/r/releases/download/{tag}/EmailAttachmentDownloader-Setup.exe.sha256" });
        }

        return new { tag_name = tag, prerelease, draft, body, html_url = $"https://github.com/o/r/releases/tag/{tag}", assets };
    }

    public static string List(params object[] releases) => JsonSerializer.Serialize(releases);
}

public class AppVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v0.10.4", 0, 10, 4)]
    [InlineData("1.2.3+abcdef", 1, 2, 3)]
    [InlineData("1.2.3-beta.1", 1, 2, 3)]
    [InlineData("0.0.57.0", 0, 0, 57)]
    public void Version_text_is_understood(string text, int major, int minor, int patch)
    {
        Assert.Equal(new Version(major, minor, patch), AppVersion.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.2")]
    [InlineData("v1.x.3")]
    public void Anything_else_is_not_a_version(string? text)
    {
        Assert.Null(AppVersion.Parse(text));
    }

    [Fact]
    public void The_running_build_has_a_version()
    {
        Assert.NotNull(AppVersion.Current);
    }
}

public class UpdateCheckerTests
{
    private static readonly Version Now = new(0, 1, 0);

    [Fact]
    public void A_newer_release_is_offered_with_its_files()
    {
        UpdateCheckResult result = UpdateChecker.Evaluate(
            ReleaseJson.List(ReleaseJson.Release("v0.2.0", prerelease: true, body: "What changed")), Now);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Update);
        Assert.Equal(new Version(0, 2, 0), result.Update!.Version);
        Assert.Equal("v0.2.0", result.Update.Tag);
        Assert.True(result.Update.IsPrerelease);
        Assert.Equal("What changed", result.Update.Notes);
        Assert.EndsWith("/v0.2.0/EmailAttachmentDownloader-Setup.exe", result.Update.InstallerUrl.AbsoluteUri, StringComparison.Ordinal);
        Assert.EndsWith(".exe.sha256", result.Update.ChecksumUrl.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_or_an_older_release_is_not_offered()
    {
        UpdateCheckResult same = UpdateChecker.Evaluate(ReleaseJson.List(ReleaseJson.Release("v0.1.0", prerelease: true)), Now);
        UpdateCheckResult older = UpdateChecker.Evaluate(ReleaseJson.List(ReleaseJson.Release("v0.0.9", prerelease: true)), Now);

        Assert.Null(same.Update);
        Assert.Equal(new Version(0, 1, 0), same.Latest);
        Assert.Null(older.Update);
    }

    [Fact]
    public void Versions_are_compared_as_numbers_and_the_highest_wins_whatever_the_order()
    {
        string json = ReleaseJson.List(
            ReleaseJson.Release("v0.9.0", prerelease: true),
            ReleaseJson.Release("v0.10.0", prerelease: true),
            ReleaseJson.Release("v0.2.5", prerelease: true));

        Assert.Equal(new Version(0, 10, 0), UpdateChecker.Evaluate(json, Now).Update!.Version);
    }

    [Fact]
    public void Drafts_odd_tags_and_releases_missing_a_file_are_ignored()
    {
        string json = ReleaseJson.List(
            ReleaseJson.Release("v0.9.0", draft: true),
            ReleaseJson.Release("nightly"),
            ReleaseJson.Release("v0.8.0", installer: false),
            ReleaseJson.Release("v0.7.0", checksum: false),
            ReleaseJson.Release("v0.3.0"));

        Assert.Equal(new Version(0, 3, 0), UpdateChecker.Evaluate(json, Now).Update!.Version);
    }

    [Fact]
    public void Pre_releases_are_offered_to_zero_point_builds_but_not_to_stable_ones()
    {
        string json = ReleaseJson.List(
            ReleaseJson.Release("v1.1.0", prerelease: true),
            ReleaseJson.Release("v1.0.5"));

        Assert.Equal(new Version(1, 1, 0), UpdateChecker.Evaluate(json, new Version(0, 5, 0)).Update!.Version);
        Assert.Equal(new Version(1, 0, 5), UpdateChecker.Evaluate(json, new Version(1, 0, 0)).Update!.Version);
        Assert.Null(UpdateChecker.Evaluate(ReleaseJson.List(ReleaseJson.Release("v1.1.0", prerelease: true)), new Version(1, 0, 0)).Update);
    }

    [Fact]
    public void An_empty_list_means_nothing_to_offer()
    {
        UpdateCheckResult result = UpdateChecker.Evaluate("[]", Now);

        Assert.True(result.Succeeded);
        Assert.Null(result.Update);
        Assert.Null(result.Latest);
    }

    [Fact]
    public async Task Check_asks_github_for_the_release_list_and_identifies_itself()
    {
        var handler = new RoutingHandler(_ => StubHandler.Json(HttpStatusCode.OK, ReleaseJson.List(ReleaseJson.Release("v0.2.0", prerelease: true))));
        var checker = new UpdateChecker(new HttpClient(handler), "owner/name");

        UpdateCheckResult result = await checker.CheckAsync(Now);

        Assert.NotNull(result.Update);
        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.github.com/repos/owner/name/releases?per_page=30", request.RequestUri!.AbsoluteUri);
        Assert.Contains("EmailAttachmentDownloader/0.1.0", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "limiting")]
    [InlineData(HttpStatusCode.NotFound, "404")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task A_failed_request_is_reported_not_thrown(HttpStatusCode status, string expected)
    {
        var checker = new UpdateChecker(new HttpClient(new RoutingHandler(_ => new HttpResponseMessage(status))));

        UpdateCheckResult result = await checker.CheckAsync(Now);

        Assert.False(result.Succeeded);
        Assert.Contains(expected, result.Error, StringComparison.Ordinal);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task Being_offline_or_getting_junk_is_reported_not_thrown()
    {
        var offline = new UpdateChecker(new HttpClient(new RoutingHandler(_ => throw new HttpRequestException("no network"))));
        var junk = new UpdateChecker(new HttpClient(new RoutingHandler(_ => StubHandler.Json(HttpStatusCode.OK, "{\"not\":\"a list\"}"))));
        var garbage = new UpdateChecker(new HttpClient(new RoutingHandler(_ => StubHandler.Json(HttpStatusCode.OK, "<html>"))));

        Assert.Contains("no network", (await offline.CheckAsync(Now)).Error, StringComparison.Ordinal);
        Assert.False((await junk.CheckAsync(Now)).Succeeded);
        Assert.False((await garbage.CheckAsync(Now)).Succeeded);
    }
}

public class UpdateDownloaderTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes(new string('x', 200_000));

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static ReleaseInfo Release(string host = "github.com", string scheme = "https") => new(
        new Version(0, 2, 0), "v0.2.0", true, "", "",
        new Uri($"{scheme}://{host}/o/r/releases/download/v0.2.0/EmailAttachmentDownloader-Setup.exe"),
        new Uri($"{scheme}://{host}/o/r/releases/download/v0.2.0/EmailAttachmentDownloader-Setup.exe.sha256"));

    private static RoutingHandler Server(byte[] installer, string checksumText) => new(request =>
    {
        bool isChecksum = request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = isChecksum ? new StringContent(checksumText) : new ByteArrayContent(installer),
        };
    });

    [Fact]
    public async Task A_matching_download_is_kept_and_progress_is_reported()
    {
        using var dir = new TempDir();
        var handler = Server(Payload, Sha(Payload) + "  EmailAttachmentDownloader-Setup.exe\n");
        var downloader = new UpdateDownloader(new HttpClient(handler));
        var reports = new List<double>();

        string path = await downloader.DownloadAsync(Release(), dir.File("dl"), new SyncProgress(reports.Add));

        Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".partial"));
        Assert.Equal(1.0, reports[^1]);
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_checksum_is_discarded()
    {
        using var dir = new TempDir();
        var handler = Server(Payload, new string('0', 64));
        var downloader = new UpdateDownloader(new HttpClient(handler));

        UpdateException ex = await Assert.ThrowsAsync<UpdateException>(() => downloader.DownloadAsync(Release(), dir.File("dl")));

        Assert.Contains("does not match", ex.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(dir.File("dl")));
    }

    [Theory]
    [InlineData("not a hash")]
    [InlineData("")]
    [InlineData("abc123")]
    public async Task A_malformed_checksum_file_stops_before_the_installer_is_fetched(string checksum)
    {
        using var dir = new TempDir();
        var handler = Server(Payload, checksum);
        var downloader = new UpdateDownloader(new HttpClient(handler));

        await Assert.ThrowsAsync<UpdateException>(() => downloader.DownloadAsync(Release(), dir.File("dl")));

        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith(".exe", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("evil.example.com", "https")]
    [InlineData("github.com.evil.example.com", "https")]
    [InlineData("github.com", "http")]
    public async Task Only_https_github_com_downloads_are_trusted(string host, string scheme)
    {
        using var dir = new TempDir();
        var handler = Server(Payload, Sha(Payload));
        var downloader = new UpdateDownloader(new HttpClient(handler));

        await Assert.ThrowsAsync<UpdateException>(() => downloader.DownloadAsync(Release(host, scheme), dir.File("dl")));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_failing_server_leaves_nothing_behind()
    {
        using var dir = new TempDir();
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Sha(Payload)) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var downloader = new UpdateDownloader(new HttpClient(handler));

        UpdateException ex = await Assert.ThrowsAsync<UpdateException>(() => downloader.DownloadAsync(Release(), dir.File("dl")));

        Assert.Contains("404", ex.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(dir.File("dl")));
    }

    /// <summary>Reports synchronously, unlike <see cref="Progress{T}"/>, so the test can read the values straight away.</summary>
    private sealed class SyncProgress : IProgress<double>
    {
        private readonly Action<double> _report;

        public SyncProgress(Action<double> report)
        {
            _report = report;
        }

        public void Report(double value) => _report(value);
    }
}

public class UpdateSettingsTests
{
    [Fact]
    public void Defaults_check_automatically_and_skip_nothing()
    {
        using var dir = new TempDir();
        var store = new UpdateSettingsStore(dir.File("update.json"));

        Assert.True(store.Current.CheckAutomatically);
        Assert.Null(store.Current.SkippedVersion);
    }

    [Fact]
    public void Changes_are_saved_and_reloaded()
    {
        using var dir = new TempDir();
        new UpdateSettingsStore(dir.File("update.json")).Update(u =>
        {
            u.CheckAutomatically = false;
            u.SkippedVersion = "0.3.0";
        });

        UpdateSettings reloaded = new UpdateSettingsStore(dir.File("update.json")).Current;

        Assert.False(reloaded.CheckAutomatically);
        Assert.Equal("0.3.0", reloaded.SkippedVersion);
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_defaults()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("update.json"), "{ not json");

        var store = new UpdateSettingsStore(dir.File("update.json"));

        Assert.True(store.Current.CheckAutomatically);
    }
}
