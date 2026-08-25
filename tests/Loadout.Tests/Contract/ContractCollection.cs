using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Runs the contract tests one at a time.
/// <para>
/// Each of them starts the built command line as a process. Left to run in
/// parallel they start dozens of copies of a freshly written executable at
/// once, and a full run would occasionally fail a scattering of them while the
/// same tests passed alone and passed on the next run — the signature of
/// contention over the file rather than of anything wrong with the code.
/// </para>
/// <para>
/// Serialising them costs a few seconds and removes a class of failure that is
/// worse than slow: one that trains everybody to re-run the suite instead of
/// reading it.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ContractCollection
{
    internal const string Name = "command line as a process";
}
