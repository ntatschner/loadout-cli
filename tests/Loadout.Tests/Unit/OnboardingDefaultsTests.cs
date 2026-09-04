using FluentAssertions;
using Loadout.Core.Projects;
using Loadout.Models.Configuration;
using Loadout.Models.Projects;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a newly registered project is given, and what it is never given.
/// </summary>
/// <remarks>
/// These are the questions registering a project currently makes somebody
/// answer later, usually after something has surprised them. Nothing here is
/// new behaviour — it is the same settings, filled in when the project exists
/// rather than the third time they matter.
/// </remarks>
public sealed class OnboardingDefaultsTests
{
    private static ProjectRegistryEntry Entry(string agent = "claude", string editorProfile = "") =>
        new() { Slug = "demo", Name = "Demo", DefaultAgent = agent, EditorProfile = editorProfile };

    private static ProjectManifest Manifest() => new() { Slug = "demo", Name = "Demo" };

    private static OnboardingSettings Settings(
        string agent = "",
        string model = "",
        string editorProfile = "",
        params (string Mode, string Model)[] byMode)
    {
        var settings = new OnboardingSettings
        {
            Agent = agent,
            Model = model,
            EditorProfile = editorProfile,
        };

        foreach (var (mode, name) in byMode)
        {
            settings.ModelByMode[mode] = name;
        }

        return settings;
    }

    [Fact]
    public void Nothing_configured_fills_nothing_in()
    {
        var entry = Entry();

        OnboardingDefaults.Apply(entry, Manifest(), new OnboardingSettings()).Should().BeEmpty();
        OnboardingDefaults.Apply(entry, Manifest(), null).Should().BeEmpty();

        entry.DefaultAgent.Should().Be("claude");
    }

    [Fact]
    public void The_configured_agent_replaces_the_built_in_default()
    {
        var entry = Entry();

        var applied = OnboardingDefaults.Apply(entry, Manifest(), Settings(agent: "codex"));

        // "claude" is the built-in default rather than a choice anybody made,
        // so a configured preference is allowed to replace it.
        entry.DefaultAgent.Should().Be("codex");
        applied.Should().ContainSingle().Which.Value.Should().Be("codex");
    }

    [Fact]
    public void An_agent_somebody_chose_is_left_alone()
    {
        var entry = Entry(agent: "codex");

        var applied = OnboardingDefaults.Apply(entry, Manifest(), Settings(agent: "claude"));

        // A project naming its own agent said so deliberately, and a
        // machine-wide preference is not grounds to reconsider it.
        entry.DefaultAgent.Should().Be("codex");
        applied.Should().BeEmpty();
    }

    [Fact]
    public void An_editor_profile_already_set_is_left_alone()
    {
        var entry = Entry(editorProfile: "Chosen");

        OnboardingDefaults.Apply(entry, Manifest(), Settings(editorProfile: "Default"))
            .Should().BeEmpty();

        entry.EditorProfile.Should().Be("Chosen");
    }

    [Fact]
    public void Models_are_filled_in_per_mode()
    {
        var manifest = Manifest();

        var applied = OnboardingDefaults.Apply(
            Entry(),
            manifest,
            Settings(model: "big", byMode: [("review", "small")]));

        manifest.Agents.Model.Should().Be("big");
        manifest.Agents.ModelByMode["review"].Should().Be("small");
        applied.Should().HaveCount(2);
    }

    [Fact]
    public void A_model_the_project_already_names_is_not_replaced()
    {
        var manifest = Manifest();

        manifest.Agents.Model = "chosen";
        manifest.Agents.ModelByMode["review"] = "chosen-for-review";

        OnboardingDefaults.Apply(
            Entry(),
            manifest,
            Settings(model: "big", byMode: [("review", "small")]))
            .Should().BeEmpty();

        manifest.Agents.Model.Should().Be("chosen");
        manifest.Agents.ModelByMode["review"].Should().Be("chosen-for-review");
    }

    [Fact]
    public void A_mode_the_project_does_not_name_is_still_filled_in()
    {
        var manifest = Manifest();

        manifest.Agents.ModelByMode["review"] = "chosen-for-review";

        OnboardingDefaults.Apply(
            Entry(),
            manifest,
            Settings(byMode: [("review", "small"), ("implement", "big")]));

        // Filling blanks, per setting, not all-or-nothing per section.
        manifest.Agents.ModelByMode["review"].Should().Be("chosen-for-review");
        manifest.Agents.ModelByMode["implement"].Should().Be("big");
    }

    [Fact]
    public void A_project_with_no_manifest_still_gets_what_the_registration_holds()
    {
        var entry = Entry();

        var applied = OnboardingDefaults.Apply(
            entry, null, Settings(agent: "codex", model: "big", editorProfile: "Agents"));

        // No manifest is an ordinary state — the workspace may be another
        // machine's. What lives on the registration is still filled in.
        entry.DefaultAgent.Should().Be("codex");
        entry.EditorProfile.Should().Be("Agents");
        applied.Select(choice => choice.Setting).Should().NotContain("model");
    }

    [Fact]
    public void Everything_filled_in_is_reported()
    {
        var applied = OnboardingDefaults.Apply(
            Entry(),
            Manifest(),
            Settings(agent: "codex", model: "big", editorProfile: "Agents",
                byMode: [("review", "small")]));

        // A setting that arrives without being mentioned is one somebody later
        // finds and cannot account for.
        applied.Select(choice => choice.Setting).Should().BeEquivalentTo(
            ["agent", "editor profile", "model", "model for review"]);
    }
}
