using MimeKit;

namespace EmailAttachmentDownloader.Core.Tests;

public class RecipientsTests
{
    [Fact]
    public void Parse_splits_on_commas_semicolons_and_newlines_and_drops_repeats()
    {
        List<string> result = Recipients.Parse("a@example.com, B@Example.com;\nc@example.com,  a@example.com");

        Assert.Equal(new[] { "a@example.com", "B@Example.com", "c@example.com" }, result);
    }

    [Fact]
    public void Parse_treats_gmail_dot_variants_as_the_same_address()
    {
        Assert.Single(Recipients.Parse("you@gmail.com, y.o.u@gmail.com, YOU@googlemail.com"));
    }

    [Fact]
    public void Parse_of_null_or_blank_is_empty()
    {
        Assert.Empty(Recipients.Parse(null));
        Assert.Empty(Recipients.Parse(" , ; "));
    }

    [Theory]
    [InlineData("you@gmail.com")]
    [InlineData("you+frames@gmail.com")]
    [InlineData("first.last@example.co.uk")]
    [InlineData("  spaced@example.com  ")]
    public void Valid_addresses_have_no_problem(string entry)
    {
        Assert.Null(Recipients.Problem(entry));
    }

    [Theory]
    [InlineData("bob")]
    [InlineData("bob@")]
    [InlineData("@example.com")]
    [InlineData("bob@gmail")]
    [InlineData("bob @example.com")]
    [InlineData("Bob <bob@example.com>")]
    [InlineData("bob@@example.com")]
    public void Invalid_addresses_are_reported(string entry)
    {
        Assert.NotNull(Recipients.Problem(entry));
    }

    [Fact]
    public void Problems_names_every_bad_entry_and_ignores_blanks()
    {
        IReadOnlyList<string> problems = Recipients.Problems(new[] { "ok@example.com", "nope", "", "also bad" });

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("nope", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("also bad", StringComparison.Ordinal));
    }

    [Fact]
    public void Matching_ignores_case()
    {
        Assert.True(Recipients.IsMatch(new[] { "You@Example.com" }, new[] { "you@example.com" }));
    }

    [Fact]
    public void Plus_addresses_must_match_exactly()
    {
        var wantedTagged = new[] { "you+frames@gmail.com" };
        var wantedPlain = new[] { "you@gmail.com" };

        Assert.True(Recipients.IsMatch(new[] { "you+frames@gmail.com" }, wantedTagged));
        Assert.False(Recipients.IsMatch(new[] { "you@gmail.com" }, wantedTagged));
        Assert.False(Recipients.IsMatch(new[] { "you+other@gmail.com" }, wantedTagged));
        Assert.False(Recipients.IsMatch(new[] { "you+frames@gmail.com" }, wantedPlain));
    }

    [Fact]
    public void Gmail_dots_are_ignored_but_other_domains_are_exact()
    {
        Assert.True(Recipients.IsMatch(new[] { "y.o.u@gmail.com" }, new[] { "you@gmail.com" }));
        Assert.True(Recipients.IsMatch(new[] { "you@gmail.com" }, new[] { "y.ou@gmail.com" }));
        Assert.True(Recipients.IsMatch(new[] { "you@googlemail.com" }, new[] { "you@gmail.com" }));
        Assert.True(Recipients.IsMatch(new[] { "y.o.u+frames@gmail.com" }, new[] { "you+frames@gmail.com" }));

        Assert.False(Recipients.IsMatch(new[] { "y.o.u@example.com" }, new[] { "you@example.com" }));
    }

    [Fact]
    public void Dots_in_a_plus_tag_stay_exact()
    {
        Assert.False(Recipients.IsMatch(new[] { "you+a.b@gmail.com" }, new[] { "you+ab@gmail.com" }));
    }

    [Fact]
    public void Matching_needs_only_one_address_to_agree()
    {
        Assert.True(Recipients.IsMatch(new[] { "x@example.com", "me@example.com" }, new[] { "me@example.com", "other@example.com" }));
        Assert.False(Recipients.IsMatch(new[] { "x@example.com" }, new[] { "me@example.com" }));
        Assert.False(Recipients.IsMatch(Array.Empty<string>(), new[] { "me@example.com" }));
    }

    [Fact]
    public void An_empty_wanted_list_matches_nothing_so_callers_must_check_the_filter_is_on()
    {
        Assert.False(Recipients.IsMatch(new[] { "me@example.com" }, Array.Empty<string>()));
    }

    [Fact]
    public void Accepts_lets_everything_through_unless_the_filter_is_on_and_has_addresses()
    {
        var others = new[] { "someone@example.com" };

        var off = new AccountProfile { RecipientFilterEnabled = false, ToAddresses = new List<string> { "me@example.com" } };
        Assert.True(Recipients.Accepts(off, others));

        var onButEmpty = new AccountProfile { RecipientFilterEnabled = true };
        Assert.True(Recipients.Accepts(onButEmpty, others));

        var on = new AccountProfile { RecipientFilterEnabled = true, ToAddresses = new List<string> { "me@example.com" } };
        Assert.False(Recipients.Accepts(on, others));
        Assert.True(Recipients.Accepts(on, new[] { "me@example.com" }));
    }

    [Fact]
    public void From_reads_to_and_cc_and_flattens_groups()
    {
        var to = new InternetAddressList
        {
            new MailboxAddress("A", "a@example.com"),
            new GroupAddress("team", new InternetAddress[] { new MailboxAddress("B", "b@example.com") }),
        };
        var cc = new InternetAddressList { new MailboxAddress("C", "c@example.com") };

        Assert.Equal(new[] { "a@example.com", "b@example.com", "c@example.com" }, Recipients.From(to, cc));
        Assert.Empty(Recipients.From(null, null));
    }

    [Fact]
    public void FromHeaders_parses_raw_header_text_and_tolerates_garbage()
    {
        List<string> parsed = Recipients.FromHeaders("Alice <alice@example.com>, bob@example.com", "\"Carol, C\" <carol@example.com>");

        Assert.Equal(new[] { "alice@example.com", "bob@example.com", "carol@example.com" }, parsed);
        Assert.Empty(Recipients.FromHeaders(null, ""));
    }
}

public class RecipientProfileTests
{
    private static AccountProfile Valid() => new()
    {
        UserName = "u",
        SaveFolder = Path.Combine(Path.GetTempPath(), "pics"),
    };

    [Fact]
    public void A_valid_list_passes_validation()
    {
        AccountProfile p = Valid();
        p.RecipientFilterEnabled = true;
        p.ToAddresses = new List<string> { "me@gmail.com", "me+frames@gmail.com" };

        Assert.Empty(p.Validate());
    }

    [Fact]
    public void An_invalid_address_blocks_saving_even_while_the_filter_is_off()
    {
        AccountProfile p = Valid();
        p.RecipientFilterEnabled = false;
        p.ToAddresses = new List<string> { "me@gmail.com", "oops" };

        Assert.Contains(p.Validate(), m => m.Contains("oops", StringComparison.Ordinal));
    }

    [Fact]
    public void Turning_the_filter_on_with_no_addresses_is_flagged()
    {
        AccountProfile p = Valid();
        p.RecipientFilterEnabled = true;

        Assert.Contains(p.Validate(), m => m.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UsesRecipientFilter_needs_both_the_switch_and_an_address()
    {
        var p = new AccountProfile();
        Assert.False(p.UsesRecipientFilter);

        p.ToAddresses.Add("me@gmail.com");
        Assert.False(p.UsesRecipientFilter);

        p.RecipientFilterEnabled = true;
        Assert.True(p.UsesRecipientFilter);
    }

    [Fact]
    public void Clone_does_not_share_the_address_list()
    {
        var original = new AccountProfile();
        original.ToAddresses.Add("a@example.com");

        AccountProfile copy = original.Clone();
        copy.ToAddresses.Add("b@example.com");

        Assert.Single(original.ToAddresses);
    }

    [Fact]
    public void The_filter_signature_changes_with_the_list_only_while_the_filter_is_in_use()
    {
        string baseline = FilterSignature.Compute(new AccountProfile());

        // Off: a stored list must not make an existing mailbox rescan.
        var offWithList = new AccountProfile { ToAddresses = new List<string> { "me@gmail.com" } };
        Assert.Equal(baseline, FilterSignature.Compute(offWithList));

        var on = new AccountProfile { RecipientFilterEnabled = true, ToAddresses = new List<string> { "me@gmail.com" } };
        string onSignature = FilterSignature.Compute(on);
        Assert.NotEqual(baseline, onSignature);

        // Equivalent spellings and ordering do not matter...
        var sameMeaning = new AccountProfile
        {
            RecipientFilterEnabled = true,
            ToAddresses = new List<string> { "M.E@Gmail.com" },
        };
        Assert.Equal(onSignature, FilterSignature.Compute(sameMeaning));

        // ...a different address does.
        on.ToAddresses.Add("other@gmail.com");
        Assert.NotEqual(onSignature, FilterSignature.Compute(on));
    }

    [Fact]
    public void Profiles_saved_before_the_filter_existed_load_with_it_off()
    {
        using var dir = new TempDir();
        string path = dir.File("profiles.json");
        File.WriteAllText(
            path,
            "{\"version\":1,\"profiles\":[{\"id\":\"old\",\"name\":\"Old\",\"host\":\"imap.example.com\",\"userName\":\"u\"}]}");

        AccountProfile loaded = Assert.Single(new ProfileStore(path).Load());

        Assert.False(loaded.RecipientFilterEnabled);
        Assert.Empty(loaded.ToAddresses);
    }

    [Fact]
    public void The_filter_settings_round_trip()
    {
        using var dir = new TempDir();
        string path = dir.File("profiles.json");
        var p = new AccountProfile
        {
            Id = "r",
            RecipientFilterEnabled = true,
            ToAddresses = new List<string> { "me@gmail.com", "me+frames@gmail.com" },
        };

        new ProfileStore(path).Save(new[] { p });
        AccountProfile back = Assert.Single(new ProfileStore(path).Load());

        Assert.True(back.RecipientFilterEnabled);
        Assert.Equal(new[] { "me@gmail.com", "me+frames@gmail.com" }, back.ToAddresses);
    }
}
