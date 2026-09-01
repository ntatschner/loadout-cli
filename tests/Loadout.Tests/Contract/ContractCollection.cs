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
/// <para>
/// It is not enough on its own, and the reason is that this collection can only
/// see its own processes. The integration tests are not serialised and drive
/// real Git, so three of them spawn alongside whichever contract test holds this
/// collection, and 0xC0000142 comes back for a process that never started.
/// <c>maxParallelThreads</c> in <c>xunit.runner.json</c> is therefore 2 rather
/// than the default: measured on one machine, four threads failed 43 and 48
/// tests on consecutive runs and took 4m54s, two threads passed three times
/// running in 1m38s, 1m45s and 1m42s, and one thread passed in 2m35s. Parallelism
/// past two was not buying speed — it was queueing inside Windows and then paying
/// for it again in the retries below.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ContractCollection
{
    internal const string Name = "command line as a process";
}
