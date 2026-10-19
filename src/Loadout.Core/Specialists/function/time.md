---
id: function.time
kind: function
title: Dates and times
summary: Instants, wall-clock times, time zones, durations and estimates.
task_phrases:
  # Matched as whole words, so 'date' does not fire on "update". A bare 'time'
  # is left out on purpose: "at the same time" and "first time" are in tasks
  # that have nothing to do with clocks.
  - 'date'
  - 'dates'
  - 'datetime'
  - 'timestamp'
  - 'timestamps'
  - 'time zone'
  - 'timezone'
  - 'utc'
  - 'daylight saving'
  - 'dst'
  - 'duration'
  - 'elapsed'
  - 'clock'
  - 'calendar'
  - 'schedule'
  - 'scheduled'
  - 'cron'
  - 'expiry'
  - 'expires'
  - 'deadline'
  - 'ttl'
  - 'iso 8601'
  - 'how long'
  - 'estimate'
---

## Cares about

Which of three things a value is: an instant, a time on somebody's wall clock,
or a calendar date. Most time bugs are one of these stored as another.

## Working rules

- Store and compare instants in UTC, or with their offset attached
  (`DateTimeOffset`, `Instant`, an ISO 8601 string ending in `Z` or an offset).
  Convert to local time only to show it to a person.
- Keep a date as a date. A birthday or a billing day is not midnight UTC, and
  storing it as one moves it a day for half the world.
- A future wall-clock time ("9am every Monday in London") is stored as the
  local time and the zone name, not as an instant computed today. The zone's
  rules can change before it arrives.
- Read the clock through an injected abstraction (`TimeProvider` in .NET, an
  injected clock elsewhere), never the static `Now`, so tests can set it. Use
  the codebase's existing one rather than adding a second.
- Measure elapsed time with a monotonic clock (`Stopwatch`,
  `performance.now()`), not by subtracting two wall-clock readings.
- Parse and format with an explicit format and the invariant culture on the
  wire, in files and in logs. The current culture is for display only.
- Use ISO 8601 for anything a machine reads back.
- Name the unit in every duration: `timeoutSeconds`, `TimeSpan`, `"30s"`. A
  bare `30` is a bug waiting for whoever assumes milliseconds.

## Estimates

When saying how long something will take, give a range and say whose time it
is: "ten to twenty minutes of agent time, then an hour or two of your review",
not "two days". Agent time and a person's time differ by an order of magnitude, and
waiting on CI or a reviewer is neither. Where a configured voice says
otherwise, follow it.

## Pitfalls

- A day that is 23 or 25 hours long. Adding 24 hours is not adding a day.
- A local time that happens twice, or never, on the night the clocks change.
- `DateTime.Kind` of `Unspecified`, silently treated as local by one call and
  UTC by the next.
- A test that passes except near midnight, at month end, or in another zone.
- An expiry checked against one machine's clock and set by another's.
- Cron expressions read in the server's zone when the person meant theirs.

## Verify

Run the covering tests with the clock pinned to the awkward moments: a clock
change, the last day of February in a leap year, midnight UTC, and a zone well
away from the machine's own.
