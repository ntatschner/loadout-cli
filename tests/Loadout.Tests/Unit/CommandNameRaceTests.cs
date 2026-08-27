using FluentAssertions;
using Loadout.Cli;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The command set is built once, whoever asks and however many at a time.
/// </summary>
/// <remarks>
/// <para>
/// <c>CommandNames</c> lazily populated a shared mutable set with no
/// synchronisation: two callers could both find it empty, both run the
/// registration, and each read it while the other was still adding — so a
/// command that is registered comes back as one that is not.
/// </para>
/// <para>
/// <see cref="Loadout.Cli.Infrastructure.Catalogue"/> is the same shape and was
/// already guarded, and its own comment describes this race exactly. The set of
/// names beside it was simply missed.
/// </para>
/// <para>
/// <strong>These tests do not prove the fix.</strong> The initialisation
/// happens once per process, so by the time any test runs another may already
/// have triggered it and no contention is possible; and even alone, thirty-two
/// threads did not reliably land inside a window that small. The reverted
/// version passes this file. What is checked here is that concurrent access is
/// safe and consistent, which is worth holding down on its own — the argument
/// for the change itself rests on the code, not on a red test.
/// </para>
/// </remarks>
public sealed class CommandNameRaceTests
{
    [Fact]
    public void Asking_from_many_threads_at_once_is_safe_and_consistent()
    {
        const int Callers = 32;

        var results = new IReadOnlySet<string>[Callers];
        var failures = new Exception?[Callers];

        // Released together, so they contend rather than queue politely.
        using var gate = new ManualResetEventSlim(false);

        var threads = Enumerable.Range(0, Callers)
            .Select(i => new Thread(() =>
            {
                gate.Wait();

                try
                {
                    results[i] = Program.CommandNames();
                }
                catch (Exception ex)
                {
                    failures[i] = ex;
                }
            }))
            .ToList();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        gate.Set();

        foreach (var thread in threads)
        {
            thread.Join(TimeSpan.FromSeconds(30)).Should().BeTrue("no caller should deadlock");
        }

        // A HashSet mutated from two threads can corrupt outright rather than
        // merely losing an entry, and that does surface as a throw.
        failures.Should().AllSatisfy(f => f.Should().BeNull());

        var first = results[0];

        first.Should().NotBeEmpty();
        results.Should().AllSatisfy(r => r!.Count.Should().Be(first.Count));
    }

    [Fact]
    public void The_set_is_complete_rather_than_merely_agreed_upon()
    {
        var names = Program.CommandNames();

        // Thirty-two callers agreeing on an empty set would satisfy the test
        // above and mean nothing.
        names.Should().Contain("instructions");
        names.Should().Contain("usage");
        names.Should().Contain("launch");
    }
}
