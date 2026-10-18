using Spectre.Console;
using Spectre.Console.Rendering;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// The console every command shares, still able to read an answer after an
/// accessibility profile has stopped it redrawing.
/// </summary>
/// <remarks>
/// <para>
/// Spectre has one flag for two things: whether output may redraw itself, and
/// whether input may be read. A profile that refuses redraws turns the flag
/// off for the first, and Spectre's own input then refuses every prompt with
/// "Failed to read input in non-interactive mode" - so <c>loadout update</c>
/// could not ask whether to install, in a terminal somebody was typing into.
/// </para>
/// <para>
/// Whether keys can be read is settled once, from the console as it was
/// detected, before any profile touched it. A console that was never
/// interactive - a pipe, the daemon's log - keeps its own input and goes on
/// refusing, which is what a script that did not expect a question needs.
/// </para>
/// </remarks>
public sealed class AnsweringConsole : IAnsiConsole
{
    private readonly IAnsiConsole _inner;
    private readonly IAnsiConsoleInput _keys;
    private readonly bool _canRead;

    public AnsweringConsole(IAnsiConsole inner, IAnsiConsoleInput keys)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(keys);

        _inner = inner;
        _keys = keys;
        _canRead = inner.Profile.Capabilities.Interactive;
    }

    public Profile Profile => _inner.Profile;

    public IAnsiConsoleCursor Cursor => _inner.Cursor;

    public IAnsiConsoleInput Input => _canRead ? _keys : _inner.Input;

    public IExclusivityMode ExclusivityMode => _inner.ExclusivityMode;

    public RenderPipeline Pipeline => _inner.Pipeline;

    public void Clear(bool home) => _inner.Clear(home);

    public void Write(IRenderable renderable) => _inner.Write(renderable);

    public void WriteAnsi(Action<AnsiWriter> action) => _inner.WriteAnsi(action);

    /// <summary>The terminal's own keyboard, read without asking Spectre's flag.</summary>
    public sealed class TerminalKeys : IAnsiConsoleInput
    {
        public bool IsKeyAvailable() => Console.KeyAvailable;

        public ConsoleKeyInfo? ReadKey(bool intercept) => Console.ReadKey(intercept);

        public async Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken)
        {
            // The same wait Spectre's own input makes: the console has no
            // awaitable read, so it is polled briefly until a key arrives.
            while (!Console.KeyAvailable)
            {
                await Task.Delay(5, cancellationToken).ConfigureAwait(false);
            }

            return Console.ReadKey(intercept);
        }
    }
}
