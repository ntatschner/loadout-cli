<script lang="ts">
  // A remedy a team wrote down, and the two keys it needs to run unattended:
  // this machine's rule for its kind, and trust in this exact script. Trust
  // is a standing agreement, so it is given in a terminal, never here; the
  // requests waiting on you are answered here, once each.
  import Command from './Command.svelte';
  import HaloButton from './HaloButton.svelte';
  import Signal from './Signal.svelte';
  import TeamRules from './TeamRules.svelte';
  import { quoted } from '../commands';

  interface Request {
    id: string;
    node: string;
    run: string;
    problem: string;
    why: string;
    asked?: string | null;
  }

  interface Props {
    team: string;
    name: string;
    kind: string;
    what: string;
    assumes?: string | null;
    proves?: string | null;
    revision: number;
    by?: string | null;
    rule: 'trusted' | 'ask' | 'never';
    trust: 'trusted' | 'not trusted' | 'changed since trusted';
    requests?: Request[];
    onread?: () => void;
    /** Answers a request; resolves false where it was not taken. */
    onanswer?: (id: string, approve: boolean, reason: string) => Promise<boolean | void> | boolean | void;
    disabledReason?: string;
  }

  let { team, name, kind, what, assumes = null, proves = null, revision, by = null, rule, trust, requests = [], onread = undefined, onanswer = undefined, disabledReason = undefined }: Props = $props();

  const RULE: Record<Props['rule'], string> = {
    trusted: 'may run unattended, if this exact script is trusted',
    ask: 'is held for you every time, trusted or not',
    never: 'is refused outright, however trusted the script is',
  };

  let reasons = $state<Record<string, string>>({});
  let answered = $state<Record<string, string>>({});

  const rows = $derived<[string, string][]>([
    ['What it does', what],
    ...(assumes ? [['It assumes', assumes] as [string, string]] : []),
    ...(proves ? [['It proves', proves] as [string, string]] : []),
    ['Revision', `${revision}${by ? ` · registered by run ${by}` : ''}`],
  ]);

  const line = (id: string, verb: 'approve' | 'refuse') => {
    const reason = (reasons[id] ?? '').trim();

    return `$ loadout team remedy requests --team ${team} --${verb} ${id}${reason ? ` --reason ${quoted(reason)}` : ''}`;
  };

  async function answer(request: Request, approve: boolean) {
    const taken = await onanswer?.(request.id, approve, (reasons[request.id] ?? '').trim());

    if (taken !== false) {
      answered[request.id] = approve ? `✓ Approved once. ${request.node} runs it.` : '✓ Refused. The node reports what it needed and why.';
    }
  }
</script>

<article class="ls-remedy" aria-label={name}>
  <div class="top"><h3>{name}</h3><span class="ls-chip">{kind}</span><Signal word={trust} /></div>
  <TeamRules {rows} />
  <p class="rule">This machine’s rule for {kind}: {rule}. A {kind} remedy {RULE[rule]}.</p>
  {#if trust === 'changed since trusted'}
    <p class="ls-note needs">The script has changed since it was trusted, so it is asked about again.</p>
  {/if}
  <div class="ls-answers">
    <HaloButton label="Read the script" size="small" cmd={`$ loadout team remedy show ${name} --team ${team} --script`} onpress={() => onread?.()} />
  </div>
  <Command
    why={trust === 'trusted' ? 'Trust is a standing agreement, made in a terminal on purpose. To take it back:' : 'Trusting it lets it run with nobody watching, so it is agreed in a terminal on purpose:'}
    command={`$ loadout team remedy trust ${name} --team ${team}${trust === 'trusted' ? ' --revoke' : ''}`}
  />
  {#each requests as request (request.id)}
    <section class="ls-gate compact" role="group" aria-label={`Request ${request.id}`}>
      <span class="who">{request.node} · run {request.run} · {request.id}{request.asked ? ` · ${request.asked}` : ''}</span>
      <p class="q">{request.problem}</p>
      <p class="rec">Held rather than run because <b>{request.why}</b>.</p>
      {#if !answered[request.id]}
        <div class="ls-answers">
          <HaloButton label="Let it run" kind="needs" glyph="✓" size="small" cmd={line(request.id, 'approve')} {disabledReason} onpress={() => void answer(request, true)} />
          <HaloButton label="Refuse" glyph="✕" size="small" cmd={line(request.id, 'refuse')} {disabledReason} onpress={() => void answer(request, false)} />
        </div>
        <div class="ls-field wide">
          <label class="lab" for={`remedy-why-${request.id}`}>Your reason</label>
          <input id={`remedy-why-${request.id}`} name={`remedy-why-${request.id}`} class="ls-text" type="text" placeholder="Optional" autocomplete="off" bind:value={reasons[request.id]} />
        </div>
      {/if}
      <p class="ls-said" role="status">{answered[request.id] ?? ''}</p>
    </section>
  {/each}
</article>
