using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

public sealed class ManifestSerialisationTests
{
    private readonly YamlStore _yaml = new(new NoOpFilePermissions());

    [Fact]
    public void A_manifest_carries_only_what_somebody_can_set()
    {
        // is_empty was written into every project.yaml, although it is worked
        // out from the other three and nothing reads it back. It then showed up
        // as a change in every settings proposal touching the specialists.
        var text = _yaml.Render(new ProjectManifest { Slug = "demo" });

        text.Should().NotContain("is_empty");
    }

    [Fact]
    public void A_manifest_written_before_still_loads()
    {
        var parsed = _yaml.Parse<ProjectManifest>(
            "slug: demo\nspecialists:\n  preferred: [platform.windows]\n  excluded: []\n  mode: ''\n  is_empty: false\n");

        parsed.Succeeded.Should().BeTrue(parsed.Error);
        parsed.Value!.Specialists.Preferred.Should().Equal("platform.windows");
    }
}
