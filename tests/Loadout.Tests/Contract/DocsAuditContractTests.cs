using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Which checkout <c>docs audit</c> reads.
/// </summary>
/// <remarks>
/// Run from a second clone or worktree of a registered project, it used to
/// resolve the directory to the project by its remote and then audit the
/// project's registered path - the other checkout. Even <c>--repo</c> pointing
/// at the second one was ignored, so the audit of a branch reported on
/// whatever the first checkout had in it.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class DocsAuditContractTests
{
    private const string Remote = "https://github.com/example/widget.git";

    [BuiltCliFact]
    public async Task The_checkout_named_is_the_one_audited()
    {
        using var loadout = new LoadoutProcess();

        var registered = Path.Combine(loadout.Home, "widget");
        var second = Path.Combine(loadout.Home, "widget-branch");

        await Repository(registered, ("README.md", "# Widget\n"));
        await Repository(
            second,
            ("README.md", "# Widget\n"),
            ("docs/orphan.md", "# Nothing links here\n"));

        var added = await loadout.RunAsync("project", "add", registered, "--json");

        added.ExitCode.Should().Be(0, added.StandardError);

        var audit = await loadout.RunAsync("docs", "audit", "--repo", second, "--json");

        audit.ExitCode.Should().Be(0, audit.StandardError);

        var json = audit.Json();

        json.GetProperty("documents").GetInt32().Should().Be(2,
            "the second checkout has two documents and the registered one has one");

        json.GetProperty("findings").EnumerateArray()
            .Select(finding => finding.GetProperty("path").GetString())
            .Should().Contain("docs/orphan.md");
    }

    private static async Task Repository(string directory, params (string Path, string Text)[] files)
    {
        Directory.CreateDirectory(directory);

        foreach (var (path, text) in files)
        {
            var full = Path.Combine(directory, path);

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, text);
        }

        await Git(directory, "init");
        await Git(directory, "remote", "add", "origin", Remote);
        await Git(directory, "add", "-A");
        await Git(directory, "-c", "user.email=t@example.com", "-c", "user.name=T", "commit", "-m", "first");
    }

    private static async Task Git(string directory, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;

        await process.WaitForExitAsync();
    }
}
