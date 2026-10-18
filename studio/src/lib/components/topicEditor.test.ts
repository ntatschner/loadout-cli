// TopicEditor: nothing to save until something changes; a clash shows what
// is on disk beside the edit with its own lines marked; keeping the edit
// saves it against their version; taking theirs drops the edit.
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { tick } from 'svelte';
import TopicEditor from './TopicEditor.svelte';
import type { Topic } from '../api';

const TOPIC: Topic = { name: 'release-process', scope: 'project', description: 'How a release is cut.', kind: 'Project', written: '2026-10-16T09:00:00Z', version: 'aaaaaaaa1111', text: 'Tag the release.\nPush the tag.\n' };
const THEIRS: Topic = { ...TOPIC, version: 'bbbbbbbb2222', text: 'Tag the release.\nCheck the notes first.\nPush the tag.\n' };

async function settle() {
  for (let i = 0; i < 5; i++) {
    await tick();
    await Promise.resolve();
  }
}

const said = (button: HTMLElement) => button.closest('.ls-action')?.querySelector('.ls-cmd')?.textContent ?? '';

describe('TopicEditor', () => {
  it('keeps Save disabled until something changes, and shows the line it stands for', async () => {
    render(TopicEditor, { project: 'loadout-cli', topic: TOPIC, editable: true, onsave: async () => ({}) });

    const save = screen.getByRole('button', { name: 'Save' });
    expect(save.getAttribute('aria-disabled')).toBe('true');
    expect(said(save)).toBe('nothing changed yet');

    await fireEvent.input(screen.getByLabelText("The topic's text"), { target: { value: 'Tag the release.\n' } });
    const now = screen.getByRole('button', { name: 'Save' });
    expect(now.getAttribute('aria-disabled')).toBeNull();
    expect(said(now)).toBe("$ loadout memory replace release-process --project loadout-cli --file '<path>' --expect aaaaaaaa1111");
    expect(screen.getByText('edited, not saved')).toBeTruthy();
  });

  it('on a clash shows what is on disk, marking the lines only it has, and keeps the edit against their version', async () => {
    const saves: { text: string; version: string }[] = [];
    render(TopicEditor, {
      project: 'loadout-cli',
      topic: TOPIC,
      editable: true,
      onsave: async (text: string, version: string) => {
        saves.push({ text, version });

        return version === TOPIC.version ? { changed: THEIRS } : { saved: { ...THEIRS, version: 'cccccccc3333', text } };
      },
    });

    await fireEvent.input(screen.getByLabelText("The topic's text"), { target: { value: 'Tag the release.\nPush the tag.\nAnnounce it.\n' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await settle();

    expect(screen.getByRole('alert').textContent).toContain('Somebody else changed this topic');
    const only = [...document.querySelectorAll('.disk span.only')].map((line) => line.textContent);
    expect(only).toEqual(['−only on disk: Check the notes first.']);

    const keep = screen.getByRole('button', { name: 'Keep my edit, then save' });
    expect(said(keep)).toContain('--expect bbbbbbbb2222');
    await fireEvent.click(keep);
    await settle();

    expect(saves.map((one) => one.version)).toEqual(['aaaaaaaa1111', 'bbbbbbbb2222']);
    expect(saves[1].text).toBe('Tag the release.\nPush the tag.\nAnnounce it.\n');
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('takes theirs and drops the edit', async () => {
    render(TopicEditor, { project: 'loadout-cli', topic: TOPIC, editable: true, onsave: async () => ({ changed: THEIRS }) });

    await fireEvent.input(screen.getByLabelText("The topic's text"), { target: { value: 'Mine.\n' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await settle();
    await fireEvent.click(screen.getByRole('button', { name: 'Take theirs' }));
    await settle();

    expect((screen.getByLabelText("The topic's text") as HTMLTextAreaElement).value).toBe(THEIRS.text);
    expect(screen.getByRole('button', { name: 'Save' }).getAttribute('aria-disabled')).toBe('true');
  });
});
