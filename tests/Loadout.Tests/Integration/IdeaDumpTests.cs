using FluentAssertions;
using Loadout.Core.Ideas;
using Loadout.Models;
using Loadout.Models.Ideas;
using Loadout.Models.Tasks;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Notes dropped in from elsewhere: kept verbatim, held to their own words when
/// split, and recorded as ideas and tasks only when the person says which.
/// </summary>
public sealed class IdeaDumpTests : IDisposable
{
    private const string Notes = """
        Things to do
        - fix the flaky login test on CI
        - maybe a status page for the home lab, with alerts
        - learn Rust properly
        """;

    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    private static DumpReply Split(params (string Title, string Excerpt, string Kind, string Project)[] items) => new()
    {
        Contract = DumpSchema.Version,
        Items = [.. items.Select(i => new DumpReply.ReplyItem
        {
            Title = i.Title,
            Excerpt = i.Excerpt,
            Kind = i.Kind,
            Project = i.Project,
            Reason = "It says so.",
        })],
    };

    private static readonly (string, string, string, string)[] ThreePieces =
    [
        ("Fix the flaky login test", "fix the flaky login test on CI", "task", "website"),
        ("Home lab status page", "maybe a status page for the home lab, with alerts", "idea", ""),
        ("Learn Rust", "learn Rust properly", "idea", "ghost"),
    ];

    private async Task<DumpPlace> DumpedAsync(string? project = null)
    {
        var kept = await _space.Dumps.KeepAsync(project, Notes, "notes.md");
        kept.Succeeded.Should().BeTrue(kept.Error);

        var place = new DumpPlace(project, kept.Value!.Id);
        (await _space.Dumps.RecordSplitAsync(place, Split(ThreePieces))).Succeeded.Should().BeTrue();

        return place;
    }

    [Fact]
    public void A_split_that_rewords_the_notes_is_refused_and_says_which_piece()
    {
        var json = """
            {"contract":"dump/1","items":[
              {"title":"a","excerpt":"fix the flaky login test on CI","kind":"task","project":"","reason":""},
              {"title":"b","excerpt":"build a status page for the lab","kind":"idea","project":"","reason":""}]}
            """;

        var read = DumpWork.Read(json, Notes);

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain("piece 2").And.Contain("word for word");
    }

    [Fact]
    public void A_line_break_the_agent_reflowed_is_not_a_rewording()
    {
        var json = """{"contract":"dump/1","items":[{"title":"t","excerpt":"Things to do - fix the flaky login test","kind":"task","project":"","reason":""}]}""";

        DumpWork.Read(json, Notes).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task The_notes_are_kept_exactly_as_they_arrived()
    {
        var kept = await _space.Dumps.KeepAsync(null, Notes, "notes.md");

        (await _space.Dumps.ReadAsync(new DumpPlace(null, kept.Value!.Id))).Value!.Text.Should().Be(Notes);
        (await _space.Tasks.ListAsync(null)).Value!.Should().BeEmpty("nothing is recorded until the person says so");
    }

    [Fact]
    public async Task Applying_records_ideas_and_tasks_where_the_agent_placed_them_when_those_projects_exist()
    {
        await _space.ProjectAsync("website");
        var place = await DumpedAsync();

        var applied = await _space.Dumps.ApplyAsync(place, [], null, "nigel");

        applied.Succeeded.Should().BeTrue(applied.Error);
        applied.Value!.Select(a => a.List).Should().Equal("website", null, null);

        var task = (await _space.Tasks.ListAsync("website")).Value!.Single();
        task.Kind.Should().Be(TaskKind.Task);
        task.Title.Should().Be("Fix the flaky login test");
        task.Note.Should().Be("fix the flaky login test on CI", "the person's own words travel with it");

        var ideas = (await _space.Tasks.ListAsync(null)).Value!;
        ideas.Should().OnlyContain(t => t.Kind == TaskKind.Idea);
        ideas.Select(t => t.Title).Should().BeEquivalentTo(["Home lab status page", "Learn Rust"],
            "'ghost' is not a project, so that piece stays on the list the notes were dropped on");

        var record = (await _space.Ideas.ReadAsync(new IdeaPlace(null, applied.Value![1].Id))).Value!;
        record.Ask.Should().Be("maybe a status page for the home lab, with alerts");
    }

    [Fact]
    public async Task Only_the_pieces_named_are_recorded_and_applying_again_does_not_double_them()
    {
        await _space.ProjectAsync("website");
        var place = await DumpedAsync();

        (await _space.Dumps.ApplyAsync(place, [2], null, "me")).Value!.Should().ContainSingle();
        (await _space.Tasks.ListAsync(null)).Value!.Should().ContainSingle();

        var rest = await _space.Dumps.ApplyAsync(place, [], null, "me");

        rest.Value!.Select(a => a.Item.Number).Should().Equal(1, 3);
        (await _space.Tasks.ListAsync(null)).Value!.Should().HaveCount(2);

        (await _space.Dumps.ApplyAsync(place, [2], null, "me")).Error.Should().Contain("recorded already");
    }

    [Fact]
    public async Task Where_the_person_says_outranks_where_the_agent_placed_each_piece()
    {
        await _space.ProjectAsync("website");
        await _space.ProjectAsync("homelab");
        var place = await DumpedAsync();

        var applied = await _space.Dumps.ApplyAsync(place, [], "homelab", "me");

        applied.Value!.Should().OnlyContain(a => a.List == "homelab");
        (await _space.Tasks.ListAsync("website")).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task A_piece_number_that_does_not_exist_records_nothing()
    {
        var place = await DumpedAsync();

        var applied = await _space.Dumps.ApplyAsync(place, [1, 9], null, "me");

        applied.Failed.Should().BeTrue();
        applied.Error.Should().Contain("9").And.Contain("Nothing was recorded");
        (await _space.Tasks.ListAsync(null)).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task A_dump_part_recorded_is_not_split_again_under_it()
    {
        var place = await DumpedAsync();
        await _space.Dumps.ApplyAsync(place, [2], null, "me");

        var again = await _space.Dumps.RecordSplitAsync(place, Split(ThreePieces[..1]));

        again.Failed.Should().BeTrue();
        (await _space.Dumps.ReadAsync(place)).Value!.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task Notes_with_a_credential_in_them_are_refused_whole()
    {
        var kept = await _space.Dumps.KeepAsync(null, Notes + "\ntoken ghp_" + new string('e', 36), "notes.md");

        kept.Failed.Should().BeTrue();
        kept.ExitCode.Should().Be(ExitCode.PolicyViolation);
        (await _space.Dumps.ListAsync()).Value!.Should().BeEmpty();
    }
}
