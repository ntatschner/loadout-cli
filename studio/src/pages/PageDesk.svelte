<script lang="ts">
  // One of the six pages as a Pure Focus desk, reached with G and a letter:
  // the list is what the page holds, the view is the same page component as
  // in Default, laid out by the pane's width, and the actions are numbered,
  // each with its command. After an action that changes something, the list
  // reads again and keeps what was picked.
  import { untrack } from 'svelte';
  import ForgetDialog from '../lib/components/ForgetDialog.svelte';
  import WorkDesk from '../lib/components/WorkDesk.svelte';
  import Context from './Context.svelte';
  import Ideas from './Ideas.svelte';
  import Memory from './Memory.svelte';
  import Projects from './Projects.svelte';
  import Settings from './Settings.svelte';
  import Spend from './Spend.svelte';
  import { commands as api, memory, readJson, sessions, signOut, SignedOut, teamApi } from '../lib/api';
  import { lineFor } from '../lib/commandLine';
  import {
    contextActions,
    contextGroups,
    ideaActions,
    ideaGroups,
    memoryActions,
    memoryGroups,
    projectActions,
    projectGroups,
    settingsActions,
    settingsGroups,
    spendActions,
    spendGroups,
    type DeskContext,
    type DeskIdea,
    type DeskProject,
    type SettingsFacts,
  } from '../lib/desks';
  import { PAGE_TITLES, type SixPage } from '../lib/modes';
  import { PAGE_KEYS, SCOPE_WORDS, type PureAction, type PureCommand, type PureGroup, type PureItem, type PureScope } from '../lib/pure';
  import { router } from '../lib/router.svelte';
  import { studio } from '../lib/state.svelte';
  import type { Chosen } from '../lib/tasks';
  import { toasts } from '../lib/toasts.svelte';

  let { page, rest, onlaunch = undefined }: { page: SixPage; rest: string[]; onlaunch?: (chosen?: Chosen) => void } = $props();

  const SCOPE = 'studio-pure-scope';

  let ideas = $state<DeskIdea[]>([]);
  let placements = $state<Record<string, { slug: string; isNew: boolean; name: string } | null>>({});
  let projects = $state<DeskProject[]>([]);
  let fixable = $state<Record<string, number>>({});
  let slug = $state<string | null>(null);
  let specialists = $state<{ id: string; title: string; kind: string }[]>([]);
  let topics = $state<{ name: string; scope: string; description?: string }[]>([]);
  let facts = $state<SettingsFacts>({ paused: null, autostart: null, unapproved: [] });
  let days = $state('30');
  let by = $state('project');
  let forgetting = $state<DeskProject | null>(null);

  const refused = $derived(studio.watchOnly ? studio.watchOnlyReason : undefined);

  function fail(error: unknown): string {
    if (error instanceof SignedOut) {
      studio.signedOut = true;
    }

    return (error as Error).message;
  }

  /* ---------- reads, the same the pages make ---------- */

  async function load() {
    try {
      if (page === 'ideas') {
        ideas = (await readJson<{ ideas: DeskIdea[] }>('idea list')).ideas;
      } else if (page === 'projects') {
        projects = (await readJson<{ projects: DeskProject[] }>('project list')).projects;
      } else if (page === 'context' || page === 'memory') {
        const all = page === 'context' ? (await readJson<{ projects: DeskProject[] }>('project list')).projects : (await memory.projects()).map((one) => ({ id: one.slug, name: one.name, available: true }));

        projects = all;
        slug ??= rest[0] ?? all[0]?.id ?? null;

        if (slug && page === 'context') {
          specialists = (await readJson<{ specialists: typeof specialists }>('instructions list', [slug])).specialists;
        } else if (slug) {
          topics = (await memory.topics(slug)).topics;
        }
      } else if (page === 'settings') {
        teamApi.daemon().then((read) => (facts = { ...facts, paused: read.paused }), () => undefined);
        readJson<{ enabled?: boolean }>('daemon autostart show').then((read) => (facts = { ...facts, autostart: !!read.enabled }), () => undefined);
        readJson<{ name: string; approved?: boolean }[]>('pack list').then((read) => (facts = { ...facts, unapproved: read.filter((one) => !one.approved).map((one) => one.name) }), () => undefined);
      }
    } catch (error) {
      fail(error);
    }
  }

  $effect(() => untrack(() => void load()));

  const settled = $derived(studio.jobs.filter((job) => job.state !== 'running').length);
  let seen = untrack(() => settled);

  $effect(() => {
    if (settled !== seen) {
      seen = settled;
      untrack(() => void load());
    }
  });

  /** Another project, for Context and Memory: the next in the list. */
  function another() {
    const at = projects.findIndex((one) => one.id === slug);
    const next = projects[(at + 1) % projects.length];

    if (next) {
      slug = next.id;
      specialists = [];
      topics = [];
      void load();

      return `Now ${next.name}`;
    }

    return 'There is no other project';
  }

  /* ---------- what an action does ---------- */

  const ctx: DeskContext = {
    get refused() {
      return refused;
    },
    async act(path, words) {
      try {
        await api.run(path, words);
        await studio.loadJobs();

        return `Started ${lineFor(path, words)}. It runs as a job; the desk reads again when it ends.`;
      } catch (error) {
        return fail(error);
      }
    },
    async read(path, words) {
      try {
        const read = await api.read(path, words);

        return (read.output || read.error || 'It said nothing.').trim().split('\n').slice(0, 3).join(' ');
      } catch (error) {
        return fail(error);
      }
    },
    async terminal(path, words) {
      try {
        const opened = await sessions.start({ project: '', command: path, words, columns: 120, rows: 32 });

        router.go('session', opened.id);

        return 'Opened in a terminal';
      } catch (error) {
        return fail(error);
      }
    },
    go: (...parts) => router.go(...parts),
  };

  /* ---------- the list and the picked thing ---------- */

  const groups: PureGroup[] = $derived(
    page === 'ideas'
      ? ideaGroups(ideas)
      : page === 'projects'
        ? projectGroups(projects)
        : page === 'context'
          ? contextGroups(slug ?? '', specialists)
          : page === 'settings'
            ? settingsGroups()
            : page === 'memory'
              ? memoryGroups(topics)
              : spendGroups(),
  );

  // The thing a route names is picked first: #memory/<slug>/<store>/<topic>, #ideas/<list>/<id> and so on.
  const routed = $derived(
    page === 'ideas' && rest[1]
      ? `${rest[0]}/${rest[1]}`
      : page === 'projects' && rest[0]
        ? rest[0]
        : page === 'context' && rest[1]
          ? rest[1] === 'specialist'
            ? `spec:${rest[2] ?? ''}`
            : `section:${rest[1]}`
          : page === 'settings' && rest[0]
            ? rest[0]
            : page === 'memory' && rest[2]
              ? `${rest[1]}/${rest[2]}`
              : null,
  );

  function picked(id: string) {
    if (page === 'ideas' && !(id in placements)) {
      const [list, name] = [id.slice(0, id.indexOf('/')), id.slice(id.indexOf('/') + 1)];

      readJson<{ plan: { placement: { slug: string; isNew: boolean; name: string } | null } | null; accepted: unknown }>('idea show', [name, ...(list === '~' ? ['--global'] : ['--project', list])]).then(
        (read) => (placements = { ...placements, [id]: read.accepted ? null : (read.plan?.placement ?? null) }),
        () => undefined,
      );
    } else if (page === 'projects' && !(id in fixable)) {
      readJson<{ findings: { fixable: boolean }[] }[]>('drift', [id]).then((read) => (fixable = { ...fixable, [id]: read[0]?.findings.filter((one) => one.fixable).length ?? 0 }), () => undefined);
    }
  }

  function actions(item: PureItem): PureAction[] {
    switch (page) {
      case 'ideas': {
        const one = ideas.find((idea) => `${idea.project ?? '~'}/${idea.id}` === item.id);

        return one ? ideaActions(one, placements[item.id] ?? null, ctx) : [];
      }
      case 'projects': {
        const one = projects.find((project) => project.id === item.id);

        return one ? projectActions(one, fixable[item.id] ?? null, { ...ctx, forget: () => (forgetting = one) }) : [];
      }
      case 'context':
        return contextActions(item, slug ?? '', { ...ctx, another });
      case 'settings':
        return settingsActions(item.id, facts, {
          ...ctx,
          setting: async (key, value) => {
            await studio.setPreference(key, value);

            return `Set ${key} back to how it came`;
          },
          signOut: async () => {
            await signOut();
            studio.signedOut = true;

            return 'Signed out';
          },
        });
      case 'memory':
        return memoryActions(item, slug ?? '', { ...ctx, another });
      default:
        return spendActions(days, by, { ...ctx, period: (value) => (days = value), group: (value) => (by = value) });
    }
  }

  function go(where: PureScope) {
    studio.keep(SCOPE, where);
    router.go('home');
    toasts.add({ level: 'info', text: `The list: ${SCOPE_WORDS[where]}`, cmd: 'G then a letter' });
  }

  /** The command bar: the picked thing's actions, every page's desk, Home's scopes, and every command Studio has a form for. */
  function bar(typed: string, item: PureItem | null): PureCommand[] {
    const rows: PureCommand[] = [];

    if (item) {
      actions(item).forEach((action, index) =>
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

    for (const [key, where] of Object.entries(PAGE_KEYS)) {
      rows.push({ kind: 'go', label: PAGE_TITLES[where], cmd: `its desk · G then ${key.toUpperCase()}`, run: () => router.go(where) });
    }

    for (const [where, words] of Object.entries(SCOPE_WORDS) as [PureScope, string][]) {
      rows.push({ kind: 'go', label: `The list: ${words}`, cmd: 'on Home · changes nothing', run: () => go(where) });
    }

    if (!studio.watchOnly && onlaunch) {
      rows.push({ kind: 'command', label: 'Start a session', cmd: '$ loadout launch <project>', run: () => onlaunch() });
    }

    for (const one of studio.catalogue) {
      rows.push({ kind: 'command', label: one.description || one.path, cmd: `$ loadout ${one.path}`, run: () => { studio.setMode('default'); router.go('commands', ...one.path.split(' ')); } });
    }

    const q = typed.trim().toLowerCase();

    return rows.filter((row) => !q || `${row.label} ${row.cmd ?? ''} ${row.kind}`.toLowerCase().includes(q));
  }

  void studio.loadCatalogue().catch(() => undefined);

  /** Scrolls the view to a block of Spend once it is drawn. */
  function block(node: HTMLElement, key: string) {
    const show = (id: string) => setTimeout(() => node.querySelector(`#sp-${id}`)?.scrollIntoView({ block: 'start' }), 60);

    show(key);

    return { update: show };
  }

  const label = $derived(page === 'context' || page === 'memory' ? `${PAGE_TITLES[page]} of ${slug ?? '…'}` : PAGE_TITLES[page]);
</script>

<h1 class="ls-vh">{PAGE_TITLES[page]}, as a desk</h1>
<div class="deskwrap">
  <WorkDesk {groups} selected={routed} keys={studio.keys} {label} {actions} commands={bar} ongo={go} onpage={(where) => router.go(where)} onselect={picked}>
    {#snippet view(item)}
      {#if page === 'ideas'}
        <Ideas rest={[item.id.slice(0, item.id.indexOf('/')), item.id.slice(item.id.indexOf('/') + 1)]} />
      {:else if page === 'projects'}
        <Projects rest={[item.id]} onlaunch={onlaunch ?? (() => undefined)} />
      {:else if page === 'context'}
        <Context rest={item.id.startsWith('spec:') ? [slug ?? '', 'specialist', item.id.slice(5)] : [slug ?? '', item.id.slice('section:'.length)]} />
      {:else if page === 'settings'}
        <Settings rest={[item.id]} />
      {:else if page === 'memory'}
        <Memory {slug} scope={item.id.slice(0, item.id.indexOf('/'))} name={item.id.slice(item.id.indexOf('/') + 1)} />
      {:else}
        <div use:block={item.id}><Spend period={days} grouping={by} /></div>
      {/if}
    {/snippet}
  </WorkDesk>
</div>

{#if forgetting}
  <ForgetDialog project={forgetting} onclose={() => (forgetting = null)} />
{/if}
