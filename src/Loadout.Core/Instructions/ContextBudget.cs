namespace Loadout.Core.Instructions;

/// <summary>One layer of what a session is given, and what it costs.</summary>
/// <param name="Name">What the layer is called.</param>
/// <param name="Bytes">Its size, which is the measure that can actually be known.</param>
/// <param name="EstimatedTokens">The same, approximately, in tokens.</param>
/// <param name="EveryLaunch">
/// Whether it is paid for on every launch or only when the work reaches for it.
/// The whole reason the layers exist is that these two prices differ, so a
/// figure that added them together would hide the thing it was meant to show.
/// </param>
public sealed record ContextLayer(string Name, long Bytes, int EstimatedTokens, bool EveryLaunch);

/// <summary>
/// What a session costs before it starts, across every layer at once.
/// </summary>
/// <remarks>
/// <para>
/// The layers have been budgeted separately and in different units: specialists
/// against a token ceiling that is enforced, rules against a byte threshold that
/// is advisory, and the memory index against nothing at all. So there has been
/// no single answer to "what does a session here cost", and the one hard limit
/// governed the layer that was already the most disciplined.
/// </para>
/// <para>
/// Advisory, deliberately. Nothing here drops anything: a launch that quietly
/// lost a rule somebody wrote would be worse than one that costs more than
/// expected, and the launcher's habit is to show a change before making it.
/// </para>
/// </remarks>
/// <param name="Layers">Every layer, in the order they are worth reading.</param>
/// <param name="TokenBudget">The ceiling in force, or 0 when none is set.</param>
public sealed record ContextBudget(IReadOnlyList<ContextLayer> Layers, int TokenBudget)
{
    /// <summary>
    /// Bytes to a token.
    /// </summary>
    /// <remarks>
    /// The same four bytes a specialist is estimated by, so the layers are added
    /// in one unit rather than two. Called an estimate everywhere it is shown:
    /// no tokeniser here matches the ones the providers use, and a figure
    /// presented as exact would be believed.
    /// </remarks>
    public const double BytesPerToken = 4.0;

    /// <summary>What every launch pays, whatever the task.</summary>
    public int EveryLaunchTokens =>
        Layers.Where(layer => layer.EveryLaunch).Sum(layer => layer.EstimatedTokens);

    /// <summary>What is there to be reached for but is not paid for up front.</summary>
    public int OnDemandTokens =>
        Layers.Where(layer => !layer.EveryLaunch).Sum(layer => layer.EstimatedTokens);

    /// <summary>How much of the budget the always-loaded layers spend.</summary>
    public double? UsedFraction =>
        TokenBudget > 0 ? (double)EveryLaunchTokens / TokenBudget : null;

    /// <summary>Whether what every launch pays is past the ceiling.</summary>
    public bool IsOverBudget => TokenBudget > 0 && EveryLaunchTokens > TokenBudget;

    /// <summary>
    /// Adds up what a session would be given.
    /// </summary>
    /// <param name="instructions">The specialists resolved for this launch.</param>
    /// <param name="alwaysLoadedRuleBytes">
    /// Core instructions and rules that apply whatever the task.
    /// </param>
    /// <param name="scopedRuleBytes">Rules that load only when the work touches their paths.</param>
    /// <param name="memoryIndexBytes">
    /// The memory index, which is the only part of memory a session pays for:
    /// topics stay on disk and are read when something makes them relevant.
    /// </param>
    public static ContextBudget From(
        Models.Instructions.EffectiveInstructions instructions,
        long alwaysLoadedRuleBytes,
        long scopedRuleBytes,
        long memoryIndexBytes)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        var layers = new List<ContextLayer>
        {
            new("Specialists", instructions.Budget.Bytes, instructions.Budget.EstimatedTokens, true),
            new("Instructions and rules", alwaysLoadedRuleBytes, Tokens(alwaysLoadedRuleBytes), true),
            new("Memory index", memoryIndexBytes, Tokens(memoryIndexBytes), true),
            new("Scoped rules", scopedRuleBytes, Tokens(scopedRuleBytes), false),
        };

        return new ContextBudget(layers, instructions.Budget.TokenBudget);
    }

    private static int Tokens(long bytes) =>
        bytes <= 0 ? 0 : (int)Math.Ceiling(bytes / BytesPerToken);
}
