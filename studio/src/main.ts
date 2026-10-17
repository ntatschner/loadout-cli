// Studio's entry. The design system's tokens are imported as they were
// handed over, then the components' and the pages' own styles.
import '../../design/studio/tokens.css';
import './styles/components.css';
import './styles/pages.css';
import './styles/studio.css';
import { mount } from 'svelte';
import App from './App.svelte';

document.body.classList.add('ls');

mount(App, { target: document.getElementById('app')! });
