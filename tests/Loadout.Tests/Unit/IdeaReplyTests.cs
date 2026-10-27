using FluentAssertions;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>Reading an agent's answer to a round, and refusing the ones that are not answers.</summary>
public sealed class IdeaReplyTests
{
    [Fact]
    public void An_answer_in_the_shape_is_read()
    {
        var read = IdeaReply.Read("""
            {
              "contract": "idea/1",
              "understanding": "A page.",
              "questions": [{ "question": "Public?", "why": "Hosting.", "options": ["yes", "no"], "recommendation": "no" }]
            }
            """);

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Questions.Single().Recommendation.Should().Be("no");
        read.Value.Plan.Should().BeNull();
    }

    [Fact]
    public void A_plan_is_read_with_its_placement()
    {
        var read = IdeaReply.Read("""
            {
              "contract": "idea/1", "understanding": "A page.", "questions": [],
              "plan": {
                "title": "Status page",
                "layers": [{ "name": "Storage", "purpose": "Keep checks.", "options": [
                  { "title": "SQLite", "detail": "One file.", "pros": ["simple"], "cons": [], "recommended": true } ] }],
                "additions": [],
                "project": { "slug": "", "is_new": true, "name": "Lab watch", "reason": "Nothing like it exists." }
              }
            }
            """);

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Plan!.Project.IsNew.Should().BeTrue();
        read.Value.Plan.Layers.Single().Options.Single().Recommended.Should().BeTrue();
    }

    [Fact]
    public void A_plan_in_the_current_shape_carries_its_proposed_done_when()
    {
        var read = IdeaReply.Read("""
            {
              "contract": "idea/2", "understanding": "A page.", "questions": [],
              "plan": {
                "title": "Status page",
                "layers": [{ "name": "Storage", "purpose": "Keep checks.", "options": [
                  { "title": "SQLite", "detail": "One file.", "pros": [], "cons": [], "recommended": true } ] }],
                "additions": [],
                "project": { "slug": "homelab", "is_new": false, "name": "", "reason": "It watches the lab." },
                "doneWhen": ["the page lists every host", "a host that is down shows red within a minute"]
              }
            }
            """);

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Plan!.DoneWhen.Should().Equal("the page lists every host", "a host that is down shows red within a minute");
    }

    [Fact]
    public void A_plan_in_the_shape_before_is_still_read_with_no_done_when()
    {
        var read = IdeaReply.Read("""
            {
              "contract": "idea/1", "understanding": "A page.", "questions": [],
              "plan": {
                "title": "Status page",
                "layers": [{ "name": "Storage", "purpose": "Keep checks.", "options": [
                  { "title": "SQLite", "detail": "One file.", "pros": [], "cons": [], "recommended": true } ] }],
                "additions": [],
                "project": { "slug": "", "is_new": false, "name": "", "reason": "" }
              }
            }
            """);

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Plan!.DoneWhen.Should().BeEmpty();
    }

    [Fact]
    public void The_schema_offers_done_when_on_the_plan_without_requiring_it()
    {
        using var schema = System.Text.Json.JsonDocument.Parse(IdeaSchema.Version3);
        var plan = schema.RootElement.GetProperty("properties").GetProperty("plan");

        schema.RootElement.GetProperty("properties").GetProperty("contract").GetProperty("const").GetString()
            .Should().Be(IdeaSchema.Version);
        plan.GetProperty("properties").TryGetProperty("doneWhen", out _).Should().BeTrue();
        plan.GetProperty("required").EnumerateArray().Select(one => one.GetString()).Should().NotContain("doneWhen");
    }

    [Fact]
    public void A_question_in_the_current_shape_says_what_it_relates_to()
    {
        var read = IdeaReply.Read("""
            {
              "contract": "idea/3", "understanding": "A page.",
              "questions": [{ "question": "Keep a week?", "why": "Size.", "options": [], "recommendation": "",
                              "relatesTo": "L2", "context": "Storage sizes depend on it." }]
            }
            """);

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Questions.Single().RelatesTo.Should().Be("L2");
        read.Value.Questions.Single().Context.Should().Be("Storage sizes depend on it.");
    }

    [Theory]
    [InlineData("idea/1")]
    [InlineData("idea/2")]
    public void A_question_in_an_earlier_shape_is_read_relating_to_nothing(string contract)
    {
        var read = IdeaReply.Read($$"""
            { "contract": "{{contract}}", "understanding": "A page.",
              "questions": [{ "question": "Public?", "why": "", "options": [], "recommendation": "" }] }
            """);

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Questions.Single().RelatesTo.Should().BeEmpty();
        read.Value.Questions.Single().Context.Should().BeEmpty();
    }

    [Fact]
    public void The_schema_requires_a_reference_and_context_on_every_question()
    {
        using var schema = System.Text.Json.JsonDocument.Parse(IdeaSchema.Version3);
        var question = schema.RootElement.GetProperty("properties").GetProperty("questions").GetProperty("items");

        question.GetProperty("required").EnumerateArray().Select(one => one.GetString())
            .Should().Contain(["relatesTo", "context"]);
    }

    [Theory]
    [InlineData("""{ "contract": "idea/4", "understanding": "", "questions": [] }""", "idea/4")]
    [InlineData(null, "without an")]
    [InlineData("not json", "not valid JSON")]
    [InlineData("""{ "contract": "report/1", "understanding": "", "questions": [] }""", "report/1")]
    [InlineData("""{ "contract": "idea/1", "understanding": "", "questions": [] }""", "neither asked")]
    [InlineData("""{ "contract": "idea/1", "understanding": "", "questions": [], "plan": { "title": "t", "layers": [], "additions": [], "project": { "slug": "", "is_new": false, "name": "", "reason": "" } } }""", "no layers")]
    public void What_is_not_an_answer_is_refused_with_the_reason(string? json, string reason)
    {
        var read = IdeaReply.Read(json);

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain(reason);
    }
}
