using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// The global <c>--repo</c> option, which every command advertises.
/// </summary>
/// <remarks>
/// <para>
/// It is described on every command as "the repository path to operate on,
/// instead of the current directory", and seven commands ignored it and used
/// the working directory regardless. That is worse than not offering it: a
/// person passes it, nothing complains, and the answer is about somewhere else.
/// </para>
/// <para>
/// It surfaced through the launcher. Choosing a command there runs it against
/// the selected project by passing this option, and the launcher's working
/// directory is wherever it was started from — for a Start Menu launch, the
/// directory it is installed in. So "code" reported that the install directory
/// was not a Git repository, which was true, and no help.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class RepoOptionContractTests
{
    /// <summary>
    /// Commands that resolve a project from where they are standing, and so
    /// have to take being told to stand somewhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The flag says whether the command has to arrive at a project before it
    /// can answer. Where it does, being pointed at a directory that is not a
    /// repository is a failure and has to be reported as one.
    /// </para>
    /// <para>
    /// The rest — <c>status</c>, <c>instructions explain</c> — have something to
    /// say without one, and answering is right rather than a miss.
    /// </para>
    /// </remarks>
    public static TheoryData<string[], bool> ProjectScopedCommands =>
        new()
        {
            { ["code"], true },
            { ["memory", "list"], true },
            { ["rules", "list"], true },
            { ["instructions", "explain", "tidy up"], false },
            { ["status"], false },

            // 'launches' accepted --repo and ignored it, so asking about this
            // repository listed another project's launches. Absent from this
            // list is how that survived — but so is the flag: written as one
            // lenient rule, this test passed with the defect in place, because
            // a command that ignores the option succeeds, and succeeding was
            // allowed. The strict half below is what actually catches it.
            { ["launches"], true },
        };

    [BuiltCliTheory]
    [MemberData(nameof(ProjectScopedCommands))]
    public async Task A_command_told_where_to_look_looks_there(string[] command, bool mustResolve)
    {
        using var loadout = new LoadoutProcess();

        // A directory that exists and is not a repository, so the answer names
        // it rather than succeeding for some other reason.
        var elsewhere = Path.Combine(loadout.Home, "not-a-repository");
        Directory.CreateDirectory(elsewhere);

        var run = await loadout.RunAsync([.. command, "--repo", elsewhere]);

        var output = run.StandardOutput + run.StandardError;
        var line = string.Join(' ', command);

        if (mustResolve)
        {
            // Succeeding here is the failure. A command that needs a project
            // and was told to find one in a directory with no repository in it
            // has nothing to report, and anything it does report is about
            // somewhere else.
            run.ExitCode.Should().NotBe(0,
                $"'{line} --repo <not a repository>' cannot have found a project to answer about");
        }

        // Either it reports on that directory, or it succeeds without needing a
        // repository at all. What it must never do is report on the directory
        // the process happens to be running in.
        if (run.ExitCode != 0)
        {
            output.Should().Contain("not-a-repository",
                $"'{line} --repo' has to be about the path it was given");
        }
    }
}
