using System.Text;
using EmailAttachmentDownloader.Core;
using Microsoft.Extensions.Logging;

// ead: a small command-line front end over the same core the tray app uses.
// Its main job is "run once and exit" so Windows Task Scheduler can drive syncs with no resident process.

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintHelp();
    return 0;
}

var paths = new AppPaths();
var store = new ProfileStore(paths.ProfilesFile);
var catalog = new ProfileCatalog(store);
ICredentialStore credentials = CredentialStores.CreateDefault(paths);

if (store.LoadWarning is not null)
{
    Console.Error.WriteLine("Warning: " + store.LoadWarning);
}

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Information);
    builder.AddSimpleConsole(options => options.SingleLine = true);
});

string[] rest = args.Skip(1).ToArray();

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "paths":
            Console.WriteLine($"Data folder:  {paths.DataDirectory}");
            Console.WriteLine($"Profiles:     {paths.ProfilesFile}");
            Console.WriteLine($"Image index:  {paths.LibraryFile}");
            Console.WriteLine($"Log file:     {paths.LogFile}");
            return 0;

        case "list":
            return ListAccounts();

        case "run":
            return await RunAsync(rest);

        case "test":
            return await TestAsync(rest);

        case "set-password":
            return SetPassword(rest);

        case "import-legacy":
            return ImportLegacy(rest);

        default:
            Console.Error.WriteLine($"Unknown command \"{args[0]}\".");
            PrintHelp();
            return 2;
    }
}
catch (Exception ex) when (ex is NotSupportedException or FormatException or IOException or InvalidOperationException)
{
    Console.Error.WriteLine("Error: " + ex.Message);
    return 1;
}

int ListAccounts()
{
    var profiles = catalog.Snapshot();
    if (profiles.Count == 0)
    {
        Console.WriteLine("No accounts yet. Add one in the tray app, or run: ead import-legacy <path to old cfg folder>");
        return 0;
    }

    foreach (AccountProfile p in profiles)
    {
        string schedule = p.Scheduled ? $"every {Math.Max(AccountProfile.MinIntervalMinutes, p.IntervalMinutes)} min" : "manual";
        string keywords = p.Keywords.Count == 0 ? "(all e-mail)" : Keywords.Format(p.Keywords);
        Console.WriteLine($"{p.Name}  [{(p.Enabled ? "on" : "off")}]  {p.Protocol} {p.Host}:{p.Port}  {schedule}");
        Console.WriteLine($"    keywords: {keywords} ({p.KeywordMode})   types: {string.Join(", ", p.AttachmentTypes)}");
        Console.WriteLine($"    saves to: {p.SaveFolder}");
    }

    return 0;
}

AccountProfile? FindProfile(string nameOrId)
{
    return catalog.Snapshot().FirstOrDefault(p =>
        p.Id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase)
        || p.Name.Equals(nameOrId, StringComparison.OrdinalIgnoreCase));
}

async Task<int> RunAsync(string[] names)
{
    var targets = names.Length == 0
        ? catalog.Snapshot().Where(p => p.Enabled).ToList()
        : names.Select(n => FindProfile(n) ?? throw new InvalidOperationException($"No account named \"{n}\".")).ToList();

    if (targets.Count == 0)
    {
        Console.Error.WriteLine("Nothing to run: no enabled accounts.");
        return 1;
    }

    var library = new ImageLibrary(paths.LibraryFile);
    var state = new SyncStateStore(paths.SyncStateFile);
    var history = new RunHistory(paths.HistoryFile);
    var service = new MailSyncService(
        library, state, credentials, paths.LockFile, loggerFactory.CreateLogger<MailSyncService>());

    service.AttachmentSaved += (_, e) => Console.WriteLine($"  saved {e.Image.FileName}");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    int exitCode = 0;
    foreach (AccountProfile profile in targets)
    {
        RunResult result = await service.RunAsync(profile, cts.Token);
        history.Append(result);
        Console.WriteLine(result.Summary);
        if (!result.Succeeded)
        {
            exitCode = 1;
        }
    }

    return exitCode;
}

async Task<int> TestAsync(string[] names)
{
    if (names.Length != 1)
    {
        Console.Error.WriteLine("Usage: ead test <account name>");
        return 2;
    }

    AccountProfile profile = FindProfile(names[0]) ?? throw new InvalidOperationException($"No account named \"{names[0]}\".");
    // Google sign-in accounts use the secrets saved by the tray app's Settings; only password accounts need one here.
    string? password = credentials.Get(profile.Id);
    if (profile.Authentication == AuthMethod.Password && string.IsNullOrEmpty(password))
    {
        throw new InvalidOperationException("No password is stored for this account. Run: ead set-password \"" + profile.Name + "\"");
    }

    var service = new MailSyncService(
        new ImageLibrary(paths.LibraryFile),
        new SyncStateStore(paths.SyncStateFile),
        credentials,
        paths.LockFile);

    Console.WriteLine(await service.TestConnectionAsync(profile, password));
    return 0;
}

int SetPassword(string[] names)
{
    if (names.Length != 1)
    {
        Console.Error.WriteLine("Usage: ead set-password <account name>");
        return 2;
    }

    AccountProfile profile = FindProfile(names[0]) ?? throw new InvalidOperationException($"No account named \"{names[0]}\".");
    Console.Write($"Password for {profile.UserName}: ");
    string password = ReadHidden();
    if (password.Length == 0)
    {
        Console.Error.WriteLine("No password entered; nothing changed.");
        return 1;
    }

    credentials.Set(profile.Id, password);
    Console.WriteLine("Password stored.");
    return 0;
}

int ImportLegacy(string[] locations)
{
    if (locations.Length != 1)
    {
        Console.Error.WriteLine("Usage: ead import-legacy <old .cfg file or the old app's cfg folder>");
        return 2;
    }

    string location = locations[0];
    string[] files = Directory.Exists(location)
        ? Directory.GetFiles(location, "*.cfg")
        : File.Exists(location) ? new[] { location } : Array.Empty<string>();

    if (files.Length == 0)
    {
        Console.Error.WriteLine("No .cfg files found there.");
        return 1;
    }

    var profiles = catalog.Snapshot().ToList();
    int imported = 0;
    foreach (string file in files)
    {
        ImportedProfile item = LegacyCfgImporter.Import(file);
        if (string.IsNullOrWhiteSpace(item.Profile.SaveFolder))
        {
            item.Profile.SaveFolder = AccountProfile.DefaultSaveFolder();
        }

        if (profiles.Any(p => p.Name.Equals(item.Profile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"Skipped {Path.GetFileName(file)}: an account with that name already exists.");
            continue;
        }

        profiles.Add(item.Profile);
        if (item.Password.Length > 0)
        {
            credentials.Set(item.Profile.Id, item.Password);
        }

        imported++;
        Console.WriteLine($"Imported {item.Profile.Name} ({item.Profile.Protocol} {item.Profile.Host}).");
    }

    catalog.Replace(profiles);
    Console.WriteLine($"{imported} account(s) imported. Passwords were moved into protected storage; you can delete the old .cfg files.");
    Console.WriteLine("Note: a single keyword now filters properly. The old app downloaded everything in that case.");
    return 0;
}

static string ReadHidden()
{
    var builder = new StringBuilder();
    while (true)
    {
        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            break;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (builder.Length > 0)
            {
                builder.Length--;
            }
        }
        else if (!char.IsControl(key.KeyChar))
        {
            builder.Append(key.KeyChar);
        }
    }

    Console.WriteLine();
    return builder.ToString();
}

static void PrintHelp()
{
    Console.WriteLine("""
        ead - Email Attachment Downloader (command line)

        Usage:
          ead list                       Show configured accounts
          ead run [account ...]          Sync now and exit (all enabled accounts if none named)
          ead test <account>             Check the server connection and sign-in
          ead set-password <account>     Store an account's password (prompted, not echoed)
          ead import-legacy <path>       Import old Java-era .cfg file(s) and their passwords
          ead paths                      Show where settings and logs live

        Tip: schedule "ead run" with Windows Task Scheduler to sync with no resident process.
        """);
}
