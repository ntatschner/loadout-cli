# Third-party notices

`loadout` is MIT licensed. It depends on the packages below, every one of them
under a permissive licence compatible with shipping this project as open source.

This file is checked rather than trusted: `build/licences.ps1` reads the licence
of every restored package from its own `.nuspec` and fails the build on anything
outside the allowlist. CI runs it on every change.

## Shipped in the binary

| Package | Licence |
|---|---|
| [Spectre.Console](https://spectreconsole.net/) | MIT |
| [Terminal.Gui](https://github.com/tui-cs/Terminal.Gui) | MIT |
| [Spectre.Console.Cli](https://spectreconsole.net/) | MIT |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | MIT |
| Microsoft.Extensions.DependencyInjection | MIT |
| Microsoft.Extensions.DependencyInjection.Abstractions | MIT |
| Microsoft.Extensions.Logging.Abstractions | MIT |
| .NET runtime and base class libraries | MIT |

## Build and test only

These are not part of a release.

| Package | Licence |
|---|---|
| [xunit](https://xunit.net/) and its components | Apache-2.0 |
| xunit.runner.visualstudio | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |
| Microsoft.CodeCoverage | MIT |
| Microsoft.TestPlatform.ObjectModel, Microsoft.TestPlatform.TestHost | MIT |
| Newtonsoft.Json | MIT |
| [FluentAssertions](https://fluentassertions.com/) **7.2.2** | Apache-2.0 |
| System.Configuration.ConfigurationManager, System.Security.Cryptography.ProtectedData | MIT |

### Why FluentAssertions is pinned exactly

FluentAssertions is Apache-2.0 up to and including version 7. From version 8 it
is distributed under the Xceed Community License, which is not an open-source
licence and charges for commercial use.

The reference is therefore pinned to `[7.2.2]` rather than floated. A routine
dependency bump would otherwise swap an open-source test library for one this
project cannot ship under, without anything in the build noticing. The licence
check exists to catch precisely that, and it fails on version 8.

## Art the office can draw with, and does not ship

`loadout` ships no artwork. The dashboard's office view draws a square with the
node's name in it, and that is the whole of what is in this repository or in a
release.

It will also draw sprites, if you install some yourself. A set is a directory
under `<state>/teams/office/` and `team-office-set` names the one to use; see
[docs/teams.md](docs/teams.md). The files stay on your machine and Loadout only
reads them.

That split is deliberate rather than tidy-minded. Pixel-art asset packs are
typically sold under a licence that permits using the files inside a finished
project and forbids making the original files available for extraction or
download — and a public source repository does exactly that to everybody who
clones it. Committing a bought pack here would breach the licence for the pack
*and* hand this project's users files they have no right to.

If you use a pack from [Lennox Studio](https://lennoxstudio.itch.io/), whose
layout the documented set names follow, its licence asks for no credit and
suggests "Pixel Art Assets by Lennox Studio". Whatever you install, the terms
are between you and whoever sold it: read the licence in the download rather
than assuming this note covers you.

## Tooling

Not dependencies of the project, but needed to build a release.

| Tool | Licence | Note |
|---|---|---|
| [WiX Toolset](https://wixtoolset.org/) **5.0.2** | MS-RL | Builds the MSI |
| `dpkg-deb`, `rpmbuild` | GPL | Build the Linux packages; not linked or redistributed |
| Docker | Apache-2.0 | Optional, for running the Linux checks on another host |

WiX is pinned to 5 deliberately. Version 6 and later require accepting the Open
Source Maintenance Fee agreement before the tool will run, which is a decision
for whoever owns this project rather than one a build script should accept on
their behalf. Version 5 asks for nothing.

`dpkg-deb` and `rpmbuild` are invoked as external programs when building
packages. Nothing from them is linked into or redistributed with `loadout`, so
their licences do not reach the released binaries.
