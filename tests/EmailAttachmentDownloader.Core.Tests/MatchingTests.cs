namespace EmailAttachmentDownloader.Core.Tests;

public class KeywordsTests
{
    [Fact]
    public void Parse_splits_on_commas_semicolons_and_newlines_but_keeps_phrases()
    {
        List<string> result = Keywords.Parse("sunset, trail ;order of the arrow\nfoo");

        Assert.Equal(new[] { "sunset", "trail", "order of the arrow", "foo" }, result);
    }

    [Fact]
    public void Parse_removes_duplicates_ignoring_case()
    {
        Assert.Equal(new[] { "Sunset" }, Keywords.Parse("Sunset, sunset,SUNSET"));
    }

    [Fact]
    public void Parse_of_null_or_blank_is_empty()
    {
        Assert.Empty(Keywords.Parse(null));
        Assert.Empty(Keywords.Parse("  , ;  "));
    }

    [Fact]
    public void No_keywords_matches_everything_including_a_missing_subject()
    {
        Assert.True(Keywords.IsMatch(null, Array.Empty<string>(), KeywordMode.Any));
        Assert.True(Keywords.IsMatch("anything", new[] { "", "  " }, KeywordMode.All));
    }

    [Fact]
    public void A_missing_subject_never_matches_when_keywords_exist()
    {
        Assert.False(Keywords.IsMatch(null, new[] { "a" }, KeywordMode.Any));
        Assert.False(Keywords.IsMatch("", new[] { "a" }, KeywordMode.Any));
    }

    [Fact]
    public void Matching_is_a_case_insensitive_substring_test()
    {
        Assert.True(Keywords.IsMatch("Sunset Trail photos", new[] { "sunset" }, KeywordMode.Any));
        Assert.True(Keywords.IsMatch("SUNSETS", new[] { "sunset" }, KeywordMode.Any));
    }

    [Fact]
    public void A_single_keyword_filters_like_any_other()
    {
        // The old Java app treated exactly one keyword as "no keywords" and downloaded everything.
        Assert.False(Keywords.IsMatch("Weekly newsletter", new[] { "photo" }, KeywordMode.Any));
        Assert.True(Keywords.IsMatch("Photo of the week", new[] { "photo" }, KeywordMode.Any));
    }

    [Fact]
    public void Any_needs_one_keyword_and_All_needs_every_keyword()
    {
        var keywords = new[] { "sunset", "camp" };

        Assert.True(Keywords.IsMatch("Sunset Trail", keywords, KeywordMode.Any));
        Assert.False(Keywords.IsMatch("Sunset Trail", keywords, KeywordMode.All));
        Assert.True(Keywords.IsMatch("Sunset camp", keywords, KeywordMode.All));
    }
}

public class AttachmentFilterTests
{
    [Fact]
    public void Wildcard_and_exact_patterns_work()
    {
        Assert.True(AttachmentFilter.Matches("image", "png", new[] { "image/*" }));
        Assert.False(AttachmentFilter.Matches("application", "pdf", new[] { "image/*" }));
        Assert.True(AttachmentFilter.Matches("application", "pdf", new[] { "image/*", "application/pdf" }));
        Assert.True(AttachmentFilter.Matches("video", "mp4", new[] { "*/*" }));
        Assert.True(AttachmentFilter.Matches("video", "mp4", new[] { "*" }));
    }

    [Fact]
    public void Patterns_are_case_insensitive_and_blank_patterns_are_ignored()
    {
        Assert.True(AttachmentFilter.Matches("image", "png", new[] { " IMAGE/* " }));
        Assert.False(AttachmentFilter.Matches("image", "png", new[] { "", "  " }));
    }

    [Fact]
    public void Extensions_come_from_the_content_type()
    {
        Assert.Equal(".jpg", AttachmentFilter.ExtensionFor("image", "jpeg"));
        Assert.Equal(".png", AttachmentFilter.ExtensionFor("image", "png"));
        Assert.Equal(".pdf", AttachmentFilter.ExtensionFor("application", "pdf"));
        Assert.Equal(".avif", AttachmentFilter.ExtensionFor("image", "avif"));
        Assert.Equal(".bin", AttachmentFilter.ExtensionFor("application", "octet-stream"));
    }
}

public class FileNamesTests
{
    [Fact]
    public void A_normal_name_is_unchanged()
    {
        Assert.Equal("holiday.jpg", FileNames.Sanitize("holiday.jpg", "pic", ".jpg"));
    }

    [Theory]
    [InlineData("..\\..\\Windows\\evil.png", "evil.png")]
    [InlineData("../../etc/evil.png", "evil.png")]
    [InlineData("C:\\temp\\evil.png", "evil.png")]
    public void Directory_parts_are_dropped(string input, string expected)
    {
        Assert.Equal(expected, FileNames.Sanitize(input, "pic", ".jpg"));
    }

    [Fact]
    public void A_missing_name_uses_the_fallbacks()
    {
        Assert.Equal("pic.jpg", FileNames.Sanitize(null, "pic", ".jpg"));
        Assert.Equal("pic.png", FileNames.Sanitize(".png", "pic", ".jpg"));
    }

    [Fact]
    public void A_missing_extension_is_filled_in_with_or_without_the_dot()
    {
        Assert.Equal("passwd.jpg", FileNames.Sanitize("../../etc/passwd", "pic", ".jpg"));
        Assert.Equal("photo.png", FileNames.Sanitize("photo", "pic", "png"));
    }

    [Fact]
    public void Invalid_characters_become_underscores()
    {
        Assert.Equal("a_b_c_d.png", FileNames.Sanitize("a<b>c:d.png", "pic", ".jpg"));
        Assert.Equal("a_b.png", FileNames.Sanitize("a\u0001b.png", "pic", ".jpg"));
    }

    [Fact]
    public void Trailing_dots_and_spaces_are_removed()
    {
        Assert.Equal("report.pdf", FileNames.Sanitize("report. ", "doc", ".pdf"));
    }

    [Theory]
    [InlineData("CON.png", "_CON.png")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("com1.png", "_com1.png")]
    [InlineData("console.png", "console.png")]
    public void Reserved_device_names_are_prefixed(string input, string expected)
    {
        Assert.Equal(expected, FileNames.Sanitize(input, "pic", ".jpg"));
    }

    [Fact]
    public void Very_long_names_are_shortened_but_keep_their_extension()
    {
        string result = FileNames.Sanitize(new string('a', 300) + ".png", "pic", ".jpg");

        Assert.Equal(124, result.Length);
        Assert.EndsWith(".png", result);
    }
}
