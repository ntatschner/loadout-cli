# Security

## Reporting something

Please report vulnerabilities privately, through
[GitHub's private reporting](https://github.com/ntatschner/loadout-cli/security/advisories/new)
on the Security tab. It is enabled on this repository.

Do not open a public issue for a vulnerability, and please do not include a
working exploit in the first message. A description of the class of problem and
how to reach it is enough to get started.

## Supported versions

The latest release. This is a young project and there is no long-term support
branch to speak of; fixes go out as a new release rather than as a backport.

## What Loadout handles that is worth your attention

**Secrets.** Credentials go to the operating system's own store: Credential
Manager, Secret Service, Keychain. The launcher does not keep its own.
Secret *detection* exists in a few places, and it reports the name of the
pattern that matched and never the matched text, anywhere: not to stdout, not
to logs, not in an exception message, not in `--json` output. If you ever see a
secret value in Loadout's output, that is a vulnerability and worth reporting
even if nothing else is wrong.

**Your repositories.** Loadout will not rewrite Git history, and remediation
will not reach the network without being asked. Operations that change files
show you the change first and take a snapshot you can restore with
`loadout backup restore`.

**Telemetry.** There is none in the usual sense. `loadout usage` reads
transcripts that already exist on your disk. `loadout telemetry serve` runs a
receiver that listens locally and stores locally. Nothing is sent anywhere, and
no repository contents, prompts, conversations, file contents or argument
values are recorded at any point.

**Downloads.** `install.sh` verifies a SHA-256 before extracting anything and
refuses on a mismatch. Every release publishes `SHA256SUMS`. Windows packages
are signed; macOS binaries are not yet signed or notarised, which is why the
install instructions say to clear the quarantine attribute on one file rather
than telling anyone to turn Gatekeeper off.
