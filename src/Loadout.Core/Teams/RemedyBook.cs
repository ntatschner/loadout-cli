using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Teams;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Loadout.Core.Teams;

/// <summary>A remediation waiting on a person.</summary>
/// <remarks>
/// Read from the runs rather than kept by this book. A run that has stopped and
/// wants somebody already has a queue, and the terminal, <c>team gate</c> and
/// the dashboard all read and answer it; a second one beside it would be a
/// second thing to answer and a second thing to forget.
/// </remarks>
/// <param name="Id">What it is answered by.</param>
/// <param name="Run">The run that stopped.</param>
/// <param name="Node">The node that asked.</param>
/// <param name="Role">What it was playing.</param>
/// <param name="Asked">The question, with the remedy's own details in it.</param>
/// <param name="At">When it asked.</param>
public sealed record RemedyWaiting(
    string Id,
    string Run,
    string Node,
    string Role,
    string Asked,
    DateTimeOffset At);

/// <summary>What a team has worked out how to fix, and what is waiting on a person.</summary>
public interface IRemedyBook
{
    /// <summary>The team's directory, whether or not anything is in it.</summary>
    string DirectoryOf(string team);

    /// <summary>Every remedy a team has registered, by name.</summary>
    OperationResult<IReadOnlyList<Remedy>> All(string team);

    /// <summary>One remedy, or a sentence saying there is no such thing.</summary>
    OperationResult<Remedy> Find(string team, string name);

    /// <summary>The script a remedy names, or a sentence saying it is not there.</summary>
    OperationResult<string> ScriptOf(string team, Remedy remedy);

    /// <summary>Writes a remedy's record back, having changed it.</summary>
    OperationResult Save(string team, Remedy remedy);

    /// <summary>Every remediation waiting on a person, newest last.</summary>
    OperationResult<IReadOnlyList<RemedyWaiting>> Waiting(string team);

    /// <summary>Answers one, the way anything else answers a gate.</summary>
    OperationResult Answer(string id, bool allowed, string? reason);
}

/// <summary>
/// What a team keeps, in the team's own directory.
/// </summary>
/// <remarks>
/// <para>
/// Files rather than a database, in the directory a team's declarations already
/// point at, because the thing being kept is a script somebody may want to read
/// and run by hand. A remedy nobody can open in an editor is a remedy nobody
/// audits.
/// </para>
/// <para>
/// A record beside each script rather than front matter inside it: the scripts
/// are in whatever language suits the problem, and a comment convention that
/// had to work in PowerShell, bash and Python would be three conventions.
/// </para>
/// </remarks>
public sealed class RemedyBook : IRemedyBook
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Writer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private readonly Platform.Abstractions.IPlatformPaths _paths;

    public RemedyBook(Platform.Abstractions.IPlatformPaths paths) => _paths = paths;

    /// <inheritdoc />
    public string DirectoryOf(string team) =>
        Path.Combine(_paths.Paths.State, "teams", "work", Slug(team));

    private string Shelf(string team) => Path.Combine(DirectoryOf(team), "remedies");

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<Remedy>> All(string team)
    {
        var shelf = Shelf(team);

        if (!Directory.Exists(shelf))
        {
            return OperationResult<IReadOnlyList<Remedy>>.Ok([]);
        }

        var found = new List<Remedy>();

        foreach (var file in Directory.EnumerateFiles(shelf, "*.yaml").OrderBy(one => one, StringComparer.Ordinal))
        {
            if (Read(file) is { } remedy)
            {
                found.Add(remedy);
            }
        }

        return OperationResult<IReadOnlyList<Remedy>>.Ok(found);
    }

    /// <inheritdoc />
    public OperationResult<Remedy> Find(string team, string name)
    {
        var read = All(team);

        if (read.Failed)
        {
            return OperationResult<Remedy>.Fail(read.Error!);
        }

        var found = read.Value!.FirstOrDefault(one =>
            string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase));

        return found is null
            ? OperationResult<Remedy>.Fail(
                $"'{team}' has registered no remedy called '{name}'. "
                + "See what it has with: loadout team remedies",
                ExitCode.ProjectNotFound)
            : OperationResult<Remedy>.Ok(found);
    }

    /// <inheritdoc />
    public OperationResult<string> ScriptOf(string team, Remedy remedy)
    {
        ArgumentNullException.ThrowIfNull(remedy);

        if (remedy.Script is not { Length: > 0 })
        {
            return OperationResult<string>.Fail(
                $"'{remedy.Name}' names no script.", ExitCode.InvalidArguments);
        }

        // Under the shelf and nowhere else. A record is written by an agent,
        // and a script path of "../../../something" would otherwise read a
        // file the team was never given.
        var at = Path.GetFullPath(Path.Combine(Shelf(team), remedy.Script));
        var shelf = Path.GetFullPath(Shelf(team));

        if (!at.StartsWith(shelf + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<string>.Fail(
                $"'{remedy.Name}' names a script outside the team's directory, which is refused.",
                ExitCode.InvalidArguments);
        }

        try
        {
            return File.Exists(at)
                ? OperationResult<string>.Ok(File.ReadAllText(at))
                : OperationResult<string>.Fail(
                    $"'{remedy.Name}' names {remedy.Script}, which is not in the team's directory.",
                    ExitCode.ProjectNotFound);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<string>.Fail($"That script could not be read: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public OperationResult Save(string team, Remedy remedy)
    {
        ArgumentNullException.ThrowIfNull(remedy);

        try
        {
            Directory.CreateDirectory(Shelf(team));
            File.WriteAllText(Path.Combine(Shelf(team), Slug(remedy.Name) + ".yaml"), Writer.Serialize(remedy));

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"That remedy could not be written: {ex.Message}");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Across every run on this machine rather than a named one, because
    /// somebody asking what is waiting does not know which run stopped. Only
    /// the ones about a remedy: a role's ordinary permission question is
    /// answered where every other permission question is.
    /// </remarks>
    public OperationResult<IReadOnlyList<RemedyWaiting>> Waiting(string team)
    {
        var runs = Path.Combine(_paths.Paths.State, "teams", "runs");

        if (!Directory.Exists(runs))
        {
            return OperationResult<IReadOnlyList<RemedyWaiting>>.Ok([]);
        }

        var waiting = new List<RemedyWaiting>();

        foreach (var run in Directory.EnumerateDirectories(runs))
        {
            foreach (var ask in NodePermissions.Pending(run))
            {
                if (!string.Equals(ask.Kind, "remedy", StringComparison.Ordinal))
                {
                    continue;
                }

                waiting.Add(new RemedyWaiting(
                    ask.Id,
                    Path.GetFileName(run),
                    ask.Node,
                    ask.Role,
                    ask.Question,
                    ask.At));
            }
        }

        return OperationResult<IReadOnlyList<RemedyWaiting>>.Ok(
            [.. waiting.OrderBy(one => one.At)]);
    }

    /// <inheritdoc />
    public OperationResult Answer(string id, bool allowed, string? reason)
    {
        var runs = Path.Combine(_paths.Paths.State, "teams", "runs");

        if (!Directory.Exists(runs))
        {
            return OperationResult.Fail("Nothing is waiting on this machine.", ExitCode.ProjectNotFound);
        }

        foreach (var run in Directory.EnumerateDirectories(runs))
        {
            if (!NodePermissions.Pending(run).Any(one =>
                string.Equals(one.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // The same answer a gate gets, in the same place the node is
            // already watching for it.
            NodePermissions.AnswerAsync(
                run,
                id,
                new AskAnswer(
                    allowed,
                    reason is { Length: > 0 } said
                        ? said
                        : allowed
                            ? "Agreed to for this call only."
                            : "Refused. Report what you needed and why rather than finding another "
                              + "way to do it."))
                .GetAwaiter().GetResult();

            return OperationResult.Ok();
        }

        return OperationResult.Fail(
            $"Nothing is waiting under '{id}'.", ExitCode.ProjectNotFound);
    }

    private static Remedy? Read(string file)
    {
        try
        {
            var remedy = Yaml.Deserialize<Remedy>(File.ReadAllText(file));

            // A record with no name is a record nothing can ask for. Named
            // after its file rather than dropped, because a file somebody wrote
            // by hand and forgot to name is a thing to show them.
            if (remedy is not null && remedy.Name is not { Length: > 0 })
            {
                remedy.Name = Path.GetFileNameWithoutExtension(file);
            }

            return remedy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    /// <summary>A name as a directory or file name, with nothing in it that can climb.</summary>
    internal static string Slug(string name)
    {
        var clean = new string([.. (name ?? string.Empty).Select(one =>
            char.IsAsciiLetterOrDigit(one) || one == '-' ? char.ToLowerInvariant(one) : '-')]);

        return clean.Trim('-') is { Length: > 0 } named ? named : "unnamed";
    }
}
