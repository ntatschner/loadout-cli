using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether the onboarding procedure accounts for every setting a project.yaml
/// can carry.
/// </summary>
/// <remarks>
/// The onboarding session is the one that proposes a project's settings, and it
/// proposes only what its procedure tells it about. The first version named four
/// sections, so models, modes, coding styles and context files were never
/// considered, and the settings nothing reads were not marked as such. Read from
/// the manifest as it is written, so a setting added later fails here until the
/// procedure says what to do with it.
/// </remarks>
public sealed class ProjectOnboardingSkillTests
{
    [Fact]
    public async Task The_onboarding_procedure_names_every_setting_in_project_yaml()
    {
        var skill = (await new SpecialistLibrary().LoadAsync(workspaceRoot: null))
            .Find(ProjectOnboardingTask.Skill);

        skill.Should().NotBeNull("the onboarding session is given this skill by name");

        var body = skill!.Body;
        var missing = new List<string>();

        foreach (var (section, children) in Sections())
        {
            // A section the procedure deals with whole, such as `symbols`, covers
            // its children. Otherwise each child has to be named on its own,
            // because a section treated child by child is one where a new child
            // would otherwise go unmentioned.
            if (body.Contains($"`{section}`", StringComparison.Ordinal))
            {
                continue;
            }

            if (children.Count == 0)
            {
                missing.Add(section);

                continue;
            }

            missing.AddRange(children
                .Select(child => $"{section}.{child}")
                .Where(path => !body.Contains($"`{path}`", StringComparison.Ordinal)));
        }

        missing.Should().BeEmpty("every setting is worth proposing, refused, or read by nothing yet");
    }

    /// <summary>
    /// The top-level keys of a project.yaml as the launcher writes it, each with
    /// the keys directly under it.
    /// </summary>
    private static List<(string Section, List<string> Children)> Sections()
    {
        var yaml = new YamlStore(new NoOpFilePermissions()).Render(new ProjectManifest());
        var sections = new List<(string Section, List<string> Children)>();

        foreach (var line in yaml.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.Length == 0 || line.TrimStart().StartsWith('-'))
            {
                continue;
            }

            var indent = line.Length - line.TrimStart().Length;
            var key = line.Trim().Split(':')[0];

            if (indent == 0)
            {
                sections.Add((key, []));
            }
            else if (indent == 2)
            {
                sections[^1].Children.Add(key);
            }
        }

        return sections;
    }
}
