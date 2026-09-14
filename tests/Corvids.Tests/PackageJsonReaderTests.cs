using Corvids.Services;

namespace Corvids.Tests;

public class PackageJsonReaderTests : IDisposable
{
    private readonly string _dir;

    public PackageJsonReaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "corvids-pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private void WritePackageJson(string body) => File.WriteAllText(Path.Combine(_dir, "package.json"), body);

    [Fact]
    public void Read_returns_null_when_no_package_json()
    {
        Assert.Null(PackageJsonReader.Read(_dir));
    }

    [Fact]
    public void Read_reports_name_and_npm_commands_without_a_lockfile()
    {
        WritePackageJson("""{ "name": "myapp", "scripts": { "dev": "next dev", "start": "node .", "build": "tsc" } }""");

        var info = PackageJsonReader.Read(_dir);

        Assert.NotNull(info);
        Assert.Equal("myapp", info!.Name);
        Assert.Contains("npm run dev", info.Commands);
        Assert.Contains("npm start", info.Commands);   // "start" is special-cased for npm
        Assert.Contains("npm run build", info.Commands);
    }

    [Fact]
    public void Read_uses_pnpm_when_a_pnpm_lockfile_is_present()
    {
        WritePackageJson("""{ "name": "p", "scripts": { "dev": "vite" } }""");
        File.WriteAllText(Path.Combine(_dir, "pnpm-lock.yaml"), "lockfileVersion: 9\n");

        var info = PackageJsonReader.Read(_dir);

        Assert.Contains("pnpm run dev", info!.Commands);
    }

    [Fact]
    public void Read_uses_yarn_when_a_yarn_lockfile_is_present()
    {
        WritePackageJson("""{ "scripts": { "dev": "vite" } }""");
        File.WriteAllText(Path.Combine(_dir, "yarn.lock"), "");

        Assert.Contains("yarn run dev", PackageJsonReader.Read(_dir)!.Commands);
    }

    [Theory]
    [InlineData("bun.lockb")]
    [InlineData("bun.lock")]
    public void Read_uses_bun_when_a_bun_lockfile_is_present(string lockfile)
    {
        WritePackageJson("""{ "scripts": { "dev": "vite" } }""");
        File.WriteAllText(Path.Combine(_dir, lockfile), "");

        Assert.Contains("bun run dev", PackageJsonReader.Read(_dir)!.Commands);
    }

    [Fact]
    public void Read_does_not_special_case_start_for_non_npm_runners()
    {
        WritePackageJson("""{ "scripts": { "start": "node ." } }""");
        File.WriteAllText(Path.Combine(_dir, "pnpm-lock.yaml"), "");

        var commands = PackageJsonReader.Read(_dir)!.Commands;

        Assert.Contains("pnpm run start", commands);   // only npm gets the bare "npm start"
        Assert.DoesNotContain("pnpm start", commands);
    }

    [Fact]
    public void Read_handles_a_package_with_no_scripts()
    {
        WritePackageJson("""{ "name": "lib" }""");

        var info = PackageJsonReader.Read(_dir);

        Assert.NotNull(info);
        Assert.Empty(info!.Commands);
    }

    [Fact]
    public void Read_returns_null_on_malformed_json()
    {
        WritePackageJson("{ not valid");

        Assert.Null(PackageJsonReader.Read(_dir));
    }
}
