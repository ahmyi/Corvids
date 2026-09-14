using Corvids.Models;

namespace Corvids.Tests;

public class AppEntryTests
{
    [Fact]
    public void ParseEnvironment_reads_key_value_pairs()
    {
        var entry = new AppEntry { Environment = "NODE_ENV=production\nPORT=3000" };

        var env = entry.ParseEnvironment();

        Assert.Equal("production", env["NODE_ENV"]);
        Assert.Equal("3000", env["PORT"]);
        Assert.Equal(2, env.Count);
    }

    [Fact]
    public void ParseEnvironment_ignores_comments_and_blank_lines()
    {
        var entry = new AppEntry { Environment = "# a comment\n\nNODE_ENV=production\n   \n# another" };

        var env = entry.ParseEnvironment();

        Assert.Single(env);
        Assert.Equal("production", env["NODE_ENV"]);
    }

    [Fact]
    public void ParseEnvironment_trims_keys_values_and_strips_surrounding_quotes()
    {
        var entry = new AppEntry { Environment = "  PORT = 4321 \nAPI_KEY=\"quoted value\"\nNAME='single'" };

        var env = entry.ParseEnvironment();

        Assert.Equal("4321", env["PORT"]);
        Assert.Equal("quoted value", env["API_KEY"]);
        Assert.Equal("single", env["NAME"]);
    }

    [Fact]
    public void ParseEnvironment_keeps_equals_signs_inside_the_value()
    {
        var entry = new AppEntry { Environment = "TOKEN=ab=cd==" };

        Assert.Equal("ab=cd==", entry.ParseEnvironment()["TOKEN"]);
    }

    [Theory]
    [InlineData("NOEQUALS")]
    [InlineData("=leadingEquals")]
    public void ParseEnvironment_skips_malformed_lines(string line)
    {
        Assert.Empty(new AppEntry { Environment = line }.ParseEnvironment());
    }

    [Fact]
    public void ParseEnvironment_of_null_is_empty()
    {
        Assert.Empty(new AppEntry().ParseEnvironment());
    }

    [Fact]
    public void New_entry_has_a_unique_id_and_auto_shell()
    {
        var a = new AppEntry();
        var b = new AppEntry();

        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(ShellKind.Auto, a.Shell);
    }
}
