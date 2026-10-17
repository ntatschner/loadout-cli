using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Configuration;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// Studio's committed build against what it was built from, and the few
/// things Studio and the daemon both know, checked from both ends.
/// </summary>
/// <remarks>
/// Building Loadout never runs Node: the page is built by 'npm run build' in
/// studio/ and the output is committed. So a change to Studio's sources that
/// was not rebuilt and committed would ship the old page without anybody
/// noticing. The build writes a hash of its inputs beside its output, and this
/// recomputes it.
/// </remarks>
public sealed partial class StudioBuildTests
{
    /// <summary>
    /// What a build reads, as build/source-hash.mjs lists it. The one in the
    /// script is the contract; the test below checks this copy matches it.
    /// </summary>
    private static readonly string[] Inputs =
    [
        "studio/index.html",
        "studio/package-lock.json",
        "studio/svelte.config.js",
        "studio/tsconfig.json",
        "studio/vite.config.ts",
        "studio/build/source-hash.mjs",
        "studio/public",
        "studio/src",
        "design/studio/tokens.css",
        "design/studio/fonts",
    ];

    private static readonly string[] Binary = [".woff2", ".png", ".ico"];

    [Fact]
    public void The_committed_build_was_made_from_the_sources_as_they_are()
    {
        var root = RepositoryRoot();
        var recorded = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "Loadout.Core", "Teams", "Daemon", "Studio", "studio-build.json")))
            .RootElement.GetProperty("source").GetString();

        recorded.Should().Be(SourceHash(root),
            "Studio's sources changed without the committed build being remade: run 'npm run build' in studio/ and commit what it writes");
    }

    [Fact]
    public void This_copy_of_the_inputs_is_the_scripts()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "studio", "build", "source-hash.mjs"));
        var listed = ScriptInputs().Match(script);

        listed.Success.Should().BeTrue("the script lists its inputs as INPUTS");
        Quoted().Matches(listed.Groups[1].Value).Select(match => match.Groups[1].Value).Should().Equal(Inputs);
    }

    [Fact]
    public void Studio_and_the_daemon_know_the_same_panels()
    {
        var layout = File.ReadAllText(Path.Combine(RepositoryRoot(), "studio", "src", "lib", "layout.ts"));

        Panels(layout, "LENS_PANELS").Should().Equal(StudioLayouts.Lens, "a layout the page saves must be one the config check accepts");
        Panels(layout, "MAP_PANELS").Should().Equal(StudioLayouts.Map);
    }

    /// <summary>
    /// The commands Studio shows beside its actions, against the lines the
    /// daemon runs for the same verbs. Studio's own test pins the same
    /// literals (helpers.test.ts), so a change to either side alone fails.
    /// </summary>
    [Theory]
    [InlineData("gate", "Approve", "gate-5e1d9a20", "loadout team gate 20261016-0840-3f1a --gate gate-5e1d9a20 --answer Approve --by dashboard")]
    [InlineData("pause", null, null, "loadout team halt 20261016-0840-3f1a --pause")]
    [InlineData("resume", null, null, "loadout team halt 20261016-0840-3f1a --resume")]
    [InlineData("stop", null, null, "loadout team halt 20261016-0840-3f1a")]
    public void A_run_action_shows_the_command_the_daemon_runs(string verb, string? answer, string? gate, string shown)
    {
        var (command, arguments) = DashboardActions.Maps(new Loadout.Core.Teams.Daemon.RunAction("20261016-0840-3f1a", verb, Gate: gate, Answer: answer));

        Typed(command, arguments).Should().Be(shown);
        Pinned().Should().Contain("$ " + shown);
    }

    [Fact]
    public void Think_again_shows_the_command_the_daemon_runs()
    {
        var (command, arguments) = DashboardActions.Maps(new Loadout.Core.Teams.Daemon.RunAction("20261016-0840-3f1a", "gate", Gate: "gate-5e1d9a20", ThinkAgain: true));
        const string shown = "loadout team gate 20261016-0840-3f1a --gate gate-5e1d9a20 --think-again --by dashboard";

        Typed(command, arguments).Should().Be(shown);
        Pinned().Should().Contain("$ " + shown);
    }

    [Theory]
    [InlineData("starstats", "loadout task declare upload-retries doing --project starstats")]
    [InlineData(null, "loadout task declare chores done --global")]
    public void A_task_action_shows_the_command_the_daemon_runs(string? project, string shown)
    {
        var id = project is null ? "chores" : "upload-retries";
        var state = project is null ? "done" : "doing";
        var (command, arguments) = DashboardTasks.Maps(new TaskAction("state", Id: id, Project: project, State: state));

        Typed(command, arguments).Should().Be(shown);
        Pinned().Should().Contain("$ " + shown);
    }

    /// <summary>
    /// Studio's "Report a problem" against the form it opens. A link naming a
    /// template or a field that is not there opens an empty form, and the
    /// default switch counts reports by the labels the template applies.
    /// </summary>
    [Fact]
    public void The_report_link_names_the_studio_bug_form_and_its_fields()
    {
        var root = RepositoryRoot();
        var report = File.ReadAllText(Path.Combine(root, "studio", "src", "lib", "report.ts"));
        var template = File.ReadAllText(Path.Combine(root, ".github", "ISSUE_TEMPLATE", "studio_bug.yml"));

        report.Should().Contain("issues/new?template=studio_bug.yml");
        report.Should().Contain("{ version: about.version, platform: about.platform }");
        template.Should().MatchRegex(@"(?m)^labels: \[bug, studio\]\r?$");
        template.Should().MatchRegex(@"(?m)^\s+id: version\r?$");
        template.Should().MatchRegex(@"(?m)^\s+id: platform\r?$");
    }

    /// <summary>
    /// The line as a person would type it. The daemon adds --non-interactive,
    /// which only says nobody is at a terminal to answer, so it is left out of
    /// what the page shows.
    /// </summary>
    private static string Typed(string command, IReadOnlyList<string> arguments) =>
        string.Join(' ', new[] { "loadout", command }.Concat(arguments.Where(one => one != "--non-interactive")));

    private static string Pinned() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "studio", "src", "lib", "helpers.test.ts"));

    private static IReadOnlyList<string> Panels(string source, string name)
    {
        var declared = new Regex(name + @" = \[([^\]]*)\]").Match(source);
        declared.Success.Should().BeTrue($"layout.ts declares {name}");

        return Quoted().Matches(declared.Groups[1].Value).Select(match => match.Groups[1].Value).ToList();
    }

    /// <summary>The same hash build/source-hash.mjs makes.</summary>
    private static string SourceHash(string root)
    {
        var files = new List<string>();

        foreach (var input in Inputs)
        {
            var full = Path.Combine(root, input);

            if (Directory.Exists(full))
            {
                files.AddRange(Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
                    .Where(file => !file.EndsWith(".test.ts", StringComparison.Ordinal)));
            }
            else
            {
                files.Add(input);
            }
        }

        // The script sorts by UTF-16 code unit, which is what ordinal does.
        files.Sort(StringComparer.Ordinal);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(Path.Combine(root, file));

            // Carriage returns out of text, so a checkout on Windows hashes
            // the same as the one the build was made in.
            if (!Binary.Any(extension => file.EndsWith(extension, StringComparison.Ordinal)))
            {
                bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\r", string.Empty, StringComparison.Ordinal));
            }

            hash.AppendData(Encoding.UTF8.GetBytes(file + "\n"));
            hash.AppendData(bytes);
            hash.AppendData("\n"u8);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        // A directory in an ordinary checkout, a file in a git worktree.
        while (directory is not null && !Path.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "The repository root could not be found from the test output directory.");
    }

    [GeneratedRegex(@"export const INPUTS = \[([^\]]*)\]")]
    private static partial Regex ScriptInputs();

    [GeneratedRegex("'([^']+)'")]
    private static partial Regex Quoted();
}
