// Moves an element to the end of <body>, for overlays: a dialog opened from
// inside a page must not sit inside the page it makes inert.
export function portal(node: HTMLElement) {
  document.body.appendChild(node);

  return {
    destroy() {
      node.remove();
    },
  };
}
