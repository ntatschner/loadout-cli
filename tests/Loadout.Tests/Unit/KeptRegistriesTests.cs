using System.Collections;
using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Core.Workspace;
using Loadout.Models.Configuration;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The machine's settings and the project registry are kept parsed between
/// reads, as ideas and task lists are, because the daemon read each of them
/// hundreds of times for every time it changed.
/// </summary>
/// <remarks>
/// Whether a file was read is shown by changing what is in it while putting
/// its length and last-write time back: a reader that went to the file sees
/// the new text, one that answered from what was kept sees the old.
/// </remarks>
public sealed class KeptRegistriesTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();
    private readonly DateTime _written = DateTime.UtcNow.AddHours(-1);

    public void Dispose() => _space.Dispose();

    private string MachinesFile => _space.Paths.Paths.MachinesFile;

    private string RegistryFile => Path.Combine(_space.Workspace.LocalPath, "registry", "projects.yaml");

    private ConfigurationService Configuration() =>
        new(_space.Paths, new FakeEnvironmentProvider(_space.Scratch("home")), _space.Yaml);

    private WorkspaceManager Workspace() =>
        new(_space.Paths, new FakeGit(_space.Workspace.LocalPath), _space.Yaml, TimeProvider.System);

    /// <summary>Writes the text and sets the file's time back an hour, so its stamp is settled and the same each time.</summary>
    private void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, _written);
    }

    [Fact]
    public async Task A_second_service_answers_the_machine_from_what_the_first_read()
    {
        Write(MachinesFile, "machine_name: alpha\n");
        (await Configuration().LoadMachineAsync()).Value!.MachineName.Should().Be("alpha");

        Write(MachinesFile, "machine_name: omega\n");

        (await Configuration().LoadMachineAsync()).Value!.MachineName
            .Should().Be("alpha", "the file looks unchanged, so the daemon's next request need not read it");
    }

    [Fact]
    public async Task The_machine_is_read_again_once_the_file_changes()
    {
        Write(MachinesFile, "machine_name: alpha\n");
        await Configuration().LoadMachineAsync();

        Write(MachinesFile, "machine_name: longer\n");

        (await Configuration().LoadMachineAsync()).Value!.MachineName.Should().Be("longer");
    }

    [Fact]
    public async Task A_caller_changing_the_machine_it_was_given_changes_nothing_kept()
    {
        Write(MachinesFile, "machine_name: alpha\n");

        (await Configuration().LoadMachineAsync()).Value!.MachineName = "changed by a caller";
        (await Configuration().LoadMachineAsync()).Value!.MachineName = "changed by a caller";

        (await Configuration().LoadMachineAsync()).Value!.MachineName.Should().Be("alpha");
    }

    [Fact]
    public async Task A_second_manager_answers_the_registry_from_what_the_first_read()
    {
        Write(RegistryFile, "projects:\n- slug: alpha\n");
        (await Workspace().ReadRegistryAsync()).Value!.Projects.Single().Slug.Should().Be("alpha");

        Write(RegistryFile, "projects:\n- slug: omega\n");

        (await Workspace().ReadRegistryAsync()).Value!.Projects.Single().Slug.Should().Be("alpha");
    }

    [Fact]
    public async Task A_caller_changing_the_registry_it_was_given_changes_nothing_kept()
    {
        Write(RegistryFile, "projects:\n- slug: alpha\n");

        (await Workspace().ReadRegistryAsync()).Value!.Projects.Clear();
        (await Workspace().ReadRegistryAsync()).Value!.Projects.Clear();

        (await Workspace().ReadRegistryAsync()).Value!.Projects.Should().ContainSingle();
    }

    [Fact]
    public async Task A_registry_that_is_not_yaml_still_fails_as_it_did()
    {
        Write(RegistryFile, "projects: [unclosed\n");

        var read = await Workspace().ReadRegistryAsync();

        read.Succeeded.Should().BeFalse();
        read.Error.Should().Contain("is not valid YAML at line");
    }

    [Fact]
    public void A_copy_of_the_machine_carries_every_property_it_has()
    {
        var machine = Filled<MachineConfig>();

        var copy = ConfigurationService.Copy(machine);

        copy.Should().BeEquivalentTo(machine);
        copy.Projects.Should().NotBeSameAs(machine.Projects);
        copy.Projects.Values.Single().Should().NotBeSameAs(machine.Projects.Values.Single());
        copy.DiscoveryRoots.Should().NotBeSameAs(machine.DiscoveryRoots);
        copy.Teams.Should().NotBeSameAs(machine.Teams);
        copy.Teams.TrustedRemedies[0].Should().NotBeSameAs(machine.Teams.TrustedRemedies[0]);
        copy.Teams.TrustedTools[0].Should().NotBeSameAs(machine.Teams.TrustedTools[0]);
        copy.Teams.Remediation.Should().NotBeSameAs(machine.Teams.Remediation);
    }

    [Fact]
    public void A_copy_of_the_machine_still_finds_a_remedy_in_any_case()
    {
        var machine = new MachineConfig();
        machine.Teams.Remediation["Build-Fix"] = "auto";

        ConfigurationService.Copy(machine).Teams.Remediation.Should().ContainKey("build-fix");
    }

    [Fact]
    public void A_copy_of_the_registry_carries_every_property_it_has()
    {
        var registry = Filled<ProjectRegistry>();

        var copy = WorkspaceManager.Copy(registry);

        copy.Should().BeEquivalentTo(registry);
        copy.Projects.Should().NotBeSameAs(registry.Projects);
        copy.Projects[0].Should().NotBeSameAs(registry.Projects[0]);
        copy.Projects[0].Aliases.Should().NotBeSameAs(registry.Projects[0].Aliases);
    }

    /// <summary>
    /// One of <typeparamref name="T"/> with every property set to something
    /// other than its default, so a property a copy leaves out shows as a
    /// difference. A property of a kind this does not know fails the test
    /// rather than being skipped.
    /// </summary>
    private static T Filled<T>() => (T)Filled(typeof(T), typeof(T).Name);

    private static object Filled(Type type, string name)
    {
        if (type == typeof(string))
        {
            return "set-" + name;
        }

        if (type == typeof(int) || type == typeof(int?))
        {
            return 7;
        }

        if (type == typeof(bool))
        {
            return true;
        }

        if (type == typeof(DateTimeOffset) || type == typeof(DateTimeOffset?))
        {
            return DateTimeOffset.UnixEpoch.AddDays(3);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = (IList)Activator.CreateInstance(type)!;
            list.Add(Filled(type.GetGenericArguments()[0], name));
            return list;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var dictionary = (IDictionary)Activator.CreateInstance(type)!;
            dictionary.Add("key-" + name, Filled(type.GetGenericArguments()[1], name));
            return dictionary;
        }

        if (type.IsClass && type.GetConstructor(Type.EmptyTypes) is not null && type.Namespace?.StartsWith("Loadout.Models", StringComparison.Ordinal) == true)
        {
            var value = Activator.CreateInstance(type)!;

            foreach (var property in type.GetProperties().Where(property => property.CanWrite))
            {
                property.SetValue(value, Filled(property.PropertyType, property.Name));
            }

            return value;
        }

        throw new InvalidOperationException($"{name} is a {type.Name}, which this test does not know how to set.");
    }
}
