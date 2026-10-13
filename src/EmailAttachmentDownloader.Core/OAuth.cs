using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EmailAttachmentDownloader.Core;

/// <summary>A short-lived access token and, at sign-in time, the long-lived refresh token.</summary>
/// <param name="Email">The Google account the token belongs to, when Google says (sign-in only).</param>
public sealed record OAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt, string? Email);

/// <summary>The two secrets an OAuth account needs besides the (non-secret) client ID in the profile.</summary>
public sealed record OAuthSecrets(string ClientSecret, string RefreshToken);

/// <summary>Sign-in failed. <see cref="NeedsSignIn"/> means the saved sign-in is no longer valid and the user must sign in again.</summary>
public sealed class OAuthException : Exception
{
    public OAuthException(string message, bool needsSignIn = false, Exception? inner = null)
        : base(message, inner)
    {
        NeedsSignIn = needsSignIn;
    }

    public bool NeedsSignIn { get; }
}

/// <summary>Credential-store keys for an account's OAuth secrets (the password itself uses the bare account id).</summary>
public static class OAuthKeys
{
    public static string ClientSecret(string accountId) => accountId + ":oauth-client-secret";

    public static string RefreshToken(string accountId) => accountId + ":oauth-refresh-token";
}

/// <summary>
/// Google sign-in for mail, using the "installed app" flow: the user's browser opens Google's consent page and
/// Google sends the user back to a one-shot listener on <c>http://127.0.0.1:&lt;random port&gt;/</c>. PKCE protects the
/// code exchange. The result is a refresh token that is exchanged for a short-lived access token before each run.
/// </summary>
/// <remarks>
/// The OAuth client must be of type "Desktop app" in Google Cloud: that type accepts any loopback port, so no redirect
/// URI needs registering. Mail access uses the restricted scope <c>https://mail.google.com/</c>; <c>openid email</c> are
/// added only so the sign-in can show which account was chosen.
/// </remarks>
public sealed class GoogleOAuth
{
    public const string Scope = "openid email https://mail.google.com/";

    internal const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    internal const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly HttpClient _http;
    private readonly TimeSpan _signInTimeout;

    public GoogleOAuth(HttpClient? http = null, TimeSpan? signInTimeout = null)
    {
        _http = http ?? SharedHttp;
        _signInTimeout = signInTimeout ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Opens the consent page through <paramref name="openBrowser"/> and waits for the user to finish. Throws
    /// <see cref="OperationCanceledException"/> if <paramref name="cancellationToken"/> is cancelled, and
    /// <see cref="OAuthException"/> for everything else that goes wrong (denied, timed out, bad client, ...).
    /// </summary>
    public async Task<OAuthTokens> SignInAsync(
        string clientId,
        string clientSecret,
        string? loginHint,
        Action<string> openBrowser,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new OAuthException("Enter the OAuth client ID and client secret first.");
        }

        string verifier = NewCodeVerifier();
        string state = RandomToken(24);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string redirectUri = $"http://127.0.0.1:{port}/";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_signInTimeout);

        var callback = new TaskCompletionSource<CallbackResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task acceptLoop = AcceptLoopAsync(listener, state, callback, cts.Token);

        CallbackResult result;
        try
        {
            try
            {
                openBrowser(BuildAuthorizationUrl(clientId.Trim(), redirectUri, state, ChallengeFor(verifier), loginHint));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new OAuthException("Could not open the browser for Google sign-in: " + ex.Message, inner: ex);
            }

            try
            {
                result = await callback.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new OAuthException("Timed out waiting for Google sign-in to finish. Try again.");
            }
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            await acceptLoop.ConfigureAwait(false);
        }

        if (result.Error is not null)
        {
            throw new OAuthException(
                result.Error == "access_denied"
                    ? "Google sign-in was cancelled or denied."
                    : "Google sign-in failed: " + result.Error);
        }

        var form = new Dictionary<string, string>
        {
            ["code"] = result.Code!,
            ["client_id"] = clientId.Trim(),
            ["client_secret"] = clientSecret.Trim(),
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier,
        };

        OAuthTokens tokens = await PostTokenAsync(form, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(tokens.RefreshToken))
        {
            throw new OAuthException(
                "Google did not return a long-lived sign-in. Remove this app at https://myaccount.google.com/permissions and sign in again.");
        }

        return tokens;
    }

    /// <summary>Exchanges a saved refresh token for a fresh access token. Throws <see cref="OAuthException"/>.</summary>
    public Task<OAuthTokens> RefreshAsync(
        string clientId, string clientSecret, string refreshToken, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId.Trim(),
            ["client_secret"] = clientSecret.Trim(),
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        };

        return PostTokenAsync(form, cancellationToken);
    }

    // ----- token endpoint ----------------------------------------------------------------------

    private async Task<OAuthTokens> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        string body;
        bool ok;
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using HttpResponseMessage response = await _http.PostAsync(TokenEndpoint, content, ct).ConfigureAwait(false);
            ok = response.IsSuccessStatusCode;
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new OAuthException("Could not reach Google to sign in: " + ex.Message, inner: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new OAuthException("Google did not answer in time. Check the internet connection and try again.", inner: ex);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new OAuthException("Google sent an answer that could not be read.", inner: ex);
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (!ok)
            {
                throw ErrorFrom(root);
            }

            string? access = Text(root, "access_token");
            if (string.IsNullOrEmpty(access))
            {
                throw new OAuthException("Google's answer did not include an access token.");
            }

            int seconds = root.TryGetProperty("expires_in", out JsonElement expires) && expires.TryGetInt32(out int value) ? value : 3600;
            return new OAuthTokens(
                access,
                Text(root, "refresh_token"),
                DateTimeOffset.UtcNow.AddSeconds(seconds),
                EmailFromIdToken(Text(root, "id_token")));
        }
    }

    private static OAuthException ErrorFrom(JsonElement root)
    {
        string error = root.ValueKind == JsonValueKind.Object ? Text(root, "error") ?? "unknown_error" : "unknown_error";
        string? description = root.ValueKind == JsonValueKind.Object ? Text(root, "error_description") : null;

        return error switch
        {
            "invalid_grant" => new OAuthException(
                "Google no longer accepts the saved sign-in (it was revoked or has expired). Open Settings and sign in with Google again.",
                needsSignIn: true),
            "invalid_client" or "unauthorized_client" => new OAuthException(
                "Google rejected the OAuth client ID or secret. Check them in Settings and in Google Cloud."),
            _ => new OAuthException($"Google refused the sign-in: {error}" + (description is null ? "" : $" ({description})")),
        };
    }

    private static string? Text(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(name, out JsonElement value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>Reads the e-mail address from the (unverified) id_token. It arrived over TLS straight from Google and is only displayed.</summary>
    internal static string? EmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken))
        {
            return null;
        }

        string[] parts = idToken.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            using JsonDocument payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            return Text(payload.RootElement, "email");
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return null;
        }
    }

    // ----- authorization request ---------------------------------------------------------------

    internal static string BuildAuthorizationUrl(string clientId, string redirectUri, string state, string codeChallenge, string? loginHint)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new("client_id", clientId),
            new("redirect_uri", redirectUri),
            new("response_type", "code"),
            new("scope", Scope),
            new("state", state),
            new("code_challenge", codeChallenge),
            new("code_challenge_method", "S256"),
            new("access_type", "offline"),
            new("prompt", "consent"), // makes Google hand back a refresh token every time
        };

        if (!string.IsNullOrWhiteSpace(loginHint))
        {
            query.Add(new("login_hint", loginHint.Trim()));
        }

        return AuthEndpoint + "?" + string.Join("&", query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
    }

    internal static string NewCodeVerifier() => RandomToken(64);

    /// <summary>PKCE S256: base64url(SHA-256(verifier)).</summary>
    internal static string ChallengeFor(string verifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    internal static string RandomToken(int byteCount) => Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string text)
    {
        string padded = text.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    // ----- loopback listener -------------------------------------------------------------------

    internal sealed record CallbackResult(string? Code, string? Error);

    /// <summary>
    /// Reads "GET /?code=...&amp;state=... HTTP/1.1". Returns null for anything that is not a genuine callback for this
    /// sign-in (favicon requests, stray hits, a wrong state), so those are ignored and the wait continues.
    /// </summary>
    internal static CallbackResult? ParseCallback(string requestLine, string expectedState)
    {
        string[] parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] != "GET")
        {
            return null;
        }

        int question = parts[1].IndexOf('?');
        if (question < 0)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in parts[1][(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            string key = equals < 0 ? pair : pair[..equals];
            string value = equals < 0 ? "" : pair[(equals + 1)..];
            values[Decode(key)] = Decode(value);
        }

        if (!values.TryGetValue("state", out string? state) || !FixedTimeEquals(state, expectedState))
        {
            return null;
        }

        if (values.TryGetValue("error", out string? error) && error.Length > 0)
        {
            return new CallbackResult(null, error);
        }

        return values.TryGetValue("code", out string? code) && code.Length > 0 ? new CallbackResult(code, null) : null;
    }

    private static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static async Task AcceptLoopAsync(
        TcpListener listener, string state, TaskCompletionSource<CallbackResult> callback, CancellationToken ct)
    {
        // Browsers open speculative idle connections, so each connection gets its own task; one stuck connection
        // must not block the real callback.
        var handlers = new List<Task>();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                handlers.Add(HandleConnectionAsync(client, state, callback, ct));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // Normal shutdown.
        }

        await Task.WhenAll(handlers).ConfigureAwait(false);
    }

    private static async Task HandleConnectionAsync(
        TcpClient client, string state, TaskCompletionSource<CallbackResult> callback, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using var readLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                readLimit.CancelAfter(TimeSpan.FromSeconds(10));

                NetworkStream stream = client.GetStream();
                string? line = await ReadRequestLineAsync(stream, readLimit.Token).ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                CallbackResult? parsed = ParseCallback(line, state);
                if (parsed is null)
                {
                    await WriteResponseAsync(stream, "404 Not Found", Page("Not found", "This address is only used for Google sign-in."), readLimit.Token)
                        .ConfigureAwait(false);
                    return;
                }

                string page = parsed.Error is null
                    ? Page("Signed in", "You can close this tab and go back to Email Attachment Downloader.")
                    : Page("Sign-in did not finish", "Google said: " + WebUtility.HtmlEncode(parsed.Error) + ". You can close this tab and try again.");
                await WriteResponseAsync(stream, "200 OK", page, readLimit.Token).ConfigureAwait(false);
                callback.TrySetResult(parsed);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // A dropped or idle connection; ignore.
            }
        }
    }

    /// <summary>
    /// Reads the whole request head (up to the blank line) and returns its first line. Everything the browser sent is
    /// consumed before replying: closing a socket that still has unread data makes Windows reset the connection, and the
    /// browser would then show an error instead of our page.
    /// </summary>
    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[16384];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            length += read;
            if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        if (length == 0)
        {
            return null;
        }

        string head = Encoding.ASCII.GetString(buffer, 0, length);
        int lineEnd = head.IndexOf('\n');
        return (lineEnd < 0 ? head : head[..lineEnd]).TrimEnd('\r');
    }

    private static async Task WriteResponseAsync(NetworkStream stream, string status, string html, CancellationToken ct)
    {
        byte[] body = Encoding.UTF8.GetBytes(html);
        string head = $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static string Page(string title, string message) =>
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>" + title + "</title></head>"
        + "<body style=\"font-family:Segoe UI,sans-serif;max-width:32rem;margin:4rem auto;padding:0 1rem\">"
        + "<h2>" + title + "</h2><p>" + message + "</p></body></html>";
}
