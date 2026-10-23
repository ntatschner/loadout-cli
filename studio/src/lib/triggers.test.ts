import { describe, expect, it, vi } from 'vitest';
import { approvalLines, approvalNeeds, attentionNeeds, type TriggerApproval } from './triggers';

describe('attentionNeeds', () => {
  it('offers resume for a paused rule, release for each rule with stale events, and says when a source stopped', () => {
    const resume = vi.fn();
    const release = vi.fn();
    const items = attentionNeeds(
      [
        { rule: { id: 'quiet-one' }, paused: false },
        { rule: { id: 'capped' }, paused: true },
      ],
      [
        { rule: 'late', subject: 'pr:acme/app#2', agesOut: '2026-10-30T12:00:00Z' },
        { rule: 'late', subject: 'pr:acme/app#1', agesOut: '2026-10-29T12:00:00Z' },
      ],
      { github: { problem: 'GitHub refused the trigger token' } },
      { resume, release },
    );

    expect(items.map((one) => [one.reason, one.goal])).toEqual([
      ['paused', 'capped'],
      ['stale', 'late'],
      ['source', 'GitHub'],
    ]);
    expect(items[0].actions?.[0].cmd).toBe(approvalLines.resume('capped'));
    expect(items[1].text).toContain('2 event(s)');
    expect(items[1].text).toContain('the first about pr:acme/app#1');
    expect(items[2].text).toBe('GitHub refused the trigger token');
    expect(items[2].actions).toBeUndefined();

    items[0].actions?.[0].onpress?.();
    items[1].actions?.[0].onpress?.();
    expect(resume).toHaveBeenCalledWith('capped');
    expect(release).toHaveBeenCalledWith('late');
  });

  it('is empty when nothing is wrong', () => {
    expect(attentionNeeds([{ rule: { id: 'fine' }, paused: false }], [], { github: { problem: null } }, { resume: vi.fn(), release: vi.fn() })).toEqual([]);
  });
});

const one = (over: Partial<TriggerApproval> = {}): TriggerApproval => ({
  id: 'ab12cd34',
  rule: 'fix-pulls',
  event: 'e1',
  kind: 'pr-updated',
  subject: 'pr:acme/app#4',
  title: 'A fix',
  url: 'https://github.com/acme/app/pull/4',
  trust: 'trusted',
  because: 'association OWNER',
  budgetUsd: 3,
  asked: '2026-10-23T12:00:00Z',
  expires: '2026-10-24T12:00:00Z',
  approved: false,
  then: 'would start at once',
  ...over,
});

describe('approvalNeeds', () => {
  it('says what it would act on and spend, and offers approve and reject with the lines they run', () => {
    const approve = vi.fn();
    const reject = vi.fn();
    const [item] = approvalNeeds([one()], { approve, reject });

    expect(item.reason).toBe('approval');
    expect(item.goal).toBe('A fix (pr:acme/app#4)');
    expect(item.href).toBe('https://github.com/acme/app/pull/4');
    expect(item.text).toContain('would set aside $3.00, and would start at once');
    expect(item.text).not.toContain('outside');
    expect(item.actions?.map((act) => [act.label, act.cmd])).toEqual([
      ['Approve', approvalLines.approve('ab12cd34')],
      ['Reject', approvalLines.reject('ab12cd34')],
    ]);

    item.actions?.[0].onpress?.();
    item.actions?.[1].onpress?.();
    expect(approve).toHaveBeenCalledWith('ab12cd34');
    expect(reject).toHaveBeenCalledWith('ab12cd34');
  });

  it('names an outside author, and offers only reject once it is approved and waiting for headroom', () => {
    const [outside] = approvalNeeds([one({ trust: 'outside', because: 'association NONE' })], { approve: vi.fn(), reject: vi.fn() });
    expect(outside.text).toContain('outside the collaborators (association NONE)');

    const [held] = approvalNeeds([one({ approved: true })], { approve: vi.fn(), reject: vi.fn() });
    expect(held.text).toContain('Approved, and waiting for fix-pulls to have headroom');
    expect(held.actions?.map((act) => act.label)).toEqual(['Reject']);
  });

  it('greys both buttons with the reason when Studio is only watching', () => {
    const [item] = approvalNeeds([one()], { approve: vi.fn(), reject: vi.fn(), disabledReason: 'only watching' });

    expect(item.actions?.every((act) => act.disabledReason === 'only watching')).toBe(true);
  });
});
