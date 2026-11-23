using System.Text;
using FluentAssertions;
using Loadout.Core.Ideas;
using Loadout.Models;
using Loadout.Models.Ideas;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Reading an idea's plan: the preview is the document accepting writes, byte
/// for byte; accepting can be held to what was read; and once accepted, the
/// file is read as it is now, wherever the idea went.
/// </summary>
public sealed class IdeaPlanTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();
    private readonly IdeaService _ideas;

    public IdeaPlanTests() => _ideas = _space.Ideas;

    public void Dispose() => _space.Dispose();

    private static IdeaReply Plan() => new()
    {
        Contract = IdeaSchema.Version,
        Understanding = "A page showing what is up.",
        Plan = new IdeaReply.ReplyPlan
        {
            Title = "Status page",
            DoneWhen = ["every host is listed"],
            Layers =
            [
                new IdeaReply.ReplyLayer
                {
                    Name = "Storage",
                    Options =
                    [
                        new IdeaReply.ReplyOption { Title = "SQLite", Recommended = true },
                        new IdeaReply.ReplyOption { Title = "Postgres" },
                    ],
                },
            ],
            Additions = [new IdeaReply.ReplyAddition { Title = "Alerts", Why = "So you hear first." }],
        },
    };

    private async Task<IdeaPlace> ReadyAsync()
    {
        await _space.ProjectAsync("homelab");
        var idea = (await _ideas.CaptureAsync(null, "Status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        (await _ideas.RecordReplyAsync(place, Plan())).Succeeded.Should().BeTrue();
        await _ideas.JudgeAsync(place, "A1", IdeaVerdict.Keep);

        return place;
    }

    private string File(string relative) =>
        Path.Combine(_space.Workspace.LocalPath, relative.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public async Task The_preview_is_byte_for_byte_the_file_accepting_writes()
    {
        var place = await ReadyAsync();
        string[] doneWhen = ["the page loads in under a second", " "];

        var preview = (await _ideas.PlanAsync(place, doneWhen)).Value!;
        var accepted = await _ideas.AcceptAsync(place, "homelab", "nigel", doneWhen: doneWhen, previewed: preview.Hash);

        accepted.Succeeded.Should().BeTrue(accepted.Error);
        preview.State.Should().Be(IdeaPlanState.Preview);
        preview.Markdown.Should().Contain("- the page loads in under a second").And.NotContain("every host is listed");

        var written = await System.IO.File.ReadAllBytesAsync(File(accepted.Value!.PlanPath));
        written.Should().Equal(Encoding.UTF8.GetBytes(preview.Markdown), "what was confirmed is what was written");
    }

    [Fact]
    public async Task Previewing_writes_nothing_and_changes_nothing()
    {
        var place = await ReadyAsync();
        var before = System.Text.Json.JsonSerializer.Serialize((await _ideas.ReadAsync(place)).Value);

        (await _ideas.PlanAsync(place, ["something else"])).Succeeded.Should().BeTrue();

        Directory.GetFiles(_space.Workspace.LocalPath, "*.md", SearchOption.AllDirectories)
            .Should().BeEmpty("a preview is not a plan written");
        System.Text.Json.JsonSerializer.Serialize((await _ideas.ReadAsync(place)).Value)
            .Should().Be(before, "the done-when shown is not kept until it is accepted");
    }

    [Fact]
    public async Task Accepting_against_a_plan_that_changed_since_it_was_read_is_refused()
    {
        var place = await ReadyAsync();
        var read = (await _ideas.PlanAsync(place)).Value!;

        (await _ideas.ChooseAsync(place, "L1", "L1b")).Succeeded.Should().BeTrue();

        var refused = await _ideas.AcceptAsync(place, "homelab", "nigel", previewed: read.Hash);

        refused.Failed.Should().BeTrue();
        refused.ExitCode.Should().Be(ExitCode.ChangedSinceRead);
        refused.Error.Should().Contain("changed since you read it");
        Directory.GetFiles(_space.Workspace.LocalPath, "*.md", SearchOption.AllDirectories).Should().BeEmpty();

        var again = (await _ideas.PlanAsync(place)).Value!;
        (await _ideas.AcceptAsync(place, "homelab", "nigel", previewed: again.Hash)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Accepting_without_a_hash_is_as_it_was()
    {
        var place = await ReadyAsync();

        await _ideas.PlanAsync(place);
        (await _ideas.ChooseAsync(place, "L1", "L1b")).Succeeded.Should().BeTrue();

        (await _ideas.AcceptAsync(place, "homelab", "nigel")).Succeeded.Should().BeTrue(
            "only a confirmation that names what it read is held to it");
    }

    [Fact]
    public async Task Once_accepted_the_plan_is_the_file_as_it_is_now_wherever_the_idea_went()
    {
        var place = await ReadyAsync();
        var accepted = (await _ideas.AcceptAsync(place, "homelab", "nigel")).Value!;
        await _space.ProjectAsync("lab2");
        (await _ideas.MoveAsync("homelab", "lab2", place.Id)).Succeeded.Should().BeTrue();

        var landed = new IdeaPlace("lab2", place.Id);
        var shown = (await _ideas.PlanAsync(landed, ["ignored once accepted"])).Value!;

        shown.State.Should().Be(IdeaPlanState.Written);
        shown.Path.Should().Be($"projects/lab2/ideas/{place.Id}.md");
        shown.Changed.Should().BeFalse();
        shown.Markdown.Should().NotContain("ignored once accepted");
        accepted.PlanPath.Should().NotBe(shown.Path);

        await System.IO.File.AppendAllTextAsync(File(shown.Path!), "A line somebody added.\n");
        var edited = (await _ideas.PlanAsync(landed)).Value!;
        edited.Changed.Should().BeTrue();
        edited.Markdown.Should().EndWith("A line somebody added.\n");

        System.IO.File.Delete(File(shown.Path!));
        var gone = (await _ideas.PlanAsync(landed)).Value!;
        gone.State.Should().Be(IdeaPlanState.Missing);
        gone.Markdown.Should().BeEmpty();
        gone.Path.Should().Be(shown.Path);
    }

    [Fact]
    public async Task The_preview_names_the_layers_going_in_with_no_option_chosen()
    {
        // A round always chooses an option for a layer, so the only way to
        // none is a record that says so: one written before that, or by hand.
        var place = await ReadyAsync();
        var record = File($"ideas/{place.Id}.yaml");
        var yaml = await System.IO.File.ReadAllTextAsync(record);
        yaml.Should().Contain("chosen: L1a");
        await System.IO.File.WriteAllTextAsync(record, yaml.Replace("chosen: L1a", "chosen: ''", StringComparison.Ordinal));

        var preview = (await _ideas.PlanAsync(place)).Value!;

        preview.Undecided.Should().Equal("Storage");
        preview.Markdown.Should().Contain("No option chosen.");
    }

    [Fact]
    public async Task An_idea_with_no_plan_has_none_to_show()
    {
        var idea = (await _ideas.CaptureAsync(null, "Status page", "me")).Value!;

        var shown = await _ideas.PlanAsync(new IdeaPlace(null, idea.Id));

        shown.Failed.Should().BeTrue();
        shown.Error.Should().Contain("no plan yet");
    }
}
