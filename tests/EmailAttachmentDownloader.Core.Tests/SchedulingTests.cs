namespace EmailAttachmentDownloader.Core.Tests;

public class SchedulingTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static AccountProfile Scheduled(int intervalMinutes) =>
        new() { Scheduled = true, Enabled = true, IntervalMinutes = intervalMinutes };

    [Fact]
    public void Accounts_that_are_not_scheduled_or_are_disabled_are_never_due()
    {
        Assert.False(SyncScheduler.IsDue(new AccountProfile { Scheduled = false }, null, Now));
        Assert.False(SyncScheduler.IsDue(new AccountProfile { Scheduled = true, Enabled = false }, null, Now));
    }

    [Fact]
    public void A_scheduled_account_that_has_never_run_is_due()
    {
        Assert.True(SyncScheduler.IsDue(Scheduled(60), null, Now));
    }

    [Fact]
    public void An_account_is_due_once_its_interval_has_passed()
    {
        Assert.False(SyncScheduler.IsDue(Scheduled(60), Now.AddMinutes(-30), Now));
        Assert.True(SyncScheduler.IsDue(Scheduled(60), Now.AddMinutes(-61), Now));
        Assert.True(SyncScheduler.IsDue(Scheduled(60), Now.AddMinutes(-60), Now));
    }

    [Fact]
    public void The_interval_is_never_shorter_than_the_minimum()
    {
        Assert.False(SyncScheduler.IsDue(Scheduled(1), Now.AddMinutes(-3), Now));
        Assert.True(SyncScheduler.IsDue(Scheduled(1), Now.AddMinutes(-6), Now));
    }
}

public class FilterSignatureTests
{
    private static AccountProfile Profile() => new()
    {
        Host = "imap.example.com",
        UserName = "me@example.com",
        Keywords = new List<string> { "sunset", "trail" },
    };

    [Fact]
    public void The_same_settings_give_the_same_signature_regardless_of_keyword_order_or_case()
    {
        AccountProfile a = Profile();
        AccountProfile b = Profile();
        b.Keywords = new List<string> { "TRAIL", "Sunset" };

        Assert.Equal(FilterSignature.Compute(a), FilterSignature.Compute(b));
    }

    [Fact]
    public void Settings_that_change_which_mail_qualifies_change_the_signature()
    {
        string baseline = FilterSignature.Compute(Profile());

        AccountProfile moreKeywords = Profile();
        moreKeywords.Keywords.Add("camp");
        Assert.NotEqual(baseline, FilterSignature.Compute(moreKeywords));

        AccountProfile allMode = Profile();
        allMode.KeywordMode = KeywordMode.All;
        Assert.NotEqual(baseline, FilterSignature.Compute(allMode));

        AccountProfile pdfs = Profile();
        pdfs.AttachmentTypes.Add("application/pdf");
        Assert.NotEqual(baseline, FilterSignature.Compute(pdfs));

        AccountProfile otherServer = Profile();
        otherServer.Host = "imap.other.example";
        Assert.NotEqual(baseline, FilterSignature.Compute(otherServer));
    }

    [Fact]
    public void Settings_that_do_not_affect_matching_leave_the_signature_alone()
    {
        string baseline = FilterSignature.Compute(Profile());

        AccountProfile renamed = Profile();
        renamed.Name = "Something else";
        renamed.SaveFolder = "C:\\elsewhere";
        renamed.Scheduled = true;
        renamed.IntervalMinutes = 15;
        renamed.RunOnStartup = true;

        Assert.Equal(baseline, FilterSignature.Compute(renamed));
    }
}
