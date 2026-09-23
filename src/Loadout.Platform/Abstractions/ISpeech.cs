using Loadout.Models.Results;

namespace Loadout.Platform.Abstractions;

/// <summary>
/// Saying something out loud, through whatever this machine has to say it
/// with.
/// </summary>
/// <remarks>
/// <para>
/// A channel to a screen reader that is already running, or failing that to
/// the operating system's own voice. Loadout never starts a screen reader and
/// never installs one: it speaks to what is there, and says plainly when there
/// is nothing.
/// </para>
/// <para>
/// This exists because no terminal toolkit has an accessibility provider on
/// Windows or macOS, so a full-screen application cannot be announced the way
/// a native window is. Handing text to the reader directly is the established
/// way round that, and the only one available today.
/// </para>
/// <para>
/// Everything here is best-effort and says so. Speech that failed is not an
/// error worth stopping for — somebody who cannot hear the launcher can still
/// read it — so a failure is reported where it is asked for and never thrown
/// into the middle of a screen.
/// </para>
/// </remarks>
public interface ISpeech
{
    /// <summary>What is doing the speaking, for diagnostics: "nvda", "sapi", "say".</summary>
    string Name { get; }

    /// <summary>Whether anything on this machine can speak right now, and what.</summary>
    Task<OperationResult<string>> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Says one thing.
    /// </summary>
    /// <param name="text">
    /// A sentence, already redacted. Nothing here inspects what it is given:
    /// a channel that decided what was safe to say would be a second opinion
    /// about redaction, and the one that matters is the caller's.
    /// </param>
    /// <param name="interrupt">
    /// Whether to stop what is being said first. True for something that
    /// replaces what was being said — a new row under the cursor — and false
    /// for something that adds to it.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    Task<OperationResult> SayAsync(string text, bool interrupt = true, CancellationToken ct = default);

    /// <summary>Stops whatever is being said.</summary>
    Task<OperationResult> SilenceAsync(CancellationToken ct = default);
}
