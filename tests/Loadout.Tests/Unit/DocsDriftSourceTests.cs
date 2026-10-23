using FluentAssertions;
using Loadout.Core.Triggers;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>Documentation coming adrift, as the trigger spine hears about it.</summary>
public sealed class DocsDriftSourceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-drift-" + Guid.NewGuid().ToString("N"));
    private readonly string _repository;
    private readonly FakeGit _git;
    private readonly DocsDriftSource _source;

    public DocsDriftSourceTests()
    {
        _repository = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(_repository, "docs"));
        Page("Nothing to see.");

        _git = new FakeGit(_repository);
        _source = new DocsDriftSource(
            Path.Combine(_root, "state"),
            new FakeProjects("demo", _repository),
            _git,
            (_, _) => Task.FromResult<Loadout.Models.Instructions.DocsPolicy?>(null));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task A_page_that_goes_stale_after_the_first_look_is_one_event_and_one_already_stale_is_none()
    {
        (await Look()).Should().BeEmpty("the first look writes down what is there");

        Page("See [the guide](missing.md).");
        Moved("c2");

        var drifted = (await Look()).Should().ContainSingle().Which;
        drifted.Kind.Should().Be(TriggerEventKinds.DocsDrift);
        drifted.Subject.Should().Be("docs:demo");
        drifted.Field("path").Should().Be("docs/guide.md");
        drifted.Field("count").Should().Be("1");

        Moved("c3");
        (await Look()).Should().BeEmpty("the same stale link is not news a second time");
    }

    [Fact]
    public async Task A_checkout_that_has_not_moved_is_not_audited_again()
    {
        await Look();

        // Stale now, but nothing was committed, so nothing is looked at.
        Page("See [the guide](missing.md).");

        (await Look()).Should().BeEmpty();

        Moved("c2");
        (await Look()).Should().ContainSingle();
    }

    [Fact]
    public async Task A_project_no_rule_waits_on_is_never_looked_at()
    {
        (await _source.LookAsync(_ => false, Now, CancellationToken.None)).Should().BeEmpty();

        Page("See [the guide](missing.md).");
        Moved("c2");

        // Its first look is now, so its stale page is the baseline.
        (await Look()).Should().BeEmpty();
    }

    private Task<IReadOnlyList<TriggerEvent>> Look() => _source.LookAsync(_ => true, Now, CancellationToken.None);

    private void Moved(string commit) => _git.State = _git.State with { HeadCommit = commit };

    private void Page(string body) =>
        File.WriteAllText(Path.Combine(_repository, "docs", "guide.md"), $"# Guide\n\n{body}\n");
}
