# Remedy

Remedy is one script a team wrote down for fixing something, with the record beside it, and the two keys it needs before it can run with nobody watching. Neither key is an agent's.

## The two keys

- **This machine, by kind**: `loadout config set team-remediation "disk=trusted, service=ask, network=never"`. The card says the rule for the remedy's kind in a sentence.
- **You, about one script**: trusting it records a fingerprint of the script as it is now. **Trusting is terminal only**: it is a standing agreement for a script to run unattended, made in a terminal on purpose, so the card shows the command and no button. A script that has changed since it was trusted reads "changed since trusted" and is asked about again.

## Requests

- A held remedy is a question: which node asked, in which run, what it says the problem is, and why it is held rather than run. Let it run or Refuse: `$ loadout team remedy requests --team <team> --approve r1 --reason "…"` (or `--refuse r1`). These are allowed from the page.
- Refusing is not a dead end: the node reports what it needed and why.

## Accessibility

- An `article` named by the remedy. Its record is a TeamRules list; a request is a group like a Gate.
