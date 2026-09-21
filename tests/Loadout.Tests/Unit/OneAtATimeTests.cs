using System.Reflection;
using FluentAssertions;
using Loadout.Agents.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The wrapper that stops two workers asking a person two things at once.
/// </summary>
/// <remarks>
/// <para>
/// It is a decorator, and the way a decorator goes wrong is by not forwarding
/// something. That is not a theoretical worry here: the console grew a way to
/// offer a brief for changing, this wrapper did not forward it, and the
/// interface's own default asked the wrapper's <c>ConfirmAsync</c> instead — so
/// everything kept working, quietly, with the lead's own words and nobody ever
/// offered the change.
/// </para>
/// <para>
/// Nothing in the run noticed. Four tests written for the new behaviour did,
/// which is the only reason this is a test rather than a bug somebody meets in
/// six months.
/// </para>
/// </remarks>
public sealed class OneAtATimeTests
{
    [Fact]
    public async Task Everything_that_asks_a_person_something_reaches_the_person()
    {
        var inner = new Recording();

        using var one = new OneAtATime(inner);

        // Through the interface, which is how the run holds it - and which is
        // what makes a member the wrapper does not forward fall through to the
        // interface's own default rather than failing to compile.
        ITeamConsole wrapped = one;

        await wrapped.ConfirmAsync("go on");
        await wrapped.ReviseAsync("brief it", "do the thing");
        await wrapped.DecideAsync(new ReportQuestion("which", ["a", "b"], "a"));

        wrapped.Note("a line");
        wrapped.Starting("C:/runs/r");

        _ = wrapped.CanAsk;

        inner.Reached.Should().Equal(
            "ConfirmAsync", "ReviseAsync", "DecideAsync", "Note", "Starting", "CanAsk");
    }

    [Fact]
    public void Nothing_on_the_console_is_left_to_its_own_default()
    {
        // The rule rather than the instance. Anything added to ITeamConsole
        // from here on has to be forwarded, and a default implementation on the
        // interface is exactly what makes forgetting it silent.
        var wrapper = typeof(OneAtATime);

        var missing = typeof(ITeamConsole)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Where(member => member is MethodInfo or PropertyInfo)
            .Select(member => member.Name)
            .Where(name => !name.StartsWith("get_", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Where(name => wrapper.GetMember(
                name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length == 0)
            .ToList();

        missing.Should().BeEmpty(
            "a decorator that does not forward something lets the interface's own default answer "
            + "instead, and the run carries on as though nobody was asked");
    }

    /// <summary>A console that writes down what it was asked, in order.</summary>
    private sealed class Recording : ITeamConsole
    {
        public List<string> Reached { get; } = [];

        public bool CanAsk
        {
            get
            {
                Reached.Add("CanAsk");

                return true;
            }
        }

        public void Starting(string runDirectory) => Reached.Add("Starting");

        public Task<bool> ConfirmAsync(string what, CancellationToken ct = default)
        {
            Reached.Add("ConfirmAsync");

            return Task.FromResult(true);
        }

        public Task<string?> ReviseAsync(string what, string task, CancellationToken ct = default)
        {
            Reached.Add("ReviseAsync");

            return Task.FromResult<string?>(task);
        }

        public Task<string?> DecideAsync(ReportQuestion question, CancellationToken ct = default)
        {
            Reached.Add("DecideAsync");

            return Task.FromResult<string?>(question.Recommendation);
        }

        public void Note(string line) => Reached.Add("Note");
    }
}
