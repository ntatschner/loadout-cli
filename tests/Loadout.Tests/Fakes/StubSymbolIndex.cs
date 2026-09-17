using Loadout.Core.Instructions;
using Loadout.Models.Results;

namespace Loadout.Tests.Fakes;

/// <summary>
/// A symbol index that answers from a fixed set rather than a repository.
/// </summary>
/// <remarks>
/// The compiler and the budget report only need what a scan would have said,
/// not the scan. Handing them a list keeps the tests off the file system and
/// off git, both of which the real service asks.
/// </remarks>
public sealed class StubSymbolIndex : ISymbolIndexService
{
    private readonly IReadOnlyList<Symbol> _symbols;

    public StubSymbolIndex(params Symbol[] symbols)
    {
        _symbols = symbols;
    }

    /// <summary>Every repository path this was asked about.</summary>
    public List<string> Asked { get; } = [];

    public Task<OperationResult<SymbolLookup>> FindAsync(
        string repositoryPath,
        string slug,
        string query,
        int limit = 10,
        bool rescan = false,
        CancellationToken ct = default) =>
        Task.FromResult(OperationResult<SymbolLookup>.Ok(new SymbolLookup(
            SymbolSearch.Find(_symbols, query, limit), _symbols.Count, "abc1234", false, false)));

    public Task<IReadOnlyList<Symbol>> ScanAsync(
        string repositoryPath,
        string slug,
        CancellationToken ct = default) =>
        Task.FromResult(_symbols);

    public Task<OperationResult<SymbolRefresh>> RefreshAsync(
        string repositoryPath,
        string slug,
        IReadOnlyList<string> files,
        bool dryRun = false,
        CancellationToken ct = default) =>
        Task.FromResult(OperationResult<SymbolRefresh>.Ok(new SymbolRefresh(
            [.. files.Select(file => new SymbolRefreshedFile(file, 0, 0))],
            _symbols.Count,
            "abc1234def",
            false)));

    public Task<OperationResult<SymbolDigest>> DigestAsync(
        string repositoryPath,
        string slug,
        CancellationToken ct = default)
    {
        Asked.Add(repositoryPath);

        return Task.FromResult(OperationResult<SymbolDigest>.Ok(new SymbolDigest(
            SymbolDigest.Modules(_symbols),
            _symbols.Count,
            "abc1234def",
            SymbolDigest.LanguagesOf(_symbols))));
    }
}
