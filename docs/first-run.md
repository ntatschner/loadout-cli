# First run and configuration

```bash
loadout setup
```

Running `loadout` with no arguments on an unconfigured machine goes here too,
because an empty project list tells a new user nothing about what to do next.

Every question can also be answered up front, so provisioning a machine needs no
one sitting at it:

```bash
loadout setup --create-new --github --name agent-workspaces   --register-discovered --migrate --global-excludes --non-interactive
```

Both routes run the same code — an interactive run is just one where nothing was
answered in advance — so the scripted path cannot drift from the one people see.
Anything genuinely unanswerable stops before doing any work and names the flag
that would settle it, rather than failing halfway through a setup.

If you choose to create a new workspace and the GitHub CLI is installed and
signed in, it offers to create the private repository and push for you. That is
a convenience for one common host, not a dependency: the launcher is
provider-agnostic (spec section 10), the other option takes any Git URL, and
Forgejo, GitLab, Azure DevOps or a bare SSH repository all work the same way.
The repository is always created private — a workspace holds project context,
decisions and handoffs, and making that public is an irreversible disclosure
that should not be one keystroke away.

The wizard offers the three choices of spec section 61 as equals — point at an
existing central workspace, create a new one, or **run without central
storage**. The last is a real way to use the tool, not a degraded mode: it
creates the same directory layout locally, so adopting a shared workspace later
is a matter of pushing what you already have.

It then checks Git is present before asking anything and sets a **global** Git
identity if none exists — global specifically, because a plain config read
resolves through whatever repository you happen to be standing in, and a local
identity in an unrelated project must not be mistaken for one the workspace can
use. Without it every workspace commit fails with "Author identity unknown".

It picks a secret provider that actually works on this machine, lists the
repositories it found in your development roots, offers to register them, and
then offers to migrate any agent files out of them.

Migration runs **before** the global Git excludes are installed, and the order
matters: installing the excludes first would make the very files migration
exists to move become ignored, so setup would protect the repository and then
report nothing to migrate. Clean up first, then stop it happening again.

## Environments and security profiles

A project can define environments, and selecting one changes both which
credentials resolve and how much the agent is allowed to do:

```yaml
environments:
  production:
    description: Production investigation
    security_profile: production
    environment:
      DATABASE_URL:
        secret: starstats/production-db
```

```bash
loadout starstats --environment production
```

Security profiles are expressed in the launcher's own vocabulary — filesystem,
network, approvals, tool lists — and each adapter translates them into whatever
its agent actually supports. A project says "production work is read-only"
once, and Claude and Codex each honour it as far as they can:

| Profile filesystem | Claude | Codex |
|---|---|---|
| `Repository` | agent default | `--sandbox workspace-write` |
| `ReadOnly` | `--permission-mode plan` | `--sandbox read-only` |
| `Restricted` | `--permission-mode manual` | `--sandbox read-only` |

**A profile can only ever tighten.** There is no value that loosens an agent's
defaults, and the adapters never emit `--dangerously-skip-permissions`,
`bypassPermissions`, `danger-full-access` or their equivalents. A profile lives
in a shared repository; if one could loosen a sandbox, anyone who could edit
that repository could switch off somebody else's safety controls. Tests assert
this over every built-in profile.

Naming an environment that does not exist stops the launch rather than falling
back — someone who typed `--environment prod` meaning `production` must not
quietly get development's permissions.

Where an installed agent does not advertise the option needed to enforce part of
a profile, the launcher says so instead of proceeding silently.

