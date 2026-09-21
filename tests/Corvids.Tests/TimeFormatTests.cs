using Corvids.Services;

namespace Corvids.Tests;

public class TimeFormatTests
{
    private static readonly DateTime Sample = new(2026, 9, 21, 15, 4, 9, 123);

    [Fact]
    public void Default_pattern_is_yy_mm_dd_hh_mm_ss()
    {
        Assert.Equal("26-09-21 15:04:09", TimeFormat.Format(Sample, TimeFormat.Default));
    }

    [Fact]
    public void Null_or_empty_pattern_falls_back_to_default()
    {
        Assert.Equal(TimeFormat.Format(Sample, TimeFormat.Default), TimeFormat.Format(Sample, null));
        Assert.Equal(TimeFormat.Format(Sample, TimeFormat.Default), TimeFormat.Format(Sample, "   "));
    }

    [Theory]
    [InlineData("%Y", "2026")]
    [InlineData("%y", "26")]
    [InlineData("%m", "09")]
    [InlineData("%d", "21")]
    [InlineData("%H", "15")]
    [InlineData("%I", "03")]
    [InlineData("%M", "04")]
    [InlineData("%S", "09")]
    [InlineData("%f", "123")]
    public void Each_token_formats_the_matching_field(string pattern, string expected)
    {
        Assert.Equal(expected, TimeFormat.Format(Sample, pattern));
    }

    [Fact]
    public void Literal_characters_and_separators_are_kept()
    {
        Assert.Equal("2026/09/21 @ 15h04", TimeFormat.Format(Sample, "%Y/%m/%d @ %Hh%M"));
    }

    [Fact]
    public void Lone_percent_is_a_literal_percent()
    {
        Assert.Equal("50% done at 15", TimeFormat.Format(Sample, "50% done at %H"));
    }

    [Fact]
    public void Trailing_percent_is_a_literal_percent()
    {
        Assert.Equal("15%", TimeFormat.Format(Sample, "%H%"));
    }

    [Fact]
    public void Unknown_token_keeps_the_percent_and_the_char()
    {
        Assert.Equal("%Q-15", TimeFormat.Format(Sample, "%Q-%H"));
    }

    [Fact]
    public void Legend_lists_the_supported_tokens_without_percent_percent()
    {
        var tokens = TimeFormat.Legend.Select(t => t.Token).ToList();
        Assert.Contains("%Y", tokens);
        Assert.Contains("%S", tokens);
        Assert.DoesNotContain("%%", tokens);
    }
}
