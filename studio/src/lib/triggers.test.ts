import { describe, expect, it, vi } from 'vitest';
import { approvalLines, approvalNeeds, type TriggerApproval } from './triggers';

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
