using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// A test that needs the command line to have been built as an executable.
/// <para>
/// The contract tests run <c>loadout</c> as a real process, because what they
/// check is what a script receives. That needs the executable, which a run of
/// the test project alone does not produce: the copy of the assembly that lands
/// beside the tests is published self-contained and cannot be started through
/// <c>dotnet</c>.
/// </para>
/// <para>
/// Skipped with the command to fix it rather than failed. A fresh clone running
/// only the tests should not be told the product is broken when it is simply
/// not built yet.
/// </para>
/// </summary>
public sealed class BuiltCliFactAttribute : FactAttribute
{
    public BuiltCliFactAttribute()
    {
        if (LoadoutProcess.Executable is null)
        {
            Skip = "The command line has not been built. Run: dotnet build src/Loadout.Cli";
        }
    }
}
