<script lang="ts">
  // Pure Focus: Home, Sessions, Teams, a session and a run all become the
  // desk, the route picking the scope or the thing. The list is what is
  // live; the view is the picked thing; its actions are the same ones every
  // other page offers, with the same commands. Loaded only when first
  // needed, as the terminal is most of Studio's size. The scope and the
  // last thing picked are kept in this browser.
  import { untrack } from 'svelte';
  import Crew from '../lib/components/Crew.svelte';
  import DoneWhen from '../lib/components/DoneWhen.svelte';
  import Gate from '../lib/components/Gate.svelte';
  import RunShape from '../lib/components/RunShape.svelte';
  import SessionPane from '../lib/components/SessionPane.svelte';
  import WorkDesk from '../lib/components/WorkDesk.svelte';
  import { actionsFor } from '../lib/actions';
  import { teamApi, type Outcome, type Run } from '../lib/api';
  import { command } from '../lib/commands';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';
  import { readSplit, SPLIT_KEYS } from '../lib/splits';
  import { resumeCommand } from '../lib/terminal';
  import { gateButtons } from '../lib/teams';
  import { answerGate, carryOn, hold, runLines, stop } from '../lib/teamsAct';
  import { criteriaOf, crewOf, gateOf, shapeOf } from '../lib/teamsData';
  import { toasts } from '../lib/toasts.svelte';
  import { isScope, PAGE_KEYS, pureGroups, SCOPE_WORDS, type PureAction, type PureCommand, type PureItem, type PureScope } from '../lib/pure';
  import { PAGE_TITLES } from '../lib/modes';

  interface Props {
    /** The route: home, sessions, session or teams. */
    page: string;
    /** The session or run the route names. */
    named?: string | null;
    onlaunch?: () => void;
  }

  let { page, named = null, onlaunch = undefined }: Props = $props();

  const SCOPE = 'studio-pure-scope';
  const PICKED = 'studio-pure-item';

  let kept = $state<PureScope>(((value) => (isScope(value) ? value : 'all'))(studio.kept(SCOPE, 'all')));
  let desk = $state<ReturnType<typeof WorkDesk> | undefined>();
  let outcomes = $state<Record<string, Outcome[]>>({});

  // Sessions and Teams scope the list themselves; Home keeps whatever was chosen last.
  const scope: PureScope = $derived(page === 'sessions' ? 'sessions' : page === 'teams' && !named ? 'runs' : kept);
  const groups = $derived(pureGroups(studio.lines, studio.runs, studio.idleAfter, scope));
  const routed = $derived(named ? (page === 'session' ? sessionId(named) : `run:${named}`) : null);
  const watch = $derived(studio.watchOnly ? 'Studio is only watching: type the command in a terminal' : undefined);

  void studio.loadCatalogue();

  function sessionId(ref: string) {
    return studio.lines.find((line) => line.kind === 'session' && line.ref === ref)?.id ?? ref;
  }

  const lineOf = (item: PureItem) => studio.lines.find((line) => line.id === item.id) ?? null;
  const runOf = (item: PureItem): Run | null => (item.kind === 'team run' ? (studio.runs.find((run) => run.id === item.ref) ?? null) : null);

  function go(where: PureScope) {
    kept = where;
    studio.keep(SCOPE, where);

    if (page !== 'home') {
      router.go('home');
    }

    toasts.add({ level: 'info', text: `The list: ${SCOPE_WORDS[where]}`, cmd: 'G then a letter' });
  }

  /** Leaves Pure Focus for a page that is not part of it. */
  function leave(...parts: string[]) {
    studio.setMode('default');
    router.go(...parts);
  }

  /** The actions for the picked thing: the same, with the same commands, as anywhere else. */
  function actions(item: PureItem): PureAction[] {
    if (item.kind === 'session') {
      const line = lineOf(item);
      const session = line?.session;

      if (!line || !session || line.stage === 4) {
        return [{ label: 'Carry it on in a terminal', cmd: resumeCommand(session?.agentSession ?? null, session?.project ?? null), disabledReason: undefined }];
      }

      return [
        {
          label: line.waiting ? 'Go to the question' : 'Type into it',
          kind: line.waiting ? 'needs' : '',
          cmd: 'puts the keyboard in its terminal · answered there',
          run: () => {
            void desk?.openView();

            return 'The keyboard is in its terminal. Ctrl+] leaves it.';
          },
        },
        {
          label: 'End the session',
          kind: 'danger',
          cmd: command.endSession(),
          disabledReason: studio.watchOnly ? 'Studio is only watching: end it from where it runs' : undefined,
          run: async () => ((await studio.endSession(session)) ? `Ended ${line.title}` : 'It did not end'),
        },
      ];
    }

    if (item.kind === 'team run') {
      const run = runOf(item);

      if (!run) {
        return [];
      }

      const out: PureAction[] = [];
      const gate = run.gates[0];

      if (gate) {
        const shown = gateOf(run, gate);

        for (const button of gateButtons({ ...shown, run: run.id })) {
          out.push({ label: button.label, kind: button.kind, cmd: button.cmd, disabledReason: watch, run: async () => ((await answerGate(run.id, gate.id, button.send)) ? button.said : 'It was not taken') });
        }
      }

      if (run.running) {
        out.push(
          { label: 'Hold after this round', cmd: runLines.hold(run.id), disabledReason: watch, run: () => hold(run.id) },
          { label: 'Let it carry on', cmd: runLines.carryOn(run.id), disabledReason: watch, run: () => carryOn(run.id) },
          { label: 'Stop after this round', kind: 'danger', cmd: runLines.stop(run.id), disabledReason: watch, run: () => stop(run.id) },
        );
      }

      out.push({ label: 'Open its full page', cmd: 'the five depths, in Default · changes nothing', run: () => leave('teams', run.id) });

      return out;
    }

    const line = lineOf(item);

    return line
      ? actionsFor(studio, line, (...parts) => leave(...parts)).map((action) => ({
          label: action.label,
          cmd: action.cmd,
          kind: action.kind === 'danger' ? 'danger' : action.kind === 'needs' ? 'needs' : '',
          disabledReason: action.disabledReason,
          run: action.onpress,
        }))
      : [];
  }

  /** The command bar's matches: the picked thing's actions, places and things, and every loadout command Studio has a form for. */
  function commands(typed: string, picked: PureItem | null): PureCommand[] {
    const rows: PureCommand[] = [];

    if (picked) {
      actions(picked).forEach((action, index) =>
        rows.push({
          kind: 'action',
          label: `${index + 1}  ${action.label}`,
          cmd: action.disabledReason ?? action.cmd,
          run: () => {
            if (!action.disabledReason && action.run) {
              void Promise.resolve(action.run()).then((said) => toasts.add({ level: 'info', text: typeof said === 'string' ? said : action.label, cmd: action.cmd ?? '' }));
            }
          },
        }),
      );
    }

    for (const [where, words] of Object.entries(SCOPE_WORDS) as [PureScope, string][]) {
      rows.push({ kind: 'go', label: `The list: ${words}`, cmd: 'changes nothing', run: () => go(where) });
    }

    for (const [key, where] of Object.entries(PAGE_KEYS)) {
      rows.push({ kind: 'go', label: PAGE_TITLES[where], cmd: `its desk · G then ${key.toUpperCase()}`, run: () => router.go(where) });
    }

    for (const item of pureGroups(studio.lines, studio.runs, studio.idleAfter, 'all').flatMap((group) => group.items)) {
      rows.push({ kind: 'go', label: item.title, cmd: `${item.kind} · ${item.word}`, run: () => void desk?.select(item.id) });
    }

    if (!studio.watchOnly && onlaunch) {
      rows.push({ kind: 'command', label: 'Start a session', cmd: '$ loadout launch <project>', run: onlaunch });
    }

    for (const one of studio.catalogue) {
      rows.push({ kind: 'command', label: one.description || one.path, cmd: `$ loadout ${one.path}`, run: () => leave('commands', ...one.path.split(' ')) });
    }

    return rows.filter((row) => {
      const q = typed.trim().toLowerCase();

      return !q || `${row.label} ${row.cmd ?? ''} ${row.kind}`.toLowerCase().includes(q);
    });
  }

  // A run's done-when, read once it is picked and again when it moves on.
  function readOutcomes(run: Run) {
    teamApi.outcomes(run.id).then((read) => (outcomes = { ...outcomes, [run.id]: read }), () => undefined);
  }

  let lastRun = '';

  function picked(id: string) {
    studio.keep(PICKED, id);
    const run = studio.runs.find((one) => `run:${one.id}` === id);

    if (run && run.id !== lastRun) {
      lastRun = run.id;
      untrack(() => readOutcomes(run));
    }
  }

  // svelte-ignore state_referenced_locally
  const start = routed ?? studio.kept(PICKED, '');

  /** How wide the desk's list and actions were left, in this browser; every desk shares it. */
  let deskSplit = $state(readSplit(studio.kept(SPLIT_KEYS.desk, ''), 2, true));

  function keepDesk(fractions: number[]) {
    deskSplit = fractions;
    studio.keep(SPLIT_KEYS.desk, JSON.stringify(fractions));
  }
</script>

<h1 class="ls-vh">Pure Focus</h1>
<div class="deskwrap">
  <WorkDesk
    bind:this={desk}
    {groups}
    selected={routed ?? (start || null)}
    keys={studio.keys}
    label={`Everything live, ${SCOPE_WORDS[scope]}`}
    {actions}
    {commands}
    ongo={go}
    onpage={(where) => router.go(where)}
    onselect={picked}
    split={deskSplit}
    onsplit={keepDesk}
  >
    {#snippet view(item)}
      {#if item.kind === 'session'}
        <SessionPane id={item.ref} />
      {:else if item.kind === 'team run'}
        {@const run = runOf(item)}
        {#if run}
          {@const gate = run.gates[0]}
          {@const shape = shapeOf(run)}
          <div class="pview">
            {#if gate}
              <Gate {...gateOf(run, gate)} run={run.id} disabledReason={watch} onanswer={(send) => answerGate(run.id, gate.id, send)} />
            {/if}
            <RunShape rounds={shape.rounds} end={shape.end} />
            {#if outcomes[run.id]}
              <DoneWhen criteria={criteriaOf(outcomes[run.id])} goal={run.goalUnderstood} />
            {/if}
            <Crew nodes={crewOf(run)} />
            <p class="calm small">Its five depths are on its full page: press the number beside Open its full page, or type <kbd class="ls-kbd">:</kbd> open.</p>
          </div>
        {:else}
          <p class="calm">That run is no longer here.</p>
        {/if}
      {:else}
        {@const line = lineOf(item)}
        <div class="pview">
          <dl class="ls-rules facts">
            <dt>What</dt><dd>{item.kind}</dd>
            <dt>Where</dt><dd>{line?.where ?? 'not known'}</dd>
            <dt>Now</dt><dd>{item.word}</dd>
          </dl>
          {#each (line?.events ?? []).slice(0, 4) as event, index (index)}
            <p class="calm event"><span class="mono">{event.t ?? ''}</span>{event.text}</p>
          {/each}
        </div>
      {/if}
    {/snippet}
  </WorkDesk>
</div>
