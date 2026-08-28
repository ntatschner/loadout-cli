using FluentAssertions;
using Loadout.Platform.Linux;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The application icon travels inside the assembly.
/// </summary>
/// <remarks>
/// The Linux desktop entry names an icon rather than pointing at a path, so the
/// icon has to be written into the hicolor theme when the entry is installed —
/// and the archive ships one executable and nothing else, so it can only come
/// from the assembly itself.
///
/// The failure this guards is silent in both directions: a resource that is not
/// embedded, or one embedded under a different name than the code asks for,
/// leaves the entry installed with no icon and nothing reported. It would only
/// be noticed by somebody looking at their applications menu.
/// </remarks>
public sealed class EmbeddedIconTests
{
    private const string ResourceName = "Loadout.Platform.loadout.png";

    [Fact]
    public void The_icon_is_embedded_under_the_name_the_code_asks_for()
    {
        var names = typeof(LinuxDesktopIntegration).Assembly.GetManifestResourceNames();

        names.Should().Contain(ResourceName);
    }

    [Fact]
    public void The_embedded_icon_is_a_png_of_a_useful_size()
    {
        using var stream = typeof(LinuxDesktopIntegration).Assembly
            .GetManifestResourceStream(ResourceName);

        stream.Should().NotBeNull();

        var header = new byte[8];
        stream!.ReadExactly(header);

        // The PNG signature. An embedded file of the wrong type would still be
        // written out and still leave the menu entry without an icon.
        header.Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);

        // Big enough to be the 256px artwork rather than a stray small frame.
        stream.Length.Should().BeGreaterThan(4_000);
    }
}
