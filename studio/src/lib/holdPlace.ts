// Holding your place while a page redraws: what you are looking at stays the
// same distance from the top of the screen when something above it grows or
// shrinks. The anchor is the element holding focus when it is on screen, or
// else the first marked element that is; after the redraw the page scrolls by
// however far it moved. Scrolling you did yourself in between is kept.

/** What scrolls: the page, or a box inside it. Only its scrollTop is read and set. */
export interface Scroller {
  scrollTop: number;
}

/** Elements worth holding on to carry data-anchor, with a key that names them across a redraw. */
const ANCHORS = '[data-anchor]';

// How far holds have scrolled the page themselves, so one hold does not take
// another's correction for scrolling done by hand.
let corrected = 0;

function scrollerOf(root: HTMLElement): Scroller {
  for (let at = root.parentElement; at; at = at.parentElement) {
    const overflow = getComputedStyle(at).overflowY;

    if ((overflow === 'auto' || overflow === 'scroll') && at.scrollHeight > at.clientHeight) {
      return at;
    }
  }

  return document.scrollingElement ?? document.documentElement;
}

const onScreen = (element: Element) => {
  const box = element.getBoundingClientRect();

  return box.bottom > 0 && box.top < (globalThis.innerHeight || Infinity);
};

/**
 * Notes where the thing you are looking at is, and returns what puts it back
 * there once the page has redrawn: call it after Svelte's tick().
 */
export function holdPlace(root: HTMLElement, scroller: Scroller = scrollerOf(root)): () => void {
  const focused = document.activeElement instanceof HTMLElement && root.contains(document.activeElement) && onScreen(document.activeElement) ? document.activeElement : null;
  const marked = [...root.querySelectorAll<HTMLElement>(ANCHORS)].find(onScreen) ?? null;
  const held = [focused, marked].filter((one): one is HTMLElement => !!one).map((element) => ({ element, key: element.dataset.anchor, top: element.getBoundingClientRect().top }));
  const scrolled = scroller.scrollTop;
  const before = corrected;

  return () => {
    for (const one of held) {
      const element = one.element.isConnected ? one.element : one.key ? root.querySelector<HTMLElement>(`[data-anchor=${JSON.stringify(one.key)}]`) : null;

      if (!element) {
        continue;
      }

      const byHand = scroller.scrollTop - scrolled - (corrected - before);
      const moved = element.getBoundingClientRect().top - one.top + byHand;

      if (Math.abs(moved) >= 1) {
        const from = scroller.scrollTop;

        scroller.scrollTop = from + moved;
        corrected += scroller.scrollTop - from;
      }

      return;
    }
  };
}
