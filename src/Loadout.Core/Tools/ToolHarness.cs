using System.Text.RegularExpressions;
using Loadout.Core.Teams;
using Loadout.Models.Configuration;
using Loadout.Models.Teams;
using Loadout.Models.Tools;
using Loadout.Platform.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Loadout.Core.Tools;

/// <summary>How one case went.</summary>
/// <param name="Case">Its name.</param>
/// <param name="Class">Its class.</param>
/// <param name="Passed">Whether every expectation held.</param>
/// <param name="Why">What did not hold, or empty.</param>
public sealed record ToolCaseResult(string Case, string Class, bool Passed, string Why);

/// <summary>What this machine says about running a harness, from its own configuration.</summary>
/// <param name="Rule">The machine's rule for kind <c>tool-test</c>, or null for none, which is asking.</param>
/// <param name="Trusted">What a person at this machine has agreed to.</param>
public sealed record ToolTestConsent(string? Rule, IReadOnlyList<TrustedRemedy>? Trusted);

/// <summary>
/// Runs a version's cases, each in a directory of its own.
/// </summary>
/// <remarks>
/// <para>
/// A harness run executes a script an agent wrote, so whether it may run at
/// all is <see cref="RemedyCeiling.Decide" />'s to say, with kind
/// <see cref="Kind" />, exactly as for a remedy. Nobody has decided yet that
/// it may run unattended, so a machine that says nothing is asked.
/// </para>
/// <para>
/// A fresh temporary directory per case, so one case cannot pass on what the
/// last one left behind.
/// </para>
/// </remarks>
public sealed class ToolHarness
{
    /// <summary>The kind a machine sets its rule against for harness runs.</summary>
    public const string Kind = "tool-test";

    /// <summary>The placeholder a case uses for its own directory.</summary>
    public const string Tmp = "{tmp}";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private static readonly ISerializer Writer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private readonly IProcessLauncher _launcher;

    public ToolHarness(IProcessLauncher launcher) => _launcher = launcher;

    /// <summary>The case classes a set of cases does not cover.</summary>
    public static IReadOnlyList<string> MissingClasses(IEnumerable<ToolCase> cases)
    {
        var have = cases.Select(one => one.Class.Trim().ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

        return [.. ToolCaseClass.All.Where(one => !have.Contains(one))];
    }

    /// <summary>What a person agrees to when they agree to a harness run: the script and its cases.</summary>
    public static string Subject(string script, IEnumerable<ToolCase> cases) =>
        script + "\n---\n" + string.Join(
            "\n---\n",
            cases.OrderBy(one => one.Name, StringComparer.Ordinal).Select(Writer.Serialize));

    /// <summary>The fingerprint a person's agreement to a harness run is recorded against.</summary>
    public static string Fingerprint(string script, IEnumerable<ToolCase> cases) =>
        RemedyCeiling.Fingerprint(Subject(script, cases));

    /// <summary>The name a harness run is agreed to under.</summary>
    public static string Named(ToolVersion draft) => $"{Kind}:{draft.Name}@{draft.Version}";

    /// <summary>Whether this machine lets the harness run these cases against this script.</summary>
    public static RemedyCeiling.Decision May(
        ToolVersion draft,
        string script,
        IReadOnlyList<ToolCase> cases,
        ToolTestConsent consent)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(consent);

        return RemedyCeiling.Decide(
            new Remedy { Name = Named(draft), Kind = Kind },
            consent.Rule,
            Subject(script, cases),
            consent.Trusted);
    }

    /// <summary>Runs every case, in order.</summary>
    public async Task<IReadOnlyList<ToolCaseResult>> RunAllAsync(
        string scriptPath,
        IEnumerable<ToolCase> cases,
        CancellationToken ct = default)
    {
        var results = new List<ToolCaseResult>();

        foreach (var one in cases)
        {
            results.Add(await RunAsync(scriptPath, one, ct).ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>Runs one case in a directory of its own, and checks what it expects.</summary>
    public async Task<ToolCaseResult> RunAsync(string scriptPath, ToolCase toolCase, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(toolCase);

        ToolCaseResult Fail(string why) => new(toolCase.Name, toolCase.Class, false, why);

        // Project detail in a case is project detail all the same.
        foreach (var (name, value) in toolCase.Args)
        {
            if (!value.Contains(Tmp, StringComparison.Ordinal) && Path.IsPathRooted(value))
            {
                return Fail($"'{name}' is an absolute path. Cases may only use {Tmp}.");
            }
        }

        var directory = Path.Combine(Path.GetTempPath(), "loadout-tool-case-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(directory);

            string Expand(string value) => value.Replace(Tmp, directory, StringComparison.Ordinal);

            foreach (var (file, content) in toolCase.Setup.Files)
            {
                var at = Under(directory, Expand(file));

                if (at is null)
                {
                    return Fail($"The setup file '{file}' is outside {Tmp}.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(at)!);
                await File.WriteAllTextAsync(at, content, ct).ConfigureAwait(false);
            }

            var arguments = new List<string> { "-NoProfile", "-NonInteractive", "-File", scriptPath };

            foreach (var (name, value) in toolCase.Args)
            {
                arguments.Add("-" + name);
                arguments.Add(Expand(value));
            }

            var ran = await _launcher.RunAsync(
                new ProcessRequest("pwsh", arguments, directory),
                Timeout,
                ct).ConfigureAwait(false);

            if (ran.Failed)
            {
                return Fail("It could not be run: " + ran.Error);
            }

            var outcome = ran.Value!;
            var expect = toolCase.Expect;

            if (expect.Exit is { } exit && outcome.ExitCode != exit)
            {
                return Fail($"Exit code {outcome.ExitCode}, expected {exit}.");
            }

            if (!Matches(expect.StdoutMatches, outcome.StandardOutput))
            {
                return Fail($"Standard output did not match '{expect.StdoutMatches}'.");
            }

            if (!Matches(expect.StderrMatches, outcome.StandardError))
            {
                return Fail($"Standard error did not match '{expect.StderrMatches}'.");
            }

            foreach (var file in expect.FilesPresent)
            {
                if (Under(directory, Expand(file)) is not { } at || !File.Exists(at))
                {
                    return Fail($"'{file}' should be there and is not.");
                }
            }

            foreach (var file in expect.FilesAbsent)
            {
                if (Under(directory, Expand(file)) is { } at && File.Exists(at))
                {
                    return Fail($"'{file}' should be gone and is not.");
                }
            }

            return new ToolCaseResult(toolCase.Name, toolCase.Class, true, string.Empty);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A temporary directory left behind is untidy, not wrong.
            }
        }
    }

    private static bool Matches(string pattern, string text)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return true;
        }

        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string? Under(string directory, string path)
    {
        var at = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(directory, path));
        var root = Path.GetFullPath(directory);

        return at.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? at : null;
    }
}
