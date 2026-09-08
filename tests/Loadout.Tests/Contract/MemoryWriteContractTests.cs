using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// What <c>memory write</c> takes, and what it does when asked to change
/// nothing.
/// </summary>
/// <remarks>
/// <para>
/// Both of these are only visible from outside the process. The first is the
/// parser's, and the command's own help was describing an order the parser did
/// not accept; the second is about what is on disk afterwards, which the
/// command cannot be asked.
/// </para>
/// <para>
/// The project is registered here rather than assumed, because the write path
/// is behind resolving one and a test that never reaches it would pass whatever
/// the command did.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class MemoryWriteContractTests
{
    private const string Fact =
        "A fact long enough to be taken as a real one, stated unambiguously.";

    private const string Description =
        "what this topic answers, said at enough length to be accepted";

    [BuiltCliFact]
    public async Task The_topic_is_the_first_thing_it_takes()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(
            "memory", "write", "build-quirks", "--fact", Fact, "--description", Description);

        var said = run.StandardOutput + run.StandardError;

        // The defect this was written for. 'project' sat at position 0 and
        // 'topic' at position 1, so the only positional given went to the
        // project and the topic was reported missing — while the help, which
        // lists required arguments first, printed 'write <topic> [project]' and
        // invited exactly this call.
        said.Should().NotContain("missing required argument",
            "the topic is the first argument the command takes, as its help says");

        // Nothing is registered in a throwaway home, so this cannot succeed.
        // What it must not do is fail about the shape of the command line.
        said.Should().NotContain("Unknown option");
    }

    [BuiltCliFact]
    public async Task A_dry_run_writes_nothing()
    {
        using var loadout = new LoadoutProcess();

        var project = await Registered(loadout);

        var before = Files(loadout.Home);

        var run = await loadout.RunAsync(
            "memory", "write", "build-quirks", "--project", project,
            "--dry-run", "--fact", Fact, "--description", Description);

        run.ExitCode.Should().Be(0, "a preview of a write is not a failure");

        // The whole of it. This command wrote the file, added the line to the
        // index and then said "commit it with: loadout workspace save" — the
        // same words it says on a real run, so nothing in the output told
        // anybody which had happened.
        Files(loadout.Home).Should().Equal(before,
            "--dry-run means the command changes nothing at all");
    }

    [BuiltCliFact]
    public async Task Without_the_flag_it_does_write()
    {
        using var loadout = new LoadoutProcess();

        var project = await Registered(loadout);

        var run = await loadout.RunAsync(
            "memory", "write", "build-quirks", "--project", project,
            "--fact", Fact, "--description", Description);

        run.ExitCode.Should().Be(0);

        // The other half of the pair. Without it, a command that had quietly
        // stopped writing altogether would pass the test above.
        Files(loadout.Home).Should().Contain(path =>
            path.EndsWith("build-quirks.md", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Shaped like a GitHub token and belonging to nobody. Built rather than
    /// written out so the literal never sits in the file as one long string.
    /// </summary>
    private static readonly string Credential = "ghp_" + new string('A', 36);

    [BuiltCliFact]
    public async Task A_credential_is_refused_and_never_echoed()
    {
        using var loadout = new LoadoutProcess();

        var project = await Registered(loadout);

        var run = await loadout.RunAsync(
            "memory", "write", "deploy-notes", "--project", project,
            "--fact", "The deploy uses token " + Credential + " for CI",
            "--description", "how the deploy authenticates against the registry");

        var said = run.StandardOutput + run.StandardError;

        run.ExitCode.Should().NotBe(0, "a credential must not be written to memory");

        // Naming the pattern is the point of the refusal: it tells somebody
        // what to go and rotate.
        said.Should().Contain("GitHub token");

        // And the value is the one thing that must not appear. The refusal was
        // careful about this; the advisory printed alongside it quoted the fact
        // verbatim before any screening had run, which put most of the token
        // into the terminal and from there into scrollback and any transcript.
        said.Should().NotContain(Credential);
        said.Should().NotContain(Credential[..30],
            "a truncated quote of a credential is still a disclosed credential");
    }

    [BuiltCliFact]
    public async Task A_dry_run_refuses_what_the_real_run_would_refuse()
    {
        using var loadout = new LoadoutProcess();

        var project = await Registered(loadout);

        var run = await loadout.RunAsync(
            "memory", "write", "deploy-notes", "--project", project, "--dry-run",
            "--fact", "The deploy uses token " + Credential + " for CI",
            "--description", "how the deploy authenticates against the registry");

        var said = run.StandardOutput + run.StandardError;

        // The preview ran ahead of both screens and reported the write it could
        // not have made. A dry run that predicts success for something the real
        // run refuses is worse than no dry run: it is the case somebody uses it
        // to check.
        said.Should().NotContain("Would record",
            "the real run refuses this, so the preview of it must not promise a write");

        run.ExitCode.Should().NotBe(0);
        said.Should().Contain("GitHub token");
        said.Should().NotContain(Credential);
    }

    /// <summary>Registers a repository so the write path can be reached.</summary>
    private static async Task<string> Registered(LoadoutProcess loadout)
    {
        var repository = Path.Combine(loadout.Home, "a-repository");

        Directory.CreateDirectory(repository);

        await Git(repository, "init");
        await File.WriteAllTextAsync(Path.Combine(repository, "readme.md"), "x");
        await Git(repository, "add", "-A");
        await Git(repository, "-c", "user.email=t@example.com", "-c", "user.name=T",
            "commit", "-m", "first");

        var added = await loadout.RunAsync("project", "add", repository, "--json");

        added.ExitCode.Should().Be(0,
            "the rest of this test is about a project that exists: " + added.StandardError);

        return added.Json().GetProperty("id").GetString()!;
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

    /// <summary>Every file under the throwaway home, so a write cannot hide.</summary>
    private static string[] Files(string home) =>
        [.. Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories).Order()];
}
