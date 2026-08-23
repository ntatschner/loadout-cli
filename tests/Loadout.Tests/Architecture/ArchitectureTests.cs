using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// Structural rules that keep the cross-platform contract of spec section 5
/// true after everyone has forgotten it was a rule.
/// <para>
/// Both checks guard mistakes that are easy to make and expensive to discover:
/// a helpful using directive that pulls a Linux implementation into shared
/// code, and an OS-suffixed target framework copied from an existing project.
/// Neither shows up on a Windows developer's machine; both break the Linux and
/// macOS builds outright.
/// </para>
/// </summary>
public sealed class ArchitectureTests
{
    /// <summary>Namespaces holding implementations that only exist on one platform.</summary>
    private static readonly string[] PlatformSpecificNamespaces =
    [
        "Loadout.Platform.Windows",
        "Loadout.Platform.Linux",
        "Loadout.Platform.MacOS",
        "Loadout.Platform.Unix",
    ];

    /// <summary>
    /// Assemblies that must stay platform-neutral. The selector in
    /// Loadout.Platform and the composition root in the CLI are the only
    /// places allowed to name a concrete platform.
    /// </summary>
    public static TheoryData<string> NeutralAssemblies =>
    [
        PathOf<Loadout.Models.ExitCode>(),
        PathOf(typeof(Loadout.Core.ServiceRegistration)),
        PathOf<Loadout.Agents.IAgentAdapter>(),
        PathOf<Loadout.Tui.LauncherTui>(),
    ];

    public static TheoryData<string> AllProductAssemblies =>
    [
        PathOf<Loadout.Models.ExitCode>(),
        PathOf(typeof(Loadout.Platform.PlatformServices)),
        PathOf(typeof(Loadout.Core.ServiceRegistration)),
        PathOf<Loadout.Agents.IAgentAdapter>(),
        PathOf<Loadout.Tui.LauncherTui>(),
        PathOf(typeof(Loadout.Cli.Program)),
    ];

    /// <summary>
    /// Locates an assembly beside the test binary rather than through
    /// Assembly.Location, which is empty in a single-file host and which the
    /// single-file analyser rejects outright.
    /// </summary>
    private static string PathOf<T>() => PathOf(typeof(T));

    private static string PathOf(Type type) =>
        Path.Combine(AppContext.BaseDirectory, type.Assembly.GetName().Name + ".dll");

    [Theory]
    [MemberData(nameof(NeutralAssemblies))]
    public void Shared_assemblies_never_reference_a_platform_implementation(string assemblyPath)
    {
        var referenced = ReadReferencedNamespaces(assemblyPath);

        var violations = referenced
            .Where(ns => PlatformSpecificNamespaces.Any(
                forbidden => ns.StartsWith(forbidden, StringComparison.Ordinal)))
            .ToList();

        violations.Should().BeEmpty(
            "shared code must depend on Loadout.Platform.Abstractions only, so that adding a "
            + "platform never requires editing it, and so a Windows-only type cannot reach the Linux "
            + "or macOS build");
    }

    [Theory]
    [MemberData(nameof(AllProductAssemblies))]
    public void No_assembly_targets_an_os_specific_framework(string assemblyPath)
    {
        File.Exists(assemblyPath).Should().BeTrue($"'{assemblyPath}' should sit beside the test binary");

        var framework = ReadTargetFramework(assemblyPath);

        framework.Should().NotBeNull();

        // net10.0-windows would compile happily here and fail on every other
        // platform, which is exactly the class of mistake this catches.
        framework.Should().NotContain("-windows");
        framework.Should().NotContain("-macos");
        framework.Should().NotContain("-ios");
        framework.Should().NotContain("-android");
    }

    /// <summary>
    /// Reads the TargetFrameworkAttribute straight out of metadata rather than
    /// loading the assembly, so the check works for any assembly on disk and
    /// has no side effects on the test process.
    /// </summary>
    private static string? ReadTargetFramework(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var metadata = peReader.GetMetadataReader();

        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);

            if (attribute.Constructor.Kind != HandleKind.MemberReference)
            {
                continue;
            }

            var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);

            if (constructor.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var declaringType = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);

            if (metadata.GetString(declaringType.Name) != nameof(TargetFrameworkAttribute))
            {
                continue;
            }

            // The single string argument of the attribute blob, after the
            // two-byte prolog.
            var blob = metadata.GetBlobReader(attribute.Value);
            blob.ReadUInt16();

            return blob.ReadSerializedString();
        }

        return null;
    }

    /// <summary>
    /// Reads the assembly's type-reference table, which lists every external
    /// type it actually uses. This catches uses inside method bodies, which a
    /// scan of using directives or public signatures would miss.
    /// </summary>
    private static ImmutableHashSet<string> ReadReferencedNamespaces(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        var metadata = peReader.GetMetadataReader();
        var namespaces = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);

        foreach (var handle in metadata.TypeReferences)
        {
            var typeReference = metadata.GetTypeReference(handle);
            var name = metadata.GetString(typeReference.Namespace);

            if (!string.IsNullOrEmpty(name))
            {
                namespaces.Add(name);
            }
        }

        return namespaces.ToImmutable();
    }
}
