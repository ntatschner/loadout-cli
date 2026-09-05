using FluentAssertions;
using Loadout.Core.Manager;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The shape the manager screen reads, and what it is allowed to leave out.
/// </summary>
/// <remarks>
/// The view is deliberately flat: three families that answer "what is loaded
/// and why" three different ways, reduced to one row shape so a screen has one
/// thing to render. What each family means stays in the service that owns it —
/// nothing here re-decides whether a pack is trusted.
/// </remarks>
public sealed class ManagerInventoryTests
{
    private static ManagedItem Pack(string name, bool attention = false) =>
        new(ManagedKind.Pack, name, "git@example.com:std.git", "machine",
            attention ? "never approved here" : "active", attention);

    private static ManagedItem Server(string name, string scope = "project") =>
        new(ManagedKind.Server, name, "npx", scope, "active", NeedsAttention: false);

    private static ManagedItem Skill(string name) =>
        new(ManagedKind.Skill, name, "built-in", "workspace", "loaded when asked for", false);

    [Fact]
    public void Each_family_is_read_out_on_its_own()
    {
        var view = new ManagerView(
            [Pack("house"), Server("github"), Skill("bug-investigation")], []);

        view.OfKind(ManagedKind.Pack).Should().ContainSingle().Which.Name.Should().Be("house");
        view.OfKind(ManagedKind.Server).Should().ContainSingle().Which.Name.Should().Be("github");
        view.OfKind(ManagedKind.Skill).Should().ContainSingle();
    }

    [Fact]
    public void A_family_with_nothing_in_it_is_empty_rather_than_missing()
    {
        // The screen draws a heading either way. "No packs declared" and
        // "packs could not be read" are the difference somebody opens this
        // screen for, and collapsing them would lose it.
        new ManagerView([Server("github")], []).OfKind(ManagedKind.Pack).Should().BeEmpty();
    }

    [Fact]
    public void Nothing_loaded_is_an_answer()
    {
        ManagerView.Empty.Items.Should().BeEmpty();
        ManagerView.Empty.Notes.Should().BeEmpty();
    }

    [Fact]
    public void What_needs_a_decision_says_so()
    {
        var view = new ManagerView([Pack("trusted"), Pack("fresh", attention: true)], []);

        // The flag is the whole reason the screen exists rather than three
        // separate listings: one place to see that something is waiting on you.
        view.Items.Where(item => item.NeedsAttention)
            .Should().ContainSingle().Which.Name.Should().Be("fresh");
    }

    [Fact]
    public void A_scope_is_recorded_for_every_row()
    {
        var view = new ManagerView(
            [Pack("house"), Server("shared", "every project"), Server("local"), Skill("review")],
            []);

        // "Which scope it applies at" is one of the four facts the decision
        // asked for, and a blank one would read as a bug in the screen.
        view.Items.Should().OnlyContain(item => item.Scope.Length > 0);
    }

    [Fact]
    public void An_installed_server_is_distinguishable_from_a_declared_one()
    {
        var view = new ManagerView([Server("declared"), Server("theirs", "installed")], []);

        // The screen refuses to offer removal for these: the agent's own
        // configuration owns them, and reporting success having changed
        // nothing this owns is worse than not offering it.
        view.OfKind(ManagedKind.Server)
            .Should().Contain(item => item.Scope == "installed");
    }

    [Fact]
    public void A_plugin_listing_is_read_into_what_the_screen_shows()
    {
        var plugins = InstalledPluginReader.Parse("""
            [
              { "id": "claude-mem@thedotmack", "version": "13.15.0",
                "scope": "user", "enabled": false },
              { "id": "rust-analyzer-lsp@claude-plugins-official", "version": "1.0.0",
                "scope": "user", "enabled": true }
            ]
            """);

        plugins.Should().HaveCount(2);
        plugins[0].Enabled.Should().BeFalse();
        plugins[1].Id.Should().Be("rust-analyzer-lsp@claude-plugins-official");
        plugins[1].Enabled.Should().BeTrue();
    }

    [Fact]
    public void A_plugin_field_this_does_not_know_about_is_ignored_rather_than_fatal()
    {
        // The shape is the agent's and can gain properties without warning. A
        // reader that insisted on the whole of it would empty this section the
        // first time one appeared.
        var plugins = InstalledPluginReader.Parse("""
            [{ "id": "a@b", "version": "1", "scope": "user", "enabled": true,
               "somethingAddedLater": { "deep": [1, 2, 3] } }]
            """);

        plugins.Should().ContainSingle().Which.Id.Should().Be("a@b");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"id\": \"an object, not an array\"}")]
    [InlineData("[{\"version\": \"no id\"}]")]
    public void A_listing_that_cannot_be_read_is_no_plugins_rather_than_a_failure(string output)
    {
        // No agent installed, one too old to know the command, or output that
        // is not what was expected. Each is a blank section, because the rest
        // of the screen is still worth showing.
        InstalledPluginReader.Parse(output).Should().BeEmpty();
    }

    [Fact]
    public void A_plugin_with_no_scope_is_taken_as_the_user_s()
    {
        InstalledPluginReader.Parse("""[{ "id": "a@b", "version": "1", "enabled": true }]""")
            .Should().ContainSingle().Which.Scope.Should().Be("user");
    }

    [Fact]
    public void Enabled_is_only_true_when_it_is_actually_true()
    {
        // Absent, and the string "true", are both not-enabled. Reading either
        // as on would report a disabled plugin as loaded.
        InstalledPluginReader.Parse("""[{ "id": "a@b", "version": "1" }]""")
            .Single().Enabled.Should().BeFalse();

        InstalledPluginReader.Parse("""[{ "id": "a@b", "enabled": "true" }]""")
            .Single().Enabled.Should().BeFalse();
    }

    [Fact]
    public void Notes_carry_what_is_true_of_the_whole_rather_than_one_row()
    {
        var view = new ManagerView([Server("a"), Server("b")], ["'a' is declared twice"]);

        view.Notes.Should().ContainSingle();
    }
}
