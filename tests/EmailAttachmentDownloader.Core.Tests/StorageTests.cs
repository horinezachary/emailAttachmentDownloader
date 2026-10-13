namespace EmailAttachmentDownloader.Core.Tests;

public class ImageLibraryTests
{
    private static LibraryImage Image(string sha, string path) => new(
        sha, path, Path.GetFileName(path), "acct", "subject", "from@example.com",
        DateTimeOffset.Now, DateTimeOffset.Now, 3);

    [Fact]
    public void Flushed_images_survive_a_restart_but_unflushed_ones_do_not()
    {
        using var dir = new TempDir();
        string path = dir.File("library.json");

        var library = new ImageLibrary(path);
        library.Add(Image("saved", dir.File("a.png")));
        library.Flush();
        library.Add(Image("pending", dir.File("b.png")));

        var reopened = new ImageLibrary(path);
        Assert.True(reopened.Contains("saved"));
        Assert.False(reopened.Contains("pending"));
    }

    [Fact]
    public void Reload_picks_up_changes_made_by_another_instance()
    {
        using var dir = new TempDir();
        string path = dir.File("library.json");
        var first = new ImageLibrary(path);
        var second = new ImageLibrary(path);

        second.Add(Image("x", dir.File("x.png")));
        second.Flush();

        Assert.False(first.Contains("x"));
        first.Reload();
        Assert.True(first.Contains("x"));
    }

    [Fact]
    public void AllExisting_leaves_out_files_that_were_deleted()
    {
        using var dir = new TempDir();
        string present = dir.File("present.png");
        File.WriteAllBytes(present, new byte[] { 1, 2, 3 });

        var library = new ImageLibrary(dir.File("library.json"));
        library.Add(Image("present", present));
        library.Add(Image("gone", dir.File("gone.png")));

        Assert.Equal(2, library.All().Count);
        LibraryImage only = Assert.Single(library.AllExisting());
        Assert.Equal("present", only.Sha256);
    }

    [Fact]
    public void A_corrupt_index_is_set_aside_and_replaced_with_an_empty_one()
    {
        using var dir = new TempDir();
        string path = dir.File("library.json");
        File.WriteAllText(path, "{ this is not json");

        var library = new ImageLibrary(path);

        Assert.Empty(library.All());
        Assert.Single(Directory.GetFiles(dir.Root, "library.json.corrupt-*"));
    }
}

public class SyncStateStoreTests
{
    [Fact]
    public void State_round_trips_through_the_file()
    {
        using var dir = new TempDir();
        string path = dir.File("sync-state.json");

        var store = new SyncStateStore(path);
        AccountSyncState state = store.Snapshot("a");
        state.FilterSignature = "sig";
        state.ImapUidValidity = 7;
        state.ImapLastUid = 42;
        state.Pop3SeenUids.Add("u1");
        state.LastRunAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        store.Put("a", state);

        AccountSyncState back = new SyncStateStore(path).Snapshot("a");
        Assert.Equal("sig", back.FilterSignature);
        Assert.Equal(7u, back.ImapUidValidity);
        Assert.Equal(42u, back.ImapLastUid);
        Assert.Equal(new[] { "u1" }, back.Pop3SeenUids);
        Assert.Equal(state.LastRunAt, back.LastRunAt);
    }

    [Fact]
    public void A_snapshot_is_a_copy_so_edits_do_nothing_until_Put()
    {
        using var dir = new TempDir();
        var store = new SyncStateStore(dir.File("sync-state.json"));
        AccountSyncState state = store.Snapshot("a");
        state.ImapLastUid = 42;
        store.Put("a", state);

        AccountSyncState edited = store.Snapshot("a");
        edited.ImapLastUid = 99;
        edited.Pop3SeenUids.Add("never-saved");

        AccountSyncState again = store.Snapshot("a");
        Assert.Equal(42u, again.ImapLastUid);
        Assert.Empty(again.Pop3SeenUids);
    }

    [Fact]
    public void Remove_forgets_an_account()
    {
        using var dir = new TempDir();
        var store = new SyncStateStore(dir.File("sync-state.json"));
        AccountSyncState state = store.Snapshot("a");
        state.ImapLastUid = 5;
        store.Put("a", state);

        store.Remove("a");

        Assert.Equal(0u, store.Snapshot("a").ImapLastUid);
        Assert.Null(store.LastRunAt("a"));
    }
}

public class ProfileStoreTests
{
    [Fact]
    public void Profiles_round_trip_and_the_file_never_contains_a_password()
    {
        using var dir = new TempDir();
        string path = dir.File("profiles.json");
        var profile = new AccountProfile
        {
            Id = "abc",
            Name = "Camp photos",
            Protocol = MailProtocol.Pop3,
            Host = "pop.example.com",
            Port = 995,
            Tls = TlsMode.StartTls,
            UserName = "me@example.com",
            SaveFolder = dir.File("pics"),
            Keywords = new List<string> { "sunset", "order of the arrow" },
            KeywordMode = KeywordMode.All,
            AttachmentTypes = new List<string> { "image/*", "application/pdf" },
            MinSizeKb = 4,
            RunOnStartup = true,
            Scheduled = true,
            IntervalMinutes = 30,
        };

        var store = new ProfileStore(path);
        store.Save(new[] { profile });
        AccountProfile back = Assert.Single(new ProfileStore(path).Load());

        Assert.Equal("Camp photos", back.Name);
        Assert.Equal(MailProtocol.Pop3, back.Protocol);
        Assert.Equal(TlsMode.StartTls, back.Tls);
        Assert.Equal(KeywordMode.All, back.KeywordMode);
        Assert.Equal(new[] { "sunset", "order of the arrow" }, back.Keywords);
        Assert.Equal(new[] { "image/*", "application/pdf" }, back.AttachmentTypes);
        Assert.Equal(4, back.MinSizeKb);
        Assert.Equal(30, back.IntervalMinutes);

        string text = File.ReadAllText(path);
        Assert.Contains("\"Pop3\"", text);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unreadable_file_is_kept_aside_with_a_warning_instead_of_being_overwritten()
    {
        using var dir = new TempDir();
        string path = dir.File("profiles.json");
        File.WriteAllText(path, "][ nope");

        var store = new ProfileStore(path);
        List<AccountProfile> loaded = store.Load();

        Assert.Empty(loaded);
        Assert.NotNull(store.LoadWarning);
        Assert.Single(Directory.GetFiles(dir.Root, "profiles.json.corrupt-*"));
    }

    [Fact]
    public void Validate_flags_missing_and_relative_settings()
    {
        var empty = new AccountProfile { Host = "", UserName = "", SaveFolder = "" };
        Assert.NotEmpty(empty.Validate());

        var relative = new AccountProfile { UserName = "u", SaveFolder = "pictures" };
        Assert.Contains(relative.Validate(), p => p.Contains("absolute", StringComparison.OrdinalIgnoreCase));

        var ok = new AccountProfile { UserName = "u", SaveFolder = Path.Combine(Path.GetTempPath(), "pics") };
        Assert.Empty(ok.Validate());
    }

    [Fact]
    public void Clone_does_not_share_lists()
    {
        var original = new AccountProfile();
        original.Keywords.Add("a");

        AccountProfile copy = original.Clone();
        copy.Keywords.Add("b");
        copy.AttachmentTypes.Add("application/pdf");

        Assert.Single(original.Keywords);
        Assert.Single(original.AttachmentTypes);
    }
}

public class RunHistoryTests
{
    private static RunResult Result(string name) => new(
        "id", name, DateTimeOffset.Now, TimeSpan.FromSeconds(1), 3, 2, 1, 1, 0, null, "C:\\pics");

    [Fact]
    public void Recent_returns_newest_first_and_survives_a_round_trip()
    {
        using var dir = new TempDir();
        var history = new RunHistory(dir.File("history.jsonl"));

        history.Append(Result("a"));
        history.Append(Result("b"));
        history.Append(Result("c") with { Error = "boom" });

        IReadOnlyList<RunResult> recent = history.Recent(2);
        Assert.Equal(2, recent.Count);
        Assert.Equal("c", recent[0].AccountName);
        Assert.Equal("boom", recent[0].Error);
        Assert.False(recent[0].Succeeded);
        Assert.Equal("b", recent[1].AccountName);
        Assert.Equal(TimeSpan.FromSeconds(1), recent[1].Duration);
    }

    [Fact]
    public void The_file_is_capped_at_MaxEntries()
    {
        using var dir = new TempDir();
        var history = new RunHistory(dir.File("history.jsonl"));

        for (int i = 0; i < RunHistory.MaxEntries + 5; i++)
        {
            history.Append(Result("run-" + i));
        }

        IReadOnlyList<RunResult> all = history.Recent(10_000);
        Assert.Equal(RunHistory.MaxEntries, all.Count);
        Assert.Equal("run-" + (RunHistory.MaxEntries + 4), all[0].AccountName);
    }

    [Fact]
    public void A_damaged_line_is_skipped_not_fatal()
    {
        using var dir = new TempDir();
        string path = dir.File("history.jsonl");
        var history = new RunHistory(path);
        history.Append(Result("good"));
        File.AppendAllText(path, "{ not json" + Environment.NewLine);

        IReadOnlyList<RunResult> recent = history.Recent(10);

        Assert.Equal("good", Assert.Single(recent).AccountName);
    }
}

public class CredentialStoreTests
{
    [Fact]
    public void Dpapi_round_trips_and_never_writes_the_password_in_plain_text()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // DPAPI exists only on Windows; the CI runner is Windows.
        }

        using var dir = new TempDir();
        string path = dir.File("credentials.json");

        var store = new DpapiCredentialStore(path);
        store.Set("acct", "hunter2-correct-horse");

        Assert.Equal("hunter2-correct-horse", store.Get("acct"));
        Assert.Equal("hunter2-correct-horse", new DpapiCredentialStore(path).Get("acct"));
        Assert.DoesNotContain("hunter2", File.ReadAllText(path));
        Assert.Null(store.Get("someone-else"));

        store.Remove("acct");
        Assert.Null(store.Get("acct"));
    }

    [Fact]
    public void The_memory_store_behaves_like_a_dictionary()
    {
        var store = new MemoryCredentialStore();
        Assert.Null(store.Get("a"));

        store.Set("a", "one");
        store.Set("a", "two");
        Assert.Equal("two", store.Get("a"));

        store.Remove("a");
        Assert.Null(store.Get("a"));
    }

    [Fact]
    public void The_environment_fallback_is_read_only()
    {
        Assert.Equal("EAD_PASSWORD_ABC123", EnvironmentCredentialStore.VariableName("abc123"));
        Assert.Throws<NotSupportedException>(() => new EnvironmentCredentialStore().Set("abc123", "x"));
    }
}
