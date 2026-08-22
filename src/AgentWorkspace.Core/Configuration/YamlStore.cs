using AgentWorkspace.Models;
using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgentWorkspace.Core.Configuration;

/// <summary>
/// Reads and writes the launcher's YAML files.
/// <para>
/// Snake case is used throughout because that is the convention the spec's own
/// examples use, and the central workspace files are meant to be hand-edited
/// and reviewed in a pull request.
/// </para>
/// </summary>
public sealed class YamlStore
{
    private readonly IFilePermissions _permissions;

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        // A workspace written by a newer launcher may carry keys this version
        // does not know. Ignoring them lets an older client keep working
        // instead of refusing the whole file; genuine incompatibility is
        // signalled by workspace.yaml's schema version instead (section 91).
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .DisableAliases()
        .Build();

    public YamlStore(IFilePermissions permissions) => _permissions = permissions;

    /// <summary>
    /// Loads a YAML file, returning the supplied default when it does not
    /// exist. A missing config file is a first-run condition, not an error.
    /// </summary>
    public async Task<OperationResult<T>> LoadAsync<T>(
        string path,
        Func<T> createDefault,
        CancellationToken ct = default)
        where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                return OperationResult<T>.Ok(createDefault());
            }

            var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text))
            {
                return OperationResult<T>.Ok(createDefault());
            }

            var value = _deserializer.Deserialize<T>(text);

            return value is null
                ? OperationResult<T>.Ok(createDefault())
                : OperationResult<T>.Ok(value);
        }
        catch (YamlException ex)
        {
            // The line and column matter: these files are hand-edited, so the
            // user needs to know where to look.
            return OperationResult<T>.Fail(
                $"'{path}' is not valid YAML at line {ex.Start.Line}, column {ex.Start.Column}: {ex.Message}",
                ExitCode.ConfigurationInvalid);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<T>.Fail(
                $"Could not read '{path}': {ex.Message}",
                ExitCode.ConfigurationInvalid);
        }
    }

    /// <summary>
    /// Writes a YAML file, creating parent directories as needed. Pass
    /// restrictPermissions for files that hold secret references or machine
    /// layout, which then get owner-only permissions (spec section 82).
    /// </summary>
    public async Task<OperationResult> SaveAsync<T>(
        string path,
        T value,
        bool restrictPermissions = true,
        CancellationToken ct = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var text = _serializer.Serialize(value);

            // Written to a temporary file and moved into place so an
            // interrupted write cannot leave a half-serialised config behind.
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, text, ct).ConfigureAwait(false);

            if (restrictPermissions)
            {
                // Applied before the move so the file is never briefly readable
                // by others at its final name.
                _permissions.RestrictToCurrentUser(temporary);
            }

            File.Move(temporary, path, overwrite: true);

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or YamlException)
        {
            return OperationResult.Fail($"Could not write '{path}': {ex.Message}");
        }
    }
}
