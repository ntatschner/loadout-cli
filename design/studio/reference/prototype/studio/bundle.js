/* @ds-bundle: {"format": 4, "namespace": "LoadoutStudio", "components": [{"name": "HaloButton"}, {"name": "StartRail"}, {"name": "Command"}, {"name": "Switch"}, {"name": "Tabs"}, {"name": "ModePicker"}, {"name": "Signal"}, {"name": "EventMark"}, {"name": "Urgency"}, {"name": "Meter"}, {"name": "Toasts"}, {"name": "SideRail"}, {"name": "Dialog"}, {"name": "Picker"}, {"name": "CommandPalette"}, {"name": "Halo"}, {"name": "TubeMap"}, {"name": "LineStrip"}, {"name": "NetworkStatus"}, {"name": "Terminal"}, {"name": "SessionView"}, {"name": "Gate"}, {"name": "RunCard"}, {"name": "NeedsYou"}, {"name": "RunShape"}, {"name": "Crew"}, {"name": "DoneWhen"}, {"name": "RunNumbers"}, {"name": "RunDepths"}, {"name": "Patch"}, {"name": "SayToNode"}, {"name": "TeamCard"}, {"name": "TeamNodes"}, {"name": "TeamRules"}, {"name": "RoleRecord"}, {"name": "TeamSheet"}, {"name": "Schedules"}, {"name": "Remedy"}, {"name": "Bin"}, {"name": "Outbox"}]} */
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
  var STATES = {"working": ["moving", "play", "breathe"], "running": ["moving", "play", "breathe"], "doing": ["moving", "play", "breathe"], "round running": ["moving", "round", "spin"], "needs a decision": ["needs", "needs", "pulse"], "waiting for you": ["needs", "needs", "pulse"], "failed": ["bad", "failed", "hard"], "last round failed": ["bad", "failed", "hard"], "blocked": ["bad", "blocked", "hard"], "limited": ["bad", "limited", "hard"], "done": ["good", "done", "settle"], "met": ["good", "done", "settle"], "accepted": ["good", "done", "settle"], "idle": ["rest", "idle", "dim"], "open": ["rest", "open", "still"], "captured": ["rest", "open", "still"], "shaping": ["rest", "shaping", "still"], "held": ["rest", "held", "still"], "held after this round": ["rest", "held", "still"], "stopped": ["rest", "stopped", "still"], "not attempted": ["rest", "open", "dashed"], "unknown": ["rest", "unknown", "flicker"], "dropped": ["rest", "dropped", "strike"], "needs approval": ["needs", "needs", "pulse"], "connecting": ["rest", "open", "dashed"], "reconnecting": ["rest", "round", "dashed"], "ended": ["good", "done", "settle"], "exited with an error": ["bad", "failed", "hard"], "only watching": ["rest", "held", "still"], "lost": ["bad", "blocked", "hard"], "unmet": ["bad", "failed", "hard"], "no verdict": ["rest", "unknown", "still"], "quiet": ["needs", "needs", "pulse"], "projected over": ["needs", "limited", "pulse"], "trusted": ["good", "done", "settle"], "not trusted": ["rest", "open", "still"], "changed since trusted": ["needs", "needs", "pulse"], "scheduled": ["rest", "held", "still"]};
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

(function () {
  "use strict";
  var NS = window.LoadoutStudio = window.LoadoutStudio || {};
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
  function focusables(root) { return Array.prototype.filter.call(root.querySelectorAll("button, [href], input, [tabindex='0']"), function (e) { return !e.disabled && e.offsetParent !== null; }); }

  /* What each terminal state says, and whether you can type. */
  var STATES = {
    connecting: { word: "connecting", typing: false },
    working: { word: "working", typing: true },
    idle: { word: "idle", typing: true },
    unknown: { word: "unknown", typing: true },
    waiting: { word: "waiting for you", typing: true, ask: ["needs", "It has finished and is waiting for your next message.", "Type a reply"] },
    approval: { word: "needs approval", typing: true, ask: ["needs", "It is asking for approval. The choices are in the terminal; answer them there.", "Go to the question"] },
    ended: { word: "ended", typing: false, ask: ["good", "Ended. Its transcript is kept.", null, "resume"] },
    error: { word: "exited with an error", typing: false, ask: ["bad", "Exited with an error (exit code {code}). Its transcript is kept.", null, "resume"] },
    reconnecting: { word: "reconnecting", typing: false, ask: ["rest", "Lost touch with the daemon. Reconnecting; the output so far is kept.", "Try now"] },
    watch: { word: "only watching", typing: false, ask: ["rest", "Only watching: typing is off. Start Studio without --watch-only to type into a session."] },
    lost: { word: "lost", typing: false, ask: ["bad", "This session ended when the daemon restarted. Its transcript is kept.", null, "resume"] }
  };

  /* Terminal: the agent's own screen, in Studio's palette. The real one is xterm.js themed from the term-* tokens; this is its reference look. */
  function Terminal(o) {
    o = o || {};
    var state = o.state || "working", cols = (o.size || [80, 24])[0], rows = (o.size || [80, 24])[1], size = 13, fit = !!o.fit, typed = "";
    var screen = h("pre", { "class": "screen", tabindex: "0", role: "log", "aria-label": "Terminal: " + (o.title || "session"), "aria-roledescription": "terminal" });
    var cursorLine = h("span"), cur = h("span", { "class": "cur", text: " " });
    var sig = h("span"), sizeEl = h("span", { "class": "size", text: cols + " × " + rows });
    var fitBtn = h("button", { type: "button", "class": "tbtn", "aria-pressed": fit ? "true" : "false", text: "Fit", title: "Fit 80 columns to the width", onclick: function () { setFit(!fit); } });
    var smaller = h("button", { type: "button", "class": "tbtn", "aria-label": "Smaller text", text: "A−", onclick: function () { zoom(-1); } });
    var larger = h("button", { type: "button", "class": "tbtn", "aria-label": "Larger text", text: "A+", onclick: function () { zoom(1); } });
    var bar = h("div", { "class": "tbar" }, [sig, h("span", { text: (o.project || "") + (o.agent ? " · " + o.agent : "") }), sizeEl, h("span", { "class": "keyhint", text: "Ctrl+] leaves the terminal" }), h("span", { "class": "grow" }), smaller, larger, fitBtn]);
    var askText = h("p"), askBtnWrap = h("span"), askCmd = h("span");
    var ask = h("div", { "class": "ask", role: "status" }, [askText, askBtnWrap, askCmd]);
    var el = h("section", { "class": "ls-term" + (fit ? " fit" : ""), "aria-label": o.label || "Live terminal" }, [bar, screen, ask]);
    screen.appendChild(cursorLine); cursorLine.appendChild(cur);

    function zoom(d) { size = Math.max(9, Math.min(22, size + d)); el.style.setProperty("--term-size", size + "px"); setFit(false); }
    function setFit(v) { fit = v; el.classList.toggle("fit", fit); fitBtn.setAttribute("aria-pressed", fit ? "true" : "false"); }
    function atBottom() { return screen.scrollHeight - screen.scrollTop - screen.clientHeight < 24; }
    /* write: one line, as runs of [text, classes]: fg-red, fg-bright-black, bold, ul, bg-black. */
    function write(runs) {
      var stick = atBottom(), line = h("span");
      (typeof runs === "string" ? [[runs, ""]] : runs).forEach(function (r) { line.appendChild(h("span", { "class": r[1] || null, text: r[0] })); });
      line.appendChild(document.createTextNode("\n"));
      screen.insertBefore(line, cursorLine);
      if (stick) { screen.scrollTop = screen.scrollHeight; }
    }
    function paintPrompt() { cursorLine.textContent = ""; if (STATES[state].typing && o.prompt !== false) { cursorLine.appendChild(h("span", { "class": "fg-cyan", text: "› " })); cursorLine.appendChild(document.createTextNode(typed)); cursorLine.appendChild(cur); } }
    function setState(s, info) {
      state = STATES[s] ? s : "unknown"; info = info || {};
      var st = STATES[state];
      sig.textContent = ""; if (NS.Signal) { sig.appendChild(NS.Signal(st.word)); }
      el.classList.toggle("off", !st.typing);
      screen.setAttribute("aria-readonly", st.typing ? "false" : "true");
      if (st.ask) {
        ask.hidden = false; ask.className = "ask " + st.ask[0];
        askText.textContent = st.ask[1].replace("{code}", info.code === undefined ? "1" : info.code);
        askBtnWrap.textContent = "";
        if (st.ask[2] && NS.HaloButton) { askBtnWrap.appendChild(NS.HaloButton({ label: st.ask[2], size: "small", kind: st.ask[0] === "needs" ? "needs" : "", onPress: function () { if (s === "reconnecting") { if (o.onRetry) { o.onRetry(); } } else { screen.focus(); screen.scrollTop = screen.scrollHeight; } } })); }
        askCmd.textContent = "";
        if (st.ask[3] === "resume") { askCmd.appendChild(h("code", { "class": "ls-cmd", text: "Carry it on in a terminal: $ loadout resume " + (o.sessionId || "<session-id>") })); }
      } else { ask.hidden = true; }
      paintPrompt();
    }
    screen.addEventListener("keydown", function (e) {
      if (e.ctrlKey && e.key === "]") { e.preventDefault(); var list = focusables(document.body), i = list.indexOf(screen); var next = o.leaveTo || list[i + 1] || list[0]; if (next) { next.focus(); } return; }
      if (!STATES[state].typing) { return; }
      if (e.key === "Tab") { e.preventDefault(); return; }
      if (e.key === "Enter") { e.preventDefault(); var t = typed; typed = ""; write([["› ", "fg-cyan"], [t, ""]]); paintPrompt(); if (o.onInput) { o.onInput(t); } return; }
      if (e.key === "Backspace") { e.preventDefault(); typed = typed.slice(0, -1); paintPrompt(); return; }
      if (e.key.length === 1 && !e.ctrlKey && !e.metaKey) { e.preventDefault(); typed += e.key; paintPrompt(); screen.scrollTop = screen.scrollHeight; }
    });
    (o.lines || []).forEach(write);
    setState(state, o.info);
    return { el: el, write: write, setState: setState, focus: function () { screen.focus(); }, get state() { return state; } };
  }

  /* SessionView: the session page. A slim header, the terminal filling the page, and a side panel that folds away. */
  function SessionView(o) {
    o = o || {};
    var l = o.line || {};
    var termOpts = Object.assign({ title: l.title, project: l.where, agent: l.agent }, o.terminal || {});
    var term = Terminal(termOpts);
    var side = h("aside", { "class": "sside", id: "ls-sside-" + Math.random().toString(36).slice(2, 7), "aria-label": "About this session" });
    function block(title, node) { return node ? h("section", {}, [h("h2", { text: title }), node]) : null; }
    var facts = (o.facts || []).length ? h("dl", {}, [].concat.apply([], o.facts.map(function (f) { return [h("dt", { text: f[0] }), h("dd", { text: f[1] })]; }))) : null;
    var loaded = (o.loaded || []).length ? h("dl", {}, [].concat.apply([], o.loaded.map(function (f) { return [h("dt", { text: f[0] }), h("dd", { text: f[1] })]; }))) : null;
    var events = (o.events || []).length ? h("ul", {}, o.events.map(function (e) { return h("li", {}, [h("b", { text: e[0] }), e[1]]); })) : null;
    var acts = (o.actions || []).length && NS.HaloButton ? h("div", { "class": "ls-answers", style: "margin: 0" }, o.actions.map(function (a) { return NS.HaloButton(Object.assign({ size: "small" }, a)); })) : null;
    [block("Facts", facts), block("This session loaded", loaded), block("Last events", events), block("Actions", acts)].forEach(function (b) { if (b) { side.appendChild(b); } });
    var open = o.details !== false && !(window.matchMedia && window.matchMedia("(max-width: 959px)").matches);
    var toggle = h("button", { type: "button", "class": "ls-halo small", "aria-expanded": open ? "true" : "false", "aria-controls": side.id, onclick: function () { open = !open; side.hidden = !open; toggle.setAttribute("aria-expanded", open ? "true" : "false"); toggle.lastChild.textContent = open ? "Hide details" : "Details"; } },
      [h("span", { "class": "ring", "aria-hidden": "true" }), open ? "Hide details" : "Details"]);
    side.hidden = !open;
    /* Ctrl+] leaves the terminal for the details toggle, never for the next action, which may be End the session. */
    if (!termOpts.leaveTo) { termOpts.leaveTo = toggle; }
    var head = h("div", { "class": "shead" }, [h("div", { "class": "who" }, [h("h1", { text: l.title || "" }), h("span", { "class": "facts1", text: [l.where, l.agent, l.mode, l.time].filter(Boolean).join(" · ") })]), toggle]);
    var root = h("div", { "class": "ls-session" }, [head, term.el, side]);
    return { el: root, terminal: term };
  }

  Object.assign(NS, { Terminal: Terminal, SessionView: SessionView, TERMINAL_STATES: Object.keys(STATES) });
})();

(function () {
  "use strict";
  var NS = window.LoadoutStudio = window.LoadoutStudio || {};
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
  function uid(p) { return p + Math.random().toString(36).slice(2, 7); }
  function cmd(t) { return t ? h("code", { "class": "ls-cmd", text: t }) : null; }
  /* An argument as a shell needs it: quoted when it holds a space, a quote or nothing at all. */
  function q(v) { v = String(v); return v === "" || /[\s"'$`\\]/.test(v) ? "\"" + v.replace(/(["\\$`])/g, "\\$1") + "\"" : v; }
  function money(n) { return "$" + Number(n || 0).toFixed(2); }
  function sig(w, o) { return NS.Signal(w, o); }
  function halo(o) { return NS.HaloButton(o); }
  function field(label, input, hint, wide) { var id = input.id || (input.id = uid("ls-f-")); return h("div", { "class": "ls-field" + (wide ? " wide" : "") }, [h("label", { "class": "lab", "for": id, text: label }), input, hint ? h("span", { "class": "hint", text: hint }) : null]); }
  function text(o) { var el = h(o.lines ? "textarea" : "input", { "class": "ls-text", type: o.lines ? null : (o.type || "text"), rows: o.lines || null, placeholder: o.placeholder || null, autocomplete: "off", spellcheck: o.spell ? "true" : "false", "aria-describedby": o.describedBy || null }); el.value = o.value || ""; if (o.onInput) { el.addEventListener("input", function () { o.onInput(el.value); }); } return el; }
  function zone(t, level) { return h(level || "h3", { "class": "ls-zone", text: t }); }

  /* ---------- Gate: a question a run stopped to ask, answered where it is shown ---------- */
  var GATE_WORDS = { confirm: "asks you to confirm", question: "the lead asks", permission: "asks permission", outward: "asks to reach outside this machine", merge: "asks to merge", brief: "a brief, before it goes out", readings: "before any worker starts", proposal: "what the run will be judged on", budget: "it has spent its budget" };
  function gateLine(o, rest) { return "$ loadout team gate " + (o.run || "<run>") + (o.gate ? " --gate " + o.gate : "") + " " + rest + " --by dashboard"; }
  function Gate(o) {
    o = o || {};
    var kind = o.kind || "question", qid = uid("ls-q-"), reason = "", typed = o.brief || (o.proposal || []).join("\n"), figure = "";
    var answers = h("div", { "class": "ls-answers" }), said = h("p", { "class": "ls-said", role: "status" });
    var el = h("section", { "class": "ls-gate " + kind + (o.compact ? " compact" : ""), role: "group", "aria-labelledby": qid });
    var who = [o.node, GATE_WORDS[kind], o.asked].filter(Boolean).join(" · ");
    el.appendChild(h("span", { "class": "who", text: who }));
    el.appendChild(h("p", { "class": "q", id: qid, text: o.question || "" }));
    var box = null;
    if (kind === "readings") {
      el.appendChild(h("ul", { "class": "readings" }, (o.readings || []).map(function (r) { return h("li", {}, [h("span", { "class": "c", text: "\u201c" + r[0] + "\u201d" }), h("span", { "class": "m", text: "read as: " + r[1] })]); })));
    }
    if (kind === "brief" || kind === "proposal") {
      box = text({ lines: kind === "brief" ? 6 : 4, value: typed, onInput: function (v) { typed = v; paint(); } });
      el.appendChild(field(kind === "brief" ? "The brief " + (o.node || "the worker") + " will be given" : "One criterion per line", box, kind === "brief" ? "Change it and send it, and that is what the worker is given. The journal keeps the lead's words and yours." : "Empty the box and the run carries on with nothing checking it.", true));
      if (kind === "proposal" && (o.held || []).length) { el.appendChild(h("p", { "class": "held" }, [h("b", { text: "Held as well, from the project: " }), o.held.join("; ") + ". They cannot be unticked here."])); }
    }
    if (o.recommendation && kind !== "confirm" && kind !== "permission" && kind !== "outward" && kind !== "merge" && kind !== "brief") { el.appendChild(h("p", { "class": "rec" }, [kind === "budget" ? "Loadout offers first: " : "The lead recommends: ", h("b", { text: o.recommendation }), "."])); }
    if (o.timer) { el.appendChild(h("p", { "class": "timer", text: o.timer })); }
    el.appendChild(answers);
    var extra = null;
    if (kind === "budget") {
      var fig = text({ placeholder: "60, or none", onInput: function (v) { figure = v.trim(); paint(); } });
      extra = field("Or a figure of your own, in US dollars", fig, null);
      fig.parentNode.style.maxWidth = "260px";
    } else if (kind !== "brief" && kind !== "proposal") {
      var r = text({ placeholder: "Optional: why, in a sentence the journal keeps", onInput: function (v) { reason = v.trim(); paint(); } });
      extra = field("Your reason", r, null, true); extra.classList.add("reason");
    }
    if (extra) { el.appendChild(extra); }
    if (kind === "readings") { el.appendChild(h("p", { "class": "ls-note", text: "To say what you meant instead, say something to the lead before you answer: it reads it at the start of the round." })); }
    el.appendChild(said);
    function why() { return reason ? " --reason " + q(reason) : ""; }
    function done(words, answer, extraInfo) {
      el.classList.add("answered"); answers.textContent = ""; if (extra) { extra.remove(); } if (box) { box.readOnly = true; }
      said.textContent = "\u2713 " + words;
      if (o.onAnswer) { o.onAnswer(answer, extraInfo || {}); }
    }
    function paint() {
      answers.textContent = "";
      var dis = o.disabledReason || null, small = o.compact ? "small" : "";
      function add(b) { answers.appendChild(halo(Object.assign({ size: small, disabledReason: dis }, b))); }
      if (kind === "question" || kind === "readings") {
        var opts = kind === "readings" ? ["Accept these readings"] : (o.options || []);
        opts.forEach(function (op, i) { var rec = op === (o.recommendation || (kind === "readings" ? op : null)); add({ label: op, kind: "needs", solid: rec, glyph: opts.length > 1 ? String(i + 1) : "\u2713", cmd: gateLine(o, "--answer " + q(op)) + why(), onPress: function () { done("You answered: " + op + ".", op); } }); });
        add({ label: "Think again", glyph: "\u21bb", cmd: gateLine(o, "--think-again") + why(), onPress: function () { done(kind === "readings" ? "Sent back. No worker is briefed this round; the lead restates its readings and asks again." : "Sent back to the lead to decide itself or ask a better question.", "Think again"); } });
      } else if (kind === "confirm" || kind === "permission" || kind === "outward" || kind === "merge") {
        add({ label: "Yes", kind: "needs", glyph: "\u2713", cmd: gateLine(o, "--answer yes") + why(), onPress: function () { done("You said yes.", "yes"); } });
        add({ label: "No", glyph: "\u2715", cmd: gateLine(o, "--answer no") + why(), onPress: function () { done("You said no. The run is told and goes on without it.", "no"); } });
      } else if (kind === "brief") {
        var changed = typed.trim() !== (o.brief || "").trim();
        add({ label: changed ? "Send your brief" : "Send it", kind: "needs", solid: true, glyph: "\u2713", cmd: gateLine(o, "--answer yes" + (changed ? " --instead " + q(typed.trim()) : "")), onPress: function () { box.readOnly = true; done(changed ? "Sent your brief instead of the lead's." : "Sent the lead's brief.", "yes", { instead: changed ? typed.trim() : null }); } });
        add({ label: "Don't send it", glyph: "\u2715", cmd: gateLine(o, "--answer no"), onPress: function () { done("Not sent. The lead is told.", "no"); } });
      } else if (kind === "proposal") {
        var lines = typed.split("\n").map(function (s) { return s.trim(); }).filter(Boolean), same = lines.join("\n") === (o.proposal || []).join("\n");
        add({ label: lines.length ? "Hold it to these" : "Hold it to nothing", kind: "needs", solid: true, glyph: "\u2713", cmd: gateLine(o, "--answer yes" + (same ? "" : " --instead " + q(lines.join("\n")))), onPress: function () { done(lines.length ? "The run is held to " + lines.length + (lines.length === 1 ? " criterion." : " criteria.") : "The run carries on with nothing checking it.", "yes", { instead: same ? null : lines }); } });
      } else if (kind === "budget") {
        (o.options || []).forEach(function (op, i) { add({ label: op, kind: "needs", solid: op === o.recommendation, glyph: String(i + 1), cmd: gateLine(o, "--answer " + q(op)), onPress: function () { done("You answered: " + op + ".", op); } }); });
        if (figure) { add({ label: /^none$/i.test(figure) ? "No cap" : "Raise it to $" + figure.replace(/^\$/, ""), glyph: "$", cmd: gateLine(o, "--answer " + q(figure)), onPress: function () { done("You set the budget to " + figure + ".", figure); } }); }
      }
    }
    paint();
    return el;
  }

  /* ---------- RunCard: one run in a list, with what it asks answered in place ---------- */
  function Spend(o) {
    var cap = o.uncapped ? null : o.cap, pct = cap ? Math.min(100, o.spent / cap * 100) : 0, over = !!(cap && o.projected && o.projected > cap);
    var words = h("div", { "class": "w" }, [h("b", { text: money(o.spent) }), h("span", { text: cap ? "of " + money(cap) : "no cap, on purpose" }), o.rate ? h("span", { text: "\u00b7 " + o.rate }) : null, over ? h("em", { text: "projected " + money(o.projected) + ", past its budget" }) : null]);
    var bar = cap ? h("div", { "class": "bar", role: "meter", "aria-label": "Spent", "aria-valuemin": "0", "aria-valuemax": String(cap), "aria-valuenow": String(o.spent), "aria-valuetext": money(o.spent) + " of " + money(cap) + (over ? ", projected " + money(o.projected) + ", past its budget" : "") }, [h("i", { style: "width:" + pct.toFixed(1) + "%" }), over ? null : (o.projected ? h("u", { style: "left:" + Math.min(100, o.projected / cap * 100).toFixed(1) + "%" }) : null)]) : null;
    return h("div", { "class": "ls-spend" + (over ? " over" : "") }, [words, bar]);
  }
  function RunCard(o) {
    o = o || {}; var r = o.run || {};
    var fam = NS.signalOf(r.word || "running").family;
    var el = h("article", { "class": "ls-run " + fam, "aria-label": r.goal });
    el.appendChild(h("div", { "class": "top" }, [o.href ? h("a", { "class": "goal", href: o.href, text: r.goal }) : h("span", { "class": "goal", text: r.goal }), sig(r.word || "running")]));
    el.appendChild(h("span", { "class": "facts", text: [r.team, r.project, r.posture, r.round ? "round " + r.round + (r.rounds ? " of " + r.rounds : "") : null, r.elapsed, r.id].filter(Boolean).join(" \u00b7 ") }));
    if (r.spent !== undefined) { el.appendChild(Spend(r)); }
    if ((o.reasons || []).length) { el.appendChild(h("ul", { "class": "why" }, o.reasons.map(function (x) { return h("li", {}, [x.text + " ", h("span", { text: "\u00b7 clears when " + x.clears })]); }))); }
    if (o.gate) { el.appendChild(Gate(Object.assign({ compact: true, run: r.id }, o.gate))); }
    if ((o.actions || []).length) { el.appendChild(h("div", { "class": "acts" }, o.actions.map(function (a) { return halo(Object.assign({ size: "small" }, a)); }))); }
    return el;
  }

  /* ---------- NeedsYou: every reason a run wants you, each clearing itself ---------- */
  var NEED = {
    asked: ["waiting for you", "It stopped and asked you something.", "you answer it"],
    silent: ["needs a decision", "The lead took a round without asking for anything. Another one ends the run.", "it asks for a node, or finishes"],
    quiet: ["quiet", "{node} has said nothing for {for}, three times its usual turn.", "it says anything at all"],
    budget: ["projected over", "It is projected to overrun its budget: {detail}.", "it slows, finishes, or you stop it"],
    spent: ["limited", "It has spent its budget.", "you raise it, or it stops"]
  };
  function NeedsYou(o) {
    o = o || {};
    var items = o.items || [], hid = uid("ls-nh-");
    var el = h("section", { "class": "ls-needs", "aria-labelledby": hid }, [h("h2", { "class": "ls-zone", id: hid }, [h("span", { "class": "no", text: String(items.length) }), "Needs you"])]);
    if (!items.length) { el.appendChild(h("p", { "class": "calm", text: "Nothing needs you. Each reason clears itself, so this only ever holds what is still true." })); return el; }
    el.appendChild(h("ul", {}, items.map(function (it) {
      var n = NEED[it.reason] || NEED.asked;
      var words = (it.text || n[1]).replace("{node}", it.node || "a node").replace("{for}", it.quiet || "a while").replace("{detail}", it.detail || "");
      return h("li", { "class": "need" }, [
        h("div", { "class": "top" }, [h("a", { href: it.href || "#", text: it.goal }), sig(n[0])]),
        it.gate ? null : h("p", { text: words }),
        it.gate ? Gate(Object.assign({ compact: true, run: it.run }, it.gate)) : null,
        h("span", { "class": "clears", text: [it.team, it.run, "clears when " + (it.clears || n[2])].filter(Boolean).join(" \u00b7 ") }),
        (it.actions || []).length ? h("div", { "class": "ls-answers", style: "margin: 0" }, it.actions.map(function (a) { return halo(Object.assign({ size: "small" }, a)); })) : null]);
    })));
    return el;
  }

  /* ---------- RunShape: rounds along the lead's line, the nodes each round briefed hanging from it ---------- */
  function RunShape(o) {
    o = o || {};
    var ol = h("ol", { "aria-label": o.label || "Rounds" });
    (o.rounds || []).forEach(function (r, i) {
      var f = NS.signalOf(r.word || "done").family, cls = r.word === "round running" || r.word === "running" ? "cur" : (f === "needs" ? "wait" : (f === "bad" ? "bad" : ""));
      var br = (r.nodes || []).length ? h("ul", { "class": "br", "aria-label": "Briefed in round " + (i + 1) }, r.nodes.map(function (n) {
        var inner = [h("span", { "class": "n", text: n[0] }), sig(n[1])];
        return h("li", {}, o.onPick ? [h("button", { type: "button", "aria-pressed": o.picked === n[0] ? "true" : "false", onclick: function () { o.onPick(n[0]); } }, inner)] : inner);
      })) : null;
      ol.appendChild(h("li", { "class": "rd " + cls }, [h("span", { "class": "stn", "aria-hidden": "true", text: String(i + 1) }), h("span", { "class": "rl" }, ["Round " + (i + 1), sig(r.word || "done")]), h("p", { "class": "lead", text: r.lead || "" }), br]));
    });
    if (o.next) { ol.appendChild(h("li", { "class": "rd next" }, [h("span", { "class": "stn", "aria-hidden": "true", text: String((o.rounds || []).length + 1) }), h("span", { "class": "rl", text: "Next round" }), h("p", { "class": "lead", text: o.next })])); }
    if (o.end) { var g = NS.signalOf(o.end.word).family; ol.appendChild(h("li", { "class": "rd end " + g }, [h("span", { "class": "stn", "aria-hidden": "true", text: g === "good" ? "\u2713" : "\u25a0" }), h("span", { "class": "rl" }, ["Ended", sig(o.end.word)]), h("p", { "class": "lead", text: o.end.text || "" })])); }
    return h("div", { "class": "ls-shape" }, [ol]);
  }

  /* ---------- Crew: who is on the run, what each is doing, what each has cost ---------- */
  function Crew(o) {
    o = o || {};
    var rows = [];
    var ul = h("ul", { "aria-label": o.label || "Who is on it" }, (o.nodes || []).map(function (n) {
      var inner = [h("span", { "class": "nm" }, [h("b", { text: n.node }), h("span", { text: [n.role, n.model].filter(Boolean).join(" \u00b7 ") })]),
        h("span", { "class": "doing" }, [sig(n.word), n.doing ? " \u00b7 " + n.doing : "", n.quiet ? h("span", { "class": "quiet", text: "Quiet for " + n.quiet }) : null]),
        h("span", { "class": "num" }, [n.turns + (n.turns === 1 ? " turn" : " turns") + " \u00b7 " + money(n.cost), n.refused ? h("span", { "class": "refused", text: " \u00b7 " + n.refused + " refused" }) : null])];
      var row = o.onPick ? h("button", { type: "button", "class": "row", "aria-pressed": o.picked === n.node ? "true" : "false", onclick: function () { rows.forEach(function (x) { x.setAttribute("aria-pressed", x === row ? "true" : "false"); }); o.onPick(n.node); } }, inner) : h("div", { "class": "row" }, inner);
      rows.push(row);
      return h("li", {}, [row]);
    }));
    return h("div", { "class": "ls-crew" }, [ul]);
  }

  /* ---------- DoneWhen: each criterion, how the lead read it, and what became of it ---------- */
  function DoneWhen(o) {
    o = o || {};
    var c = o.criteria || [], met = c.filter(function (x) { return x.verdict === "met"; }).length;
    var el = h("div", { "class": "ls-done" });
    el.appendChild(h("div", { "class": "head" }, [h("b", { text: "Done when " + met + " of " + c.length + " met" }), o.by ? h("span", { text: "Criteria " + o.by }) : null]));
    if (o.goal) { el.appendChild(h("p", { "class": "goalread" }, ["The goal, taken to mean: ", h("i", { text: o.goal })])); }
    if (!c.length) { el.appendChild(h("p", { "class": "goalread", text: "No criteria. Done is the lead's word for it." })); return el; }
    el.appendChild(h("ul", { "class": "crit" }, c.map(function (x) {
      var word = { met: "met", unmet: "unmet", "not-attempted": "not attempted", "not attempted": "not attempted" }[x.verdict] || "no verdict";
      return h("li", {}, [sig(word), h("span", { "class": "t" }, [x.text, x.project ? h("span", { "class": "ls-chip", text: "project" }) : null]),
        x.meaning ? h("span", { "class": "m" }, ["taken to mean: ", h("i", { text: x.meaning })]) : null]
        .concat((x.evidence || []).map(function (e) { return h("span", { "class": "e", text: e }); }))
        .concat((x.delivered || []).map(function (d) { return h("span", { "class": "e", text: "delivered: " + d }); }))
        .concat((x.cited || []).map(function (d) { return h("span", { "class": "e cited", text: "! cited, but no worker reported it: " + d }); })));
    })));
    return el;
  }

  /* ---------- RunNumbers: the numbers that change a decision ---------- */
  function Bars(rows, unit) {
    var max = Math.max.apply(null, rows.map(function (r) { return r[1]; }).concat([1]));
    return h("ul", { "class": "ls-bars" }, rows.map(function (r) { return h("li", {}, [h("span", { "class": "n", text: r[0], title: r[0] }), h("span", { "class": "b", "aria-hidden": "true" }, [h("i", { style: "width:" + (r[1] / max * 100).toFixed(1) + "%" })]), h("span", { "class": "v", text: r[1] + " " + (unit || "min") })]); }));
  }
  function RunNumbers(o) {
    o = o || {};
    var over = !!(o.cap && o.projected > o.cap);
    var cost = h("section", { "class": "cost" + (over ? " over" : ""), "aria-label": "What it is costing" }, [h("h4", { text: "What it is costing" }), h("span", { "class": "big", text: o.rate || "" }),
      h("p", { "class": "proj" }, [over ? sig("projected over") : null, over ? " " : null, (o.projected ? "Projected " + money(o.projected) + " by " + (o.by || "its last round") : "") + (o.cap ? (over ? ", past its " + money(o.cap) + " budget." : ", inside its " + money(o.cap) + " budget.") : ". It has no cap, on purpose.")]),
      h("p", { "class": "ls-note", text: money(o.spent) + " so far \u00b7 projected from rounds, not the clock" })]);
    var time = h("section", { "aria-label": "Where the time went" }, [h("h4", { text: "Where the time went" }), h("p", { "class": "sub", text: "By round" }), Bars(o.byRound || []), h("p", { "class": "sub", text: "By node" }), Bars(o.byNode || [])]);
    var wrong = h("section", { "aria-label": "What went wrong" }, [h("h4", { text: "What went wrong" }), h("ul", { "class": "wrong" }, (o.wrong || []).map(function (w) { return h("li", { "class": w[1] ? "" : "none" }, [w[0], h("b", { text: w[1] ? String(w[1]) : "none" })]); }))]);
    return h("div", { "class": "ls-nums" }, [cost, time, wrong]);
  }

  /* ---------- RunDepths: what it said, what it cost, the papers, what it changed ---------- */
  function Journal(lines) {
    return h("ol", { "class": "log", tabindex: "0", "aria-label": "Journal" }, lines.map(function (l) { return h("li", { "class": (l[2] || "") + (l[4] ? " sub" : "") }, [h("span", { "class": "tm", text: l[0] }), h("span", { "class": "tx" }, [l[3] ? h("span", { "class": "k", text: l[3] }) : null, l[1], l[5] ? h("span", { "class": "x", text: "\u00d7" + l[5] }) : null])]); }));
  }
  function Exchanges(rows) {
    var H = ["Round", "Node", "Model", "Messages", "Refused", "Cost", "Report"];
    return h("table", { "class": "ls-xch" }, [h("caption", { text: "Every exchange" }), h("thead", {}, [h("tr", {}, H.map(function (x, i) { return h("th", { scope: "col", "class": i >= 3 && i <= 5 ? "r" : null, text: x }); }))]),
      h("tbody", {}, rows.map(function (r) { return h("tr", {}, r.map(function (c, i) { return h("td", { "data-h": H[i], "class": (i >= 3 && i <= 5 ? "r" : "") + (i === 6 ? "rep" : "") || null }, [h("span", { text: i === 5 ? money(c) : String(c) })]); })); }))]);
  }
  function Papers(p) {
    return h("div", { "class": "ls-papers" }, [p.summary ? h("p", { "class": "sum", text: p.summary }) : null].concat((p.pairs || []).map(function (x) {
      return h("section", { "class": "pair" }, [h("h4", {}, [x.node, h("span", { text: x.role || "" })]), h("div", { "class": "two" }, [
        h("div", {}, [h("h5", { text: "What it was told" }), h("p", { text: x.brief }), x.instead ? h("p", { "class": "changed", text: "You changed this brief before it went out. The lead wrote: " + x.instead }) : null]),
        h("div", {}, [h("h5", { text: "What it said it did" }), h("p", { text: x.report || "No report yet." })])])]);
    })));
  }
  function RunDepths(o) {
    o = o || {};
    var node = o.node || null, el = h("div", { "class": "ls-depths" });
    var labels = [["1", "What it said"], ["2", "What it cost"], ["3", "The papers"], ["4", "What it changed"], ["5", "What it delivered"]];
    var T = NS.Tabs({ label: "Five depths of this run", selected: o.selected || 0, tabs: labels.map(function (l) { return { label: l[1] }; }), onSelect: function (i, panel) { panel.textContent = ""; paintTab(i, panel); if (o.onSelect) { o.onSelect(i); } } });
    T.el.querySelectorAll("[role=tab]").forEach(function (b, i) { b.insertBefore(h("span", { "class": "k", "aria-hidden": "true", text: labels[i][0] }), b.firstChild); b.setAttribute("aria-keyshortcuts", labels[i][0]); });
    el.appendChild(T.el);
    var current = o.selected || 0;
    function paintTab(i, panel) {
      current = i;
      if (i === 0) {
        var s = o.said || {}, nodes = s.nodes || [];
        var picks = [h("button", { type: "button", "aria-pressed": node ? "false" : "true", text: "The run", onclick: function () { node = null; T.select(0); } })].concat(nodes.map(function (n) { return h("button", { type: "button", "aria-pressed": node === n ? "true" : "false", text: n, onclick: function () { node = n; T.select(0); } }); }));
        panel.appendChild(h("div", { "class": "who", role: "group", "aria-label": "Whose words" }, picks));
        if (!node) { panel.appendChild(Journal(s.journal || [])); panel.appendChild(h("p", { "class": "src" }, ["One line every few seconds, repeats counted. ", cmd("$ loadout team log " + (o.run || "<run>") + " --events")])); }
        else {
          panel.appendChild(Journal((s.streams || {})[node] || [["", "Nothing from " + node + " yet."]]));
          panel.appendChild(h("p", { "class": "src", text: "Everything " + node + " did: every tool it called and everything it said. Sub-agents are marked." }));
          if (s.say) { var say = SayToNode(Object.assign({ run: o.run, node: node }, s.say, { state: (s.live || {})[node] ? s.say.state : "ended" })); say.style.marginTop = "12px"; panel.appendChild(say); }
        }
      } else if (i === 1) {
        var c = o.cost || {};
        if (c.numbers) { panel.appendChild(RunNumbers(c.numbers)); }
        if (c.exchanges) { var wrap = h("div", { style: "margin-top: 16px" }, [Exchanges(c.exchanges)]); panel.appendChild(wrap); }
      } else if (i === 2) { panel.appendChild(Papers(o.papers || {})); }
      else if (i === 3) {
        var ps = (o.changed || {}).patches || [];
        if (!ps.length) { panel.appendChild(h("p", { "class": "ls-note", text: "No node of this run produced a patch." })); }
        ps.forEach(function (p, k) { var x = Patch(Object.assign({ run: o.run }, p)); if (k) { x.style.marginTop = "22px"; } panel.appendChild(x); });
      } else if (i === 4) { panel.appendChild(NS.Outbox(Object.assign({ run: o.run }, o.delivered || { repository: null, files: [] }))); }
    }
    function onKey(e) {
      if (e.ctrlKey || e.metaKey || e.altKey || e.defaultPrevented) { return; }
      var t = e.target, tag = t && t.tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || (t && t.isContentEditable) || (t && t.closest && t.closest(".ls-term, [role=dialog]"))) { return; }
      var n = "12345".indexOf(e.key); if (n < 0) { return; }
      e.preventDefault(); T.select(n); var tab = el.querySelectorAll("[role=tab]")[n]; if (tab) { tab.focus(); }
    }
    if (o.keys !== false) { document.addEventListener("keydown", onKey); }
    return { el: el, select: function (i) { T.select(i); }, showNode: function (n) { node = n; T.select(0); }, unbind: function () { document.removeEventListener("keydown", onKey); } };
  }

  /* ---------- Patch: what a node changed, and opening a pull request for it ---------- */
  function Patch(o) {
    o = o || {};
    var el = h("section", { "class": "ls-patch", "aria-label": "What " + (o.node || "the node") + " changed" });
    el.appendChild(h("div", { "class": "head" }, [h("b", { text: o.node || "" }), o.branch ? h("span", { "class": "ls-mono", text: o.branch }) : null, o.base ? h("span", { "class": "ls-mono", text: "measured from " + o.base }) : null]));
    if (o.unmeasured) { el.appendChild(h("p", { "class": "none", text: "This run was recorded before the commit each branch started at was written down, so its patch cannot be shown. A diff measured from the wrong place would look like one." })); return el; }
    var files = o.files || [], add = 0, del = 0;
    files.forEach(function (f) { add += f[1]; del += f[2]; });
    el.appendChild(h("p", { "class": "ls-note", text: files.length + (files.length === 1 ? " file" : " files") + " \u00b7 " + add + " added \u00b7 " + del + " removed" }));
    el.appendChild(h("ul", { "class": "files" }, files.map(function (f) { return h("li", {}, [h("span", { "class": "a", text: "+" + f[1] }), h("span", { "class": "d", text: "\u2212" + f[2] }), h("span", { "class": "p", text: f[0] })]); })));
    if ((o.diff || []).length) {
      var CL = { "+": "add", "-": "del", "@": "hunk", " ": "ctx" };
      el.appendChild(h("pre", { "class": "diff", tabindex: "0", "aria-label": "The patch" }, o.diff.map(function (d) { return h("span", { "class": CL[d[0]] || "ctx", text: (d[0] === "@" ? "" : (d[0] === "-" ? "\u2212" : d[0])) + d[1] }); })));
    }
    if (o.pr !== false) {
      var said = h("p", { "class": "ls-said", role: "status" });
      el.appendChild(h("div", { "class": "ls-answers", style: "margin-top: 4px" }, [halo({ label: "Open a pull request", size: "small", glyph: "\u2197", disabledReason: o.disabledReason || null, cmd: "$ loadout team pr " + (o.run || "<run>") + " --node " + o.node + " --draft", onPress: function () { openPr(o, said); } })]));
      el.appendChild(said);
    }
    return el;
  }
  function openPr(o, said) {
    var draft = true, title = "";
    var line = h("code", { "class": "ls-cmdline" });
    function command(dry) { return "$ loadout team pr " + (o.run || "<run>") + " --node " + o.node + (draft ? " --draft" : "") + (title ? " --title " + q(title) : "") + (dry ? " --dry-run" : ""); }
    function paint() { line.textContent = command(false); }
    var t = text({ placeholder: o.goal || "The run's goal", onInput: function (v) { title = v.trim(); paint(); } });
    var content = h("div", { style: "display: grid; gap: 14px" }, [
      NS.Switch({ label: "Open it as a draft", sub: "--draft", checked: true, onChange: function (v) { draft = v; paint(); } }),
      field("Its title", t, "The run's goal unless you give one.", true), line]);
    paint();
    NS.Dialog({ title: "Open a pull request for " + o.node + "?", why: "This is the one thing on the page that reaches a remote. It pushes " + (o.branch || "the node's branch") + " to origin and opens the pull request with gh, using the GitHub login you already have.", content: content,
      actions: [{ label: "Push and open it", kind: "needs", solid: true, cmd: "runs the command above", onPress: function () { said.textContent = "\u2713 Pushed " + (o.branch || "the branch") + " and opened a " + (draft ? "draft " : "") + "pull request."; if (o.onPr) { o.onPr({ draft: draft, title: title }); } } },
        { label: "Say what it would do", cmd: "the same, with --dry-run · pushes nothing", onPress: function () { said.textContent = "\u2713 Dry run: it would push " + (o.branch || "the branch") + " and open the pull request. Nothing was pushed."; } }] }).open();
  }

  /* ---------- SayToNode: typing at a live node, behind a second credential ---------- */
  function SayToNode(o) {
    o = o || {};
    var state = o.state || "locked", msg = "";
    var el = h("section", { "class": "ls-say", "aria-label": "Say something to " + (o.node || "this node") });
    function paint() {
      el.textContent = "";
      if (o.lead) { return paintLead(); }
      el.appendChild(h("h4", { text: "Say something to " + (o.node || "this node") + " now" }));
      if (state === "ended") { el.appendChild(h("p", { text: (o.node || "This node") + " is not running, so there is nothing to type at. Say something to the lead instead: it reads it at the start of its next round." })); return; }
      if (state === "unset") {
        el.appendChild(h("p", { text: "Nothing on this machine can type at a node until a passphrase is set for it. It is set in a terminal, so this page cannot set the passphrase that guards it." }));
        el.appendChild(NS.Command({ why: "In a terminal:", command: "$ loadout team attach set --passphrase \"<something worth having>\"" }));
        return;
      }
      if (state === "locked") {
        el.appendChild(h("p", { text: "This puts words into a process running with your file access, so it needs the passphrase set for it as well as your sign-in. It is swapped for a grant that lasts 30 minutes and lives only in this page." }));
        var pass = text({ type: "password" }), bad = h("p", { "class": "ls-said no", role: "alert" });
        el.appendChild(h("div", { "class": "row" }, [field("Passphrase", pass), halo({ label: "Unlock for 30 minutes", size: "small", cmd: "asks the daemon for a 30-minute grant · stores nothing", onPress: function () { var ok = o.onUnlock ? o.onUnlock(pass.value) : pass.value.length > 0; if (ok) { state = "open"; paint(); var b = el.querySelector("textarea"); if (b) { b.focus(); } } else { bad.textContent = "\u2715 That is not the passphrase."; pass.select(); } } })]));
        el.appendChild(bad);
        return;
      }
      var line = h("code", { "class": "ls-cmd" }), sent = h("p", { "class": "ls-said", role: "status" });
      function paintLine() { line.textContent = "$ loadout team say " + (o.run || "<run>") + " --node " + o.node + " --message " + q(msg || "\u2026"); }
      var box = text({ lines: 2, placeholder: "stop, wrong file", onInput: function (v) { msg = v.trim(); paintLine(); } });
      box.setAttribute("aria-label", "Message to " + o.node);
      var send = halo({ label: "Send", size: "small", onPress: function () { if (!msg) { sent.className = "ls-said no"; sent.textContent = "\u2715 Write something to send first."; return; } sent.className = "ls-said"; sent.textContent = "\u2713 Sent. It reaches " + o.node + " the next time it says anything, once."; if (o.onSend) { o.onSend(msg); } box.value = ""; msg = ""; paintLine(); } });
      el.appendChild(h("div", { "class": "row" }, [box, h("div", { "class": "ls-action" }, [send, line])]));
      el.appendChild(sent);
      el.appendChild(h("span", { "class": "grant", text: "Unlocked \u00b7 the grant ends in " + (o.minutesLeft || 30) + " min" }));
      paintLine();
    }
    /* To the lead: no second credential. It is read at the start of the lead's next round, once. */
    function paintLead() {
      el.appendChild(h("h4", { text: "Say something to the lead" }));
      var line = h("code", { "class": "ls-cmd" }), sent = h("p", { "class": "ls-said", role: "status" });
      function paintLine() { line.textContent = "$ loadout team message " + (o.run || "<run>") + " --message " + q(msg || "\u2026"); }
      var box = text({ lines: 2, placeholder: "leave the tests alone", onInput: function (v) { msg = v.trim(); paintLine(); } });
      box.setAttribute("aria-label", "Message to the lead");
      var send = halo({ label: "Send", size: "small", disabledReason: o.disabledReason || null, onPress: function () { if (!msg) { sent.className = "ls-said no"; sent.textContent = "\u2715 Write something to send first."; return; } sent.className = "ls-said"; sent.textContent = "\u2713 Sent. The lead reads it at the start of its next round, once."; if (o.onSend) { o.onSend(msg); } box.value = ""; msg = ""; paintLine(); } });
      el.appendChild(h("div", { "class": "row" }, [box, h("div", { "class": "ls-action" }, [send, line])]));
      el.appendChild(sent);
      el.appendChild(h("p", { "class": "grant", text: "It steers the run without interrupting a node mid-thought. To stop a node now, open its stream under What it said." }));
      paintLine();
    }
    paint();
    return el;
  }

  /* ---------- TeamCard: a team you can run ---------- */
  function TeamCard(o) {
    o = o || {};
    var el = h("article", { "class": "ls-teamcard" + (o.template ? " tpl" : ""), "aria-label": o.name });
    el.appendChild(h("div", { "class": "top" }, [h("h3", {}, [o.href ? h("a", { href: o.href, text: o.name }) : o.name]), h("span", { "class": "ls-chip", text: o.template ? "a shape to copy" : (o.origin || "ships with Loadout") })]));
    el.appendChild(h("p", { text: o.description || "" }));
    el.appendChild(h("span", { "class": "facts", text: [o.nodes ? o.nodes + " nodes" : null, o.lead ? "led by " + o.lead : null, o.template ? null : o.posture, o.template ? null : (o.budget || null), o.doneWhen ? o.doneWhen + " done-when" : null].filter(Boolean).join(" \u00b7 ") }));
    (o.problems || []).forEach(function (p) { el.appendChild(h("span", { "class": "prob" }, [sig("blocked"), " Can't run here: " + p])); });
    var acts = o.template
      ? [{ label: "Make a team from it", size: "small", cmd: "opens Make a team · from " + o.name, onPress: o.onCopy }]
      : [{ label: "Run it", size: "small", cmd: "opens Start a run · starts nothing yet", onPress: o.onRun, disabledReason: (o.problems || []).length ? "can't run here until that is fixed" : null }];
    el.appendChild(h("div", { "class": "ls-answers" }, acts.map(halo)));
    return el;
  }

  /* ---------- TeamNodes: a team's nodes as the lead's line and its branches ---------- */
  function TeamNodes(o) {
    o = o || {};
    var by = {}; (o.nodes || []).forEach(function (n) { by[n.name] = n; });
    var seen = {};
    function item(n, lead) {
      seen[n.name] = true;
      var kids = (n.delegates || []).filter(function (d) { return by[d] && !seen[d]; });
      return h("li", { "class": lead ? "lead" : null }, [h("div", { "class": "nd" }, [h("span", { "class": "dot", "aria-hidden": "true" }), h("div", {}, [h("b", { text: n.name }), h("span", { text: [n.role, n.mode, n.instances > 1 ? "up to " + n.instances + " at once" : null, n.worktree ? "each in its own worktree" : null].filter(Boolean).join(" \u00b7 ") }), n.deliverable ? h("span", { text: "Hands back: " + n.deliverable }) : null])]),
        kids.length ? h("ul", { "aria-label": "Briefed by " + n.name }, kids.map(function (d) { return item(by[d], false); })) : null]);
    }
    var root = (o.nodes || [])[0];
    var tree = root ? h("ul", { "aria-label": "Nodes" }, [item(root, true)]) : h("p", { text: "No nodes." });
    return h("div", { "class": "ls-tnodes" }, [tree]);
  }

  /* ---------- TeamRules: what a run of it follows ---------- */
  function TeamRules(o) {
    o = o || {};
    var dl = h("dl", { "class": "ls-rules" });
    (o.rows || []).forEach(function (r) { dl.appendChild(h("dt", { text: r[0] })); dl.appendChild(h("dd", {}, [r[1], r[2] ? cmd(r[2]) : null])); });
    return dl;
  }

  /* ---------- RoleRecord: how the roles have been doing, read when you open it ---------- */
  function RoleRecord(o) {
    o = o || {};
    var body = h("div", { style: "padding-top: 6px" }), read = false;
    var d = h("details", { "class": "ls-roles" }, [h("summary", { text: o.title || "How the roles have been doing" }), body]);
    d.addEventListener("toggle", function () {
      if (!d.open || read) { return; } read = true;
      body.appendChild(h("p", { "class": "ls-note", role: "status", text: "Reading every journal on this machine\u2026" }));
      var rows = o.read ? o.read() : (o.rows || []);
      setTimeout(function () { body.textContent = ""; paint(rows); }, o.delay === undefined ? 450 : o.delay);
    });
    function paint(rows) {
      rows = rows.slice().sort(function (a, b) { return b[3] - a[3]; });
      var H = ["Role", "Model", "Runs", "In all", "A run", "Reports taken", "Refused a run"];
      body.appendChild(h("div", { "class": "ls-depths", style: "container-type: inline-size" }, [h("table", { "class": "ls-xch" }, [h("caption", { text: "Dearest first" }), h("thead", {}, [h("tr", {}, H.map(function (x, i) { return h("th", { scope: "col", "class": i >= 2 ? "r" : null, text: x }); }))]),
        h("tbody", {}, rows.map(function (r) { var high = r[6] >= (o.refusedHigh || 10); return h("tr", {}, [r[0], r[1] || "not recorded", r[2], money(r[3]), money(r[4]), r[5] + "%", r[6]].map(function (c, i) { return h("td", { "data-h": H[i], "class": i >= 2 ? "r" : null }, [h("span", { text: String(c) }), i === 6 && high ? h("span", { "class": "high", text: "high" }) : null]); })); }))])]));
      body.appendChild(h("p", { "class": "ls-note", text: o.note || "Which model a node ran on has only been recorded since this was added; older runs say not recorded." }));
      if ((o.time || []).length) {
        body.appendChild(h("h4", { "class": "ls-zone", style: "margin-top: 18px", text: "Where each node's time went, roughly" }));
        body.appendChild(h("ul", { "class": "time" }, o.time.map(function (t) {
          var sum = t[1] + t[2] + t[3] + t[4] || 1;
          return h("li", {}, [h("b", { text: t[0] }), h("span", { text: "deciding " + t[1] + " min \u00b7 tools running " + t[2] + " min \u00b7 writing " + t[3] + " min \u00b7 waiting " + t[4] + " min" }),
            h("div", { "class": "split", "aria-hidden": "true" }, [1, 2, 3, 4].map(function (k) { return h("i", { "class": "s" + (k - 1), style: "width:" + (t[k] / sum * 100).toFixed(1) + "%" }); }))]);
        })));
        body.appendChild(h("p", { "class": "ls-note", text: "From the gaps between what each node did. Deciding includes the time an answer took to arrive; a gap over two minutes counts as waiting." }));
      }
    }
    if (o.open) { d.open = true; }
    return d;
  }

  /* ---------- TeamSheet: Start a run, Make a team, Run one on a schedule ---------- */
  var POSTURES = [{ name: "manual", desc: "Asks you at every step, and lets you change each brief" }, { name: "supervised", desc: "Goes on, and holds at the gates the team names" }, { name: "autonomous", desc: "Holds for nothing, within its budget and permissions" }];
  var TIMEOUTS = [{ name: "think-again-once", sub: "Back to the lead once, then its recommendation (the default)" }, { name: "recommend", sub: "Take its recommendation at once" }, { name: "think-again", sub: "Back to the lead every time" }];
  function TeamSheet(o) {
    o = o || {};
    var kind = o.kind || "run", p = Object.assign({}, o.preset || {}), dlg = null;
    var teams = o.teams || [], projects = o.projects || [];
    var runnable = teams.filter(function (t) { return !t.template; });
    var V = { team: p.team || (kind === "make" ? "" : (runnable[0] || {}).name || ""), goal: p.goal || "", project: p.project || (projects[0] || {}).name || "", autonomy: p.autonomy || "", rounds: p.rounds || "", usd: p.usd || "", done: p.done || "", dropped: {}, after: p.after || "", timeout: "think-again-once", name: p.name || "", from: p.from || "", forProject: false, when: "every", at: "09:00", every: "24h" };
    var line = h("code", { "class": "ls-cmdline" }), foot = h("div", { "class": "foot" }), said = h("p", { "class": "ls-said", role: "status" });
    var el = h("div", { "class": "ls-sheet" });
    function team() { return teams.filter(function (t) { return t.name === V.team; })[0] || null; }
    function proj() { return projects.filter(function (x) { return x.name === V.project; })[0] || null; }
    function command() {
      if (kind === "make") { return "$ loadout team new " + (V.name || "<name>") + (V.from ? " --from " + V.from : "") + (V.forProject ? " --for-this-project --project " + V.project : ""); }
      if (kind === "schedule") { return "$ loadout team schedule add " + (V.name || "<name>") + " " + (V.team || "<team>") + " " + q(V.goal || "\u2026") + " --project " + V.project + (V.when === "at" ? " --at " + V.at : V.when === "on" ? " --on commit" : " --every " + (V.every || "24h")) + (V.autonomy ? " --autonomy " + V.autonomy : "") + (V.usd ? " --usd " + V.usd : ""); }
      var c = "$ loadout team run " + (V.team || "<team>") + " " + q(V.goal || "\u2026") + " --project " + V.project;
      if (V.autonomy) { c += " --autonomy " + V.autonomy; }
      if (V.rounds) { c += " --rounds " + V.rounds; }
      if (V.usd) { c += " --usd " + V.usd; }
      V.done.split("\n").map(function (s) { return s.trim(); }).filter(Boolean).forEach(function (d) { c += " --done-when " + q(d); });
      var held = ((o.projectDoneWhen || {})[V.project] || []), dropped = held.filter(function (x) { return V.dropped[x]; });
      if (held.length && dropped.length === held.length) { c += " --no-project-done-when"; } else { dropped.forEach(function (x) { c += " --drop-project-done-when " + q(x); }); }
      if (V.after) { c += " --take-recommendation-after " + V.after; if (V.timeout !== "think-again-once") { c += " --on-timeout " + V.timeout; } }
      return c;
    }
    function problem() {
      if (kind === "make") { return !V.name ? "give it a name first" : (!/^[a-z0-9]+(-[a-z0-9]+)*$/.test(V.name) ? "lowercase words joined by hyphens, as the built-in ones are" : (teams.some(function (t) { return t.name === V.name; }) ? "a team called " + V.name + " already exists" : null)); }
      if (kind === "schedule" && !V.name) { return "give the schedule a name first"; }
      var t = team();
      if (!t) { return "pick a team first"; }
      if (t.template) { return t.name + " is a shape to copy, not a team to run"; }
      if ((t.problems || []).length) { return "it can't run here: " + t.problems[0]; }
      if (!V.goal.trim()) { return "say what the run is for first"; }
      if (kind === "schedule" && V.when === "every" && !/^\d+\s*(m|h|d)$/.test(V.every.trim())) { return "say how often as 30m, 2h or 1d"; }
      if (kind === "schedule" && V.when === "every" && /^\d+\s*m$/.test(V.every.trim()) && parseInt(V.every, 10) < 5) { return "nothing may repeat faster than every five minutes"; }
      if (V.usd && !/^(none|\d+(\.\d+)?)$/i.test(V.usd.trim())) { return "a budget is a figure in US dollars, or none"; }
      if (V.usd && /^0+(\.0+)?$/.test(V.usd.trim())) { return "a budget of nothing is refused; say none for no cap"; }
      if (V.after && !/^(\d+\s*(m|h|d|mins?|minutes?|hours?|days?)|never|off)$/i.test(V.after.trim())) { return "write the wait as 30m, 2h or 1d"; }
      return null;
    }
    function paintFoot() {
      line.textContent = command(); foot.textContent = "";
      var bad = problem(), go = { run: "Start the run", make: "Write it", schedule: "Schedule it" }[kind];
      foot.appendChild(halo({ label: go, solid: true, glyph: "\u25b6", disabledReason: bad || (o.watchOnly ? "Studio is only watching: type the command in a terminal" : null), cmd: bad ? null : "runs the command above", onPress: submit }));
      if (dlg) { foot.appendChild(halo({ label: "Cancel", size: "small", onPress: function () { dlg.close(); } })); }
    }
    function submit() {
      var c = command();
      if (kind === "run") {
        NS.Dialog({ title: "Start " + V.team + " on \u201c" + V.goal.trim() + "\u201d?", why: "It spends money and edits " + V.project + ". Nothing starts until you say so.", actions: [{ label: "Start it", kind: "needs", solid: true, cmd: c, onPress: function () { finish(c); } }] }).open();
      } else { finish(c); }
    }
    function finish(c) {
      said.textContent = kind === "run" ? "\u2713 Started. The run is on its page." : kind === "make" ? "\u2713 Written. $ loadout team edit " + V.name + " --path-only says where." : "\u2713 Scheduled. " + (o.daemon === false ? "It will not fire until the daemon is running." : "The daemon fires it.");
      if (o.onSubmit) { o.onSubmit(c, Object.assign({}, V)); }
      if (dlg) { setTimeout(function () { dlg.close(); }, 30); }
    }
    function teamPicker(only) {
      var list = only ? runnable : teams;
      var i = Math.max(0, list.map(function (t) { return t.name; }).indexOf(V.team));
      return NS.Picker({ label: "Team", selected: i, options: list.map(function (t) { return { name: t.name, sub: (t.template ? "a shape to copy \u00b7 " : "") + t.description }; }), onPick: function (x) { V.team = x.name; paintAbout(); paintFoot(); } });
    }
    function projectPicker() { return NS.Picker({ label: "Project", selected: Math.max(0, projects.map(function (x) { return x.name; }).indexOf(V.project)), options: projects.map(function (x) { return { name: x.name, sub: x.path }; }), onPick: function (x) { V.project = x.name; paintHeld(); paintFoot(); } }); }
    var about = h("p", { "class": "about" }), held = h("div", { "class": "proj" });
    function paintAbout() {
      var t = team(); about.textContent = "";
      if (!t) { about.textContent = "Pick a team to see what it is."; return; }
      about.appendChild(document.createTextNode([t.nodes + " nodes", "led by " + t.lead, t.posture, t.budget, (t.doneWhen || []).length ? (t.doneWhen.length + " done-when of its own") : "no done-when of its own"].filter(Boolean).join(" \u00b7 ")));
      (t.problems || []).forEach(function (x) { about.appendChild(h("span", { "class": "prob", text: "\u2715 Can't run here: " + x })); });
      if (doneBox) { doneBox.placeholder = (t.doneWhen || []).length ? "Empty, it is held to the team's own:\n" + t.doneWhen.join("\n") : "Empty, the lead proposes some and you agree them before any worker starts."; }
    }
    function paintHeld() {
      held.textContent = ""; V.dropped = {};
      var list = (o.projectDoneWhen || {})[V.project] || [];
      if (!list.length) { return; }
      held.appendChild(h("span", { "class": "hint", style: "font-size: 12.5px; color: var(--muted)", text: V.project + " holds every run to these as well. Untick one to leave it out of this run; the journal says so." }));
      list.forEach(function (x) { held.appendChild(NS.Switch({ label: x, sub: "the project's own", checked: true, onChange: function (v) { V.dropped[x] = !v; paintFoot(); } })); });
    }
    var doneBox = null;
    if (kind === "run") {
      doneBox = text({ lines: 3, value: V.done, onInput: function (v) { V.done = v; paintFoot(); } });
      var postureIdx = Math.max(0, POSTURES.map(function (x) { return x.name; }).indexOf(V.autonomy || (team() || {}).posture || "supervised"));
      el.appendChild(h("div", { "class": "two" }, [teamPicker(false), projectPicker()]));
      el.appendChild(about);
      el.appendChild(field("What the run is for", text({ lines: 2, value: V.goal, spell: true, placeholder: "for example: make the docs true", onInput: function (v) { V.goal = v; paintFoot(); } }), null, true));
      el.appendChild(h("fieldset", {}, [h("legend", { text: "How it runs" }), NS.ModePicker({ label: "Posture", selected: postureIdx, options: POSTURES, onChange: function (n) { V.autonomy = n === ((team() || {}).posture || "supervised") ? "" : n; paintFoot(); } })]));
      el.appendChild(h("div", { "class": "two" }, [field("Budget, in US dollars", text({ value: V.usd, placeholder: (team() || {}).budget ? "the team's " + team().budget : "a figure, or none", onInput: function (v) { V.usd = v.trim(); paintFoot(); } }), "Or none, for no cap on purpose."), field("Rounds at most", text({ value: V.rounds, type: "number", placeholder: "no limit", onInput: function (v) { V.rounds = v.trim(); paintFoot(); } }), "The budget is the cap that binds.")]));
      el.appendChild(field("Done when, one per line", doneBox, null, true));
      el.appendChild(held);
      el.appendChild(h("fieldset", {}, [h("legend", { text: "When nobody answers" }), h("div", { "class": "two" }, [field("Take the lead's recommendation after", text({ value: V.after, placeholder: "never", onInput: function (v) { V.after = v.trim(); paintFoot(); } }), "30m, 2h or 1d. Only the lead's own questions."), NS.Picker({ label: "What happens then", options: TIMEOUTS, onPick: function (x) { V.timeout = x.name; paintFoot(); } })])]));
      paintAbout(); paintHeld();
    } else if (kind === "make") {
      el.appendChild(h("div", { "class": "two" }, [field("Its name", text({ value: V.name, placeholder: "for example: quick-review", onInput: function (v) { V.name = v.trim(); paintFoot(); } }), "Lowercase and hyphenated, as the built-in ones are."),
        NS.Picker({ label: "Start from", selected: Math.max(0, [""].concat(teams.map(function (t) { return t.name; })).indexOf(V.from)), options: [{ name: "An empty team", sub: "a file with nothing in it yet" }].concat(teams.map(function (t) { return { name: t.name, sub: (t.template ? "a shape to copy \u00b7 " : "") + t.description }; })), onPick: function (x, i) { V.from = i ? x.name : ""; paintFoot(); } })]));
      var pp = projectPicker(); pp.hidden = true;
      el.appendChild(NS.Switch({ label: "Only for one project", sub: "--for-this-project · otherwise every project sees it", onChange: function (v) { V.forProject = v; pp.hidden = !v; paintFoot(); } }));
      el.appendChild(pp);
      el.appendChild(h("p", { "class": "ls-note", text: "This writes the file and tells you where it is. Its nodes, rules and done-when are written in the file." }));
    } else {
      var whenBox = h("div", {});
      function paintWhen() {
        whenBox.textContent = "";
        if (V.when === "at") { whenBox.appendChild(field("The time of day, this machine's own", text({ type: "time", value: V.at, onInput: function (v) { V.at = v; paintFoot(); } }))); }
        else if (V.when === "every") { whenBox.appendChild(field("How often", text({ value: V.every, placeholder: "24h", onInput: function (v) { V.every = v.trim(); paintFoot(); } }), "30m, 2h or 1d. At least five minutes.")); }
        else { whenBox.appendChild(h("p", { "class": "ls-note", text: "When the project's repository moves: a new commit since the daemon last looked. The first look only records where it is, and a run's own commits never fire it again." })); }
      }
      el.appendChild(h("div", { "class": "two" }, [field("A name for the schedule", text({ value: V.name, placeholder: "for example: nightly-docs", onInput: function (v) { V.name = v.trim(); paintFoot(); } })), teamPicker(true)]));
      el.appendChild(field("What each run is for", text({ lines: 2, value: V.goal, spell: true, placeholder: "for example: check the docs against the code", onInput: function (v) { V.goal = v; paintFoot(); } }), null, true));
      el.appendChild(projectPicker());
      el.appendChild(h("fieldset", {}, [h("legend", { text: "When it runs" }), NS.ModePicker({ label: "When", selected: 1, options: [{ name: "A time of day", desc: "--at 09:00" }, { name: "An interval", desc: "--every 24h" }, { name: "Something to watch", desc: "--on commit" }], onChange: function (n, i) { V.when = ["at", "every", "on"][i]; paintWhen(); paintFoot(); } }), whenBox]));
      el.appendChild(h("fieldset", {}, [h("legend", { text: "How it runs" }), NS.ModePicker({ label: "Posture", selected: 0, options: POSTURES.slice(1), onChange: function (n) { V.autonomy = n === "supervised" ? "" : n; paintFoot(); } }), h("p", { "class": "ls-note", text: "Manual is not offered: it needs a person at every step, and nobody is watching when a schedule fires." })]));
      el.appendChild(field("What each run may spend, in US dollars", text({ value: V.usd, placeholder: "the team's own", onInput: function (v) { V.usd = v.trim(); paintFoot(); } }), "Or none, for no cap."));
      el.appendChild(h("p", { "class": "ls-note" + (o.daemon === false ? " needs" : "") }, [o.daemon === false ? "The daemon is not running, so nothing will fire. Start it in a terminal: " : "Nothing fires unless the daemon is running. It is running now. A missed run is not made up for later. ", o.daemon === false ? h("code", { "class": "ls-cmd", text: "$ loadout daemon" }) : null]));
      paintWhen();
    }
    el.appendChild(line); el.appendChild(foot); el.appendChild(said);
    paintFoot();
    var TITLES = { run: "Start a run", make: "Make a team", schedule: "Run one on a schedule" };
    return { el: el, command: command, open: function () { dlg = NS.Dialog({ title: TITLES[kind], label: TITLES[kind], content: el, extraClass: "sheet", why: kind === "run" ? "A team against a project, with a goal. It asks once more before anything starts." : null }); paintFoot(); dlg.open(); return dlg; } };
  }

  /* ---------- Schedules: runs that start without you ---------- */
  function Schedules(o) {
    o = o || {};
    var el = h("div", { "class": "ls-sched" });
    var d = o.daemon || {};
    el.appendChild(h("p", { "class": "ls-note" + (d.running ? "" : " needs") }, [d.running ? (d.paused ? "The daemon is held: no schedule fires until it is resumed. " : "The daemon is running and fires these. ") : "The daemon is not running, so none of these will fire. ", d.running ? (d.paused ? cmd("$ loadout daemon resume") : null) : cmd("$ loadout daemon")]));
    if (!(o.items || []).length) { el.appendChild(h("p", { "class": "ls-note", text: "Nothing is scheduled on this machine." })); return el; }
    el.appendChild(h("ul", { "aria-label": "Runs on a schedule" }, o.items.map(function (s) {
      var row = h("li", {}, [h("div", { "class": "t" }, [h("b", { text: s.name }), h("span", { text: s.team + " \u00b7 " + s.goal }), h("em", { text: [s.when, s.next ? "next " + s.next : null, s.autonomy, s.usd ? "$" + s.usd + " a run" : null].filter(Boolean).join(" \u00b7 ") })]),
        halo({ label: "Stop it", size: "small", cmd: "$ loadout team schedule remove " + s.name, disabledReason: o.disabledReason || null, onPress: function () { if (o.onRemove) { o.onRemove(s.name); } row.remove(); } })]);
      return row;
    })));
    return el;
  }

  /* ---------- Remedy: a script a team wrote down, and the two keys it needs ---------- */
  function Remedy(o) {
    o = o || {};
    var el = h("article", { "class": "ls-remedy", "aria-label": o.name });
    el.appendChild(h("div", { "class": "top" }, [h("h3", { text: o.name }), h("span", { "class": "ls-chip", text: o.kind }), sig(o.trust || "not trusted")]));
    el.appendChild(TeamRules({ rows: [["What it does", o.what], ["It assumes", o.assumes], ["It proves", o.proves], ["Revision", o.revision + (o.by ? " \u00b7 registered by run " + o.by : "")]] }));
    var RULE = { trusted: "may run unattended, if this exact script is trusted", ask: "is held for you every time, trusted or not", never: "is refused outright, however trusted the script is" };
    el.appendChild(h("p", { "class": "rule", text: "This machine's rule for " + o.kind + ": " + o.rule + ". A " + o.kind + " remedy " + RULE[o.rule] + "." }));
    if (o.trust === "changed since trusted") { el.appendChild(h("p", { "class": "ls-note needs", text: "The script has changed since it was trusted, so it is asked about again." })); }
    el.appendChild(h("div", { "class": "ls-answers", style: "margin: 0" }, [halo({ label: "Read the script", size: "small", cmd: "$ loadout team remedy show " + o.name + " --team " + o.team + " --script", onPress: o.onRead })]));
    el.appendChild(NS.Command({ why: o.trust === "trusted" ? "Trust is a standing agreement, made in a terminal on purpose. To take it back:" : "Trusting it lets it run with nobody watching, so it is agreed in a terminal on purpose:", command: "$ loadout team remedy trust " + o.name + " --team " + o.team + (o.trust === "trusted" ? " --revoke" : "") }));
    (o.requests || []).forEach(function (r) {
      var reason = "", said = h("p", { "class": "ls-said", role: "status" }), acts = h("div", { "class": "ls-answers" });
      function base() { return "$ loadout team remedy requests --team " + o.team; }
      function paint() { acts.textContent = ""; [{ label: "Let it run", kind: "needs", glyph: "\u2713", cmd: base() + " --approve " + r.id + (reason ? " --reason " + q(reason) : ""), onPress: function () { acts.remove(); rb.remove(); said.textContent = "\u2713 Approved once. " + r.node + " runs it."; if (o.onApprove) { o.onApprove(r.id, reason); } } }, { label: "Refuse", glyph: "\u2715", cmd: base() + " --refuse " + r.id + (reason ? " --reason " + q(reason) : ""), onPress: function () { acts.remove(); rb.remove(); said.textContent = "\u2713 Refused. The node reports what it needed and why."; if (o.onRefuse) { o.onRefuse(r.id, reason); } } }].forEach(function (b) { acts.appendChild(halo(Object.assign({ size: "small", disabledReason: o.disabledReason || null }, b))); }); }
      var rb = field("Your reason", text({ placeholder: "Optional", onInput: function (v) { reason = v.trim(); paint(); } }), null, true);
      el.appendChild(h("section", { "class": "ls-gate compact", role: "group", "aria-label": "Request " + r.id }, [h("span", { "class": "who", text: r.node + " \u00b7 run " + r.run + " \u00b7 " + r.id + (r.asked ? " \u00b7 " + r.asked : "") }), h("p", { "class": "q", text: r.problem }), h("p", { "class": "rec" }, ["Held rather than run because ", h("b", { text: r.why }), "."]), acts, rb, said]));
      paint();
    });
    return el;
  }

  /* ---------- Bin: what was removed, putting it back, and clearing out runs ---------- */
  function Bin(o) {
    o = o || {};
    var el = h("div", { "class": "ls-bin" }), entries = (o.entries || []).slice();
    var list = h("ul", { "aria-label": "In the bin" }), said = h("p", { "class": "ls-said", role: "status" });
    function restoreCmd(e) { return e.kind === "run" ? "$ loadout team runs restore " + e.name : "$ loadout team restore " + e.name; }
    function paint() {
      list.textContent = "";
      if (!entries.length) { list.appendChild(h("li", {}, [h("span"), h("span", { "class": "ls-note", style: "margin: 0", text: "The bin is empty." }), h("span")])); }
      entries.forEach(function (e) {
        list.appendChild(h("li", {}, [h("span", { "class": "kind", text: e.kind }), h("span", { "class": "t" }, [h("b", { text: e.name }), e.what ? h("span", { text: e.what }) : null, h("span", { "class": e.left <= 3 ? "soon" : null, text: "removed " + e.removed + " \u00b7 " + e.left + (e.left === 1 ? " day" : " days") + " left" + (e.size ? " \u00b7 " + e.size : "") })]),
          halo({ label: "Put it back", size: "small", cmd: restoreCmd(e), disabledReason: o.disabledReason || null, onPress: function () { entries = entries.filter(function (x) { return x !== e; }); said.textContent = "\u2713 Put back: " + e.name + "."; if (o.onRestore) { o.onRestore(e); } paint(); } })]));
      });
    }
    paint();
    el.appendChild(h("p", { "class": "ls-note", style: "margin: 0", text: "Removed runs and teams wait here for " + (o.keptDays || 30) + " days, then go for good. A run's branches were never touched, so they are not in here." }));
    el.appendChild(list); el.appendChild(said);
    el.appendChild(h("div", { "class": "ls-answers", style: "margin: 0" }, [halo({ label: "Empty it for good", kind: "danger", size: "small", cmd: "$ loadout team bin empty --yes", disabledReason: o.disabledReason || (entries.length ? null : "the bin is already empty"), onPress: function () {
      NS.Dialog({ danger: true, title: "Empty the bin for good?", why: "This is the one thing here with no way back. It deletes:", content: h("ul", { style: "margin: 0 0 4px; padding-left: 18px; font-family: var(--font-mono); font-size: 12.5px" }, entries.map(function (e) { return h("li", { text: e.kind + " " + e.name }); })),
        actions: [{ label: "Empty it", kind: "danger", cmd: "$ loadout team bin empty --yes", onPress: function () { entries = []; said.textContent = "\u2713 The bin is empty."; if (o.onEmpty) { o.onEmpty(); } paint(); } }] }).open(); } })]));
    /* Clear out runs: the same as team runs prune, said as a sentence before it goes. */
    var P = { outcome: "", older: "30d", keep: "20" };
    var sentence = h("p", { "class": "sentence" }), pline = h("code", { "class": "ls-cmd" }), pacts = h("div", { "class": "ls-answers", style: "margin: 0" });
    function pcmd(dry) { return "$ loadout team runs prune" + (P.outcome === "failed" ? " --failed" : P.outcome ? " --outcome " + P.outcome : "") + (P.older ? " --older-than " + P.older : "") + (P.keep ? " --keep " + P.keep : "") + (dry ? " --dry-run" : ""); }
    function ppaint() {
      var none = !P.outcome && !P.older && !P.keep;
      sentence.textContent = none ? "Say which runs: an ending, an age or how many to keep." : "Moves " + (P.outcome ? "the " + P.outcome + " runs" : "the runs") + (P.older ? " older than " + P.older : "") + " to the bin" + (P.keep ? ", never the newest " + P.keep : "") + ", and never one still going or one that left a branch nothing merged.";
      pline.textContent = pcmd(false); pacts.textContent = "";
      pacts.appendChild(halo({ label: "Say which first", size: "small", cmd: pcmd(true), disabledReason: none ? "say which runs first" : null, onPress: function () { said.textContent = "\u2713 Dry run: it lists what it would take and why it keeps the rest. Nothing moved."; } }));
      pacts.appendChild(halo({ label: "Clear them out", size: "small", cmd: pcmd(false), disabledReason: o.disabledReason || (none ? "say which runs first" : null), onPress: function () { NS.Dialog({ title: "Clear out these runs?", why: sentence.textContent + " They wait in the bin for " + (o.keptDays || 30) + " days.", actions: [{ label: "Clear them out", kind: "needs", cmd: pcmd(false), onPress: function () { said.textContent = "\u2713 Cleared out. They are in the bin."; if (o.onPrune) { o.onPrune(pcmd(false)); } } }] }).open(); } }));
    }
    var ENDS = ["any ending", "failed", "stopped", "limited", "blocked", "done", "needs-decision", "unrecorded"];
    el.appendChild(h("details", {}, [h("summary", { text: "Clear out runs" }), h("div", { "class": "prune" }, [h("div", { "class": "two" }, [
      NS.Picker({ label: "Ending", options: ENDS.map(function (e) { return { name: e }; }), onPick: function (x, i) { P.outcome = i ? x.name : ""; ppaint(); } }),
      field("Older than", text({ value: P.older, placeholder: "30d", onInput: function (v) { P.older = v.trim(); ppaint(); } })),
      field("Keep the newest", text({ value: P.keep, type: "number", placeholder: "20", onInput: function (v) { P.keep = v.trim(); ppaint(); } }))]), sentence, pacts])]));
    ppaint();
    return el;
  }

  Object.assign(NS, { Gate: Gate, RunCard: RunCard, Spend: Spend, NeedsYou: NeedsYou, RunShape: RunShape, Crew: Crew, DoneWhen: DoneWhen, RunNumbers: RunNumbers, RunDepths: RunDepths, Journal: Journal, Exchanges: Exchanges, Papers: Papers, Patch: Patch, SayToNode: SayToNode,
    TeamCard: TeamCard, TeamNodes: TeamNodes, TeamRules: TeamRules, RoleRecord: RoleRecord, TeamSheet: TeamSheet, Schedules: Schedules, Remedy: Remedy, Bin: Bin, shellQuote: q });
})();

(function () {
  "use strict";
  var NS = window.LoadoutStudio = window.LoadoutStudio || {};
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
  function size(b) { if (b === null || b === undefined) { return ""; } return b < 1024 ? b + " B" : b < 1048576 ? (b / 1024).toFixed(b < 10240 ? 1 : 0) + " KB" : (b / 1048576).toFixed(1) + " MB"; }
  function ext(p) { var m = /\.([A-Za-z0-9]+)$/.exec(p); return m ? m[1].toLowerCase() : ""; }
  function base(p) { return p.split(/[\\/]/).pop(); }
  var KIND = { md: "markdown", markdown: "markdown", csv: "csv", tsv: "csv", json: "json", png: "image", jpg: "image", jpeg: "image", gif: "image", webp: "image", svg: "image", txt: "text", log: "text" };
  var CODE = { cs: 1, ts: 1, js: 1, mjs: 1, svelte: 1, py: 1, ps1: 1, sh: 1, yaml: 1, yml: 1, toml: 1, xml: 1, csproj: 1, html: 1, css: 1, go: 1, rs: 1, java: 1, kt: 1, sql: 1, ini: 1, props: 1 };
  function kindOf(p) { var e = ext(p); return KIND[e] || (CODE[e] ? "code" : (e ? "binary" : "text")); }

  /* ---------- renderers: everything built as nodes, never as HTML from the file ---------- */
  var KW = /^(abstract|as|async|await|base|bool|break|case|catch|class|const|continue|def|default|do|else|enum|export|extends|false|finally|fn|for|foreach|from|function|get|if|implements|import|in|init|interface|internal|is|let|namespace|new|null|override|private|protected|public|readonly|record|return|sealed|set|static|string|struct|switch|this|throw|true|try|type|typeof|using|var|void|while|yield)$/;
  function codeLine(text, lang) {
    var out = h("span", { "class": "l" }), re = /(\/\/.*$|#.*$|"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|\b\d+(?:\.\d+)?\b|\b[A-Za-z_][A-Za-z0-9_]*\b)/g, last = 0, m;
    var hashComments = /^(py|sh|yaml|yml|toml|ps1|ini)$/.test(lang);
    while ((m = re.exec(text))) {
      var t = m[0], cls = null;
      if (t.slice(0, 2) === "//" && !hashComments) { cls = "c"; } else if (t[0] === "#") { if (!hashComments) { continue; } cls = "c"; }
      else if (t[0] === "\"" || t[0] === "'") { cls = "s"; } else if (/^\d/.test(t)) { cls = "n"; } else if (KW.test(t)) { cls = "k"; } else if (/^[A-Z][a-z]/.test(t)) { cls = "t"; }
      if (!cls) { continue; }
      if (m.index > last) { out.appendChild(document.createTextNode(text.slice(last, m.index))); }
      out.appendChild(h("span", { "class": cls, text: t })); last = m.index + t.length;
      if (cls === "c") { break; }
    }
    if (last < text.length) { out.appendChild(document.createTextNode(text.slice(last))); }
    if (!text.length) { out.appendChild(document.createTextNode(" ")); }
    return out;
  }
  function Code(text, lang, limit) {
    var lines = text.split("\n"); if (lines.length > 1 && lines[lines.length - 1] === "") { lines.pop(); }
    var shown = limit ? lines.slice(0, limit) : lines;
    return h("pre", { "class": "ls-code" }, shown.map(function (l) { return codeLine(l, lang); }));
  }
  function DiffView(rows) {
    var CL = { "+": "add", "-": "del", "@": "hunk", " ": "" };
    return h("pre", { "class": "ls-code diffv" }, rows.map(function (r) { var s = codeLine(r[1], ""); if (CL[r[0]]) { s.classList.add(CL[r[0]]); } if (r[0] === "@") { s.textContent = r[1]; } return s; }));
  }
  function inline(text) {
    var frag = document.createDocumentFragment(), re = /(`[^`]+`|\*\*[^*]+\*\*|\*[^*\s][^*]*\*|_[^_\s][^_]*_|\[[^\]]+\]\([^)\s]+\))/g, last = 0, m;
    while ((m = re.exec(text))) {
      if (m.index > last) { frag.appendChild(document.createTextNode(text.slice(last, m.index))); }
      var t = m[0];
      if (t[0] === "`") { frag.appendChild(h("code", { text: t.slice(1, -1) })); }
      else if (t.slice(0, 2) === "**") { frag.appendChild(h("strong", {}, [inline(t.slice(2, -2))])); }
      else if (t[0] === "*" || t[0] === "_") { frag.appendChild(h("em", {}, [inline(t.slice(1, -1))])); }
      else { var lm = /^\[([^\]]+)\]\(([^)\s]+)\)$/.exec(t), href = lm[2]; frag.appendChild(/^https?:\/\//i.test(href) ? h("a", { href: href, target: "_blank", rel: "noopener noreferrer" }, [inline(lm[1])]) : h("span", {}, [inline(lm[1]), " (", h("code", { text: href }), ")"])); }
      last = m.index + t.length;
    }
    if (last < text.length) { frag.appendChild(document.createTextNode(text.slice(last))); }
    return frag;
  }
  function cells(row) { return row.replace(/^\s*\|/, "").replace(/\|\s*$/, "").split("|").map(function (c) { return c.trim(); }); }
  function Markdown(src) {
    var el = h("div", { "class": "ls-md" }), lines = src.replace(/\r/g, "").split("\n"), i = 0, para = [];
    function flush() { if (para.length) { el.appendChild(h("p", {}, [inline(para.join(" "))])); para = []; } }
    while (i < lines.length) {
      var l = lines[i], m;
      if (/^```/.test(l)) { flush(); var code = []; i++; while (i < lines.length && !/^```/.test(lines[i])) { code.push(lines[i]); i++; } i++; el.appendChild(h("pre", {}, [h("code", { text: code.join("\n") })])); continue; }
      if ((m = /^(#{1,4})\s+(.*)$/.exec(l))) { flush(); el.appendChild(h("h" + m[1].length, {}, [inline(m[2])])); i++; continue; }
      if (/^\s*([-*_])\s*\1\s*\1[\s\-*_]*$/.test(l)) { flush(); el.appendChild(h("hr")); i++; continue; }
      if (/^\s*\|.*\|\s*$/.test(l) && i + 1 < lines.length && /^\s*\|?\s*:?-{3,}/.test(lines[i + 1])) {
        flush(); var head = cells(l); i += 2; var body = [];
        while (i < lines.length && /^\s*\|.*\|\s*$/.test(lines[i])) { body.push(cells(lines[i])); i++; }
        el.appendChild(h("table", {}, [h("thead", {}, [h("tr", {}, head.map(function (c) { return h("th", { scope: "col" }, [inline(c)]); }))]), h("tbody", {}, body.map(function (r) { return h("tr", {}, r.map(function (c) { return h("td", {}, [inline(c)]); })); }))]));
        continue;
      }
      if (/^\s*([-*+]|\d+[.)])\s+/.test(l)) {
        flush(); var ordered = /^\s*\d/.test(l), list = h(ordered ? "ol" : "ul");
        while (i < lines.length && /^\s*([-*+]|\d+[.)])\s+/.test(lines[i])) { list.appendChild(h("li", {}, [inline(lines[i].replace(/^\s*([-*+]|\d+[.)])\s+/, ""))])); i++; }
        el.appendChild(list); continue;
      }
      if (/^>\s?/.test(l)) { flush(); var q = []; while (i < lines.length && /^>\s?/.test(lines[i])) { q.push(lines[i].replace(/^>\s?/, "")); i++; } el.appendChild(h("blockquote", {}, [h("p", {}, [inline(q.join(" "))])])); continue; }
      if (!l.trim()) { flush(); i++; continue; }
      para.push(l.trim()); i++;
    }
    flush();
    return el;
  }
  function parseCsv(text, sep) {
    var rows = [], row = [], cell = "", q = false;
    for (var i = 0; i < text.length; i++) {
      var c = text[i];
      if (q) { if (c === "\"" && text[i + 1] === "\"") { cell += "\""; i++; } else if (c === "\"") { q = false; } else { cell += c; } }
      else if (c === "\"") { q = true; } else if (c === sep) { row.push(cell); cell = ""; } else if (c === "\n") { row.push(cell); rows.push(row); row = []; cell = ""; } else if (c !== "\r") { cell += c; }
    }
    if (cell || row.length) { row.push(cell); rows.push(row); }
    return rows;
  }
  function Table(text, sep) {
    var rows = parseCsv(text, sep), head = rows.shift() || [];
    var num = head.map(function (_, k) { return rows.length && rows.every(function (r) { return !r[k] || /^-?[\d.,]+%?$/.test(r[k]); }); });
    return h("table", { "class": "ls-csv" }, [h("caption", { "class": "ls-note", style: "text-align: left; margin: 0 0 6px", text: rows.length + (rows.length === 1 ? " row" : " rows") + " · " + head.length + " columns" }), h("thead", {}, [h("tr", {}, head.map(function (c) { return h("th", { scope: "col", text: c }); }))]),
      h("tbody", {}, rows.map(function (r) { return h("tr", {}, head.map(function (_, k) { return h("td", { "class": num[k] ? "r" : null, text: r[k] || "" }); })); }))]);
  }

  /* ---------- Outbox ---------- */
  function Outbox(o) {
    o = o || {};
    var files = (o.files || []).map(function (f) { return Object.assign({ key: f.commit + ":" + f.path }, f); });
    var loose = (o.loose || []).map(function (f) { return Object.assign({ key: "loose:" + f.path, loose: true }, f); });
    var all = files.concat(loose), byKey = {}; all.forEach(function (f) { byKey[f.key] = f; });
    var by = o.by || "node", filter = "", picked = null, mode = "file", raw = false, open = {};
    var commits = []; files.forEach(function (f) { if (commits.indexOf(f.commit) < 0) { commits.push(f.commit); } });
    var el = h("section", { "class": "ls-outbox", "aria-label": "What it delivered" });

    /* The head: a summary, Download all, and where it comes from. */
    var said = h("p", { "class": "ls-said", role: "status" });
    el.appendChild(h("div", { "class": "ohead" }, [h("div", { "class": "sum" }, [h("b", { text: files.length + (files.length === 1 ? " file" : " files") + " from " + commits.length + (commits.length === 1 ? " commit" : " commits") + (loose.length ? " · " + loose.length + " not committed" : "") }), h("span", { text: (o.repository || "repository not recorded") + " · $ loadout team outbox " + (o.run || "<run>") })]),
      NS.HaloButton({ label: "Download all", size: "small", glyph: "↓", cmd: (o.run || "run") + "-outbox.zip · reads only", disabledReason: all.length ? (o.downloadAllReason || null) : "nothing was delivered", onPress: function () { said.textContent = "✓ Downloading " + (o.run || "run") + "-outbox.zip: " + all.filter(function (f) { return f.bytes !== null; }).length + " files, each as its commit left it."; if (o.onDownloadAll) { o.onDownloadAll(); } } })]));
    el.appendChild(said);
    if (!o.repository && !all.length) { el.appendChild(h("p", { "class": "notice", text: "Every node of this run had its own worktree, and they have been cleared away, so where it worked cannot be told. Nothing can be listed." })); return el; }

    /* The side: group by, find, and the tree. */
    var tree = h("ul", { role: "tree", "aria-label": "Delivered files" });
    var groupBtns = [["node", "By node"], ["folder", "By folder"]].map(function (g) { return h("button", { type: "button", "aria-pressed": by === g[0] ? "true" : "false", text: g[1], onclick: function () { by = g[0]; groupBtns.forEach(function (b, k) { b.setAttribute("aria-pressed", ["node", "folder"][k] === by ? "true" : "false"); }); paintTree(); } }); });
    var find = h("input", { "class": "ls-text find", type: "search", placeholder: "Find a file", "aria-label": "Find a file", autocomplete: "off" });
    find.addEventListener("input", function () { filter = find.value.trim().toLowerCase(); paintTree(); });
    var side = h("div", { "class": "side" }, [h("div", { "class": "grp", role: "group", "aria-label": "Group the files" }, groupBtns), find, tree]);

    /* The viewer. */
    var vpath = h("div", { "class": "path" }), vbar = h("div", { "class": "vbar", role: "group", "aria-label": "How to show it" }), vbody = h("div", { "class": "vbody", tabindex: "0", role: "region", "aria-label": "File contents" }), vfoot = h("div", { "class": "vfoot" });
    var back = h("button", { type: "button", "class": "ls-halo small back", onclick: function () { el.classList.remove("reading"); var it = tree.querySelector("[aria-selected=true]"); if (it) { it.focus(); } } }, [h("span", { "class": "ring", "aria-hidden": "true" }), "The files"]);
    var view = h("div", { "class": "view" }, [h("div", { "class": "vhead" }, [back, vpath, vbar]), vbody, vfoot]);
    el.appendChild(h("div", { "class": "split" }, [side, view]));

    function chip(f) {
      if (f.loose) { return f.bytes === null ? h("span", { "class": "ch gone", text: "gone" }) : h("span", { "class": "ch loose", title: "reported, not committed", text: f.kind || "file" }); }
      return h("span", { "class": "ch " + f.change, text: f.change });
    }
    function matches(f) { return !filter || f.path.toLowerCase().indexOf(filter) >= 0; }
    var items = [];
    function fileItem(f, lvl, label) {
      var li = h("li", { role: "treeitem", tabindex: "-1", "aria-selected": picked === f.key ? "true" : "false", "data-key": f.key, "aria-level": String(lvl + 1) }, [h("div", { "class": "ti", style: "--lvl:" + lvl, title: f.path }, [h("span", { "class": "fi", "aria-hidden": "true" }), h("span", { "class": "nm", text: label || base(f.path) }), chip(f)])]);
      li.firstChild.addEventListener("click", function () { pick(f.key, true); });
      items.push(li); return li;
    }
    function groupItem(key, label, sub, lvl, kids) {
      var isOpen = open[key] !== false || !!filter;
      var g = h("ul", { role: "group" }, kids);
      var li = h("li", { role: "treeitem", tabindex: "-1", "aria-expanded": isOpen ? "true" : "false", "data-group": key, "aria-level": String(lvl + 1) }, [h("div", { "class": "ti", style: "--lvl:" + lvl }, [h("span", { "class": "tw", "aria-hidden": "true" }), h("span", { "class": "grpname" }, [label, sub ? h("em", { text: " · " + sub }) : null]), h("span")]), g]);
      g.hidden = !isOpen;
      li.firstChild.addEventListener("click", function () { toggle(li); });
      items.push(li); return li;
    }
    function toggle(li, to) { var now = li.getAttribute("aria-expanded") === "true", next = to === undefined ? !now : to; li.setAttribute("aria-expanded", next ? "true" : "false"); li.querySelector("[role=group]").hidden = !next; open[li.getAttribute("data-group")] = next; }
    function folderTree(list, lvl, prefix) {
      var dirs = {}, here = [];
      list.forEach(function (f) { var rest = f.rel.slice(prefix.length), cut = rest.indexOf("/"); if (cut < 0) { here.push(f); } else { var d = rest.slice(0, cut); (dirs[d] = dirs[d] || []).push(f); } });
      return Object.keys(dirs).sort().map(function (d) { return groupItem("dir:" + prefix + d, d + "/", dirs[d].length + "", lvl, folderTree(dirs[d], lvl + 1, prefix + d + "/")); })
        .concat(here.sort(function (a, b) { return a.rel < b.rel ? -1 : 1; }).map(function (f) { return fileItem(f, lvl); }));
    }
    function paintTree() {
      tree.textContent = ""; items = [];
      var shown = all.filter(matches);
      if (by === "node") {
        var nodes = []; shown.forEach(function (f) { if (nodes.indexOf(f.node) < 0) { nodes.push(f.node); } });
        nodes.forEach(function (n) {
          var mine = shown.filter(function (f) { return f.node === n; }), cs = [];
          mine.forEach(function (f) { var c = f.loose ? "not committed" : f.commit; if (cs.indexOf(c) < 0) { cs.push(c); } });
          tree.appendChild(groupItem("node:" + n, n, mine.length + (mine.length === 1 ? " file" : " files"), 0, cs.map(function (c) {
            var inC = mine.filter(function (f) { return (f.loose ? "not committed" : f.commit) === c; });
            return groupItem("commit:" + n + c, c, c === "not committed" ? "on disk" : null, 1, inC.map(function (f) { return fileItem(f, 2, f.path); }));
          })));
        });
      } else {
        folderTree(shown.map(function (f) { return Object.assign(f, { rel: f.path.replace(/\\/g, "/").replace(/^[A-Za-z]:\//, "") }); }), 0, "").forEach(function (x) { tree.appendChild(x); });
      }
      (o.missing || []).forEach(function (c) { var li = h("li", { role: "treeitem", tabindex: "-1", "class": "missing", "aria-level": "1", "aria-disabled": "true" }, [h("div", { "class": "ti" }, [h("span", { "class": "fi", "aria-hidden": "true" }), h("span", { "class": "nm", text: "commit " + c }), h("span", { "class": "ch gone", text: "no longer here" })])]); items.push(li); tree.appendChild(li); });
      if (!shown.length) { tree.appendChild(h("li", { role: "none" }, [h("p", { "class": "ls-note", style: "padding: 8px 12px; margin: 0", text: filter ? "No delivered file matches “" + filter + "”." : "This run delivered no files." })])); }
      var cur = tree.querySelector("[aria-selected=true]") || visible()[0]; if (cur) { cur.tabIndex = 0; }
    }
    function visible() { return items.filter(function (li) { return li.offsetParent !== null || !li.closest("[hidden]"); }).filter(function (li) { return !li.closest("[role=group][hidden]"); }); }
    tree.addEventListener("keydown", function (e) {
      var li = e.target.closest && e.target.closest("[role=treeitem]"); if (!li) { return; }
      var list = visible(), i = list.indexOf(li), go = null;
      if (e.key === "ArrowDown") { go = list[i + 1]; } else if (e.key === "ArrowUp") { go = list[i - 1]; } else if (e.key === "Home") { go = list[0]; } else if (e.key === "End") { go = list[list.length - 1]; }
      else if (e.key === "ArrowRight") { if (li.hasAttribute("aria-expanded")) { if (li.getAttribute("aria-expanded") === "false") { toggle(li, true); } else { go = list[i + 1]; } } }
      else if (e.key === "ArrowLeft") { if (li.getAttribute("aria-expanded") === "true") { toggle(li, false); } else { var up = li.parentNode.closest("[role=treeitem]"); if (up) { go = up; } } }
      else if (e.key === "Enter" || e.key === " ") { if (li.hasAttribute("data-key")) { pick(li.getAttribute("data-key"), true); } else if (li.hasAttribute("aria-expanded")) { toggle(li); } }
      else { return; }
      e.preventDefault();
      if (go) { items.forEach(function (x) { x.tabIndex = -1; }); go.tabIndex = 0; go.focus(); }
    });

    function pick(key, fromTree) {
      picked = key; mode = "file"; raw = false;
      items.forEach(function (li) { li.setAttribute("aria-selected", li.getAttribute("data-key") === key ? "true" : "false"); li.tabIndex = li.getAttribute("data-key") === key ? 0 : -1; });
      paintView();
      if (fromTree) { el.classList.add("reading"); }
      if (o.onPick) { o.onPick(byKey[key]); }
    }
    function bar(label, on, fn) { return h("button", { type: "button", "aria-pressed": on ? "true" : "false", text: label, onclick: fn }); }
    function paintView() {
      var f = byKey[picked]; vpath.textContent = ""; vbar.textContent = ""; vbody.textContent = ""; vfoot.textContent = "";
      if (!f) { vbody.appendChild(h("p", { "class": "empty", text: "Pick a file to see it." })); return; }
      var kind = kindOf(f.path);
      vpath.appendChild(h("b", { text: f.path }));
      vpath.appendChild(h("span", { text: f.loose ? [f.node, "reported, not committed", f.bytes === null ? "gone" : size(f.bytes)].join(" · ") + (f.note ? " · " + f.note : "") : [f.node, f.change + " in " + f.commit, f.bytes !== undefined ? size(f.bytes) : null].filter(Boolean).join(" · ") }));
      if (!f.loose && f.change !== "removed") { vbar.appendChild(bar("The file", mode === "file", function () { mode = "file"; paintView(); })); vbar.appendChild(bar("The change", mode === "change", function () { mode = "change"; paintView(); })); }
      if (mode === "file" && kind === "markdown") { vbar.appendChild(bar(raw ? "Rendered" : "Source", false, function () { raw = !raw; paintView(); })); }
      var got = o.read ? o.read(f, mode) : null;
      Promise.resolve(got).then(function (r) { if (picked === f.key) { paintBody(f, kind, r || {}); } }, function (err) { vbody.appendChild(h("p", { "class": "notice bad", text: "✕ It could not be read: " + (err && err.message || err) })); });
      vbody.appendChild(h("p", { "class": "empty", role: "status", text: "Reading…" }));
      var dl = NS.HaloButton({ label: "Download", size: "small", glyph: "↓", disabledReason: f.bytes === null ? "nothing is there now" : (f.change === "removed" ? "removed by this commit" : null), onPress: function () { said.textContent = "✓ Downloading " + base(f.path) + (f.loose ? ", as it is on disk now." : ", as " + f.commit + " left it."); if (o.onDownload) { o.onDownload(f); } } });
      var cp = NS.HaloButton({ label: "Copy path", size: "small", onPress: function () { var full = (o.repository ? o.repository.replace(/[\\/]$/, "") + "/" : "") + f.path; try { navigator.clipboard.writeText(full); } catch (e) { /* the path is on screen either way */ } said.textContent = "✓ Copied " + full; } });
      vfoot.appendChild(dl); vfoot.appendChild(cp);
      vfoot.appendChild(h("code", { "class": "ls-cmd", text: f.loose ? "In a terminal: $ cat " + f.path : "In a terminal: $ git -C " + (o.repository || "<repo>") + " show " + (f.change === "removed" ? f.commit + "~1" : f.commit) + ":" + f.path }));
    }
    function paintBody(f, kind, r) {
      vbody.textContent = "";
      if (r.redacted) { vbody.appendChild(h("p", { "class": "notice needs", text: r.redacted + (r.redacted === 1 ? " line" : " lines") + " that looked like credentials were taken out before it was shown." })); }
      if (f.loose && f.bytes === null) { vbody.appendChild(h("p", { "class": "notice bad", text: "Nothing is there now. " + f.node + " reported it, but it has since gone." })); return; }
      if (r.unchecked) { vbody.appendChild(h("p", { "class": "notice", text: "It could not be checked for credentials in time, so it is not shown. Download it to read it." })); return; }
      if (f.change === "removed") { vbody.appendChild(h("p", { "class": "notice", text: f.commit + " removed it. This is how it was just before." })); }
      if (mode === "change") { vbody.appendChild(r.diff && r.diff.length ? DiffView(r.diff) : h("p", { "class": "empty", text: "No change to show for this file." })); return; }
      if (kind === "image") { vbody.appendChild(h("div", { "class": "ls-img" }, [h("div", { "class": "chk" }, [h("img", { src: r.url, alt: "The image " + base(f.path) + ", as delivered" })]), h("span", { text: [r.width && r.height ? r.width + " × " + r.height : null, size(f.bytes)].filter(Boolean).join(" · ") })])); return; }
      if (kind === "binary" || r.binary) { vbody.appendChild(h("p", { "class": "empty", text: "A binary file (" + (size(f.bytes) || "size unknown") + "), so it is not shown here. Download it to open it." })); return; }
      var t = r.text || "";
      if (r.truncated) { vbody.appendChild(h("p", { "class": "notice", text: "It is " + size(f.bytes) + ", so only its first " + r.truncated.toLocaleString() + " lines are shown. Download it for the rest." })); }
      if (kind === "markdown" && !raw) { vbody.appendChild(Markdown(t)); }
      else if (kind === "csv") { vbody.appendChild(Table(t, ext(f.path) === "tsv" ? "\t" : ",")); }
      else if (kind === "json") { var pretty = t; try { pretty = JSON.stringify(JSON.parse(t), null, 2); } catch (e) { /* shown as written */ } vbody.appendChild(Code(pretty, "json")); }
      else { vbody.appendChild(Code(t, ext(f.path))); }
    }
    paintTree();
    var first = o.picked || (all[0] && all[0].key);
    if (first) { picked = first; items.forEach(function (li) { li.setAttribute("aria-selected", li.getAttribute("data-key") === first ? "true" : "false"); li.tabIndex = li.getAttribute("data-key") === first ? 0 : -1; }); paintView(); } else { paintView(); }
    return el;
  }

  Object.assign(NS, { Outbox: Outbox, Markdown: Markdown, CodeView: Code, CsvTable: Table, fileKind: kindOf });
})();
