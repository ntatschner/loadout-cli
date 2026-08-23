using Loadout.Core.Configuration;
using Loadout.Models.Backups;
using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Timestamps are the one field whose corruption is silent. A value that fails
/// to round-trip comes back as the year 1 rather than as an error, so backup
/// sets sort arbitrarily and recent-project ordering has nothing to sort on,
/// with nothing anywhere saying why.
/// </summary>
public sealed class YamlTimestampTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "loadout-yaml-" + Guid.NewGuid().ToString("N"));

    private readonly YamlStore _store = new(new NoOpFilePermissions());

    public YamlTimestampTests() => Directory.CreateDirectory(_root);

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

    [Fact]
    public async Task A_timestamp_survives_a_round_trip()
    {
        var when = new DateTimeOffset(2026, 8, 23, 14, 15, 16, TimeSpan.FromHours(1));
        var path = Path.Combine(_root, "set.yaml");

        await _store.SaveAsync(path, new BackupSet { Id = "a", CreatedUtc = when });

        var loaded = await _store.LoadAsync(path, () => new BackupSet());

        loaded.Value!.CreatedUtc.Should().Be(when);
    }

    [Fact]
    public async Task A_timestamp_is_written_as_one_readable_line()
    {
        var path = Path.Combine(_root, "readable.yaml");

        await _store.SaveAsync(
            path,
            new BackupSet { Id = "a", CreatedUtc = DateTimeOffset.UnixEpoch });

        var text = await File.ReadAllTextAsync(path);

        // These files are hand-edited and reviewed in a pull request. The
        // struct expanded into twenty of its own properties is unreadable, and
        // it is what caused the round-trip to fail in the first place.
        text.Should().Contain("created_utc: 1970-01-01T00:00:00.0000000+00:00");
        text.Should().NotContain("day_of_year");
    }

    [Fact]
    public async Task A_timestamp_written_before_the_converter_existed_is_still_read()
    {
        var path = Path.Combine(_root, "legacy.yaml");

        // Every file in this shape is a backup set somebody could still need to
        // restore, so refusing it would orphan a rollback point.
        await File.WriteAllTextAsync(path, """
schema_version: 1
id: 20260823-024916-196337
operation: migrate
created_utc:
  date_time: 2026-08-23T02:49:16.8587337
  utc_date_time: 2026-08-23T02:49:16.8587337Z
  local_date_time: 2026-08-23T03:49:16.8587337+01:00
  day_of_year: 235
  offset:
    ticks: 36000000000
machine_name: TEST
entries: []
""");

        var loaded = await _store.LoadAsync(path, () => new BackupSet());

        loaded.Succeeded.Should().BeTrue(loaded.Error ?? string.Empty);
        loaded.Value!.Id.Should().Be("20260823-024916-196337");
        loaded.Value.CreatedUtc.Should().Be(
            new DateTimeOffset(2026, 8, 23, 2, 49, 16, TimeSpan.Zero).AddTicks(8587337));
        loaded.Value.MachineName.Should().Be("TEST");
    }

    [Fact]
    public async Task A_value_that_is_not_a_timestamp_is_reported_rather_than_defaulted()
    {
        var path = Path.Combine(_root, "broken.yaml");
        await File.WriteAllTextAsync(path, "id: a\ncreated_utc: last Tuesday\n");

        var loaded = await _store.LoadAsync(path, () => new BackupSet());

        // Defaulting it would reintroduce exactly the silent corruption the
        // converter exists to fix.
        loaded.Failed.Should().BeTrue();
    }
}
