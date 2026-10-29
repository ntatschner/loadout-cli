# Schedules

Schedules lists the runs that start without you, from `loadout team schedule list`, and says first whether the daemon will fire them: running, held (`loadout daemon resume`), or not running (`loadout daemon`).

## Each row

- The schedule's name, its team and goal, when it fires (every 24h, at 09:00, on commit), the next time, its posture and what each run may spend, then `held` where it will not fire (main says "held" for one that is not enabled).
- Stop it: `$ loadout team schedule remove <name>`. `onRemove` may answer `false` (or resolve to it) where it was not removed, and the row stays, as main; the last one gone says "Nothing is scheduled on this machine."

## Do and don't

- Do say "not running, so none of these will fire" in Needs text when the daemon is down; a restarted machine looks exactly like this.
- Don't offer to make up a missed run.
