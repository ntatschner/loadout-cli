using System.Collections;
using System.Reflection;
using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Models.Configuration;
using Loadout.Models.Delivery;
using Loadout.Models.Ideas;
using Loadout.Models.Packs;
using Loadout.Models.Projects;
using Loadout.Models.Teams;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What <see cref="YamlStore.LoadAsync{T}"/> parses is kept for the process
/// and handed out as a copy while the file is unchanged, because the daemon
/// asked for the same unchanged files hundreds of times an hour.
/// </summary>
/// <remarks>
/// Whether a file was read is shown by changing what is in it while putting
/// its length and last-write time back: a store that went to the file sees the
/// new text, one that answered from what was kept sees the old.
/// </remarks>
public sealed class KeptYamlTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-keptyaml-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly DateTime _written = DateTime.UtcNow.AddHours(-1);

    public KeptYamlTests()
    {
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "note.yaml");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    /// <summary>A file of the shape the tests read.</summary>
    public sealed class Note
    {
        public string Text { get; set; } = string.Empty;

        public List<string> Tags { get; set; } = [];

        public Dictionary<string, string> Names { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>One that holds something the copier does not know how to copy.</summary>
    public sealed class Loose
    {
        public string Text { get; set; } = string.Empty;

        public object? Anything { get; set; }
    }

    private static YamlStore Store() => new(new NoOpFilePermissions());

    /// <summary>Writes the text and sets the file's time back an hour, so its stamp is settled and the same each time.</summary>
    private void Write(string text)
    {
        File.WriteAllText(_path, text);
        File.SetLastWriteTimeUtc(_path, _written);
    }

    [Fact]
    public async Task A_second_store_answers_from_what_the_first_loaded()
    {
        Write("text: alpha\n");
        (await Store().LoadAsync(_path, () => new Note())).Value!.Text.Should().Be("alpha");

        Write("text: omega\n");

        (await Store().LoadAsync(_path, () => new Note())).Value!.Text
            .Should().Be("alpha", "the file looks unchanged, so the daemon's next request need not read it");
    }

    [Fact]
    public async Task A_file_that_changed_is_loaded_again()
    {
        Write("text: alpha\n");
        await Store().LoadAsync(_path, () => new Note());

        Write("text: longer\n");

        (await Store().LoadAsync(_path, () => new Note())).Value!.Text.Should().Be("longer");
    }

    [Fact]
    public async Task A_file_written_in_the_last_two_seconds_is_always_loaded_again()
    {
        // Two writes a coarse clock gave the same time, both moments ago: the
        // same length and the same time, and still not the same file.
        var moment = DateTime.UtcNow.AddMilliseconds(-200);

        File.WriteAllText(_path, "text: alpha\n");
        File.SetLastWriteTimeUtc(_path, moment);
        (await Store().LoadAsync(_path, () => new Note())).Value!.Text.Should().Be("alpha");

        File.WriteAllText(_path, "text: omega\n");
        File.SetLastWriteTimeUtc(_path, moment);

        (await Store().LoadAsync(_path, () => new Note())).Value!.Text.Should().Be("omega");
    }

    [Fact]
    public async Task A_caller_changing_what_it_was_given_changes_nothing_kept()
    {
        Write("text: alpha\ntags: [one]\nnames:\n  Key: value\n");

        var first = (await Store().LoadAsync(_path, () => new Note())).Value!;
        first.Text = "changed";
        first.Tags.Add("two");
        first.Names["other"] = "x";

        var second = (await Store().LoadAsync(_path, () => new Note())).Value!;
        second.Text = "changed";
        second.Tags.Add("two");

        var third = (await Store().LoadAsync(_path, () => new Note())).Value!;

        third.Text.Should().Be("alpha");
        third.Tags.Should().Equal("one");
        third.Names.Should().ContainSingle();
    }

    [Fact]
    public async Task A_type_the_copier_cannot_copy_is_loaded_every_time()
    {
        Write("text: alpha\n");
        (await Store().LoadAsync(_path, () => new Loose())).Value!.Text.Should().Be("alpha");

        Write("text: omega\n");

        (await Store().LoadAsync(_path, () => new Loose())).Value!.Text.Should().Be("omega");
    }

    [Fact]
    public async Task A_file_that_is_not_yaml_still_fails_as_it_did()
    {
        Write("text: [unclosed\n");

        var loaded = await Store().LoadAsync(_path, () => new Note());

        loaded.Succeeded.Should().BeFalse();
        loaded.Error.Should().Contain("is not valid YAML at line");
    }

    [Theory]
    [InlineData(typeof(LauncherConfig))]
    [InlineData(typeof(MachineConfig))]
    [InlineData(typeof(ProjectManifest))]
    [InlineData(typeof(ProjectRegistry))]
    [InlineData(typeof(TeamQueue))]
    [InlineData(typeof(TeamScheduleList))]
    [InlineData(typeof(IdeaDump))]
    [InlineData(typeof(ProjectLandings))]
    [InlineData(typeof(PackApprovalFile))]
    public void The_files_the_daemon_kept_reading_are_ones_the_copier_can_copy(Type type)
    {
        ModelCopy.Supports(type).Should().BeTrue($"{type.Name} is loaded hundreds of times an hour, and a type the copier refuses is read every time");
    }

    [Fact]
    public void A_copy_keeps_a_dictionarys_comparer()
    {
        var note = new Note();
        note.Names["Build-Fix"] = "auto";

        ModelCopy.Of(note).Names.Should().ContainKey("build-fix");
    }

    /// <summary>
    /// Every model the copier accepts, filled in completely, comes back equal
    /// and sharing nothing that can be changed. A property of a kind the copier
    /// mishandles shows as a difference or a shared reference.
    /// </summary>
    [Fact]
    public void Every_model_the_copier_accepts_is_copied_whole_and_shares_nothing()
    {
        var models = typeof(LauncherConfig).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsPublic: true } && !type.IsGenericTypeDefinition)
            .Where(ModelCopy.Supports)
            .ToList();

        models.Should().HaveCountGreaterThan(20, "most models are plain classes the copier can copy");

        foreach (var type in models)
        {
            var value = Filled(type, type.Name, depth: 0)!;
            var copy = typeof(ModelCopy).GetMethod(nameof(ModelCopy.Of))!.MakeGenericMethod(type).Invoke(null, [value])!;

            SameAndSharesNothing(value, copy, type.Name);
        }
    }

    /// <summary>
    /// Walks what the copier copies, the writable properties all the way down,
    /// and checks each value is equal and nothing that can change is shared.
    /// Computed properties are left out: they follow from the rest.
    /// </summary>
    private static void SameAndSharesNothing(object original, object? copy, string where)
    {
        copy.Should().NotBeNull(where);

        var type = original.GetType();
        copy!.GetType().Should().Be(type, where);

        if (IsUnchangeable(type))
        {
            copy.Should().Be(original, where);
            return;
        }

        ReferenceEquals(original, copy).Should().BeFalse($"{where} is shared between the original and the copy");

        switch (original)
        {
            case IDictionary dictionary:
                ((IDictionary)copy).Count.Should().Be(dictionary.Count, where);

                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Value is not null)
                    {
                        SameAndSharesNothing(entry.Value, ((IDictionary)copy)[entry.Key], where + "[" + entry.Key + "]");
                    }
                }

                return;
            case IList list:
                ((IList)copy).Count.Should().Be(list.Count, where);

                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i] is not null)
                    {
                        SameAndSharesNothing(list[i]!, ((IList)copy)[i], where + "[" + i + "]");
                    }
                }

                return;
        }

        foreach (var property in Writable(type))
        {
            if (property.GetValue(original) is { } value)
            {
                SameAndSharesNothing(value, property.GetValue(copy), where + "." + property.Name);
            }
            else
            {
                property.GetValue(copy).Should().BeNull(where + "." + property.Name);
            }
        }
    }

    private static bool IsUnchangeable(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
        || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
        || type == typeof(TimeOnly) || type == typeof(DateOnly) || type == typeof(Guid) || type == typeof(Uri);

    private static IEnumerable<PropertyInfo> Writable(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0
                && property.GetMethod is { IsPublic: true }
                && property.SetMethod is { IsPublic: true });

    /// <summary>
    /// A value of <paramref name="type"/> with every property set to something
    /// other than its default, down to a few levels for types that hold
    /// themselves.
    /// </summary>
    private static object? Filled(Type type, string name, int depth)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Filled(underlying, name, depth);
        }

        if (type == typeof(string))
        {
            return "set-" + name;
        }

        if (type == typeof(bool))
        {
            return true;
        }

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.GetValue(values.Length - 1);
        }

        if (type.IsPrimitive || type == typeof(decimal))
        {
            return Convert.ChangeType(7, type, System.Globalization.CultureInfo.InvariantCulture);
        }

        if (type == typeof(DateTime))
        {
            return DateTime.UnixEpoch.AddDays(3);
        }

        if (type == typeof(DateTimeOffset))
        {
            return DateTimeOffset.UnixEpoch.AddDays(3);
        }

        if (type == typeof(TimeSpan))
        {
            return TimeSpan.FromMinutes(7);
        }

        if (type == typeof(TimeOnly))
        {
            return new TimeOnly(7, 30);
        }

        if (type == typeof(DateOnly))
        {
            return new DateOnly(2026, 10, 29);
        }

        if (type == typeof(Guid))
        {
            return Guid.Parse("2b7c1d64-0000-4000-8000-000000000007");
        }

        if (type == typeof(Uri))
        {
            return new Uri("https://example.invalid/" + name);
        }

        if (depth > 3)
        {
            return null;
        }

        if (type.IsArray)
        {
            var array = Array.CreateInstance(type.GetElementType()!, 1);
            array.SetValue(Filled(type.GetElementType()!, name, depth + 1), 0);
            return array;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = (IList)Activator.CreateInstance(type)!;
            list.Add(Filled(type.GetGenericArguments()[0], name, depth + 1));
            return list;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var dictionary = (IDictionary)Activator.CreateInstance(type)!;
            dictionary.Add(Filled(type.GetGenericArguments()[0], "key-" + name, depth + 1)!, Filled(type.GetGenericArguments()[1], name, depth + 1));
            return dictionary;
        }

        var value = Activator.CreateInstance(type, nonPublic: true)!;

        foreach (var property in Writable(type))
        {
            property.SetValue(value, Filled(property.PropertyType, property.Name, depth + 1));
        }

        return value;
    }
}
