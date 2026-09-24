using Loadout.Models.Tools;

namespace Loadout.Core.Tools;

/// <summary>What the regression gate found.</summary>
/// <param name="Passed">Whether the draft may be promoted.</param>
/// <param name="Own">The draft's own cases.</param>
/// <param name="Regression">The active known-good version's cases, rerun against the draft.</param>
/// <param name="Regressions">Known-good cases the draft newly fails, and has not retired.</param>
/// <param name="Retired">Known-good cases the draft fails under a declared break.</param>
public sealed record ToolGateResult(
    bool Passed,
    IReadOnlyList<ToolCaseResult> Own,
    IReadOnlyList<ToolCaseResult> Regression,
    IReadOnlyList<string> Regressions,
    IReadOnlyList<string> Retired)
{
    /// <summary>Why, in a sentence.</summary>
    public string Because =>
        Passed
            ? "Every case passed" + (Retired.Count > 0 ? $", with {string.Join(", ", Retired)} retired by a declared break." : ".")
            : string.Join(
                " ",
                Own.Where(one => !one.Passed).Select(one => $"'{one.Case}' failed: {one.Why}")
                    .Concat(Regressions.Select(one => $"'{one}' passed on the known-good version and fails now.")));
}

/// <summary>
/// What a draft has to show before it can replace a known-good version.
/// </summary>
/// <remarks>
/// A failed refinement must not replace what worked. The gate reruns every case
/// the active version passed against the draft, and a case that newly fails
/// rejects the draft, unless the draft owns up: a declared break, a major bump,
/// migration text, and the case named as retired.
/// </remarks>
public static class ToolPromotion
{
    /// <summary>The manifest fields a version must fill before it can be promoted.</summary>
    public static IReadOnlyList<string> Missing(ToolVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(version.Purpose))
        {
            missing.Add("purpose");
        }

        if (version.Inputs.Count == 0)
        {
            missing.Add("inputs");
        }

        if (string.IsNullOrWhiteSpace(version.Outputs.Stdout) && version.Outputs.Exit.Count == 0)
        {
            missing.Add("outputs");
        }

        if (version.Dependencies.Count == 0)
        {
            missing.Add("dependencies");
        }

        if (version.Constraints.Count == 0)
        {
            missing.Add("constraints");
        }

        if (string.IsNullOrWhiteSpace(version.ErrorBehaviour))
        {
            missing.Add("error_behaviour");
        }

        if (version.Examples.Count == 0)
        {
            missing.Add("examples");
        }

        if (string.IsNullOrWhiteSpace(version.Origin))
        {
            missing.Add("origin");
        }

        return missing;
    }

    /// <summary>
    /// Runs the draft's cases and the active version's cases against the draft's script.
    /// </summary>
    /// <param name="next">The draft.</param>
    /// <param name="nextScript">The draft's script, where the runner can find it.</param>
    /// <param name="nextCases">The draft's own cases.</param>
    /// <param name="active">The active known-good version, or null for a first version.</param>
    /// <param name="activeCases">Its cases.</param>
    /// <param name="run">Runs one case against one script: the harness, in anything but a test.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<ToolGateResult> GateAsync(
        ToolVersion next,
        string nextScript,
        IReadOnlyList<ToolCase> nextCases,
        ToolVersion? active,
        IReadOnlyList<ToolCase> activeCases,
        Func<string, ToolCase, CancellationToken, Task<ToolCaseResult>> run,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(run);

        var own = new List<ToolCaseResult>();

        foreach (var one in nextCases)
        {
            own.Add(await run(nextScript, one, ct).ConfigureAwait(false));
        }

        var regression = new List<ToolCaseResult>();
        var regressions = new List<string>();
        var retired = new List<string>();

        if (active is not null)
        {
            foreach (var one in activeCases)
            {
                var result = await run(nextScript, one, ct).ConfigureAwait(false);
                regression.Add(result);

                if (result.Passed)
                {
                    continue;
                }

                if (Retires(active, next, one.Name))
                {
                    retired.Add(one.Name);
                }
                else
                {
                    regressions.Add(one.Name);
                }
            }
        }

        return new ToolGateResult(
            own.All(one => one.Passed) && regressions.Count == 0,
            own,
            regression,
            regressions,
            retired);
    }

    /// <summary>Whether a draft has retired a known-good case the only way that counts.</summary>
    private static bool Retires(ToolVersion active, ToolVersion next, string name) =>
        ToolCompatibility.Declared(active, next)
        && next.Compatibility.RetiredCases.Contains(name, StringComparer.OrdinalIgnoreCase);
}
