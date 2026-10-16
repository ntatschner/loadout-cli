/* @ds-bundle: {"format": 4, "namespace": "LoadoutStudio", "components": [{"name": "HaloButton"}, {"name": "StartRail"}, {"name": "Command"}, {"name": "Switch"}, {"name": "Tabs"}, {"name": "ModePicker"}, {"name": "Signal"}, {"name": "EventMark"}, {"name": "Urgency"}, {"name": "Meter"}, {"name": "Toasts"}, {"name": "SideRail"}, {"name": "Dialog"}, {"name": "Picker"}, {"name": "CommandPalette"}, {"name": "Halo"}, {"name": "TubeMap"}, {"name": "LineStrip"}, {"name": "NetworkStatus"}]} */
/* Loadout Studio — plain JavaScript, no framework, no imports, no network.
   Components are CSS classes plus small helpers, written to port line for line to Svelte. */
(function () {
  "use strict";
  var THEMES = ["dark", "light", "hc-dark", "hc-light"];
  var ACCENTS = ["cyan", "ice", "magenta", "violet"];
  var DENSITIES = ["compact", "comfortable", "roomy"];
  var MOTIONS = ["full", "reduced", "none"];

  function pick(list, v, fallback) { return list.indexOf(v) >= 0 ? v : fallback; }
  function root(el) { return el || document.documentElement; }

  /** Set the theme on <html>, or on a panel. */
  function setTheme(theme, el) { root(el).setAttribute("data-theme", pick(THEMES, theme, "dark")); }
  /** Set the accent; cyan is the default. */
  function setAccent(accent, el) { root(el).setAttribute("data-accent", pick(ACCENTS, accent, "cyan")); }
  /** Set the density; comfortable is the default. */
  function setDensity(density, el) { root(el).setAttribute("data-density", pick(DENSITIES, density, "comfortable")); }
  /** Set the page's motion level. The OS reduced-motion preference still wins: see motion(). */
  function setMotion(level, el) { root(el).setAttribute("data-motion", pick(MOTIONS, level, "full")); }

  /** The motion level actually in force: the page's setting, lowered to "reduced" when the OS asks for it. */
  function motion(el) {
    var set = pick(MOTIONS, root(el).getAttribute("data-motion"), "full");
    var os = typeof window !== "undefined" && window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    return os && set === "full" ? "reduced" : set;
  }

  /** The theme to start with when the person has not picked one: follows the OS for light/dark and for more contrast. */
  function preferredTheme() {
    var mm = typeof window !== "undefined" && window.matchMedia ? function (q) { return window.matchMedia(q).matches; } : function () { return false; };
    var light = mm("(prefers-color-scheme: light)"), more = mm("(prefers-contrast: more)");
    return more ? (light ? "hc-light" : "hc-dark") : (light ? "light" : "dark");
  }

  window.LoadoutStudio = Object.assign(window.LoadoutStudio || {}, {
    THEMES: THEMES, ACCENTS: ACCENTS, DENSITIES: DENSITIES, MOTIONS: MOTIONS,
    setTheme: setTheme, setAccent: setAccent, setDensity: setDensity, setMotion: setMotion,
    motion: motion, preferredTheme: preferredTheme
  });
})();

(function () {
  "use strict";
  var NS = window.LoadoutStudio = window.LoadoutStudio || {};
  var GLYPHS = {"play": "M7 5 L15 10 L7 15 Z", "round": "M15.5 7 A6 6 0 1 0 16 11.5 M15.5 3 V7 H11.5", "needs": "M10 4 V11.5 M10 15 V15.2", "failed": "M6 6 L14 14 M14 6 L6 14", "blocked": "M4 10a6 6 0 1 0 12 0a6 6 0 1 0-12 0 M6.5 10 H13.5", "limited": "M4 14 A6 6 0 0 1 16 14 M10 14 L6.8 10.6", "done": "M5 10.5 L8.5 14 L15 6.5", "idle": "M5 10 H15", "open": "M5 10a5 5 0 1 0 10 0a5 5 0 1 0-10 0", "held": "M7.5 5.5 V14.5 M12.5 5.5 V14.5", "stopped": "M6 6 H14 V14 H6 Z", "unknown": "M7.5 7.5 A2.5 2.5 0 1 1 10 10 V11.5 M10 14.8 V15", "dropped": "M5 15 L15 5", "shaping": "M10 3 L16 10 L10 17 L4 10 Z"};
  var STATES = {"working": ["moving", "play", "breathe"], "running": ["moving", "play", "breathe"], "doing": ["moving", "play", "breathe"], "round running": ["moving", "round", "spin"], "needs a decision": ["needs", "needs", "pulse"], "waiting for you": ["needs", "needs", "pulse"], "failed": ["bad", "failed", "hard"], "last round failed": ["bad", "failed", "hard"], "blocked": ["bad", "blocked", "hard"], "limited": ["bad", "limited", "hard"], "done": ["good", "done", "settle"], "met": ["good", "done", "settle"], "accepted": ["good", "done", "settle"], "idle": ["rest", "idle", "dim"], "open": ["rest", "open", "still"], "captured": ["rest", "open", "still"], "shaping": ["rest", "shaping", "still"], "held": ["rest", "held", "still"], "held after this round": ["rest", "held", "still"], "stopped": ["rest", "stopped", "still"], "not attempted": ["rest", "open", "dashed"], "unknown": ["rest", "unknown", "flicker"], "dropped": ["rest", "dropped", "strike"]};
  var SVGNS = "http://www.w3.org/2000/svg";

  function h(tag, attrs, kids) {
    var el = document.createElement(tag);
    Object.keys(attrs || {}).forEach(function (k) {
      var v = attrs[k];
      if (v === null || v === undefined || v === false) { return; }
      if (k === "text") { el.textContent = v; } else if (k === "class") { el.className = v; } else if (k.slice(0, 2) === "on") { el.addEventListener(k.slice(2), v); } else { el.setAttribute(k, v === true ? "" : v); }
    });
    (kids || []).forEach(function (c) { if (c) { el.appendChild(typeof c === "string" ? document.createTextNode(c) : c); } });
    return el;
  }
  function cmd(text) { return text ? h("code", { "class": "ls-cmd", text: text }) : null; }

  /* HaloButton: a word beside a ring of light. kind: "" | "needs" | "danger"; size: "" | "small" | "big". */
  function HaloButton(o) {
    o = o || {};
    var cls = ["ls-halo", o.kind, o.size, o.solid ? "solid" : ""].filter(Boolean).join(" ");
    var ring = h("span", { "class": "ring", "aria-hidden": "true" }, o.glyph ? [h("b", { text: o.glyph })] : []);
    var btn = h("button", { type: "button", "class": cls, "aria-disabled": o.disabledReason ? "true" : null, "aria-describedby": o.describedBy || null,
      onclick: function (e) { if (!o.disabledReason && o.onPress) { o.onPress(e); } } }, [ring, o.label || ""]);
    if (!o.cmd && !o.disabledReason) { return btn; }
    return h("div", { "class": "ls-action" }, [btn, cmd(o.cmd || o.disabledReason)]);
  }

  /* Command: the block form of a command, with an optional line saying why it must be run in a terminal. */
  function Command(o) {
    o = o || {};
    return h("code", { "class": "ls-cmdline" }, [o.why ? h("span", { "class": "why", text: o.why }) : null, o.command || ""]);
  }

  /* StartRail: the accent tab, the input, Deploy, and the command it will run, updated as you type. */
  function StartRail(o) {
    o = o || {};
    var id = o.id || "ls-start-" + Math.random().toString(36).slice(2, 7);
    var input = h("input", { id: id, value: o.value || "", placeholder: o.placeholder || "Describe the task in a sentence", autocomplete: "off" });
    var line = h("code", { "class": "ls-cmd" });
    function upd() { line.textContent = (o.command || function (t) { return "$ loadout --task \"" + t + "\""; })(input.value); }
    input.addEventListener("input", upd);
    input.addEventListener("keydown", function (e) { if (e.key === "Enter") { go(); } });
    function go() { if (input.value.trim() && o.onDeploy) { o.onDeploy(input.value.trim()); } }
    var root = h("div", { "class": "ls-start" }, [
      h("label", { "for": id, "class": "tab", text: o.label || "What should it do?" }),
      h("div", { "class": "field" }, [input, HaloButton({ label: o.action || "Deploy", onPress: go })]),
      line]);
    upd();
    return root;
  }

  /* Switch: role=switch, On or Off written beside the light. */
  function Switch(o) {
    o = o || {};
    var on = !!o.checked;
    var word = h("span", { "class": "state", text: on ? "On" : "Off" });
    var btn = h("button", { type: "button", role: "switch", "class": "ls-switch", "aria-checked": on ? "true" : "false" }, [
      h("span", { "class": "lbl", text: o.label || "" }), o.sub ? h("span", { "class": "sub", text: o.sub }) : null,
      h("span", { "class": "track", "aria-hidden": "true" }, [h("span", { "class": "pill" }, [h("span", { "class": "knob" })]), word])]);
    btn.addEventListener("click", function () { on = !on; btn.setAttribute("aria-checked", on ? "true" : "false"); word.textContent = on ? "On" : "Off"; if (o.onChange) { o.onChange(on); } });
    return btn;
  }

  /* roving: arrow keys move between the items of a tablist or radiogroup, and pick as they go. */
  function roving(items, pick) {
    items.forEach(function (el, i) {
      el.addEventListener("keydown", function (e) {
        var d = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key];
        if (e.key === "Home") { d = -i; } if (e.key === "End") { d = items.length - 1 - i; }
        if (d === undefined) { return; }
        e.preventDefault(); var n = (i + d + items.length) % items.length; pick(n); items[n].focus();
      });
    });
  }

  /* Tabs: the line of light slides to the tab you are on. tabs: [{id, label}]; onSelect(index). Returns {el, panel, select}. */
  function Tabs(o) {
    o = o || {};
    var base = o.id || "ls-tabs-" + Math.random().toString(36).slice(2, 7), sel = o.selected || 0;
    var panel = h("div", { role: "tabpanel", id: base + "-panel", tabindex: "0" });
    var btns = (o.tabs || []).map(function (t, i) {
      return h("button", { type: "button", role: "tab", id: base + "-" + i, "aria-controls": base + "-panel", text: t.label, onclick: function () { select(i); } });
    });
    function select(i) {
      sel = i;
      btns.forEach(function (b, k) { b.setAttribute("aria-selected", k === i ? "true" : "false"); b.tabIndex = k === i ? 0 : -1; });
      panel.setAttribute("aria-labelledby", base + "-" + i);
      if (o.onSelect) { o.onSelect(i, panel); }
    }
    roving(btns, select);
    var list = h("div", { "class": "ls-tabs", role: "tablist", "aria-label": o.label || null }, btns);
    select(sel);
    return { el: h("div", {}, [list, panel]), panel: panel, select: select };
  }

  /* ModePicker: rings as radios, a name and what it means; the command below follows the pick. */
  function ModePicker(o) {
    o = o || {};
    var sel = o.selected || 0, line = h("code", { "class": "ls-cmd" });
    var btns = (o.options || []).map(function (m, i) {
      return h("button", { type: "button", role: "radio", "class": "ls-choice", onclick: function () { select(i); } },
        [h("span", { "class": "ring", "aria-hidden": "true" }), h("b", { text: m.name }), h("span", { text: m.desc || "" })]);
    });
    function select(i) {
      sel = i;
      btns.forEach(function (b, k) { b.setAttribute("aria-checked", k === i ? "true" : "false"); b.tabIndex = k === i ? 0 : -1; });
      if (o.command) { line.textContent = o.command(o.options[i].name); }
      if (o.onChange) { o.onChange(o.options[i].name, i); }
    }
    roving(btns, select);
    var group = h("div", { "class": "ls-choices", role: "radiogroup", "aria-label": o.label || null }, btns);
    select(sel);
    return h("div", {}, [group, o.command ? h("div", { style: "margin-top: 10px" }, [line]) : null]);
  }

  /* Signal: a state word becomes its family, glyph and motion. The motion is a class; CSS drops loops under reduced or no motion. */
  function signalOf(word) {
    var d = STATES[word] || ["rest", "open", "still"];
    return { word: word, family: d[0], glyph: GLYPHS[d[1]], motion: d[2] };
  }
  function Signal(word, o) {
    o = o || {};
    var s = signalOf(word);
    var svg = document.createElementNS(SVGNS, "svg"); svg.setAttribute("viewBox", "0 0 20 20");
    var p = document.createElementNS(SVGNS, "path");
    p.setAttribute("d", s.glyph); p.setAttribute("fill", "none"); p.setAttribute("stroke", "currentColor"); p.setAttribute("stroke-width", "2"); p.setAttribute("stroke-linecap", "round"); p.setAttribute("stroke-linejoin", "round");
    svg.appendChild(p);
    return h("span", { "class": ["ls-sig", s.family, "m-" + s.motion, o.big ? "big" : ""].filter(Boolean).join(" ") },
      [h("span", { "class": "ic", "aria-hidden": "true" }, [svg]), h("b", { text: word })]);
  }
  /* The state word for a piece of work, as the boards derive it. idleAfter: minutes quiet before a working session reads idle. */
  function wordFor(it, idleAfter) {
    if (it.waiting) { return it.kind === "team run" ? "needs a decision" : "waiting for you"; }
    if (it.blocked) { return "blocked"; }
    if (it.failed) { return "last round failed"; }
    if (it.held) { return "held"; }
    if (it.stage === 4) { return it.kind === "idea" ? "accepted" : "done"; }
    if (it.stage === 2) {
      if (it.kind === "session") { return (it.quiet || 0) >= (idleAfter || 10) ? "idle" : "working"; }
      return it.kind === "team run" ? "running" : (it.kind === "task" ? "doing" : "round running");
    }
    if (it.stage === 1) { return "shaping"; }
    return it.kind === "task" ? "open" : "captured";
  }

  /* EventMark: what just happened, with its age; stays until the item is looked at. */
  var MARK_GLYPH = { needs: "?", bad: "✕", good: "✓", moving: "↻", rest: "·" };
  function EventMark(o) {
    o = o || {};
    return h("span", { "class": "ls-mark " + (o.family || "moving") }, [h("i", { "aria-hidden": "true", text: MARK_GLYPH[o.family || "moving"] }), o.text || "", h("span", { "class": "age", text: o.age || "just now" })]);
  }
  /* ping: one expanding ring at x, y inside a positioned container. Nothing under reduced or no motion. */
  function ping(container, x, y, family) {
    if (NS.motion && NS.motion() !== "full") { return; }
    var r = h("span", { "class": "ls-ping", "aria-hidden": "true", style: "left:" + x + "px;top:" + y + "px;--c:var(--" + (family || "moving") + ")" });
    container.appendChild(r); setTimeout(function () { r.remove(); }, 1500);
  }

  /* Urgency: rank waiting items (a problem first, then the oldest question) and raise the first; the rest recede. */
  function rankUrgent(items) {
    return items.filter(function (i) { return i.waiting || i.problem; }).sort(function (a, b) {
      if (!!a.problem !== !!b.problem) { return a.problem ? -1 : 1; } return (a.askedAt || 0) - (b.askedAt || 0);
    });
  }
  function applyUrgency(rows, risingIndex, level) {
    rows.forEach(function (r, i) {
      r.classList.toggle("ls-rise", i === risingIndex); r.classList.toggle("problem", i === risingIndex && level === "problem");
      r.classList.toggle("ls-recede", risingIndex >= 0 && i !== risingIndex);
    });
  }

  /* Meter: value against a threshold you set; past it, the fill turns to Needs and the words say so. */
  function Meter(o) {
    o = o || {};
    var fill = h("i", { "class": "fill" }), tick = h("u", { "class": "tick" }), tl = h("span", { "class": "tl" });
    var b = h("b"), span = h("span"), em = h("em");
    var track = h("div", { "class": "track", role: "meter", "aria-label": o.label || "Meter", "aria-valuemin": "0" }, [fill, tick, tl]);
    var el = h("div", { "class": "ls-meter" }, [h("div", { "class": "top" }, [b, span, em]), track, o.source ? h("span", { "class": "src", text: o.source }) : null]);
    function set(v) {
      var max = o.max || 100, pct = Math.max(0, Math.min(100, v / max * 100)), over = o.threshold !== undefined && v >= o.threshold;
      fill.style.width = pct.toFixed(1) + "%";
      el.classList.toggle("over", over);
      b.textContent = (o.format || String)(v) + (o.unit ? " " + o.unit : "");
      span.textContent = o.of ? o.of(v) : "";
      em.textContent = over ? (o.overWords || "past your threshold") : "";
      track.setAttribute("aria-valuemax", String(max)); track.setAttribute("aria-valuenow", String(v));
      track.setAttribute("aria-valuetext", b.textContent + (over ? ", " + em.textContent : ""));
      if (o.threshold !== undefined) { var t = (o.threshold / max * 100).toFixed(1) + "%"; tick.style.left = t; tl.style.left = t; tl.textContent = o.thresholdLabel || "threshold"; }
      else { tick.style.display = "none"; tl.style.display = "none"; }
    }
    set(o.value || 0);
    return { el: el, set: set };
  }
  /* ProgressRing: a ring that fills for stages. */
  function ProgressRing(o) {
    o = o || {};
    var C = 2 * Math.PI * 48;
    var wrap = h("div", { "class": "ls-progress" });
    wrap.innerHTML = '<svg viewBox="0 0 120 120" aria-hidden="true"><circle class="bg" cx="60" cy="60" r="48"/><circle class="fg" cx="60" cy="60" r="48" transform="rotate(-90 60 60)"/></svg>';
    var pct = h("span", { "class": "pct", "aria-hidden": "true" }); wrap.appendChild(pct);
    function set(f) { wrap.querySelector(".fg").style.strokeDasharray = (C * f).toFixed(1) + " " + C.toFixed(1); pct.textContent = Math.round(f * 100) + "%"; }
    set(o.value || 0);
    return { el: wrap, set: set };
  }

  /* Toasts: a region top right. Done and For your information fold into a chip after dur-toast-fold; Needs you and Problem
     stay open until answered or acknowledged. Nothing folds while the pointer or keyboard is on the stack. */
  var LEVELS = { done: ["✓", "Done"], info: ["↻", "For your information"], needs: ["!", "Needs you"], problem: ["✕", "Problem"] };
  function Toasts(region) {
    var list = [], seq = 0, now = 0, hold = false;
    var foldAfter = Math.round((parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--dur-toast-fold")) || 6000) / 1000);
    var polite = h("p", { role: "status", "class": "ls-sr" }), alert = h("p", { role: "alert", "class": "ls-sr" });
    region.classList.add("ls-toasts"); region.setAttribute("role", "region"); region.setAttribute("aria-label", "Notifications");
    region.parentNode.insertBefore(polite, region.nextSibling); region.parentNode.insertBefore(alert, polite.nextSibling);
    region.addEventListener("mouseenter", function () { hold = true; }); region.addEventListener("mouseleave", function () { hold = false; });
    region.addEventListener("focusin", function () { hold = true; }); region.addEventListener("focusout", function () { hold = false; });
    var timer = setInterval(tick, 1000);
    function anim() { return NS.motion && NS.motion() === "full" ? " anim" : ""; }
    function add(t) {
      t = Object.assign({ level: "info", text: "", cmd: "", actions: [] }, t, { id: ++seq, born: now, folded: false, settled: null });
      list.unshift(t);
      if (t.level === "problem") { alert.textContent = "Problem: " + t.text; } else { polite.textContent = LEVELS[t.level][1] + ": " + t.text; }
      render(t.id); return t.id;
    }
    function settle(id, words) {
      list.forEach(function (t) { if (t.id === id) { t.settled = now; t.text = words || t.text; t.actions = []; } });
      polite.textContent = words || ""; render();
    }
    function tick() {
      if (hold) { return; }
      now += 1;
      list.forEach(function (t) {
        var age = now - t.born, sAge = t.settled === null ? -1 : now - t.settled;
        if ((t.level === "done" || t.level === "info") ? age >= foldAfter : sAge >= foldAfter) { t.folded = true; }
      });
      list = list.filter(function (t) {
        if (t.level === "done") { return now - t.born < 60; }
        if (t.level === "needs" || t.level === "problem") { return t.settled === null || now - t.settled < 60; }
        return true;
      });
      var infos = list.filter(function (t) { return t.level === "info" && t.folded; });
      if (infos.length > 3) { var drop = infos.slice(3).map(function (t) { return t.id; }); list = list.filter(function (t) { return drop.indexOf(t.id) < 0; }); }
      render();
    }
    function render(fresh) {
      region.textContent = "";
      list.forEach(function (t) {
        var L = LEVELS[t.settled !== null ? "done" : t.level], lvl = t.settled !== null ? "done" : t.level, a = t.id === fresh ? anim() : "";
        if (t.folded) {
          region.appendChild(h("button", { type: "button", "class": "ls-toast-chip " + lvl + a, "aria-label": L[1] + ": " + t.text + ". Open it",
            onclick: function () { t.folded = false; t.born = now; if (t.settled !== null) { t.settled = now; } render(); } },
            [h("span", { "class": "gl", "aria-hidden": "true", text: L[0] }), h("b", { text: L[1] }), h("span", { text: t.text })]));
          return;
        }
        region.appendChild(h("div", { "class": "ls-toast " + lvl + a }, [
          h("div", { "class": "th" }, [h("span", { "class": "gl", "aria-hidden": "true", text: L[0] }), h("span", { "class": "lv", text: L[1] }),
            h("button", { type: "button", "class": "x", "aria-label": "Dismiss: " + t.text, text: "✕", onclick: function () { list = list.filter(function (x) { return x.id !== t.id; }); render(); } })]),
          h("p", { text: t.text }), t.cmd ? cmd(t.cmd) : null,
          t.actions.length ? h("div", { "class": "acts" }, t.actions.map(function (ac) {
            return HaloButton({ label: ac.label, kind: ac.kind, size: "small", cmd: ac.cmd, onPress: function () { settle(t.id, ac.onPress ? ac.onPress() : ac.label); } });
          })) : null]));
      });
    }
    return { add: add, settle: settle, stop: function () { clearInterval(timer); } };
  }

  Object.assign(NS, {
    HaloButton: HaloButton, Command: Command, StartRail: StartRail, Switch: Switch, Tabs: Tabs, ModePicker: ModePicker,
    Signal: Signal, signalOf: signalOf, wordFor: wordFor, SIGNAL_STATES: STATES, SIGNAL_GLYPHS: GLYPHS,
    EventMark: EventMark, ping: ping, rankUrgent: rankUrgent, applyUrgency: applyUrgency,
    Meter: Meter, ProgressRing: ProgressRing, Toasts: Toasts, roving: roving
  });
})();

(function () {
  "use strict";
  var NS = window.LoadoutStudio = window.LoadoutStudio || {};
  var ICONS = {"home": "M3 10 L10 3 L17 10 M5 8.5 V17 H15 V8.5", "sessions": "M3 4.5 H17 V15.5 H3 Z M6 8.5 L8.5 10.5 L6 12.5 M10 12.5 H14", "ideas": "M10 2 L15 10 L10 18 L5 10 Z", "teams": "M3.5 7a2 2 0 1 0 4 0a2 2 0 1 0-4 0 M12.5 7a2 2 0 1 0 4 0a2 2 0 1 0-4 0 M8 14.5a2 2 0 1 0 4 0a2 2 0 1 0-4 0 M7 8.5 L9 12.5 M13 8.5 L11 12.5", "memory": "M4 4.5 H16 M4 8.5 H16 M4 12.5 H16 M4 16.5 H11", "context": "M10 3 L17 7 L10 11 L3 7 Z M3 10.5 L10 14.5 L17 10.5 M3 14 L10 18 L17 14", "spend": "M10 2 V18 M14 6 C13 4 7 4 7 7 C7 10 13 9.5 13 13 C13 16 7 16.5 6 14", "projects": "M3 5.5 H8 L10 7.5 H17 V16 H3 Z", "search": "M8.5 3a5.5 5.5 0 1 0 0 11a5.5 5.5 0 1 0 0-11 M12.6 12.6 L17 17", "settings": "M7 10a3 3 0 1 0 6 0a3 3 0 1 0-6 0 M10 2 V5 M10 15 V18 M2 10 H5 M15 10 H18 M4.3 4.3 L6.4 6.4 M13.6 13.6 L15.7 15.7 M15.7 4.3 L13.6 6.4 M6.4 13.6 L4.3 15.7"};
  var SVGNS = "http://www.w3.org/2000/svg";
  function h(tag, attrs, kids) {
    var el = document.createElement(tag);
    Object.keys(attrs || {}).forEach(function (k) {
      var v = attrs[k];
      if (v === null || v === undefined || v === false) { return; }
      if (k === "text") { el.textContent = v; } else if (k === "class") { el.className = v; } else if (k.slice(0, 2) === "on") { el.addEventListener(k.slice(2), v); } else { el.setAttribute(k, v === true ? "" : v); }
    });
    (kids || []).forEach(function (c) { if (c) { el.appendChild(typeof c === "string" ? document.createTextNode(c) : c); } });
    return el;
  }
  function s(tag, attrs) { var el = document.createElementNS(SVGNS, tag); Object.keys(attrs || {}).forEach(function (k) { el.setAttribute(k, attrs[k]); }); return el; }
  function icon(d) { var v = s("svg", { viewBox: "0 0 20 20", "aria-hidden": "true" }); v.appendChild(s("path", { d: d, fill: "none", stroke: "currentColor", "stroke-width": "1.6", "stroke-linecap": "round", "stroke-linejoin": "round" })); return v; }
  function full() { return !NS.motion || NS.motion() === "full"; }
  function focusables(root) { return Array.prototype.filter.call(root.querySelectorAll("button, [href], input, textarea, select, [tabindex='0']"), function (el) { return !el.disabled && el.offsetParent !== null; }); }
  function trap(root, close) {
    root.addEventListener("keydown", function (e) {
      if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); close(); return; }
      if (e.key !== "Tab") { return; }
      var list = focusables(root); if (!list.length) { return; }
      var first = list[0], last = list[list.length - 1];
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); } else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    });
  }

  /* SideRail: icons with live counts; widens over the page with a dim on hover or focus; below 600px a Pages tab slides it in. */
  function SideRail(o) {
    o = o || {};
    var root = h("div", { "class": "ls-nav" + (o.narrow ? " narrow" : "") });
    function item(p, inSlide) {
      var cur = p.key === o.current;
      return h("a", { href: p.href || "#", "class": "ls-rl" + (p.need ? " need" : ""), "aria-current": cur ? "page" : null },
        [icon(ICONS[p.key] || ICONS.home), h("span", { "class": "n", text: p.name }), h("span", { "class": "f", text: p.fact || "" }), h("span", { "class": "needdot", "aria-hidden": "true", text: "!" })]);
    }
    function search() {
      return h("button", { type: "button", "class": "ls-rl search", "aria-keyshortcuts": "Control+K Meta+K", onclick: function () { if (o.onSearch) { o.onSearch(); } } },
        [icon(ICONS.search), h("span", { "class": "n", text: "Search" }), h("span", { "class": "f", text: "Ctrl K · go to or do" })]);
    }
    var pages = o.pages || [];
    var nav = h("nav", { "class": "ls-railnav", "aria-label": o.label || "Studio" }, [h("div", { "class": "mark" }, [h("i", { "aria-hidden": "true" }), h("b", { text: "LOADOUT" })]), search()].concat(pages.map(function (p) { return item(p); })));
    if (o.contained) { nav.style.position = "absolute"; }
    var tabId = "ls-pages-" + Math.random().toString(36).slice(2, 7);
    var tab = h("button", { type: "button", id: tabId, "class": "ls-pagestab", "aria-expanded": "false", text: "Pages" });
    var layer = null;
    function close() { if (!layer) { return; } layer.forEach(function (n) { n.remove(); }); layer = null; tab.setAttribute("aria-expanded", "false"); tab.focus(); }
    function open() {
      var scrim = h("div", { "class": "ls-slidescrim", onclick: close });
      var closeBtn = h("button", { type: "button", "class": "close", onclick: close }, [h("span", { "aria-hidden": "true", text: "×" }), "Close"]);
      var slide = h("nav", { "class": "ls-slide", "aria-label": o.label || "Studio" }, [closeBtn, search()].concat(pages.map(function (p) { return item(p, true); })));
      slide.addEventListener("keydown", function (e) { if (e.key === "Escape") { e.stopPropagation(); close(); } });
      root.appendChild(scrim); root.appendChild(slide); layer = [scrim, slide];
      tab.setAttribute("aria-expanded", "true"); setTimeout(function () { closeBtn.focus(); }, 30);
    }
    tab.addEventListener("click", function () { if (layer) { close(); } else { open(); } });
    root.appendChild(nav); root.appendChild(tab);
    return root;
  }

  /* Dialog: a projected panel over a dim. Focus is held inside; Escape and the scrim close it; focus returns to what opened it. */
  function Dialog(o) {
    o = o || {};
    var id = "ls-dlg-" + Math.random().toString(36).slice(2, 7), back = null, scrim, panel, host = o.host || document.body;
    function close() {
      if (!panel) { return; } scrim.remove(); panel.remove(); panel = null;
      if (o.behind) { o.behind.classList.remove("is-back"); }
      if (back && back.focus) { back.focus(); } if (o.onClose) { o.onClose(); }
    }
    function open() {
      back = document.activeElement;
      scrim = h("div", { "class": "ls-scrim", onclick: close });
      var acts = (o.actions || []).map(function (a) { return NS.HaloButton(Object.assign({}, a, { onPress: function () { var keep = a.onPress && a.onPress(); if (keep !== true) { close(); } } })); });
      panel = h("div", { "class": "ls-dialog" + (o.danger ? " danger" : "") + (full() ? " anim" : " fade") + (o.extraClass ? " " + o.extraClass : ""), role: o.role || "dialog", "aria-modal": "true", "aria-labelledby": o.title ? id : null, "aria-label": o.title ? null : (o.label || null) },
        [o.title ? h("h2", { id: id, text: o.title }) : null, o.why ? h("p", { "class": "why", text: o.why }) : null, o.content || null,
         acts.length ? h("div", { "class": "acts" }, acts.concat([NS.HaloButton({ label: o.cancelLabel || "Cancel", size: "small", onPress: close })])) : null]);
      if (o.contained) { scrim.style.position = "absolute"; panel.style.position = "absolute"; }
      host.appendChild(scrim); host.appendChild(panel);
      if (o.behind) { o.behind.classList.add("ls-behind", "is-back"); }
      trap(panel, close);
      setTimeout(function () { var list = focusables(panel), f = o.initialFocus ? panel.querySelector(o.initialFocus) : (o.danger ? list[list.length - 1] : list[0]); if (f) { f.focus(); } }, 60);
      return panel;
    }
    return { open: open, close: close, get panel() { return panel; } };
  }

  /* Picker: a trigger that opens a listbox on a light tether. Arrow keys, Home, End, Enter and Escape. */
  function Picker(o) {
    o = o || {};
    var id = "ls-pk-" + Math.random().toString(36).slice(2, 7), sel = o.selected || 0, active = sel, pop = null;
    var opts = o.options || [];
    var name = h("span", { text: opts[sel] ? opts[sel].name : "" }), sub = h("span", { "class": "s", text: opts[sel] ? (opts[sel].sub || "") : "" });
    var trig = h("button", { type: "button", id: id + "-t", "class": "ls-picker", "aria-haspopup": "listbox", "aria-expanded": "false", "aria-labelledby": id + "-l " + id + "-t" }, [name, sub, h("span", { "class": "chev", "aria-hidden": "true", text: "▾" })]);
    var anchor = h("div", { "class": "ls-anchor" }, [trig]);
    var field = h("div", { "class": "ls-field" }, [h("span", { "class": "lab", id: id + "-l", text: o.label || "" }), anchor]);
    function close(refocus) { if (!pop) { return; } pop.remove(); pop = null; trig.setAttribute("aria-expanded", "false"); if (refocus !== false) { trig.focus(); } document.removeEventListener("mousedown", outside); }
    function outside(e) { if (!anchor.contains(e.target)) { close(false); } }
    function choose(i) { sel = i; name.textContent = opts[i].name; sub.textContent = opts[i].sub || ""; close(); if (o.onPick) { o.onPick(opts[i], i); } }
    function paint() { Array.prototype.forEach.call(pop.children, function (c, i) { c.classList.toggle("is-active", i === active); c.setAttribute("aria-selected", i === sel ? "true" : "false"); }); pop.setAttribute("aria-activedescendant", id + "-o" + active); }
    function open() {
      active = sel;
      pop = h("div", { "class": "ls-pop" + (full() ? " anim" : ""), role: "listbox", tabindex: "-1", "aria-labelledby": id + "-l" }, opts.map(function (op, i) {
        return h("div", { role: "option", id: id + "-o" + i, "class": "ls-opt", onclick: function () { choose(i); }, onmousemove: function () { if (active !== i) { active = i; paint(); } } },
          [h("span", { "class": "g", "aria-hidden": "true", text: op.glyph || (i === sel ? "✓" : "") }), h("span", { "class": "t", text: op.name }), op.sub ? h("span", { "class": "s", text: op.sub }) : null]);
      }));
      pop.addEventListener("keydown", function (e) {
        var n = opts.length, d = { ArrowDown: 1, ArrowUp: -1 }[e.key];
        if (e.key === "Escape" || e.key === "Tab") { if (e.key === "Escape") { e.preventDefault(); } close(e.key === "Escape"); return; }
        if (e.key === "Home") { e.preventDefault(); active = 0; } else if (e.key === "End") { e.preventDefault(); active = n - 1; }
        else if (e.key === "Enter" || e.key === " ") { e.preventDefault(); choose(active); return; }
        else if (d) { e.preventDefault(); active = (active + d + n) % n; } else { return; }
        paint();
      });
      anchor.appendChild(pop); trig.setAttribute("aria-expanded", "true"); paint(); pop.focus();
      document.addEventListener("mousedown", outside);
    }
    trig.addEventListener("click", function () { if (pop) { close(); } else { open(); } });
    trig.addEventListener("keydown", function (e) { if ((e.key === "ArrowDown" || e.key === "ArrowUp") && !pop) { e.preventDefault(); open(); } });
    return field;
  }

  /* CommandPalette: Ctrl or Cmd K. Go to a page, find work in flight, or do something; every action says its command. */
  function matches(q, text) { if (!q) { return true; } var t = text.toLowerCase(); return q.toLowerCase().split(/\s+/).filter(Boolean).every(function (w) { return t.indexOf(w) >= 0; }); }
  function CommandPalette(o) {
    o = o || {};
    var dlg = null, q = "", active = 0, flat = [], input, list, id = "ls-pal-" + Math.random().toString(36).slice(2, 7);
    function rows() {
      var level = function (r) { return !q ? 1 : (matches(q, r.label) ? 2 : (matches(q, r.label + " " + (r.sub || "") + " " + (r.kind || "")) ? 1 : 0)); };
      var groups = (o.groups || []).map(function (g) {
        var items = g.items.map(function (r) { return Object.assign({ lvl: level(r) }, r); }).filter(function (r) { return r.lvl > 0; }).sort(function (a, b) { return b.lvl - a.lvl; });
        return { name: g.name, items: items, best: items.length ? items[0].lvl : 0 };
      }).filter(function (g) { return g.items.length; });
      if (q) { groups.sort(function (a, b) { return b.best - a.best; }); }
      return groups;
    }
    function paint() {
      var groups = rows(); flat = []; list.textContent = "";
      if (!groups.length) { list.appendChild(h("p", { "class": "ls-pempty", text: "Nothing matches “" + q + "”. Try a page name, a project, or a word from a session." })); input.removeAttribute("aria-activedescendant"); return; }
      active = Math.min(active, 0 + Math.max(0, groups.reduce(function (n, g) { return n + g.items.length; }, 0) - 1));
      groups.forEach(function (g, gi) {
        var gid = id + "-g" + gi;
        var grp = h("div", { "class": "ls-pgroup", role: "group", "aria-labelledby": gid }, [h("h3", { id: gid, text: g.name })]);
        g.items.forEach(function (r) {
          var i = flat.length; flat.push(r);
          grp.appendChild(h("div", { role: "option", id: id + "-o" + i, "class": "ls-popt" + (r.need ? " need" : ""), "aria-selected": i === active ? "true" : "false",
            onclick: function () { run(r); }, onmousemove: function () { if (active !== i) { active = i; paint(); } } },
            [h("span", { "class": "g", "aria-hidden": "true", text: r.glyph || "→" }), h("span", { "class": "t", text: r.label }), h("span", { "class": "s", text: r.sub || "" }), h("span", { "class": "k", text: r.kind || "" })]));
        });
        list.appendChild(grp);
      });
      input.setAttribute("aria-activedescendant", id + "-o" + active);
      var a = document.getElementById(id + "-o" + active); if (a && a.scrollIntoView) { a.scrollIntoView({ block: "nearest" }); }
    }
    function run(r) { close(); if (r.run) { r.run(r); } }
    function close() { if (dlg) { dlg.close(); dlg = null; } }
    function open() {
      if (dlg) { return; }
      q = ""; active = 0;
      input = h("input", { role: "combobox", "aria-expanded": "true", "aria-controls": id + "-list", "aria-autocomplete": "list", "aria-label": o.placeholder || "Go to a page, find work, or do something", placeholder: o.placeholder || "Go to a page, find work, or do something", autocomplete: "off" });
      list = h("div", { id: id + "-list", "class": "ls-plist", role: "listbox", "aria-label": "Results" });
      input.addEventListener("input", function () { q = input.value; active = 0; paint(); });
      input.addEventListener("keydown", function (e) {
        var n = flat.length, d = { ArrowDown: 1, ArrowUp: -1 }[e.key];
        if (e.key === "Enter") { e.preventDefault(); if (flat[active]) { run(flat[active]); } return; }
        if (e.key === "Home" && e.ctrlKey) { e.preventDefault(); active = 0; paint(); return; }
        if (d && n) { e.preventDefault(); active = (active + d + n) % n; paint(); }
      });
      var field = h("div", { "class": "ls-start" }, [h("div", { "class": "field" }, [input, h("span", { "class": "ls-kbd", "aria-hidden": "true", text: "Esc" })])]);
      var foot = h("div", { "class": "ls-pfoot", "aria-hidden": "true" }, [h("span", { text: "↑ ↓ to move" }), h("span", { text: "Enter to go or do" }), h("span", { text: "Esc to close" })]);
      var content = h("div", {}, [field, list, foot]);
      dlg = Dialog({ label: "Command palette", content: content, extraClass: "pal", host: o.host, contained: o.contained, behind: o.behind, onClose: function () { dlg = null; }, initialFocus: "input" });
      dlg.open(); paint();
    }
    function onKey(e) { if ((e.ctrlKey || e.metaKey) && (e.key === "k" || e.key === "K")) { e.preventDefault(); if (dlg) { close(); } else { open(); } } }
    if (o.bindKeys !== false) { window.addEventListener("keydown", onKey); }
    return { open: open, close: close, unbind: function () { window.removeEventListener("keydown", onKey); } };
  }

  /* Halo: the Lens focus. Orbit is five stage rings with a body orbiting the one the work is on; Gyroscope is three tilted rings for stage, the hour and spend. */
  var STAGE_NAMES = ["Captured", "Shaping", "Running", "Needs you", "Done"];
  var STAGE_INK = ["var(--rest)", "var(--shaping)", "var(--moving)", "var(--needs)", "var(--good)"];
  var RINGS = [180, 196, 212, 228, 244];
  function glowOf(c, px) { return "drop-shadow(0 0 " + px + "px color-mix(in srgb, " + c + " calc(var(--glow-on) * 100%), transparent))"; }
  function arc(r, fromDeg, lenDeg, color, width, cx, extra) {
    var c = 2 * Math.PI * r, o = cx || 270;
    return "stroke: " + color + "; stroke-width: " + width + "; stroke-dasharray: " + (c * lenDeg / 360).toFixed(1) + " " + c.toFixed(1) + "; transform: rotate(" + (fromDeg - 90).toFixed(1) + "deg); transform-origin: " + o + "px " + o + "px" + (extra || "");
  }
  function polar(c, r, deg) { var t = deg * Math.PI / 180; return { x: c + r * Math.sin(t), y: c - r * Math.cos(t) }; }
  function Halo(o) {
    o = o || {};
    var wrap = h("div", { "class": "ls-halo-wrap" });
    var state = Object.assign({ style: "orbit", stage: 2, hour: 0.3, spend: null, names: STAGE_NAMES }, o);
    function render() {
      wrap.textContent = "";
      var stg = Math.max(0, Math.min(4, state.stage | 0)), ink = STAGE_INK[stg];
      wrap.appendChild(h("p", { "class": "ls-sr", text: "Stage " + (stg + 1) + " of 5: " + state.names[stg] + "." }));
      if (state.style === "gyroscope") { gyro(stg); } else { orbit(stg, ink); }
      if (state.orbiters && state.orbiters.length) { orbiters(); }
    }
    function orbit(stg, ink) {
      var svg = s("svg", { viewBox: "0 0 540 540", "aria-hidden": "true" });
      RINGS.forEach(function (rr, k) {
        var cur = k === stg, lit = k <= stg;
        svg.appendChild(s("circle", { cx: 270, cy: 270, r: rr, fill: "none", "class": "trk", style: "stroke: " + (cur ? ink : "color-mix(in srgb, var(--accent) " + (lit ? 38 : 14) + "%, transparent)") + "; stroke-width: " + (cur ? 2.5 : 1) + (cur ? "; filter: " + glowOf(ink, 6) : "") }));
      });
      for (var k = 0; k < stg; k++) { var p = polar(270, RINGS[k], 200 + k * 30); svg.appendChild(s("circle", { cx: p.x.toFixed(1), cy: p.y.toFixed(1), r: 5, style: "fill: " + STAGE_INK[k] + "; filter: " + glowOf(STAGE_INK[k], 5) })); }
      var r = RINGS[stg], g = s("g", { "class": "spin" });
      g.appendChild(s("circle", { cx: 270, cy: 270, r: r, fill: "none", style: arc(r, -70, 70, ink, 3, 270, "; opacity: .25") }));
      g.appendChild(s("circle", { cx: 270, cy: 270, r: r, fill: "none", style: arc(r, -24, 24, ink, 4, 270, "; opacity: .6; filter: " + glowOf(ink, 6)) }));
      g.appendChild(s("circle", { cx: 270, cy: 270, r: 10, "class": "obody", style: "fill: var(--ink-strong); stroke: " + ink + "; stroke-width: 4; transform: translateY(-" + r + "px); filter: " + glowOf(ink, 12) }));
      svg.appendChild(g); wrap.appendChild(svg);
      state.names.forEach(function (n, k) { wrap.appendChild(h("span", { "class": "olabel" + (k === stg ? " cur" : (k < stg ? " lit" : "")), "aria-hidden": "true", text: n, style: "left: 50%; top: " + ((270 - RINGS[k]) / 5.4).toFixed(2) + "%" })); });
      wrap.appendChild(h("div", { "class": "core" }, [state.core || null]));
    }
    function ringBox(size, tilt) { return "width: " + size + "px; height: " + size + "px; margin: -" + size / 2 + "px 0 0 -" + size / 2 + "px; transform: " + tilt; }
    function gyro(stg) {
      var flat = NS.motion && NS.motion() === "none";
      var g = h("div", { "class": "gyro" }), inner = h("div", { "class": "gyro-in" });
      var A = h("div", { "class": "gring", style: ringBox(500, flat ? "none" : "rotateX(66deg)") }), sa = s("svg", { viewBox: "0 0 500 500", width: 500, height: 500, "aria-hidden": "true", "class": "gspin" });
      sa.appendChild(s("circle", { cx: 250, cy: 250, r: 244, fill: "none", "stroke-width": 10, style: "stroke: color-mix(in srgb, var(--accent) 16%, transparent)" }));
      state.names.forEach(function (x, k) { var lit = k <= stg, cur = k === stg; if (!lit) { return; }
        sa.appendChild(s("circle", { cx: 250, cy: 250, r: 244, fill: "none", style: arc(244, -144 + k * 72 + 3, 66, STAGE_INK[k], cur ? 12 : 8, 250, "; opacity: " + (cur ? 1 : 0.6) + "; filter: " + glowOf(STAGE_INK[k], cur ? 10 : 4)) })); });
      A.appendChild(sa);
      var hb = polar(220, 214, (state.hour || 0) * 360);
      var B = h("div", { "class": "gring", style: ringBox(440, flat ? "none" : "rotateY(64deg) rotateX(12deg)") }), sb = s("svg", { viewBox: "0 0 440 440", width: 440, height: 440, "aria-hidden": "true", "class": "gspin", style: "animation-duration: 36s; animation-direction: reverse" });
      sb.appendChild(s("circle", { cx: 220, cy: 220, r: 214, fill: "none", "stroke-width": 2, "stroke-dasharray": "3 6", style: "stroke: color-mix(in srgb, var(--accent) 30%, transparent)" }));
      sb.appendChild(s("circle", { cx: 220, cy: 220, r: 214, fill: "none", style: arc(214, 0, Math.max(1, (state.hour || 0) * 360), "var(--accent)", 4, 220, "; filter: " + glowOf("var(--accent)", 6)) }));
      sb.appendChild(s("circle", { cx: hb.x.toFixed(1), cy: hb.y.toFixed(1), r: 7, style: "fill: var(--ink-strong); filter: " + glowOf("var(--accent)", 8) }));
      B.appendChild(sb);
      var C = h("div", { "class": "gring", style: ringBox(380, flat ? "none" : "rotateX(-56deg) rotateY(28deg)") }), sc = s("svg", { viewBox: "0 0 380 380", width: 380, height: 380, "aria-hidden": "true", "class": "gspin", style: "animation-duration: 80s" });
      sc.appendChild(s("circle", { cx: 190, cy: 190, r: 184, fill: "none", "stroke-width": 5, style: "stroke: color-mix(in srgb, var(--gauge) 22%, transparent)" }));
      if (state.spend !== null && state.spend !== undefined) {
        var sf = Math.max(0, Math.min(1, state.spend)), cb = polar(190, 184, sf * 360);
        sc.appendChild(s("circle", { cx: 190, cy: 190, r: 184, fill: "none", style: arc(184, 0, Math.max(1, sf * 360), "var(--gauge)", 5, 190, "; filter: " + glowOf("var(--gauge)", 6)) }));
        sc.appendChild(s("circle", { cx: cb.x.toFixed(1), cy: cb.y.toFixed(1), r: 7, style: "fill: var(--ink-strong); filter: " + glowOf("var(--gauge)", 8) }));
      }
      C.appendChild(sc);
      inner.appendChild(A); inner.appendChild(B); inner.appendChild(C);
      inner.appendChild(h("div", { "class": "core" }, [state.core || null]));
      g.appendChild(inner); wrap.appendChild(g);
      if (!flat && state.tilt !== false) {
        wrap.onmousemove = function (e) { if (!full()) { return; } var b = wrap.getBoundingClientRect(), px = (e.clientX - b.left) / b.width - 0.5, py = (e.clientY - b.top) / b.height - 0.5; inner.style.transform = "rotateX(" + (-py * 18).toFixed(1) + "deg) rotateY(" + (px * 26).toFixed(1) + "deg)"; };
        wrap.onmouseleave = function () { inner.style.transform = ""; };
      }
    }
    function orbiters() {
      var R = state.orbitRadius || 330, ring = h("div", { "class": "orbiters" }), n = state.orbiters.length;
      state.orbiters.forEach(function (it, k) {
        var a = -60 + k * 360 / n, fam = NS.signalOf ? NS.signalOf(it.word).family : "rest";
        var slot = h("div", { style: "transform: rotate(" + a.toFixed(1) + "deg) translateY(-" + R + "px) rotate(" + (-a).toFixed(1) + "deg)" });
        slot.appendChild(h("button", { type: "button", "class": "orbiter", style: "--c: var(--" + fam + ")", "aria-label": "Bring into the halo: " + it.title + ", " + it.word, onclick: function () { if (it.onPick) { it.onPick(it); } } },
          [h("i", { "aria-hidden": "true" }), h("span", { "class": "ot", text: it.title }), h("span", { "class": "ow", text: it.word })]));
        ring.appendChild(slot);
      });
      wrap.appendChild(ring);
    }
    render();
    return { el: wrap, set: function (patch) { Object.assign(state, patch || {}); render(); } };
  }

  /* TubeMap: every piece of work is a line in its identity colour; stations are stages; Needs you is the interchange. */
  var X = [360, 540, 720, 900, 1080], X0 = 326, X1 = 1180, LANE = 92, TOPY = 64;
  var PROFILE = { "1": [0, -18, -18, 0, 0], "2": [12, 12, -12, 0, 12], "3": [-14, 0, 16, 0, 0], "4": [0, 14, 0, 0, -14], "5": [16, 0, 0, 0, 16], "6": [-16, -16, 0, 0, -16], "7": [0, 16, 0, 0, 0], "8": [10, -10, 10, 0, -10] };
  var EVG = { asked: "?", answered: "✓", round: "↻", failed: "✕", started: "▶", ended: "■", blocked: "‖", report: "↩" };
  function route(code, y) {
    var off = PROFILE[code] || [0, 0, 0, 0, 0], pts = [[X0, y + off[0]]];
    for (var k = 0; k < 5; k++) {
      pts.push([X[k], y + off[k]]);
      if (k < 4 && off[k + 1] !== off[k]) { var mid = (X[k] + X[k + 1]) / 2, hh = Math.abs(off[k + 1] - off[k]) / 2; pts.push([mid - hh, y + off[k]]); pts.push([mid + hh, y + off[k + 1]]); }
    }
    pts.push([X1, y + off[4]]); return pts;
  }
  function pathOf(pts) { return pts.map(function (p, i) { return (i ? "L" : "M") + p[0].toFixed(1) + " " + p[1].toFixed(1); }).join(" "); }
  function lenTo(pts, x) { var len = 0; for (var i = 1; i < pts.length; i++) { var a = pts[i - 1], b = pts[i], seg = Math.hypot(b[0] - a[0], b[1] - a[1]); if (b[0] >= x) { var f = b[0] === a[0] ? 1 : (x - a[0]) / (b[0] - a[0]); return len + seg * Math.max(0, Math.min(1, f)); } len += seg; } return len; }
  function yAt(pts, x) { for (var i = 1; i < pts.length; i++) { var a = pts[i - 1], b = pts[i]; if (b[0] >= x) { var f = b[0] === a[0] ? 1 : (x - a[0]) / (b[0] - a[0]); return a[1] + (b[1] - a[1]) * f; } } return pts[pts.length - 1][1]; }
  function stageOf(l) { return l.waiting ? 3 : (l.stage | 0); }
  function lineVar(code) { return "var(--line-" + (((parseInt(code, 10) || 7) - 1) % 8 + 1) + ")"; }
  function TubeMap(o) {
    o = o || {};
    var wrap = h("div", { "class": "ls-tube", role: "group", "aria-label": o.label || "Map" });
    var state = { lines: o.lines || [], picked: o.picked };
    function render() {
      wrap.textContent = "";
      var y = TOPY, lanes = state.lines.map(function (l) { var hh = l.workers ? LANE + 66 : LANE, cy = y + 30; y += hh; return { l: l, cy: cy }; });
      var mapH = y + 10;
      var box = h("div", { "class": "box", style: "height: " + mapH + "px" });
      var svg = s("svg", { viewBox: "0 0 1280 " + mapH, width: 1280, height: mapH, "aria-hidden": "true" });
      X.forEach(function (sx, k) { svg.appendChild(s("rect", { x: sx - 90, y: 34, width: 180, height: mapH - 34, style: "fill: " + (k === 3 ? "var(--needs-wash)" : "color-mix(in srgb, var(--accent) " + (k % 2 ? 4.5 : 2) + "%, transparent)") })); });
      if (lanes.length) { var f0 = lanes[0], fl = lanes[lanes.length - 1]; svg.appendChild(s("rect", { x: X[3] - 11, y: f0.cy - 22, width: 22, height: fl.cy - f0.cy + 44, rx: 11, fill: "none", "stroke-width": 2, style: "stroke: var(--ink); filter: " + glowOf("var(--ink)", 6) })); }
      var marks = [];
      lanes.forEach(function (ln) {
        var l = ln.l, col = lineVar(l.code), pts = route(String(l.code), ln.cy), st = stageOf(l), total = lenTo(pts, X1), tx = X[st], tl = lenTo(pts, tx), d = pathOf(pts), picked = l.id === state.picked;
        svg.appendChild(s("path", { d: d, fill: "none", "stroke-width": 8, "stroke-linejoin": "round", "stroke-linecap": "round", style: "stroke: " + col + "; opacity: .55; stroke-dasharray: 14 9" }));
        svg.appendChild(s("path", { d: d, fill: "none", "stroke-width": 8, "stroke-linejoin": "round", "stroke-linecap": "round", "class": "trk", style: "stroke: " + col + "; stroke-dasharray: " + tl.toFixed(1) + " " + (total + 20).toFixed(1) + "; filter: " + glowOf(col, picked ? 9 : 5) }));
        var hit = s("path", { d: d, fill: "none", stroke: "transparent", "stroke-width": 26, style: "cursor: pointer; pointer-events: stroke" });
        hit.addEventListener("click", function () { pick(l.id); }); svg.appendChild(hit);
        X.forEach(function (sx, k) { var yy = yAt(pts, sx);
          if (k === 3) { svg.appendChild(s("circle", { cx: sx, cy: yy.toFixed(1), r: 8, "stroke-width": 3, style: "fill: var(--ground); stroke: var(--ink-strong)" })); return; }
          svg.appendChild(s("line", { x1: sx, y1: (yy - 9).toFixed(1), x2: sx, y2: (k === 4 ? yy + 9 : yy).toFixed(1), "stroke-width": 4, "stroke-linecap": "round", style: "stroke: " + (k <= st ? "var(--ink-strong)" : col) })); });
        (l.checks || []).forEach(function (c, j) { var cx = X[3] + 45 + j * 45, cy = yAt(pts, cx), met = c[1] === "met", un = c[1] === "unmet";
          marks.push(h("span", { "class": "mk", "aria-hidden": "true", text: met ? "✓" : (un ? "✕" : "○"), style: "left: " + cx + "px; top: " + cy.toFixed(1) + "px; background: " + (met ? "var(--good)" : "var(--ground)") + "; border-color: " + (met ? "var(--ground)" : (un ? "var(--bad)" : "var(--muted)")) + "; color: " + (met ? "var(--on-signal)" : (un ? "var(--bad)" : "var(--muted)")) })); });
        (l.workers || []).forEach(function (w, j) { var by = ln.cy + 24 + j * 22, bx = X[2] + 14 + j * 8, ex = bx + 118, busy = !/waiting|reported/.test(w[1]);
          svg.appendChild(s("path", { d: "M" + X[2] + " " + ln.cy + " L" + (X[2] + (by - ln.cy)) + " " + by + " L" + ex + " " + by, fill: "none", "stroke-width": 5, "stroke-linejoin": "round", "stroke-linecap": "round", style: "stroke: " + col + "; opacity: .8" }));
          svg.appendChild(s("circle", { cx: ex, cy: by, r: 6, "stroke-width": 2, style: "fill: " + (busy ? "var(--ink-strong)" : "var(--ground)") + "; stroke: " + col }));
          marks.push(h("span", { "class": "wlabel", "aria-hidden": "true", text: w[0] + " · " + w[1], style: "left: " + (ex + 12) + "px; top: " + by + "px" })); });
        if (l.blocked) { var sy = yAt(pts, tx), sx2 = tx + 22;
          svg.appendChild(s("line", { x1: sx2, y1: sy, x2: sx2, y2: sy - 30, "stroke-width": 2, style: "stroke: var(--ink)" }));
          svg.appendChild(s("circle", { cx: sx2, cy: sy - 30, r: 10, "stroke-width": 2, style: "fill: var(--bad); stroke: var(--ink-strong); filter: " + glowOf("var(--bad)", 8) }));
          svg.appendChild(s("rect", { x: sx2 - 6, y: sy - 32, width: 12, height: 4, style: "fill: var(--ink-strong)" })); }
        (l.events || []).slice(0, 5).forEach(function (ev, j) {
          var same = (l.events || []).slice(0, j).filter(function (q) { return q.stage === ev.stage; }).length, ex2 = X[ev.stage] - 26 - same * 22, ey = yAt(pts, ex2) - 22;
          marks.push(h("span", { "class": "mk", "aria-hidden": "true", text: EVG[ev.kind] || "·", style: "left: " + ex2.toFixed(1) + "px; top: " + ey.toFixed(1) + "px; border-color: " + (ev.kind === "failed" || ev.kind === "blocked" ? "var(--bad)" : (ev.kind === "asked" ? "var(--needs)" : col)) + "; opacity: " + (1 - j * 0.15).toFixed(2) }));
        });
        var tr = h("span", { "class": "train" + (l.waiting ? " wait" : "") + (full() ? " anim-ok" : ""), "aria-hidden": "true", text: String(l.code), style: "--tc: color-mix(in srgb, " + col + " calc(var(--glow-on) * 100%), transparent); background: " + col + "; offset-path: path('" + d + "'); offset-distance: " + tl.toFixed(1) + "px" });
        marks.push(tr);
        var word = NS.wordFor ? NS.wordFor(l) : "";
        var facts = l.blocked ? l.blocked : (l.facts || "");
        var b = h("button", { type: "button", "class": "board", "aria-pressed": picked ? "true" : "false", style: "top: " + (ln.cy - 24) + "px", "aria-label": "Line " + l.code + ", " + (l.kind || "") + ": " + l.title + ", " + word + (facts ? ". " + facts : ""), title: l.title, onclick: function () { pick(l.id); } },
          [h("span", { "class": "ls-badge", style: "--lc: " + col, text: String(l.code) }), h("span", { style: "min-width: 0" }, [h("span", { "class": "t", text: l.title }), NS.Signal ? NS.Signal(word) : null, " ", h("span", { "class": "x", text: facts })])]);
        marks.push(b);
      });
      box.appendChild(svg);
      (o.stations || STAGE_NAMES).forEach(function (nm, k) { box.appendChild(h("span", { "class": "stn" + (k === 3 ? " wait" : ""), "aria-hidden": "true", text: nm, style: "left: " + X[k] + "px" })); });
      marks.forEach(function (m) { box.appendChild(m); });
      wrap.appendChild(box);
    }
    function pick(id) { state.picked = id; render(); if (o.onPick) { o.onPick(id); } var b = wrap.querySelector('.board[aria-pressed="true"]'); if (b) { b.focus(); } }
    render();
    return { el: wrap, set: function (patch) { Object.assign(state, patch || {}); render(); } };
  }

  /* LineStrip: the picked line drawn straight, with what it is, where it is, its last events and what you can do. */
  function LineStrip(o) {
    o = o || {};
    var l = o.line || {}, col = lineVar(l.code), st = stageOf(l), SX = [10, 125, 240, 355, 470], names = o.stations || STAGE_NAMES;
    var word = NS.wordFor ? NS.wordFor(l) : "";
    var who = h("div", { "class": "who" }, [h("span", { "class": "ls-badge", style: "--lc: " + col + "; width: 34px; height: 34px; font-size: 16px", text: String(l.code) }),
      h("h2", { text: l.title }), h("span", { "class": "where", text: (l.kind || "") + (l.where ? " · " + l.where : "") }),
      h("span", { style: "display: block; margin-top: 4px" }, [NS.Signal ? NS.Signal(word) : null]), l.note ? h("span", { "class": "note", text: l.note }) : null]);
    var draw = h("div", { "class": "draw" }), svg = s("svg", { viewBox: "0 0 560 120", "aria-hidden": "true" }), tx = SX[st] + 20;
    svg.appendChild(s("line", { x1: 20, y1: 70, x2: 540, y2: 70, "stroke-width": 10, "stroke-linecap": "round", style: "stroke: " + col + "; opacity: .55; stroke-dasharray: 14 9" }));
    svg.appendChild(s("line", { x1: 20, y1: 70, x2: tx, y2: 70, "stroke-width": 10, "stroke-linecap": "round", "class": "trk", style: "stroke: " + col + "; filter: " + glowOf(col, 6) }));
    SX.forEach(function (x, k) { svg.appendChild(s("line", { x1: x + 20, y1: 58, x2: x + 20, y2: 82, "stroke-width": 3, style: "stroke: var(--ink-strong)" + (k <= st ? "" : "; opacity: .45") })); });
    svg.appendChild(s("circle", { cx: tx, cy: 70, r: 13, "stroke-width": 3, style: "fill: " + col + "; stroke: var(--ground); filter: " + glowOf(col, 10) }));
    draw.appendChild(svg);
    var pct = function (x) { return (x / 5.6).toFixed(2) + "%"; };
    (o.events || []).slice(0, 5).forEach(function (ev, j) {
      var same = (o.events || []).slice(0, j).filter(function (q) { return q.stage === ev.stage; }).length, x = SX[ev.stage] + 20 - 18 - same * 24;
      draw.appendChild(h("span", { "class": "mk" + (ev.kind === "failed" || ev.kind === "blocked" ? " bad" : (ev.kind === "asked" ? " needs" : "")), "aria-hidden": "true", text: EVG[ev.kind] || "·", style: "--lc: " + col + "; left: " + pct(x) + "; top: 25%" }));
    });
    draw.appendChild(h("span", { "class": "code", "aria-hidden": "true", text: String(l.code), style: "left: " + pct(tx) + "; top: 58.3%" }));
    var nm = h("div", { "class": "names", "aria-hidden": "true" }, SX.map(function (x, k) { return h("span", { "class": k === st ? "cur" : "", text: names[k], style: "left: " + pct(x + 20) }); }));
    var line = h("div", { "class": "line" }, [draw, nm,
      (o.events || []).length ? h("ul", { "class": "events", "aria-label": "Last events" }, o.events.slice(0, 5).map(function (ev) { return h("li", {}, [h("b", { text: ev.t || "" }), (EVG[ev.kind] || "·") + " " + ev.text]); })) : null]);
    var side = h("div", { "class": "side" }, [
      (o.facts || []).length ? h("dl", {}, [].concat.apply([], o.facts.map(function (f) { return [h("dt", { text: f.k }), h("dd", { text: f.v })]; }))) : null,
      (o.checks || []).length ? h("ul", { "class": "chk", "aria-label": "Checks" }, o.checks.map(function (c) { return h("li", { "class": c[1] }, [h("i", { "aria-hidden": "true", text: c[1] === "met" ? "✓" : (c[1] === "unmet" ? "✕" : "○") }), c[0] + " · " + c[1]]); })) : null,
      (o.actions || []).length ? h("div", { "class": "ls-answers", style: "margin: 0" }, o.actions.map(function (a) { return NS.HaloButton(Object.assign({ size: "small" }, a)); })) : null]);
    return h("section", { "class": "ls-strip", style: "--lc: " + col, "aria-label": "Line " + l.code + ": " + l.title }, [who, line, side]);
  }

  /* NetworkStatus: how many are at each station, the oldest question, tokens today, and what is disrupted. */
  function NetworkStatus(o) {
    o = o || {};
    var lines = o.lines || [], glyph = ["○", "◇", "▶", "!", "✓"];
    var chips = h("div", { "class": "chips", role: "list", "aria-label": "How many are at each station" }, STAGE_NAMES.map(function (nm, k) {
      return h("span", { role: "listitem", style: "color: " + STAGE_INK[k] }, [glyph[k] + " " + nm, h("b", { text: String(lines.filter(function (l) { return stageOf(l) === k; }).length) })]); }));
    var dis = lines.filter(function (l) { return l.blocked || l.failed; });
    var list = h("ul", { "class": "disrupt", "aria-label": "Disruptions" }, dis.map(function (l) {
      return h("li", {}, [h("span", { "class": "ls-badge", style: "--lc: " + lineVar(l.code), text: String(l.code), "aria-hidden": "true" }), h("span", { text: l.title + " · " + (l.blocked ? "blocked: " + l.blocked : l.failed) })]); }));
    list.appendChild(h("li", {}, [h("span", { "class": "ok", "aria-hidden": "true", text: "✓" }), h("span", { "class": "ok", style: "text-align: left", text: dis.length ? "Good service on every other line" : "Good service on every line" })]));
    return h("section", { "class": "ls-status", "aria-label": o.label || "Network status" }, [
      h("h2", { text: "Network status" }), chips,
      h("span", { "class": "clock", text: o.clock || "" }), h("div", { "class": "chips" }, [h("span", {}, ["Oldest question waiting", h("b", { text: o.oldest || "none" })]), o.tokens ? h("span", {}, ["Tokens today", h("b", { text: o.tokens })]) : null]),
      h("span"), list]);
  }

  Object.assign(NS, { NAV_ICONS: ICONS, SideRail: SideRail, Dialog: Dialog, Picker: Picker, CommandPalette: CommandPalette, Halo: Halo, TubeMap: TubeMap, LineStrip: LineStrip, NetworkStatus: NetworkStatus, STAGE_NAMES: STAGE_NAMES });
})();
