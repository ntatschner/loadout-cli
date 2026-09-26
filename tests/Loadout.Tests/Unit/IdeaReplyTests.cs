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

    [Theory]
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
