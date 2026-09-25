using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// Every place that names the SDK names the one global.json pins.
/// </summary>
/// <remarks>
/// <para>
/// global.json pins the SDK exactly with rollForward off, because the SDK
/// chooses the version of implicit packages and the lock files only hold if
/// every machine uses the same one. That makes any other copy of the version a
/// build that refuses to start the day it drifts.
/// </para>
/// <para>
/// The Linux verify image did exactly that: it took the floating
/// <c>sdk:10.0</c> tag, which moved to 10.0.401 while the pin sat at 10.0.303,
/// and a version with no published image at that. The container could not
/// restore until somebody relaxed global.json by hand.
/// </para>
/// </remarks>
public sealed class SdkPinTests
{
    private static readonly Regex Image = new(
        @"^ARG\s+SDK_IMAGE=mcr\.microsoft\.com/dotnet/sdk:(\S+)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex Documented = new(
        @"SDK\W{0,4}(\d+\.\d+\.\d+)",
        RegexOptions.Compiled);

    [Fact]
    public void The_verify_image_uses_the_pinned_sdk()
    {
        var root = Repository();

        var dockerfile = File.ReadAllText(Path.Combine(root, "build", "docker", "Dockerfile"));

        var image = Image.Match(dockerfile);

        image.Success.Should().BeTrue("the Dockerfile has to name its SDK image");

        image.Groups[1].Value.Should().Be(Pinned(root),
            "rollForward is off, so an image with any other SDK cannot build the repository");
    }

    [Fact]
    public void The_build_instructions_name_the_pinned_sdk()
    {
        var root = Repository();

        var pinned = Pinned(root);

        var named = new[] { "README.md", "CONTRIBUTING.md" }
            .SelectMany(page => Documented.Matches(File.ReadAllText(Path.Combine(root, page)))
                .Select(match => (Page: page, Version: match.Groups[1].Value)))
            .ToList();

        named.Should().NotBeEmpty("the build instructions have to say which SDK to install");

        foreach (var (page, version) in named)
        {
            version.Should().Be(pinned, $"{page} tells a contributor which SDK to install");
        }
    }

    private static string Pinned(string root)
    {
        using var global = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "global.json")));

        return global.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
    }

    private static string Repository()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository has to be findable from the tests");

        return root!.FullName;
    }
}
