using System.Runtime.CompilerServices;

namespace Loadout.Tests;

/// <summary>
/// Keeps the screen tests' toolkit off the test host's own console.
/// <para>
/// Under <c>dotnet test</c> the host has no console window and pipes for all
/// three standard handles, but on Windows it still has a console, hidden,
/// and Terminal.Gui finds it by opening the console devices directly rather
/// than by looking at the standard handles. So every screen test's Init ran
/// the real Windows driver against that console: mode changes, terminal
/// queries, keyboard-protocol detection, all aimed at a console nobody was
/// reading. On Linux and macOS there is no such console and the driver runs
/// degraded, which is the mode every screen test has always passed in.
/// </para>
/// <para>
/// What that cost: every child the suite starts without a window of its own
/// attaches to the host's console, and from some point in about one Windows
/// run in three, every such child died on start-up with 0xC0000142 for the
/// rest of the run, while a child given its own console started fine. The
/// probe that showed this, <c>SpawnRefusalProbe</c>, was run once with the
/// host's environment, working directory and desktop all healthy, so the
/// console the children inherited was the one thing left. It began the day
/// the screen tests arrived, 25 August 2026.
/// </para>
/// <para>
/// The switch is the toolkit's own: with this set, its terminal detection
/// answers no before touching anything, and the driver runs degraded here as
/// it does on the other two operating systems.
/// </para>
/// </summary>
internal static class ConsoleIsolation
{
    /// <summary>The variable Terminal.Gui's <c>Driver.IsAttachedToTerminal</c> honours.</summary>
    internal const string Switch = "DisableRealDriverIO";

    [ModuleInitializer]
    internal static void Isolate() =>
        Environment.SetEnvironmentVariable(Switch, "1");
}
