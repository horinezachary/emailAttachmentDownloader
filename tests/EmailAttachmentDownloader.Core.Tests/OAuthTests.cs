using System.Net;
using System.Text;

namespace EmailAttachmentDownloader.Core.Tests;

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<string, HttpResponseMessage> _respond;

    public StubHandler(Func<string, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    /// <summary>Every request body that was posted, in order.</summary>
    public List<string> Bodies { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Bodies)
        {
            Bodies.Add(body);
        }

        return _respond(body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public class OAuthHelperTests
{
    [Fact]
    public void Pkce_challenge_matches_the_RFC_7636_example()
    {
        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            GoogleOAuth.ChallengeFor("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Verifiers_are_long_enough_url_safe_and_different_each_time()
    {
        string a = GoogleOAuth.NewCodeVerifier();
        string b = GoogleOAuth.NewCodeVerifier();

        Assert.NotEqual(a, b);
        Assert.InRange(a.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", a);
    }

    [Fact]
    public void The_authorization_url_carries_what_google_needs_and_never_the_client_secret()
    {
        string url = GoogleOAuth.BuildAuthorizationUrl("my-client", "http://127.0.0.1:5555/", "st&te", "chal", "me@gmail.com");
        Dictionary<string, string> q = OAuthTestHelpers.Query(url);

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url, StringComparison.Ordinal);
        Assert.Equal("my-client", q["client_id"]);
        Assert.Equal("http://127.0.0.1:5555/", q["redirect_uri"]);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("chal", q["code_challenge"]);
        Assert.Equal("st&te", q["state"]);
        Assert.Equal("offline", q["access_type"]);
        Assert.Equal("consent", q["prompt"]);
        Assert.Equal("me@gmail.com", q["login_hint"]);
        Assert.Contains("https://mail.google.com/", q["scope"], StringComparison.Ordinal);
        Assert.DoesNotContain("secret", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_login_hint_is_left_out_when_there_is_none()
    {
        string url = GoogleOAuth.BuildAuthorizationUrl("c", "http://127.0.0.1:1/", "s", "x", null);

        Assert.DoesNotContain("login_hint", url, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseCallback_accepts_a_code_with_the_right_state()
    {
        GoogleOAuth.CallbackResult? result = GoogleOAuth.ParseCallback("GET /?state=abc&code=4%2F0AbC-d&scope=x HTTP/1.1", "abc");

        Assert.NotNull(result);
        Assert.Equal("4/0AbC-d", result.Code);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ParseCallback_reports_an_error_from_google()
    {
        GoogleOAuth.CallbackResult? result = GoogleOAuth.ParseCallback("GET /?error=access_denied&state=abc HTTP/1.1", "abc");

        Assert.NotNull(result);
        Assert.Null(result.Code);
        Assert.Equal("access_denied", result.Error);
    }

    [Theory]
    [InlineData("GET /?code=x&state=WRONG HTTP/1.1")]
    [InlineData("GET /?code=x HTTP/1.1")]
    [InlineData("GET /favicon.ico HTTP/1.1")]
    [InlineData("GET /?state=abc HTTP/1.1")]
    [InlineData("POST /?code=x&state=abc HTTP/1.1")]
    [InlineData("")]
    public void ParseCallback_ignores_anything_that_is_not_a_genuine_callback(string requestLine)
    {
        Assert.Null(GoogleOAuth.ParseCallback(requestLine, "abc"));
    }

    [Fact]
    public void The_email_is_read_from_an_id_token_and_bad_tokens_give_null()
    {
        Assert.Equal("me@gmail.com", GoogleOAuth.EmailFromIdToken(OAuthTestHelpers.Jwt("me@gmail.com")));
        Assert.Null(GoogleOAuth.EmailFromIdToken(null));
        Assert.Null(GoogleOAuth.EmailFromIdToken("not-a-jwt"));
        Assert.Null(GoogleOAuth.EmailFromIdToken("a.!!!.c"));
    }

    [Fact]
    public void OAuth_keys_are_distinct_per_account_and_from_the_password_key()
    {
        Assert.NotEqual(OAuthKeys.ClientSecret("a"), OAuthKeys.RefreshToken("a"));
        Assert.NotEqual(OAuthKeys.RefreshToken("a"), OAuthKeys.RefreshToken("b"));
        Assert.NotEqual("a", OAuthKeys.RefreshToken("a"));
    }

    [Fact]
    public void Environment_variable_names_are_safe_for_oauth_keys()
    {
        Assert.Equal("EAD_PASSWORD_ABC_OAUTH_REFRESH_TOKEN", EnvironmentCredentialStore.VariableName("abc:oauth-refresh-token"));
        Assert.Equal("EAD_PASSWORD_ABC123", EnvironmentCredentialStore.VariableName("abc123"));
    }
}

public class GoogleOAuthFlowTests
{
    private static GoogleOAuth Create(StubHandler handler, TimeSpan? timeout = null) =>
        new(new HttpClient(handler), timeout ?? TimeSpan.FromSeconds(30));

    private static string TokenJson(string? refresh = "RT", string email = "me@gmail.com") =>
        "{\"access_token\":\"AT\",\"expires_in\":3599"
        + (refresh is null ? "" : ",\"refresh_token\":\"" + refresh + "\"")
        + ",\"id_token\":\"" + OAuthTestHelpers.Jwt(email) + "\"}";

    [Fact]
    public async Task SignIn_runs_the_whole_flow_and_returns_the_tokens()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, TokenJson()));
        string? openedUrl = null;

        OAuthTokens tokens = await Create(handler).SignInAsync(
            "client-id", "client-secret", "me@gmail.com",
            url =>
            {
                openedUrl = url;
                OAuthTestHelpers.HitCallback(url, "the-code");
            });

        Assert.Equal("AT", tokens.AccessToken);
        Assert.Equal("RT", tokens.RefreshToken);
        Assert.Equal("me@gmail.com", tokens.Email);
        Assert.True(tokens.ExpiresAt > DateTimeOffset.UtcNow);

        // What was sent to Google's token endpoint ties back to the page that was opened.
        Dictionary<string, string> auth = OAuthTestHelpers.Query(openedUrl!);
        Dictionary<string, string> form = OAuthTestHelpers.Form(Assert.Single(handler.Bodies));
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("the-code", form["code"]);
        Assert.Equal("client-id", form["client_id"]);
        Assert.Equal("client-secret", form["client_secret"]);
        Assert.Equal(auth["redirect_uri"], form["redirect_uri"]);
        Assert.Equal(auth["code_challenge"], GoogleOAuth.ChallengeFor(form["code_verifier"]));
    }

    [Fact]
    public async Task SignIn_ignores_a_callback_with_the_wrong_state_and_keeps_waiting()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, TokenJson()));

        OAuthTokens tokens = await Create(handler).SignInAsync(
            "c", "s", null, url => OAuthTestHelpers.HitCallback(url, "good-code", sendWrongStateFirst: true));

        Assert.Equal("good-code", OAuthTestHelpers.Form(Assert.Single(handler.Bodies))["code"]);
        Assert.Equal("AT", tokens.AccessToken);
    }

    [Fact]
    public async Task SignIn_reports_a_denied_consent_screen()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("the token endpoint must not be called"));

        OAuthException ex = await Assert.ThrowsAsync<OAuthException>(() => Create(handler).SignInAsync(
            "c", "s", null, url => OAuthTestHelpers.HitCallback(url, code: null, error: "access_denied")));

        Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task SignIn_times_out_if_nobody_finishes()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, TokenJson()));

        OAuthException ex = await Assert.ThrowsAsync<OAuthException>(
            () => Create(handler, TimeSpan.FromMilliseconds(300)).SignInAsync("c", "s", null, _ => { }));

        Assert.Contains("Timed out", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignIn_can_be_cancelled()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, TokenJson()));
        using var cts = new CancellationTokenSource();

        Task<OAuthTokens> pending = Create(handler).SignInAsync("c", "s", null, _ => cts.CancelAfter(100), cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task SignIn_without_a_client_id_or_secret_is_refused_before_anything_opens()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, TokenJson()));
        bool opened = false;

        await Assert.ThrowsAsync<OAuthException>(() => Create(handler).SignInAsync("", "s", null, _ => opened = true));
        await Assert.ThrowsAsync<OAuthException>(() => Create(handler).SignInAsync("c", " ", null, _ => opened = true));

        Assert.False(opened);
    }

    [Fact]
    public async Task SignIn_fails_clearly_when_google_returns_no_refresh_token()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, TokenJson(refresh: null)));

        OAuthException ex = await Assert.ThrowsAsync<OAuthException>(() => Create(handler).SignInAsync(
            "c", "s", null, url => OAuthTestHelpers.HitCallback(url, "code")));

        Assert.Contains("long-lived", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_exchanges_the_refresh_token_for_an_access_token()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, "{\"access_token\":\"NEW\",\"expires_in\":3600}"));

        OAuthTokens tokens = await Create(handler).RefreshAsync("cid", "csecret", "the-refresh");

        Assert.Equal("NEW", tokens.AccessToken);
        Assert.Null(tokens.RefreshToken);
        Dictionary<string, string> form = OAuthTestHelpers.Form(Assert.Single(handler.Bodies));
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("the-refresh", form["refresh_token"]);
        Assert.Equal("cid", form["client_id"]);
        Assert.Equal("csecret", form["client_secret"]);
    }

    [Fact]
    public async Task Refresh_with_a_revoked_token_says_the_user_must_sign_in_again()
    {
        var handler = new StubHandler(_ => StubHandler.Json(
            HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"error_description\":\"Token has been expired or revoked.\"}"));

        OAuthException ex = await Assert.ThrowsAsync<OAuthException>(() => Create(handler).RefreshAsync("c", "s", "r"));

        Assert.True(ex.NeedsSignIn);
        Assert.Contains("sign in with Google again", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_with_a_wrong_client_says_so_and_does_not_ask_for_a_new_sign_in()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\"}"));

        OAuthException ex = await Assert.ThrowsAsync<OAuthException>(() => Create(handler).RefreshAsync("c", "s", "r"));

        Assert.False(ex.NeedsSignIn);
        Assert.Contains("client", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_survives_garbage_from_the_server()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>oops</html>") });

        await Assert.ThrowsAsync<OAuthException>(() => Create(handler).RefreshAsync("c", "s", "r"));
    }
}

public class OAuthProfileTests
{
    private static AccountProfile Google() => new()
    {
        UserName = "me@gmail.com",
        Host = "imap.gmail.com",
        Authentication = AuthMethod.GoogleOAuth,
        OAuthClientId = "abc.apps.googleusercontent.com",
        SaveFolder = Path.Combine(Path.GetTempPath(), "pics"),
    };

    [Fact]
    public void A_complete_google_profile_is_valid()
    {
        Assert.Empty(Google().Validate());
    }

    [Fact]
    public void Google_sign_in_needs_a_client_id()
    {
        AccountProfile p = Google();
        p.OAuthClientId = " ";

        Assert.Contains(p.Validate(), m => m.Contains("client ID", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("imap.gmail.com", true)]
    [InlineData("pop.gmail.com", true)]
    [InlineData("imap.googlemail.com", true)]
    [InlineData("IMAP.GMAIL.COM", true)]
    [InlineData("outlook.office365.com", false)]
    [InlineData("notgmail.com", false)]
    [InlineData("gmail.com.evil.example", false)]
    [InlineData("", false)]
    public void Google_sign_in_is_only_for_gmail_servers(string host, bool allowed)
    {
        Assert.Equal(allowed, AccountProfile.IsGoogleHost(host));

        AccountProfile p = Google();
        p.Host = host;
        if (host.Length > 0)
        {
            Assert.Equal(allowed, !p.Validate().Any(m => m.Contains("Gmail servers", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void Password_accounts_do_not_need_a_client_id_or_a_gmail_host()
    {
        var p = new AccountProfile { UserName = "u", Host = "mail.example.com", SaveFolder = Path.Combine(Path.GetTempPath(), "pics") };

        Assert.Empty(p.Validate());
    }

    [Fact]
    public void Profiles_saved_before_google_sign_in_load_as_password_accounts()
    {
        using var dir = new TempDir();
        string path = dir.File("profiles.json");
        File.WriteAllText(
            path,
            "{\"version\":1,\"profiles\":[{\"id\":\"old\",\"name\":\"Old\",\"host\":\"imap.gmail.com\",\"userName\":\"u\"}]}");

        AccountProfile loaded = Assert.Single(new ProfileStore(path).Load());

        Assert.Equal(AuthMethod.Password, loaded.Authentication);
        Assert.Equal("", loaded.OAuthClientId);
    }

    [Fact]
    public void The_sign_in_choice_round_trips_and_no_secret_reaches_the_file()
    {
        using var dir = new TempDir();
        string path = dir.File("profiles.json");

        new ProfileStore(path).Save(new[] { Google() });
        AccountProfile back = Assert.Single(new ProfileStore(path).Load());
        string text = File.ReadAllText(path);

        Assert.Equal(AuthMethod.GoogleOAuth, back.Authentication);
        Assert.Equal("abc.apps.googleusercontent.com", back.OAuthClientId);
        Assert.Contains("\"GoogleOAuth\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Switching_to_google_sign_in_does_not_change_which_mail_qualifies()
    {
        AccountProfile password = Google();
        password.Authentication = AuthMethod.Password;

        Assert.Equal(FilterSignature.Compute(password), FilterSignature.Compute(Google()));
    }
}

public class OAuthSyncServiceTests
{
    private static readonly GoogleClientCredentials BuiltIn = new("builtin.apps.googleusercontent.com", "builtin-secret");

    private static MailSyncService Service(
        TempDir dir, ICredentialStore credentials, GoogleOAuth oauth, GoogleClientCredentials? builtIn = null) => new(
        new ImageLibrary(dir.File("library.json")),
        new SyncStateStore(dir.File("state.json")),
        credentials,
        dir.File("sync.lock"),
        logger: null,
        oauth: oauth,
        builtInClient: builtIn);

    private static StubHandler Revoked() =>
        new(_ => StubHandler.Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}"));

    private static AccountProfile Google(TempDir dir) => new()
    {
        Id = "acct",
        Name = "Frames",
        UserName = "me@gmail.com",
        Host = "imap.gmail.com",
        Authentication = AuthMethod.GoogleOAuth,
        OAuthClientId = "abc.apps.googleusercontent.com",
        SaveFolder = dir.File("pics"),
    };

    [Fact]
    public async Task A_google_account_that_was_never_signed_in_says_so_and_does_not_ask_for_a_password()
    {
        using var dir = new TempDir();
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not be called"));
        MailSyncService service = Service(dir, new MemoryCredentialStore(), new GoogleOAuth(new HttpClient(handler)));

        RunResult result = await service.RunAsync(Google(dir));

        Assert.False(result.Succeeded);
        Assert.Contains("not signed in", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task A_revoked_google_sign_in_is_reported_as_a_failed_run_with_advice()
    {
        using var dir = new TempDir();
        var credentials = new MemoryCredentialStore();
        credentials.Set(OAuthKeys.ClientSecret("acct"), "csecret");
        credentials.Set(OAuthKeys.RefreshToken("acct"), "refresh");
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}"));
        MailSyncService service = Service(dir, credentials, new GoogleOAuth(new HttpClient(handler)));

        RunResult result = await service.RunAsync(Google(dir));

        Assert.False(result.Succeeded);
        Assert.Contains("sign in with Google again", result.Error, StringComparison.OrdinalIgnoreCase);

        // It really did try the stored secrets.
        Dictionary<string, string> form = OAuthTestHelpers.Form(Assert.Single(handler.Bodies));
        Assert.Equal("refresh", form["refresh_token"]);
        Assert.Equal("csecret", form["client_secret"]);
        Assert.Equal("abc.apps.googleusercontent.com", form["client_id"]);
    }

    [Fact]
    public async Task A_password_account_still_asks_for_its_password()
    {
        using var dir = new TempDir();
        var handler = new StubHandler(_ => throw new InvalidOperationException("must not be called"));
        MailSyncService service = Service(dir, new MemoryCredentialStore(), new GoogleOAuth(new HttpClient(handler)));
        AccountProfile p = Google(dir);
        p.Authentication = AuthMethod.Password;

        RunResult result = await service.RunAsync(p);

        Assert.Contains("No password is stored", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_built_in_client_signs_in_without_any_stored_client_secret()
    {
        using var dir = new TempDir();
        var credentials = new MemoryCredentialStore();
        credentials.Set(OAuthKeys.RefreshToken("acct"), "refresh");
        StubHandler handler = Revoked();
        MailSyncService service = Service(dir, credentials, new GoogleOAuth(new HttpClient(handler)), BuiltIn);
        AccountProfile p = Google(dir);
        p.OAuthClientId = "";

        RunResult result = await service.RunAsync(p);

        Assert.Contains("sign in with Google again", result.Error, StringComparison.OrdinalIgnoreCase);
        Dictionary<string, string> form = OAuthTestHelpers.Form(Assert.Single(handler.Bodies));
        Assert.Equal("builtin.apps.googleusercontent.com", form["client_id"]);
        Assert.Equal("builtin-secret", form["client_secret"]);
        Assert.Equal("refresh", form["refresh_token"]);
    }

    [Fact]
    public async Task An_account_that_chose_its_own_client_uses_it_even_when_one_is_built_in()
    {
        using var dir = new TempDir();
        var credentials = new MemoryCredentialStore();
        credentials.Set(OAuthKeys.RefreshToken("acct"), "refresh");
        credentials.Set(OAuthKeys.ClientSecret("acct"), "own-secret");
        StubHandler handler = Revoked();
        MailSyncService service = Service(dir, credentials, new GoogleOAuth(new HttpClient(handler)), BuiltIn);
        AccountProfile p = Google(dir);
        p.UseOwnOAuthClient = true;

        await service.RunAsync(p);

        Dictionary<string, string> form = OAuthTestHelpers.Form(Assert.Single(handler.Bodies));
        Assert.Equal("abc.apps.googleusercontent.com", form["client_id"]);
        Assert.Equal("own-secret", form["client_secret"]);
    }

    [Fact]
    public async Task An_own_client_with_no_stored_secret_says_what_is_missing()
    {
        using var dir = new TempDir();
        var credentials = new MemoryCredentialStore();
        credentials.Set(OAuthKeys.RefreshToken("acct"), "refresh");
        StubHandler handler = Revoked();
        MailSyncService service = Service(dir, credentials, new GoogleOAuth(new HttpClient(handler)), BuiltIn);
        AccountProfile p = Google(dir);
        p.UseOwnOAuthClient = true;

        RunResult result = await service.RunAsync(p);

        Assert.Contains("own Google OAuth client", result.Error, StringComparison.Ordinal);
        Assert.Empty(handler.Bodies);
    }
}

public class BuiltInGoogleClientTests
{
    private static readonly GoogleClientCredentials BuiltIn = new("builtin-id", "builtin-secret");

    private static AccountProfile Account() => new()
    {
        Id = "acct",
        Name = "Frames",
        UserName = "me@gmail.com",
        Host = "imap.gmail.com",
        Authentication = AuthMethod.GoogleOAuth,
        SaveFolder = Path.Combine(Path.GetTempPath(), "pics"),
    };

    private static IEnumerable<KeyValuePair<string, string?>> Meta(params (string Key, string? Value)[] items) =>
        items.Select(i => new KeyValuePair<string, string?>(i.Key, i.Value));

    [Fact]
    public void The_built_in_client_is_read_from_assembly_metadata_and_needs_both_parts()
    {
        GoogleClientCredentials? both = GoogleClient.FromMetadata(Meta(("Other", "x"), ("GoogleClientId", " id "), ("GoogleClientSecret", "sec")));
        Assert.Equal(new GoogleClientCredentials("id", "sec"), both);

        Assert.Null(GoogleClient.FromMetadata(Meta(("GoogleClientId", "id"))));
        Assert.Null(GoogleClient.FromMetadata(Meta(("GoogleClientSecret", "sec"))));
        Assert.Null(GoogleClient.FromMetadata(Meta(("GoogleClientId", ""), ("GoogleClientSecret", "sec"))));
        Assert.Null(GoogleClient.FromMetadata(Meta()));
    }

    [Fact]
    public void An_account_uses_the_built_in_client_unless_it_asks_for_its_own()
    {
        AccountProfile p = Account();

        Assert.False(GoogleClient.UsesOwnClient(p, BuiltIn));
        Assert.Equal(BuiltIn, GoogleClient.Resolve(p, null, BuiltIn));

        p.UseOwnOAuthClient = true;
        Assert.True(GoogleClient.UsesOwnClient(p, BuiltIn));
    }

    [Fact]
    public void An_own_client_needs_both_its_id_and_its_secret()
    {
        AccountProfile p = Account();
        p.UseOwnOAuthClient = true;

        Assert.Null(GoogleClient.Resolve(p, "own-secret", BuiltIn));

        p.OAuthClientId = " own-id ";
        Assert.Null(GoogleClient.Resolve(p, null, BuiltIn));
        Assert.Null(GoogleClient.Resolve(p, "  ", BuiltIn));
        Assert.Equal(new GoogleClientCredentials("own-id", "own-secret"), GoogleClient.Resolve(p, " own-secret ", BuiltIn));
    }

    [Fact]
    public void Without_a_built_in_client_every_account_must_use_its_own()
    {
        AccountProfile p = Account();
        p.OAuthClientId = "own-id";

        Assert.True(GoogleClient.UsesOwnClient(p, null));
        Assert.Equal(new GoogleClientCredentials("own-id", "s"), GoogleClient.Resolve(p, "s", null));
        Assert.Null(GoogleClient.Resolve(p, null, null));
    }

    [Fact]
    public void A_client_id_is_only_required_when_the_account_uses_its_own_client()
    {
        AccountProfile p = Account();

        Assert.Empty(p.Validate(BuiltIn));
        Assert.Contains(p.Validate(null), m => m.Contains("client ID", StringComparison.OrdinalIgnoreCase));

        p.UseOwnOAuthClient = true;
        Assert.Contains(p.Validate(BuiltIn), m => m.Contains("client ID", StringComparison.OrdinalIgnoreCase));

        p.OAuthClientId = "own-id";
        Assert.Empty(p.Validate(BuiltIn));
    }

    [Fact]
    public void The_own_client_choice_round_trips_and_defaults_to_off()
    {
        using var dir = new TempDir();
        string path = dir.File("profiles.json");
        AccountProfile p = Account();
        p.UseOwnOAuthClient = true;

        new ProfileStore(path).Save(new[] { p });
        Assert.True(Assert.Single(new ProfileStore(path).Load()).UseOwnOAuthClient);

        File.WriteAllText(path, "{\"version\":1,\"profiles\":[{\"id\":\"old\",\"name\":\"Old\"}]}");
        Assert.False(Assert.Single(new ProfileStore(path).Load()).UseOwnOAuthClient);
    }
}

internal static class OAuthTestHelpers
{
    public static Dictionary<string, string> Query(string url) => Parse(new Uri(url).Query.TrimStart('?'));

    public static Dictionary<string, string> Form(string body) => Parse(body);

    private static Dictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            result[Uri.UnescapeDataString(parts[0].Replace('+', ' '))] =
                Uri.UnescapeDataString((parts.Length > 1 ? parts[1] : "").Replace('+', ' '));
        }

        return result;
    }

    public static string Jwt(string email)
    {
        static string B64(string json) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return B64("{\"alg\":\"none\"}") + "." + B64("{\"email\":\"" + email + "\"}") + ".sig";
    }

    /// <summary>Plays the part of the browser: calls the loopback address Google would redirect to.</summary>
    public static void HitCallback(string authUrl, string? code, string? error = null, bool sendWrongStateFirst = false)
    {
        Dictionary<string, string> q = Query(authUrl);
        string redirect = q["redirect_uri"];
        string state = Uri.EscapeDataString(q["state"]);

        _ = Task.Run(async () =>
        {
            using var http = new HttpClient();
            if (sendWrongStateFirst)
            {
                await http.GetAsync(redirect + "?code=evil&state=wrong");
            }

            string query = error is not null
                ? $"?error={Uri.EscapeDataString(error)}&state={state}"
                : $"?code={Uri.EscapeDataString(code ?? "")}&state={state}";
            await http.GetAsync(redirect + query);
        });
    }
}
