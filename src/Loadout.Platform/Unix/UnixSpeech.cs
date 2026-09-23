using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Platform.Unix;

/// <summary>
/// Speaks through whichever command this Unix has for it.
/// </summary>
/// <remarks>
/// <para>
/// macOS has <c>say</c> in the base system. The freedesktop desktops have
/// <c>spd-say</c>, which talks to speech-dispatcher, which is what Orca is
/// already using — so speaking through it reaches the same voice rather than
/// starting a second one.
/// </para>
/// <para>
/// VoiceOver has its own AppleScript route that would be better than
/// <c>say</c>, because it is the voice the person is already listening to. It
/// needs the person to allow scripting in VoiceOver Utility first, so it is
/// not something to reach for without asking, and it is not built here.
/// </para>
/// <para>
/// <strong>Neither route is verified.</strong> This was written on Windows.
/// The commands and their flags are the documented ones and the code that
/// builds them is covered; nothing has been heard.
/// </para>
/// </remarks>
public sealed class UnixSpeech : ISpeech
{
    private readonly IProcessLauncher _processes;
    private readonly IExecutableResolver _resolver;
    private readonly bool _macOS;

    public UnixSpeech(IProcessLauncher processes, IExecutableResolver resolver, bool macOS)
    {
        _processes = processes;
        _resolver = resolver;
        _macOS = macOS;
    }

    /// <inheritdoc />
    public string Name => _macOS ? "say" : "spd-say";

    private string? Command => _resolver.Resolve(Name);

    /// <inheritdoc />
    public Task<OperationResult<string>> IsAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult(Command is { Length: > 0 } found
            ? OperationResult<string>.Ok($"'{found}' will speak.")
            : OperationResult<string>.Fail(
                $"'{Name}' was not found, so nothing here can speak."
                + (_macOS ? string.Empty : " It comes with speech-dispatcher.")));

    /// <inheritdoc />
    public async Task<OperationResult> SayAsync(
        string text,
        bool interrupt = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return OperationResult.Ok();
        }

        if (Command is not { Length: > 0 } command)
        {
            return OperationResult.Fail($"'{Name}' was not found.");
        }

        var result = await _processes.RunAsync(
            new ProcessRequest(command, Arguments(text, interrupt, _macOS)),

            // Short, because a launcher waiting on a sentence is a launcher
            // that has stopped answering the key that started it.
            TimeSpan.FromSeconds(5),
            ct).ConfigureAwait(false);

        return result.Failed || result.Value is not { Succeeded: true }
            ? OperationResult.Fail($"'{Name}' would not speak.")
            : OperationResult.Ok();
    }

    /// <summary>
    /// What to hand the speaking command, for one sentence.
    /// </summary>
    /// <remarks>
    /// The text is its own argument and never part of a shell string. A node's
    /// name or a project's path can contain anything at all, and a sentence
    /// that became a command would be the worst possible way to lose this
    /// argument.
    /// </remarks>
    internal static IReadOnlyList<string> Arguments(string text, bool interrupt, bool macOS) =>
        macOS
            ? [text]

            // --cancel first so a new line replaces the one being read rather
            // than queueing behind it: somebody holding the arrow key down
            // would otherwise be minutes behind their own cursor.
            : interrupt ? ["--cancel", "--wait", text] : [text];

    /// <inheritdoc />
    public async Task<OperationResult> SilenceAsync(CancellationToken ct = default)
    {
        if (Command is not { Length: > 0 } command)
        {
            return OperationResult.Ok();
        }

        // macOS has no stop for 'say'; each call replaces the last anyway
        // because it is one process per sentence.
        if (_macOS)
        {
            return OperationResult.Ok();
        }

        await _processes.RunAsync(
            new ProcessRequest(command, ["--cancel"]),
            TimeSpan.FromSeconds(5),
            ct).ConfigureAwait(false);

        return OperationResult.Ok();
    }
}
