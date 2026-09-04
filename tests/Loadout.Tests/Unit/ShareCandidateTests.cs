using FluentAssertions;
using Loadout.Core.Workspace;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What gets offered for sharing, and what is never even looked at.
/// </summary>
/// <remarks>
/// A workspace holds handoffs, memory and decisions, which is why one is
/// created private: publishing them is an irreversible disclosure. So the
/// search has an allow list rather than a deny list, and the private
/// directories are named as well — widening one must not quietly widen the
/// other.
/// </remarks>
public sealed class ShareCandidateTests
{
    private static WorkspaceFile File(string path, string text) => new(path, text);

    [Fact]
    public void Guidance_that_never_names_the_project_is_offered()
    {
        var found = ShareCandidates.Find(
            [File("projects/demo/specialists/style.md", "Prefer small functions.")],
            "demo",
            "Demo");

        found.Should().ContainSingle().Which.RelativePath
            .Should().Be("projects/demo/specialists/style.md");
    }

    [Fact]
    public void Guidance_about_this_project_is_left_where_it_is()
    {
        ShareCandidates.Find(
            [File("projects/demo/specialists/style.md", "In demo, prefer small functions.")],
            "demo",
            "Demo")
            .Should().BeEmpty();
    }

    [Fact]
    public void The_project_name_counts_as_naming_it_too()
    {
        // The name has to be unlike the slug for this to test anything. Written
        // as "demo"/"Demo" it passed with the name check deleted, because the
        // slug check was catching it and the assertion could not tell.
        ShareCandidates.Find(
            [File("projects/acme-web/specialists/style.md", "Storefront prefers small functions.")],
            "acme-web",
            "Storefront")
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("projects/demo/handoffs/last-session.md")]
    [InlineData("projects/demo/memory/how-the-build-works.md")]
    [InlineData("projects/demo/state/something.md")]
    public void The_private_half_of_a_workspace_is_never_offered(string path)
    {
        // The reason a workspace is created private. These are not filtered
        // out after being found — they are not searched, because a filter is a
        // place a mistake can be made and not looking is not.
        ShareCandidates.Find([File(path, "Nothing here names the project.")], "demo", "Demo")
            .Should().BeEmpty();
    }

    [Fact]
    public void A_private_directory_wins_over_a_searched_one()
    {
        // The case the allow list alone does not cover: a folder named like a
        // searched one, sitting inside the private half. Without the explicit
        // refusal this is offered, because "specialists/" matches.
        ShareCandidates.Find(
            [File("projects/demo/memory/specialists/note.md", "Nothing here names it.")],
            "demo",
            "Demo")
            .Should().BeEmpty();
    }

    [Fact]
    public void Nothing_outside_the_searched_directories_is_offered()
    {
        // An allow list, so a directory added to the workspace later is not
        // searched until somebody says it should be.
        ShareCandidates.Find(
            [File("projects/demo/project.yaml", "schema_version: 1")],
            "demo",
            "Demo")
            .Should().BeEmpty();
    }

    [Fact]
    public void Every_offer_says_why_so_it_can_be_disagreed_with()
    {
        var found = ShareCandidates.Find(
            [File("projects/demo/rules/style.md", "Prefer small functions.")], "demo", "Demo");

        // The signal is weak on purpose. Guidance filed under a project that
        // never names it is often general and sometimes not, so this offers
        // rather than decides, and the reason has to be refutable at a glance.
        found.Single().Reason.Should().Contain("never mentions demo");
    }

    [Fact]
    public void A_very_short_project_name_is_not_matched_everywhere()
    {
        // A two-letter name would appear inside ordinary words and mark every
        // file as project-specific, which silently turns the whole feature off.
        ShareCandidates.Find(
            [File("projects/x/specialists/style.md", "Prefer small functions.")], "x", "X")
            .Should().ContainSingle();
    }

    [Fact]
    public void Nothing_at_all_is_offered_from_an_empty_workspace() =>
        ShareCandidates.Find([], "demo", "Demo").Should().BeEmpty();

    [Theory]
    [InlineData("projects/demo/handoffs/last.md")]
    [InlineData("projects/demo/memory/thing.md")]
    public void The_private_half_is_refused_by_name_as_well(string path)
    {
        // The search would never offer these, but somebody can type one. The
        // refusal is named separately so that widening what is searched cannot
        // quietly widen what may be promoted.
        SharePaths.Rejection(path).Should().NotBeNull();
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("projects/demo/../../secrets.md")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/system.ini")]
    [InlineData("")]
    public void A_path_that_leaves_the_workspace_is_refused(string path) =>
        SharePaths.Rejection(path).Should().NotBeNull();

    [Fact]
    public void An_ordinary_path_is_accepted() =>
        SharePaths.Rejection("projects/demo/specialists/style.md").Should().BeNull();

    [Fact]
    public void Text_carrying_a_credential_is_refused_by_pattern_not_by_value()
    {
        const string Secret = "sk-ant-abcdefghijklmnopqrstuvwx";

        var refusal = SharedContent.Refusal($"Use the key {Secret} to authenticate.");

        refusal.Should().NotBeNull();

        // Named, never quoted. A refusal that printed what it found would put
        // the credential into scrollback and logs, which is the problem rather
        // than the report of it.
        refusal!.Should().NotContain(Secret);
        refusal.Should().Contain("credential");
    }

    [Fact]
    public void Ordinary_guidance_is_not_refused() =>
        SharedContent.Refusal("Prefer small functions.").Should().BeNull();
}
