using Loadout.Models.Tasks;

namespace Loadout.Core.Capabilities;

/// <summary>A template written out for one project: what the task it makes will say.</summary>
/// <param name="Id">The task's id, unless the list has one by that name already.</param>
/// <param name="Title">The task's title.</param>
/// <param name="Type">The task's type.</param>
/// <param name="Detail">Everything an unattended run needs, frozen into the task.</param>
/// <param name="DoneWhen">Its finish line.</param>
public sealed record RenderedTemplate(string Id, string Title, TaskType Type, string Detail, IReadOnlyList<string> DoneWhen);

/// <summary>
/// The three setups Loadout offers when a GitHub project lacks one: CI, release
/// and environments.
/// </summary>
/// <remarks>
/// <para>
/// Only these make offers. An accepted offer can hold unattended work, so that
/// power stays with text Loadout tests. A specialist's check can report a gap,
/// and a person can link a task to it by hand.
/// </para>
/// <para>
/// The text is copied into the task when it is made, so nothing an unattended
/// run reads changes when a later Loadout ships a newer template. Raise
/// <see cref="Version"/> whenever the words change: that is what lets
/// <c>project capabilities</c> say an open prerequisite is out of date.
/// </para>
/// <para>
/// The done-when wording is chosen with <c>CriterionTags.Guess</c> in mind. CI's
/// criteria avoid its trigger words so a run can finish them alone, and the
/// person's step on release and environments says "by hand" so a run hands it
/// over rather than claiming it. A test holds this in place.
/// </para>
/// </remarks>
public static class PrerequisiteTemplates
{
    /// <summary>The version of the three templates' text.</summary>
    public const int Version = 1;

    /// <summary>What a stack choice is called when it means the generic text.</summary>
    public const string Generic = "generic";

    /// <summary>What a stack choice is called when it means every stack detected.</summary>
    public const string AllDetected = "all";

    /// <summary>The environment names offered when none have been given before.</summary>
    public static readonly IReadOnlyList<string> DefaultEnvironments = ["dev", "production"];

    private static readonly Dictionary<string, (string Setup, string Commands)> Builds = new(StringComparer.Ordinal)
    {
        ["dotnet"] = ("actions/setup-dotnet", "dotnet restore, dotnet build and dotnet test"),
        ["node"] = ("actions/setup-node", "npm ci, then npm test"),
        ["python"] = ("actions/setup-python", "pip install the project, then pytest"),
        ["powershell"] = ("the runner's own pwsh", "Invoke-Pester"),
        ["go"] = ("actions/setup-go", "go build ./... and go test ./..."),
        ["rust"] = ("the runner's own toolchain", "cargo build and cargo test"),
    };

    private static readonly Dictionary<string, (string Secret, string Publish)> Publishes = new(StringComparer.Ordinal)
    {
        ["dotnet"] = ("NUGET_API_KEY", "dotnet pack, then dotnet nuget push"),
        ["node"] = ("NPM_TOKEN", "npm publish"),
        ["python"] = ("PYPI_API_TOKEN", "build the package, then upload it to PyPI"),
        ["powershell"] = ("PSGALLERY_API_KEY", "Publish-Module"),
        ["go"] = (string.Empty, "build the binaries and attach them to the release"),
        ["rust"] = ("CARGO_REGISTRY_TOKEN", "cargo publish"),
    };

    /// <summary>Whether a check has a template, and so can make an offer.</summary>
    public static bool Has(string check) =>
        check is CapabilityChecks.Ci or CapabilityChecks.Release or CapabilityChecks.Environments;

    /// <summary>Whether the template has a step only a person can do.</summary>
    public static bool HasPersonStep(string check) =>
        check is CapabilityChecks.Release or CapabilityChecks.Environments;

    /// <summary>The template written out, or null for a check that has none.</summary>
    /// <param name="check">The check it fixes.</param>
    /// <param name="stacks">The stacks to write it for. Empty means the generic text.</param>
    /// <param name="environments">The environment names, for that template.</param>
    public static RenderedTemplate? Render(string check, IReadOnlyList<string> stacks, IReadOnlyList<string> environments)
    {
        ArgumentNullException.ThrowIfNull(stacks);
        ArgumentNullException.ThrowIfNull(environments);

        return check switch
        {
            CapabilityChecks.Ci => Ci(stacks),
            CapabilityChecks.Release => Release(stacks),
            CapabilityChecks.Environments => Environments(environments),
            _ => null,
        };
    }

    private static RenderedTemplate Ci(IReadOnlyList<string> stacks)
    {
        var jobs = stacks.Count == 0
            ? "One job that runs the project's own build and test commands."
            : string.Join("\n", stacks.Select(stack => $"- A job for {stack}: {Builds[stack].Setup}, then {Builds[stack].Commands}."));

        return new RenderedTemplate(
            "add-ci",
            "Add a CI workflow on GitHub Actions",
            TaskType.CiCd,
            "Add one workflow at .github/workflows/build.yml that runs on every push and every pull request to the default branch.\n\n"
            + jobs
            + "\n\nPin each action to a version. Use only the permissions the jobs need, starting from contents: read. It needs no secrets.",
            [
                ".github/workflows/build.yml exists and its triggers cover every change to the default branch and every change proposed to it",
                stacks.Count > 1 ? "It has one job for each of: " + string.Join(", ", stacks) : "Its job builds the project and runs its tests",
            ]);
    }

    private static RenderedTemplate Release(IReadOnlyList<string> stacks)
    {
        var stack = stacks.Count > 0 ? stacks[0] : null;
        var (secret, publish) = stack is null ? (string.Empty, "publish whatever this project ships") : Publishes[stack];
        var secrets = secret.Length > 0 ? $"the repository secret {secret}" : stack is null ? "whatever publishing secret it needs" : "no secret beyond the built-in GITHUB_TOKEN";

        return new RenderedTemplate(
            "add-release-workflow",
            "Add a release workflow on GitHub Actions",
            TaskType.Release,
            $"Add one workflow at .github/workflows/release.yml that runs when a version tag is pushed, and {publish}"
            + (stack is null ? "." : $", for the {stack} stack.")
            + $"\n\nThe workflow expects {secrets}. A run cannot create secrets, so the last step is a person's: "
            + "the workflow file alone does not prove a release works.",
            [
                ".github/workflows/release.yml exists and runs when a version tag is pushed",
                $"By hand: {secrets} is set on the repository, and a test release has gone through",
            ]);
    }

    private static RenderedTemplate Environments(IReadOnlyList<string> names)
    {
        var said = string.Join(" and ", names);

        return new RenderedTemplate(
            "add-environments",
            "Set up deployment environments on GitHub",
            TaskType.CiCd,
            $"Set up these deployment environments on the GitHub repository: {said}. "
            + "Point any deploying workflow's environment key at them.\n\n"
            + "Creating an environment needs repository admin rights, which an unattended run's token usually lacks, "
            + "so that step is a person's. Once GitHub lists the environments, Loadout sees them and closes this task.",
            [
                $"By hand: {said} exist under the repository's environment settings on GitHub",
            ]);
    }
}
