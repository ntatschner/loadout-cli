# Schedules

Schedules lists the runs that start without you, from `loadout team schedule list`, and says first whether the daemon will fire them: running, held (`loadout daemon resume`), or not running (`loadout daemon`).

## Each row

- The schedule's name, its team and goal, when it fires (every 24h, at 09:00, on commit), the next time, its posture and what each run may spend.
- Stop it: `$ loadout team schedule remove <name>`.

## Do and don't

- Do say "not running, so none of these will fire" in Needs text when the daemon is down; a restarted machine looks exactly like this.
- Don't offer to make up a missed run.
