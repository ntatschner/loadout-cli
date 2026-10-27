using FluentAssertions;
using Loadout.Core.Ask;
using Loadout.Core.Tasks;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Reading what the Ask agent answers, strictly: whatever it names has to be
/// on this machine, because a request it proposes may become a plan somebody
/// approves.
/// </summary>
public sealed class AskAgentSchemaTests
{
    private static readonly AskVocabulary Names = new([("webapp", []), ("api", [])], ["bug-hunt"]);

    [Fact]
    public void A_request_is_read_into_the_same_shape_the_parser_gives()
    {
        var read = AskAgentSchema.Read(
            """{"answer": null, "request": {"shape": "team-queue", "from": null, "until": null, "project": "API", "count": 3, "pick": "easy", "team": "Bug-Hunt", "start": true}}""",
            Names);

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value.Request.Should().Be(new AskRequest(AskShape.TeamQueue, null, "api", 3, TaskPick.Easy, "bug-hunt", true));
    }

    [Fact]
    public void A_period_is_read_as_its_days()
    {
        var read = AskAgentSchema.Read(
            """{"answer": null, "request": {"shape": "spend", "from": "2026-10-19", "until": "2026-10-25", "project": null, "count": null, "pick": "any", "team": null, "start": false}}""",
            Names);

        read.Value.Request!.Period!.From.Should().Be(new DateOnly(2026, 10, 19));
        read.Value.Request.Period.Until.Should().Be(new DateOnly(2026, 10, 25));
    }

    [Fact]
    public void An_answer_in_words_needs_no_request()
    {
        var read = AskAgentSchema.Read("""{"answer": "Nobody broke it.", "request": null}""", Names);

        read.Value.Answer.Should().Be("Nobody broke it.");
        read.Value.Request.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"answer": null, "request": null}""", "neither an answer nor a request")]
    [InlineData("""{"answer": null, "request": {"shape": "deploy", "pick": "any", "start": false}}""", "'deploy' is not a kind of request")]
    [InlineData("""{"answer": null, "request": {"shape": "tasks", "project": "elsewhere", "pick": "any", "start": false}}""", "'elsewhere' is not a project on this machine")]
    [InlineData("""{"answer": null, "request": {"shape": "team-queue", "team": "red-team", "pick": "any", "start": true}}""", "'red-team' is not a team on this machine")]
    [InlineData("""{"answer": null, "request": {"shape": "spend", "from": "last tuesday", "pick": "any", "start": false}}""", "is not a date")]
    [InlineData("""{"answer": null, "request": {"shape": "spend", "from": "2026-10-25", "until": "2026-10-19", "pick": "any", "start": false}}""", "ends before it starts")]
    [InlineData("not json", "could not be used")]
    public void Anything_not_on_this_machine_or_not_well_formed_is_refused(string json, string why)
    {
        var read = AskAgentSchema.Read(json, Names);

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain(why);
    }

    [Fact]
    public void The_agent_is_told_the_names_it_may_use_and_that_it_cannot_act()
    {
        var prompt = AskAgentSchema.Prompt("who broke the build", new AskAgentContext(Names, new DateOnly(2026, 10, 26), "webapp"));

        prompt.Should().Contain("Projects: webapp, api").And.Contain("Teams: bug-hunt").And.Contain("2026-10-26 (Monday)");
        prompt.Should().Contain("You cannot start, queue, record or change anything");
        prompt.Should().EndWith("who broke the build" + Environment.NewLine);
    }

    [Fact]
    public void The_schema_given_to_the_agent_is_valid_json_naming_both_halves()
    {
        using var document = System.Text.Json.JsonDocument.Parse(AskAgentSchema.Json);

        document.RootElement.GetProperty("required").EnumerateArray().Select(one => one.GetString())
            .Should().Equal("answer", "request");
    }
}
