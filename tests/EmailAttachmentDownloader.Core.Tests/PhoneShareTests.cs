namespace EmailAttachmentDownloader.Core.Tests;

public class PhoneShareTests
{
    private static AccountProfile Phone(string tag = "a1b2c3d4e5") => new()
    {
        Kind = AccountKind.PhoneShare,
        ShareTag = tag,
        Name = "Frames",
        UserName = "me@gmail.com",
        SaveFolder = Path.Combine(Path.GetTempPath(), "ead-phone-share-tests"),
    };

    // ----- model ---------------------------------------------------------------------------------

    [Fact]
    public void A_profile_saved_before_kinds_existed_loads_as_mail()
    {
        using var dir = new TempDir();
        string path = dir.File("profiles.json");
        File.WriteAllText(path, """
            { "version": 1, "profiles": [ { "id": "old", "name": "Old", "host": "imap.gmail.com", "userName": "me@gmail.com" } ] }
            """);

        AccountProfile loaded = Assert.Single(new ProfileStore(path).Load());

        Assert.Equal(AccountKind.Mail, loaded.Kind);
        Assert.False(loaded.IsPhoneShare);
        Assert.Equal("", loaded.ShareTag);
    }

    [Fact]
    public void Kind_tag_and_address_survive_a_save_and_load()
    {
        using var dir = new TempDir();
        string path = dir.File("profiles.json");
        AccountProfile original = Phone();
        original.ShareAddress = "me+frames@gmail.com";

        var store = new ProfileStore(path);
        store.Save(new[] { original });
        AccountProfile loaded = Assert.Single(store.Load());

        Assert.Equal(AccountKind.PhoneShare, loaded.Kind);
        Assert.Equal("a1b2c3d4e5", loaded.ShareTag);
        Assert.Equal("me+frames@gmail.com", loaded.ShareAddress);
        Assert.Contains("\"PhoneShare\"", File.ReadAllText(path));
    }

    [Fact]
    public void SendToAddress_falls_back_to_the_user_name()
    {
        AccountProfile p = Phone();
        Assert.Equal("me@gmail.com", p.SendToAddress);

        p.ShareAddress = "  me+frames@gmail.com ";
        Assert.Equal("me+frames@gmail.com", p.SendToAddress);
    }

    [Fact]
    public void A_phone_share_profile_with_a_tag_is_valid()
    {
        Assert.Empty(Phone().Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("UPPERCASE123")]
    [InlineData("has space 123")]
    [InlineData("[ead:abcdef12]")]
    public void A_phone_share_profile_needs_a_valid_tag(string tag)
    {
        IReadOnlyList<string> problems = Phone(tag).Validate();

        Assert.Contains(problems, p => p.Contains("share tag", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_bad_send_to_address_is_reported()
    {
        AccountProfile p = Phone();
        p.ShareAddress = "not an address";

        Assert.Contains(p.Validate(), problem => problem.StartsWith("Send photos to:", StringComparison.Ordinal));
    }

    [Fact]
    public void Leftover_recipient_filter_settings_do_not_block_or_apply_to_a_phone_share_profile()
    {
        AccountProfile p = Phone();
        p.RecipientFilterEnabled = true;
        p.ToAddresses = new List<string>(); // would be an error for a mail account

        Assert.Empty(p.Validate());
        Assert.False(p.UsesRecipientFilter);
        Assert.True(Recipients.Accepts(p, new[] { "someone-else@example.com" }));
    }

    // ----- matching ------------------------------------------------------------------------------

    [Fact]
    public void Phone_share_needs_the_marker_in_the_subject()
    {
        AccountProfile p = Phone("a1b2c3d4e5");

        Assert.True(SubjectMatcher.IsMatch(p, "[ead:a1b2c3d4e5] Photos from iPhone (3)"));
        Assert.True(SubjectMatcher.IsMatch(p, "Fwd: [EAD:A1B2C3D4E5] shared"));
        Assert.False(SubjectMatcher.IsMatch(p, "Photos from iPhone"));
        Assert.False(SubjectMatcher.IsMatch(p, "[ead:ffffffffff] Photos from iPhone"));
        Assert.False(SubjectMatcher.IsMatch(p, ""));
        Assert.False(SubjectMatcher.IsMatch(p, null));
    }

    [Fact]
    public void Phone_share_ignores_keywords()
    {
        AccountProfile p = Phone();
        p.Keywords = new List<string> { "invoice" };

        Assert.True(SubjectMatcher.IsMatch(p, "[ead:a1b2c3d4e5] holiday"));
    }

    [Fact]
    public void A_phone_share_profile_without_a_valid_tag_matches_nothing()
    {
        AccountProfile p = Phone("");

        Assert.False(SubjectMatcher.IsMatch(p, "[ead:] anything"));
        Assert.False(SubjectMatcher.IsMatch(p, "anything"));
    }

    [Fact]
    public void Mail_accounts_still_match_by_keywords()
    {
        var p = new AccountProfile { Keywords = new List<string> { "sunset" }, KeywordMode = KeywordMode.Any };

        Assert.True(SubjectMatcher.IsMatch(p, "Sunset Trail"));
        Assert.False(SubjectMatcher.IsMatch(p, "[ead:a1b2c3d4e5] holiday"));
        Assert.True(SubjectMatcher.IsMatch(new AccountProfile(), "anything at all"));
    }

    // ----- when a rescan is needed ---------------------------------------------------------------

    [Fact]
    public void Changing_the_tag_makes_the_next_check_rescan_the_mailbox()
    {
        Assert.NotEqual(FilterSignature.Compute(Phone("a1b2c3d4e5")), FilterSignature.Compute(Phone("f6e5d4c3b2")));
    }

    [Fact]
    public void Switching_a_mail_account_to_phone_share_changes_its_signature()
    {
        var mail = new AccountProfile { UserName = "me@gmail.com" };
        AccountProfile phone = mail.Clone();
        phone.Kind = AccountKind.PhoneShare;
        phone.ShareTag = "a1b2c3d4e5";

        Assert.NotEqual(FilterSignature.Compute(mail), FilterSignature.Compute(phone));
    }

    [Fact]
    public void The_phone_share_fields_do_not_change_a_mail_accounts_signature()
    {
        var plain = new AccountProfile { UserName = "me@gmail.com" };
        AccountProfile withLeftovers = plain.Clone();
        withLeftovers.ShareTag = "a1b2c3d4e5";
        withLeftovers.ShareAddress = "me+frames@gmail.com";

        Assert.Equal(FilterSignature.Compute(plain), FilterSignature.Compute(withLeftovers));
    }

    // ----- tags ----------------------------------------------------------------------------------

    [Fact]
    public void New_tags_are_valid_and_different_each_time()
    {
        var tags = Enumerable.Range(0, 50).Select(_ => ShareTags.NewTag()).ToList();

        Assert.All(tags, tag =>
        {
            Assert.Equal(ShareTags.Length, tag.Length);
            Assert.True(ShareTags.IsValid(tag));
        });
        Assert.Equal(tags.Count, tags.Distinct().Count());
    }

    [Fact]
    public void The_marker_wraps_the_tag()
    {
        Assert.Equal("[ead:a1b2c3d4e5]", ShareTags.MarkerFor("a1b2c3d4e5"));
        Assert.Equal("[ead:a1b2c3d4e5]", Phone().SubjectMarker);
    }

    // ----- pairing link --------------------------------------------------------------------------

    [Fact]
    public void A_pairing_link_round_trips()
    {
        string link = PairingLink.Build("Family frames", "me+frames@gmail.com", "a1b2c3d4e5");

        PairingInfo? info = PairingLink.TryParse(link);

        Assert.NotNull(info);
        Assert.Equal("Family frames", info!.Name);
        Assert.Equal("me+frames@gmail.com", info.ToAddress);
        Assert.Equal("a1b2c3d4e5", info.Tag);
    }

    [Fact]
    public void The_link_has_the_exact_shape_the_phone_app_expects()
    {
        // The iOS app's PairingTests parse this same string, so changing it breaks one side.
        Assert.Equal(
            "ead://pair?v=1&name=Family%20frames&to=me%2Bframes%40gmail.com&tag=a1b2c3d4e5",
            PairingLink.Build("Family frames", "me+frames@gmail.com", "a1b2c3d4e5"));
    }

    [Fact]
    public void Names_with_symbols_and_accents_survive()
    {
        string link = PairingLink.Build("Mum & Dad's frame #1 (Zoë)", "me@gmail.com", "a1b2c3d4e5");

        Assert.Equal("Mum & Dad's frame #1 (Zoë)", PairingLink.TryParse(link)!.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/pair?v=1&name=x&to=me%40gmail.com&tag=a1b2c3d4e5")]
    [InlineData("ead://other?v=1&name=x&to=me%40gmail.com&tag=a1b2c3d4e5")]
    [InlineData("ead://pair?name=x&to=me%40gmail.com&tag=a1b2c3d4e5")]
    [InlineData("ead://pair?v=2&name=x&to=me%40gmail.com&tag=a1b2c3d4e5")]
    [InlineData("ead://pair?v=0&name=x&to=me%40gmail.com&tag=a1b2c3d4e5")]
    [InlineData("ead://pair?v=1&to=me%40gmail.com&tag=a1b2c3d4e5")]
    [InlineData("ead://pair?v=1&name=x&to=nobody&tag=a1b2c3d4e5")]
    [InlineData("ead://pair?v=1&name=x&to=me%40gmail.com")]
    [InlineData("ead://pair?v=1&name=x&to=me%40gmail.com&tag=BAD")]
    public void Links_that_are_not_usable_are_rejected(string? link)
    {
        Assert.Null(PairingLink.TryParse(link));
    }

    [Fact]
    public void The_profile_produces_the_link_for_its_own_address_and_tag()
    {
        AccountProfile p = Phone();
        p.ShareAddress = "me+frames@gmail.com";

        PairingInfo? info = PairingLink.TryParse(PairingLink.Build(p.Name, p.SendToAddress, p.ShareTag));

        Assert.Equal(new PairingInfo("Frames", "me+frames@gmail.com", "a1b2c3d4e5"), info);
    }
}
