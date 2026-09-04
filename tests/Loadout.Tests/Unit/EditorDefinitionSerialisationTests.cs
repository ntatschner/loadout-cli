using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Models.Configuration;
using Loadout.Models.Editors;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What ends up in somebody's config.yaml after the launcher writes it back.
/// </summary>
/// <remarks>
/// Every <c>loadout config set</c> rewrites the whole file, so anything the
/// serialiser is willing to emit becomes a line somebody reads and reasonably
/// assumes is a setting. A derived value written that way is worse than
/// untidy: it invites being edited, and editing it does nothing.
/// </remarks>
public sealed class EditorDefinitionSerialisationTests
{
    [Fact]
    public async Task Writing_the_config_back_does_not_invent_settings_nobody_can_change()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);

        try
        {
            var path = Path.Combine(directory, "config.yaml");
            var store = new YamlStore(new NoOpFilePermissions());

            var config = new LauncherConfig();
            config.CustomEditors["helix"] = new EditorDefinition
            {
                Executable = "hx",
                Arguments = ["${DIRECTORY}"],
                ProfileEnvironment = "HELIX_RUNTIME",
            };

            (await store.SaveAsync(path, config)).Succeeded.Should().BeTrue();

            var written = await File.ReadAllTextAsync(path);

            // Whether an editor can be told a profile is worked out from what
            // was declared. Writing it back as though it were declared is how a
            // read-only fact becomes a setting somebody tries to turn on.
            written.Should().NotContain("can_open_a_profile");

            // The things that genuinely are settings still survive the trip.
            written.Should().Contain("profile_environment: HELIX_RUNTIME");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
