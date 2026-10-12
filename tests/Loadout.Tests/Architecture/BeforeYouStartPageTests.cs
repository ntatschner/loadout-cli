using FluentAssertions;
using Loadout.Core.Starters;
using Loadout.Models.Platform;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// docs/plain/before-you-start.md against the table <c>loadout start</c> prints
/// from, so the page and the guide cannot tell somebody different things.
/// </summary>
public sealed class BeforeYouStartPageTests
{
    [Fact]
    public void Every_command_the_guide_prints_is_on_the_page()
    {
        var page = Page();

        foreach (var prerequisite in Prerequisites.All)
        {
            foreach (var os in Enum.GetValues<HostOperatingSystem>())
            {
                foreach (var command in prerequisite.CommandsFor(os))
                {
                    page.Should().Contain(command, $"the guide prints it for {prerequisite.Id} on {os}");
                }
            }
        }
    }

    [Fact]
    public void Every_section_the_guide_points_to_is_a_heading_on_the_page()
    {
        var headings = Page()
            .Split('\n')
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line[3..].Trim())
            .ToList();

        foreach (var prerequisite in Prerequisites.All)
        {
            headings.Should().Contain(
                prerequisite.Section,
                $"the guide sends somebody missing {prerequisite.Id} to it by name");
        }
    }

    [Fact]
    public void The_address_the_guide_prints_is_the_page()
    {
        Prerequisites.PageUrl.Should().EndWith(Prerequisites.Page);
    }

    private static string Page()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository has to be findable from the tests");

        return File.ReadAllText(Path.Combine(root!.FullName, Prerequisites.Page)).Replace("\r\n", "\n");
    }
}
