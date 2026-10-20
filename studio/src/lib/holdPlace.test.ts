// Holding your place: whatever is on screen when a redraw starts is the same
// distance from the top when it ends, the focused element first; scrolling
// done by hand in between is kept; and two holds over one redraw correct it
// once, not twice. jsdom lays nothing out, so each element's top is set here.
import { afterEach, describe, expect, it } from 'vitest';
import { holdPlace } from './holdPlace';
import { arrivalIn, keepUnchanged } from './ideas';

const tops = new Map<Element, number>();

function place(element: Element, top: number) {
  tops.set(element, top);
  element.getBoundingClientRect = () => {
    const at = tops.get(element) ?? 0;

    return { top: at, bottom: at + 40, left: 0, right: 0, width: 0, height: 40, x: 0, y: at, toJSON: () => ({}) };
  };
}

function page() {
  document.body.innerHTML = '<section id="work"><h2 data-anchor="zone:questions">Q</h2><section data-anchor="piece:L1">L1</section><section data-anchor="piece:L2"><button>Choose</button></section></section>';
  const [questions, first, second] = [...document.querySelectorAll('[data-anchor]')];

  place(questions, -300);
  place(first, -20);
  place(second, 200);
  place(second.querySelector('button')!, 260);

  return { root: document.getElementById('work')!, questions, first, second, button: second.querySelector('button')! };
}

// Moves everything by the scroll, as a browser would.
function scroller() {
  const fake = { at: 1000 };

  return {
    get scrollTop() {
      return fake.at;
    },
    set scrollTop(to: number) {
      for (const [element, top] of tops) {
        tops.set(element, top - (to - fake.at));
      }

      fake.at = to;
    },
  };
}

afterEach(() => tops.clear());

describe('holdPlace', () => {
  it('keeps the first thing on screen where it was when something above it shrinks', () => {
    const { root, first, second } = page();
    const scroll = scroller();
    const restore = holdPlace(root, scroll);

    // The questions above fold: everything under them rises by 150.
    place(first, -170);
    place(second, 50);
    restore();

    expect(first.getBoundingClientRect().top).toBe(-20);
    expect(second.getBoundingClientRect().top).toBe(200);
    expect(scroll.scrollTop).toBe(850);
  });

  it('holds the focused element rather than the first on screen', () => {
    const { root, first, button } = page();
    const scroll = scroller();

    button.focus();
    const restore = holdPlace(root, scroll);

    // L1 grows by 60: the button moves down, L1's top does not.
    place(button, 320);
    restore();

    expect(button.getBoundingClientRect().top).toBe(260);
    expect(first.getBoundingClientRect().top).toBe(-80);
  });

  it('finds a redrawn anchor by its key', () => {
    const { root, first } = page();
    const scroll = scroller();
    const restore = holdPlace(root, scroll);

    const again = first.cloneNode(true) as Element;
    first.replaceWith(again);
    tops.delete(first);
    place(again, 20);
    restore();

    expect(again.getBoundingClientRect().top).toBe(-20);
  });

  it('keeps scrolling done by hand while the read was out', () => {
    const { root, first } = page();
    const scroll = scroller();
    const restore = holdPlace(root, scroll);

    scroll.scrollTop = 1300;
    // And then the questions above fold by 150.
    place(first, first.getBoundingClientRect().top - 150);
    restore();

    expect(first.getBoundingClientRect().top).toBe(-320);
    expect(scroll.scrollTop).toBe(1150);
  });

  it('corrects once when two holds span the same redraws', () => {
    const { root, first } = page();
    const scroll = scroller();
    const outer = holdPlace(root, scroll);
    const inner = holdPlace(root, scroll);

    place(first, 20);
    inner();
    place(first, first.getBoundingClientRect().top + 30);
    outer();

    expect(first.getBoundingClientRect().top).toBe(-20);
    expect(scroll.scrollTop).toBe(1070);
  });
});

describe('keepUnchanged', () => {
  const was = {
    id: 'i',
    questions: [{ id: 'Q1', answer: null as string | null }, { id: 'Q2', answer: null as string | null }],
    plan: { revision: 1, layers: [{ id: 'L1', chosen: null as string | null }, { id: 'L2', chosen: null as string | null }] },
  };

  it('keeps every part that did not change as the same object', () => {
    const now = JSON.parse(JSON.stringify(was));
    now.plan.layers[1].chosen = 'B';
    const kept = keepUnchanged(was, now);

    expect(kept).toEqual(now);
    expect(kept).not.toBe(was);
    expect(kept.questions).toBe(was.questions);
    expect(kept.plan.layers[0]).toBe(was.plan.layers[0]);
    expect(kept.plan.layers[1]).not.toBe(was.plan.layers[1]);
  });

  it('is the same idea when nothing changed', () => {
    expect(keepUnchanged(was, JSON.parse(JSON.stringify(was)))).toBe(was);
  });

  it('matches by id, so a list that grew keeps what it still holds', () => {
    const now = { ...JSON.parse(JSON.stringify(was)), questions: [{ id: 'Q0', answer: null }, ...JSON.parse(JSON.stringify(was.questions))] };
    const kept = keepUnchanged(was, now);

    expect(kept.questions[1]).toBe(was.questions[0]);
    expect(kept.questions[2]).toBe(was.questions[1]);
  });
});

describe('arrivalIn', () => {
  const was = { questions: [{ id: 'Q1' }], plan: { revision: 2 } };

  it('is new questions when one was not asked before', () => {
    expect(arrivalIn(was, { questions: [{ id: 'Q1' }, { id: 'Q2' }], plan: { revision: 3 } })).toBe('questions');
  });

  it('is a new revision when the revision went up', () => {
    expect(arrivalIn(was, { questions: [{ id: 'Q1' }], plan: { revision: 3 } })).toBe('revision');
    expect(arrivalIn({ questions: [], plan: null }, { questions: [], plan: { revision: 1 } })).toBe('revision');
  });

  it('is nothing for a choice or an answer being recorded', () => {
    expect(arrivalIn(was, { questions: [{ id: 'Q1' }], plan: { revision: 2 } })).toBeNull();
    expect(arrivalIn(was, { questions: [], plan: { revision: 2 } })).toBeNull();
  });
});
