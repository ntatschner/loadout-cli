using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Triggers;
using Loadout.Models;
using Loadout.Models.Triggers;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Where an allow-list entry holds: one repository, one owner's, or everywhere.</summary>
public class TriggerScopeSettings : TriggerSettings
{
    [CommandArgument(0, "<LOGIN>")]
    [Description("The GitHub account, by login, such as dependabot[bot].")]
    public string Login { get; init; } = string.Empty;

    [CommandOption("--repository <OWNER/NAME>")]
    [Description("Only on this repository.")]
    public string? Repository { get; init; }

    [CommandOption("--owner <OWNER>")]
    [Description("Only on this owner's repositories.")]
    public string? Owner { get; init; }
}

/// <summary>Trusts a GitHub account that is not a collaborator, such as a bot.</summary>
[Description("Trust a GitHub account that is not a collaborator, such as a bot, so trigger rules treat what it writes as yours. Everywhere, or on one repository or owner.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger allow trust bot collaborator allow-list", Mutates = true)]
public sealed class TriggerAllowCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time,
    IExecutableResolver resolver,
    IProcessLauncher launcher) : AsyncCommand<TriggerAllowCommand.Settings>
{
    public sealed class Settings : TriggerScopeSettings
    {
        [CommandOption("--id <ID>")]
        [Description("The account's numeric GitHub id, which saves looking it up with gh.")]
        public long? Id { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, yaml, permissions, time);


        if (TriggerTrustWords.Scope(settings) is (null, var wrong))
        {
            return output.Fail(wrong!, ExitCode.InvalidArguments);
        }

        // Looked up once, here, so nobody types an id; kept by id because a
        // login can change hands and the id cannot.
        var found = settings.Id is { } given
            ? (Id: given, Login: settings.Login.Trim(), Problem: (string?)null)
            : await LookUpAsync(settings.Login.Trim(), cancellationToken).ConfigureAwait(false);

        if (found.Problem is { } problem)
        {
            return output.Fail(problem, ExitCode.GeneralFailure);
        }

        var entry = new TriggerAllowEntry
        {
            Id = found.Id,
            Login = found.Login,
            Repo = GitHubRepo.Parse(settings.Repository) ?? string.Empty,
            Owner = settings.Owner?.Trim() ?? string.Empty,
            Added = time.GetUtcNow(),
        };

        if (settings.DryRun)
        {
            output.WriteLine($"[dim]Dry run: nothing was changed.[/] {Markup.Escape(entry.Login)} ({entry.Id}) would be "
                + $"trusted {Markup.Escape(TriggerTrustWords.Where(entry))}.");

            return CommandOutput.Success();
        }

        var added = await new TriggerAllowStore(paths.Paths.State, yaml).AddAsync(entry, cancellationToken).ConfigureAwait(false);

        if (added.Failed)
        {
            return output.Fail(added.Error!, added.ExitCode);
        }

        if (output.IsJson)
        {
            output.WriteJson(new { id = entry.Id, login = entry.Login, scope = entry.Scope });

            return CommandOutput.Success();
        }

        output.WriteLine($"{Markup.Escape(entry.Login)} ({entry.Id}) is trusted {Markup.Escape(TriggerTrustWords.Where(entry))}. "
            + "Their next event is trusted; anything already waiting keeps the trust it was decided with.");

        return CommandOutput.Success();
    }

    private async Task<(long Id, string Login, string? Problem)> LookUpAsync(string login, CancellationToken ct)
    {
        if (resolver.Resolve("gh") is not { } gh)
        {
            return (0, login, "The GitHub CLI is not on PATH to look up the account's id. Install gh, or give the id with --id.");
        }

        var ran = await launcher.RunAsync(
            new ProcessRequest(gh, ["api", $"users/{login}"], Environment.CurrentDirectory),
            TimeSpan.FromSeconds(30),
            ct).ConfigureAwait(false);

        if (ran.Failed || !ran.Value!.Succeeded)
        {
            return (0, login, $"GitHub has no account '{login}' that gh could find. Check the login, or give the id with --id.");
        }

        try
        {
            using var document = JsonDocument.Parse(ran.Value.StandardOutput);

            return (
                document.RootElement.GetProperty("id").GetInt64(),
                document.RootElement.GetProperty("login").GetString() ?? login,
                null);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (0, login, "gh answered in a shape Loadout does not read. Give the id with --id.");
        }
    }
}

/// <summary>Stops trusting an account, and withdraws at once what waits on its trust.</summary>
[Description("Stop trusting a GitHub account on the allow-list, and withdraw at once anything waiting on that trust.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger disallow untrust remove allow-list", Mutates = true)]
public sealed class TriggerDisallowCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time) : AsyncCommand<TriggerScopeSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, TriggerScopeSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, yaml, permissions, time);


        if (TriggerTrustWords.Scope(settings) is (null, var wrong))
        {
            return output.Fail(wrong!, ExitCode.InvalidArguments);
        }

        var store = new TriggerAllowStore(paths.Paths.State, yaml);
        var login = settings.Login.Trim();
        var repo = GitHubRepo.Parse(settings.Repository);
        var owner = settings.Owner?.Trim() is { Length: > 0 } named ? named : null;

        if (settings.DryRun)
        {
            var listed = (await store.ListAsync(cancellationToken).ConfigureAwait(false)).Value ?? [];
            var going = listed.Where(one => string.Equals(one.Login, login, StringComparison.OrdinalIgnoreCase)
                && (repo is null || string.Equals(one.Repo, repo, StringComparison.OrdinalIgnoreCase))
                && (owner is null || string.Equals(one.Owner, owner, StringComparison.OrdinalIgnoreCase))).ToList();

            output.WriteLine(going.Count == 0
                ? $"[dim]Dry run: nothing was changed.[/] '{Markup.Escape(login)}' is not on the allow-list like that."
                : $"[dim]Dry run: nothing was changed.[/] {going.Count} entry(s) for {Markup.Escape(login)} would go, "
                  + "and anything waiting on their trust would be withdrawn.");

            return CommandOutput.Success();
        }

        // Off the list first, so no new event is trusted on it while what was
        // already waiting is withdrawn.
        var removed = await store.RemoveAsync(login, repo, owner, cancellationToken).ConfigureAwait(false);

        if (removed.Failed)
        {
            return output.Fail(removed.Error!, removed.ExitCode);
        }

        var id = removed.Value![0].Id;
        var request = Guid.NewGuid().ToString("N");

        return await desk.ChangeAsync(
            new TriggerAsk(request, TriggerVerbs.Distrust, Author: id, Login: login),
            output,
            () =>
            {
                // No daemon, so nothing waits in memory; what the journal
                // holds for later is withdrawn here, the same way it would be.
                var now = time.GetUtcNow();
                var withdrawn = TriggerSpine.Distrusted(desk.Journal.Read(now - TriggerSpine.Remembered), now, id, login, request);

                foreach (var entry in withdrawn)
                {
                    desk.Journal.Append(entry);
                }

                desk.Journal.Append(new TriggerEntry(
                    now, TriggerEntryKinds.Distrusted,
                    Reason: $"{login} ({id.ToString(CultureInfo.InvariantCulture)}) taken off the allow-list; {withdrawn.Count} withdrawn",
                    Request: request));

                return Task.FromResult(new TriggerAnswer(
                    request, true, $"{login} is off the allow-list; {withdrawn.Count} waiting on their trust withdrawn."));
            },
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Lists the allow-list.</summary>
[Description("List the GitHub accounts trusted beyond the collaborators, where each holds, and any renamed since it was added.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger allowed allow-list trusted accounts")]
public sealed class TriggerAllowedCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time) : AsyncCommand<TriggerSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, TriggerSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, yaml, permissions, time);


        var listed = await new TriggerAllowStore(paths.Paths.State, yaml).ListAsync(cancellationToken).ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed.Error!, listed.ExitCode);
        }

        // What the GitHub source last saw each account called: a login that
        // has changed under an entry is somebody to check on.
        var seen = GitHubSource.Logins(paths.Paths.State);

        string? Now(TriggerAllowEntry one) =>
            seen.TryGetValue(one.Id, out var login) && !string.Equals(login, one.Login, StringComparison.OrdinalIgnoreCase)
                ? login
                : null;

        if (output.IsJson)
        {
            output.WriteJson(listed.Value!.Select(one => new
            {
                id = one.Id,
                login = one.Login,
                scope = one.Scope,
                added = one.Added,
                nowCalled = Now(one),
            }));

            return CommandOutput.Success();
        }

        if (listed.Value!.Count == 0)
        {
            output.WriteLine("Nobody is on the allow-list. GitHub's owners, members and collaborators are trusted as they are.");
            output.WriteLine("[dim]Trust a bot or a contributor with:[/] loadout trigger allow <login> [[--repository owner/name]]");

            return CommandOutput.Success();
        }

        foreach (var one in listed.Value!)
        {
            output.WriteLine(Markup.Escape($"{one.Login} ({one.Id}): {one.Scope}, added {one.Added:yyyy-MM-dd}")
                + (Now(one) is { } renamed ? $" [yellow]now called {Markup.Escape(renamed)}[/]" : string.Empty));
        }

        return CommandOutput.Success();
    }
}

/// <summary>Says how each trigger source is doing.</summary>
[Description("Show how each trigger source is doing: which token GitHub is read with, quota left, repositories read less often to save it, and anything stopping a source.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger sources health github token quota")]
public sealed class TriggerSourcesCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time) : AsyncCommand<TriggerSettings>
{
    /// <inheritdoc />
    public override Task<int> ExecuteAsync(CommandContext context, TriggerSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, yaml, permissions, time);


        var github = GitHubSource.ReadHealth(paths.Paths.State);

        if (output.IsJson)
        {
            output.WriteJson(new { github });

            return Task.FromResult(CommandOutput.Success());
        }

        if (github is null)
        {
            output.WriteLine("GitHub has not been read yet: it is read once a rule waits for a GitHub event and the daemon is running.");

            return Task.FromResult(CommandOutput.Success());
        }

        output.WriteLine(Markup.Escape(
            $"GitHub, read with the {github.Credential}"
            + (github.Looked is { } looked ? $", last looked {looked:yyyy-MM-dd HH:mm} UTC" : string.Empty)
            + (github.Remaining is { } left && github.Limit is { } limit ? $"; {left} of {limit} requests left this hour" : string.Empty)
            + (github.Expires is { } expires ? $"; the token expires {expires:yyyy-MM-dd}" : string.Empty)));

        if (github.Problem is { } problem)
        {
            output.WriteLine($"[yellow]{Markup.Escape(problem)}[/]");
        }

        if (GitHubSource.WritesWith(github.Scopes) is { } writes)
        {
            output.WriteLine($"[yellow]The token can write ({Markup.Escape(writes)}); polling only reads, so a read-only one is safer.[/]");
        }

        foreach (var (repo, one) in github.Repos.OrderBy(each => each.Key, StringComparer.OrdinalIgnoreCase))
        {
            output.WriteLine(Markup.Escape(
                $"  {repo}: every {Every(one.Every)}"
                + (one.Slowed ? ", slowed while quota is short" : string.Empty)
                + (one.Last is { } last ? $", last read {last:HH:mm} UTC" : string.Empty)
                + (one.Problem is { } said ? $"; {said}" : string.Empty)));
        }

        return Task.FromResult(CommandOutput.Success());
    }

    private static string Every(TimeSpan every) =>
        every >= TimeSpan.FromMinutes(1) ? $"{every.TotalMinutes:0} min" : $"{every.TotalSeconds:0} s";
}

/// <summary>Keeps a token for the GitHub source alone.</summary>
[Description("Keep a GitHub token for trigger polling alone, in the credential store. A fine-grained, read-only token is best. Read from standard input, or asked for.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger token set github poll credential", Mutates = true)]
public sealed class TriggerTokenSetCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time,
    ISecretProvider secrets) : AsyncCommand<TriggerSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, TriggerSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, yaml, permissions, time);


        string token;

        if (Console.IsInputRedirected)
        {
            token = (await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).Trim();
        }
        else if (settings.AllowsPrompting)
        {
            token = console.Prompt(new TextPrompt<string>("GitHub token for trigger polling:").PromptStyle("red").Secret()).Trim();
        }
        else
        {
            return output.Fail("No token given. Pipe it on standard input when running non-interactively.", ExitCode.InvalidArguments);
        }

        if (token.Length == 0)
        {
            return output.Fail("An empty token was given.", ExitCode.InvalidArguments);
        }

        // Never echoed, a preview least of all: it would put the token in the
        // scrollback of whoever checked first.
        if (settings.DryRun)
        {
            output.WriteLine($"[dim]Dry run: nothing was stored.[/] A token would be kept under {TriggerCredential.Reference}.");

            return CommandOutput.Success();
        }

        var kept = await secrets.SetAsync(TriggerCredential.Reference, token, cancellationToken).ConfigureAwait(false);

        if (kept.Failed)
        {
            return output.Fail(kept.Error!, kept.ExitCode);
        }

        output.WriteLine($"Kept under {TriggerCredential.Reference}. The daemon reads GitHub with it within five minutes; "
            + "'loadout trigger sources' says which token is in use.");

        return CommandOutput.Success();
    }
}

/// <summary>Forgets the GitHub source's own token.</summary>
[Description("Forget the GitHub token kept for trigger polling, after which polling uses the one gh is signed in with.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger token clear github credential", Mutates = true)]
public sealed class TriggerTokenClearCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time,
    ISecretProvider secrets) : AsyncCommand<TriggerSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, TriggerSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, yaml, permissions, time);


        if (settings.DryRun)
        {
            output.WriteLine($"[dim]Dry run: nothing was removed.[/] The token under {TriggerCredential.Reference} would be forgotten.");

            return CommandOutput.Success();
        }

        var gone = await secrets.RemoveAsync(TriggerCredential.Reference, cancellationToken).ConfigureAwait(false);

        if (gone.Failed)
        {
            return output.Fail(gone.Error!, gone.ExitCode);
        }

        output.WriteLine("Forgotten. Polling uses the token gh is signed in with, where there is one.");

        return CommandOutput.Success();
    }
}

/// <summary>Words the allow-list commands share.</summary>
internal static class TriggerTrustWords
{
    /// <summary>The scope given, checked, or why it cannot be used.</summary>
    public static (string? Scope, string? Problem) Scope(TriggerScopeSettings settings)
    {
        var repo = settings.Repository?.Trim() is { Length: > 0 } named ? named : null;
        var owner = settings.Owner?.Trim() is { Length: > 0 } who ? who : null;

        if (string.IsNullOrWhiteSpace(settings.Login))
        {
            return (null, "Name the GitHub account by its login.");
        }

        if (repo is not null && owner is not null)
        {
            return (null, "Name one repository with --repository or one owner with --owner, not both.");
        }

        if (repo is not null && GitHubRepo.Parse(repo) is null)
        {
            return (null, $"'{repo}' is not a repository: name one as owner/name.");
        }

        return (repo ?? (owner is null ? "everywhere" : $"{owner}/*"), null);
    }

    /// <summary>Where an entry holds, as a phrase.</summary>
    public static string Where(TriggerAllowEntry entry) =>
        entry.Repo.Length > 0 ? $"on {entry.Repo}" : entry.Owner.Length > 0 ? $"on {entry.Owner}'s repositories" : "everywhere";
}
