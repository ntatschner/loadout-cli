// Studio's own DOM events, so svelte-check knows their handlers.
import 'svelte/elements';

declare module 'svelte/elements' {
  interface HTMLAttributes<T> {
    /** A terminal given nowhere to send the keyboard on Ctrl+] asks the element around it. */
    onleaveterminal?: (event: CustomEvent) => void;
  }
}
