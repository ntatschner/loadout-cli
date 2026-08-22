using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;

namespace AgentWorkspace.Tests.Fakes;

/// <summary>
/// Records permission requests without applying them, so path-layout tests can
/// run on a host whose permission model differs from the one being exercised.
/// </summary>
public sealed class NoOpFilePermissions : IFilePermissions
{
    public List<string> RestrictedFiles { get; } = [];

    public List<string> RestrictedDirectories { get; } = [];

    public List<string> MadeExecutable { get; } = [];

    public OperationResult RestrictToCurrentUser(string filePath)
    {
        RestrictedFiles.Add(filePath);
        return OperationResult.Ok();
    }

    public OperationResult RestrictDirectoryToCurrentUser(string directoryPath)
    {
        RestrictedDirectories.Add(directoryPath);
        return OperationResult.Ok();
    }

    public OperationResult MakeExecutable(string filePath)
    {
        MadeExecutable.Add(filePath);
        return OperationResult.Ok();
    }
}
