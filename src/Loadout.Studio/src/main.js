import { mount } from 'svelte';
import App from './App.svelte';
import { followSystem } from './lib/theme.js';
import './app.css';

followSystem();

mount(App, { target: document.getElementById('app') });
