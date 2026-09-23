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
| [Atkinson Hyperlegible Next](https://github.com/googlefonts/atkinson-hyperlegible-next) | OFL-1.1 |
| [Atkinson Hyperlegible Mono](https://github.com/googlefonts/atkinson-hyperlegible-next-mono) | OFL-1.1 |

## The type the dashboard is set in

The team dashboard is set in Atkinson Hyperlegible Next, with Atkinson
Hyperlegible Mono for anything you would type. Both were designed for the
Braille Institute so that the characters people most often misread - I, l and
1, 0 and O, rn and m - look different from one another, which is the reason
they were chosen for a page read at a glance from across a room.

They are not packages, so `build/licences.ps1` does not see them. Three woff2
files, cut down to Latin, are inlined into the dashboard page as base64 and
reach a browser as part of it: nothing is fetched. They are version 2.001 of
each, and the copyright lines below are the ones in the files' own name
tables.

The SIL Open Font License lets them be embedded and redistributed with
software, on two conditions that matter here: the copyright notice and the
licence travel with every copy, which is what this section is for, and the
fonts are never sold on their own.

```text
Copyright 2020-2024 The Atkinson Hyperlegible Next Project Authors
(https://github.com/googlefonts/atkinson-hyperlegible-next)

Copyright 2020-2024 The Atkinson Hyperlegible Mono Project Authors
(https://github.com/googlefonts/atkinson-hyperlegible-next-mono)

This Font Software is licensed under the SIL Open Font License, Version 1.1.
This license is copied below, and is also available with a FAQ at:
https://openfontlicense.org


-----------------------------------------------------------
SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
-----------------------------------------------------------

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded,
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.

DEFINITIONS
"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting,
or substituting -- in part or in whole -- any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

"Author" refers to any designer, engineer, programmer, technical
writer or other person who contributed to the Font Software.

PERMISSION & CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining
a copy of the Font Software, to use, study, copy, merge, embed, modify,
redistribute, and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components,
in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or
in the appropriate machine-readable metadata fields within text or
binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless explicit written permission is granted by the corresponding
Copyright Holder. This restriction only applies to the primary font name as
presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any
Modified Version, except to acknowledge the contribution(s) of the
Copyright Holder(s) and the Author(s) or with their explicit written
permission.

5) The Font Software, modified or unmodified, in part or in whole,
must be distributed entirely under this license, and must not be
distributed under any other license. The requirement for fonts to
remain under this license does not apply to any document created
using the Font Software.

TERMINATION
This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE FONT SOFTWARE.
```

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
