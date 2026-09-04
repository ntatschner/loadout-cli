using Loadout.Core.Usage;

namespace Loadout.Tests.Fakes;

/// <summary>A spend watch with nothing to say.</summary>
/// <remarks>
/// The state somebody who set no threshold is in, which is the default and the
/// common case. Tests about launching are not about spending, and a real one
/// here would read the machine's own transcripts and make them depend on what
/// its owner had been doing.
/// </remarks>
public sealed class QuietSpendWatch : ISpendWatch
{
    /// <summary>What to say, if a test wants it to say something.</summary>
    public List<string> Warnings { get; } = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> WarningsAsync(
        string? projectSlug,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Warnings);
}
