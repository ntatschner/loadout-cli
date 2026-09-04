using Loadout.Models.Configuration;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Usage;

/// <summary>The usage readers that come from configuration rather than code.</summary>
internal interface IDeclaredUsageHistories
{
    /// <summary>One reader per custom agent that describes where its numbers are.</summary>
    IReadOnlyList<IUsageHistory> All { get; }
}

/// <summary>
/// Builds a usage reader for every custom agent that describes its accounting.
/// </summary>
/// <remarks>
/// Separate from the session readers, and an agent may have one without the
/// other. Describing where sessions are is enough to be listed; describing where
/// the numbers are is what makes an agent countable. Saying only what is true of
/// an agent beats saying whatever is convenient.
/// </remarks>
internal sealed class DeclaredUsageHistories : IDeclaredUsageHistories
{
    public DeclaredUsageHistories(LauncherConfig config, IEnvironmentProvider environment)
    {
        ArgumentNullException.ThrowIfNull(config);

        All = config.CustomAgents
            .Where(entry => entry.Value?.Transcripts is { } format && format.CanCount)
            .Select(entry => (IUsageHistory)new DeclaredUsageHistory(
                entry.Key,
                entry.Value.Transcripts!,
                environment))
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<IUsageHistory> All { get; }
}
