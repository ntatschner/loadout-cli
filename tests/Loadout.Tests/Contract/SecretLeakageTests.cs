using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Proves a credential cannot reach any output path.
/// <para>
/// The scanner and the redactor each have unit tests, and both are correct in
/// isolation. What was never checked is the property that actually matters:
/// that a credential the launcher has been given does not come back out of the
/// running program — through standard output, standard error, a JSON document
/// or a stack trace.
/// </para>
/// <para>
/// A distinctive sentinel is planted and every surface is searched for it. The
/// value is synthetic and matches no real credential; what makes it useful is
/// that it cannot occur by accident, so a hit is always a leak.
/// </para>
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class SecretLeakageTests
{
    /// <summary>
    /// The password half of a remote URL. Distinctive enough that finding it
    /// anywhere is conclusive, and shaped like the thing being guarded rather
    /// than like a word.
    /// </summary>
    private const string Sentinel = "Sentinel-b7f3a91c4e20-LEAKED";

    private static string RemoteWithCredentials =>
        $"https://loadout-tests:{Sentinel}@git.example.invalid/workspace.git";

    /// <summary>
    /// Configures a workspace remote carrying a credential, the way somebody
    /// who pasted a tokenised clone URL would have.
    /// </summary>
    private static async Task<LoadoutProcess> WithCredentialInConfigAsync()
    {
        var loadout = new LoadoutProcess();

        var set = await loadout.RunAsync("config", "set", "workspace-remote", RemoteWithCredentials);

        set.ExitCode.Should().Be(0, "the credential has to be stored for the test to mean anything");

        return loadout;
    }

    /// <summary>Fails naming the surface, because "it leaked" is not enough to act on.</summary>
    private static void ShouldNotLeak(LoadoutRun run, string command)
    {
        run.StandardOutput.Should().NotContain(
            Sentinel, $"'{command}' must not print a credential to standard output");

        run.StandardError.Should().NotContain(
            Sentinel, $"'{command}' must not print a credential to standard error");
    }

    [BuiltCliFact]
    public async Task Doctor_does_not_report_the_credential_in_a_remote()
    {
        using var loadout = await WithCredentialInConfigAsync();

        ShouldNotLeak(await loadout.RunAsync("doctor"), "doctor");
        ShouldNotLeak(await loadout.RunAsync("doctor", "--json"), "doctor --json");
    }

    [BuiltCliFact]
    public async Task Status_does_not_report_the_credential()
    {
        using var loadout = await WithCredentialInConfigAsync();

        ShouldNotLeak(await loadout.RunAsync("status"), "status");
        ShouldNotLeak(await loadout.RunAsync("status", "--json"), "status --json");
    }

    [BuiltCliFact]
    public async Task Workspace_status_does_not_report_the_credential()
    {
        using var loadout = await WithCredentialInConfigAsync();

        ShouldNotLeak(await loadout.RunAsync("workspace", "status"), "workspace status");
        ShouldNotLeak(
            await loadout.RunAsync("workspace", "status", "--json"), "workspace status --json");
    }

    [BuiltCliFact]
    public async Task Config_list_does_not_report_the_credential()
    {
        using var loadout = await WithCredentialInConfigAsync();

        // config get is deliberately excluded: asking for a value by name and
        // being given it is what that command is for, and a script reading its
        // own configured remote is not a leak. Listing every setting for a
        // person to read is a different act, and a credential has no business
        // in it.
        ShouldNotLeak(await loadout.RunAsync("config", "list"), "config list");
        ShouldNotLeak(await loadout.RunAsync("config", "list", "--json"), "config list --json");
    }

    [BuiltCliFact]
    public async Task A_failure_that_mentions_the_remote_does_not_carry_the_credential()
    {
        using var loadout = await WithCredentialInConfigAsync();

        // The remote does not resolve, so this fails with a message about it —
        // which is exactly where an unredacted URL would surface.
        var run = await loadout.RunAsync("workspace", "sync");

        ShouldNotLeak(run, "workspace sync");
    }

    [BuiltCliFact]
    public async Task A_stack_trace_does_not_carry_the_credential()
    {
        using var loadout = await WithCredentialInConfigAsync();

        // --debug prints exception detail in full, which is the path most
        // likely to carry something raw.
        ShouldNotLeak(await loadout.RunAsync("workspace", "sync", "--debug"), "workspace sync --debug");
    }

    [BuiltCliFact]
    public async Task No_command_prints_the_credential()
    {
        using var loadout = await WithCredentialInConfigAsync();

        // The tests above name five commands. There are more than fifty, and
        // naming them is how the launcher's own screens came to print the
        // remote unredacted for as long as they did: the guard covered the
        // surface somebody remembered, not the surface that exists. The
        // dry-run contract already takes its list from the catalogue for the
        // same reason.
        Loadout.Cli.Program.CommandNames();

        var leaked = new List<string>();
        var exercised = 0;

        foreach (var entry in Loadout.Cli.Program.RegisteredCommands()
            .Where(command => !command.RequiresNetwork)
            .OrderBy(command => command.Path, StringComparer.Ordinal))
        {
            // Nothing is registered in a throwaway home, so most of these fail
            // — which is the point. A failure is where a remote gets quoted
            // back, and it is the path that leaked.
            string[] arguments = entry.Mutates
                ? [.. entry.Path.Split(' '), "--dry-run"]
                : [.. entry.Path.Split(' ')];

            var run = await loadout.RunAsync(arguments);

            exercised++;

            if (run.StandardOutput.Contains(Sentinel, StringComparison.Ordinal)
                || run.StandardError.Contains(Sentinel, StringComparison.Ordinal))
            {
                leaked.Add(entry.Path);
            }
        }

        exercised.Should().BeGreaterThan(20, "the catalogue has to be filled for this to check anything");

        leaked.Should().BeEmpty("a credential must not come back out of any command");
    }

    [BuiltCliFact]
    public async Task The_sentinel_would_be_found_if_it_were_printed()
    {
        using var loadout = new LoadoutProcess();

        // A guard on the guard. If nothing in this suite can observe the
        // sentinel — because output is swallowed, or the harness reads the
        // wrong stream — every test above would pass while proving nothing.
        var run = await loadout.RunAsync("config", "set", "workspace-remote", RemoteWithCredentials);

        var everything = run.StandardOutput + run.StandardError;

        everything.Should().NotBeNullOrEmpty(
            "the harness must be able to see what the command wrote");
    }
}
