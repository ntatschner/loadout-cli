/* @ds-bundle: {"format": 4, "namespace": "LoadoutStudio", "components": [{"name": "HaloButton"}, {"name": "StartRail"}, {"name": "Command"}, {"name": "Switch"}, {"name": "Tabs"}, {"name": "ModePicker"}, {"name": "Signal"}, {"name": "EventMark"}, {"name": "Urgency"}, {"name": "Meter"}, {"name": "Toasts"}, {"name": "SideRail"}, {"name": "Dialog"}, {"name": "Picker"}, {"name": "CommandPalette"}, {"name": "Halo"}, {"name": "TubeMap"}, {"name": "LineStrip"}, {"name": "NetworkStatus"}, {"name": "Terminal"}, {"name": "SessionView"}, {"name": "Gate"}, {"name": "RunCard"}, {"name": "NeedsYou"}, {"name": "RunShape"}, {"name": "Crew"}, {"name": "DoneWhen"}, {"name": "RunNumbers"}, {"name": "RunDepths"}, {"name": "Patch"}, {"name": "SayToNode"}, {"name": "TeamCard"}, {"name": "TeamNodes"}, {"name": "TeamRules"}, {"name": "RoleRecord"}, {"name": "TeamSheet"}, {"name": "Schedules"}, {"name": "Remedy"}, {"name": "Bin"}, {"name": "Outbox"}, {"name": "ModeSwitcher"}, {"name": "ModeBar"}, {"name": "SessionTabs"}, {"name": "TermDeck"}, {"name": "RunBoard"}, {"name": "QuietList"}, {"name": "FocusFrame"}, {"name": "WorkDesk"}, {"name": "ActionPane"}, {"name": "CommandBar"}, {"name": "KeyHelp"}, {"name": "ActionDrawer"}, {"name": "TerminalOnly"}, {"name": "HealthList"}, {"name": "IdeaStages"}, {"name": "QuestionSet"}, {"name": "PlanLayer"}, {"name": "NoteDump"}, {"name": "ProjectTile"}, {"name": "TaskBoard"}, {"name": "LoadStack"}, {"name": "ConfigList"}, {"name": "StoreList"}, {"name": "TopicEditor"}, {"name": "TokenSplit"}, {"name": "SpendBars"}, {"name": "PageBeside"}, {"name": "QuietPage"}, {"name": "ForgetProject"}, {"name": "ProgressLine"}, {"name": "CriteriaGrid"}, {"name": "PickUpSheet"}, {"name": "RefusalsList"}, {"name": "RedAlert"}, {"name": "TaskPicker"}, {"name": "ValueReport"}, {"name": "FieldMap"}, {"name": "OfficeView"}, {"name": "OfficePanel"}, {"name": "ActionForm"}, {"name": "ItemList"}, {"name": "SettingRow"}, {"name": "Readout"}, {"name": "ActionRow"}]} */
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
  var STATES = {"working": ["moving", "play", "breathe"], "running": ["moving", "play", "breathe"], "doing": ["moving", "play", "breathe"], "round running": ["moving", "round", "spin"], "needs a decision": ["needs", "needs", "pulse"], "waiting for you": ["needs", "needs", "pulse"], "failed": ["bad", "failed", "hard"], "last round failed": ["bad", "failed", "hard"], "blocked": ["bad", "blocked", "hard"], "limited": ["bad", "limited", "hard"], "done": ["good", "done", "settle"], "met": ["good", "done", "settle"], "accepted": ["good", "done", "settle"], "idle": ["rest", "idle", "dim"], "open": ["rest", "open", "still"], "captured": ["rest", "open", "still"], "shaping": ["rest", "shaping", "still"], "held": ["rest", "held", "still"], "held after this round": ["rest", "held", "still"], "stopped": ["rest", "stopped", "still"], "not attempted": ["rest", "open", "dashed"], "unknown": ["rest", "unknown", "flicker"], "dropped": ["rest", "dropped", "strike"], "needs approval": ["needs", "needs", "pulse"], "connecting": ["rest", "open", "dashed"], "reconnecting": ["rest", "round", "dashed"], "ended": ["good", "done", "settle"], "exited with an error": ["bad", "failed", "hard"], "only watching": ["rest", "held", "still"], "lost": ["bad", "blocked", "hard"], "unmet": ["bad", "failed", "hard"], "no verdict": ["rest", "unknown", "still"], "quiet": ["needs", "needs", "pulse"], "projected over": ["needs", "limited", "pulse"], "trusted": ["good", "done", "settle"], "not trusted": ["rest", "open", "still"], "changed since trusted": ["needs", "needs", "pulse"], "to put right": ["needs", "needs", "pulse"], "stalled": ["needs", "held", "pulse"], "not approved": ["rest", "open", "still"], "not on this machine": ["rest", "open", "dashed"], "scheduled": ["rest", "held", "still"]};
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
      setTimeout(function () { if (!panel) { return; } var list = focusables(panel), f = o.initialFocus ? panel.querySelector(o.initialFocus) : (o.danger ? list[list.length - 1] : list[0]); if (f) { f.focus(); } }, 60);
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
    if (o.progress && NS.ProgressLine) { el.appendChild(NS.ProgressLine(o.progress)); }
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
    spent: ["limited", "It has spent its budget.", "you raise it, or it stops"],
    stalled: ["stalled", "No done-when has moved for {detail}, so it is held after this round.", "you let it carry on, tell it something, or stop it"]
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
    function tasksHere() { return (o.tasks || []).filter(function (t) { return (t.project || V.project) === V.project && (t.state === "doing" || t.state === "open" || t.state === "blocked") && t.kind !== "idea"; }); }
    function command() {
      if (kind === "make") { return "$ loadout team new " + (V.name || "<name>") + (V.from ? " --from " + V.from : "") + (V.forProject ? " --for-this-project --project " + V.project : ""); }
      if (kind === "schedule") { return "$ loadout team schedule add " + (V.name || "<name>") + " " + (V.team || "<team>") + " " + q(V.goal || "\u2026") + " --project " + V.project + (V.when === "at" ? " --at " + V.at : V.when === "on" ? " --on commit" : " --every " + (V.every || "24h")) + (V.autonomy ? " --autonomy " + V.autonomy : "") + (V.usd ? " --usd " + V.usd : ""); }
      var c = "$ loadout team run " + (V.team || "<team>") + " " + q(V.goal || "\u2026") + " --project " + V.project + (V.task ? " --task " + V.task : "");
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
    function projectPicker() { return NS.Picker({ label: "Project", selected: Math.max(0, projects.map(function (x) { return x.name; }).indexOf(V.project)), options: projects.map(function (x) { return { name: x.name, sub: x.path }; }), onPick: function (x) { V.project = x.name; paintHeld(); if (el._paintTasks) { el._paintTasks(); } paintFoot(); } }); }
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
      var goalBox = text({ lines: 2, value: V.goal, spell: true, placeholder: "for example: make the docs true", onInput: function (v) { V.goal = v; paintFoot(); } });
      var pickBox = h("div", { "class": "taskpick" });
      V.task = p.task || "";
      function paintTasks() {
        pickBox.textContent = "";
        var ts = tasksHere(); if (!ts.length || !NS.TaskPicker) { V.task = ""; return; }
        if (V.task && !ts.some(function (t) { return t.id === V.task; })) { V.task = ""; }
        pickBox.appendChild(NS.TaskPicker({ label: "A task it is for", tasks: ts, selected: V.task || null, otherLabel: "Something else", otherSub: "say what it is for below; the run records a task of its own",
          onPick: function (t) { V.task = t ? t.id : ""; if (t) { V.goal = t.title + (t.note ? "\n\n" + t.note : ""); goalBox.value = V.goal; } paintFoot(); } }));
      }
      el._paintTasks = paintTasks;
      el.appendChild(pickBox);
      el.appendChild(field("What the run is for", goalBox, "With a task, the run moves it to doing, then done or blocked: --task. Its title and note are the goal.", true));
      el.appendChild(h("fieldset", {}, [h("legend", { text: "How it runs" }), NS.ModePicker({ label: "Posture", selected: postureIdx, options: POSTURES, onChange: function (n) { V.autonomy = n === ((team() || {}).posture || "supervised") ? "" : n; paintFoot(); } })]));
      el.appendChild(h("div", { "class": "two" }, [field("Budget, in US dollars", text({ value: V.usd, placeholder: (team() || {}).budget ? "the team's " + team().budget : "a figure, or none", onInput: function (v) { V.usd = v.trim(); paintFoot(); } }), "Or none, for no cap on purpose."), field("Rounds at most", text({ value: V.rounds, type: "number", placeholder: "no limit", onInput: function (v) { V.rounds = v.trim(); paintFoot(); } }), "The budget is the cap that binds.")]));
      el.appendChild(field("Done when, one per line", doneBox, null, true));
      el.appendChild(held);
      el.appendChild(h("fieldset", {}, [h("legend", { text: "When nobody answers" }), h("div", { "class": "two" }, [field("Take the lead's recommendation after", text({ value: V.after, placeholder: "never", onInput: function (v) { V.after = v.trim(); paintFoot(); } }), "30m, 2h or 1d. Only the lead's own questions."), NS.Picker({ label: "What happens then", options: TIMEOUTS, onPick: function (x) { V.timeout = x.name; paintFoot(); } })])]));
      paintAbout(); paintHeld(); paintTasks();
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
  var SVGNS = "http://www.w3.org/2000/svg";
  function icon(d, vb) { var s = document.createElementNS(SVGNS, "svg"); s.setAttribute("viewBox", vb || "0 0 20 20"); s.setAttribute("aria-hidden", "true"); var p = document.createElementNS(SVGNS, "path"); p.setAttribute("d", d); p.setAttribute("fill", "none"); p.setAttribute("stroke", "currentColor"); p.setAttribute("stroke-width", "1.6"); p.setAttribute("stroke-linecap", "round"); p.setAttribute("stroke-linejoin", "round"); s.appendChild(p); return s; }
  function motion() { return NS.motion ? NS.motion() : "full"; }

  var MODES = [
    { key: "default", name: "Default", desc: "Studio as it is: Home, Sessions, Teams and the rest", icon: "M4 10a6 6 0 1 0 12 0a6 6 0 1 0-12 0 M9 10a1 1 0 1 0 2 0a1 1 0 1 0-2 0" },
    { key: "terminals", name: "Terminals", desc: "Terminal Focus: sessions as tabs and panes, the terminal as big as it goes", icon: "M3 4.5h14v11H3z M6 8.2l2.4 1.8L6 11.8 M10.5 12.2h3.5" },
    { key: "teams", name: "Teams", desc: "Team Focus: every live run as a line on one board, each team a click away", icon: "M2.5 6.5h15 M7 6.5v7h4.5 M13 6.5v3.5h4.5" },
    { key: "minimal", name: "Minimal", desc: "One quiet column of what is live; open one and it is all there is", icon: "M5 10h7.5 M15.5 10h.01" }
  ];

  /* morph: run a change of layout as one animated step. A view transition where the browser has one; a rise-in where it doesn't; nothing under no motion. */
  function morph(apply, o) {
    o = o || {};
    var m = motion(), root = document.documentElement;
    if (m === "none") { apply(); return null; }
    if (document.startViewTransition && !o.noVT) {
      if (m !== "full") { root.setAttribute("data-vt", "fade"); }
      var t = document.startViewTransition(function () { apply(); });
      var done = function () { root.removeAttribute("data-vt"); };
      t.finished.then(done, done);
      return t;
    }
    apply();
    var target = o.target || document.getElementById("main");
    if (target) { target.classList.remove("ls-enter", "ls-enter-fade"); void target.offsetWidth; target.classList.add(m === "full" ? "ls-enter" : "ls-enter-fade"); }
    return null;
  }

  /* ModeSwitcher: four modes as radios; the light travels to the one picked. */
  function ModeSwitcher(o) {
    o = o || {};
    var cur = o.mode || "default", light = h("span", { "class": "light", "aria-hidden": "true" });
    var btns = MODES.map(function (m) {
      return h("button", { type: "button", role: "radio", "data-mode": m.key, title: m.desc, "aria-keyshortcuts": "Control+Shift+M", onclick: function () { pick(m.key, true); } }, [icon(m.icon), h("span", { "class": "n", text: m.name })]);
    });
    var el = h("div", { "class": "ls-modes" + (o.compact ? " compact" : "") + (o.vertical ? " vertical" : ""), role: "radiogroup", "aria-label": o.label || "Mode" }, [light].concat(btns));
    var placed = false;
    function place() {
      var b = btns.filter(function (x) { return x.getAttribute("data-mode") === cur; })[0]; if (!b || !b.offsetWidth) { return; }
      /* The first placement lands without travelling, so a new switcher never slides in from the edge. */
      if (!placed) { light.style.transition = "none"; requestAnimationFrame(function () { light.style.transition = ""; }); placed = true; }
      if (o.vertical) { light.style.transform = "translateY(" + b.offsetTop + "px)"; light.style.height = b.offsetHeight + "px"; }
      else { light.style.transform = "translateX(" + b.offsetLeft + "px)"; light.style.width = b.offsetWidth + "px"; }
    }
    function set(k, moved) {
      cur = k;
      btns.forEach(function (b) { var on = b.getAttribute("data-mode") === k; b.setAttribute("aria-checked", on ? "true" : "false"); b.tabIndex = on ? 0 : -1; });
      place();
      if (moved) { el.classList.remove("moved"); void el.offsetWidth; el.classList.add("moved"); }
    }
    function pick(k, focus) { if (k === cur) { return; } set(k, true); if (o.onChange) { o.onChange(k); } if (focus) { var b = el.querySelector('[data-mode="' + k + '"]'); if (b) { b.focus(); } } }
    btns.forEach(function (b, i) {
      b.addEventListener("keydown", function (e) {
        var d = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key]; if (e.key === "Home") { d = -i; } if (e.key === "End") { d = btns.length - 1 - i; }
        if (d === undefined) { return; } e.preventDefault(); pick(MODES[(i + d + MODES.length) % MODES.length].key, true);
      });
    });
    set(cur, false);
    requestAnimationFrame(place);
    if (window.ResizeObserver) { new ResizeObserver(place).observe(el); }
    return { el: el, set: function (k) { set(k, true); }, get mode() { return cur; } };
  }

  /* ModeBar: the switcher, and whatever is waiting on you, so no focus mode ever hides a question. */
  function ModeBar(o) {
    o = o || {};
    var sw = ModeSwitcher({ mode: o.mode, onChange: o.onMode, compact: o.compact });
    var kids = [sw.el, h("span", { "class": "grow" })];
    if (o.onSearch) { kids.push(h("button", { type: "button", "class": "tool", "aria-label": "Search: go to or do", "aria-keyshortcuts": "Control+K Meta+K", title: "Ctrl K", onclick: o.onSearch }, [icon("M8.5 3.5a5 5 0 1 0 0 10a5 5 0 1 0 0-10 M12.2 12.2L16.5 16.5")])); }
    var w = o.waiting || [];
    if (w.length) {
      kids.push(h("button", { type: "button", "class": "waiting", onclick: function () { if (o.onWaiting) { o.onWaiting(w[0]); } } }, [NS.Signal("waiting for you"), h("b", {}, [String(w.length), h("span", { "class": "wn", text: w.length === 1 ? " needs you" : " need you" })]), h("span", { "class": "wl", text: "· " + w[0].label })]));
    } else { kids.push(h("span", { "class": "calmword", text: "nothing waiting on you" })); }
    return { el: h("div", { "class": "ls-modebar", role: "region", "aria-label": "Mode" }, kids), switcher: sw };
  }

  /* SessionTabs: every session as a tab, with its state and whether it has written since you looked. */
  function SessionTabs(o) {
    o = o || {};
    var list = h("div", { role: "tablist", "aria-label": o.label || "Sessions" });
    var tabs = (o.sessions || []).map(function (s, i) {
      var f = NS.signalOf(s.word).family;
      var t = h("button", { type: "button", role: "tab", "class": f === "needs" ? "needs" : null, "aria-selected": s.id === o.current ? "true" : "false", tabindex: s.id === o.current ? "0" : "-1", "aria-controls": o.controls || null, "aria-keyshortcuts": i < 9 ? "Control+Alt+" + (i + 1) : null, "data-id": s.id,
        onclick: function () { if (o.onPick) { o.onPick(s.id); } }, ondblclick: function () { if (o.onMaximise) { o.onMaximise(s.id); } } },
        [NS.Signal(s.word), h("span", { "class": "t", text: s.title }), h("span", { "class": "x", text: [s.where, s.agent].filter(Boolean).join(" · ") }), i < 9 ? h("span", { "class": "k", "aria-hidden": "true", text: "⌃⌥" + (i + 1) }) : null, s.unread ? h("span", { "class": "new", title: "New output since you looked" }, [h("span", { "class": "ls-vh", text: "new output" })]) : null]);
      return t;
    });
    tabs.forEach(function (t) { list.appendChild(t); });
    if (NS.roving) { NS.roving(tabs, function (i) { if (o.onPick) { o.onPick(o.sessions[i].id); } }); }
    var add = o.onNew ? h("button", { type: "button", "class": "add", "aria-label": "New session", title: "New session", onclick: o.onNew, text: "+" }) : null;
    return h("div", { "class": "ls-stabs" }, [list, add]);
  }

  /* TermDeck: Terminal Focus. Tabs over one, two or four panes; Ctrl+Alt+1-9 picks a session, Ctrl+Alt+arrows moves between panes. */
  var LAYOUTS = [[1, "One", "M2 2h16v12H2z"], [2, "Two side by side", "M2 2h16v12H2z M10 2v12"], [4, "Four", "M2 2h16v12H2z M10 2v12 M2 8h16"]];
  function TermDeck(o) {
    o = o || {};
    var layout = o.layout || 1, current = o.current || ((o.sessions || [])[0] || {}).id, terms = {}, panesEl = h("div", { "class": "panes l" + layout, id: "ls-deck-" + Math.random().toString(36).slice(2, 7) });
    var top = h("div", { "class": "dtop" }), bar2 = h("div", { "class": "dtop" }), el = h("section", { "class": "ls-deck", "aria-label": "Terminals" }, [top, bar2, panesEl]);
    var findIn = h("input", { "class": "ls-text", type: "search", placeholder: "Find in this terminal", "aria-label": "Find in the focused terminal", autocomplete: "off" }), findOut = h("span", { role: "status" });
    function order() { return (o.sessions || []).map(function (s) { return s.id; }); }
    function shown() { var ids = order(), out = [current]; ids.forEach(function (id) { if (out.length < layout && out.indexOf(id) < 0) { out.push(id); } }); return out.filter(Boolean); }
    function paintTop() {
      top.textContent = "";
      top.appendChild(SessionTabs({ sessions: o.sessions, current: current, controls: panesEl.id, onPick: function (id) { pick(id); }, onMaximise: function (id) { current = id; setLayout(1); }, onNew: o.onNew }));
      var lay = h("div", { "class": "lay", role: "radiogroup", "aria-label": "Panes" }, LAYOUTS.map(function (L) { return h("button", { type: "button", role: "radio", "aria-checked": L[0] === layout ? "true" : "false", "aria-label": L[1], title: L[1], onclick: function () { setLayout(L[0]); } }, [icon(L[2], "0 0 20 16")]); }));
      top.appendChild(lay);
    }
    function paintBar2() {
      bar2.textContent = "";
      var waiting = (o.sessions || []).filter(function (s) { return NS.signalOf(s.word).family === "needs"; });
      if (waiting.length && NS.HaloButton) { bar2.appendChild(NS.HaloButton({ label: "Go to the one waiting", kind: "needs", size: "small", onPress: function () { pick(waiting[0].id, true); } })); }
      bar2.appendChild(h("div", { "class": "find" }, [findIn, findOut]));
      bar2.appendChild(h("span", { "class": "grow", style: "flex: 1 1 auto" }));
      bar2.appendChild(h("span", { "class": "keys", text: "Ctrl Alt 1–9 picks · Ctrl Alt arrows move · Ctrl ] leaves a terminal" }));
    }
    function paintPanes() {
      var ids = shown();
      panesEl.className = "panes l" + layout; panesEl.textContent = "";
      ids.forEach(function (id) {
        if (!terms[id]) { terms[id] = o.terminal(id); }
        var pane = h("div", { "class": "pane" + (id === current ? " on" : ""), "data-id": id }, [terms[id].el]);
        pane.addEventListener("focusin", function () { if (current !== id) { current = id; mark(); paintTop(); } });
        panesEl.appendChild(pane);
      });
      if (o.onShown) { o.onShown(ids, terms); }
    }
    function mark() { Array.prototype.forEach.call(panesEl.children, function (p) { p.classList.toggle("on", p.getAttribute("data-id") === current); }); }
    function pick(id, focus) {
      var visible = shown().indexOf(id) >= 0; current = id;
      paintTop();
      if (!visible) { paintPanes(); } else { mark(); }
      if (o.onPick) { o.onPick(id); }
      if (focus !== false && terms[id]) { terms[id].focus(); }
    }
    function setLayout(n) { layout = n; NS.morph ? NS.morph(function () { paintTop(); paintPanes(); }, { noVT: true, target: panesEl }) : (paintTop(), paintPanes()); if (o.onLayout) { o.onLayout(n); } }
    el.addEventListener("keydown", function (e) {
      if (!(e.ctrlKey && e.altKey)) { return; }
      var n = parseInt(e.key, 10), ids = order();
      if (n >= 1 && n <= 9 && ids[n - 1]) { e.preventDefault(); pick(ids[n - 1]); return; }
      var d = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key];
      if (d) { e.preventDefault(); var s = shown(), i = s.indexOf(current); pick(s[(i + d + s.length) % s.length]); }
    });
    findIn.addEventListener("input", function () {
      var q = findIn.value.trim().toLowerCase(), pane = panesEl.querySelector('.pane[data-id="' + current + '"]'), scr = pane && pane.querySelector(".screen");
      if (!scr) { return; }
      Array.prototype.forEach.call(scr.querySelectorAll("mark"), function (m) { m.replaceWith(document.createTextNode(m.textContent)); }); scr.normalize();
      if (!q) { findOut.textContent = ""; return; }
      var hits = 0, first = null, walker = document.createTreeWalker(scr, NodeFilter.SHOW_TEXT), nodes = [];
      while (walker.nextNode()) { nodes.push(walker.currentNode); }
      nodes.forEach(function (t) { var i = t.nodeValue.toLowerCase().indexOf(q); if (i < 0) { return; } var r = t.splitText(i); r.splitText(q.length); var m = h("mark", { text: r.nodeValue }); r.replaceWith(m); hits++; if (!first) { first = m; } });
      findOut.textContent = hits ? hits + (hits === 1 ? " match" : " matches") : "no match";
      if (first && first.scrollIntoView) { first.scrollIntoView({ block: "center" }); }
    });
    paintTop(); paintBar2(); paintPanes();
    return { el: el, pick: pick, setLayout: setLayout, terminal: function (id) { return terms[id]; }, update: function (sessions) { o.sessions = sessions; paintTop(); paintBar2(); }, get current() { return current; }, get layout() { return layout; } };
  }

  /* RunBoard: Team Focus. Every live run as a line, the one that needs you first, the teams along the top. */
  function MiniLine(rounds, end, running) {
    var el = h("div", { "class": "ls-mini", "aria-hidden": "true" });
    rounds.forEach(function (r, i) {
      var f = NS.signalOf(r.word || "done").family, cls = r.word === "running" || r.word === "round running" ? "cur" : f === "needs" ? "wait" : f === "bad" ? "bad" : "";
      if (i) { el.appendChild(h("span", { "class": "seg" + (cls === "cur" ? " into" : "") })); }
      el.appendChild(h("span", { "class": "st " + cls }, [h("i"), h("span", { "class": "nd" }, (r.nodes || []).map(function (n) { return h("u", { "class": NS.signalOf(n[1]).family, title: n[0] + " · " + n[1] }); }))]));
    });
    if (running) { el.appendChild(h("span", { "class": "seg dash" })); el.appendChild(h("span", { "class": "st next" }, [h("i")])); }
    else if (end) { el.appendChild(h("span", { "class": "seg" })); el.appendChild(h("span", { "class": "st end" }, [h("i")])); }
    return el;
  }
  function RunBoard(o) {
    o = o || {};
    var el = h("section", { "class": "ls-board", "aria-label": o.label || "Live team runs" });
    if ((o.teams || []).length) {
      el.appendChild(h("div", { "class": "teams", role: "group", "aria-label": "Teams" }, o.teams.map(function (t) {
        return h("button", { type: "button", "aria-pressed": o.team === t.name ? "true" : "false", onclick: function () { if (o.onTeam) { o.onTeam(t.name); } } }, [NS.Signal(t.word || "idle"), t.name, h("em", { text: t.fact || "" })]);
      })));
    }
    var runs = (o.runs || []).slice().sort(function (a, b) { var r = function (x) { var f = NS.signalOf(x.word).family; return f === "needs" ? 0 : f === "bad" ? 1 : f === "moving" ? 2 : 3; }; return r(a) - r(b); });
    var ol = h("ol", { "aria-label": "Runs, the one that needs you first" });
    runs.forEach(function (r, i) {
      var f = NS.signalOf(r.word).family, live = f === "moving" || f === "needs" || r.word === "held after this round";
      var over = r.cap && r.projected > r.cap;
      var row = h("li", { "class": "brow " + f + (o.picked === r.id ? " picked" : ""), style: "--i:" + i }, [
        h("button", { type: "button", "class": "go", "aria-pressed": o.picked === r.id ? "true" : "false", onclick: function () { if (o.onPick) { o.onPick(r.id); } } }, [h("span", { "class": "t", text: r.goal }), h("span", { "class": "x", text: [r.team, r.project, "round " + r.round, (r.rounds || []).filter(function (x) { return x.word === "running"; }).length ? null : null].filter(Boolean).join(" · ") })]),
        MiniLine(r.rounds || [], !live, live),
        NS.Signal(r.word),
        h("span", { "class": "spend" }, [h("b", { text: "$" + Number(r.spent || 0).toFixed(2) }), r.cap ? " of $" + Number(r.cap).toFixed(0) : " no cap", over ? h("em", { text: "projected over" }) : null])
      ]);
      if (r.progress && NS.ProgressLine) { var pl = NS.ProgressLine(Object.assign({ compact: true }, r.progress)); pl.classList.add("bprog"); row.appendChild(pl); }
      if (r.gate && NS.Gate) { row.appendChild(NS.Gate(Object.assign({ compact: true, run: r.id }, r.gate))); }
      ol.appendChild(row);
    });
    if (!runs.length) { ol.appendChild(h("li", { "class": "brow" }, [h("span", { "class": "x", text: "No team runs here." })])); }
    el.appendChild(ol);
    return el;
  }

  /* QuietList: Minimal. One column: what needs you first, with its question; the rest quietly. */
  function QuietList(o) {
    o = o || {};
    var items = (o.items || []).slice(), needs = items.filter(function (x) { var f = NS.signalOf(x.word).family; return f === "needs" || f === "bad"; }), rest = items.filter(function (x) { return needs.indexOf(x) < 0; });
    var ol = h("ol", { "class": "ls-quiet", "aria-label": o.label || "Everything live" }), i = 0;
    function zone(t) { ol.appendChild(h("li", { "class": "zone", "aria-hidden": "true", text: t })); }
    function row(x, calm) {
      var b = h("button", { type: "button", onclick: function (e) { if (o.onOpen) { o.onOpen(x, b.getBoundingClientRect()); } } }, [h("span", { "class": "t", text: x.title }), h("span", { "class": "x", text: [x.kind, x.where].filter(Boolean).join(" · ") }), NS.Signal(x.word)]);
      ol.appendChild(h("li", { "class": calm ? "calm" : "needs", style: "--i:" + (i++) }, [b, !calm && x.ask ? h("p", { "class": "ask", text: x.ask }) : null]));
    }
    if (needs.length) { zone("Needs you"); needs.forEach(function (x) { row(x, false); }); }
    if (rest.length) { zone(needs.length ? "The rest" : "Live"); rest.forEach(function (x) { row(x, true); }); }
    if (!items.length) { ol.appendChild(h("li", { "class": "calm" }, [h("p", { "class": "ask", style: "color: var(--muted)", text: "Nothing is live." })])); }
    return ol;
  }

  /* FocusFrame: one session or one run, the whole screen. It grows out of the line you opened; Escape gives the page back. */
  function FocusFrame(o) {
    o = o || {};
    var back = document.activeElement, m = motion();
    var close = h("button", { type: "button", "class": "close", "aria-keyshortcuts": "Escape", onclick: function () { done(); } }, [h("span", { "aria-hidden": "true", text: "×" }), o.closeLabel || "Back"]);
    var el = h("div", { "class": "ls-focus", role: "region", "aria-label": o.title || "Focus" }, [h("div", { "class": "fhead" }, [h("div", { "class": "who" }, [h("h1", { text: o.title || "" }), o.sub ? h("span", { text: o.sub }) : null]), o.word ? NS.Signal(o.word) : null, close]), h("div", { "class": "fbody" }, [o.content || null])]);
    if (o.from && m === "full") {
      var r = o.from, W = window.innerWidth, H = window.innerHeight;
      el.style.setProperty("--ft", Math.max(0, r.top) + "px"); el.style.setProperty("--fl", Math.max(0, r.left) + "px"); el.style.setProperty("--fr", Math.max(0, W - r.right) + "px"); el.style.setProperty("--fb", Math.max(0, H - r.bottom) + "px");
      el.classList.add("from");
    } else if (m !== "none") { el.classList.add("fade"); }
    function onKey(e) { if (e.key === "Escape" && !e.defaultPrevented && !(e.target && e.target.closest && e.target.closest(".ls-dialog"))) { e.preventDefault(); done(); } }
    function done() { document.removeEventListener("keydown", onKey); el.remove(); if (back && back.focus && document.contains(back)) { back.focus(); } if (o.onClose) { o.onClose(); } }
    document.addEventListener("keydown", onKey);
    (o.host || document.body).appendChild(el);
    setTimeout(function () { var f = o.initialFocus ? el.querySelector(o.initialFocus) : close; if (f) { f.focus(); } }, 40);
    return { el: el, close: done };
  }

  Object.assign(NS, { MODES: MODES, morph: morph, ModeSwitcher: ModeSwitcher, ModeBar: ModeBar, SessionTabs: SessionTabs, TermDeck: TermDeck, MiniLine: MiniLine, RunBoard: RunBoard, QuietList: QuietList, FocusFrame: FocusFrame });
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
  function kbd() { return Array.prototype.map.call(arguments, function (k) { return h("kbd", { "class": "ls-kbd", text: k }); }); }
  function uid(p) { return p + Math.random().toString(36).slice(2, 7); }
  function ack(el) { if (!el) { return; } el.classList.remove("ls-ack"); void el.offsetWidth; el.classList.add("ls-ack"); }
  function typingIn(t) { return t && (t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.tagName === "SELECT" || t.isContentEditable); }

  /* The fifth mode. */
  if (NS.MODES && !NS.MODES.some(function (m) { return m.key === "pure"; })) {
    NS.MODES.push({ key: "pure", name: "Pure", desc: "Pure Focus: list, view and actions in three panes, every key shown, effects off", icon: "M3 4h14v12H3z M8 4v12 M13 4v12" });
  }

  /* ActionPane: the picked thing's actions, numbered 1 to 9, each with its command. A danger action asks for its key a second time. */
  function ActionPane(o) {
    o = o || {};
    var el = h("div", { "class": "ls-apane" }), said = h("p", { "class": "said", role: "status" }), armed = null, timer = null;
    var btns = [];
    function run(i) {
      var a = (o.actions || [])[i], b = btns[i]; if (!a || !b) { return; }
      if (a.disabledReason) { said.className = "said no"; said.textContent = a.label + ": " + a.disabledReason; return; }
      if (a.kind === "danger" && armed !== i) {
        disarm(); armed = i; b.classList.add("arming"); b.querySelector(".l").textContent = "Press " + (i + 1) + " again to " + a.label.toLowerCase();
        said.className = "said no"; said.textContent = "Press " + (i + 1) + " again, or Escape to keep it.";
        timer = setTimeout(disarm, 3000); return;
      }
      disarm(); ack(b);
      var r = a.run ? a.run() : null;
      said.className = "said"; said.textContent = "✓ " + (typeof r === "string" ? r : a.label);
    }
    function disarm() { if (armed === null) { return; } clearTimeout(timer); var b = btns[armed], a = o.actions[armed]; if (b) { b.classList.remove("arming"); b.querySelector(".l").textContent = a.label; } armed = null; if (said.className === "said no") { said.textContent = ""; } }
    (o.actions || []).slice(0, 9).forEach(function (a, i) {
      var b = h("button", { type: "button", "class": "act " + (a.kind || ""), "aria-keyshortcuts": String(i + 1), "aria-disabled": a.disabledReason ? "true" : null, onclick: function () { run(i); } }, kbd(String(i + 1)).concat([h("span", { "class": "l", text: a.label }), h("code", { text: a.disabledReason ? a.disabledReason : (a.cmd || "") })]));
      btns.push(b); el.appendChild(b);
    });
    if (!btns.length) { el.appendChild(h("p", { "class": "empty", text: o.emptyText || "Nothing to do here." })); }
    el.appendChild(said);
    if (o.legend !== false) {
      var keys = h("div", { "class": "keys", "aria-label": "Keys" });
      [[["J", "K"], "move in the list"], [["Enter"], "open it"], [["1", "9"], "run an action"], [["/"], "filter the list"], [[":"], "the command bar"], [["G"], "then a letter: go"], [["F6"], "the next pane"], [["?"], "every key"]].forEach(function (r) { keys.appendChild(h("span", {}, r[0].length === 2 && r[0][0] === "1" ? [kbd("1")[0], "–", kbd("9")[0]] : kbd.apply(null, r[0]))); keys.appendChild(h("span", { text: r[1] })); });
      el.appendChild(keys);
    }
    return { el: el, run: run, disarm: disarm, get armed() { return armed !== null; } };
  }

  /* CommandBar: type an action, a place or a loadout command; every match shows what it runs. */
  function CommandBar(o) {
    o = o || {};
    var lid = uid("ls-cb-"), active = 0, rows = [];
    var list = h("ul", { role: "listbox", id: lid, "aria-label": "Matches", hidden: true });
    var input = h("input", { type: "text", role: "combobox", "aria-expanded": "false", "aria-controls": lid, "aria-autocomplete": "list", "aria-label": "Command bar", placeholder: o.placeholder || "Type an action, a place or a loadout command", autocomplete: "off", spellcheck: "false", "aria-keyshortcuts": ":" });
    function paint() {
      var q = input.value.trim();
      rows = o.source ? o.source(q) : [];
      list.textContent = ""; active = Math.min(active, Math.max(rows.length - 1, 0));
      rows.slice(0, 40).forEach(function (r, i) {
        list.appendChild(h("li", { role: "option", id: lid + "-" + i, "aria-selected": i === active ? "true" : "false", onmousedown: function (e) { e.preventDefault(); active = i; choose(); } }, [h("span", { "class": "k", text: r.kind || "" }), h("span", { "class": "l", text: r.label }), r.cmd ? h("code", { text: r.cmd }) : null]));
      });
      if (!rows.length) { list.appendChild(h("li", { role: "option", "class": "none", "aria-disabled": "true", text: q ? "Nothing matches “" + q + "”." : "Start typing." })); }
      input.setAttribute("aria-activedescendant", rows.length ? lid + "-" + active : "");
      var a = list.querySelector('[aria-selected="true"]'); if (a && a.scrollIntoView) { a.scrollIntoView({ block: "nearest" }); }
    }
    function openList() { list.hidden = false; input.setAttribute("aria-expanded", "true"); paint(); }
    function closeList() { list.hidden = true; input.setAttribute("aria-expanded", "false"); input.removeAttribute("aria-activedescendant"); }
    function choose() { var r = rows[active]; if (!r) { return; } closeList(); input.value = ""; if (o.onDone) { o.onDone(); } if (r.run) { r.run(); } }
    input.addEventListener("focus", openList);
    input.addEventListener("blur", function () { setTimeout(closeList, 120); });
    input.addEventListener("input", function () { active = 0; openList(); });
    input.addEventListener("keydown", function (e) {
      if (e.key === "ArrowDown" || e.key === "ArrowUp") { e.preventDefault(); if (rows.length) { active = (active + (e.key === "ArrowDown" ? 1 : -1) + rows.length) % rows.length; paint(); } }
      else if (e.key === "Enter") { e.preventDefault(); choose(); }
      else if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); input.value = ""; closeList(); if (o.onDone) { o.onDone(); } }
    });
    var hints = h("span", { "class": "hints", "aria-hidden": "true" }, [h("span", {}, kbd(":").concat(["command"])), h("span", {}, kbd("?").concat(["keys"])), h("span", {}, kbd("G").concat(["go"])), h("span", {}, kbd("F6").concat(["panes"]))]);
    var el = h("div", { "class": "ls-cmdbar" }, kbd(":").concat([input, list, hints]));
    return { el: el, input: input, focus: function () { input.focus(); }, get open() { return !list.hidden; } };
  }

  /* KeyHelp: every key in Pure Focus, in a Dialog. */
  var KEYS = [
    ["Move", [[["J", "↓"], "next in the list"], [["K", "↑"], "previous"], [["Home"], "first"], [["End"], "last"], [["/"], "filter the list"]]],
    ["Open and act", [[["Enter"], "open the picked one in the view"], [["Esc"], "back to the list"], [["1"], "to 9: run that action"], [["Ctrl", "]"], "leave a terminal"]]],
    ["Go", [[["G", "H"], "Home: everything"], [["G", "S"], "sessions"], [["G", "T"], "team runs"], [["G", "K"], "tasks"], [["G", "N"], "what needs you"], [["G", "I"], "Ideas"], [["G", "P"], "Projects"], [["G", "C"], "Context"], [["G", "M"], "Memory"], [["G", "U"], "Spend (usage)"], [["G", "O"], "Settings (options)"]]],
    ["Everywhere", [[[":"], "the command bar"], [["F6"], "the next pane"], [["?"], "this list"], [["Ctrl", "Shift", "M"], "change mode"], [["Ctrl", "K"], "the palette"]]]
  ];
  function KeyHelp(o) {
    o = o || {};
    var body = h("div", { "class": "ls-keyhelp" }, (o.groups || KEYS).map(function (g) {
      var dl = h("dl"); g[1].forEach(function (r) { dl.appendChild(h("dt", {}, kbd.apply(null, r[0]))); dl.appendChild(h("dd", { text: r[1] })); });
      return h("section", {}, [h("h3", { text: g[0] }), dl]);
    }));
    if (o.inline) { return body; }
    return NS.Dialog({ title: "Keys in Pure Focus", why: "Single keys work whenever you are not typing. In a terminal every key goes to the agent; Ctrl+] leaves it.", content: body, cancelLabel: "Close" });
  }

  /* WorkDesk: Pure Focus itself. */
  function WorkDesk(o) {
    o = o || {};
    var groups = o.groups || [], selected = o.selected || null, viewing = false, filter = "", gPending = false, gTimer = null, AP = null;
    var lid = uid("ls-wd-");
    var fin = h("input", { type: "search", placeholder: "Filter", "aria-label": "Filter the list", autocomplete: "off", "aria-keyshortcuts": "/" });
    var lb = h("ul", { role: "listbox", "class": "items", id: lid, tabindex: "0", "aria-label": o.label || "Everything live" });
    var listPane = h("section", { "class": "list", "aria-label": "List" }, [h("div", { "class": "ph" }, [h("span", { text: "List" }), h("span", { "class": "grow" }), h("span", { "class": "hint", "aria-hidden": "true" }, kbd("J", "K").concat(["move"]))]), h("div", { "class": "lwrap" }, [h("div", { "class": "filter" }, kbd("/").concat([fin])), lb])]);
    var vhead = h("div", { "class": "ph" }), vbody = h("div", { "class": "vbody", tabindex: "-1" });
    var viewPane = h("section", { "class": "view", "aria-label": "View" }, [vhead, vbody]);
    var actBody = h("div", { style: "display: grid; min-height: 0" });
    var actPane = h("section", { "class": "acts", "aria-label": "Actions" }, [h("div", { "class": "ph" }, [h("span", { text: "Actions" }), h("span", { "class": "grow" }), h("span", { "class": "hint", "aria-hidden": "true" }, kbd("1").concat(["–"], kbd("9")))]), actBody]);
    var CB = CommandBar({ source: function (q) { return o.commands ? o.commands(q, item(selected)) : []; }, onDone: function () { focusList(); } });
    var bar = h("div", { "class": "cbar" }, [CB.el]);
    var el = h("div", { "class": "ls-desk", role: "application", "aria-roledescription": "Pure Focus", "aria-label": "Pure Focus" }, [listPane, viewPane, actPane, bar]);
    function all() { var out = []; groups.forEach(function (g) { g.items.forEach(function (x) { out.push(x); }); }); return out; }
    function item(id) { return all().filter(function (x) { return x.id === id; })[0] || null; }
    function visible() { var q = filter.toLowerCase(); return all().filter(function (x) { return !q || (x.title + " " + (x.meta || "")).toLowerCase().indexOf(q) >= 0; }); }
    function paintList() {
      lb.textContent = ""; var q = filter.toLowerCase(), seen = {};
      groups.forEach(function (g) {
        var xs = g.items.filter(function (x) { return !q || (x.title + " " + (x.meta || "")).toLowerCase().indexOf(q) >= 0; });
        if (!xs.length) { return; }
        lb.appendChild(h("li", { role: "presentation", "class": "gh " + (g.key === "needs" ? "needs" : "") }, [h("span", { text: g.name }), h("span", { text: String(xs.length) })]));
        xs.forEach(function (x) {
          var oid = lid + "-" + g.key + "-" + x.id, fam = NS.signalOf(x.word).family;
          lb.appendChild(h("li", { role: "option", id: oid, "data-id": x.id, "class": fam, "aria-selected": x.id === selected && !seen[x.id] ? "true" : "false", onclick: function () { select(x.id); }, ondblclick: function () { select(x.id); openView(); } },
            [x.word ? NS.Signal(x.word) : h("span", { "aria-hidden": "true" }), h("span", { "class": "t", text: x.title }), h("span", { "class": "x", text: x.meta || "" }), h("span", { "class": "w", text: x.word || "" })]));
          if (x.id === selected) { seen[x.id] = true; }
        });
      });
      if (!lb.children.length) { lb.appendChild(h("li", { role: "presentation", "class": "none", text: filter ? "Nothing matches “" + filter + "”." : "Nothing is live." })); }
      var cur = lb.querySelector('[aria-selected="true"]');
      lb.setAttribute("aria-activedescendant", cur ? cur.id : "");
      if (cur && cur.scrollIntoView) { cur.scrollIntoView({ block: "nearest" }); }
    }
    function paintView() {
      var x = item(selected); vhead.textContent = ""; vbody.textContent = ""; vbody.className = "vbody";
      var back = h("button", { type: "button", "class": "ls-halo small back", onclick: function () { closeView(); } }, [h("span", { "class": "ring", "aria-hidden": "true" }), "List"]);
      if (!x) { vhead.appendChild(h("span", { text: "View" })); vbody.appendChild(h("p", { "class": "none", text: "Pick something in the list." })); return; }
      vhead.appendChild(back);
      vhead.appendChild(h("span", { "class": "vtitle" }, [h("b", { text: x.title }), h("span", { text: x.meta || "" })]));
      if (x.word) { vhead.appendChild(NS.Signal(x.word)); }
      vhead.appendChild(h("span", { "class": "hint", "aria-hidden": "true" }, kbd("Esc").concat(["list"])));
      var v = o.view ? o.view(x) : null;
      if (v && v.el) { if (v.el.classList && v.el.classList.contains("ls-term")) { vbody.className = "vbody term"; } vbody.appendChild(v.el); }
      paintActions(x);
      if (o.onShown) { o.onShown(x, v); }
    }
    function paintActions(x) {
      actBody.textContent = "";
      AP = ActionPane({ actions: x && o.actions ? o.actions(x) : [], emptyText: x ? "Nothing to do with this one." : "Pick something in the list." });
      actBody.appendChild(AP.el);
    }
    function select(id, quiet) {
      if (!id) { return; }
      selected = id; paintList(); paintView();
      if (!quiet) { lb.focus({ preventScroll: true }); }
      if (o.onSelect) { o.onSelect(id); }
    }
    function step(d) { var xs = visible(); if (!xs.length) { return; } var i = xs.map(function (x) { return x.id; }).indexOf(selected); var n = i < 0 ? 0 : Math.max(0, Math.min(xs.length - 1, i + d)); if (d === Infinity) { n = xs.length - 1; } if (d === -Infinity) { n = 0; } select(xs[n].id); }
    function openView() { viewing = true; el.classList.add("viewing"); var t = vbody.querySelector(".ls-term .screen"); if (t) { t.focus(); } else { var f = vbody.querySelector("button, [href], input, textarea, [tabindex='0']"); (f || vbody).focus(); } }
    function closeView() { viewing = false; el.classList.remove("viewing"); focusList(); }
    function focusList() { lb.focus({ preventScroll: true }); }
    function panes() { return [lb, vbody, actPane.querySelector("button") || actPane, CB.input]; }
    function nextPane(d) { var p = panes(), a = document.activeElement, i = p.findIndex(function (x) { return x === a || (x.contains && x.contains(a)); }); var n = p[(i + d + p.length) % p.length]; if (n === vbody) { openView(); return; } if (n) { n.focus(); } }
    fin.addEventListener("input", function () { filter = fin.value.trim(); var xs = visible(); if (xs.length && xs.map(function (x) { return x.id; }).indexOf(selected) < 0) { selected = xs[0].id; paintView(); } paintList(); });
    fin.addEventListener("keydown", function (e) { if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); fin.value = ""; filter = ""; paintList(); focusList(); } else if (e.key === "Enter" || e.key === "ArrowDown") { e.preventDefault(); focusList(); } });
    function onKey(e) {
      if (!document.contains(el)) { return; }
      var t = e.target;
      if (e.key === "F6") { e.preventDefault(); nextPane(e.shiftKey ? -1 : 1); return; }
      if (t && t.closest && (t.closest(".ls-term") || t.closest(".ls-dialog"))) { return; }
      if (typingIn(t)) { return; }
      if (e.ctrlKey || e.metaKey || e.altKey) { return; }
      var k = e.key;
      /* Studio's "Single-letter shortcuts" off: only Escape and the list's own arrows, Home, End and Enter still work. */
      if (o.keys === false && !(k === "Escape" || (t === lb && /^(ArrowUp|ArrowDown|Home|End|Enter)$/.test(k)))) { return; }
      if (gPending) {
        gPending = false; clearTimeout(gTimer);
        var where = (o.goKeys || {})[k.toLowerCase()]; if (where) { e.preventDefault(); if (o.onGo) { o.onGo(where); } }
        return;
      }
      if (k === "Escape") { if (AP && AP.armed) { e.preventDefault(); AP.disarm(); return; } if (viewing || el.contains(t) && t !== lb) { e.preventDefault(); closeView(); } return; }
      if (k === "j" || k === "J" || (k === "ArrowDown" && t === lb)) { e.preventDefault(); step(1); return; }
      if (k === "k" || k === "K" || (k === "ArrowUp" && t === lb)) { e.preventDefault(); step(-1); return; }
      if (t === lb && k === "Home") { e.preventDefault(); step(-Infinity); return; }
      if (t === lb && k === "End") { e.preventDefault(); step(Infinity); return; }
      if (k === "Enter" && t === lb) { e.preventDefault(); openView(); return; }
      if (k === "/") { e.preventDefault(); fin.focus(); fin.select(); return; }
      if (k === ":") { e.preventDefault(); CB.focus(); return; }
      if (k === "?") { e.preventDefault(); KeyHelp().open(); return; }
      if (k === "g" || k === "G") { e.preventDefault(); gPending = true; gTimer = setTimeout(function () { gPending = false; }, 1500); return; }
      if (/^[1-9]$/.test(k) && AP) { e.preventDefault(); AP.run(parseInt(k, 10) - 1); }
    }
    document.addEventListener("keydown", onKey);
    if (!selected) { var first = all()[0]; selected = first ? first.id : null; }
    paintList(); paintView();
    return { el: el, select: select, focusList: focusList, openView: openView, closeView: closeView, refresh: function (g) { groups = g; if (!item(selected)) { var f = all()[0]; selected = f ? f.id : null; paintView(); } paintList(); }, repaintView: paintView, repaintActions: function () { paintActions(item(selected)); }, get selected() { return selected; }, destroy: function () { document.removeEventListener("keydown", onKey); } };
  }

  Object.assign(NS, { WorkDesk: WorkDesk, ActionPane: ActionPane, CommandBar: CommandBar, KeyHelp: KeyHelp, PURE_KEYS: KEYS });
})();

(function () {
  "use strict";
  var NS = window.LoadoutStudio = window.LoadoutStudio || {};
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
  function svg(d, cls) { var v = document.createElementNS(SVGNS, "svg"), p = document.createElementNS(SVGNS, "path"); v.setAttribute("viewBox", "0 0 20 20"); v.setAttribute("aria-hidden", "true"); if (cls) { v.setAttribute("class", cls); } p.setAttribute("d", d); p.setAttribute("fill", "none"); p.setAttribute("stroke", "currentColor"); p.setAttribute("stroke-width", "1.8"); p.setAttribute("stroke-linecap", "round"); p.setAttribute("stroke-linejoin", "round"); v.appendChild(p); return v; }
  function uid(p) { return p + Math.random().toString(36).slice(2, 7); }
  function q(v) { v = String(v); if (/^<[^<>\s]+>$/.test(v)) { return v; } return v === "" || /[\s"'$`\\|&;<>()*?!#~]/.test(v) ? "\"" + v.replace(/(["\\$`])/g, "\\$1") + "\"" : v; }
  /* A disabled button says why in place of its command, and is never the solid one. */
  function halo(o) { return NS.HaloButton(o.disabledReason ? Object.assign({}, o, { cmd: null, solid: false }) : o); }
  function tag(text, kind, glyph) { return h("span", { "class": "ls-tag" + (kind ? " " + kind : "") }, [glyph ? svg(glyph) : null, text]); }
  function num(n) { n = Number(n) || 0; function f(x, u) { return x.toFixed(1).replace(/\.0$/, "") + u; } return n >= 1e9 ? f(n / 1e9, "B") : n >= 1e6 ? f(n / 1e6, "M") : n >= 1e3 ? f(n / 1e3, "k") : String(Math.round(n)); }
  function said(el, text) { el.textContent = text ? "\u2713 " + text : ""; }
  var G = { lock: "M6 9V6.5a4 4 0 0 1 8 0V9 M5 9h10v8H5z", error: "M6 6 L14 14 M14 6 L6 14", warning: "M10 4 V11.5 M10 15 V15.2", info: "M10 9 V14.5 M10 5.6 V5.8", ok: "M5 10.5 L8.5 14 L15 6.5", fix: "M12.5 3.5a3.5 3.5 0 0 0-3.3 4.6L3.5 13.8l2.7 2.7 5.7-5.7a3.5 3.5 0 0 0 4.6-3.3l-2 2-2-.5-.5-2z", chev: "M7 4 L13 10 L7 16", term: "M3 4h14v12H3z M6 8l3 2-3 2 M10.5 12.5h3.5" };
  var EFFECT = { reads: ["reads only", ""], changes: ["changes something", "accent"], session: ["starts a session", "needs"], terminal: ["terminal only", ""] };

  /* ActionDrawer: one loadout command as a closed drawer. Opened, it is the command's form, the exact line it will run, and its button. */
  function lineOf(path, fields, values, extra) {
    var words = [], ordered = (fields || []).filter(function (f) { return f.name.slice(0, 2) !== "--"; }).concat((fields || []).filter(function (f) { return f.name.slice(0, 2) === "--"; }));
    ordered.forEach(function (f) {
      var v = values[f.name];
      if (f.kind === "flag") { if (v) { words.push(f.name); } return; }
      if (v === undefined || v === null || v === "") { return; }
      if (f.name.slice(0, 2) === "--") { if (f.joined) { words.push(f.name + "=" + v); } else { words.push(f.name, v); } } else { words.push(v); }
    });
    return "$ loadout " + [path].concat(words.concat(extra || []).map(function (w) { return /^--[\w-]+(=|$)/.test(w) && w.indexOf("=") < 0 ? w : (/^--[\w-]+=/.test(w) ? w.split("=")[0] + "=" + q(w.slice(w.indexOf("=") + 1)) : q(w)); })).join(" ");
  }
  function ActionDrawer(o) {
    o = o || {};
    var values = {}, fields = o.fields || [], open = !!o.open, bid = uid("ls-dr-");
    fields.forEach(function (f) { values[f.name] = f.value !== undefined ? f.value : (f.kind === "flag" ? false : ""); });
    var effect = o.effect || "changes", ew = EFFECT[effect] || EFFECT.changes;
    function reads() {
      if (effect === "reads") { return true; }
      if (effect !== "changes" || !o.changesOnlyWith) { return false; }
      return !o.changesOnlyWith.some(function (n) { return !!values[n]; });
    }
    var el = h("section", { "class": "ls-drawer", "data-open": open ? "true" : "false", "data-effect": effect });
    var top = h("button", { type: "button", "class": "top", "aria-expanded": open ? "true" : "false", "aria-controls": bid, onclick: function () { set(!open); } },
      [svg(G.chev, "chev"), h("span", { "class": "t", text: o.title || o.path }), h("code", { text: "loadout " + o.path }), tag(ew[0], ew[1], effect === "terminal" ? G.lock : null)]);
    var body = h("div", { "class": "body", id: bid, role: "group", "aria-label": o.title || o.path });
    el.appendChild(top); el.appendChild(body);
    function set(v) { open = v; el.setAttribute("data-open", v ? "true" : "false"); top.setAttribute("aria-expanded", v ? "true" : "false"); if (v && o.onOpen) { o.onOpen(); } }
    if (o.why) { body.appendChild(h("p", { "class": "why", text: o.why })); }
    if (effect === "terminal") {
      body.appendChild(TerminalOnly({ title: "Studio does not run this", why: "It " + (o.reason || "is only ever done in a terminal") + ".", command: o.command || "$ loadout " + o.path, bare: true }));
      return { el: el, open: function () { set(true); }, close: function () { set(false); } };
    }
    var line = h("code", { "class": "ls-cmdline", "aria-live": "polite" }), acts = h("div", { "class": "ls-answers" }), out = h("p", { "class": "ls-said", role: "status" });
    if (fields.length) {
      var box = h("div", { "class": "fields" });
      fields.forEach(function (f) {
        var id = uid("ls-fd-"), lab = [f.label || f.name, " ", f.name.slice(0, 2) === "--" && f.kind !== "flag" ? h("code", { text: f.name }) : null];
        if (f.kind === "flag") {
          var cb = h("input", { type: "checkbox", id: id, disabled: f.fixed ? true : null, onchange: function () { values[f.name] = cb.checked; paint(); } }); cb.checked = !!values[f.name];
          box.appendChild(h("label", { "class": "flag" + (f.wide ? " wide" : ""), "for": id }, [cb, h("span", { text: f.label || f.name }), h("code", { text: f.name })])); return;
        }
        var inp;
        if (f.kind === "choice") { inp = h("select", { "class": "ls-text", id: id, disabled: f.fixed ? true : null, onchange: function () { values[f.name] = inp.value; paint(); } }, (f.options || []).map(function (op) { return h("option", { value: op, text: op === "" ? "(none)" : op }); })); inp.value = values[f.name]; }
        else { inp = h(f.lines ? "textarea" : "input", { "class": "ls-text", id: id, rows: f.lines || null, placeholder: f.placeholder || null, autocomplete: "off", spellcheck: "false", readonly: f.fixed ? true : null, oninput: function () { values[f.name] = inp.value; paint(); } }); inp.value = values[f.name]; }
        box.appendChild(h("div", { "class": "ls-field" + (f.wide || f.lines ? " wide" : "") }, [h("label", { "class": "lab", "for": id }, lab), inp, f.hint ? h("span", { "class": "hint", text: f.hint }) : null]));
      });
      body.appendChild(box);
    }
    body.appendChild(line); body.appendChild(acts); body.appendChild(out);
    if (o.note) { body.appendChild(h("p", { "class": "note", text: o.note })); }
    function missing() { var m = fields.filter(function (f) { return f.required && !values[f.name]; })[0]; return m ? "say " + (m.label || m.name).toLowerCase() + " first" : null; }
    function run(preview) {
      var l = lineOf(o.path, fields, values, preview ? ["--dry-run"] : []);
      var r = o.onRun ? o.onRun(Object.assign({}, values), { preview: !!preview, line: l, reads: preview || reads() }) : null;
      said(out, typeof r === "string" ? r : (preview ? "Previewed: nothing changed" : "Ran it"));
    }
    function paint() {
      line.textContent = lineOf(o.path, fields, values);
      acts.textContent = "";
      var dis = missing() || null, r = reads();
      if (effect === "session") {
        acts.appendChild(halo({ label: o.label || "Open it in a terminal", size: "small", glyph: "\u25b8", cmd: "opens a terminal here, hosted by the daemon", disabledReason: o.disabledReason || dis, onPress: function () { run(false); } }));
        return;
      }
      if (!r && o.dryRun) { acts.appendChild(halo({ label: "Preview", size: "small", cmd: "adds --dry-run \u00b7 changes nothing", disabledReason: dis, onPress: function () { run(true); } })); }
      acts.appendChild(halo({ label: o.label || "Run", size: "small", solid: !r, kind: o.danger ? "danger" : "", cmd: r ? "reads only" : "changes something", disabledReason: (r ? null : o.disabledReason) || dis, onPress: function () { run(false); } }));
    }
    paint();
    return { el: el, open: function () { set(true); }, close: function () { set(false); }, values: values, line: function () { return lineOf(o.path, fields, values); } };
  }
  function ActionDrawers(groups) {
    var box = h("div", { "class": "ls-drawers" });
    groups.forEach(function (g) { if (g.name) { box.appendChild(h("h4", { "class": "gh", text: g.name })); } g.items.forEach(function (d) { box.appendChild((d.el ? d : ActionDrawer(d)).el); }); });
    return box;
  }

  /* TerminalOnly: something only ever done where you type. The command and why, and a copy, never a button that does it. */
  function TerminalOnly(o) {
    o = o || {};
    var out = h("p", { "class": "ls-said", role: "status" });
    var el = h("div", { "class": "ls-tonly" }, [
      h("div", { "class": "top" }, [h("b", { text: o.title || "Done in a terminal" }), tag("terminal only", "", G.lock)]),
      o.why ? h("p", { "class": "why", text: o.why }) : null,
      h("div", { "class": "line" }, [h("code", { text: o.command || "" }), halo({ label: "Copy", size: "small", cmd: "copies the line \u00b7 runs nothing", onPress: function () { try { navigator.clipboard.writeText((o.command || "").replace(/^\$ /, "")); said(out, "Copied. Paste it in a terminal."); } catch (e) { said(out, "Select the line to copy it."); } } })]),
      o.fact ? h("p", { "class": "fact", text: o.fact }) : null, out]);
    return el;
  }

  /* HealthList: findings, worst first, each with its severity as a word and a glyph; what can be put right says so. */
  var SEV = { error: 0, bad: 0, warning: 1, warn: 1, info: 2, ok: 3, pass: 3 };
  function sevOf(s) { s = String(s || "info").toLowerCase(); return s === "bad" ? "error" : s === "warn" ? "warning" : s === "pass" ? "ok" : (SEV[s] === undefined ? "info" : s); }
  function HealthList(o) {
    o = o || {};
    var f = (o.findings || []).slice().sort(function (a, b) { return SEV[sevOf(a.severity)] - SEV[sevOf(b.severity)]; });
    var bad = f.filter(function (x) { return sevOf(x.severity) === "error"; }).length, warn = f.filter(function (x) { return sevOf(x.severity) === "warning"; }).length, fix = f.filter(function (x) { return x.fixable; }).length;
    var el = h("div", { "class": "ls-health" });
    if (o.verdict !== undefined || f.length) {
      var parts = [];
      if (o.verdict) { parts.push(h("span", {}, ["The audit says ", h("b", { text: String(o.verdict).toLowerCase() }), ". "])); }
      if (bad || warn) { parts.push((bad ? bad + " error" + (bad === 1 ? "" : "s") : "") + (bad && warn ? " and " : "") + (warn ? warn + " warning" + (warn === 1 ? "" : "s") : "") + (fix ? "; " + fix + " can be put right." : ".")); }
      el.appendChild(h("p", { "class": "sum" }, parts));
    }
    if (!f.length) { el.appendChild(h("p", { "class": "none", text: "\u2713 " + (o.noneText || "Nothing to put right.") })); }
    else {
      el.appendChild(h("ul", {}, f.map(function (x) {
        var s = sevOf(x.severity);
        return h("li", { "class": s }, [svg(G[s] || G.info, "g"), h("span", { "class": "n" }, [h("span", { text: s === "ok" ? "fine" : s }), x.name || ""]), x.detail ? h("span", { "class": "d", text: x.detail }) : null, x.fixable ? tag("can be put right", "accent", G.fix) : null]);
      })));
    }
    if (o.from) { el.appendChild(h("p", { "class": "ls-from" }, ["From ", h("code", { text: o.from }), "."])); }
    return el;
  }

  /* IdeaStages: the five stages an idea passes through, with where it is now and why. */
  var STAGES = ["Captured", "Answering", "Ready", "Proposed", "Accepted"];
  function IdeaStages(o) {
    o = o || {};
    var at = Math.max(0, STAGES.map(function (s) { return s.toLowerCase(); }).indexOf(String(o.stage || "captured").toLowerCase()));
    var fam = NS.signalOf ? NS.signalOf(o.word || "").family : "";
    var ol = h("ol", { "class": "ls-istages", "aria-label": "Where it stands" }, STAGES.map(function (s, i) {
      var now = i === at;
      return h("li", { "class": (i < at ? "past" : "") + (now ? " now " + (fam === "needs" ? "needs" : fam === "bad" ? "bad" : "") : ""), "aria-current": now ? "step" : null, "aria-label": s }, [h("span", { "class": "dot", "aria-hidden": "true" }), h("span", { "class": "nm", text: s }), now && o.word ? h("span", { "class": "w", text: o.word }) : null]);
    }));
    /* On a phone the five names do not fit: the dots stay, and one line says where it is. */
    return h("div", { "class": "ls-istages-wrap" }, [ol, h("p", { "class": "cap", "aria-hidden": "true" }, ["Stage " + (at + 1) + " of 5: ", h("b", { text: STAGES[at] }), o.word ? " \u00b7 " + o.word : ""])]);
  }

  /* QuestionSet: the questions an agent asked about an idea, answered here, then sent together, with or without another round. */
  function QuestionSet(o) {
    o = o || {};
    var qs = o.questions || [], drafts = {}, editing = {}, where = o.where || [], id = o.id || "<id>";
    var el = h("div", { "class": "ls-qset" }), list = h("div", { "class": "qs", style: "display: grid; gap: 10px" }), send = h("div", { "class": "send" }), out = h("p", { "class": "ls-said", role: "status" });
    el.appendChild(list); el.appendChild(send);
    function changed() { return qs.filter(function (x) { var d = (drafts[x.id] || "").trim(); return d && d !== (x.answer || ""); }); }
    function answerLine(x) { return lineOf("idea answer", [], {}, [id, x.id, "--answer=" + drafts[x.id].trim()].concat(where)); }
    function paintSend() {
      send.textContent = "";
      var c = changed(), n = c.length, dis = o.disabledReason || (n ? null : "answer a question first");
      send.appendChild(h("p", { "class": "count", text: n ? n + " answer" + (n === 1 ? "" : "s") + " ready to send." : "Answer what you can. Nothing is sent until you press a button." }));
      var row = h("div", { "class": "ls-answers" });
      row.appendChild(halo({ label: "Send answers and refine", kind: "needs", solid: true, glyph: "\u2713", cmd: n + " \u00d7 loadout idea answer, then " + lineOf("idea refine", [], {}, [id].concat(where)), disabledReason: dis, onPress: function () { go(true); } }));
      row.appendChild(halo({ label: "Only save the answers", cmd: n + " \u00d7 loadout idea answer", disabledReason: dis, onPress: function () { go(false); } }));
      send.appendChild(row);
      if (n) { var d = h("details", {}, [h("summary", { text: "The commands, one per answer" })]); c.forEach(function (x) { d.appendChild(h("code", { text: answerLine(x) })); }); send.appendChild(d); }
      send.appendChild(out);
    }
    function go(refine) {
      var c = changed(), lines = c.map(answerLine);
      if (o.onSend) { o.onSend(c.map(function (x) { return { question: x.id, answer: drafts[x.id].trim(), line: answerLine(x) }; }), refine, lines); }
      c.forEach(function (x) { x.answer = drafts[x.id].trim(); delete drafts[x.id]; editing[x.id] = false; });
      paint(); said(out, c.length + " answer" + (c.length === 1 ? "" : "s") + " saved" + (refine ? ". The next round has started: it runs on the daemon, and a toast says when it ends." : "."));
    }
    function paintQ(x, i) {
      var answered = x.answer && !editing[x.id], dr = (drafts[x.id] || "").trim(), cls = "q" + (answered ? " answered" : dr ? " drafted" : "");
      var qid = uid("ls-qq-"), card = h("section", { "class": cls, "aria-labelledby": qid });
      card.appendChild(h("span", { "class": "who", text: [x.id, answered ? "answered" : dr ? "your answer, not sent yet" : "the agent asks"].join(" \u00b7 ") }));
      if (answered) {
        card.appendChild(h("p", { "class": "qt", id: qid, text: x.question }));
        card.appendChild(h("p", { "class": "was" }, ["You answered: ", h("b", { text: x.answer }), h("button", { type: "button", onclick: function () { editing[x.id] = true; drafts[x.id] = x.answer; repaint(i); }, text: "Change it" })]));
        return card;
      }
      var fs = h("fieldset", {}, [h("legend", { id: qid, text: x.question })]);
      if (x.why) { fs.appendChild(h("p", { "class": "why", text: x.why })); }
      var name = uid("ls-qo-"), opts = h("div", { "class": "opts" });
      (x.options || []).forEach(function (op) {
        var r = h("input", { type: "radio", name: name, value: op, onchange: function () { drafts[x.id] = op; own.value = ""; paintSend(); card.className = "q drafted"; } });
        r.checked = drafts[x.id] === op;
        opts.appendChild(h("label", { "class": "opt" }, [r, op, x.recommendation === op ? h("span", { "class": "rec", text: "recommended" }) : null]));
      });
      fs.appendChild(opts);
      if (x.recommendation && (x.options || []).indexOf(x.recommendation) < 0) { fs.appendChild(h("p", { "class": "why", text: "Recommended: " + x.recommendation })); }
      var oid = uid("ls-qw-"), own = h("textarea", { "class": "ls-text", id: oid, rows: 2, placeholder: "Or say it in your own words", oninput: function () { drafts[x.id] = own.value; Array.prototype.forEach.call(opts.querySelectorAll("input"), function (r) { r.checked = r.value === own.value; }); card.className = own.value.trim() ? "q drafted" : "q"; paintSend(); } });
      if (drafts[x.id] && (x.options || []).indexOf(drafts[x.id]) < 0) { own.value = drafts[x.id]; }
      fs.appendChild(h("div", { "class": "own" }, [h("label", { "for": oid, text: "Or in your own words" }), own]));
      card.appendChild(fs);
      return card;
    }
    function repaint(i) { var old = list.children[i], nu = paintQ(qs[i], i); list.replaceChild(nu, old); paintSend(); var f = nu.querySelector("input, textarea"); if (f) { f.focus(); } }
    function paint() { list.textContent = ""; qs.forEach(function (x, i) { list.appendChild(paintQ(x, i)); }); paintSend(); }
    paint();
    return { el: el, get changed() { return changed(); } };
  }

  /* PlanLayer: one layer of an idea's plan. Its options side by side with what is for and against each; choose one, keep or drop the layer, or ask for it to be reworked. An addition is drawn the same way, without options. */
  function PlanLayer(o) {
    o = o || {};
    var L = o.layer || o.addition || {}, add = !!o.addition, id = o.idea || "<id>", where = o.where || [], dis = o.disabledReason || null, lock = !!o.accepted;
    var verdict = String(L.verdict || "Undecided");
    var el = h("section", { "class": "ls-layer" + (add ? " addition" : "") + (verdict === "Drop" ? " dropped" : ""), "aria-label": (add ? "Addition " : "Layer ") + L.id + ": " + (L.name || L.title) });
    var out = h("p", { "class": "ls-said", role: "status" });
    var vk = { Keep: "good", Drop: "", Improve: "needs", Undecided: "" }[verdict];
    el.appendChild(h("div", { "class": "lh" }, [h("span", { "class": "id", text: L.id }), h("h4", { text: L.name || L.title }), tag("verdict: " + verdict.toLowerCase(), vk)]));
    if (add) { el.appendChild(h("p", { "class": "why", text: L.why || "" })); }
    else if (L.purpose) { el.appendChild(h("p", { "class": "purpose", text: L.purpose })); }
    if (!add) {
      var grid = h("div", { "class": "opts" });
      (L.options || []).forEach(function (op) {
        var chosen = L.chosen === op.id, card = h("article", { "class": "card" + (chosen ? " chosen" : ""), "aria-label": op.title + (chosen ? ", chosen" : "") });
        card.appendChild(h("div", { "class": "ct" }, [h("span", { "class": "id", text: op.id }), h("b", { text: op.title }), op.recommended ? tag("recommended", "accent") : null, chosen ? tag("chosen", "good", G.ok) : null]));
        if (op.detail) { card.appendChild(h("p", { text: op.detail })); }
        var pc = (op.pros || []).map(function (p) { return h("li", { "class": "pro" }, [h("i", { "aria-hidden": "true", text: "+" }), h("span", {}, [h("span", { "class": "ls-vh", text: "For: " }), p])]); })
          .concat((op.cons || []).map(function (p) { return h("li", { "class": "con" }, [h("i", { "aria-hidden": "true", text: "\u2212" }), h("span", {}, [h("span", { "class": "ls-vh", text: "Against: " }), p])]); }));
        if (pc.length) { card.appendChild(h("ul", {}, pc)); }
        if (!chosen && !lock) { card.appendChild(halo({ label: "Choose this", size: "small", cmd: lineOf("idea choose", [], {}, [id, L.id, op.id].concat(where)), disabledReason: dis, onPress: function () { if (o.onChoose) { o.onChoose(op.id); } } })); }
        grid.appendChild(card);
      });
      el.appendChild(grid);
    }
    if (!lock) {
      var acts = h("div", { "class": "acts" }), req = "", rw = h("div", { "class": "rework" }), ta = h("textarea", { "class": "ls-text", rows: 2, placeholder: "What should change, in a sentence", "aria-label": "What should change about " + L.id });
      acts.appendChild(halo({ label: "Keep it", size: "small", glyph: "\u2713", cmd: lineOf("idea keep", [], {}, [id, L.id].concat(where)), disabledReason: dis || (verdict === "Keep" ? "kept already" : null), onPress: function () { if (o.onVerdict) { o.onVerdict("Keep"); } said(out, "Kept " + L.id); } }));
      acts.appendChild(halo({ label: "Drop it", size: "small", glyph: "\u2715", cmd: lineOf("idea drop", [], {}, [id, L.id].concat(where)), disabledReason: dis || (verdict === "Drop" ? "dropped already" : null), onPress: function () { if (o.onVerdict) { o.onVerdict("Drop"); } said(out, "Dropped " + L.id); } }));
      acts.appendChild(halo({ label: "Ask for it to be reworked", size: "small", glyph: "\u21bb", cmd: "says what should change, then sends it", disabledReason: dis, onPress: function () { rw.classList.toggle("on"); if (rw.classList.contains("on")) { ta.focus(); } } }));
      var send = h("div", {});
      function paintRw() { send.textContent = ""; send.appendChild(halo({ label: "Send it back", size: "small", kind: "needs", cmd: lineOf("idea improve", [], {}, [id, L.id, "--request=" + (req || "<what should change>")].concat(where)), disabledReason: req ? dis : "say what should change", onPress: function () { if (o.onImprove) { o.onImprove(req); } rw.classList.remove("on"); said(out, "Sent " + L.id + " back to be reworked"); } })); }
      ta.addEventListener("input", function () { req = ta.value.trim(); paintRw(); });
      paintRw(); rw.appendChild(ta); rw.appendChild(send);
      el.appendChild(acts); el.appendChild(rw);
    }
    el.appendChild(out);
    return el;
  }

  /* NoteDump: notes pasted from elsewhere, split by an agent into ideas and tasks; tick the pieces to record. */
  function NoteDump(o) {
    o = o || {};
    var d = o.dump || {}, picked = {}, out = h("p", { "class": "ls-said", role: "status" });
    (d.items || []).forEach(function (it) { if (!it.recorded) { picked[it.number] = true; } });
    var el = h("section", { "class": "ls-dump", "aria-label": "Notes " + d.id });
    el.appendChild(h("div", { "class": "dh" }, [h("b", { text: d.source || d.id }), h("span", { text: d.id + " \u00b7 " + (d.captured || "") + (d.split ? " \u00b7 split " + d.split : " \u00b7 not split yet") })]));
    var ol = h("ol", {});
    (d.items || []).forEach(function (it) {
      var cb = h("input", { type: "checkbox", disabled: it.recorded ? true : null, onchange: function () { picked[it.number] = cb.checked; paint(); } }); cb.checked = !!picked[it.number];
      ol.appendChild(h("li", { "class": it.recorded ? "recorded" : "" }, [h("label", {}, [cb, h("span", { "class": "no", text: String(it.number) }), h("span", { "class": "t", text: it.title }), it.excerpt ? h("span", { "class": "x", text: it.excerpt }) : null, tag(it.recorded ? it.kind + " \u00b7 recorded" : it.kind, it.recorded ? "good" : "")])]));
    });
    el.appendChild(ol);
    var acts = h("div", { "class": "acts" }); el.appendChild(acts); el.appendChild(out);
    function only() { return Object.keys(picked).filter(function (k) { return picked[k]; }).sort(function (a, b) { return a - b; }); }
    function paint() {
      acts.textContent = ""; var ks = only(), all = (d.items || []).filter(function (i) { return !i.recorded; }).length;
      acts.appendChild(halo({ label: ks.length ? "Record " + ks.length + (ks.length === all ? ", all of them" : "") : "Record them", size: "small", solid: true, cmd: lineOf("idea dump apply", [], {}, [d.id].concat(ks.length && ks.length < all ? ["--only", ks.join(",")] : [])), disabledReason: o.disabledReason || (ks.length ? null : "tick a piece first"), onPress: function () { if (o.onApply) { o.onApply(ks); } said(out, "Recorded " + ks.length); } }));
      acts.appendChild(halo({ label: "Split it again", size: "small", glyph: "\u21bb", cmd: lineOf("idea dump split", [], {}, [d.id]), disabledReason: o.disabledReason, onPress: function () { if (o.onSplit) { o.onSplit(); } said(out, "Splitting again: an agent reads it once more"); } }));
      acts.appendChild(halo({ label: "Read it whole", size: "small", cmd: lineOf("idea dump show", [], {}, [d.id]) + " \u00b7 reads only", onPress: function () { if (o.onShow) { o.onShow(); } } }));
    }
    paint();
    return el;
  }

  /* ProjectTile: one project as a tile on the Projects wall. */
  function ProjectTile(o) {
    o = o || {};
    var p = o.project || {}, away = p.available === false;
    if (o.place) { return h("a", { "class": "ls-ptile place", href: o.href || "#" }, [h("span", { "class": "nm", text: " " + o.place }), o.sub ? h("span", { "class": "slug", text: o.sub }) : null]); }
    var facts = [];
    if (away) { facts.push(tag("not on this machine", "")); }
    else if (p.branch) { facts.push(tag(p.branch + (p.clean === false ? " \u00b7 uncommitted" : " \u00b7 clean"), p.clean === false ? "needs" : "")); }
    facts.push(tag(p.defaultAgent || "claude"));
    var counts = [];
    if (o.doing !== undefined) { counts.push(h("span", {}, [h("b", { text: String(o.doing) }), " doing"])); }
    if (o.blocked) { counts.push(h("span", { "class": "needs" }, [h("b", { text: String(o.blocked) }), " blocked"])); }
    if (o.findings !== undefined) { counts.push(h("span", { "class": o.findings ? "needs" : "" }, o.findings ? [h("b", { text: String(o.findings) }), " to put right"] : ["\u2713 healthy"])); }
    if (o.ideas) { counts.push(h("span", {}, [h("b", { text: String(o.ideas) }), " idea" + (o.ideas === 1 ? "" : "s")])); }
    return h("a", { "class": "ls-ptile" + (away ? " away" : ""), href: o.href || "#", "aria-current": o.picked ? "page" : null }, [
      h("span", { "class": "nm" }, [p.name || p.id, p.pinned ? h("span", { "class": "pin", text: "pinned" }) : null]),
      h("span", { "class": "slug", text: p.id + (away ? "" : " \u00b7 " + (p.path || "")) }),
      h("span", { "class": "facts" }, facts), counts.length ? h("span", { "class": "counts" }, counts) : null]);
  }

  /* TaskBoard: a project's tasks by where they stand, each moved on with the command that says so. */
  var NEXT = { open: ["doing", "blocked", "dropped"], doing: ["done", "blocked", "open"], blocked: ["doing", "dropped"], done: ["open"], dropped: ["open"] };
  var MOVE = { doing: "Start it", done: "Mark it done", blocked: "It is blocked", dropped: "Drop it", open: "Reopen it" };
  function TaskBoard(o) {
    o = o || {};
    var tasks = o.tasks || [], where = o.where || [], el = h("div", { "class": "ls-tboard" });
    function card(t) {
      var s = h("p", { "class": "said", role: "status" }), moves = h("div", { "class": "moves" });
      (NEXT[t.state] || []).forEach(function (n) { moves.appendChild(halo({ label: MOVE[n], size: "small", kind: n === "dropped" ? "danger" : "", cmd: lineOf("task declare", [], {}, [t.id, n].concat(where)), disabledReason: o.disabledReason, onPress: function () { var was = t.state; t.state = n; if (o.onDeclare) { o.onDeclare(t, n, was); } paint(); } })); });
      var go = null;
      if ((o.onSession || o.onRun) && t.kind !== "idea" && (t.state === "open" || t.state === "doing" || t.state === "blocked")) {
        go = h("div", { "class": "go" });
        if (o.onSession) { go.appendChild(halo({ label: "Start a session on it", size: "small", glyph: "\u25b8", cmd: "opens the launch sheet with this task \u00b7 --task-id=" + t.id, disabledReason: o.disabledReason, onPress: function () { o.onSession(t); } })); }
        if (o.onRun) { go.appendChild(halo({ label: "Run a team on it", size: "small", glyph: "\u25b6", cmd: "opens Start a run with this task \u00b7 --task " + t.id, disabledReason: o.disabledReason || (/^team /.test(t.declaredBy || "") && t.state === "doing" ? "a team run is on it already" : null), onPress: function () { o.onRun(t); } })); }
      }
      return h("article", { "class": "task " + t.state, "aria-label": t.title + ", " + t.state }, [h("span", { "class": "tt", text: t.title }), h("span", { "class": "tx", text: [t.id, t.kind, t.declaredBy, t.declared].filter(Boolean).join(" \u00b7 ") }), t.note ? h("p", { "class": "note", text: t.note }) : null, moves, go, s]);
    }
    function paint() {
      el.textContent = "";
      var cols = h("div", { "class": "cols" });
      [["open", "Open"], ["doing", "Doing"], ["blocked", "Blocked"]].forEach(function (c) {
        var ts = tasks.filter(function (t) { return t.state === c[0]; }), sid = uid("ls-tc-");
        var col = h("section", { "class": "col", "aria-labelledby": sid }, [h("h4", { id: sid }, [NS.Signal ? NS.Signal(c[0]) : null, c[1], h("span", { "class": "n", text: String(ts.length) })])]);
        if (!ts.length) { col.appendChild(h("p", { "class": "empty", text: { open: "Nothing waiting to start.", doing: "Nothing under way.", blocked: "Nothing blocked." }[c[0]] })); }
        ts.forEach(function (t) { col.appendChild(card(t)); });
        cols.appendChild(col);
      });
      el.appendChild(cols);
      var closed = tasks.filter(function (t) { return t.state === "done" || t.state === "dropped"; });
      if (closed.length) {
        var dn = closed.filter(function (t) { return t.state === "done"; }).length, dr = closed.length - dn, grid = h("div", { "class": "grid" });
        closed.forEach(function (t) { grid.appendChild(card(t)); });
        el.appendChild(h("details", { "class": "closed" }, [h("summary", { text: dn + " done, " + dr + " dropped" }), grid]));
      }
      if (o.from) { el.appendChild(h("p", { "class": "ls-from" }, ["The same as ", h("code", { text: o.from }), "."])); }
    }
    paint();
    return { el: el, set: function (ts) { tasks = ts; paint(); } };
  }

  /* LoadStack: what a session would start with, as one bar of its specialists against the token budget, with why each is there and what was left out. */
  function LoadStack(o) {
    o = o || {};
    var sel = (o.selected || []).slice().sort(function (a, b) { return (b.always ? 1 : 0) - (a.always ? 1 : 0) || b.estimatedTokens - a.estimatedTokens; });
    var used = o.estimatedTokens !== undefined ? o.estimatedTokens : sel.reduce(function (s, x) { return s + x.estimatedTokens; }, 0), budget = o.tokenBudget || 0, max = Math.max(used, budget) || 1;
    var over = budget && used > budget, near = !over && o.nearBudget;
    var el = h("div", { "class": "ls-load" });
    el.appendChild(h("p", { "class": "big" + (over ? " over" : "") }, [h("b", { text: used.toLocaleString("en-GB") }), h("span", { text: "tokens every session starts with" }), budget ? h("span", { "class": "w", text: over ? "\u00b7 " + (used - budget).toLocaleString("en-GB") + " over the budget of " + budget.toLocaleString("en-GB") : near ? "\u00b7 near the budget of " + budget.toLocaleString("en-GB") : "\u00b7 within the budget of " + budget.toLocaleString("en-GB") }) : null]));
    var bar = h("div", { "class": "bar", role: "group", "aria-label": "Loaded specialists, by tokens" }), rows = h("ol", { "aria-label": "Loaded" }), segs = [], lis = [];
    function lit(i, on) { if (segs[i]) { segs[i].classList.toggle("lit", on); } if (lis[i]) { lis[i].classList.toggle("lit", on); } }
    sel.forEach(function (x, i) {
      var w = (x.estimatedTokens / max) * 100;
      var s = h("button", { type: "button", "class": "seg" + (x.always ? " always" : ""), style: "flex: 0 0 calc(" + w.toFixed(3) + "% - 2px)", "aria-label": x.title + ", " + x.estimatedTokens + " tokens", onmouseenter: function () { lit(i, true); }, onmouseleave: function () { lit(i, false); }, onfocus: function () { lit(i, true); }, onblur: function () { lit(i, false); }, onclick: function () { if (o.onPick) { o.onPick(x.id); } } });
      segs.push(s); bar.appendChild(s);
      var t = o.href ? h("a", { "class": "t", href: o.href(x.id), text: x.title }) : h("span", { "class": "t", text: x.title });
      var li = h("li", { "class": x.always ? "always" : "", onmouseenter: function () { lit(i, true); }, onmouseleave: function () { lit(i, false); } }, [h("span", { "class": "sw", "aria-hidden": "true" }), t, h("span", { "class": "n", text: x.estimatedTokens.toLocaleString("en-GB") }), h("span", { "class": "r", text: [x.id, x.always ? "always loaded" : null, x.reason].filter(Boolean).join(" \u00b7 ") })]);
      lis.push(li); rows.appendChild(li);
    });
    if (budget) { bar.appendChild(h("span", { "class": "mark", style: "left: calc(" + (budget / max * 100).toFixed(3) + "% - 1px)", "aria-hidden": "true" })); }
    if (over) { bar.appendChild(h("span", { "class": "over", style: "left: " + (budget / max * 100).toFixed(3) + "%; right: 0", "aria-hidden": "true" })); }
    el.appendChild(bar);
    if (budget) { el.appendChild(h("div", { "class": "scale", "aria-hidden": "true" }, [h("span", { "class": "start", style: "left: 0", text: "0" }), h("span", { "class": over ? "" : "end", style: "left: " + (budget / max * 100).toFixed(3) + "%", text: "budget " + num(budget) }), over ? h("span", { "class": "end", style: "left: 100%", text: num(used) }) : null])); }
    el.appendChild(h("div", { "class": "legend", "aria-hidden": "true" }, [h("span", {}, [h("i", {}), "loaded for this task"]), h("span", {}, [h("i", { "class": "always" }), "always loaded"]), budget ? h("span", {}, [h("i", { "class": "mk" }), "the budget"]) : null]));
    el.appendChild(rows);
    var om = o.omitted || [];
    if (om.length) { el.appendChild(h("details", {}, [h("summary", { text: om.length + " considered and left out" }), h("ul", {}, om.map(function (x) { return h("li", {}, [h("b", { text: x.id }), x.reason]); }))])); }
    if (o.conflicts) { el.appendChild(h("p", { "class": "ls-from", text: o.conflicts + " overlap" + (o.conflicts === 1 ? "" : "s") + " settled; the narrower one wins." })); }
    return el;
  }

  Object.assign(NS, { ActionDrawer: ActionDrawer, ActionDrawers: ActionDrawers, TerminalOnly: TerminalOnly, HealthList: HealthList, IdeaStages: IdeaStages, QuestionSet: QuestionSet, PlanLayer: PlanLayer, NoteDump: NoteDump, ProjectTile: ProjectTile, TaskBoard: TaskBoard, LoadStack: LoadStack, commandLine: lineOf, TASK_NEXT: NEXT, TASK_MOVE: MOVE, tokens: num });
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
  /* A disabled button says why in place of its command, and is never the solid one. */
  function halo(o) { return NS.HaloButton(o.disabledReason ? Object.assign({}, o, { cmd: null, solid: false }) : o); }
  function tag(text, kind) { return h("span", { "class": "ls-tag" + (kind ? " " + kind : ""), text: text }); }
  function line(path, words) { return NS.commandLine(path, [], {}, words); }
  function said(el, text) { el.textContent = text ? "✓ " + text : ""; }
  function num(n) { return NS.tokens(n); }
  function pct(f) { return Math.round((Number(f) || 0) * 100) + "%"; }

  /* ConfigList: every setting loadout config knows, in its groups, found by name or what it does, and changed where it is shown. */
  function ConfigList(o) {
    o = o || {};
    var groups = o.groups || [], drafts = {}, query = "", out = h("p", { "class": "ls-said", role: "status" });
    var el = h("div", { "class": "ls-config" }), sid = uid("ls-cf-");
    var input = h("input", { "class": "ls-text", id: sid, type: "search", placeholder: "Find a setting by name or what it does", autocomplete: "off", spellcheck: "false", oninput: function () { query = input.value.trim().toLowerCase(); paint(); } });
    var count = h("span", { "class": "n", "aria-live": "polite" });
    el.appendChild(h("div", { "class": "find" }, [h("label", { "class": "ls-vh", "for": sid, text: "Find a setting" }), input, count]));
    var list = h("div", { style: "display: grid; gap: 6px" }); el.appendChild(list); el.appendChild(out);
    if (o.from !== false) { el.appendChild(h("p", { "class": "ls-from" }, ["The same as ", h("code", { text: "$ loadout config list" }), ". Empty clears a setting back to its default."])); }
    var openGroups = {}; if (groups[0]) { openGroups[groups[0].group] = true; }
    function match(s) { return !query || (s.name + " " + s.description).toLowerCase().indexOf(query) >= 0; }
    function row(s) {
      var has = Object.prototype.hasOwnProperty.call(drafts, s.name), d = has ? drafts[s.name] : s.value, changed = has && d !== s.value;
      var r = h("div", { "class": "crow" + (changed ? " changed" : ""), role: "group", "aria-label": s.name });
      var desc = [s.description];
      if (!s.value && s.whenUnset) { desc.push(h("span", { "class": "u", text: " Unset, it uses " + s.whenUnset + "." })); }
      r.appendChild(h("div", {}, [h("div", { "class": "k" }, [h("code", { text: s.name }), s.machineLocal ? tag("this machine only") : null, s.value && !s.flag ? tag("set", "accent") : null]), h("p", { "class": "d" }, desc)]));
      var ctl = h("div", { "class": "ctl" }), ex = h("p", { "class": "ex", "aria-live": "polite" });
      if (s.flag) {
        ctl.appendChild(NS.Switch({ label: s.name, sub: null, checked: d === "true", onChange: function (on) { drafts[s.name] = on ? "true" : "false"; paint(s.name); } }));
      } else {
        var iid = uid("ls-cv-"), inp = h("input", { "class": "ls-text", id: iid, value: d || "", placeholder: s.whenUnset ? "default: " + s.whenUnset : "unset", autocomplete: "off", spellcheck: "false", "aria-label": s.name, oninput: function () { drafts[s.name] = inp.value; mark(); } });
        inp.value = d || "";
        ctl.appendChild(inp);
      }
      var acts = h("div", { "class": "acts" }); ctl.appendChild(acts);
      function mark() {
        var v = drafts[s.name], ch = v !== undefined && v !== s.value;
        r.classList.toggle("changed", ch); acts.textContent = "";
        if (ch) {
          acts.appendChild(halo({ label: "Set it", kind: "", solid: true, size: "small", cmd: line("config set", [s.name, v]), disabledReason: o.disabledReason, onPress: function () { var res = o.onSet ? o.onSet(s.name, v) : null; s.value = v; delete drafts[s.name]; paint(); said(out, typeof res === "string" ? res : "Set " + s.name + (v === "" ? " back to its default" : " to " + v)); } }));
          acts.appendChild(halo({ label: "Put it back", size: "small", cmd: "changes nothing", onPress: function () { delete drafts[s.name]; paint(s.name); } }));
        } else if (s.value && !s.flag) {
          acts.appendChild(halo({ label: "Clear it", size: "small", cmd: line("config set", [s.name, ""]), disabledReason: o.disabledReason, onPress: function () { if (o.onSet) { o.onSet(s.name, ""); } s.value = ""; paint(); said(out, "Cleared " + s.name + ": it uses its default again"); } }));
        }
        acts.appendChild(h("button", { type: "button", "class": "xb", text: "Explain it", "aria-label": "Explain " + s.name + ": " + line("config get", [s.name, "--explain"]), title: line("config get", [s.name, "--explain"]), onclick: function () { ex.textContent = o.onExplain ? o.onExplain(s) : line("config get", [s.name, "--explain"]); } }));
      }
      mark();
      r.appendChild(ctl); r.appendChild(ex);
      return r;
    }
    function paint(focusName) {
      list.textContent = ""; var shown = 0, total = 0;
      groups.forEach(function (g) {
        var ss = g.settings.filter(match); total += g.settings.length; shown += ss.length;
        if (!ss.length) { return; }
        var setN = g.settings.filter(function (s) { return s.value; }).length;
        var d = h("details", { "class": "grp", open: query || openGroups[g.group] ? true : null, ontoggle: function () { if (!query) { openGroups[g.group] = d.open; } } }, [h("summary", {}, [h("h3", { text: g.group }), h("span", { "class": "n", text: query ? ss.length + " of " + g.settings.length : String(g.settings.length) }), setN ? h("span", { "class": "set", text: setN + " set" }) : null])]);
        var rows = h("div", { "class": "rows" }); ss.forEach(function (s) { rows.appendChild(row(s)); }); d.appendChild(rows); list.appendChild(d);
      });
      if (!shown) { list.appendChild(h("p", { "class": "none", text: "No setting matches “" + query + "”." })); }
      count.textContent = query ? shown + " of " + total + " settings" : total + " settings";
      if (focusName) { var f = list.querySelector('[aria-label="' + focusName + '"] .ls-switch, [aria-label="' + focusName + '"] input'); if (f) { f.focus(); } }
    }
    paint();
    return { el: el, find: function (q) { input.value = q; query = q.toLowerCase(); paint(); } };
  }

  /* StoreList: what a project remembers, in its three stores, and what the agent recorded outside the workspace. */
  function StoreList(o) {
    o = o || {};
    var el = h("nav", { "class": "ls-stores", "aria-label": "Topics" }), pk = o.picked || {};
    (o.stores || []).forEach(function (s) {
      var sid = uid("ls-st-"), sec = h("section", { "aria-labelledby": sid }, [h("h3", { id: sid }, [s.label, s.readOnly ? tag("read-only here") : null]), s.hint ? h("p", { "class": "hint", text: s.hint }) : null]);
      if (!(s.topics || []).length) { sec.appendChild(h("p", { "class": "empty", text: "Nothing recorded." })); }
      else { sec.appendChild(h("ul", {}, s.topics.map(function (t) { var cur = pk.store === s.key && pk.name === t.name; return h("li", {}, [h("a", { href: o.href ? o.href(s.key, t.name) : "#", "aria-current": cur ? "page" : null }, [h("span", { "class": "t", text: t.name }), h("span", { "class": "x", text: t.description || "" }), t.facts !== undefined ? h("span", { "class": "n", text: t.facts + " fact" + (t.facts === 1 ? "" : "s") }) : null])]); }))); }
      el.appendChild(sec);
    });
    var a = o.agent;
    if (a) {
      var aid = uid("ls-ag-"), box = h("section", { "class": "agent", "aria-labelledby": aid }, [h("h3", { id: aid, text: "Recorded by the agent, outside the workspace" }), h("p", { text: "Shown, never written from here. Bring it in with:" }), h("code", { text: "$ " + a.command })]);
      if (a.error) { box.appendChild(h("p", { "class": "needs", text: a.error })); }
      else {
        var n = (a.wouldImport || []).length, dr = (a.drifted || []).length;
        box.appendChild(h("p", { text: n + " topic" + (n === 1 ? "" : "s") + " would come in." + (dr ? "" : "") }));
        if (dr) { box.appendChild(h("p", { "class": "needs", text: dr + " differ" + (dr === 1 ? "s" : "") + " from the workspace's copy: " + a.drifted.join(", ") + "." })); }
      }
      el.appendChild(box);
    }
    return el;
  }

  /* TopicEditor: one memory topic, read and edited in place. A save carries the version it was read at; if somebody changed it meanwhile, theirs is shown beside your edit and nothing is overwritten. */
  function TopicEditor(o) {
    o = o || {};
    var topic = Object.assign({}, o.topic || {}), draft = topic.text || "", theirs = null, out = h("p", { "class": "ls-said", role: "status" });
    var el = h("section", { "class": "ls-topic", "aria-label": "Topic " + (topic.name || "") });
    function scopeWords() { return topic.scope && topic.scope !== "project" ? ["--scope", topic.scope] : []; }
    function replaceLine(version) { return line("memory replace", [topic.name, "--project", o.project || "<slug>"].concat(scopeWords(), ["--file", "<path>", "--expect", version || topic.version])); }
    function head() {
      var tags = h("div", { "class": "tags" }, [tag(o.storeLabel || topic.scope || "project"), topic.kind ? tag(topic.kind) : null, topic.written ? tag("written " + topic.written) : null, topic.version ? tag("version " + String(topic.version).slice(0, 8)) : null]);
      return h("div", { "class": "th" }, [h("h2", { text: topic.name }), topic.description ? h("p", { text: topic.description }) : null, tags]);
    }
    function lines(t) { return String(t || "").replace(/\n$/, "").split("\n"); }
    function paint() {
      el.textContent = ""; el.appendChild(head());
      if (theirs) { return paintClash(); }
      var tid = uid("ls-tt-"), ta = h("textarea", { "class": "ls-text", id: tid, rows: 14, spellcheck: "true", readonly: o.editable ? null : true, oninput: function () { draft = ta.value; meta(); acts(); } });
      ta.value = draft;
      el.appendChild(h("label", { "class": "ls-vh", "for": tid, text: "The topic's text" })); el.appendChild(ta);
      var m = h("div", { "class": "meta" }); el.appendChild(m);
      function meta() { var dirty = draft !== topic.text; m.textContent = ""; m.appendChild(h("span", { text: lines(draft).length + " lines · " + draft.length + " characters" })); m.appendChild(h("span", { "class": dirty ? "dirty" : "", text: dirty ? "edited, not saved" : "as stored" })); }
      meta();
      var row = h("div", { "class": "ls-answers" }); el.appendChild(row);
      function acts() {
        row.textContent = ""; if (!o.editable) { return; }
        var dirty = draft !== topic.text;
        row.appendChild(halo({ label: "Save", solid: true, size: "small", cmd: replaceLine(), disabledReason: dirty ? null : "nothing changed yet", onPress: save }));
        if (dirty) { row.appendChild(halo({ label: "Discard changes", size: "small", cmd: "changes nothing", onPress: function () { draft = topic.text; paint(); } })); }
      }
      acts();
      if (!o.editable) { el.appendChild(h("div", { "class": "ro" }, [h("p", { text: o.readOnlyReason || "This topic is read-only here." }), NS.Command({ why: "Edit it in a terminal:", command: replaceLine() })])); }
      el.appendChild(out);
    }
    function save() {
      var r = o.onSave ? o.onSave(draft, topic.version) : { saved: Object.assign({}, topic, { text: draft }) };
      if (r && r.changed) { theirs = r.changed; paint(); var f = el.querySelector(".two textarea"); if (f) { f.focus(); } return; }
      topic = Object.assign({}, topic, r && r.saved ? r.saved : { text: draft }); draft = topic.text; paint();
      said(out, "Saved " + topic.name + ". It is in the workspace; loadout workspace save commits it.");
      if (o.onSaved) { o.onSaved(topic); }
    }
    function paintClash() {
      el.appendChild(h("div", { "class": "clash", role: "alert" }, [h("b", { text: "Somebody else changed this topic while you were editing" }), h("p", { text: "Nothing was saved. On the left is what is on disk now; on the right is your edit. Merge what you want into yours, then keep it, or take theirs and lose your edit." })]));
      var mine = lines(draft), disk = lines(theirs.text), only = disk.filter(function (l) { return mine.indexOf(l) < 0; }).length, yours = mine.filter(function (l) { return disk.indexOf(l) < 0; }).length;
      el.appendChild(h("p", { "class": "diffsum", text: only + " line" + (only === 1 ? "" : "s") + " only on disk, marked −; " + yours + " only in your edit." }));
      var pre = h("pre", { "class": "disk", tabindex: "0", "aria-label": "On disk now, read-only" }, disk.map(function (l) { var o2 = mine.indexOf(l) < 0; return h("span", { "class": o2 ? "only" : "" }, [h("i", { "aria-hidden": "true", text: o2 ? "−" : "" }), o2 ? h("span", { "class": "ls-vh", text: "only on disk: " }) : null, l || " "]); }));
      var yid = uid("ls-ty-"), ta = h("textarea", { "class": "ls-text", id: yid, rows: 14, oninput: function () { draft = ta.value; } });
      ta.value = draft;
      el.appendChild(h("div", { "class": "two" }, [h("div", {}, [h("p", { "class": "lab" }, ["On disk now ", h("span", { text: "version " + String(theirs.version).slice(0, 8) + (theirs.written ? " · " + theirs.written : "") })]), pre]), h("div", {}, [h("label", { "class": "lab", "for": yid }, ["Your edit ", h("span", { text: "from version " + String(topic.version).slice(0, 8) })]), ta])]));
      var row = h("div", { "class": "ls-answers" });
      row.appendChild(halo({ label: "Keep my edit, then save", kind: "needs", solid: true, size: "small", cmd: replaceLine(theirs.version), onPress: function () { topic = Object.assign({}, theirs); theirs = null; save(); } }));
      row.appendChild(halo({ label: "Take theirs", size: "small", cmd: "drops your edit · changes nothing", onPress: function () { topic = Object.assign({}, theirs); draft = topic.text; theirs = null; paint(); said(out, "Took theirs. Your edit is gone."); } }));
      el.appendChild(row); el.appendChild(out);
    }
    paint();
    return { el: el, get draft() { return draft; }, get dirty() { return draft !== topic.text; } };
  }

  /* TokenSplit: what the agents spent over a period, billed as input, and what the cache saved, with how it splits. */
  function TokenSplit(o) {
    o = o || {};
    var t = o.totals || {}, el = h("div", { "class": "ls-split" });
    el.appendChild(h("p", { "class": "hero" }, [h("b", { text: num(t.billedInputEquivalent) }), h("span", { text: "tokens billed as input" }), h("span", { "class": "saved", text: "✓ the cache saved " + pct(t.savedFraction) })]));
    var parts = [["input", "Input", t.input], ["cache", "Read from the cache", t.cacheRead], ["output", "Output", t.output], ["thinking", "Thinking", t.thinking]], sum = parts.reduce(function (s, p) { return s + (p[2] || 0); }, 0) || 1;
    el.appendChild(h("div", { "class": "bar", role: "img", "aria-label": parts.map(function (p) { return p[1] + " " + num(p[2]); }).join(", ") }, parts.map(function (p) { return h("span", { "class": "k-" + p[0], style: "flex: 0 0 calc(" + ((p[2] || 0) / sum * 100).toFixed(3) + "% - 2px)" }); })));
    el.appendChild(h("dl", {}, parts.map(function (p) {
      var extra = p[0] === "cache" ? " · " + pct(t.cacheHitFraction) + " of input" : "";
      return h("div", {}, [h("i", { "class": "k-" + p[0], "aria-hidden": "true" }), h("dt", { text: p[1] }), h("dd", {}, [num(p[2]), extra ? h("span", { text: extra }) : null])]);
    })));
    if (o.since) { el.appendChild(h("p", { "class": "since" + (o.complete === false ? " needs" : "") }, ["Since " + o.since + (o.complete === false ? " · some transcripts could not be read, so these are a floor" : "") + ". From ", h("code", { "class": "ls-cmd", text: o.from || "$ loadout usage" }), "."])); }
    return el;
  }

  /* SpendBars: tokens by project, model or agent as bars, the twenty largest; by day as columns, read with the arrow keys. */
  function SpendBars(o) {
    o = o || {};
    var g = (o.groups || []).slice(), el = h("div", { "class": "ls-bars" }), fmt = o.format || num, unit = o.unit || "tokens";
    if (o.by === "day") {
      var max = Math.max.apply(null, g.map(function (x) { return x.value; }).concat([1])), cur = g.length - 1;
      var cols = h("div", { "class": "cols", tabindex: "0", role: "group", "aria-roledescription": "column chart", "aria-label": (o.label || "Tokens by day") + ". Left and right arrows read each day." });
      var readout = h("p", { "class": "readout", "aria-live": "polite" });
      var cs = g.map(function (x, i) { var c = h("div", { "class": "c", onmouseenter: function () { pick(i); } }, [h("span", { style: "height: " + Math.max(1, x.value / max * 100).toFixed(2) + "%" })]); cols.appendChild(c); return c; });
      function pick(i) { cur = Math.max(0, Math.min(g.length - 1, i)); cs.forEach(function (c, j) { c.classList.toggle("cur", j === cur); }); var x = g[cur]; readout.textContent = ""; if (x) { readout.appendChild(h("span", {}, [h("b", { text: x.name }), " · " + fmt(x.value) + " " + unit])); } }
      cols.addEventListener("keydown", function (e) { if (e.key === "ArrowLeft") { pick(cur - 1); e.preventDefault(); } else if (e.key === "ArrowRight") { pick(cur + 1); e.preventDefault(); } else if (e.key === "Home") { pick(0); e.preventDefault(); } else if (e.key === "End") { pick(g.length - 1); e.preventDefault(); } });
      el.appendChild(readout); el.appendChild(cols);
      var step = Math.max(1, Math.ceil(g.length / 6));
      el.appendChild(h("div", { "class": "axis", "aria-hidden": "true" }, g.map(function (x, i) { return h("span", { text: i % step === 0 ? x.name : "" }); })));
      el.appendChild(h("p", { "class": "keys", text: "Hover a day, or focus the chart and use the arrow keys." }));
      pick(cur);
    } else {
      g.sort(function (a, b) { return b.value - a.value; });
      var limit = o.limit || 20, shown = g.slice(0, limit), top = shown[0] ? shown[0].value : 1;
      el.appendChild(h("ol", { "class": "rows", "aria-label": o.label || "Tokens by " + (o.by || "project") }, shown.map(function (x) {
        return h("li", { "class": x.registered === false ? "away" : "" }, [h("span", { "class": "nm", title: x.name }, [h("span", { text: x.name }), x.registered === false ? tag("not a project") : null]), h("span", { "class": "track", "aria-hidden": "true" }, [h("span", { "class": "fill", style: "width: " + Math.max(0.5, x.value / top * 100).toFixed(2) + "%" })]), h("span", { "class": "v", text: fmt(x.value) })]);
      })));
      if (g.length > limit) { el.appendChild(h("p", { "class": "left", text: "The " + (g.length - limit) + " smallest are left out here." })); }
    }
    return el;
  }

  Object.assign(NS, { ConfigList: ConfigList, StoreList: StoreList, TopicEditor: TopicEditor, TokenSplit: TokenSplit, SpendBars: SpendBars });
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
  function typingIn(t) { return t && (t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.tagName === "SELECT" || t.isContentEditable); }

  /* PageBeside: one of the pages opened next to the terminals or the runs board, which stay live beside it. Escape closes it, unless you are typing or inside a terminal. */
  function PageBeside(o) {
    o = o || {};
    var close = h("button", { type: "button", "class": "x", "aria-keyshortcuts": "Escape", onclick: function () { done(); } }, [o.closeLabel || "Close", h("kbd", { "aria-hidden": "true", text: "Esc" })]);
    var body = h("div", { "class": "body" }, [o.content || null]);
    var el = h("aside", { "class": "ls-beside", "aria-label": (o.page || "Page") + ", beside " + (o.beside || "your work") }, [h("div", { "class": "bh" }, [h("span", { "class": "t" }, [h("b", { text: o.page || "" }), " · beside " + (o.beside || "your work")]), h("span", { "class": "grow" }), close]), body]);
    function onKey(e) {
      if (e.key !== "Escape" || e.defaultPrevented || typingIn(e.target)) { return; }
      if (e.target && e.target.closest && e.target.closest(".ls-term, .ls-dialog")) { return; }
      if (document.querySelector(".ls-dialog")) { return; }
      e.preventDefault(); done();
    }
    function done() { document.removeEventListener("keydown", onKey); if (o.onClose) { o.onClose(); } }
    document.addEventListener("keydown", onKey);
    return { el: el, body: body, set: function (n) { body.textContent = ""; if (n) { body.appendChild(n); } }, close: done, destroy: function () { document.removeEventListener("keydown", onKey); } };
  }
  /* The two side by side: what you are watching on the left, the page on the right. Below 1100px the page takes the width. */
  function BesideGrid(focus, beside) { return h("div", { "class": "ls-besidegrid" }, [h("div", { "class": "focusarea" }, [focus]), beside.el || beside]); }

  /* QuietPage: a page in Minimal. One sentence, what needs you or matters now as a QuietList, and everything else behind one button. */
  function QuietPage(o) {
    o = o || {};
    var all = h("div", { "class": "all", hidden: true, id: "ls-qp-" + Math.random().toString(36).slice(2, 7) }), filled = false;
    var more = h("button", { type: "button", "class": "more", "aria-expanded": "false", "aria-controls": all.id, text: o.moreLabel || "Show everything", onclick: function () {
      var open = more.getAttribute("aria-expanded") !== "true";
      if (open && !filled && o.everything) { all.appendChild(o.everything()); filled = true; }
      all.hidden = !open; more.setAttribute("aria-expanded", open ? "true" : "false"); more.textContent = open ? (o.lessLabel || "Show less") : (o.moreLabel || "Show everything");
      if (o.onToggle) { o.onToggle(open); }
    } });
    var items = o.items || [];
    var kids = [h("div", { "class": "qh" }, [h("h1", { text: o.title || "" }), o.sentence ? (typeof o.sentence === "string" ? h("p", { text: o.sentence }) : o.sentence) : null])];
    if (items.length) { kids.push(NS.QuietList({ items: items, label: o.label || ("What matters on " + (o.title || "this page")), onOpen: o.onOpen })); }
    else { kids.push(h("p", { "class": "calmnote", text: "✓ " + (o.calmText || "Nothing here needs you.") })); }
    kids.push(more, all);
    var el = h("div", { "class": "ls-qpage" }, kids);
    if (o.open) { setTimeout(function () { more.click(); }, 0); }
    return { el: el, showAll: function () { if (more.getAttribute("aria-expanded") !== "true") { more.click(); } } };
  }

  /* ForgetProject: forget a project on this machine only, or from the Loadout workspace for every machine, with what should go with it. Cleanup runs first, while the project is still known; the repository is never touched. */
  function q(v) { v = String(v); return /^<[^<>\s]+>$/.test(v) ? v : (v === "" || /[\s"'$`\\|&;<>()*?!#~]/.test(v) ? "\"" + v.replace(/(["\\$`])/g, "\\$1") + "\"" : v); }
  function ForgetProject(o) {
    o = o || {};
    var p = o.project || {}, slug = p.id || "<project>", c = o.counts || {}, scope = "here", picked = { protect: false, tasks: false, ideas: false, topics: false }, armed = false, armT = null;
    var el = h("div", { "class": "ls-forget" });
    el.appendChild(h("p", { "class": "lead", text: "Loadout forgets " + (p.name || slug) + ". Its repository and code are never touched." }));
    var name = "ls-fg-" + Math.random().toString(36).slice(2, 7);
    function radio(v, title, sub, cls) { var r = h("input", { type: "radio", name: name, value: v, checked: scope === v ? true : null, onchange: function () { scope = v; disarm(); paint(); } }); return h("label", { "class": cls || "" }, [r, h("b", { text: title }), h("span", { text: sub })]); }
    el.appendChild(h("fieldset", { "class": "where" }, [h("legend", { text: "Forget it" }),
      radio("here", "On this machine only", "Loadout forgets where it is here. It stays in the workspace, listed as not on this machine, and your other machines keep it."),
      radio("everywhere", "From the Loadout workspace", "Every machine forgets it, and it leaves the list. Its tasks, ideas and memory can go with it.", "wide")]));
    var clean = h("fieldset", { "class": "clean" }), kept = h("p", { "class": "kept" }), plan = h("ol", { "class": "plan", "aria-label": "What will run, in order" }), armedEl = h("p", { "class": "armed", role: "status" });
    var acts = h("div", { "class": "ls-answers" }), said = h("p", { "class": "ls-said", role: "status" });
    el.appendChild(clean); el.appendChild(kept); el.appendChild(h("div", {}, [h("p", { "class": "lab", style: "margin: 0 0 6px; font-size: 11.5px; font-weight: 500; letter-spacing: .16em; text-transform: uppercase; color: var(--ink-soft)", text: "What will run, in order" }), plan])); el.appendChild(armedEl); el.appendChild(acts); el.appendChild(said);
    function line(path, words) { return "$ loadout " + [path].concat(words.map(q)).join(" "); }
    function cleanups() {
      var out = [];
      if (picked.protect && p.available !== false) { out.push(line("protect", [slug, "--remove"])); }
      if (scope === "everywhere") {
        if (picked.tasks) { (c.tasks || []).forEach(function (t) { out.push(line("task remove", [t, "--project", slug])); }); }
        if (picked.ideas) { (c.ideas || []).forEach(function (t) { out.push(line("idea remove", [t, "--project", slug])); }); }
        if (picked.topics) { (c.topics || []).forEach(function (t) { out.push(line("memory delete", [t, slug, "--yes"])); }); }
      }
      return out;
    }
    function steps() { return cleanups().concat([line("project remove", [slug].concat(scope === "everywhere" ? ["--from-workspace"] : []))]); }
    function box(key, title, sub, cmd, off) {
      var cb = h("input", { type: "checkbox", disabled: off ? true : null, onchange: function () { picked[key] = cb.checked; disarm(); paint(); } }); cb.checked = !off && picked[key];
      return h("label", { "class": off ? "off" : "" }, [cb, h("b", { text: title }), h("span", { text: off || sub }), cmd ? h("code", { text: cmd }) : null]);
    }
    function n(list, one, many) { var k = (list || []).length; return k + " " + (k === 1 ? one : many); }
    function paint() {
      clean.textContent = ""; clean.appendChild(h("legend", { text: "Also clean up" }));
      var away = p.available === false, local = scope === "here" ? "Only when forgetting it from the workspace: on this machine only, the workspace keeps them for your other machines." : null;
      clean.appendChild(box("protect", "Take the Git protections out of its repository", "The hooks and ignore rules Loadout installed to keep AI files out of commits.", line("protect", [slug, "--remove"]), away ? "It is not on this machine, so there is no repository here to change." : null));
      clean.appendChild(box("tasks", "Its " + n(c.tasks, "task", "tasks"), "Every task recorded for it, open or not.", (c.tasks || []).length ? line("task remove", ["<id>", "--project", slug]) + "  \u00d7 " + c.tasks.length : null, local || (!(c.tasks || []).length ? "It has none." : null)));
      clean.appendChild(box("ideas", "Its " + n(c.ideas, "idea", "ideas"), "Ideas being shaped for it. Accepted plans stay where they were written.", (c.ideas || []).length ? line("idea remove", ["<id>", "--project", slug]) + "  \u00d7 " + c.ideas.length : null, local || (!(c.ideas || []).length ? "It has none." : null)));
      clean.appendChild(box("topics", "Its " + n(c.topics, "memory topic", "memory topics"), "What agents remembered about it. A backup of each is taken first.", (c.topics || []).length ? line("memory delete", ["<topic>", slug, "--yes"]) + "  \u00d7 " + c.topics.length : null, local || (!(c.topics || []).length ? "It has none." : null)));
      kept.textContent = "";
      if (scope === "everywhere" && o.definition) { kept.appendChild(document.createTextNode("Kept in the workspace whatever you tick: its instructions, rules and memory folder, " + o.definition.files + " files under ")); kept.appendChild(h("code", { text: o.definition.path })); kept.appendChild(document.createTextNode(". Loadout never deletes it; delete it yourself if you meant to lose it.")); kept.hidden = false; }
      else { kept.hidden = true; }
      plan.textContent = ""; steps().forEach(function (s) { plan.appendChild(h("li", { text: s })); });
      acts.textContent = "";
      var dis = o.disabledReason || null;
      acts.appendChild(NS.HaloButton({ label: "Preview", size: "small", cmd: dis ? null : "adds --dry-run to each \u00b7 changes nothing", disabledReason: dis, onPress: function () { run(true); } }));
      acts.appendChild(NS.HaloButton({ label: armed ? "Press again to forget it everywhere" : (scope === "everywhere" ? "Forget it on every machine" : "Forget it here"), kind: "danger", size: "small", solid: armed && !dis, cmd: dis ? null : steps().length + " command" + (steps().length === 1 ? "" : "s") + ", in order; stops at the first that fails", disabledReason: dis, onPress: function () {
        if (scope === "everywhere" && !armed) { armed = true; armedEl.textContent = "Every machine that uses the workspace forgets " + slug + ". Press the button again within five seconds to go ahead."; clearTimeout(armT); armT = setTimeout(function () { disarm(); paint(); }, 5000); paint(); var b = acts.querySelectorAll("button")[1]; if (b) { b.focus(); } return; }
        disarm(); run(false);
      } }));
    }
    function disarm() { armed = false; clearTimeout(armT); armedEl.textContent = ""; }
    function run(preview) {
      var lines = steps().map(function (s) { return preview ? s + " --dry-run" : s; });
      var r = o.onRun ? o.onRun({ scope: scope, lines: lines, preview: preview, picked: Object.assign({}, picked) }) : null;
      said.textContent = "\u2713 " + (typeof r === "string" ? r : preview ? "Previewed. Nothing changed." : "Forgotten.");
      paint();
    }
    paint();
    return { el: el, get scope() { return scope; }, steps: steps };
  }

  Object.assign(NS, { PageBeside: PageBeside, BesideGrid: BesideGrid, QuietPage: QuietPage, ForgetProject: ForgetProject });
})();

(function () {
  "use strict";
  var NS = window.LoadoutStudio = window.LoadoutStudio || {};
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
  function s(tag, attrs) { var e = document.createElementNS(SVGNS, tag); Object.keys(attrs || {}).forEach(function (k) { e.setAttribute(k, attrs[k]); }); return e; }
  function icon(d) { var v = s("svg", { viewBox: "0 0 20 20", "aria-hidden": "true", "class": "g" }), p = s("path", { d: d, fill: "none", stroke: "currentColor", "stroke-width": "1.8", "stroke-linecap": "round", "stroke-linejoin": "round" }); v.appendChild(p); return v; }
  function uid(p) { return p + Math.random().toString(36).slice(2, 7); }
  function halo(o) { return NS.HaloButton(o.disabledReason ? Object.assign({}, o, { cmd: null, solid: false }) : o); }
  function q(v) { return NS.shellQuote ? NS.shellQuote(v) : JSON.stringify(v); }
  function said(el, t) { el.textContent = t ? "✓ " + t : ""; }
  function ago(n) { return n === 0 ? "this round" : n + (n === 1 ? " round ago" : " rounds ago"); }
  var MARK = { met: ["✓", "met"], unmet: ["✕", "unmet"], "not-attempted": ["–", "not attempted"], "not attempted": ["–", "not attempted"] };
  function cls(v) { return v === "met" ? "met" : v === "unmet" ? "unmet" : (v === "not-attempted" || v === "not attempted") ? "na" : "none"; }

  /* ProgressLine: "3 of 5 met, up from 1 · last moved 2 rounds ago", with a small line of the met count by round. The words carry it; the line is decoration. */
  function ProgressLine(o) {
    o = o || {};
    var by = o.byRound || [], total = o.total || 0, met = by.length ? by[by.length - 1] : (o.met || 0), first = by.length ? by[0] : met;
    var el = h("div", { "class": "ls-prog" + (o.stalled ? " stalled" : "") + (o.compact ? " compact" : "") });
    if (!total) { el.appendChild(h("span", { text: "No done-when to judge it by." })); return el; }
    var w = o.compact ? 64 : 84, H = o.compact ? 18 : 22;
    if (by.length > 1) {
      var v = s("svg", { viewBox: "0 0 " + w + " " + H, "aria-hidden": "true" }), step = w / (by.length - 1);
      function y(n) { return H - 2 - (n / total) * (H - 4); }
      var pts = by.map(function (n, i) { return (i * step).toFixed(1) + "," + y(n).toFixed(1); });
      v.appendChild(s("line", { "class": "top", x1: 0, y1: y(total), x2: w, y2: y(total) }));
      v.appendChild(s("path", { "class": "ar", d: "M0," + H + " L" + pts.join(" L") + " L" + w + "," + H + " Z" }));
      v.appendChild(s("polyline", { "class": "ln", points: pts.join(" ") }));
      v.appendChild(s("circle", { "class": "dot", cx: w, cy: y(met), r: 2.4 }));
      el.appendChild(v);
    }
    var lead = h("span", {}, [h("b", { text: met + " of " + total }), " met"]);
    if (by.length > 1 && met > first) { lead.appendChild(h("span", { "class": "up", text: ", up from " + first })); }
    el.appendChild(lead);
    if (o.lastMoved !== undefined && o.lastMoved !== null) { el.appendChild(h("span", { "class": "moved", text: (o.stalled ? "stalled · " : "") + "last moved " + ago(o.lastMoved) })); }
    return el;
  }

  /* CriteriaGrid: each done-when a row, each round a column; what changed in a round is tinted; each row says when it last moved. */
  function CriteriaGrid(o) {
    o = o || {};
    var rounds = o.rounds || 0, crit = o.criteria || [], now = o.current || rounds;
    var el = h("div", { "class": "ls-cgrid" });
    var met = crit.filter(function (c) { return c.verdicts[c.verdicts.length - 1] === "met"; }).length, was = crit.filter(function (c) { return c.verdicts[0] === "met"; }).length;
    el.appendChild(h("p", { "class": "sum" }, [h("b", { text: met + " of " + crit.length + " met" }), rounds > 1 && met > was ? h("span", { "class": "up", text: ", up from " + was + " in round 1" }) : null, o.stalledFor ? h("span", { style: "color: var(--needs-text)", text: " · none has moved for " + o.stalledFor + " rounds" }) : null]));
    var cid = uid("ls-cg-"), tbl = h("table", { "aria-describedby": cid }, [h("caption", { text: "Each done-when, round by round" })]);
    var hr = h("tr", {}, [h("th", { scope: "col", text: "Done when" })]);
    for (var r = 1; r <= rounds; r++) { hr.appendChild(h("th", { scope: "col", "class": r === now ? "now" : null, text: "R" + r, "aria-label": "Round " + r })); }
    tbl.appendChild(h("thead", {}, [hr]));
    var tb = h("tbody", {});
    crit.forEach(function (c) {
      var at = null; for (var i = c.verdicts.length - 1; i > 0; i--) { if (c.verdicts[i] !== c.verdicts[i - 1]) { at = i + 1; break; } }
      var nowV = c.verdicts[c.verdicts.length - 1], since = c.verdicts.length - (at || 1), stuck = nowV !== "met" && since >= 3;
      var tr = h("tr", {}, [h("th", { scope: "row" }, [c.text, h("span", { "class": "mv" + (stuck ? " stuck" : ""), text: nowV === "met" ? "met since round " + (at || 1) : (at === null ? "has not moved since round 1" : "last moved in round " + at) })])]);
      for (var k = 0; k < rounds; k++) {
        var v = c.verdicts[k], m = MARK[v] || ["·", "no verdict"], moved = k > 0 && v !== c.verdicts[k - 1] && v !== undefined;
        tr.appendChild(h("td", { "class": cls(v) + (moved ? " moved" : "") }, [h("i", { "aria-hidden": "true", text: m[0] }), h("span", { "class": "ls-vh", text: "Round " + (k + 1) + ": " + m[1] + (moved ? ", changed" : "") })]));
      }
      tb.appendChild(tr);
    });
    tbl.appendChild(tb);
    el.appendChild(h("div", { "class": "scroll", tabindex: "0", role: "region", "aria-label": "Done-when by round" }, [tbl]));
    el.appendChild(h("p", { "class": "legend", id: cid }, [h("span", {}, [h("i", { text: "✓" }), "met"]), h("span", {}, [h("i", { text: "✕" }), "unmet"]), h("span", {}, [h("i", { text: "–" }), "not attempted"]), h("span", {}, [h("i", { text: "·" }), "no verdict"]), h("span", {}, ["tinted: changed that round"])]));
    if (o.from) { el.appendChild(h("p", { "class": "ls-from", style: "margin: 0" }, ["The lead's account after each round, from its run journal (", h("code", { text: o.from }), ")."])); }
    return el;
  }

  /* PickUpSheet: picking a finished run up asks for exactly what it ran out of. */
  function PickUpSheet(o) {
    o = o || {};
    var r = o.run || {}, ended = o.ended || "other", V = { usd: "", rounds: "", message: "" }, out = h("p", { "class": "ls-said", role: "status" });
    var el = h("div", { "class": "ls-pickup" }), line = h("code", { "class": "ls-cmdline" }), act = h("div", { "class": "ls-answers" });
    var why = { budget: ["It stopped because it spent its budget: ", "$" + Number(o.spent || 0).toFixed(2) + " of $" + Number(o.cap || 0).toFixed(2), ". Say what it may spend in all from now on."], rounds: ["It stopped at its round limit: ", (o.rounds || "?") + " of " + (o.roundLimit || "?") + " rounds", ". Say how many more it may take."], other: ["It ended: ", o.reason || "stopped", ". It carries on from where it was, with what it had."] }[ended] || ["", "", ""];
    el.appendChild(h("p", { "class": "why" }, [why[0], h("b", { text: why[1] }), why[2]]));
    function inp(key, label, hint, ph) { var id = uid("ls-pu-"), i = h("input", { "class": "ls-text", id: id, placeholder: ph || "", autocomplete: "off", oninput: function () { V[key] = i.value.trim(); paint(); } }); return h("div", { "class": "ls-field" }, [h("label", { "class": "lab", "for": id, text: label }), i, hint ? h("span", { "class": "hint", text: hint }) : null]); }
    var two = h("div", { "class": "two" });
    two.appendChild(inp("usd", ended === "budget" ? "What it may spend in all, in US dollars" : "Budget (optional)", ended === "budget" ? "--usd: in all, from now on, as team resume takes it; or none for no cap. It has spent $" + Number(o.spent || 0).toFixed(2) + "." : "--usd: leave empty to keep what it had.", ended === "budget" ? "for example " + Math.ceil((o.cap || 10) * 1.5) : "as it was"));
    two.appendChild(inp("rounds", ended === "rounds" ? "How many more rounds" : "More rounds (optional)", "--rounds: how many more it may take.", ended === "rounds" ? "for example 3" : "as it was"));
    el.appendChild(two);
    var mid = uid("ls-pm-"), ta = h("textarea", { "class": "ls-text", id: mid, rows: 3, placeholder: "What to do next, or what changed (optional)", oninput: function () { V.message = ta.value.trim(); paint(); } });
    el.appendChild(h("div", { "class": "ls-field" }, [h("label", { "class": "lab", "for": mid, text: "Tell the lead" }), ta]));
    el.appendChild(line); el.appendChild(act); el.appendChild(out);
    function problem() {
      if (ended === "budget" && !V.usd) { return "say what it may spend first"; }
      if (V.usd && !/^(none|\d+(\.\d+)?)$/i.test(V.usd)) { return "a budget is a figure in US dollars, or none"; }
      if (ended === "budget" && /^\d/.test(V.usd) && parseFloat(V.usd) <= (o.spent || 0)) { return "more than the $" + Number(o.spent || 0).toFixed(2) + " it has spent"; }
      if (ended === "rounds" && !V.rounds) { return "say how many more rounds first"; }
      if (V.rounds && !/^[1-9]\d*$/.test(V.rounds)) { return "rounds are a whole number, 1 or more"; }
      return null;
    }
    function cmd() { return "$ loadout team resume " + (r.id || "<run>") + (V.usd ? " --usd " + V.usd : "") + (V.rounds ? " --rounds " + V.rounds : "") + (V.message ? " --message " + q(V.message) : ""); }
    function paint() {
      line.textContent = cmd(); act.textContent = "";
      act.appendChild(halo({ label: "Pick it up", solid: true, glyph: "▶", cmd: "runs the command above", disabledReason: problem() || o.disabledReason, onPress: function () { var res = o.onSubmit ? o.onSubmit(cmd(), Object.assign({}, V)) : null; said(out, typeof res === "string" ? res : "It carries on from round " + ((o.rounds || 0) + 1) + "."); } }));
    }
    paint();
    return { el: el, command: cmd };
  }

  /* RefusalsList: what the run was refused, allowed and decided, oldest first, refusals first to filter. */
  var RG = { refused: "M6 6 L14 14 M14 6 L6 14", allowed: "M5 10.5 L8.5 14 L15 6.5", merged: "M6 4v8a4 4 0 0 0 4 4h4 M14 12l2 4-4 0", answered: "M4 6h12v7H9l-3 3v-3H4z" };
  function RefusalsList(o) {
    o = o || {};
    var all = o.items || [], show = o.only || "all", el = h("div", { "class": "ls-refused" }), list = h("div", {});
    var counts = { refused: all.filter(function (x) { return x.outcome === "refused"; }).length };
    var bar = h("div", { "class": "filters", role: "group", "aria-label": "Show" });
    [["all", "Everything (" + all.length + ")"], ["refused", "Refused (" + counts.refused + ")"]].forEach(function (f) { bar.appendChild(h("button", { type: "button", "aria-pressed": show === f[0] ? "true" : "false", text: f[1], onclick: function () { show = f[0]; Array.prototype.forEach.call(bar.children, function (b, i) { b.setAttribute("aria-pressed", ["all", "refused"][i] === show ? "true" : "false"); }); paint(); } })); });
    el.appendChild(bar); el.appendChild(list);
    function paint() {
      list.textContent = "";
      var xs = all.filter(function (x) { return show === "all" || x.outcome === show; });
      if (!xs.length) { list.appendChild(h("p", { "class": "none", text: show === "refused" ? "Nothing was refused." : "Nothing was asked or decided." })); return; }
      list.appendChild(h("ol", {}, xs.map(function (x) { return h("li", { "class": x.outcome }, [h("span", { "class": "t", text: x.at || "" }), icon(RG[x.outcome] || RG.answered), h("span", { "class": "w" }, [h("span", { text: x.outcome }), x.what]), x.detail ? h("span", { "class": "d", text: x.detail }) : null]); })));
    }
    paint();
    if (o.from) { el.appendChild(h("p", { "class": "ls-from", style: "margin: 0" }, ["From the run's journal: ", h("code", { text: o.from }), "."])); }
    return el;
  }

  /* RedAlert: when it is on and something needs you, the page's edge lights and one line says what. It clears itself when answered. */
  function RedAlert(o) {
    o = o || {};
    var items = [], quietSig = null, label = uid("ls-ra-");
    var reason = h("span", { "class": "r", id: label }), count = h("b", {});
    var goBtn = h("button", { type: "button", text: "Go to it", onclick: function () { if (items[0] && o.onGo) { o.onGo(items[0]); } } });
    var quiet = h("button", { type: "button", "class": "q", text: "Quiet until something new", onclick: function () { quietSig = sig(); paint(); } });
    var bar = h("div", { "class": "bar", role: "status", "aria-live": "assertive", "aria-labelledby": label }, [count, reason, goBtn, quiet]);
    var el = h("div", { "class": "ls-alert", hidden: true }, [h("div", { "class": "edge", "aria-hidden": "true" }), bar]);
    (o.host || document.body).appendChild(el);
    function sig() { return items.map(function (x) { return x.id; }).join("|"); }
    function paint() {
      var on = !!o.enabled && items.length > 0 && quietSig !== sig();
      el.hidden = !on; if (!on) { return; }
      var problem = items.some(function (x) { return x.level === "problem"; });
      el.className = "ls-alert" + (problem ? " problem" : "");
      count.textContent = items.length === 1 ? (problem ? "Something went wrong" : "Needs you") : items.length + " need you";
      reason.textContent = items[0].text + (items.length > 1 ? " (and " + (items.length - 1) + " more)" : "");
    }
    return { el: el, set: function (xs) { var before = sig(); items = (xs || []).slice().sort(function (a, b) { return (b.level === "problem") - (a.level === "problem"); }); if (sig() !== before && quietSig && sig().indexOf(quietSig) < 0) { quietSig = null; } if (!items.length) { quietSig = null; } paint(); }, enable: function (v) { o.enabled = !!v; paint(); }, get showing() { return !el.hidden; }, destroy: function () { el.remove(); } };
  }

  /* TaskPicker: which recorded task this is for, doing first, then open and blocked, newest first; or something else. */
  var ORDER = { doing: 0, open: 1, blocked: 2 };
  function TaskPicker(o) {
    o = o || {};
    var ts = (o.tasks || []).slice().sort(function (a, b) { return (ORDER[a.state] - ORDER[b.state]) || String(b.declared || "").localeCompare(String(a.declared || "")); });
    var name = uid("ls-tp-"), el = h("fieldset", { "class": "ls-taskpick" }, [h("legend", { text: o.label || "A task it is for" })]), opts = h("div", { "class": "opts" });
    var sel = o.selected === undefined ? null : o.selected;
    function opt(t) {
      var r = h("input", { type: "radio", name: name, value: t ? t.id : "", onchange: function () { sel = t ? t.id : null; if (o.onPick) { o.onPick(t); } } });
      r.checked = t ? sel === t.id : !sel;
      return h("label", { "class": t ? "" : "other" }, [r, h("span", { "class": "t", text: t ? t.title : (o.otherLabel || "Something else") }), h("span", { "class": "x", text: t ? [t.id, t.declaredBy, t.declared].filter(Boolean).join(" · ") : (o.otherSub || "say it below") }), t ? NS.Signal(t.state) : null]);
    }
    ts.forEach(function (t) { opts.appendChild(opt(t)); });
    opts.appendChild(opt(null));
    el.appendChild(opts);
    return el;
  }

  /* ValueReport: what Loadout did for you over a period, counted from what this machine records. A figure not recorded yet says so instead of guessing. */
  function ValueReport(o) {
    o = o || {};
    var el = h("div", { "class": "ls-value" });
    if (o.head) { el.appendChild(typeof o.head === "string" ? h("p", { "class": "vhead", text: o.head }) : o.head); }
    (o.groups || []).forEach(function (g) {
      var sid = uid("ls-vg-"), sec = h("section", { "class": "grp", "aria-labelledby": sid }, [h("h3", { id: sid, text: g.name })]), figs = h("div", { "class": "figs" });
      g.figures.forEach(function (f) {
        var waits = f.value === null || f.value === undefined;
        figs.appendChild(h("div", { "class": "fig" + (waits ? " waits" : "") }, [h("span", { "class": "n", text: waits ? "not counted yet" : String(f.value) }), h("span", { "class": "l", text: f.label }), f.detail ? h("span", { "class": "d", text: f.detail }) : null, waits ? h("span", { "class": "w", text: f.waits || "counted once the event log ships" }) : null, f.from ? h("code", { text: f.from }) : null]));
      });
      sec.appendChild(figs); el.appendChild(sec);
    });
    el.appendChild(h("p", { "class": "how", text: o.how || "Counted on this machine from what Loadout records; nothing leaves it. These are counts of what happened, not estimates of time or money saved." }));
    return el;
  }

  Object.assign(NS, { ProgressLine: ProgressLine, CriteriaGrid: CriteriaGrid, PickUpSheet: PickUpSheet, RefusalsList: RefusalsList, RedAlert: RedAlert, TaskPicker: TaskPicker, ValueReport: ValueReport });
})();

(function () {
  "use strict";
  var NS = window.LoadoutStudio = window.LoadoutStudio || {};
  var SVGNS = "http://www.w3.org/2000/svg";
  function s(tag, attrs, kids) { var e = document.createElementNS(SVGNS, tag); Object.keys(attrs || {}).forEach(function (k) { if (attrs[k] !== null && attrs[k] !== undefined) { e.setAttribute(k, attrs[k]); } }); (kids || []).forEach(function (c) { if (c) { e.appendChild(c); } }); return e; }
  var BACKDROPS = ["plain", "map", "map-animated"];
  /** Plain ground, the field map still (the default), or the field map with its scan. */
  function setBackdrop(v, el) { (el || document.documentElement).setAttribute("data-backdrop", BACKDROPS.indexOf(v) >= 0 ? v : "map"); }

  /* A small deterministic value noise, so the map is the same on every visit. */
  function rng(seed) { var x = seed >>> 0 || 1; return function () { x ^= x << 13; x >>>= 0; x ^= x >>> 17; x ^= x << 5; x >>>= 0; return x / 4294967296; }; }
  function noise(seed) {
    var r = rng(seed), N = 256, perm = [], val = [];
    for (var i = 0; i < N; i++) { perm[i] = i; val[i] = r(); }
    for (var j = N - 1; j > 0; j--) { var k = Math.floor(r() * (j + 1)), t = perm[j]; perm[j] = perm[k]; perm[k] = t; }
    function at(x, y) { return val[perm[(perm[x & 255] + y) & 255]]; }
    function sm(t) { return t * t * (3 - 2 * t); }
    function v(x, y) { var x0 = Math.floor(x), y0 = Math.floor(y), fx = sm(x - x0), fy = sm(y - y0); var a = at(x0, y0), b = at(x0 + 1, y0), c = at(x0, y0 + 1), d = at(x0 + 1, y0 + 1); return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy; }
    return function (x, y) { return v(x, y) * 0.58 + v(x * 2.1 + 17, y * 2.1 + 9) * 0.28 + v(x * 4.3 + 41, y * 4.3 + 3) * 0.14; };
  }

  /* Contour lines by marching squares over the noise, one path for the ordinary lines and one for every fifth (the index contours). */
  function contours(W, H, o) {
    var cell = o.cell || 12, scale = o.scale || 260, n = noise(o.seed || 1789), cols = Math.ceil(W / cell) + 1, rows = Math.ceil(H / cell) + 1, z = [];
    for (var y = 0; y < rows; y++) { z[y] = []; for (var x = 0; x < cols; x++) { z[y][x] = n(x * cell / scale, y * cell / scale); } }
    var levels = o.levels || 22, lo = 0.12, hi = 0.88, minor = [], index = [];
    for (var L = 1; L < levels; L++) {
      var t = lo + (hi - lo) * L / levels, d = (L % 5 === 0) ? index : minor;
      for (var yy = 0; yy < rows - 1; yy++) {
        for (var xx = 0; xx < cols - 1; xx++) {
          var a = z[yy][xx], b = z[yy][xx + 1], c = z[yy + 1][xx + 1], e = z[yy + 1][xx];
          var code = (a > t ? 8 : 0) | (b > t ? 4 : 0) | (c > t ? 2 : 0) | (e > t ? 1 : 0);
          if (code === 0 || code === 15) { continue; }
          var X = xx * cell, Y = yy * cell;
          function lerp(p, q) { return (t - p) / (q - p); }
          var top = [X + cell * lerp(a, b), Y], right = [X + cell, Y + cell * lerp(b, c)], bot = [X + cell * lerp(e, c), Y + cell], left = [X, Y + cell * lerp(a, e)];
          var segs = { 1: [[left, bot]], 2: [[bot, right]], 3: [[left, right]], 4: [[top, right]], 5: [[left, top], [bot, right]], 6: [[top, bot]], 7: [[left, top]], 8: [[left, top]], 9: [[top, bot]], 10: [[left, bot], [top, right]], 11: [[top, right]], 12: [[left, right]], 13: [[bot, right]], 14: [[left, bot]] }[code];
          segs.forEach(function (sg) { d.push("M" + sg[0][0].toFixed(1) + " " + sg[0][1].toFixed(1) + "L" + sg[1][0].toFixed(1) + " " + sg[1][1].toFixed(1)); });
        }
      }
    }
    return { minor: minor.join(""), index: index.join("") };
  }

  /* FieldMap: the embossed survey map. Each contour is drawn three times: a light edge, a dark edge, and the line itself. */
  var fmCount = 0;
  function FieldMap(o) {
    o = o || {};
    var el = document.createElement("div"); el.className = "ls-fieldmap"; el.setAttribute("aria-hidden", "true");
    var host = o.host || document.body, timer = null;
    function draw() {
      var W = o.width || window.innerWidth, H = o.height || window.innerHeight, c = contours(W, H, o), sq = o.square || 200;
      var svg = s("svg", { viewBox: "0 0 " + W + " " + H, preserveAspectRatio: "none", focusable: "false" });
      /* Each set of lines is drawn once, in defs, and reused four times (dark edge, light edge, line, scan), so the page holds its path data once. */
      var id = "ls-fm" + (++fmCount), MINOR = id + "-minor", INDEX = id + "-index";
      svg.appendChild(s("defs", {}, [s("path", { id: MINOR, d: c.minor }), s("path", { id: INDEX, d: c.index })]));
      function lines(ref, cls, w, dx, dy) { return s("use", { href: "#" + ref, "class": cls, fill: "none", "stroke-width": w, "stroke-linecap": "round", transform: dx || dy ? "translate(" + dx + " " + dy + ")" : null }); }
      var grid = s("g", {});
      for (var x = sq; x < W; x += sq) { grid.appendChild(s("line", { "class": "grid", x1: x, y1: 0, x2: x, y2: H, "stroke-width": 1 })); }
      for (var y = sq; y < H; y += sq) { grid.appendChild(s("line", { "class": "grid", x1: 0, y1: y, x2: W, y2: y, "stroke-width": 1 })); }
      var refs = s("g", {}), L = "ABCDEFGHJKLMNOPQRSTUVWXYZ";
      for (var i = 0; i * sq < W; i++) { var tx = s("text", { "class": "ref", x: i * sq + sq / 2, y: H - 14, "text-anchor": "middle" }); tx.textContent = L[i % L.length]; refs.appendChild(tx); }
      for (var j = 0; j * sq < H; j++) { var ty = s("text", { "class": "ref", x: 12, y: j * sq + sq / 2 + 4, "text-anchor": "start" }); ty.textContent = (j < 9 ? "0" : "") + (j + 1); refs.appendChild(ty); }
      svg.appendChild(grid);
      svg.appendChild(s("g", {}, [lines(MINOR, "lo", 1.1, 0.7, 0.7), lines(MINOR, "hi", 1, -0.6, -0.6), lines(MINOR, "ln", 0.9)]));
      svg.appendChild(s("g", { "class": "ixg" }, [lines(INDEX, "lo", 1.8, 0.9, 0.9), lines(INDEX, "hi", 1.5, -0.8, -0.8), lines(INDEX, "ix", 1.3)]));
      svg.appendChild(refs);
      var scan = s("svg", { viewBox: "0 0 " + W + " " + H, preserveAspectRatio: "none", "class": "scan", focusable: "false" }, [lines(MINOR, "ln", 1.1), lines(INDEX, "ix", 1.6)]);
      el.textContent = ""; el.appendChild(svg); el.appendChild(scan);
    }
    draw();
    host.insertBefore(el, host.firstChild);
    function onResize() { clearTimeout(timer); timer = setTimeout(draw, 250); }
    if (!o.width) { window.addEventListener("resize", onResize); }
    return { el: el, redraw: draw, destroy: function () { window.removeEventListener("resize", onResize); el.remove(); } };
  }

  Object.assign(NS, { FieldMap: FieldMap, setBackdrop: setBackdrop, BACKDROPS: BACKDROPS });
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
  function q(v) { return NS.shellQuote ? NS.shellQuote(v) : JSON.stringify(v); }
  function centre(t) { return { x: t.rect.x + t.rect.w / 2, y: t.rect.y + t.rect.h / 2 }; }

  /* The nearest target in a direction: along the arrow first, then across it, so a row is walked before the next row is jumped to. */
  function nearest(targets, from, dx, dy) {
    var a = centre(from), best = null, score = Infinity;
    targets.forEach(function (t) {
      if (t === from) { return; }
      var b = centre(t), along = (b.x - a.x) * dx + (b.y - a.y) * dy, across = Math.abs((b.x - a.x) * dy) + Math.abs((b.y - a.y) * dx);
      if (along <= 1) { return; }
      var sc = along + across * 2;
      if (sc < score) { score = sc; best = t; }
    });
    return best;
  }

  /* OfficeView: the frame. The engine draws the building and says where things are; the frame gives it Studio's toolbar, a keyboard way round, a focus ring and room for a panel. */
  var VIEWS = [["corner", "Corner"], ["aerial", "Aerial"], ["street", "From the street"]];
  function OfficeView(o) {
    o = o || {};
    var engine = o.engine, focusId = null, hoverId = null, targets = [];
    var where = h("div", { "class": "where" });
    var live = h("span", { "class": "ls-vh", "aria-live": "polite" });
    var ringTag = h("span", { "class": "tag" });
    var ring = h("span", { "class": "oring", hidden: true }, [ringTag]);
    var hover = h("span", { "class": "oring hover", hidden: true });
    var stage = h("div", { "class": "stage", tabindex: "0", role: "group", "aria-roledescription": "office picture", "aria-keyshortcuts": "ArrowUp ArrowDown ArrowLeft ArrowRight Enter Escape Q E + - PageUp PageDown" }, [hover, ring, live]);
    var side = h("div", { "class": "side" });
    var body = h("div", { "class": "body" }, [stage, side]);
    function btn(label, title, key, on) { return h("button", { type: "button", title: title, "aria-label": title, onclick: on }, [label, key ? h("kbd", { text: key }) : null]); }
    var bBack = btn("Back to the building", "Back to the building", "Esc", function () { goBack(); });
    var bUp = btn("↑", "A level up", null, function () { engine.level(1); changed(true); });
    var bDown = btn("↓", "A level down", null, function () { engine.level(-1); changed(true); });
    var bLeft = btn("⟲", "Turn left", "Q", function () { engine.turn(-1); changed(false); });
    var bRight = btn("⟳", "Turn right", "E", function () { engine.turn(1); changed(false); });
    var bOut = btn("−", "Zoom out", null, function () { engine.zoom(-1); changed(false); });
    var bIn = btn("+", "Zoom in", null, function () { engine.zoom(1); changed(false); });
    var viewBtns = VIEWS.map(function (v) { var b = btn(v[1], "See it " + (v[0] === "street" ? "from the street" : v[0] === "aerial" ? "from above" : "from a corner"), null, function () { engine.setView(v[0]); changed(true); }); b.dataset.view = v[0]; return b; });
    var views = h("span", { "class": "grp", role: "group", "aria-label": "Outside views" }, viewBtns);
    var settings = o.settingsHref ? h("a", { href: o.settingsHref, text: "Office art and size" }) : null;
    var tools = h("div", { "class": "tools" }, [bBack, h("span", { "class": "grp", role: "group", "aria-label": "Levels" }, [bUp, bDown]), views, h("span", { "class": "grp", role: "group", "aria-label": "Turn" }, [bLeft, bRight]), h("span", { "class": "grp", role: "group", "aria-label": "Zoom" }, [bOut, bIn]), settings]);
    var keys = h("p", { "class": "keys" });
    keys.innerHTML = "<kbd>Tab</kbd> into the office · <kbd>← ↑ → ↓</kbd> move · <kbd>Enter</kbd> open · <kbd>Esc</kbd> back · <kbd>Q</kbd> <kbd>E</kbd> turn · <kbd>+</kbd> <kbd>−</kbd> zoom · <kbd>PgUp</kbd> <kbd>PgDn</kbd> level";
    var el = h("section", { "class": "ls-office", "aria-label": o.label || "The office" }, [h("div", { "class": "obar" }, [where, tools]), body, keys]);

    function byId(id) { for (var i = 0; i < targets.length; i++) { if (targets[i].id === id) { return targets[i]; } } return null; }
    function place(r, t) {
      if (!t) { r.hidden = true; return; }
      r.hidden = false; r.style.left = t.rect.x + "px"; r.style.top = t.rect.y + "px"; r.style.width = t.rect.w + "px"; r.style.height = t.rect.h + "px";
      r.classList.toggle("below", t.rect.y < 64);
    }
    function paintRing() {
      var t = byId(focusId);
      place(ring, document.activeElement === stage ? t : null);
      if (t) { ringTag.textContent = ""; ringTag.appendChild(document.createTextNode(t.label)); if (t.detail) { ringTag.appendChild(h("small", { text: t.detail })); } }
      /* Keep the tag inside the picture: shift it left when the thing is near the right edge. */
      if (t && !ring.hidden) { ringTag.style.left = "0px"; var room = stage.clientWidth - t.rect.x - 6, tw = ringTag.offsetWidth; ringTag.style.left = (tw > room ? Math.max(-t.rect.x + 6, room - tw) : 0) + "px"; }
      place(hover, hoverId && hoverId !== focusId ? byId(hoverId) : null);
    }
    function paintBar() {
      var w = engine.where();
      where.textContent = ""; where.appendChild(document.createTextNode(w.label)); if (w.detail) { where.appendChild(h("small", { text: w.detail })); }
      stage.setAttribute("aria-label", "The office: " + w.label + ". Arrow keys move between " + (w.place === "outside" ? "floors" : "people and rooms") + "; Enter opens.");
      bBack.hidden = w.place === "outside";
      bUp.disabled = !w.canLevel[0]; bDown.disabled = !w.canLevel[1];
      bLeft.disabled = bRight.disabled = !w.canTurn;
      bOut.disabled = w.zoom <= w.zoomRange[0]; bIn.disabled = w.zoom >= w.zoomRange[1];
      views.hidden = w.place !== "outside";
      viewBtns.forEach(function (b) { b.setAttribute("aria-pressed", String(b.dataset.view === w.view)); });
    }
    function announce(t) { live.textContent = t ? t.label + (t.detail ? ", " + t.detail : "") : ""; }
    function changed(moved) {
      targets = engine.targets();
      if (moved || !byId(focusId)) { focusId = targets.length ? targets[0].id : null; if (moved) { announce(byId(focusId)); } }
      paintBar(); paintRing();
    }
    function goBack() { if (side.firstChild) { hidePanel(); return true; } if (engine.back()) { changed(true); return true; } return false; }
    function activate(t) {
      if (!t) { return; }
      var r = engine.activate(t.id);
      if (r === "entered") { changed(true); return; }
      if (o.onPick) { o.onPick(t); }
    }
    function showPanel(node) { side.textContent = ""; side.appendChild(node); body.classList.add("has-side"); var f = node.querySelector("h3"); if (f) { f.tabIndex = -1; f.focus(); } setTimeout(function () { changed(false); }, 0); }
    function hidePanel() { side.textContent = ""; body.classList.remove("has-side"); stage.focus(); setTimeout(function () { changed(false); }, 0); }

    stage.addEventListener("focus", function () { if (!byId(focusId) && targets.length) { focusId = targets[0].id; } paintRing(); announce(byId(focusId)); });
    stage.addEventListener("blur", paintRing);
    side.addEventListener("keydown", function (e) { var tg = e.target && e.target.tagName; if (e.key === "Escape" && tg !== "INPUT" && tg !== "TEXTAREA" && side.firstChild) { e.preventDefault(); hidePanel(); } });
    stage.addEventListener("keydown", function (e) {
      var k = e.key, t = byId(focusId), next = null;
      if (e.ctrlKey || e.metaKey || e.altKey) { return; }
      if (k === "ArrowRight") { next = t && nearest(targets, t, 1, 0); } else if (k === "ArrowLeft") { next = t && nearest(targets, t, -1, 0); }
      else if (k === "ArrowDown") { next = t && nearest(targets, t, 0, 1); } else if (k === "ArrowUp") { next = t && nearest(targets, t, 0, -1); }
      else if (k === "Home") { next = targets[0]; } else if (k === "End") { next = targets[targets.length - 1]; }
      else if (k === "Enter" || k === " ") { e.preventDefault(); activate(t); return; }
      else if (k === "Escape") { if (goBack()) { e.preventDefault(); } return; }
      else if (k === "q" || k === "Q") { if (!bLeft.disabled) { bLeft.click(); } e.preventDefault(); return; }
      else if (k === "e" || k === "E") { if (!bRight.disabled) { bRight.click(); } e.preventDefault(); return; }
      else if (k === "+" || k === "=") { if (!bIn.disabled) { bIn.click(); } e.preventDefault(); return; }
      else if (k === "-" || k === "_") { if (!bOut.disabled) { bOut.click(); } e.preventDefault(); return; }
      else if (k === "PageUp") { if (!bUp.disabled) { bUp.click(); } e.preventDefault(); return; }
      else if (k === "PageDown") { if (!bDown.disabled) { bDown.click(); } e.preventDefault(); return; }
      else { return; }
      e.preventDefault();
      if (next) { focusId = next.id; paintRing(); announce(next); }
    });
    function hit(e) { var r = stage.getBoundingClientRect(), x = e.clientX - r.left, y = e.clientY - r.top, found = null; targets.forEach(function (t) { if (x >= t.rect.x && x <= t.rect.x + t.rect.w && y >= t.rect.y && y <= t.rect.y + t.rect.h) { found = t; } }); return found; }
    stage.addEventListener("pointermove", function (e) { var t = hit(e); hoverId = t ? t.id : null; stage.style.cursor = t ? "pointer" : "default"; paintRing(); });
    stage.addEventListener("pointerleave", function () { hoverId = null; paintRing(); });
    stage.addEventListener("click", function (e) { var t = hit(e); if (t) { focusId = t.id; activate(t); } });

    engine.mount(stage, { motion: function () { return NS.motion ? NS.motion() : "full"; }, onChange: function () { changed(false); } });
    var ro = typeof ResizeObserver === "function" ? new ResizeObserver(function () { if (engine.resize) { engine.resize(); } changed(false); }) : null;
    if (ro) { ro.observe(stage); }
    setTimeout(function () { changed(false); }, 0);
    return { el: el, stage: stage, side: side, show: showPanel, hide: hidePanel, refresh: function () { changed(false); }, focus: function (id) { if (id) { focusId = id; } stage.focus(); }, destroy: function () { if (ro) { ro.disconnect(); } engine.destroy(); el.remove(); } };
  }

  /* OfficePanel: a room or a person, with the same actions the classic popup has, as Studio buttons that show their commands. Anything that can't be undone asks first, by name. */
  function OfficePanel(o) {
    o = o || {};
    var said = h("p", { "class": "said", role: "status" });
    var close = h("button", { type: "button", "class": "close", "aria-label": "Close " + (o.title || "this"), text: "×", onclick: function () { if (o.onClose) { o.onClose(); } } });
    var kids = [close, h("p", { "class": "kind", text: o.kind || "" }), h("h3", { text: o.title || "" })];
    (o.lines || []).forEach(function (l) { if (l) { kids.push(h("p", { text: l })); } });
    if (o.list && o.list.length) { kids.push(h("ul", {}, o.list.map(function (l) { return h("li", { text: l }); }))); }
    (o.teams || []).forEach(function (t) {
      kids.push(h("p", { "class": "team", text: t.name }));
      if (t.list && t.list.length) { kids.push(h("ul", {}, t.list.map(function (l) { return h("li", { text: l }); }))); }
      if (t.open) { kids.push(NS.HaloButton({ label: "Open " + t.name, size: "small", onPress: t.open })); }
    });
    var acts = h("div", { "class": "acts" });
    (o.actions || []).forEach(function (a) {
      acts.appendChild(NS.HaloButton({ label: a.label, size: "small", kind: a.danger ? "danger" : "", cmd: a.cmd, disabledReason: a.disabledReason, onPress: function () {
        function go() { var r = a.onPress && a.onPress(); said.textContent = a.said ? "✓ " + a.said : ""; return r; }
        if (!a.ask) { go(); return; }
        NS.Dialog({ title: a.label, why: a.ask, danger: !!a.danger, role: "alertdialog", actions: [{ label: a.yes || a.label, kind: a.danger ? "danger" : "", solid: true, cmd: a.cmd, onPress: function () { go(); } }] }).open();
      } }));
    });
    if (acts.firstChild) { kids.push(acts); }
    if (o.rename) {
      var r = o.rename, id = "ls-on-" + Math.random().toString(36).slice(2, 7), input = h("input", { id: id, type: "text", value: r.current || "", placeholder: r.worked || "", autocomplete: "off" });
      var row = h("div", { "class": "row" });
      function line() { var v = input.value.trim(); return v ? "$ loadout team name " + r.run + " --room " + q(v) : "$ loadout team name " + r.run + " --clear"; }
      var save = NS.HaloButton({ label: "Name it", size: "small", cmd: line(), onPress: function () { var v = input.value.trim(); if (r.onSave) { r.onSave(v); } said.textContent = v ? "✓ Called " + v + " from now on." : "✓ Back to " + (r.worked || "the worked-out name") + "."; } });
      input.addEventListener("input", function () { var c = save.querySelector ? save.querySelector(".ls-cmd, code") : null; if (c) { c.textContent = line(); } });
      row.appendChild(save);
      kids.push(h("div", { "class": "name" }, [h("label", { "for": id, text: "Name this room" }), input, row, h("p", { "class": "hint", text: "Empty puts the worked-out name back: " + (r.worked || "") })]));
    }
    kids.push(said);
    return h("section", { "class": "ls-officepanel", "aria-label": o.title || "The office" }, kids);
  }

  Object.assign(NS, { OfficeView: OfficeView, OfficePanel: OfficePanel });
})();
/* ---------- Stage 13: commands as controls. Every command a page offers is a control made for it, not a form built from its options. ---------- */
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
  function q(v) { v = String(v); if (/^<[^<>\s]+>$/.test(v)) { return v; } return v === "" || /[\s"'$`\\|&;<>()*?!#~]/.test(v) ? "\"" + v.replace(/(["\\$`])/g, "\\$1") + "\"" : v; }
  function said(el, t, bad) { el.textContent = t ? (bad ? "" : "\u2713 ") + t : ""; el.classList.toggle("bad", !!bad); }

  /* The line a set of fields makes: arguments in order, then each option given, then anything extra. */
  function lineFor(path, fields, values, extra) {
    var args = [], opts = [];
    fields.forEach(function (f) {
      var v = values[f.name];
      if (f.flags && Object.prototype.hasOwnProperty.call(f.flags, v)) { if (f.flags[v]) { opts.push(f.flags[v]); } return; }
      if (f.always && (v === "" || v === undefined || v === null)) { opts.push(f.name); return; }
      if (f.name.slice(0, 1) !== "-") { if (f.kind === "lines" && v) { String(v).split("\n").map(function (x) { return x.trim(); }).filter(Boolean).forEach(function (x) { args.push(q(x)); }); return; } if (v !== "" && v !== undefined && v !== null) { args.push(q(v)); } else if (f.required) { args.push("<" + (f.placeholder || f.name) + ">"); } return; }
      if (f.kind === "flag") { if (v) { opts.push(f.name); } return; }
      if (v === "" || v === undefined || v === null) { return; }
      if (f.kind === "lines") { String(v).split("\n").map(function (s) { return s.trim(); }).filter(Boolean).forEach(function (s) { opts.push(f.name + " " + q(s)); }); return; }
      opts.push(f.joined ? f.name + "=" + q(v) : f.name + " " + q(v));
    });
    return "$ loadout " + [path].concat(args, opts, extra || []).join(" ");
  }

  /* One field, in plain words, with the option it sets shown small beside its label. */
  function field(f, values, onChange) {
    var id = uid("ls-cf-"), v = values[f.name], flag = f.name.slice(0, 1) === "-" ? h("code", { "class": "opt", text: f.name }) : null;
    function set(x) { values[f.name] = x; onChange(); }
    if (f.fixed) { return h("div", { "class": "ls-cfield fixed" }, [h("span", { "class": "cl", text: f.label }), h("span", { "class": "val" }, [f.kind === "flag" ? (v ? "yes" : "no") : String(v), " ", flag])]); }
    if (f.kind === "flag") {
      var cb = h("input", { type: "checkbox", id: id, disabled: f.fixed ? true : null, onchange: function () { set(cb.checked); } }); cb.checked = !!v;
      return h("label", { "class": "ls-cfield flag", "for": id }, [cb, h("span", { "class": "t" }, [f.label, f.hint ? h("small", { text: f.hint }) : null, flag])]);
    }
    var inp;
    if (f.kind === "choice") {
      inp = h("select", { "class": "ls-text", id: id, disabled: f.fixed ? true : null, onchange: function () { set(inp.value); } },
        [f.required ? (v === undefined || v === null || v === "" ? h("option", { value: "", text: f.placeholder || "Choose one", disabled: true, selected: true }) : null) : h("option", { value: "", text: f.unset || "not given" })].concat((f.options || []).map(function (o) { var val = Array.isArray(o) ? o[0] : o; var op = h("option", { value: val, text: Array.isArray(o) ? o[1] : o }); if (val === v) { op.selected = true; } return op; })));
    } else {
      inp = h(f.kind === "lines" ? "textarea" : "input", { "class": "ls-text", id: id, type: f.kind === "number" ? "number" : null, rows: f.kind === "lines" ? 3 : null, placeholder: f.placeholder || null, autocomplete: "off", spellcheck: "false", readonly: f.fixed ? true : null, oninput: function () { set(inp.value); } });
      inp.value = v === undefined || v === null ? "" : v;
    }
    return h("div", { "class": "ls-cfield" + (f.wide || f.kind === "lines" ? " wide" : "") }, [h("label", { "class": "cl", "for": id }, [f.label, f.required ? null : h("span", { "class": "opt-word", text: " (optional)" }), " ", flag]), inp, f.hint ? h("span", { "class": "hint", text: f.hint }) : null]);
  }

  function confirmThen(a, go) {
    if (!a.ask) { go(); return; }
    NS.Dialog({ title: a.askTitle || a.label, why: a.ask, danger: !!a.danger, role: "alertdialog", actions: [{ label: a.yes || a.label, kind: a.danger ? "danger" : "", solid: true, cmd: a.cmd, onPress: function () { go(); } }] }).open();
  }

  /* ActionForm: one thing a command does, in place. The fields people use, in plain words; every other option under More options; the exact line it will run; Preview when it can try it first; then the action. */
  var EFFECT = { reads: "reads only", changes: "changes something", session: "opens a terminal here, hosted by the daemon" };
  function ActionForm(o) {
    o = o || {};
    var fields = o.fields || [], values = {}, effect = o.effect || "changes";
    fields.forEach(function (f) { values[f.name] = f.value !== undefined ? f.value : (f.kind === "flag" ? false : ""); });
    var main = fields.filter(function (f) { return !f.more; }), more = fields.filter(function (f) { return f.more; });
    var line = h("code", { "class": "ls-cmdline", "aria-live": "polite" }), acts = h("div", { "class": "ls-answers" }), out = h("p", { "class": "ls-said", role: "status" }), result = h("div", { "class": "result" });
    var moreCount = h("span", { "class": "n" });
    var kids = [o.title ? h(o.heading || "h4", { "class": "t", text: o.title }) : null, o.why ? h("p", { "class": "why", text: o.why }) : null];
    if (main.length) { kids.push(h("div", { "class": "fields" }, main.map(function (f) { return field(f, values, paint); }))); }
    if (more.length) {
      kids.push(h("details", { "class": "more" }, [h("summary", {}, ["More options ", moreCount]), h("div", { "class": "fields" }, more.map(function (f) { return field(f, values, paint); }))]));
    }
    kids.push(line, acts, out, result);
    if (o.note) { kids.push(h("p", { "class": "note", text: o.note })); }
    var el = h("section", { "class": "ls-aform" + (o.compact ? " compact" : ""), "data-effect": effect, "aria-label": o.title || o.path }, kids);
    function reads() { if (o.applyWith) { return false; } return effect === "reads" || (effect === "changes" && o.changesOnlyWith && !o.changesOnlyWith.some(function (n) { return !!values[n]; })); }
    function acting(preview) { return (o.extra || []).concat(preview ? (o.previewWith || (o.applyWith ? [] : ["--dry-run"])) : (o.applyWith || [])); }
    function missing() { var m = fields.filter(function (f) { return f.required && !String(values[f.name] || "").trim(); })[0]; return m ? "say " + m.label.toLowerCase() + " first" : null; }
    function run(preview) {
      var l = lineFor(o.path, fields, values, acting(preview));
      var r = o.onRun ? o.onRun(Object.assign({}, values), { preview: !!preview, line: l }) : null;
      result.textContent = "";
      if (r && r.nodeType) { result.appendChild(r); said(out, preview ? "Previewed: nothing changed" : (o.said || "Done")); return; }
      said(out, typeof r === "string" ? r : (preview ? "Previewed: nothing changed" : (o.said || "Done")));
    }
    function paint() {
      line.textContent = lineFor(o.path, fields, values, acting(false));
      var set = more.filter(function (f) { var v = values[f.name]; return f.kind === "flag" ? !!v : v !== "" && v !== undefined && v !== null; }).length;
      moreCount.textContent = more.length ? "\u00b7 " + more.length + (set ? ", " + set + " set" : "") : "";
      acts.textContent = "";
      var dis = o.disabledReason || missing();
      if (effect === "session") { acts.appendChild(NS.HaloButton({ label: o.label || "Open it in a terminal", size: "small", glyph: "\u25b8", solid: true, cmd: o.disabledReason || missing() ? null : EFFECT.session, disabledReason: dis, onPress: function () { run(false); } })); return; }
      var r = reads();
      if (!r && (o.dryRun || o.applyWith)) { acts.appendChild(NS.HaloButton({ label: o.previewLabel || "Preview", size: "small", cmd: o.previewWith ? "adds " + o.previewWith.join(" ") + " \u00b7 changes nothing" : o.applyWith ? "runs it without " + o.applyWith.join(" ") + " \u00b7 changes nothing" : "adds --dry-run \u00b7 changes nothing", disabledReason: missing(), onPress: function () { run(true); } })); }
      var a = { label: o.label || (r ? "Show it" : "Do it"), size: "small", solid: !r, kind: o.danger ? "danger" : "", cmd: r ? EFFECT.reads : EFFECT.changes, disabledReason: (r ? missing() : dis), ask: o.ask, yes: o.yes };
      a.onPress = function () { confirmThen({ label: a.label, ask: o.ask, yes: o.yes, danger: o.danger, cmd: lineFor(o.path, fields, values, acting(false)) }, function () { run(false); }); };
      acts.appendChild(NS.HaloButton(a));
    }
    paint();
    return { el: el, values: values, line: function () { return lineFor(o.path, fields, values, acting(false)); }, set: function (k, v) { values[k] = v; paint(); }, result: result };
  }

  /* ActionRow: things done with one press, each with its line under it. */
  function ActionRow(o) {
    o = o || {};
    var out = h("p", { "class": "ls-said", role: "status" });
    var row = h("div", { "class": "ls-answers" });
    (o.actions || []).forEach(function (a) {
      row.appendChild(NS.HaloButton({ label: a.label, size: "small", kind: a.danger ? "danger" : "", glyph: a.glyph, cmd: a.cmd, disabledReason: a.disabledReason, onPress: function () { confirmThen(a, function () { var r = a.onPress && a.onPress(); said(out, typeof r === "string" ? r : (a.said || "Done")); }); } }));
    });
    return h("div", { "class": "ls-arow" }, [o.title ? h("h4", { "class": "t", text: o.title }) : null, o.why ? h("p", { "class": "why", text: o.why }) : null, row, out]);
  }

  /* SettingRow: a value a command keeps. What it is now, a control to change it, and the line that sets or clears it. */
  function SettingRow(o) {
    o = o || {};
    var cur = o.value === undefined || o.value === null ? "" : String(o.value), draft = cur, id = uid("ls-sr-");
    var now = h("span", { "class": "now" }), line = h("code", { "class": "ls-cmd" }), out = h("p", { "class": "ls-said", role: "status" }), acts = h("div", { "class": "acts" });
    var ctl;
    if (o.kind === "switch") {
      ctl = NS.Switch({ label: o.label, sub: o.sub, checked: cur === "on" || cur === "true", onChange: function (on) { draft = on ? "on" : "off"; commit(); } });
    } else if (o.kind === "choice") {
      ctl = h("select", { "class": "ls-text", id: id, onchange: function () { draft = ctl.value; paint(); } }, (cur ? [] : [h("option", { value: "", text: o.choose || "Choose one\u2026" })]).concat((o.options || []).map(function (x) { var val = Array.isArray(x) ? x[0] : x; var op = h("option", { value: val, text: Array.isArray(x) ? x[1] : x }); if (val === cur) { op.selected = true; } return op; })));
    } else {
      ctl = h("input", { "class": "ls-text", id: id, type: o.kind === "number" ? "number" : "text", placeholder: o.placeholder || null, autocomplete: "off", spellcheck: "false", oninput: function () { draft = ctl.value; paint(); } });
      ctl.value = cur;
    }
    function commit() { var v = draft.trim(); var r = v ? (o.onSet && o.onSet(v)) : (o.onClear && o.onClear()); cur = v; said(out, typeof r === "string" ? r : (v ? (o.setSaid || "Set") : (o.clearSaid || "Cleared"))); paint(); }
    function paint() {
      var v = String(draft).trim();
      now.textContent = cur ? (o.show ? o.show(cur) : cur) : (o.unset || "not set");
      now.classList.toggle("unset", !cur);
      line.textContent = v ? o.setCmd(v) : (cur && o.clearCmd ? o.clearCmd : o.setCmd("<" + (o.valueWord || "value") + ">"));
      acts.textContent = "";
      if (o.kind === "switch") { line.textContent = o.setCmd(cur === "on" ? "off" : "on"); return; }
      acts.appendChild(NS.HaloButton({ label: o.setLabel || "Set it", size: "small", solid: true, disabledReason: !v ? "write a value first" : v === cur ? "that is already the value" : null, onPress: commit }));
      if (o.clearCmd && cur) { acts.appendChild(NS.HaloButton({ label: o.clearLabel || "Clear it", size: "small", onPress: function () { draft = ""; if (ctl.value !== undefined && o.kind !== "choice") { ctl.value = ""; } commit(); } })); }
    }
    paint();
    var head = o.kind === "switch" ? [ctl] : [h("label", { "class": "nm", "for": id, text: o.label }), o.sub ? h("span", { "class": "sub", text: o.sub }) : null, h("span", { "class": "nowwrap" }, [h("span", { "class": "k", text: "now " }), now])];
    return h("div", { "class": "ls-setrow" + (o.kind === "switch" ? " sw" : "") }, [h("div", { "class": "head" }, head), o.kind === "switch" ? null : h("div", { "class": "ctl" }, [ctl, acts]), line, out]);
  }

  /* ItemList: the things a command keeps, each with what can be done to it, and a way to add one. The commands a row's buttons run show under the row, for whichever button is pointed at or focused. */
  function ItemList(o) {
    o = o || {};
    var items = (o.items || []).slice(), out = h("p", { "class": "ls-said", role: "status" });
    var list = h("ul", { "class": "rows" }), addBox = h("div", { "class": "add", hidden: true }), count = h("span", { "class": "count" });
    var addBtn = o.add ? h("button", { type: "button", "class": "addbtn", "aria-expanded": "false", onclick: function () { var open = addBox.hidden; addBox.hidden = !open; addBtn.setAttribute("aria-expanded", String(open)); if (open) { var f = addBox.querySelector("input, select, textarea"); if (f) { f.focus(); } } } }, ["+ ", o.add.label || "Add one"]) : null;
    if (o.add) { var form = ActionForm(Object.assign({ compact: true }, o.add.form, { onRun: function (v, info) { var r = o.add.form.onRun ? o.add.form.onRun(v, info) : null; if (!info.preview && o.add.toItem) { var it = o.add.toItem(v); if (it) { items.unshift(it); paint(); addBox.hidden = true; addBtn.setAttribute("aria-expanded", "false"); said(out, o.add.said ? o.add.said(it) : "Added " + it.name); } } return r; } })); addBox.appendChild(form.el); }
    function row(it) {
      var peek = h("code", { "class": "ls-cmd peek" });
      var acts = h("div", { "class": "acts" });
      var det = it.detail || (it.actions || []).some(function (a) { return a.form; }) ? h("div", { "class": "det", hidden: true }) : null;
      var first = null;
      (it.actions || []).forEach(function (a) {
        var b = h("button", { type: "button", "class": "rb" + (a.danger ? " danger" : ""), disabled: a.disabledReason ? true : null, title: a.disabledReason || null, text: a.label });
        function show() { peek.textContent = a.disabledReason ? a.label + ": " + a.disabledReason : a.cmd || ""; }
        b.addEventListener("mouseenter", show); b.addEventListener("focus", show);
        b.addEventListener("click", function () {
          if (a.form && det) { var open = det.dataset.form === a.label && !det.hidden; det.textContent = ""; det.hidden = open; det.dataset.form = open ? "" : a.label;
            if (!open) { det.appendChild(ActionForm(Object.assign({ compact: true }, a.form, { onRun: function (v, info) { var r = a.form.onRun ? a.form.onRun(v, info, it) : null; if (!info.preview && a.removes) { items = items.filter(function (x) { return x !== it; }); paint(); } if (!info.preview) { said(out, a.said ? (typeof a.said === "function" ? a.said(it) : a.said) : a.label + ": done"); } return r; } })).el); }
            return; }
          confirmThen({ label: a.label + (a.ask ? "" : ""), askTitle: a.askTitle, ask: a.ask, yes: a.yes, danger: a.danger, cmd: a.cmd }, function () {
          var r = a.onPress && a.onPress(it);
          if (a.removes) { items = items.filter(function (x) { return x !== it; }); paint(); }
          if (r && r.nodeType && det) { det.textContent = ""; det.appendChild(r); det.hidden = false; }
          said(out, typeof r === "string" ? r : (a.said ? (typeof a.said === "function" ? a.said(it) : a.said) : a.label + ": done")); }); });
        acts.appendChild(b); if (!first) { first = a; }
      });
      function reset() { peek.textContent = first && !first.disabledReason ? first.cmd || "" : ""; }
      reset();
      acts.addEventListener("mouseleave", function () { if (!acts.contains(document.activeElement)) { reset(); } });
      acts.addEventListener("focusout", function (e) { if (!acts.contains(e.relatedTarget)) { reset(); } });
      var openBtn = it.detail ? h("button", { type: "button", "class": "rb more", "aria-expanded": "false", text: it.detailLabel || "Details", onclick: function () { var open = det.hidden; if (open && !det.firstChild) { det.appendChild(it.detail()); } det.hidden = !open; openBtn.setAttribute("aria-expanded", String(open)); } }) : null;
      if (openBtn) { acts.insertBefore(openBtn, acts.firstChild); }
      return h("li", { "class": "row" }, [
        h("div", { "class": "who" }, [h("span", { "class": "nm", text: it.name }), it.sub ? h("span", { "class": "sub", text: it.sub }) : null]),
        it.word ? NS.Signal(it.word) : h("span", {}),
        h("div", { "class": "facts" }, (it.facts || []).map(function (f) { return h("span", { text: f }); })),
        acts, peek, det]);
    }
    function paint() {
      list.textContent = "";
      count.textContent = items.length + " " + (items.length === 1 ? (o.one || "item") : (o.many || "items"));
      if (!items.length) { list.appendChild(h("li", { "class": "empty", text: o.empty || "None yet." })); }
      items.forEach(function (it) { list.appendChild(row(it)); });
    }
    paint();
    var el = h("section", { "class": "ls-ilist", "aria-label": o.title || o.label || null }, [
      h("div", { "class": "bar" }, [o.title ? h(o.heading || "h4", { "class": "t", text: o.title }) : null, count, h("span", { "class": "grow" }), addBtn]),
      o.why ? h("p", { "class": "why", text: o.why }) : null, addBox, list, out,
      o.readCmd ? h("p", { "class": "from" }, ["From ", h("code", { "class": "ls-cmd", text: o.readCmd }), o.asOf ? " \u00b7 " + o.asOf : ""]) : null]);
    return { el: el, items: function () { return items; }, set: function (xs) { items = xs.slice(); paint(); } };
  }

  /* Readout: what a reading command says, drawn as a view, never as a transcript. Small settings beside it change what it reads; the line it reads with is under it. */
  function Readout(o) {
    o = o || {};
    var fields = o.params || [], values = {};
    fields.forEach(function (f) { values[f.name] = f.value !== undefined ? f.value : (f.kind === "flag" ? false : ""); });
    var body = h("div", { "class": "body" }), line = h("code", { "class": "ls-cmd" }), when = h("span", { "class": "when" });
    function view(data) {
      body.textContent = "";
      if (data === null || data === undefined) { body.appendChild(h("p", { "class": "calm", text: o.waiting || "Not read yet." })); return; }
      var kind = o.view || "list", node;
      if (o.render) { node = o.render(data, values); }
      else if (kind === "figures") { node = h("dl", { "class": "figs" }, data.map(function (f) { return h("div", { "class": "fig" }, [h("dt", { text: f.label }), h("dd", {}, [h("b", { text: f.value }), f.note ? h("small", { text: f.note }) : null])]); })); }
      else if (kind === "table") {
        var cols = o.columns || Object.keys(data[0] || {}).map(function (k) { return { key: k, label: k }; });
        node = data.length ? h("div", { "class": "tw" }, [h("table", {}, [h("thead", {}, [h("tr", {}, cols.map(function (c) { return h("th", { scope: "col", text: c.label, "class": c.num ? "num" : null }); }))]), h("tbody", {}, data.map(function (r) { return h("tr", {}, cols.map(function (c) { var v = c.format ? c.format(r[c.key], r) : r[c.key]; return h("td", { "class": c.num ? "num" : null }, [v && v.nodeType ? v : String(v === undefined || v === null ? "" : v)]); })); }))])]) : h("p", { "class": "calm", text: o.empty || "Nothing to show." });
      }
      else if (kind === "document") { node = NS.Markdown ? NS.Markdown(String(data)) : h("pre", { text: String(data) }); }
      else if (kind === "findings") { node = NS.HealthList(Object.assign({ findings: data.findings || data }, data.findings ? data : {})); }
      else { node = data.length ? h("ul", { "class": "plain" }, data.map(function (x) { return h("li", {}, typeof x === "string" ? [x] : [h("b", { text: x.name }), x.sub ? " \u00b7 " + x.sub : ""]); })) : h("p", { "class": "calm", text: o.empty || "Nothing to show." }); }
      body.appendChild(node);
    }
    function read() { line.textContent = lineFor(o.path, fields, values, (o.extra || []).concat(["--json"])); var d = o.load ? o.load(Object.assign({}, values)) : o.data; view(d); when.textContent = d === null || d === undefined ? "" : "read just now"; }
    var params = fields.length ? h("div", { "class": "params" }, fields.map(function (f) { return field(f, values, function () { line.textContent = lineFor(o.path, fields, values, (o.extra || []).concat(["--json"])); }); })) : null;
    var again = NS.HaloButton({ label: o.readLabel || "Read it again", size: "small", glyph: "\u21bb", onPress: read });
    var el = h("section", { "class": "ls-readout", "aria-label": o.title || o.path }, [
      h("div", { "class": "bar" }, [o.title ? h(o.heading || "h4", { "class": "t", text: o.title }) : null, when, h("span", { "class": "grow" }), again]),
      o.why ? h("p", { "class": "why", text: o.why }) : null, params, body, h("p", { "class": "from" }, ["Read with ", line])]);
    if (o.lazy) { line.textContent = lineFor(o.path, fields, values, (o.extra || []).concat(["--json"])); view(null); } else { read(); }
    return { el: el, read: read, values: values };
  }

  Object.assign(NS, { ActionForm: ActionForm, ActionRow: ActionRow, SettingRow: SettingRow, ItemList: ItemList, Readout: Readout, controlLine: lineFor });
})();
/* end stage 13 */
