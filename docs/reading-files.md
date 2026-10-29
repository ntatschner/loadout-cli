# What Loadout reads, and how often

Loadout keeps no database. Runs, sessions, tasks, memory and configuration are
all files, and the processes that show them — the daemon behind Studio and the
dashboard, the launcher, the status line — find out what changed by reading
those files again. This page records how often that happens, which files are
being moved to a reader that remembers what it read, and which are left alone
and why. It is updated as each one moves, with the count before and after.

The figures come from the read trace described in
[Counting what Loadout reads](commands.md#counting-what-loadout-reads). It
counts only the reads it was put around, so every figure here is a floor.

## The bar

A file is worth a shared reader when one process reads it more than once for
each time it changes. A second read of contents nothing has touched is the
waste; a read of new contents is the job. The trace calls the ratio
`readsPerChange`, and tells one version of a file from the next by its length
and last-write time together, because either alone misses some changes.

A file that clears the bar but costs almost nothing to read is still left for
later. Moving a source is a change with its own tests and its own way of
going wrong, and it has to buy something.

## Before anything moved

Version 0.108.0, half an hour of ordinary use: Studio open, the launcher
opened a few times, and a Claude Code session drawing the status line. The
daemon, the one process that runs throughout, made 8,544 reads of 2.0 GB and
spent 15 seconds reading.

| What | Reads | Reads per change | MB | Seconds reading |
|---|---|---|---|---|
| The launch ledger | 864 | 144 | 1,138 | 7.3 |
| Memory topics, 489 of them | 1,869 | up to 27 | 3.4 | 3.0 |
| Claude Code transcripts | 159 | 2.5 | 346 | 2.0 |
| Codex session files | 574 | 20 | 442 | 0.1 |
| Project task lists | 307 | 26 | 13 | 0.6 |
| Run journals | 330 | 33 | 50 | 0.1 |
| `machines.yaml` | 397 | 132 | 2.5 | 0.4 |
| The project registry | 357 | 357 | 1.9 | 0.4 |
| Trigger journals | 2,070 | 345 | 1.6 | 0.2 |
| Running sessions | 759 | 190 | 0.2 | 0.1 |

The launch ledger is more than half of all of it. It gains a line or two for
each session started, yet the daemon read it whole about every two seconds,
because listing your past conversations reads it two or three times a
request. That it is the conversation list doing most of the reading comes from
the code, not the trace, which records the method that read and not the page
that asked.

The other short-lived processes barely register. The status line ran every 13
seconds and read four small files each time, in about 70 ms. The launcher read
about 7 MB each time it opened.

## What moves, in order

1. **The launch ledger.** Read once per process, then only the lines added
   since. Every read still checks the file's length and time first, so a new
   session shows up on the next read, as before. After: not measured yet.
2. **Memory topics.** Three seconds of reading in half an hour, almost all of
   it topics that had not changed.
3. **Claude Code transcripts.** Two seconds, and each one read about two and a
   half times per change.

`LOADOUT_SHARED_READERS=0` puts every moved source back to reading the whole
file every time. It is there so that a wrong answer from the sharing can be
told apart from one in the file, without waiting for a release.

## Left alone, for now

- **Codex session files.** 442 MB read and twenty reads per change, but 69 ms
  in all: the operating system already had them in memory. The bytes overstate
  what they cost.
- **The small, much-repeated files:** trigger journals, running sessions,
  `machines.yaml`, the project registry, task lists and run journals. Each is
  read hundreds of times per change, and together they took under two seconds
  in half an hour. They clear the bar and buy little; they move once the
  sources above have shown the shared reader works.
- **Ideas, and topics read once.** 373 of the daemon's 662 sources were read
  once, or once per change: each idea when it was opened, each memory topic a
  search touched once. There is nothing to share.

## Across processes

The launcher and the daemon read some of the same files, the run journals and
the ledger among them, and a reader inside one process cannot help the other.
That came to about 22 MB in the half hour, against 2 GB the daemon read inside
itself. So sharing stays inside each process. Having the daemon serve reads to
the others waits until a measurement says it would pay.

## What these figures don't show

- Time spent parsing what was read. The trace times the read, not what was done
  with it afterwards.
- Any other machine. The figures are one half hour on Windows with a warm file
  cache; a cold cache or a slower disk would make the large files cost more.
- Reads nobody put a trace around. A file read somewhere new is not counted
  until its read is.
