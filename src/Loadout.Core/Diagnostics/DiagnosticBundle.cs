using System.Globalization;
using System.Text;
using Loadout.Core.Security;
using Loadout.Models.Diagnostics;
using Loadout.Models.Platform;

namespace Loadout.Core.Diagnostics;

/// <summary>
/// The doctor report written as one file somebody can send.
/// </summary>
/// <remarks>
/// <para>
/// For a single user this is nothing that <c>doctor --json</c> and a pipe do not
/// already do. It earns its place the moment somebody else runs the launcher and
/// reports a problem that cannot be reproduced here — which is a consequence of
/// a workspace being shared rather than a feature in its own right.
/// </para>
/// <para>
/// Redaction is not a nicety. The whole point of the file is that it leaves this
/// machine, and a diagnostic report names paths, machine names and secret
/// references. So it is built from the checks, which already carry a rule
/// against quoting a credential, and then screened again before it is written —
/// because the thing that makes this dangerous is exactly the thing that makes
/// it useful.
/// </para>
/// </remarks>
public static class DiagnosticBundle
{
    /// <summary>
    /// The bundle text, or a refusal when it would carry a credential.
    /// </summary>
    /// <param name="report">What the doctor found.</param>
    /// <param name="host">The machine, its operating system and architecture.</param>
    /// <param name="version">The launcher build this was taken from.</param>
    /// <param name="written">When it was taken.</param>
    public static Models.Results.OperationResult<string> Build(
        DiagnosticReport report,
        HostPlatform host,
        string version,
        DateTimeOffset written)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(host);

        var builder = new StringBuilder()
            .AppendLine("# Loadout diagnostics")
            .AppendLine()
            .AppendLine($"- Taken: {written.ToUniversalTime():u}")
            .AppendLine($"- Version: {version}")
            .AppendLine($"- Platform: {host.OperatingSystem} {host.Architecture}")
            .AppendLine($"- Verdict: {report.Overall}")
            .AppendLine();

        // The machine name is left out on purpose. It is in the doctor report
        // on screen, where it belongs — this file is going somewhere else, and
        // whose machine it was is not what anybody reading it needs.
        builder.AppendLine("| Category | Check | Severity | Detail |");
        builder.AppendLine("|---|---|---|---|");

        foreach (var check in report.Checks)
        {
            builder.AppendLine(
                $"| {Cell(check.Category)} | {Cell(check.Name)} | "
                + $"{check.Severity} | {Cell(check.Detail)} |");
        }

        // Substituted rather than filtered out by check name. The machine name
        // is its own check, and it also turns up inside paths on a machine
        // whose home directory is named after it — so dropping one row would
        // leave it in six others and the claim that it is gone would be false.
        // A filter keyed on a check's name would also stop working silently the
        // day somebody renames the check.
        var text = Anonymise(builder.ToString(), host.MachineName);

        // Screened as a whole rather than trusting every contributor to have
        // been careful. Contributors are added over time and a diagnostic that
        // quoted a value would put it in a file whose purpose is to be sent.
        var matched = SecretScanner.Match(text);

        return matched.Count > 0
            ? Models.Results.OperationResult<string>.Fail(
                $"The bundle was not written: a check reads like it contains a credential "
                + $"({string.Join(", ", matched)}). Run 'loadout doctor' to see which, and fix "
                + "that before sending anything.",
                Models.ExitCode.PolicyViolation)
            : Models.Results.OperationResult<string>.Ok(text);
    }

    /// <summary>
    /// The report with this machine's name taken out of it, wherever it appears.
    /// </summary>
    /// <remarks>
    /// Whose machine it was is not what somebody reading a diagnostic needs, and
    /// the file exists to be sent to them. A very short machine name is left
    /// alone: substituting a two-letter name would replace fragments of
    /// unrelated words and produce a report that reads as nonsense, which is a
    /// worse outcome than a name nobody needed.
    /// </remarks>
    private static string Anonymise(string text, string? machineName) =>
        machineName is { Length: > 3 }
            ? text.Replace(machineName, "<machine>", StringComparison.OrdinalIgnoreCase)
            : text;

    /// <summary>
    /// One table cell, with anything that would break the table taken out.
    /// </summary>
    /// <remarks>
    /// A detail can hold a path, and a path can hold a pipe on no platform this
    /// runs on — but it can hold a newline when a tool's output was captured
    /// into it, and one newline turns the rest of the table into prose.
    /// </remarks>
    private static string Cell(string? value) =>
        (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Trim();

    /// <summary>A file name that sorts by when it was taken.</summary>
    public static string FileName(DateTimeOffset written) =>
        $"loadout-diagnostics-{written.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.md";
}
