namespace EmailAttachmentDownloader.Core.Tests;

public class SyncProgressTests
{
    private static SyncProgress P(SyncPhase phase, int done = 0, int total = 0, int matched = 0, int files = 0) =>
        new("id", "Frames", phase, done, total, matched, files);

    [Fact]
    public void Fraction_is_unknown_until_there_is_a_total_and_never_passes_one()
    {
        Assert.Null(P(SyncPhase.Listing, 50).Fraction);
        Assert.Equal(0.25, P(SyncPhase.Checking, 25, 100).Fraction);
        Assert.Equal(1.0, P(SyncPhase.Checking, 120, 100).Fraction);
    }

    [Fact]
    public void Each_phase_reads_as_a_plain_sentence()
    {
        Assert.Equal("Signing in...", P(SyncPhase.SigningIn).Describe());
        Assert.Equal("Connecting to the mail server...", P(SyncPhase.Connecting).Describe());
        Assert.Equal("Reading the mailbox...", P(SyncPhase.Listing).Describe());
        Assert.Contains("1,200 e-mails so far", P(SyncPhase.Listing, 1200).Describe(), StringComparison.Ordinal);
        Assert.Contains("300 of 1,200 e-mails", P(SyncPhase.Listing, 300, 1200).Describe(), StringComparison.Ordinal);
        Assert.StartsWith("Checking e-mails: 5 of 20", P(SyncPhase.Checking, 5, 20).Describe(), StringComparison.Ordinal);
        Assert.StartsWith("Downloading a matching e-mail", P(SyncPhase.Downloading, 5, 20).Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_of_matches_and_pictures_are_shown_once_there_are_some()
    {
        Assert.DoesNotContain("matching,", P(SyncPhase.Checking, 5, 20).Describe(), StringComparison.Ordinal);
        Assert.Contains("(3 matching, 2 new picture(s))", P(SyncPhase.Checking, 5, 20, 3, 2).Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Activity_entries_say_whether_they_are_running_or_waiting()
    {
        var waiting = new SyncActivity("id", "Frames", false, null, null);
        var starting = new SyncActivity("id", "Frames", true, null, DateTimeOffset.Now);
        var going = new SyncActivity("id", "Frames", true, P(SyncPhase.Checking, 1, 2), DateTimeOffset.Now);

        Assert.Equal("Waiting for its turn", waiting.Describe());
        Assert.Equal("Starting...", starting.Describe());
        Assert.StartsWith("Checking e-mails", going.Describe(), StringComparison.Ordinal);
    }
}

public class ProgressThrottleTests
{
    [Fact]
    public void The_first_report_always_goes_through()
    {
        Assert.True(new ProgressThrottle().ShouldReport(SyncPhase.Checking, 0));
    }

    [Fact]
    public void Reports_in_the_same_phase_are_spaced_out()
    {
        var throttle = new ProgressThrottle(250);

        Assert.True(throttle.ShouldReport(SyncPhase.Checking, 1000));
        Assert.False(throttle.ShouldReport(SyncPhase.Checking, 1100));
        Assert.False(throttle.ShouldReport(SyncPhase.Checking, 1249));
        Assert.True(throttle.ShouldReport(SyncPhase.Checking, 1250));
    }

    [Fact]
    public void A_new_phase_is_reported_straight_away()
    {
        var throttle = new ProgressThrottle(250);
        throttle.ShouldReport(SyncPhase.Checking, 1000);

        Assert.True(throttle.ShouldReport(SyncPhase.Downloading, 1001));
        Assert.True(throttle.ShouldReport(SyncPhase.Checking, 1002));
    }

    [Fact]
    public void A_huge_mailbox_produces_a_handful_of_reports_not_one_per_message()
    {
        var throttle = new ProgressThrottle(250);
        int reports = 0;
        for (int i = 0; i < 50_000; i++)
        {
            // 50,000 messages examined over 10 seconds.
            if (throttle.ShouldReport(SyncPhase.Checking, i / 5))
            {
                reports++;
            }
        }

        Assert.InRange(reports, 30, 50);
    }
}

public class SyncActivityTests
{
    private static AccountProfile Account(TempDir dir, string id, string name) => new()
    {
        Id = id,
        Name = name,
        UserName = "me@example.com",
        Host = "imap.example.com",
        SaveFolder = dir.File("pics-" + id),
    };

    private static MailSyncService Service(TempDir dir) => new(
        new ImageLibrary(dir.File("library.json")),
        new SyncStateStore(dir.File("state.json")),
        new MemoryCredentialStore(),
        dir.File("sync.lock"));

    [Fact]
    public async Task A_run_reports_its_first_step_before_it_does_anything_slow()
    {
        using var dir = new TempDir();
        MailSyncService service = Service(dir);
        var seen = new List<SyncProgress>();
        service.Progress += (_, p) => seen.Add(p);

        RunResult result = await service.RunAsync(Account(dir, "a", "Alpha")); // no password stored, so it stops early

        Assert.False(result.Succeeded);
        SyncProgress first = Assert.Single(seen);
        Assert.Equal(SyncPhase.Connecting, first.Phase);
        Assert.Equal("a", first.AccountId);
        Assert.Equal("Alpha", first.AccountName);
    }

    [Fact]
    public void Queued_accounts_are_listed_in_order_and_each_queueing_is_announced()
    {
        using var dir = new TempDir();
        var profiles = new List<AccountProfile> { Account(dir, "a", "Alpha"), Account(dir, "b", "Beta") };
        var scheduler = new SyncScheduler(
            () => profiles, Service(dir), new RunHistory(dir.File("history.jsonl")), new SyncStateStore(dir.File("sched-state.json")));
        int announced = 0;
        scheduler.ActivityChanged += (_, _) => announced++;

        // Not started, so nothing runs and everything stays queued.
        Assert.True(scheduler.RequestRun("a"));
        Assert.True(scheduler.RequestRun("b"));
        Assert.False(scheduler.RequestRun("a"));

        IReadOnlyList<SyncActivity> activity = scheduler.CurrentActivity();
        Assert.Equal(new[] { "Alpha", "Beta" }, activity.Select(a => a.AccountName));
        Assert.All(activity, a => Assert.False(a.IsRunning));
        Assert.Equal(2, announced);
    }

    [Fact]
    public async Task A_started_scheduler_shows_the_account_as_running_and_then_clears_it()
    {
        using var dir = new TempDir();
        var profiles = new List<AccountProfile> { Account(dir, "a", "Alpha") };
        var scheduler = new SyncScheduler(
            () => profiles, Service(dir), new RunHistory(dir.File("history.jsonl")), new SyncStateStore(dir.File("sched-state.json")));

        var snapshots = new List<IReadOnlyList<SyncActivity>>();
        var completed = new TaskCompletionSource();
        scheduler.ActivityChanged += (_, _) =>
        {
            lock (snapshots)
            {
                snapshots.Add(scheduler.CurrentActivity());
            }
        };
        scheduler.RunCompleted += (_, _) => completed.TrySetResult();

        scheduler.Start();
        try
        {
            Assert.True(scheduler.RequestRun("a"));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));

            // The run has finished, but the worker clears its state a moment later.
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (scheduler.CurrentActivity().Count > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.Empty(scheduler.CurrentActivity());
            lock (snapshots)
            {
                Assert.Contains(snapshots, s => s.Count == 1 && s[0].IsRunning && s[0].AccountName == "Alpha");
            }
        }
        finally
        {
            await scheduler.DisposeAsync();
        }
    }
}
