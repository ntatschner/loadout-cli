/* @ds-bundle: {"format": 4, "namespace": "LoadoutStudio", "components": [{"name": "HaloButton"}, {"name": "StartRail"}, {"name": "Command"}, {"name": "Switch"}, {"name": "Tabs"}, {"name": "ModePicker"}, {"name": "Signal"}, {"name": "EventMark"}, {"name": "Urgency"}, {"name": "Meter"}, {"name": "Toasts"}, {"name": "SideRail"}, {"name": "Dialog"}, {"name": "Picker"}, {"name": "CommandPalette"}, {"name": "Halo"}, {"name": "TubeMap"}, {"name": "LineStrip"}, {"name": "NetworkStatus"}, {"name": "Terminal"}, {"name": "SessionView"}, {"name": "Gate"}, {"name": "RunCard"}, {"name": "NeedsYou"}, {"name": "RunShape"}, {"name": "Crew"}, {"name": "DoneWhen"}, {"name": "RunNumbers"}, {"name": "RunDepths"}, {"name": "Patch"}, {"name": "SayToNode"}, {"name": "TeamCard"}, {"name": "TeamNodes"}, {"name": "TeamRules"}, {"name": "RoleRecord"}, {"name": "TeamSheet"}, {"name": "Schedules"}, {"name": "Remedy"}, {"name": "Bin"}, {"name": "Outbox"}, {"name": "ModeSwitcher"}, {"name": "ModeBar"}, {"name": "SessionTabs"}, {"name": "TermDeck"}, {"name": "RunBoard"}, {"name": "QuietList"}, {"name": "FocusFrame"}, {"name": "WorkDesk"}, {"name": "ActionPane"}, {"name": "CommandBar"}, {"name": "KeyHelp"}, {"name": "ActionDrawer"}, {"name": "TerminalOnly"}, {"name": "HealthList"}, {"name": "IdeaStages"}, {"name": "QuestionSet"}, {"name": "PlanLayer"}, {"name": "NoteDump"}, {"name": "ProjectTile"}, {"name": "TaskBoard"}, {"name": "LoadStack"}, {"name": "ConfigList"}, {"name": "StoreList"}, {"name": "TopicEditor"}, {"name": "TokenSplit"}, {"name": "SpendBars"}, {"name": "PageBeside"}, {"name": "QuietPage"}, {"name": "ForgetProject"}, {"name": "ProgressLine"}, {"name": "CriteriaGrid"}, {"name": "PickUpSheet"}, {"name": "RefusalsList"}, {"name": "RedAlert"}, {"name": "TaskPicker"}, {"name": "ValueReport"}, {"name": "FieldMap"}, {"name": "OfficeView"}, {"name": "OfficePanel"}, {"name": "ActionForm"}, {"name": "ItemList"}, {"name": "SettingRow"}, {"name": "Readout"}, {"name": "ActionRow"}, {"name": "AskPane"}, {"name": "AskPlanCard"}, {"name": "BlockedRuns"}, {"name": "ContextMark"}, {"name": "ConversationList"}, {"name": "Handover"}, {"name": "JobResult"}, {"name": "Journal"}, {"name": "ListToolbar"}, {"name": "MiniLine"}, {"name": "ProgressRing"}, {"name": "QueuePanel"}, {"name": "Spend"}, {"name": "SplitCross"}, {"name": "Splitter"}, {"name": "StatusBlock"}, {"name": "StatusStrip"}, {"name": "TaskDetail"}, {"name": "TriggerRules"}, {"name": "Landings"}, {"name": "MapFilterBar"}, {"name": "LayoutEdit"}, {"name": "Peek"}]} */
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
  /* Teams words as main's signals.ts has them since 0.104: a lead or a node that went silent twice and was given up on, and a run abandoned. */
  STATES.stalled = ["bad", "blocked", "hard"]; STATES.stuck = ["bad", "blocked", "hard"]; STATES.abandoned = ["rest", "stopped", "still"];

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

  /* HaloButton: a word beside a ring of light. kind: "" | "needs" | "danger"; size: "" | "small" | "big".
     describedBy points the button at a line that says more; key and next mark it as main's data-k and data-next do. */
  /* busy: while what it started is working, the label gives way to the busy word ("ending…"; true is "working…"), it is inactive and aria-busy. */
  function HaloButton(o) {
    o = o || {};
    var cls = ["ls-halo", o.kind, o.size, o.solid ? "solid" : ""].filter(Boolean).join(" ");
    var ring = h("span", { "class": "ring", "aria-hidden": "true" }, o.glyph ? [h("b", { text: o.glyph })] : []);
    var word = o.busy === true ? BUSY_WORD : o.busy || "";
    var btn = h("button", { type: "button", "class": cls, "aria-disabled": o.disabledReason || o.inactive || word ? "true" : null, "aria-busy": word ? "true" : null, "aria-describedby": o.describedBy || null, "data-k": o.key || null, "data-next": o.next || null,
      onclick: function (e) { if (!o.disabledReason && !o.inactive && btn.getAttribute("aria-busy") !== "true" && o.onPress) { o.onPress(e); } } }, [ring, word || o.label || ""]);
    btn.__lsLabel = o.label || ""; btn.__lsWas = o.disabledReason || o.inactive ? "true" : null;
    if (!o.cmd && !o.disabledReason) { return btn; }
    var line = cmd(o.disabledReason || o.cmd);
    if (o.disabledReason && line) { line.classList.add("reason"); }
    return h("div", { "class": "ls-action" }, [btn, line]);
  }
  var BUSY_WORD = "working\u2026";
  /* The button itself, given it or the wrapper HaloButton returned. */
  function haloOf(node) { if (!node) { return null; } if (node.classList && node.classList.contains("ls-halo")) { return node; } return node.querySelector ? node.querySelector("button.ls-halo") : null; }
  /* HaloButton.setBusy(node, "ending…" | true | false): puts a button made by HaloButton into its busy state, or back as it was. */
  function setBusy(node, busy) {
    var btn = haloOf(node); if (!btn) { return; }
    var word = busy === true ? BUSY_WORD : busy || "";
    var text = btn.lastChild && btn.lastChild.nodeType === 3 ? btn.lastChild : btn.appendChild(document.createTextNode(""));
    if (word) {
      if (btn.getAttribute("aria-busy") !== "true") { btn.__lsWas = btn.getAttribute("aria-disabled"); }
      text.data = word; btn.setAttribute("aria-busy", "true"); btn.setAttribute("aria-disabled", "true");
    } else if (btn.getAttribute("aria-busy") === "true") {
      text.data = btn.__lsLabel || ""; btn.removeAttribute("aria-busy");
      if (btn.__lsWas) { btn.setAttribute("aria-disabled", btn.__lsWas); } else { btn.removeAttribute("aria-disabled"); }
    }
  }
  HaloButton.setBusy = setBusy;
  HaloButton.BUSY = BUSY_WORD;

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

  /* liveFirst: live work first, as WORKFLOW.md's Decisions put it. Four groups, in this order: waiting on you (a gate or question,
     a trigger approval, a session waiting or asking for approval, an idea with questions open); running; you might pick it up (failed,
     blocked or ended short of its done-when, ended when the daemon stopped, a round that failed, a branch waiting to land); and history
     (ended, done, past conversations). Within each live group the oldest wait comes first; history keeps the order it was given.
     Returns a new array; the items are not changed. */
  var LIVE_ORDER = ["waiting", "running", "pickup", "history"];
  var LIVE_NAMES = { waiting: "Waiting on you", running: "Running", pickup: "Pick up", history: "History" };
  var LIVE_WORDS = {};
  [["waiting", ["waiting", "waiting on you", "waiting for you", "needs a decision", "needs approval", "approval", "approving", "asking", "asked", "gate", "question", "questions", "questions open"]],
   ["running", ["running", "working", "doing", "round running", "starting", "shaping", "refining", "idle", "connecting", "reconnecting", "waiting on background work", "background", "held", "held after this round", "live"]],
   ["pickup", ["failed", "last round failed", "round failed", "blocked", "stalled", "limited", "unmet", "short", "ended short", "lost", "ended when the daemon stopped", "exited with an error", "error", "waiting to land", "to land", "landing"]],
   ["history", ["ended", "done", "met", "accepted", "stopped", "dropped", "moved", "abandoned", "merged", "history", "past"]]
  ].forEach(function (g) { g[1].forEach(function (w) { LIVE_WORDS[w] = g[0]; }); });
  function liveState(it, opts) { var s = opts && opts.state ? opts.state(it) : (it.state !== undefined && it.state !== null ? it.state : it.word); return s === undefined || s === null ? "" : String(s).toLowerCase(); }
  function openQuestions(it) { return !!(it.questions && it.questions.length && it.questions.some(function (x) { return x && !x.answer; })); }
  /* Which of the four groups an item is in: what opts.group says, then waiting or a gate, then its state word, then its other flags,
     then the word's signal family. */
  function liveGroup(it, opts) {
    it = it || {};
    var given = opts && opts.group ? opts.group(it) : undefined;
    if (given && LIVE_NAMES[given]) { return given; }
    if (it.waiting || it.gate) { return "waiting"; }
    var s = liveState(it, opts);
    /* A blocked team run waits on you: nothing happens until you confirm what it does next. (A blocked task is still one to pick up.) */
    if (s === "blocked" && (it.kind === "team run" || it.kind === "run")) { return "waiting"; }
    if (LIVE_WORDS[s]) { return LIVE_WORDS[s]; }
    if (openQuestions(it)) { return "waiting"; }
    if (it.failed || it.blocked || it.problem || it.lost || it.short) { return "pickup"; }
    if (it.ended || it.done) { return "history"; }
    if (it.running || it.stage === 2) { return "running"; }
    if (s && STATES[s]) { var f = STATES[s][0]; return f === "needs" ? "waiting" : f === "moving" ? "running" : f === "bad" ? "pickup" : "history"; }
    return "history";
  }
  /* When an item's wait began: since, at or askedAt (a number, a Date or a date in words); null when it has none. */
  function liveSince(it, opts) {
    var v = opts && opts.since ? opts.since(it) : [it.since, it.at, it.askedAt].filter(function (x) { return x !== undefined && x !== null && x !== ""; })[0];
    if (v === undefined || v === null || v === "") { return null; }
    if (typeof v === "number") { return v; }
    if (v instanceof Date) { return v.getTime(); }
    var t = Date.parse(v); return isNaN(t) ? null : t;
  }
  function liveFirst(items, opts) {
    opts = opts || {};
    var rows = (items || []).map(function (it, i) { return { it: it, i: i, g: LIVE_ORDER.indexOf(liveGroup(it, opts)), t: liveSince(it, opts) }; });
    if (opts.only) { rows = rows.filter(function (r) { return opts.only.indexOf(LIVE_ORDER[r.g]) >= 0; }); }
    rows.sort(function (a, b) {
      if (a.g !== b.g) { return a.g - b.g; }
      if (a.g < 3 && a.t !== b.t) { if (a.t === null) { return 1; } if (b.t === null) { return -1; } return a.t - b.t; }
      return a.i - b.i;
    });
    return rows.map(function (r) { return r.it; });
  }
  /* waitingOf: the one count of what is waiting on you, and the list, oldest first. A line or a run counts while it waits on you
     (a gate, a question, a session waiting or asking for approval); every trigger approval counts; an idea counts while a question
     is open (open: N, or a question with no answer). Something given twice under one id (a run that is also a line) counts once. */
  function waitingOf(state) {
    state = state || {};
    var seen = {}, list = [];
    function add(kind, it, counts) {
      if (!it || !counts) { return; }
      var key = it.id !== undefined && it.id !== null ? String(it.id) : null;
      if (key !== null) { if (seen[key]) { return; } seen[key] = true; }
      list.push({ kind: kind, id: key, title: it.title || it.goal || it.name || it.rule || key || "", since: liveSince(it), item: it });
    }
    (state.lines || []).forEach(function (l) { add(l.kind || "line", l, liveGroup(l) === "waiting"); });
    (state.runs || []).forEach(function (r) { add("team run", r, liveGroup(r) === "waiting"); });
    (state.approvals || []).forEach(function (a) { add("approval", a, true); });
    (state.ideas || []).forEach(function (x) { add("idea", x, typeof x.open === "number" ? x.open > 0 : x.questions ? openQuestions(x) : liveGroup(x) === "waiting"); });
    list = liveFirst(list, { group: function () { return "waiting"; } });
    return { count: list.length, list: list };
  }

  /* Urgency: rank waiting items and raise the first; the rest recede. The order is liveFirst's: what waits on you before a problem,
     the oldest first within each. */
  function rankUrgent(items) {
    return liveFirst(items.filter(function (i) { return i.waiting || i.problem; }), {
      group: function (i) { return i.waiting ? "waiting" : "pickup"; }, since: function (i) { return i.askedAt || 0; } });
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

  /* Toasts: as main's Toasts.svelte and toasts.svelte.ts. A region top right. Done and For your information fold into a chip
     after 4 seconds and leave after 10, into Earlier; Needs you and Problem stay open until answered or acknowledged and then
     follow the same times. Each toast counts its own seconds, so the one under the pointer or holding the keyboard stops while
     the rest go on. The same Done or notice again counts up instead of stacking. In a named place ("session", "pure", "desk")
     everything arrives as a chip, and Done and notice chips go see-through and let clicks pass while the pointer is near.
     Ctrl+Shift+X folds what is open and, pressed again, clears what is not waiting on you. */
  var LEVELS = { done: ["✓", "Done"], info: ["↻", "For your information"], needs: ["!", "Needs you"], problem: ["✕", "Problem"] };
  var TOAST_OPEN_FOR = 4, TOAST_GONE_AFTER = 10, TOAST_KEPT = 50, TOAST_NEAR = 48;
  function Toasts(region, o) {
    o = o || {};
    var list = [], past = [], seq = 0, tucked = false, place = null, near = false, pointed = null, focused = null, showPast = false, timer = null;
    var polite = h("p", { role: "status", "class": "ls-sr" }), alert = h("p", { role: "alert", "class": "ls-sr" });
    region.classList.add("ls-toasts"); region.setAttribute("role", "region"); region.setAttribute("aria-label", "Notifications"); region.setAttribute("aria-keyshortcuts", "Control+Shift+X");
    region.parentNode.insertBefore(polite, region.nextSibling); region.parentNode.insertBefore(alert, polite.nextSibling);
    function shown(t) { return t.settled ? "done" : t.level; }
    /* Whether its seconds count: a question waiting on someone never leaves by itself. */
    function timed(t) { return t.level === "done" || t.level === "info" || t.settled; }
    /* Done and notices give way in a named place; a question waiting on you never does. */
    function givesWay(t) { return place !== null && shown(t) !== "needs" && shown(t) !== "problem"; }
    function full() { return !NS.motion || NS.motion() === "full"; }
    function tk(node, key) { var b = node.tagName === "BUTTON" ? node : node.querySelector("button"); if (b) { b.setAttribute("data-tk", key); } return node; }
    function remember(gone) {
      if (!gone.length) { return; }
      gone.forEach(function (t) { if (pointed === t.id) { pointed = null; } if (focused === t.id) { focused = null; } });
      past = gone.map(function (t) { return { id: t.id, level: shown(t), text: t.text, cmd: t.cmd, count: t.count }; }).concat(past).slice(0, TOAST_KEPT);
    }
    function leave(gone) { var ids = gone.map(function (t) { return t.id; }); list = list.filter(function (t) { return ids.indexOf(t.id) < 0; }); remember(gone); paint(); }
    function watch(n, t) {
      n.addEventListener("mouseenter", function () { pointed = t.id; }); n.addEventListener("mouseleave", function () { if (pointed === t.id) { pointed = null; } });
      n.addEventListener("focusin", function () { focused = t.id; }); n.addEventListener("focusout", function () { if (focused === t.id) { focused = null; } });
    }
    /* An answer does its thing (it may return a promise), then settles the toast with the words it returns, or its label. */
    function answer(t, ac) {
      var w = ac.onPress ? ac.onPress() : ac.onpress ? ac.onpress() : null;
      function done(v) { settle(t.id, typeof v === "string" && v ? v : ac.label); }
      if (w && typeof w.then === "function") { w.then(done, function () { done(null); }); } else { done(w); }
    }
    function paint() {
      var a = document.activeElement, keep = a && region.contains(a) ? a.getAttribute("data-tk") : null;
      region.textContent = ""; region.classList.toggle("near", near); region.classList.toggle("calm", !full());
      list.forEach(function (t) {
        var lv = shown(t), L = LEVELS[lv], anim = t.fresh && full() ? " anim" : "";
        if (t.folded) {
          var chip = h("button", { type: "button", "class": "ls-toast-chip " + lv + anim, "data-gives-way": givesWay(t) ? "" : null, "data-tk": "c" + t.id, "aria-label": L[1] + ": " + t.text + (t.count > 1 ? ", " + t.count + " times" : "") + ". Open it",
            onclick: function () { open(t.id); var x = region.querySelector('[data-tk="x' + t.id + '"]'); if (x) { x.focus({ preventScroll: true }); } } },
            [h("span", { "class": "gl", "aria-hidden": "true", text: L[0] }), h("b", { text: L[1] }), h("span", { text: t.text }), t.count > 1 ? h("i", { "class": "n", "aria-hidden": "true", text: "×" + t.count }) : null]);
          watch(chip, t); region.appendChild(chip); return;
        }
        var card = h("div", { "class": "ls-toast " + lv + anim, role: "group", "aria-label": L[1] }, [
          h("div", { "class": "th" }, [h("span", { "class": "gl", "aria-hidden": "true", text: L[0] }), h("span", { "class": "lv" }, [L[1], t.count > 1 ? h("i", { "class": "n", text: " ×" + t.count }) : null]),
            h("button", { type: "button", "class": "x", "data-tk": "x" + t.id, "aria-label": "Dismiss: " + t.text, text: "✕", onclick: function () { dismiss(t.id); } })]),
          h("p", { text: t.text }), cmd(t.cmd),
          t.actions.length ? h("div", { "class": "acts" }, t.actions.map(function (ac, i) {
            return tk(HaloButton({ label: ac.label, kind: ac.kind || "", size: "small", cmd: ac.cmd, onPress: function () { answer(t, ac); } }), "t" + t.id + "a" + i);
          })) : null]);
        watch(card, t); region.appendChild(card);
      });
      if (past.length) {
        region.appendChild(h("button", { type: "button", "class": "ls-toast-past", "aria-expanded": showPast ? "true" : "false", "data-tk": "past", onclick: function () { showPast = !showPast; paint(); } }, ["Earlier ", h("b", { text: String(past.length) })]));
        if (showPast) {
          region.appendChild(h("div", { "class": "ls-toast-history", role: "group", "aria-label": "Earlier notifications, this session" }, [
            h("ul", {}, past.map(function (g) { return h("li", { "class": g.level }, [h("span", { "class": "gl", "aria-hidden": "true", text: LEVELS[g.level][0] }), h("span", { "class": "t" }, [h("b", { text: LEVELS[g.level][1] }), " " + g.text, g.count > 1 ? h("i", { "class": "n", text: " ×" + g.count }) : null]), cmd(g.cmd)]); })),
            h("button", { type: "button", "class": "clear", "data-tk": "clear", text: "Clear", onclick: function () { past = []; showPast = false; paint(); var p = region.querySelector(".ls-toast-chip, .ls-toast .x"); if (p) { p.focus({ preventScroll: true }); } } })]));
        }
      }
      if (keep) {
        /* An answered toast's buttons are gone: its dismiss button takes the keyboard instead. */
        var k = region.querySelector('[data-tk="' + keep + '"]') || (/^t\d+a/.test(keep) ? region.querySelector('[data-tk="x' + keep.slice(1).split("a")[0] + '"]') : null);
        if (k) { k.focus({ preventScroll: true }); }
      }
    }
    /* Adds one at the top of the stack, or counts one more of the same Done or notice. */
    function add(t) {
      var c = t.cmd || "", level = LEVELS[t.level] ? t.level : "info";
      if (level === "done" || level === "info") {
        var same = list.filter(function (x) { return x.level === level && !x.settled && x.text === t.text && x.cmd === c; })[0];
        if (same) { same.count += 1; same.aged = 0; polite.textContent = LEVELS[level][1] + ": " + t.text + ", " + same.count + " times"; paint(); return same.id; }
      }
      list.forEach(function (x) { x.fresh = false; });
      var n = { id: ++seq, level: level, text: t.text || "", cmd: c, actions: t.actions || [], aged: 0, folded: tucked, settled: false, fresh: true, count: 1 };
      list.unshift(n);
      if (level === "problem") { alert.textContent = "Problem: " + n.text; } else { polite.textContent = LEVELS[level][1] + ": " + n.text; }
      paint(); return n.id;
    }
    /* Answered or acknowledged: it shows as done, with these words, and from now on follows Done's times. */
    function settle(id, words) {
      list.forEach(function (x) { if (x.id === id) { x.settled = true; x.aged = 0; x.text = words || x.text; x.actions = []; x.fresh = false; } });
      polite.textContent = words || ""; if (focused === id) { focused = null; } paint();
    }
    /* Opens a folded one again, for another run of the times. */
    function open(id) { list.forEach(function (x) { if (x.id === id) { x.folded = false; x.aged = 0; } }); if (focused === id) { focused = null; } paint(); }
    function dismiss(id) { leave(list.filter(function (x) { return x.id === id; })); }
    /* Entering a named place folds everything to a chip; leaving it opens again whatever still needs someone. */
    function tuck(on) {
      if (on === tucked) { return; }
      tucked = on;
      list.forEach(function (x) { if (on) { x.folded = true; x.fresh = false; } else if (!timed(x)) { x.folded = false; x.fresh = false; } });
    }
    function setPlace(p) {
      p = p === "session" || p === "pure" || p === "desk" ? p : null;
      if (p === place) { return; }
      place = p; if (p === null) { near = false; }
      tuck(p !== null); paint();
    }
    /* The key: folds whatever is open; with nothing open, sends everything not waiting on someone into the history. */
    function sweep() {
      if (list.some(function (x) { return !x.folded; })) { list.forEach(function (x) { x.folded = true; x.fresh = false; }); paint(); return; }
      leave(list.filter(timed));
    }
    /* One second passes. Called by the clock, or by a page that runs its own (clock: false). */
    function tick() {
      var gone = [], changed = false;
      list = list.filter(function (t) {
        if (!timed(t) || t.id === pointed || t.id === focused) { return true; }
        t.aged += 1;
        if (t.aged >= TOAST_GONE_AFTER) { gone.push(t); changed = true; return false; }
        if (t.aged >= TOAST_OPEN_FOR && !t.folded) { t.folded = true; t.fresh = false; changed = true; }
        return true;
      });
      remember(gone);
      if (changed) { paint(); }
    }
    /* A chip that lets clicks through never sees the pointer, so the page is watched instead, and only in a named place. */
    function onMove(e) {
      if (place === null) { if (near) { near = false; region.classList.remove("near"); } return; }
      var now = Array.prototype.some.call(region.querySelectorAll("[data-gives-way]"), function (c) {
        var b = c.getBoundingClientRect(), dx = Math.max(b.left - e.clientX, 0, e.clientX - b.right), dy = Math.max(b.top - e.clientY, 0, e.clientY - b.bottom);
        return Math.sqrt(dx * dx + dy * dy) <= TOAST_NEAR;
      });
      if (now !== near) { near = now; region.classList.toggle("near", near); }
    }
    /* Ctrl+Shift+X, caught before a terminal takes it. */
    function onKey(e) {
      if (e.ctrlKey && e.shiftKey && !e.altKey && !e.metaKey && e.code === "KeyX" && list.length) { e.preventDefault(); e.stopPropagation(); sweep(); }
    }
    window.addEventListener("pointermove", onMove, { passive: true });
    window.addEventListener("keydown", onKey, true);
    if (o.clock !== false) { timer = setInterval(tick, 1000); }
    if (o.place) { setPlace(o.place); }
    return { add: add, settle: settle, open: open, dismiss: dismiss, sweep: sweep, tick: tick, place: setPlace,
      clearHistory: function () { past = []; showPast = false; paint(); },
      list: function () { return list.slice(); }, history: function () { return past.slice(); },
      stop: function () { clearInterval(timer); window.removeEventListener("pointermove", onMove); window.removeEventListener("keydown", onKey, true); } };
  }

  Object.assign(NS, {
    HaloButton: HaloButton, Command: Command, StartRail: StartRail, Switch: Switch, Tabs: Tabs, ModePicker: ModePicker,
    Signal: Signal, signalOf: signalOf, wordFor: wordFor, SIGNAL_STATES: STATES, SIGNAL_GLYPHS: GLYPHS,
    EventMark: EventMark, ping: ping, rankUrgent: rankUrgent, applyUrgency: applyUrgency,
    liveFirst: liveFirst, liveGroup: liveGroup, LIVE_GROUPS: LIVE_NAMES, waitingOf: waitingOf,
    Meter: Meter, ProgressRing: ProgressRing, Toasts: Toasts, roving: roving
  });
})();

(function () {
  "use strict";
  var NS = window.LoadoutStudio = window.LoadoutStudio || {};
  var ICONS = {"home": "M3 10 L10 3 L17 10 M5 8.5 V17 H15 V8.5", "sessions": "M3 4.5 H17 V15.5 H3 Z M6 8.5 L8.5 10.5 L6 12.5 M10 12.5 H14", "ideas": "M10 2 L15 10 L10 18 L5 10 Z", "teams": "M3.5 7a2 2 0 1 0 4 0a2 2 0 1 0-4 0 M12.5 7a2 2 0 1 0 4 0a2 2 0 1 0-4 0 M8 14.5a2 2 0 1 0 4 0a2 2 0 1 0-4 0 M7 8.5 L9 12.5 M13 8.5 L11 12.5", "events": "M6 12.5 V8.5 A4 4 0 0 1 14 8.5 V12.5 L15.5 14.5 H4.5 Z M8.5 16.5 A1.5 1.5 0 0 0 11.5 16.5", "memory": "M4 4.5 H16 M4 8.5 H16 M4 12.5 H16 M4 16.5 H11", "context": "M10 3 L17 7 L10 11 L3 7 Z M3 10.5 L10 14.5 L17 10.5 M3 14 L10 18 L17 14", "spend": "M10 2 V18 M14 6 C13 4 7 4 7 7 C7 10 13 9.5 13 13 C13 16 7 16.5 6 14", "projects": "M3 5.5 H8 L10 7.5 H17 V16 H3 Z", "commands": "M4 6 L9 10 L4 14 M11 14.5 H16", "search": "M8.5 3a5.5 5.5 0 1 0 0 11a5.5 5.5 0 1 0 0-11 M12.6 12.6 L17 17", "settings": "M7 10a3 3 0 1 0 6 0a3 3 0 1 0-6 0 M10 2 V5 M10 15 V18 M2 10 H5 M15 10 H18 M4.3 4.3 L6.4 6.4 M13.6 13.6 L15.7 15.7 M15.7 4.3 L13.6 6.4 M6.4 13.6 L4.3 15.7"};
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

  /* SideRail: icons with live counts; widens over the page with a dim on hover or focus; below 600px a Pages tab slides it in.
     Going somewhere from the slid-in list puts it away. foot is what sits at the foot of the rail, such as the Appearance row. */
  function SideRail(o) {
    o = o || {};
    var root = h("div", { "class": "ls-nav" + (o.narrow ? " narrow" : "") });
    function item(p, inSlide) {
      var cur = p.key === o.current;
      return h("a", { href: p.href || "#" + p.key, "class": "ls-rl" + (p.need ? " need" : ""), "aria-current": cur ? "page" : null, onclick: function () { if (inSlide) { put(); } } },
        [icon(ICONS[p.key] || ICONS.home), h("span", { "class": "n", text: p.name }), h("span", { "class": "f", text: p.fact || "" }), h("span", { "class": "needdot", "aria-hidden": "true", text: "!" })]);
    }
    function search(inSlide) {
      return h("button", { type: "button", "class": "ls-rl search", "aria-keyshortcuts": "Control+K Meta+K", onclick: function () { if (inSlide) { put(); } if (o.onSearch) { o.onSearch(); } } },
        [icon(ICONS.search), h("span", { "class": "n", text: "Search" }), h("span", { "class": "f", text: "Ctrl K · go to or do" })]);
    }
    /* A function is called once for the rail and once for the slid-in list; a node can only sit in the rail. */
    function foot(inSlide) { var f = typeof o.foot === "function" ? o.foot(!!inSlide) : inSlide ? null : o.foot; return f ? (f.el || f) : null; }
    var pages = o.pages || [];
    var nav = h("nav", { "class": "ls-railnav", "aria-label": o.label || "Studio" }, [h("div", { "class": "mark" }, [h("i", { "aria-hidden": "true" }), h("b", { text: "LOADOUT" })]), search()].concat(pages.map(function (p) { return item(p); }), [foot()]));
    if (o.contained) { nav.style.position = "absolute"; }
    var tabId = "ls-pages-" + Math.random().toString(36).slice(2, 7);
    var tab = h("button", { type: "button", id: tabId, "class": "ls-pagestab", "aria-expanded": "false", text: "Pages" });
    var layer = null;
    /* Put away without moving focus: what was pressed takes it (the page, or the palette). */
    function put() { if (!layer) { return; } layer.forEach(function (n) { n.remove(); }); layer = null; tab.setAttribute("aria-expanded", "false"); }
    function close() { if (!layer) { return; } put(); tab.focus(); }
    function open() {
      var scrim = h("div", { "class": "ls-slidescrim", onclick: close });
      var closeBtn = h("button", { type: "button", "class": "close", onclick: close }, [h("span", { "aria-hidden": "true", text: "×" }), "Close"]);
      var slide = h("nav", { "class": "ls-slide", "aria-label": o.label || "Studio" }, [closeBtn, search(true)].concat(pages.map(function (p) { return item(p, true); }), [foot(true)]));
      slide.addEventListener("keydown", function (e) { if (e.key === "Escape") { e.stopPropagation(); close(); } });
      root.appendChild(scrim); root.appendChild(slide); layer = [scrim, slide];
      tab.setAttribute("aria-expanded", "true"); setTimeout(function () { closeBtn.focus(); }, 30);
    }
    tab.addEventListener("click", function () { if (layer) { close(); } else { open(); } });
    root.appendChild(nav); root.appendChild(tab);
    return root;
  }

  /* Dialog: a projected panel over a dim. Focus is held inside; Escape and the scrim close it; focus returns to what opened it.
     Confirmations wait, then close: pressing an action makes it busy (its busy word, "working…" by default) and the other buttons
     inactive, and waits for what its onPress returns. false, a throw or a rejection keeps the dialog open with "✕ <why>" in an alert
     line inside, every field as typed; anything else closes it, unless the action says stay: true. Nothing closes it while it works. */
  function Dialog(o) {
    o = o || {};
    var id = "ls-dlg-" + Math.random().toString(36).slice(2, 7), back = null, scrim, panel, fail = null, btns = [], working = false, host = o.host || document.body;
    function close() {
      if (!panel) { return; } scrim.remove(); panel.remove(); panel = null; working = false;
      if (o.behind) { o.behind.classList.remove("is-back"); }
      if (back && back.focus) { back.focus(); } if (o.onClose) { o.onClose(); }
    }
    /* Escape, the dim and Cancel: not while an action is working. */
    function leave() { if (!working) { close(); } }
    function why(e) { var m = e && e.message !== undefined ? e.message : e; m = m === undefined || m === null || m === "" ? "It did not go through." : String(m); return m.replace(/^✕\s*/, ""); }
    function buttonOf(b) { return b.matches && b.matches("button") ? b : b.querySelector("button"); }
    function press(a, i) {
      if (working || !panel) { return; }
      fail.textContent = "";
      var r;
      try { r = a.onPress ? a.onPress() : undefined; } catch (e) { settle(a, false, e); return; }
      if (!r || typeof r.then !== "function") { settle(a, r !== false, a.failed); return; }
      working = true;
      btns.forEach(function (b, j) {
        var btn = buttonOf(b);
        if (j === i) { NS.HaloButton.setBusy(b, a.busy || true); }
        else if (btn) { btn.__lsDlgWas = btn.getAttribute("aria-disabled"); btn.setAttribute("aria-disabled", "true"); }
      });
      r.then(function (v) { settle(a, v !== false, a.failed, true); }, function (e) { settle(a, false, e, true); });
    }
    function settle(a, ok, e, waited) {
      if (!panel) { return; }
      working = false;
      if (waited) {
        btns.forEach(function (b) {
          var btn = buttonOf(b);
          NS.HaloButton.setBusy(b, false);
          if (btn && "__lsDlgWas" in btn) { if (btn.__lsDlgWas) { btn.setAttribute("aria-disabled", btn.__lsDlgWas); } else { btn.removeAttribute("aria-disabled"); } delete btn.__lsDlgWas; }
        });
      }
      if (!ok) { fail.textContent = "✕ " + why(e); return; }
      if (!a.stay) { close(); }
    }
    function open() {
      back = document.activeElement;
      scrim = h("div", { "class": "ls-scrim", onclick: leave });
      btns = (o.actions || []).map(function (a, i) { return NS.HaloButton(Object.assign({}, a, { busy: false, onPress: function () { press(a, i); } })); });
      if (btns.length) { btns.push(NS.HaloButton({ label: o.cancelLabel || "Cancel", size: "small", onPress: leave })); }
      fail = h("p", { "class": "fail", role: "alert" });
      panel = h("div", { "class": "ls-dialog" + (o.danger ? " danger" : "") + (full() ? " anim" : " fade") + (o.extraClass ? " " + o.extraClass : ""), role: o.role || "dialog", "aria-modal": "true", "aria-labelledby": o.title ? id : null, "aria-label": o.title ? null : (o.label || null) },
        [o.title ? h("h2", { id: id, text: o.title }) : null, o.why ? h("p", { "class": "why", text: o.why }) : null, o.content || null, fail,
         btns.length ? h("div", { "class": "acts" }, btns) : null]);
      if (o.contained) { scrim.style.position = "absolute"; panel.style.position = "absolute"; }
      host.appendChild(scrim); host.appendChild(panel);
      if (o.behind) { o.behind.classList.add("ls-behind", "is-back"); }
      trap(panel, leave);
      setTimeout(function () { if (!panel) { return; } var list = focusables(panel), f = o.initialFocus ? panel.querySelector(o.initialFocus) : (o.danger ? list[list.length - 1] : list[0]); if (f) { f.focus(); } }, 60);
      return panel;
    }
    return { open: open, close: close, get panel() { return panel; }, get working() { return working; } };
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

  /* CommandPalette: Ctrl or Cmd K. Go to a page, find work in flight, or do something; every action says its command.
     Given search, what is typed is also looked for everywhere once it is two characters long and typing has paused, and what that finds
     follows the palette's own rows. Given ask, a leading '?', or the Ask Loadout row, turns it into a conversation with Loadout (an AskPane)
     instead, which Enter asks; the conversation is kept when the palette closes, and focus comes back to the box once a plan is approved. */
  function matches(q, text) { if (!q) { return true; } var t = text.toLowerCase(); return q.toLowerCase().split(/\s+/).filter(Boolean).every(function (w) { return t.indexOf(w) >= 0; }); }
  function isAsk(typed) { return /^\s*\?/.test(typed || ""); }
  function askRequest(typed) { return String(typed || "").replace(/^\s+/, "").replace(/^\?+/, "").trim(); }
  function CommandPalette(o) {
    o = o || {};
    var dlg = null, q = "", active = 0, flat = [], input, list, askWrap, foot, id = "ls-pal-" + Math.random().toString(36).slice(2, 7);
    var ask = o.ask || null, conv = ask ? ask.conversation || null : null, found = [], timer = null, asked = 0;
    var placeholder = o.placeholder || (ask ? "Go to a page, find work, or do something, or ? to ask" : "Go to a page, find work, or do something");
    function asking() { return !!ask && isAsk(q); }
    /* The conversation: the one given, or one the palette keeps for as long as it lives. Bound to this palette's box each time it opens. */
    function pane() {
      if (!conv && NS.AskPane) { conv = NS.AskPane({ turns: [], asking: false }); }
      if (conv) {
        conv.set({ onrewrite: function (rewrite) { askNow(rewrite); }, approve: ask.approve, onclear: function () { conv.clear(); },
          onagent: function (text) { if (ask.agent) { conv.ask(text, function (query) { return ask.agent(query, conv.agentSession || null); }); } },
          onapproved: function () { if (input) { input.focus(); } } });
      }
      return conv;
    }
    function rows() {
      var level = function (r) { return !q ? 1 : (matches(q, r.label) ? 2 : (matches(q, r.label + " " + (r.sub || "") + " " + (r.kind || "")) ? 1 : 0)); };
      var groups = (o.groups || []).map(function (g) {
        var items = g.items.map(function (r) { return Object.assign({ lvl: level(r) }, r); }).filter(function (r) { return r.lvl > 0; }).sort(function (a, b) { return b.lvl - a.lvl; });
        return { name: g.name, items: items, best: items.length ? items[0].lvl : 0 };
      }).filter(function (g) { return g.items.length; });
      if (q) { groups.sort(function (a, b) { return b.best - a.best; }); }
      /* Asking is offered after the palette's own rows, so Enter on a page or an action still does what it always did. */
      if (ask && q.trim().length >= 2) { groups.push({ name: "Ask Loadout", items: [{ key: "ask-loadout", glyph: "?", label: "Ask Loadout: “" + q.trim() + "”", sub: "answered from Loadout's own records", stay: true, run: function () { askNow(q.trim()); } }] }); }
      return groups.concat(found.filter(function (g) { return g.items && g.items.length; }));
    }
    function paintFoot() {
      foot.textContent = "";
      (asking() ? ["Enter to ask", "Esc to close, and the conversation is kept"] : ["↑ ↓ to move", "Enter to go or do"].concat(ask ? ["? to ask Loadout"] : [], ["Esc to close"])).forEach(function (t) { foot.appendChild(h("span", { text: t })); });
    }
    function paint() {
      var now = asking();
      input.setAttribute("aria-expanded", now ? "false" : "true");
      askWrap.hidden = !now; list.hidden = now; paintFoot();
      if (now) { var p = pane(); if (p && p.el.parentNode !== askWrap) { askWrap.textContent = ""; askWrap.appendChild(p.el); } }
      var groups = now ? [] : rows(); flat = []; list.textContent = "";
      if (now) { input.removeAttribute("aria-activedescendant"); lit(null); return; }
      if (!groups.length) { list.appendChild(h("p", { "class": "ls-pempty", text: "Nothing matches “" + q + "”. Try a page name, a project, or a word from a session." })); input.removeAttribute("aria-activedescendant"); lit(null); return; }
      active = Math.min(active, 0 + Math.max(0, groups.reduce(function (n, g) { return n + g.items.length; }, 0) - 1));
      groups.forEach(function (g, gi) {
        var gid = id + "-g" + gi;
        var grp = h("div", { "class": "ls-pgroup", role: "group", "aria-labelledby": gid }, [h("h3", { id: gid, text: g.name })]);
        g.items.forEach(function (r) {
          var i = flat.length; flat.push(r); r.__group = g.name;
          grp.appendChild(h("div", { role: "option", id: id + "-o" + i, "data-key": r.key || null, "class": "ls-popt" + (r.need ? " need" : ""), "aria-selected": i === active ? "true" : "false", tabindex: "-1",
            onclick: function () { run(r); }, onmousemove: function () { if (active !== i) { active = i; paint(); } } },
            [h("span", { "class": "g", "aria-hidden": "true", text: r.glyph || "→" }), h("span", { "class": "t", text: r.label }), h("span", { "class": "s" + (/^\$ loadout /.test(r.sub || "") ? " is-cmd" : ""), text: r.sub || "" }), h("span", { "class": "k", text: r.kind || "" })]));
        });
        list.appendChild(grp);
      });
      input.setAttribute("aria-activedescendant", id + "-o" + active);
      var a = document.getElementById(id + "-o" + active); if (a && a.scrollIntoView) { a.scrollIntoView({ block: "nearest" }); }
      lit(flat[active] || null, a);
    }
    /* onHighlight(item, row, group): the highlighted row as it moves (arrows, pointer, typing), and null when nothing is, so a page can preview it beside the palette. Called only when it changes. */
    var litNow;
    function lit(r, row) {
      if (r === litNow) { return; }
      litNow = r;
      if (o.onHighlight) { o.onHighlight(r, row || null, r ? r.__group : null); }
    }
    /* Looked for everywhere once typing pauses; an answer to an older query is dropped. */
    function look() {
      var typed = q.trim(), mine = ++asked;
      clearTimeout(timer);
      if (!o.search || typed.length < 2 || isAsk(typed)) { found = []; return; }
      timer = setTimeout(function () { Promise.resolve(o.search(typed)).then(function (groups) { if (mine === asked && dlg) { found = groups || []; paint(); } }, function () {}); }, 250);
    }
    /* Asks Loadout and leaves the box ready for the next request. */
    function askNow(typed) {
      var wanted = askRequest(typed);
      q = "? "; if (input) { input.value = q; input.focus(); }
      paint();
      var p = pane();
      if (p && wanted) { p.ask(wanted, ask.asker); }
    }
    function run(r) { if (!r.stay) { close(); } if (r.run) { r.run(r); } }
    function close() { if (dlg) { dlg.close(); dlg = null; } clearTimeout(timer); lit(null); }
    function open() {
      if (dlg) { return; }
      q = ""; active = 0; found = [];
      input = h("input", { name: "palette", role: "combobox", "aria-expanded": "true", "aria-controls": id + "-list", "aria-autocomplete": "list", "aria-label": placeholder, placeholder: placeholder, autocomplete: "off" });
      list = h("div", { id: id + "-list", "class": "ls-plist", role: "listbox", "aria-label": "Results" });
      askWrap = h("div", { "class": "ls-plist", hidden: true });
      input.addEventListener("input", function () { q = input.value; active = 0; look(); paint(); });
      input.addEventListener("keydown", function (e) {
        var n = flat.length, d = { ArrowDown: 1, ArrowUp: -1 }[e.key];
        if (e.key === "Enter" && asking()) { e.preventDefault(); if (!(conv && conv.asking)) { askNow(q); } return; }
        if (e.key === "Enter") { e.preventDefault(); if (flat[active]) { run(flat[active]); } return; }
        if (e.key === "Home" && e.ctrlKey) { e.preventDefault(); active = 0; paint(); return; }
        if (d && n) { e.preventDefault(); active = (active + d + n) % n; paint(); }
      });
      var field = h("div", { "class": "ls-start" }, [h("div", { "class": "field" }, [input, h("span", { "class": "ls-kbd", "aria-hidden": "true", text: "Esc" })])]);
      foot = h("div", { "class": "ls-pfoot", "aria-hidden": "true" });
      var content = h("div", {}, [field, askWrap, list, foot]);
      dlg = Dialog({ label: "Command palette", content: content, extraClass: "pal", host: o.host, contained: o.contained, behind: o.behind, onClose: function () { dlg = null; clearTimeout(timer); lit(null); }, initialFocus: "input" });
      dlg.open(); paint();
    }
    function onKey(e) { if ((e.ctrlKey || e.metaKey) && (e.key === "k" || e.key === "K")) { e.preventDefault(); if (dlg) { close(); } else { open(); } } }
    if (o.bindKeys !== false) { window.addEventListener("keydown", onKey); }
    return { open: open, close: close, unbind: function () { window.removeEventListener("keydown", onKey); }, get conversation() { return conv; } };
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
  var lastTilt = "";
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
      keepPhase();
    }
    /* A page redraws the halo whenever its work changes. Each spin is timed from one clock, not from when it was drawn, so a
       redrawn halo carries on where the last one was instead of jumping back to the start. */
    function keepPhase() {
      var now = Date.now();
      Array.prototype.forEach.call(wrap.querySelectorAll(".spin, .gspin, .orbiters"), function (n) {
        var d = parseFloat(n.style.animationDuration) || (n.classList.contains("spin") ? 40 : n.classList.contains("orbiters") ? 150 : 60);
        n.style.animationDelay = "-" + ((now % (d * 1000)) / 1000).toFixed(3) + "s";
      });
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
        wrap.onmousemove = function (e) { if (!full()) { return; } var b = wrap.getBoundingClientRect(), px = (e.clientX - b.left) / b.width - 0.5, py = (e.clientY - b.top) / b.height - 0.5; inner.style.transform = lastTilt = "rotateX(" + (-py * 18).toFixed(1) + "deg) rotateY(" + (px * 26).toFixed(1) + "deg)"; };
        wrap.onmouseleave = function () { inner.style.transform = lastTilt = ""; };
        /* Redrawn under the pointer, it keeps the tilt it had rather than snapping flat. */
        if (lastTilt) { inner.style.transition = "none"; inner.style.transform = lastTilt; requestAnimationFrame(function () { inner.style.transition = ""; }); }
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
    /* Quiet on purpose: no ask, so no badge, sound or count. It turns into waiting for you by itself if the work goes silent past its time. */
    background: { word: "waiting on background work", typing: true },
    ended: { word: "ended", typing: false, ask: ["good", "Ended. Its transcript is kept.", null, "resume"] },
    error: { word: "exited with an error", typing: false, ask: ["bad", "Exited with an error (exit code {code}). Its transcript is kept.", null, "resume"] },
    reconnecting: { word: "reconnecting", typing: false, ask: ["rest", "Lost touch with the daemon. Reconnecting; the output so far is kept.", "Try now"] },
    watch: { word: "only watching", typing: false, ask: ["rest", "Only watching: typing is off. Start Studio without --watch-only to type into a session."] },
    lost: { word: "lost", typing: false, ask: ["bad", "This session ended when the daemon restarted. Its transcript is kept.", null, "resume"] }
  };

  /* The line that carries a session on in a terminal: the project is named, since 'loadout resume' reads the folder it is typed in; every project is searched where it is not known. */
  function resumeCommand(agentSession, project) {
    var where = project ? "--project " + project : "--all";
    return agentSession ? "$ loadout resume " + agentSession + " " + where : "$ loadout resume --last " + where;
  }

  /* Terminal: the agent's own screen, in Studio's palette. The real one is xterm.js themed from the term-* tokens; this is its reference look. */
  function Terminal(o) {
    o = o || {};
    var state = o.state || "working", cols = (o.size || [80, 24])[0], rows = (o.size || [80, 24])[1], size = 13, fit = !!o.fit, typed = "", refused = null;
    var screen = h("pre", { "class": "screen", tabindex: "0", role: "log", "aria-label": "Terminal: " + (o.title || "session"), "aria-roledescription": "terminal" });
    var cursorLine = h("span"), cur = h("span", { "class": "cur", text: " " });
    var sig = h("span"), sizeEl = h("span", { "class": "size", text: cols + " × " + rows });
    var fitBtn = h("button", { type: "button", "class": "tbtn", "aria-pressed": fit ? "true" : "false", text: "Fit", title: "Fit 80 columns to the width", onclick: function () { setFit(!fit); } });
    var smaller = h("button", { type: "button", "class": "tbtn", "aria-label": "Smaller text", text: "A−", onclick: function () { zoom(-1); } });
    var larger = h("button", { type: "button", "class": "tbtn", "aria-label": "Larger text", text: "A+", onclick: function () { zoom(1); } });
    var grow = h("span", { "class": "grow" });
    var bar = h("div", { "class": "tbar" }, [sig, h("span", { text: [o.project, o.agent].filter(Boolean).join(" · ") }), sizeEl, h("span", { "class": "keyhint", text: "Ctrl+] leaves the terminal" }), h("span", { "class": "keyhint", text: "Ctrl+V paste · Alt+V image · right-click copy or paste" }), grow, smaller, larger, fitBtn]);
    var askText = h("p"), askBtnWrap = h("span"), askCmd = h("span", { style: "display: contents" });
    var ask = h("div", { "class": "ask", role: "status" }, [askText, askBtnWrap, askCmd]);
    var footEl = h("div", { "class": "foot" });
    var el = h("section", { "class": "ls-term" + (fit ? " fit" : ""), "aria-label": o.label || "Live terminal" }, [bar, screen, ask]);
    screen.appendChild(cursorLine); cursorLine.appendChild(cur);
    /* foot: a row under the screen, as main's Terminal has for the session's StatusStrip. A node, or anything with .el; null takes it away. */
    function setFoot(node) {
      footEl.textContent = "";
      var n = node && node.el ? node.el : node;
      if (n) { footEl.appendChild(n); if (!footEl.parentNode) { el.appendChild(footEl); } } else if (footEl.parentNode) { footEl.remove(); }
    }
    /* Said on the bar when the browser will not hand over or take the clipboard, so a copy or paste that did nothing says why. */
    function clipboard(call) {
      return Promise.resolve().then(function () { if (!navigator.clipboard) { throw new Error("no clipboard"); } return call(navigator.clipboard); })
        .then(function (r) { setRefused(false); return r; }, function () { setRefused(true); return undefined; });
    }
    function setRefused(on) {
      if (on && !refused) { refused = h("span", { role: "alert", text: "The browser refused the clipboard" }); bar.insertBefore(refused, grow); }
      else if (!on && refused) { refused.remove(); refused = null; }
    }
    function selectionHere() { var sel = window.getSelection && window.getSelection(); return sel && !sel.isCollapsed && screen.contains(sel.anchorNode) ? sel : null; }
    /* Copies what is selected and clears it, as Windows Terminal does. */
    function copySelection(sel) { var text = sel.toString(); sel.removeAllRanges(); clipboard(function (b) { return b.writeText(text); }); }
    function paste() {
      if (!STATES[state].typing) { return; }
      clipboard(function (b) { return b.readText(); }).then(function (text) { if (text) { typed += text.split("\n")[0]; paintPrompt(); screen.scrollTop = screen.scrollHeight; } });
    }

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
        if (st.ask[2] && NS.HaloButton) { askBtnWrap.appendChild(NS.HaloButton({ label: st.ask[2], size: "small", kind: st.ask[0] === "needs" ? "needs" : "", cmd: s === "reconnecting" ? "reconnects · changes nothing" : "puts the keyboard in the terminal", onPress: function () { if (s === "reconnecting") { if (o.onRetry) { o.onRetry(); } } else { screen.focus(); screen.scrollTop = screen.scrollHeight; } } })); }
        askCmd.textContent = "";
        if (st.ask[3] === "resume") { var line = resumeCommand(o.sessionId, o.project); askCmd.appendChild(NS.Command ? NS.Command({ why: "Carry it on in a terminal:", command: line }) : h("code", { "class": "ls-cmdline", text: "Carry it on in a terminal: " + line })); }
      } else { ask.hidden = true; }
      paintPrompt();
    }
    screen.addEventListener("keydown", function (e) {
      if (e.ctrlKey && !e.altKey && !e.metaKey && (e.key === "]" || e.code === "BracketRight")) {
        e.preventDefault();
        if (o.leaveTo) { o.leaveTo.focus(); return; }
        /* Where nothing was named, the page around says where the keyboard goes (the tab in a deck); else the next control on the page. */
        var asked = new CustomEvent("leaveterminal", { bubbles: true, cancelable: true });
        if (!el.dispatchEvent(asked)) { return; }
        var list = focusables(document.body), i = list.indexOf(screen); var next = list[i + 1] || list[0]; if (next) { next.focus(); } return;
      }
      var plainCtrl = e.ctrlKey && !e.altKey && !e.metaKey;
      /* Ctrl+C copies a selection; with nothing selected it is the interrupt. */
      if (plainCtrl && !e.shiftKey && (e.key === "c" || e.code === "KeyC")) { var sel = selectionHere(); if (sel) { e.preventDefault(); copySelection(sel); return; } }
      /* Ctrl+V and Ctrl+Shift+V paste; Alt+V is left alone for Claude Code's image paste. */
      if (plainCtrl && (e.key === "v" || e.key === "V" || e.code === "KeyV")) { e.preventDefault(); paste(); return; }
      if (!STATES[state].typing) { return; }
      if (e.key === "Tab") { e.preventDefault(); return; }
      if (e.key === "Enter") { e.preventDefault(); var t = typed; typed = ""; write([["› ", "fg-cyan"], [t, ""]]); paintPrompt(); if (o.onInput) { o.onInput(t); } return; }
      if (e.key === "Backspace") { e.preventDefault(); typed = typed.slice(0, -1); paintPrompt(); return; }
      if (e.key.length === 1 && !e.ctrlKey && !e.metaKey) { e.preventDefault(); typed += e.key; paintPrompt(); screen.scrollTop = screen.scrollHeight; }
    });
    /* Right-click copies a selection, otherwise pastes, in place of the browser's own menu for a web page. */
    screen.addEventListener("contextmenu", function (e) { e.preventDefault(); var sel = selectionHere(); if (sel) { copySelection(sel); } else { paste(); } });
    (o.lines || []).forEach(write);
    setState(state, o.info);
    if (o.foot) { setFoot(o.foot); }
    return { el: el, write: write, setState: setState, setFoot: setFoot, focus: function () { screen.focus(); }, get state() { return state; } };
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
    /* Actions: the commands that carry it on elsewhere, a note, then the buttons, as main's session page lays them. */
    var cmds = (o.commands || []).map(function (c) { return NS.Command ? NS.Command(c) : h("code", { "class": "ls-cmdline", text: (c.why ? c.why + " " : "") + c.command }); });
    var btns = (o.actions || []).length && NS.HaloButton ? h("div", { "class": "ls-answers", style: "margin: 0" }, o.actions.map(function (a) { return NS.HaloButton(Object.assign({ size: "small" }, a)); })) : null;
    var actKids = cmds.concat([o.note ? h("p", { "class": "calm small", text: o.note }) : null, btns]).filter(Boolean);
    var acts = actKids.length ? (actKids.length === 1 ? actKids[0] : h("div", { "class": "acts" }, actKids)) : null;
    /* Extra sections: a node, anything with .el (a StatusBlock), or { title, content }; each at "facts" (after Facts, where main puts the StatusBlock; the default), "loaded", "events" or "actions" (after Actions). */
    function extra(x) {
      if (!x) { return null; }
      if (x.nodeType) { return x; }
      if (x.el && !x.title) { return x.el; }
      var c = x.content || x.el;
      return block(x.title, Array.isArray(c) ? h("div", { "class": "acts" }, c) : c);
    }
    var at = { facts: [], loaded: [], events: [], actions: [] };
    var statusBlock = o.status && NS.StatusBlock ? NS.StatusBlock({ status: o.status, now: o.now }) : null;
    if (statusBlock) { at.facts.push(statusBlock.el); }
    (o.sections || []).forEach(function (x) { var n = extra(x); if (n) { (at[x && x.at] || at.facts).push(n); } });
    [block("Facts", facts)].concat(at.facts, [block("This session loaded", loaded)], at.loaded, [block("Last events", events)], at.events, [block("Actions", acts)], at.actions).forEach(function (b) { if (b) { side.appendChild(b); } });
    var open = o.details !== false && !(window.matchMedia && window.matchMedia("(max-width: 959px)").matches);
    var toggle = h("button", { type: "button", "class": "ls-halo small", "aria-expanded": open ? "true" : "false", "aria-controls": side.id, onclick: function () { open = !open; side.hidden = !open; toggle.setAttribute("aria-expanded", open ? "true" : "false"); toggle.lastChild.textContent = open ? "Hide details" : "Details"; } },
      [h("span", { "class": "ring", "aria-hidden": "true" }), open ? "Hide details" : "Details"]);
    side.hidden = !open;
    /* Ctrl+] leaves the terminal for the details toggle, never for the next action, which may be End the session. */
    if (!termOpts.leaveTo) { termOpts.leaveTo = toggle; }
    var head = h("div", { "class": "shead" }, [h("div", { "class": "who" }, [h("h1", { text: l.title || "" }), h("span", { "class": "facts1", text: [l.where, l.agent, l.mode, l.time].filter(Boolean).join(" · ") })]), toggle]);
    /* A button in the header, before Details: main's "Type here instead" when another window holds the input. HaloButton options or a node; null takes it away. */
    var headBtn = null;
    function setButton(b) {
      if (headBtn) { headBtn.remove(); headBtn = null; }
      if (!b) { return; }
      headBtn = b.nodeType ? b : (b.el && b.el.nodeType ? b.el : (NS.HaloButton ? NS.HaloButton(Object.assign({ size: "small" }, b)) : null));
      if (headBtn) { head.insertBefore(headBtn, toggle); }
    }
    setButton(o.button);
    var root = h("div", { "class": "ls-session" }, [head, term.el, side]);
    return { el: root, terminal: term, side: side, head: head, statusBlock: statusBlock, setButton: setButton };
  }

  Object.assign(NS, { Terminal: Terminal, SessionView: SessionView, TERMINAL_STATES: Object.keys(STATES), resumeCommand: resumeCommand });
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
  /* What the daemon can say of the process driving a run, as main's livenessOf words it; live, finished or unknown say nothing. */
  function livenessOf(liveness, pid) {
    var p = pid ? " · pid " + pid : "";
    return { "not-responding": "not responding" + p, "possibly-not-responding": "possibly not responding", unreadable: "owner unreadable", silent: "silent for an hour or more" + p }[liveness] || null;
  }
  function RunCard(o) {
    o = o || {}; var r = o.run || {};
    var fam = NS.signalOf(r.word || "running").family;
    var badge = o.badge !== undefined ? o.badge : (o.liveness ? livenessOf(o.liveness, o.pid) : null);
    var el = h("article", { "class": "ls-run " + fam, "aria-label": r.goal });
    el.appendChild(h("div", { "class": "top" }, [o.href ? h("a", { "class": "goal", href: o.href, text: r.goal }) : h("span", { "class": "goal", text: r.goal }), sig(r.word || "running"), badge ? h("span", { "class": "ls-liveness", role: "status", text: badge }) : null]));
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
    stalled: ["stalled", "No done-when has moved for {detail}, so it is held after this round.", "you let it carry on, tell it something, or stop it"],
    /* The trigger rules' reasons, as main's NEED_REASONS has them. */
    approval: ["needs approval", "A trigger rule wants to start a run that writes.", "you approve or reject it"],
    paused: ["paused", "It reached one of its caps and starts nothing until you resume it.", "you resume it"],
    stale: ["held too long", "It held events past the 24-hour window.", "you release them, or they are dropped after a week"],
    source: ["stopped", "A trigger source has stopped.", "what stopped it is put right"]
  };
  /* Live work first: a question or an approval waits on you; the lead's quiet rounds, a quiet node and a projected overrun are on a
     run still going; a spent budget, a stall, a paused rule, held events and a stopped source are for you to pick up. Oldest first in each. */
  var NEED_GROUP = { asked: "waiting", approval: "waiting", silent: "running", quiet: "running", budget: "running", spent: "pickup", stalled: "pickup", paused: "pickup", stale: "pickup", source: "pickup" };
  function NeedsYou(o) {
    o = o || {};
    var items = NS.liveFirst(o.items || [], { group: function (it) { return it.gate ? "waiting" : NEED_GROUP[it.reason] || "waiting"; } }), hid = uid("ls-nh-");
    var el = h("section", { "class": "ls-needs", "aria-labelledby": hid }, [h("h2", { "class": "ls-zone", id: hid }, [h("span", { "class": "no", text: String(items.length) }), "Needs you"])]);
    if (!items.length) { el.appendChild(h("p", { "class": "calm", text: "Nothing needs you. Each reason clears itself, so this only ever holds what is still true." })); return el; }
    el.appendChild(h("ul", {}, items.map(function (it) {
      var n = NEED[it.reason] || NEED.asked;
      var words = (it.text || n[1]).replace("{node}", it.node || "a node").replace("{for}", it.quiet || "a while").replace("{detail}", it.detail || "");
      return h("li", { "class": "need" }, [
        h("div", { "class": "top" }, [h("a", { href: it.href || "#teams", text: it.goal }), sig(n[0])]),
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
  /* RunDepths draws its journal with main's Journal (stage 14's NS.Journal), which takes lines as objects; the
     older [time, text, family, kind, sub, repeats] arrays are still taken. */
  function journalOf(lines, label) {
    var J = NS.Journal;
    if (!J || J === Journal) { return Journal(lines); }
    var made = J({ label: label || "Journal", lines: lines.map(function (l) { return Array.isArray(l) ? { time: l[0] || "", text: l[1] || "", family: l[2] || undefined, kind: l[3] || undefined, sub: !!l[4], repeats: l[5] || undefined } : l; }) });
    return made.el || made;
  }
  function Exchanges(rows) {
    var H = ["Round", "Node", "Model", "Messages", "Refused", "Cost", "Report"];
    return h("table", { "class": "ls-xch" }, [h("caption", { text: "Every exchange" }), h("thead", {}, [h("tr", {}, H.map(function (x, i) { return h("th", { scope: "col", "class": i >= 3 && i <= 5 ? "r" : null, text: x }); }))]),
      h("tbody", {}, rows.map(function (r) { return h("tr", {}, r.map(function (c, i) { return h("td", { "data-h": H[i], "class": (i >= 3 && i <= 5 ? "r" : "") + (i === 6 ? "rep" : "") || null }, [h("span", { text: i === 5 ? money(c) : String(c) })]); })); }))]);
  }
  function Papers(p) {
    return h("div", { "class": "ls-papers" }, [p.summary ? h("p", { "class": "sum", text: p.summary }) : null, (p.pairs || []).length ? null : h("p", { "class": "ls-note", text: "No brief has gone out yet." })].concat((p.pairs || []).map(function (x) {
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
        if (!node) { panel.appendChild(journalOf(s.journal || [])); panel.appendChild(h("p", { "class": "src" }, ["One line every few seconds, repeats counted. ", cmd("$ loadout team log " + (o.run || "<run>") + " --events")])); }
        else {
          var stream = (s.streams || {})[node] || [];
          panel.appendChild(journalOf(stream.length ? stream : [{ time: "", text: "Nothing from " + node + " yet." }], "What " + node + " did"));
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
        /* A node whose diff could not be read says why, in its place, as main does. */
        ps.forEach(function (p, k) { var x = p.error ? h("p", { "class": "ls-note", text: p.node + ": " + p.error }) : Patch(Object.assign({ run: o.run, goal: o.goal }, p)); if (k) { x.style.marginTop = "22px"; } panel.appendChild(x); });
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
      actions: [{ label: "Push and open it", kind: "needs", solid: true, cmd: "runs the command above", busy: "pushing\u2026", onPress: function () { return prSay(o, said, { draft: draft, title: title, dry: false }); } },
        { label: "Say what it would do", cmd: "the same, with --dry-run · pushes nothing", busy: "checking\u2026", onPress: function () { return prSay(o, said, { draft: draft, title: title, dry: true }); } }] }).open();
  }
  /* As main: onPr is asked both ways, dry or not, and what it answers (or resolves to) is what is said; with no answer, the words main falls back on. */
  function prSay(o, said, p) {
    var answer = o.onPr ? o.onPr(p) : null;
    function show(a) { said.textContent = a === null || a === undefined || a === "" ? (p.dry ? "\u2713 Dry run: it would push " + (o.branch || "the branch") + " and open the pull request. Nothing was pushed." : "\u2713 Pushed " + (o.branch || "the branch") + " and opened a " + (p.draft ? "draft " : "") + "pull request.") : String(a); }
    return Promise.resolve(answer).then(function (a) { if (a === false) { throw new Error("It did not go through."); } show(a); }, function (e) { said.textContent = "\u2715 " + ((e && e.message) || e); throw e; });
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
    function numeric(x) { x.setAttribute("inputmode", "numeric"); return x; }
    function team() { return teams.filter(function (t) { return t.name === V.team; })[0] || null; }
    function proj() { return projects.filter(function (x) { return x.name === V.project; })[0] || null; }
    function tasksHere() { return (o.tasks || []).filter(function (t) { return (t.project || V.project) === V.project && (t.state === "doing" || t.state === "open" || t.state === "blocked") && t.kind !== "idea"; }); }
    /* The words after the command, as main's sheetWords has them; the line quotes each as a shell would need. */
    function words() {
      if (kind === "make") { return [V.name || "<name>"].concat(V.from ? ["--from", V.from] : [], V.forProject ? ["--for-this-project", "--project", V.project] : []); }
      if (kind === "schedule") { return [V.name || "<name>", V.team || "<team>", V.goal.trim() || "…", "--project", V.project].concat(V.when === "at" ? ["--at", V.at] : V.when === "on" ? ["--on", "commit"] : ["--every", V.every.trim() || "24h"], V.autonomy ? ["--autonomy", V.autonomy] : [], V.usd ? ["--usd", V.usd] : []); }
      var w = [V.team || "<team>", V.goal.trim() || "…", "--project", V.project].concat(V.task ? ["--task", V.task] : []);
      if (V.autonomy) { w.push("--autonomy", V.autonomy); }
      if (V.rounds) { w.push("--rounds", V.rounds); }
      if (V.usd) { w.push("--usd", V.usd); }
      V.done.split("\n").map(function (s) { return s.trim(); }).filter(Boolean).forEach(function (d) { w.push("--done-when", d); });
      /* Each one left out is named, even all of them, as main's start route takes them. */
      ((o.projectDoneWhen || {})[V.project] || []).filter(function (x) { return V.dropped[x]; }).forEach(function (x) { w.push("--drop-project-done-when", x); });
      if (V.after) { w.push("--take-recommendation-after", V.after); if (V.timeout !== "think-again-once") { w.push("--on-timeout", V.timeout); } }
      return w;
    }
    function command() { return "$ " + { run: "loadout team run", make: "loadout team new", schedule: "loadout team schedule add" }[kind] + " " + words().map(q).join(" "); }
    function problem() {
      if (kind === "make") { return !V.name ? "give it a name first" : (!/^[a-z0-9]+(-[a-z0-9]+)*$/.test(V.name) ? "lowercase words joined by hyphens, as the built-in ones are" : (teams.some(function (t) { return t.name === V.name; }) ? "a team called " + V.name + " already exists" : null)); }
      if (kind === "schedule" && !V.name) { return "give the schedule a name first"; }
      var t = team();
      if (!t) { return "pick a team first"; }
      if (t.template) { return t.name + " is a shape to copy, not a team to run"; }
      if ((t.problems || []).length) { return "it can’t run here: " + t.problems[0]; }
      if (!V.goal.trim()) { return "say what the run is for first"; }
      if (!V.project) { return "pick a project first"; }
      if (kind === "schedule" && V.autonomy === "manual") { return "a schedule cannot be manual: nobody is there to answer when it fires"; }
      if (kind === "schedule" && V.when === "every" && !/^\d+\s*(m|h|d)$/.test(V.every.trim())) { return "say how often as 30m, 2h or 1d"; }
      if (kind === "schedule" && V.when === "every" && /^\d+\s*m$/.test(V.every.trim()) && parseInt(V.every, 10) < 5) { return "nothing may repeat faster than every five minutes"; }
      if (V.usd && !/^(none|\d+(\.\d+)?)$/i.test(V.usd.trim())) { return "a budget is a figure in US dollars, or none"; }
      if (V.usd && /^0+(\.0+)?$/.test(V.usd.trim())) { return "a budget of nothing is refused; say none for no cap"; }
      if (V.rounds && !/^\d+$/.test(V.rounds.trim())) { return "rounds is a whole number"; }
      if (V.after && !/^(\d+\s*(m|h|d|mins?|minutes?|hours?|days?)|never|off)$/i.test(V.after.trim())) { return "write the wait as 30m, 2h or 1d"; }
      return null;
    }
    var sending = false;
    function paintFoot() {
      line.textContent = command(); foot.textContent = "";
      var bad = problem(), go = { run: "Start the run", make: "Write it", schedule: "Schedule it" }[kind];
      foot.appendChild(halo({ label: go, solid: true, glyph: "▶", disabledReason: bad || (o.watchOnly ? "Studio is only watching: type the command in a terminal" : sending ? "sending…" : null), cmd: bad ? null : "runs the command above", onPress: submit }));
      if (dlg) { foot.appendChild(halo({ label: "Close", size: "small", onPress: function () { dlg.close(); } })); }
    }
    function submit() {
      var c = command();
      if (kind === "run") {
        NS.Dialog({ title: "Start " + V.team + " on “" + V.goal.trim() + "”?", why: "It spends money and edits " + V.project + ". Nothing starts until you say so.", actions: [{ label: "Start it", kind: "needs", solid: true, cmd: c, onPress: function () { finish(c); } }] }).open();
      } else { finish(c); }
    }
    /* As main: onSubmit says what to say (or resolves to it), or throws (or rejects) with why it was refused. The sheet stays open to say it. */
    function finish(c) {
      if (sending) { return; }
      var plain = kind === "run" ? "Started. Its page opens as soon as the run is listed." : kind === "make" ? "Written. $ loadout team edit " + V.name + " --path-only says where." : o.daemon === false ? "Scheduled. It will not fire until the daemon is running." : "Scheduled. The daemon fires it.";
      function ok(a) { sending = false; said.className = "ls-said"; said.textContent = "✓ " + (typeof a === "string" && a ? a : plain); paintFoot(); }
      function no(e) { sending = false; said.className = "ls-said no"; said.textContent = "✕ " + ((e && e.message) || e); paintFoot(); }
      var r;
      try { r = o.onSubmit ? o.onSubmit(c, Object.assign({}, V)) : undefined; } catch (e) { no(e); return; }
      if (r && typeof r.then === "function") { sending = true; paintFoot(); r.then(ok, no); } else { ok(r); }
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
      el.appendChild(h("div", { "class": "two" }, [field("Budget, in US dollars", text({ value: V.usd, placeholder: (team() || {}).budget ? "the team's " + team().budget : "a figure, or none", onInput: function (v) { V.usd = v.trim(); paintFoot(); } }), "Or none, for no cap on purpose."), field("Rounds at most", numeric(text({ value: V.rounds, placeholder: "no limit", onInput: function (v) { V.rounds = v.trim(); paintFoot(); } })), "The budget is the cap that binds.")]));
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
    var d = o.daemon || {}, gone = [];
    el.appendChild(h("p", { "class": "ls-note" + (d.running ? "" : " needs") }, [d.running ? (d.paused ? "The daemon is held: no schedule fires until it is resumed. " : "The daemon is running and fires these. ") : "The daemon is not running, so none of these will fire. ", d.running ? (d.paused ? cmd("$ loadout daemon resume") : null) : cmd("$ loadout daemon")]));
    var body = h("div", { style: "display: contents" });
    el.appendChild(body);
    /* As main: held says why it will not fire, after the rest; a removal answered false (or resolving false) keeps the row. */
    function paint() {
      body.textContent = "";
      var shown = (o.items || []).filter(function (s) { return gone.indexOf(s.name) < 0; });
      if (!shown.length) { body.appendChild(h("p", { "class": "ls-note", text: "Nothing is scheduled on this machine." })); return; }
      body.appendChild(h("ul", { "aria-label": "Runs on a schedule" }, shown.map(function (s) {
        return h("li", {}, [h("div", { "class": "t" }, [h("b", { text: s.name }), h("span", { text: s.team + " · " + s.goal }), h("em", { text: [s.when, s.next ? "next " + s.next : null, s.autonomy, s.usd ? "$" + s.usd + " a run" : null, s.held].filter(Boolean).join(" · ") })]),
          halo({ label: "Stop it", size: "small", cmd: "$ loadout team schedule remove " + s.name, disabledReason: o.disabledReason || null, onPress: function () {
            var r = o.onRemove ? o.onRemove(s.name) : undefined;
            function after(v) { if (v !== false) { gone.push(s.name); paint(); } }
            if (r && typeof r.then === "function") { r.then(after); } else { after(r); }
          } })]);
      })));
    }
    paint();
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
    var list = h("ul", { "aria-label": "In the bin" }), said = h("p", { "class": "ls-said", role: "status" }), emptyActs = h("div", { "class": "ls-answers", style: "margin: 0" });
    function restoreCmd(e) { return e.kind === "run" ? "$ loadout team runs restore " + e.name : "$ loadout team restore " + e.name; }
    /* As main: each callback may answer false (or resolve to false) where it was not done, and nothing changes. */
    function then(r, done) { if (r && typeof r.then === "function") { r.then(done); } else { done(r); } }
    function paint() {
      list.textContent = "";
      if (!entries.length) { list.appendChild(h("li", {}, [h("span"), h("span", { "class": "ls-note", style: "margin: 0", text: "The bin is empty." }), h("span")])); }
      entries.forEach(function (e) {
        list.appendChild(h("li", {}, [h("span", { "class": "kind", text: e.kind }), h("span", { "class": "t" }, [h("b", { text: e.name }), e.what ? h("span", { text: e.what }) : null, h("span", { "class": e.left <= 3 ? "soon" : null, text: "removed " + e.removed + " · " + e.left + (e.left === 1 ? " day" : " days") + " left" + (e.size ? " · " + e.size : "") })]),
          halo({ label: "Put it back", size: "small", cmd: restoreCmd(e), disabledReason: o.disabledReason || null, onPress: function () { then(o.onRestore ? o.onRestore(e) : undefined, function (v) { if (v === false) { return; } entries = entries.filter(function (x) { return x !== e; }); said.textContent = "✓ Put back: " + e.name + "."; paint(); }); } })]));
      });
      emptyActs.textContent = "";
      emptyActs.appendChild(halo({ label: "Empty it for good", kind: "danger", size: "small", cmd: "$ loadout team bin empty --yes", disabledReason: o.disabledReason || (entries.length ? null : "the bin is already empty"), onPress: function () {
        NS.Dialog({ danger: true, title: "Empty the bin for good?", why: "This is the one thing here with no way back. It deletes:", content: h("ul", { "class": "binlist" }, entries.map(function (e) { return h("li", { text: e.kind + " " + e.name }); })),
          actions: [{ label: "Empty it", kind: "danger", cmd: "$ loadout team bin empty --yes", busy: "emptying\u2026", onPress: function () { return Promise.resolve(o.onEmpty ? o.onEmpty() : undefined).then(function (v) { if (v === false) { return false; } entries = []; said.textContent = "✓ The bin is empty."; paint(); }); } }] }).open(); } }));
    }
    paint();
    el.appendChild(h("p", { "class": "ls-note", style: "margin: 0", text: "Removed runs and teams wait here for " + (o.keptDays || 30) + " days, then go for good. A run's branches were never touched, so they are not in here." }));
    el.appendChild(list); el.appendChild(said); el.appendChild(emptyActs);
    /* Clear out runs: the same as team runs prune, said as a sentence before it goes. */
    var P = { outcome: "", older: "30d", keep: "20" };
    var sentence = h("p", { "class": "sentence" }), pacts = h("div", { "class": "ls-answers", style: "margin: 0" });
    function pcmd(dry) { return "$ loadout team runs prune" + (P.outcome === "failed" ? " --failed" : P.outcome ? " --outcome " + P.outcome : "") + (P.older ? " --older-than " + P.older : "") + (P.keep ? " --keep " + P.keep : "") + (dry ? " --dry-run" : " --yes"); }
    /* As main's onprune(prune, dry): a dry run asks onPrune too, when it takes the second argument, and says what it answers (what would move);
       the real one says what it answers, or that they are in the bin. A one-argument onPrune(line) is only called for the real one, as before. */
    function clear(dry) {
      var line = pcmd(dry), asks = o.onPrune && (!dry || o.onPrune.length >= 2);
      var r = asks ? o.onPrune(line, dry, { outcome: P.outcome, olderThan: P.older, keep: P.keep }) : undefined;
      return Promise.resolve(r).then(function (a) { if (a === false) { said.textContent = "\u2715 It did not go through. Nothing moved."; return false; } said.textContent = typeof a === "string" && a ? "✓ " + a : dry ? "✓ Dry run: it lists what it would take and why it keeps the rest. Nothing moved." : "✓ Cleared out. They are in the bin."; });
    }
    function ppaint() {
      var none = !P.outcome && !P.older && !P.keep;
      sentence.textContent = none ? "Say which runs: an ending, an age or how many to keep." : "Moves " + (P.outcome ? "the " + P.outcome + " runs" : "the runs") + (P.older ? " older than " + P.older : "") + " to the bin" + (P.keep ? ", never the newest " + P.keep : "") + ", and never one still going or one that left a branch nothing merged.";
      pacts.textContent = "";
      pacts.appendChild(halo({ label: "Say which first", size: "small", cmd: pcmd(true), disabledReason: none ? "say which runs first" : null, onPress: function () { clear(true); } }));
      pacts.appendChild(halo({ label: "Clear them out", size: "small", cmd: pcmd(false), disabledReason: o.disabledReason || (none ? "say which runs first" : null), onPress: function () { NS.Dialog({ title: "Clear out these runs?", why: sentence.textContent + " They wait in the bin for " + (o.keptDays || 30) + " days.", actions: [{ label: "Clear them out", kind: "needs", cmd: pcmd(false), busy: "clearing\u2026", onPress: function () { return clear(false); } }] }).open(); } }));
    }
    /* Main's endings since 0.104: abandoned, and stalled (a lead given up on). */
    var ENDS = ["any ending", "failed", "stopped", "abandoned", "limited", "blocked", "done", "needs-decision", "unrecorded", "stalled"];
    var keep = text({ value: P.keep, placeholder: "20", onInput: function (v) { P.keep = v.trim(); ppaint(); } }); keep.setAttribute("inputmode", "numeric");
    el.appendChild(h("details", {}, [h("summary", { text: "Clear out runs" }), h("div", { "class": "prune" }, [h("div", { "class": "two" }, [
      NS.Picker({ label: "Ending", options: ENDS.map(function (e) { return { name: e }; }), onPick: function (x, i) { P.outcome = i ? x.name : ""; ppaint(); } }),
      field("Older than", text({ value: P.older, placeholder: "30d", onInput: function (v) { P.older = v.trim(); ppaint(); } })),
      field("Keep the newest", keep)]), sentence, pacts])]));
    ppaint();
    return el;
  }

  Object.assign(NS, { Gate: Gate, RunCard: RunCard, Spend: Spend, NeedsYou: NeedsYou, RunShape: RunShape, Crew: Crew, DoneWhen: DoneWhen, RunNumbers: RunNumbers, RunDepths: RunDepths, Journal: Journal, Exchanges: Exchanges, Papers: Papers, Patch: Patch, SayToNode: SayToNode,
    TeamCard: TeamCard, TeamNodes: TeamNodes, TeamRules: TeamRules, RoleRecord: RoleRecord, TeamSheet: TeamSheet, Schedules: Schedules, Remedy: Remedy, Bin: Bin, shellQuote: q, livenessOf: livenessOf });
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
      var cp = NS.HaloButton({ label: "Copy path", size: "small", onPress: function () { var full = (o.repository ? o.repository.replace(/[\\/]$/, "") + "/" : "") + f.path; var shown = function (ok) { said.textContent = ok ? "\u2713 Copied " + full : "Select the path to copy it."; }; try { var pr = navigator.clipboard.writeText(full); if (pr && pr.then) { pr.then(function () { shown(true); }, function () { shown(false); }); } else { shown(true); } } catch (e) { shown(false); } } });
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
    /* What waits on you: the count and the oldest one's label. With list, pressing it opens a list of them all (the page draws it,
       from onWaiting): the button says it has a popup and whether it is open (setOpen). setWaiting changes the count in place, so
       an open list keeps its button, its place and what it said. */
    var w = o.waiting || [], open = false, slot = h("span", { "class": "ls-mb-wait" });
    var btn = h("button", { type: "button", "class": "waiting", "aria-haspopup": o.list ? "dialog" : null, "aria-expanded": o.list ? "false" : null, onclick: function () { if (o.onWaiting) { o.onWaiting(w[0], btn); } } });
    var calm = h("span", { "class": "calmword", text: "nothing waiting on you" });
    function paintWaiting() {
      btn.textContent = "";
      if (w.length) {
        btn.appendChild(NS.Signal("waiting for you"));
        btn.appendChild(h("b", {}, [String(w.length), h("span", { "class": "wn", text: " waiting on you" })]));
        btn.appendChild(h("span", { "class": "wl", text: "· " + w[0].label }));
        if (calm.parentNode) { calm.remove(); } if (btn.parentNode !== slot) { slot.appendChild(btn); }
      } else {
        if (btn.parentNode) { btn.remove(); } if (calm.parentNode !== slot) { slot.appendChild(calm); }
        open = false;
      }
      if (o.list) { btn.setAttribute("aria-expanded", open ? "true" : "false"); }
    }
    paintWaiting();
    kids.push(slot);
    return { el: h("div", { "class": "ls-modebar", role: "region", "aria-label": "Mode" }, kids), switcher: sw, waitingButton: btn,
      setWaiting: function (list) { w = list || []; paintWaiting(); },
      setOpen: function (on) { open = !!on && w.length > 0; if (o.list) { btn.setAttribute("aria-expanded", open ? "true" : "false"); } } };
  }

  /* Shell words as main's commands.ts quotes them: $, ` and \ escaped too, so a line copied out of the page runs as shown. An empty value is "". */
  function shellWord(v) { v = String(v); return v === "" || /[\s"'$`\\]/.test(v) ? "\"" + v.replace(/(["\\$`])/g, "\\$1") + "\"" : v; }
  /* Renaming a hosted session, or giving it its launch name back (null). */
  function renameCommand(id, name) { return "$ loadout session rename " + id + " " + (name === null ? "--reset" : shellWord(name)); }
  /* Ending a hosted session. */
  function endCommand(id) { return "$ loadout session end " + id + " --yes"; }
  /* The strip's order with one tab moved along it by a step, kept at either end; the same list where it cannot move. */
  function nudgeTab(shown, id, by) {
    var from = shown.indexOf(id), to = Math.max(0, Math.min(shown.length - 1, from + by));
    if (from < 0 || to === from) { return shown; }
    var without = shown.filter(function (x) { return x !== id; });
    return without.slice(0, to).concat([id], without.slice(to));
  }
  /* The strip's order with one tab put before another, or at the end where before is null. */
  function placeTab(shown, id, before) {
    var without = shown.filter(function (x) { return x !== id; }), at = before === null ? without.length : without.indexOf(before);
    return at < 0 ? shown : without.slice(0, at).concat([id], without.slice(at));
  }
  function idOf(s) { return s.id; }

  /* SessionTabs: every session as a tab, with its state, its context and whether it has written since you looked.
     Each tab can be closed, renamed and moved, as main's SessionTabs.svelte: closing an ended tab clears it at once; closing a running one asks
     whether to hide the tab or end the session, and ending asks again, naming what it ends. Rename and End are left out where Studio is only
     watching. Everything is reached by keyboard too: F2 renames, Delete closes, Ctrl+Shift+arrows move, and the context-menu key or Shift+F10
     opens the tab's menu. Each change is made here at once and told to its callback, so the strip works alone or under a page that keeps it. */
  function SessionTabs(o) {
    o = o || {};
    var copy = function (s) { return Object.assign({}, s); };
    var sessions = (o.sessions || []).map(copy), hidden = (o.hidden || []).map(copy), current = o.current === undefined ? null : o.current, changes = o.changes !== false;
    var hintId = "ls-stabs-" + Math.random().toString(36).slice(2, 7);
    var renaming = null, draft = "", menu = null, drag = null, swallow = false;
    var list = h("div", { role: "tablist", "aria-label": o.label || "Sessions" });
    var addBtn = o.onNew ? h("button", { type: "button", "class": "add", "aria-label": "New session", title: "New session", onclick: function () { o.onNew(); }, text: "+" }) : null;
    var hidBtn = null;
    var root = h("div", { "class": "ls-stabs" }, [list, addBtn]);
    function order() { return sessions.map(idOf); }
    function byId(id) { return sessions.filter(function (s) { return s.id === id; })[0] || hidden.filter(function (s) { return s.id === id; })[0]; }
    function hostOf(s) { return s.hid || s.id; }
    function tabOf(id) { return Array.prototype.filter.call(list.querySelectorAll('[role="tab"]'), function (b) { return b.getAttribute("data-id") === id; })[0]; }
    function focusTab(id) { setTimeout(function () { var b = tabOf(id); if (b) { b.focus(); } }, 0); }

    /* ---------- the changes, made here and told to the page ---------- */
    function doMove(next) {
      var ids = order();
      sessions = next.map(function (id) { return sessions[ids.indexOf(id)]; }).filter(Boolean);
      paint(); if (o.onMove) { o.onMove(next); }
    }
    function move(id, by) { var next = nudgeTab(order(), id, by); if (next !== order()) { doMove(next); focusTab(id); } }
    function doHide(id) { var s = byId(id); sessions = sessions.filter(function (x) { return x.id !== id; }); if (s && hidden.indexOf(s) < 0) { hidden.push(s); } paint(); if (o.onHide) { o.onHide(id); } }
    function doClear(id) { sessions = sessions.filter(function (x) { return x.id !== id; }); hidden = hidden.filter(function (x) { return x.id !== id; }); paint(); if (o.onClear) { o.onClear(id); } }
    function doUnhide(id) { var s = byId(id); hidden = hidden.filter(function (x) { return x.id !== id; }); if (s && sessions.indexOf(s) < 0) { sessions.push(s); } paint(); if (o.onUnhide) { o.onUnhide(id); } }
    function doRename(id, name) { var s = byId(id); if (s) { s.launchTitle = s.launchTitle || s.title; s.title = name === null ? s.launchTitle : name; } paint(); if (o.onRename) { o.onRename(id, name); } }
    function doEnd(id) { return o.onEnd ? o.onEnd(id) : undefined; }
    function pick(id) { setCurrent(id); if (o.onPick) { o.onPick(id); } }

    /* ---------- closing ---------- */
    function askEnd(id) {
      var s = byId(id); if (!s || !NS.Dialog) { return; }
      NS.Dialog({ danger: true, title: "End " + s.title + "?", why: "This stops its agent" + (s.project ? " in " + s.project : "") + ", and cannot be undone. The session stays listed, ended, so you can still see how it ended.",
        actions: [{ label: "End session", kind: "danger", solid: true, cmd: endCommand(hostOf(s)), busy: "ending\u2026", onPress: function () { return doEnd(id); } }] }).open();
    }
    function close(id) {
      var s = byId(id); if (!s) { return; }
      /* Nothing is running to lose: cleared from this browser at once. */
      if (s.ended) { doClear(id); return; }
      /* Only watching: hiding is the one thing to do, and it can be undone. */
      if (!changes || !NS.Dialog) { doHide(id); return; }
      NS.Dialog({ title: "Close " + s.title + "?", why: "It is still running. Hide takes the tab off this browser's strip and leaves the session running, still on the Sessions page. End session stops its agent.",
        actions: [{ label: "Hide", solid: true, cmd: "this browser only · changes nothing anywhere else", onPress: function () { doHide(id); } },
          { label: "End session…", kind: "danger", cmd: "asks once more", onPress: function () { setTimeout(function () { askEnd(id); }, 0); } }] }).open();
    }

    /* ---------- renaming ---------- */
    function startRename(id) {
      closeMenu(false);
      var s = byId(id); if (!s) { return; }
      renaming = id; draft = s.title || "";
      paint();
      var f = list.querySelector("input.rename"); if (f) { f.focus(); f.select(); }
      placeHint();
    }
    function finishRename(save) {
      var id = renaming; if (id === null) { return; }
      renaming = null;
      var name = draft.trim(), s = byId(id);
      /* Emptied, it goes back to the name it was launched as. */
      if (save && s && name !== s.title) { doRename(id, name === "" ? null : name); } else { paint(); }
      focusTab(id);
    }

    /* ---------- the menu: right-click, the context-menu key or Shift+F10 ---------- */
    function items(forId) {
      if (forId === null) { return hidden.map(function (s) { return { label: "Show " + s.title, run: function () { doUnhide(s.id); pick(s.id); focusTab(s.id); } }; }); }
      var s = byId(forId); if (!s) { return []; }
      var out = [], at = order().indexOf(s.id);
      if (changes) {
        out.push({ label: "Rename", hint: "F2", run: function () { startRename(s.id); } });
        if (s.launchTitle && s.title !== s.launchTitle) { out.push({ label: "Back to “" + s.launchTitle + "”", cmd: renameCommand(hostOf(s), null), run: function () { doRename(s.id, null); focusTab(s.id); } }); }
      }
      if (at > 0) { out.push({ label: "Move left", hint: "Ctrl Shift ←", run: function () { move(s.id, -1); } }); }
      if (at >= 0 && at < sessions.length - 1) { out.push({ label: "Move right", hint: "Ctrl Shift →", run: function () { move(s.id, 1); } }); }
      if (s.ended) { out.push({ label: "Clear", hint: "Delete", run: function () { doClear(s.id); } }); }
      else {
        out.push({ label: "Hide", run: function () { doHide(s.id); } });
        if (changes) { out.push({ label: "End session…", danger: true, run: function () { askEnd(s.id); } }); }
      }
      return out;
    }
    function away(e) { if (menu && !menu.el.contains(e.target)) { closeMenu(false); } }
    function closeMenu(back) {
      if (!menu) { return; }
      var was = menu; menu = null; was.el.remove(); document.removeEventListener("pointerdown", away, true);
      if (hidBtn) { hidBtn.setAttribute("aria-expanded", "false"); }
      if (back) { if (was.for) { focusTab(was.for); } else if (hidBtn) { hidBtn.focus(); } }
    }
    function openMenu(forId, from, at) {
      closeMenu(false);
      var its = items(forId); if (!its.length) { return; }
      var box = from && from.getBoundingClientRect(), x = at ? at.x : box ? box.left : 0, y = at ? at.y : box ? box.bottom : 0;
      var m = h("div", { "class": "ls-tabmenu", role: "menu", tabindex: "-1", "aria-label": forId === null ? "Hidden tabs" : "Tab " + (byId(forId) || {}).title });
      its.forEach(function (it) {
        m.appendChild(h("button", { type: "button", role: "menuitem", tabindex: "-1", "class": it.danger ? "danger" : null, title: it.cmd || null, onclick: function () { closeMenu(false); it.run(); } },
          [h("span", { text: it.label }), it.hint ? h("kbd", { text: it.hint }) : null]));
      });
      /* Kept inside the window, so a menu opened near an edge never scrolls the page sideways. */
      m.style.left = Math.round(Math.max(8, Math.min(x, window.innerWidth - 228))) + "px";
      m.style.top = Math.round(Math.max(8, Math.min(y, window.innerHeight - 38 * its.length - 20))) + "px";
      m.addEventListener("keydown", function (e) {
        var all = Array.prototype.slice.call(m.querySelectorAll('[role="menuitem"]')), i = all.indexOf(document.activeElement), d = { ArrowDown: 1, ArrowUp: -1 }[e.key];
        if (d) { e.preventDefault(); all[(i + d + all.length) % all.length].focus(); }
        else if (e.key === "Home" || e.key === "End") { e.preventDefault(); all[e.key === "Home" ? 0 : all.length - 1].focus(); }
        else if (e.key === "Escape") { e.preventDefault(); e.stopPropagation(); closeMenu(true); }
        else if (e.key === "Tab") { closeMenu(false); }
      });
      document.body.appendChild(m);
      menu = { for: forId, el: m };
      if (forId === null && hidBtn) { hidBtn.setAttribute("aria-expanded", "true"); }
      document.addEventListener("pointerdown", away, true);
      setTimeout(function () { var f = m.querySelector('[role="menuitem"]'); if (f) { f.focus(); } }, 0);
    }

    /* ---------- dragging a tab along the strip ---------- */
    function marks() {
      Array.prototype.forEach.call(list.querySelectorAll(".tab"), function (t, i, all) {
        var id = t.getAttribute("data-id"), moving = drag && drag.moved;
        t.classList.toggle("dragging", !!(moving && drag.id === id));
        t.classList.toggle("before", !!(moving && drag.before === id));
        t.classList.toggle("after", !!(moving && drag.before === null && i === all.length - 1));
      });
    }
    list.addEventListener("pointermove", function (e) {
      if (!drag) { return; }
      if (!drag.moved && Math.abs(e.clientX - drag.x) < 6) { return; }
      if (!drag.moved) { drag.moved = true; try { list.setPointerCapture(e.pointerId); } catch (x) { /* already released */ } }
      /* Before the first tab whose middle is past the pointer, or at the end. */
      var mine = drag.id, past = order().filter(function (id) { var b = tabOf(id), r = b && b.getBoundingClientRect(); return id !== mine && r && e.clientX < r.left + r.width / 2; })[0];
      drag.before = past === undefined ? null : past; marks();
    });
    function up() {
      var was = drag; drag = null;
      if (was && was.moved) {
        swallow = true; setTimeout(function () { swallow = false; }, 0);
        if (was.before !== undefined) { var next = placeTab(order(), was.id, was.before); if (next.join("\u0000") !== order().join("\u0000")) { doMove(next); return; } }
      }
      marks();
    }
    list.addEventListener("pointerup", up); list.addEventListener("pointercancel", up);

    function keys(e, i) {
      var s = sessions[i], n = sessions.length;
      if (e.ctrlKey && e.shiftKey && (e.key === "ArrowLeft" || e.key === "ArrowRight")) { e.preventDefault(); move(s.id, e.key === "ArrowRight" ? 1 : -1); return; }
      if (e.key === "F2" && changes) { e.preventDefault(); startRename(s.id); return; }
      if (e.key === "Delete") { e.preventDefault(); close(s.id); return; }
      if (e.key === "ContextMenu" || (e.shiftKey && e.key === "F10")) { e.preventDefault(); openMenu(s.id, e.currentTarget); return; }
      var to = e.key === "ArrowRight" ? (i + 1) % n : e.key === "ArrowLeft" ? (i - 1 + n) % n : e.key === "Home" ? 0 : e.key === "End" ? n - 1 : null;
      if (to === null) { return; }
      e.preventDefault();
      var b = tabOf(sessions[to].id); if (b) { b.focus(); }
      pick(sessions[to].id);
    }

    function tab(s, i) {
      var wrap = h("div", { "class": "tab", "data-id": s.id });
      if (renaming === s.id) {
        var inp = h("input", { "class": "rename", type: "text", maxlength: "80", autocomplete: "off", "aria-label": "New name for " + s.title, "aria-describedby": hintId });
        inp.value = draft;
        var cmd = h("code", { text: renameCommand(hostOf(s), draft.trim() === "" ? null : draft.trim()) });
        inp.addEventListener("input", function () { draft = inp.value; cmd.textContent = renameCommand(hostOf(s), draft.trim() === "" ? null : draft.trim()); });
        inp.addEventListener("keydown", function (e) { e.stopPropagation(); if (e.key === "Enter") { e.preventDefault(); finishRename(true); } else if (e.key === "Escape") { e.preventDefault(); finishRename(false); } });
        inp.addEventListener("blur", function () { setTimeout(function () { if (renaming === s.id && document.activeElement !== list.querySelector("input.rename")) { finishRename(true); } }, 0); });
        wrap.appendChild(inp);
        wrap.appendChild(h("span", { "class": "hint", id: hintId }, [
          "Enter saves, Escape cancels, empty goes back to “" + (s.launchTitle || s.title) + "”." + (s.kind === "launch" || s.kind === undefined ? (s.namesAgent ? " Its agent is given the name too." : " Only Loadout shows this name: its agent has not said it can take one.") : ""),
          cmd]));
        return wrap;
      }
      var fam = NS.signalOf(s.word).family, ctx = s.context !== null && s.context !== undefined && NS.ContextMark ? NS.ContextMark({ percent: s.context }).el : null;
      var b = h("button", { type: "button", role: "tab", "class": fam === "needs" ? "needs" : null, "aria-selected": s.id === current ? "true" : "false", tabindex: s.id === current ? "0" : "-1", "aria-controls": o.controls || null, "aria-keyshortcuts": i < 9 ? "Control+Alt+" + (i + 1) : null, "aria-haspopup": "menu", "data-id": s.id },
        [NS.Signal(s.word), h("span", { "class": "t", text: s.title }), h("span", { "class": "x", text: [s.where, s.agent].filter(Boolean).join(" · ") }), ctx,
          i < 9 ? h("span", { "class": "k", "aria-hidden": "true", text: "⌃⌥" + (i + 1) }) : null,
          s.unread ? h("span", { "class": "new", title: "New output since you looked" }, [h("span", { "class": "ls-vh", text: "new output" })]) : null]);
      b.addEventListener("click", function () { if (!swallow) { pick(s.id); } });
      b.addEventListener("dblclick", function () { if (o.onMaximise) { o.onMaximise(s.id); } });
      b.addEventListener("keydown", function (e) { keys(e, i); });
      b.addEventListener("pointerdown", function (e) { if (e.button === 0 && !renaming) { drag = { id: s.id, x: e.clientX, moved: false }; } });
      b.addEventListener("contextmenu", function (e) { e.preventDefault(); openMenu(s.id, b, { x: e.clientX, y: e.clientY }); });
      wrap.appendChild(b);
      wrap.appendChild(h("button", { type: "button", "class": "close", tabindex: "-1", "aria-label": "Close " + s.title, title: s.ended ? "Clear this tab" : "Hide this tab, or end the session", text: "×", onclick: function () { close(s.id); } }));
      return wrap;
    }
    function paint() {
      var a = document.activeElement, keep = null, caret = null;
      if (a && root.contains(a)) { keep = a.classList.contains("rename") ? "rename" : a.classList.contains("hid") ? "hid" : a.getAttribute("data-id"); if (keep === "rename") { caret = a.selectionStart; } }
      list.textContent = "";
      sessions.forEach(function (s, i) { list.appendChild(tab(s, i)); });
      if (hidBtn) { hidBtn.remove(); hidBtn = null; }
      if (hidden.length) {
        hidBtn = h("button", { type: "button", "class": "hid", "aria-haspopup": "menu", "aria-expanded": menu && menu.for === null ? "true" : "false", text: hidden.length + " hidden" });
        hidBtn.addEventListener("click", function () { if (menu && menu.for === null) { closeMenu(false); } else { openMenu(null, hidBtn); } });
        root.insertBefore(hidBtn, addBtn);
      }
      if (keep) {
        var n = keep === "rename" ? list.querySelector("input.rename") : keep === "hid" ? hidBtn : tabOf(keep);
        if (n) { n.focus({ preventScroll: true }); if (keep === "rename" && typeof caret === "number") { try { n.setSelectionRange(caret, caret); } catch (x) { /* not text */ } } }
      }
      if (renaming) { setTimeout(placeHint, 0); }
    }
    /* The hint sits under the field. The strip scrolls sideways, which would clip it, so it is placed against the window instead (main's hint is drawn inside the strip, where it is cut off). */
    function placeHint() {
      var f = list.querySelector("input.rename"), hint = f && f.parentNode.querySelector(".hint");
      if (!hint) { return; }
      var r = f.getBoundingClientRect();
      hint.style.position = "fixed"; hint.style.width = Math.max(r.width, 280) + "px";
      hint.style.left = Math.round(Math.max(8, Math.min(r.left, window.innerWidth - hint.offsetWidth - 8))) + "px";
      hint.style.top = Math.round(r.bottom) + "px";
    }
    function setCurrent(id) {
      current = id;
      Array.prototype.forEach.call(list.querySelectorAll('[role="tab"]'), function (b) { var on = b.getAttribute("data-id") === id; b.setAttribute("aria-selected", on ? "true" : "false"); b.tabIndex = on ? 0 : -1; });
    }
    paint();
    /* The page's word again: new sessions, hidden ones, the current tab, or whether changes are allowed. A rename in progress is kept. */
    root.update = function (p) {
      p = p || {};
      var full = false;
      if (p.sessions) { sessions = p.sessions.map(copy); full = true; }
      if (p.hidden) { hidden = p.hidden.map(copy); full = true; }
      if (p.changes !== undefined && (p.changes !== false) !== changes) { changes = p.changes !== false; full = true; }
      if (renaming && !byId(renaming)) { renaming = null; }
      if (p.current !== undefined) { current = p.current; }
      if (full) { paint(); } else if (p.current !== undefined) { setCurrent(current); }
    };
    root.rename = startRename;
    root.closeMenu = function () { closeMenu(false); };
    return root;
  }

  /* TermDeck: Terminal Focus, as main's TermDeck.svelte builds it. Sessions as tabs over one, two or four panes, each its own terminal.
     Ctrl+Alt+1-9 picks a session by its tab and Ctrl+Alt+arrows move between the panes shown. Panes keep their places: clicking into one only
     moves the highlight, and a session picked from its tab takes the highlighted pane. A session just started takes an empty pane instead,
     growing the layout 1→2→4 to make one, and with four full the pane of one that has ended or been idle longest; panes grown that way are
     given back as their sessions go, never below the layout you chose. Dividers between the panes are kept per layout. */
  var LAYOUTS = [[1, "One", "M2 2h16v12H2z"], [2, "Two side by side", "M2 2h16v12H2z M10 2v12"], [4, "Four", "M2 2h16v12H2z M10 2v12 M2 8h16"]];
  /* How small a pane holding a terminal may go, 40 columns by 8 rows at 13px, in pixels (main's splits.ts terminalFloor). */
  var TERM_FLOOR = { width: Math.ceil(40 * 13 * 0.6 + 24), height: Math.ceil(8 * 13 * 1.4 + 56) };
  function floorOf(px, length) { return length > 0 ? Math.min(0.5, px / length) : 0; }
  function clampSplit(v, min, max) { return min > max ? (min + max) / 2 : Math.min(max, Math.max(min, v)); }
  function tracksOf(first) { return "minmax(0, " + first.toFixed(4) + "fr) minmax(0, " + (1 - first).toFixed(4) + "fr)"; }
  /* The panes of a layout, each session in at most one, with nulls where a pane has nothing (main's lib/modes.ts, as are the three below). */
  function padSlots(slots, n) { var out = (slots || []).slice(0, n).map(function (id, i, all) { return id && all.indexOf(id) === i ? id : null; }); while (out.length < n) { out.push(null); } return out; }
  /* Which session each pane shows: a pick already in a pane changes nothing; one that is not takes the pane at `at`, else the first empty one. Only the first `fill` panes take spare tabs. */
  function placeSlots(slots, p) {
    var out = padSlots(slots, p.layout);
    if (p.pick && out.indexOf(p.pick) < 0) { var at = p.at === undefined ? -1 : p.at, empty = out.indexOf(null); out[at >= 0 ? Math.min(at, p.layout - 1) : Math.max(0, empty)] = p.pick; }
    var spare = p.order.filter(function (id) { return out.indexOf(id) < 0; }), fill = p.fill === undefined ? p.layout : p.fill;
    return out.map(function (id, i) { return id || (i < fill ? spare.shift() : null) || null; });
  }
  /* Where a session just started goes: an empty pane (a gone one leaves its pane); else a layout grown 1→2→4; else, with four full, the pane of one ended, or else the one idle longest that is not waiting on you. */
  function launchSlots(slots, p) {
    var gone = p.gone || [], ended = p.ended || [], waiting = p.waiting || [], active = p.active || {};
    var out = padSlots(slots, p.layout).map(function (id) { return id && gone.indexOf(id) >= 0 ? null : id; }), already = out.indexOf(p.pick);
    if (already >= 0) { return { slots: out, layout: p.layout, at: already, grew: false, replaced: null, because: null }; }
    var empty = out.indexOf(null);
    if (empty >= 0) { out[empty] = p.pick; return { slots: out, layout: p.layout, at: empty, grew: false, replaced: null, because: null }; }
    if (p.layout < 4) { var layout = p.layout === 1 ? 2 : 4, grown = padSlots(out, layout), g = grown.indexOf(null); grown[g] = p.pick; return { slots: grown, layout: layout, at: g, grew: true, replaced: null, because: null }; }
    var end = -1; out.forEach(function (id, i) { if (end < 0 && id && ended.indexOf(id) >= 0) { end = i; } });
    var at = end;
    if (at < 0) {
      var every = out.map(function (x, i) { return i; }), free = every.filter(function (i) { return waiting.indexOf(out[i]) < 0; }), pool = free.length ? free : every, quiet = function (i) { return active[out[i]] || 0; };
      at = pool.reduce(function (best, i) { return quiet(i) < quiet(best) ? i : best; }, pool[0]);
    }
    var replaced = out[at]; out[at] = p.pick;
    return { slots: out, layout: p.layout, at: at, grew: false, replaced: replaced, because: end >= 0 ? "ended" : "idle" };
  }
  /* Gives back panes a launch added once their sessions have gone: the smallest layout, never below the one chosen, that holds the sessions still listed. */
  function shrinkSlots(slots, p) {
    if (p.layout <= p.chosen) { return { slots: slots, layout: p.layout }; }
    var kept = padSlots(slots, p.layout).filter(function (id) { return id && p.order.indexOf(id) >= 0; }), layout = [1, 2, 4].filter(function (n) { return n >= p.chosen && n >= kept.length; })[0] || 4;
    return layout >= p.layout ? { slots: slots, layout: p.layout } : { slots: padSlots(kept, layout), layout: layout };
  }
  function TermDeck(o) {
    o = o || {};
    var copy = function (s) { return Object.assign({}, s); };
    var all = (o.sessions || []).map(copy), hidden = (o.hidden || []).map(copy), changes = o.changes !== false, listed = o.listed !== false, launch = o.launch || null, active = o.active || {};
    var chosen = [1, 2, 4].indexOf(o.layout) >= 0 ? o.layout : 1;
    var panes = (o.grown === 2 || o.grown === 4) && o.grown > chosen ? o.grown : chosen;
    var sp = o.splits || {}, split2 = (sp[2] && sp[2][0]) || 0.5, split4 = [(sp[4] && sp[4][0]) || 0.5, (sp[4] && sp[4][1]) || 0.5];
    var terms = {}, arranged = padSlots(o.slots || [], panes), hi = 0, note = "", noteTimer = null, lastSig = "", splitters = {};
    var panesEl = h("div", { "class": "panes l" + panes, id: "ls-deck-" + Math.random().toString(36).slice(2, 7) });
    var lay = h("div", { "class": "lay", role: "radiogroup", "aria-label": "Panes" }, LAYOUTS.map(function (L) {
      return h("button", { type: "button", role: "radio", "aria-checked": L[0] === panes ? "true" : "false", "aria-label": L[1], title: L[1], "data-n": String(L[0]), onclick: function () { setLayout(L[0]); } }, [icon(L[2], "0 0 20 16")]);
    }));
    var tabs = SessionTabs({ sessions: all, hidden: hidden, changes: changes, current: null, controls: panesEl.id, label: o.label,
      onPick: function (id) { pick(id, false); },
      onMaximise: function (id) { pick(id); setLayout(1); },
      onNew: o.onNew ? function () { o.onNew(); } : null,
      onMove: function (next) { var ids = order(); all = next.map(function (id) { return all[ids.indexOf(id)]; }).filter(Boolean); if (o.onMove) { o.onMove(next); } arrange(on()); paintBar2(); paintPanes(); },
      onRename: function (id, name) { all.forEach(function (s) { if (s.id === id) { s.launchTitle = s.launchTitle || s.title; s.title = name === null ? s.launchTitle : name; } }); if (o.onRename) { o.onRename(id, name); } },
      onHide: function (id) { var s = all.filter(function (x) { return x.id === id; })[0]; all = all.filter(function (x) { return x.id !== id; }); if (s) { hidden.push(s); } if (o.onHide) { o.onHide(id); } release(id); },
      onUnhide: function (id) { var s = hidden.filter(function (x) { return x.id === id; })[0]; hidden = hidden.filter(function (x) { return x.id !== id; }); if (s) { all.push(s); } if (o.onUnhide) { o.onUnhide(id); } },
      onClear: function (id) { all = all.filter(function (x) { return x.id !== id; }); hidden = hidden.filter(function (x) { return x.id !== id; }); if (o.onClear) { o.onClear(id); } release(id); },
      onEnd: function (id) { return o.onEnd ? o.onEnd(id) : undefined; } });
    var top = h("div", { "class": "dtop" }, [tabs, lay]);
    var waitHost = h("span", { style: "display: contents" }), noteEl = h("span", { "class": "note", role: "status" }), keysEl = h("span", { "class": "keys" });
    var findIn = h("input", { "class": "ls-text", type: "search", placeholder: "Find in this terminal", "aria-label": "Find in the focused terminal: Enter for the next, Shift Enter for the one before", autocomplete: "off" }), findOut = h("span", { role: "status" });
    var bar2 = h("div", { "class": "dtop" }, [waitHost, h("div", { "class": "find" }, [findIn, findOut]), noteEl, h("span", { "class": "grow", style: "flex: 1 1 auto" }), keysEl]);
    var el = h("section", { "class": "ls-deck", "aria-label": "Terminals" }, [top, bar2, panesEl]);
    var mq = window.matchMedia ? window.matchMedia("(max-width: 899px)") : null;

    function order() { return all.map(idOf); }
    function narrow() { return !!(mq && mq.matches); }
    function on() { return arranged[hi] || order()[0] || null; }
    /* Below 900px one pane, the highlighted one; the wider arrangement waits for the window to widen again. */
    function shown() { var c = on(); return narrow() ? (c ? [c] : []) : arranged.filter(Boolean); }
    function waiting() { return all.filter(function (s) { return NS.signalOf(s.word).family === "needs"; }); }
    /* Dividers only where there is more than one pane to share the room. */
    function split() { return !narrow() && panes > 1 && shown().length > 1; }
    function titleOf(id) { var s = all.filter(function (x) { return x.id === id; })[0] || hidden.filter(function (x) { return x.id === id; })[0]; return s ? s.title : id; }
    /* Puts a session in a pane if it is not in one, and highlights it. Spare tabs fill only the panes of the layout chosen, never with a launch still waiting for its own pane; panes a launch added are given back once their sessions have gone. */
    function arrange(p, from, ids) {
      ids = ids || order(); from = from || arranged;
      var spare = launch ? ids.filter(function (x) { return x !== launch; }) : ids;
      var placed = placeSlots(from, { order: spare, layout: panes, pick: p, at: hi, fill: chosen });
      var fitted = listed ? shrinkSlots(placed, { layout: panes, chosen: chosen, order: ids }) : { slots: placed, layout: panes };
      if (fitted.layout !== panes) { panes = fitted.layout; if (o.onGrown) { o.onGrown(panes); } }
      var next = fitted.slots, at = p ? next.indexOf(p) : -1;
      hi = at >= 0 ? at : Math.min(hi, panes - 1);
      if (next.length !== arranged.length || next.some(function (x, i) { return x !== arranged[i]; })) { arranged = next; if (o.onSlots) { o.onSlots(next.slice()); } }
    }
    function say(t) { note = t; noteEl.textContent = t; if (noteTimer) { clearTimeout(noteTimer); } noteTimer = setTimeout(function () { note = ""; noteEl.textContent = ""; }, 10000); }
    function focusSoon(id) { setTimeout(function () { if (terms[id] && terms[id].focus) { terms[id].focus(); } }, 0); }
    /* A session just started: a pane of its own, the keyboard, and a word on where it went. */
    function placeLaunch(id) {
      var ids = order();
      var p = launchSlots(arranged, { layout: panes, pick: id, ended: all.filter(function (s) { return s.ended; }).map(idOf), gone: arranged.filter(function (x) { return x && ids.indexOf(x) < 0; }), waiting: waiting().map(idOf), active: active });
      /* The layout first, so the arrangement is never cut to the old one. */
      if (p.layout !== panes) { panes = p.layout; if (o.onGrown) { o.onGrown(panes); } }
      arranged = p.slots; hi = p.at; launch = null;
      if (o.onSlots) { o.onSlots(p.slots.slice()); }
      if (o.onLaunched) { o.onLaunched(id); }
      if (o.onPick) { o.onPick(id); }
      var where = "Opened in pane " + (p.at + 1);
      say(p.grew ? where + ", added for it." : p.replaced ? where + ", in place of ‘" + titleOf(p.replaced) + "’, which " + (p.because === "ended" ? "had ended" : "had been idle longest") + ". It is still a tab." : where + ".");
      paint(); focusSoon(id);
    }
    /* A tab hidden or cleared off the strip gives up its pane, which the next tab along fills. One that ends or goes stays where it was. */
    function release(id) {
      var freed = arranged.map(function (x) { return x === id ? null : x; });
      arrange(freed[hi] || null, freed, order().filter(function (x) { return x !== id; }));
      paint();
    }
    function pick(id, focus) {
      arrange(id);
      if (o.onPick) { o.onPick(id); }
      paint();
      if (focus !== false) { focusSoon(id); }
    }
    function setLayout(n) {
      chosen = n;
      var apply = function () { panes = n; arrange(arranged[hi] || null); paint(); };
      if (NS.morph) { NS.morph(apply, { noVT: true, target: panesEl }); } else { apply(); }
      if (o.onLayout) { o.onLayout(n); }
    }
    function paintTop() {
      tabs.update({ sessions: all, hidden: hidden, changes: changes, current: on() });
      Array.prototype.forEach.call(lay.children, function (b) { b.setAttribute("aria-checked", b.getAttribute("data-n") === String(panes) ? "true" : "false"); });
    }
    function paintBar2() {
      waitHost.textContent = "";
      var w = waiting();
      if (w.length && NS.HaloButton) { waitHost.appendChild(NS.HaloButton({ label: "Go to the one waiting", kind: "needs", size: "small", onPress: function () { pick(w[0].id); } })); }
      keysEl.textContent = "Ctrl Alt 1–9 picks · Ctrl Alt arrows move · Ctrl ] leaves a terminal · F2 renames a tab · Ctrl Shift arrows move it" + (split() ? " · drag a divider, or arrow it; Enter puts it back" : "");
    }
    function limits() { return { col: floorOf(TERM_FLOOR.width, panesEl.clientWidth - 10), row: floorOf(TERM_FLOOR.height, panesEl.clientHeight - 10) }; }
    /* The tracks as main sets them: --deck-cols and --deck-rows on the panes, held to the floors as the window changes without moving what was kept. */
    function tracks() {
      var lim = limits(), col = clampSplit(panes === 4 ? split4[0] : split2, lim.col, 1 - lim.col), row = clampSplit(split4[1], lim.row, 1 - lim.row);
      if (split()) { panesEl.style.setProperty("--deck-cols", tracksOf(col)); } else { panesEl.style.removeProperty("--deck-cols"); }
      if (split() && panes === 4) { panesEl.style.setProperty("--deck-rows", tracksOf(row)); } else { panesEl.style.removeProperty("--deck-rows"); }
      return { col: col, row: row, lim: lim };
    }
    function setColumn(v) { if (panes === 4) { split4 = [v, split4[1]]; if (splitters.cross) { splitters.cross.set({ column: v }); } if (o.onSplit) { o.onSplit(4, split4.slice()); } } else { split2 = v; if (o.onSplit) { o.onSplit(2, [v]); } } tracks(); }
    function setRow(v) { split4 = [split4[0], v]; if (splitters.cross) { splitters.cross.set({ row: v }); } if (o.onSplit) { o.onSplit(4, split4.slice()); } tracks(); }
    function setBoth(c, r) { split4 = [c, r]; if (splitters.cols) { splitters.cols.set({ value: c }); } if (splitters.rows) { splitters.rows.set({ value: r }); } if (o.onSplit) { o.onSplit(4, split4.slice()); } tracks(); }
    function fitSplitters() {
      if (!splitters.cols) { tracks(); return; }
      var t = tracks();
      splitters.cols.set({ value: t.col, min: t.lim.col, max: 1 - t.lim.col });
      if (splitters.rows) { splitters.rows.set({ value: t.row, min: t.lim.row, max: 1 - t.lim.row }); }
      if (splitters.cross) { splitters.cross.set({ column: t.col, row: t.row, colMin: t.lim.col, colMax: 1 - t.lim.col, rowMin: t.lim.row, rowMax: 1 - t.lim.row }); }
    }
    function paintPanes() {
      var ids = shown(), cur = on(), have = order(), sig = ids.join("\u0000") + "#" + panes + "#" + narrow() + "#" + ids.map(function (id) { return listed && have.indexOf(id) < 0 ? 1 : 0; }).join("");
      if (sig === lastSig) { Array.prototype.forEach.call(panesEl.querySelectorAll(".pane"), function (p) { p.classList.toggle("on", p.getAttribute("data-id") === cur); }); return; }
      lastSig = sig; splitters = {};
      panesEl.className = "panes l" + (narrow() ? 1 : panes); panesEl.textContent = "";
      ids.forEach(function (id) {
        var pane = h("div", { "class": "pane" + (id === cur ? " on" : ""), "data-id": id });
        if (listed && have.indexOf(id) < 0) { pane.appendChild(h("p", { "class": "gone", role: "note", text: "This session is no longer in Studio. The pane keeps it until you pick another tab." })); }
        if (!terms[id] && o.terminal) { terms[id] = o.terminal(id) || null; }
        if (terms[id] && terms[id].el) { pane.appendChild(terms[id].el); }
        pane.addEventListener("focusin", function () { if (on() !== id) { hi = arranged.indexOf(id); if (o.onPick) { o.onPick(id); } paintTop(); paintPanes(); } });
        panesEl.appendChild(pane);
      });
      if (!ids.length) { panesEl.appendChild(h("p", { "class": "calm", text: "No sessions are running. Start one with the + beside the tabs, or N." })); }
      /* After the panes, so a pane's place among its siblings is still its place in the grid. */
      var t = tracks();
      if (split() && NS.Splitter) {
        splitters.cols = NS.Splitter({ "class": "cols", label: "Width of the panes", controls: panesEl.id, value: t.col, min: t.lim.col, max: 1 - t.lim.col, reset: 0.5, onchange: setColumn });
        panesEl.appendChild(splitters.cols.el);
        if (panes === 4) {
          splitters.rows = NS.Splitter({ "class": "rows", orientation: "horizontal", label: "Height of the panes", controls: panesEl.id, value: t.row, min: t.lim.row, max: 1 - t.lim.row, reset: 0.5, onchange: setRow });
          panesEl.appendChild(splitters.rows.el);
          /* Where they cross, over both, so a drag there moves the two together. */
          if (NS.SplitCross) { splitters.cross = NS.SplitCross({ column: t.col, row: t.row, colMin: t.lim.col, colMax: 1 - t.lim.col, rowMin: t.lim.row, rowMax: 1 - t.lim.row, reset: [0.5, 0.5], onchange: setBoth }); panesEl.appendChild(splitters.cross.el); }
        }
        /* The floors depend on the room the panes have, known once they are on the page. */
        setTimeout(fitSplitters, 0);
      }
      if (o.onShown) { o.onShown(ids, terms); }
    }
    function paint() { paintTop(); paintBar2(); paintPanes(); }
    /* Ctrl+Alt+1-9 picks a session by its tab; Ctrl+Alt+arrows move between the panes shown. Ctrl+Alt is AltGr on some layouts, so the digit is read from the physical key too. */
    el.addEventListener("keydown", function (e) {
      if (!(e.ctrlKey && e.altKey)) { return; }
      var m = /^Digit([1-9])$/.exec(e.code || ""), digit = m ? m[1] : (/^[1-9]$/.test(e.key) ? e.key : null), ids = order(), to = null;
      if (digit) { to = ids[Number(digit) - 1] || null; }
      else { var d = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key], s = shown(); if (d && s.length) { var i = Math.max(0, s.indexOf(on())); to = s[(i + d + s.length) % s.length]; } }
      if (to) { e.preventDefault(); e.stopPropagation(); pick(to); }
    }, true);
    /* Ctrl+] in a terminal with nowhere named goes back to its tab. */
    el.addEventListener("leaveterminal", function (e) { var b = tabs.querySelector('[role="tab"][aria-selected="true"]'); if (b) { e.preventDefault(); b.focus(); } });
    /* Find searches the terminal that has the keyboard: Enter for the next match, Shift Enter for the one before, Escape clears. A terminal with find() of its own is asked; the reference one is searched in place. */
    var hits = [], hit = -1;
    function clearMarks(scr) { Array.prototype.forEach.call(scr.querySelectorAll("mark"), function (mk) { mk.replaceWith(document.createTextNode(mk.textContent)); }); scr.normalize(); }
    function find(back) {
      var q = findIn.value.trim(), t = terms[on()];
      if (t && t.find) { findOut.textContent = q ? (t.find(q, back) ? "found" : "no match") : ""; if (!q && t.clearFind) { t.clearFind(); } return; }
      var pane = Array.prototype.filter.call(panesEl.querySelectorAll(".pane"), function (p) { return p.getAttribute("data-id") === on(); })[0], scr = pane && pane.querySelector(".screen");
      if (!scr) { findOut.textContent = q ? "no match" : ""; return; }
      clearMarks(scr); hits = [];
      if (!q) { findOut.textContent = ""; return; }
      var lower = q.toLowerCase(), walker = document.createTreeWalker(scr, NodeFilter.SHOW_TEXT), nodes = [];
      while (walker.nextNode()) { nodes.push(walker.currentNode); }
      nodes.forEach(function (tn) { var i = tn.nodeValue.toLowerCase().indexOf(lower); if (i < 0) { return; } var r = tn.splitText(i); r.splitText(lower.length); var mk = h("mark", { text: r.nodeValue }); r.replaceWith(mk); hits.push(mk); });
      hit = hits.length ? (back ? (hit - 1 + hits.length) % hits.length : (hit + 1) % hits.length) : -1;
      findOut.textContent = hits.length ? "found" : "no match";
      if (hit >= 0 && hits[hit].scrollIntoView) { hits[hit].scrollIntoView({ block: "nearest" }); }
    }
    findIn.addEventListener("input", function () { hit = -1; find(false); });
    findIn.addEventListener("keydown", function (e) { if (e.key === "Enter") { e.preventDefault(); find(e.shiftKey); } else if (e.key === "Escape") { findIn.value = ""; hit = -1; find(false); } });
    if (mq) { var redo = function () { lastSig = ""; paint(); }; if (mq.addEventListener) { mq.addEventListener("change", redo); } else if (mq.addListener) { mq.addListener(redo); } }
    if (window.ResizeObserver) { new ResizeObserver(function () { if (split()) { fitSplitters(); } }).observe(panesEl); }

    /* The first arrangement: the panes as they were, the session named, or a launch waiting for its pane. */
    hi = Math.max(0, arranged.indexOf(o.current));
    if (launch && order().indexOf(launch) >= 0) { arrange(null); placeLaunch(launch); }
    else { arrange(o.current && order().indexOf(o.current) >= 0 ? o.current : (arranged[hi] || null)); paint(); }
    return { el: el, tabs: tabs, pick: pick, setLayout: setLayout, terminal: function (id) { return terms[id]; },
      /* The daemon's list again (an array), or a patch: sessions, hidden, changes, listed, active, current (a session the page names, which then takes the highlighted pane) and launch (one just started, placed by the launch rule once it is among the tabs). */
      update: function (p) {
        if (Array.isArray(p)) { p = { sessions: p }; }
        p = p || {};
        if (p.sessions) { all = p.sessions.map(copy); }
        if (p.hidden) { hidden = p.hidden.map(copy); }
        if (p.changes !== undefined) { changes = p.changes !== false; }
        if (p.listed !== undefined) { listed = p.listed !== false; }
        if (p.active) { active = p.active; }
        if (p.launch !== undefined) { launch = p.launch || null; }
        var ids = order();
        if (launch && ids.indexOf(launch) >= 0) { placeLaunch(launch); return; }
        arrange(p.current && ids.indexOf(p.current) >= 0 ? p.current : (arranged[hi] || null));
        paint();
      },
      /* A session just started: placed now if it is among the tabs, else once an update brings it. */
      launch: function (id) { launch = id; if (order().indexOf(id) >= 0) { placeLaunch(id); } },
      get current() { return on(); }, get layout() { return panes; }, get chosen() { return chosen; }, get slots() { return arranged.slice(); } };
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
  /* Each row's rounds are drawn by main's MiniLine (stage 14's NS.MiniLine, which takes { rounds, end, running });
     the positional one above stands in where that is not loaded. */
  function boardLine(rounds, end, running) {
    var ML = NS.MiniLine;
    if (!ML || ML === MiniLine) { return MiniLine(rounds, end, running); }
    var made = ML({ rounds: rounds, end: end, running: running });
    return made.el || made;
  }
  function RunBoard(o) {
    o = o || {};
    var el = h("section", { "class": "ls-board", "aria-label": o.label || "Live team runs" });
    if ((o.teams || []).length) {
      el.appendChild(h("div", { "class": "teams", role: "group", "aria-label": "Teams" }, o.teams.map(function (t) {
        return h("button", { type: "button", "aria-pressed": o.team === t.name ? "true" : "false", onclick: function () { if (o.onTeam) { o.onTeam(t.name); } } }, [NS.Signal(t.word || "idle"), t.name, h("em", { text: t.fact || "" })]);
      })));
    }
    /* Live work first (liveFirst): what waits on you, then running, then what you might pick up, then the ended; oldest wait first. */
    var runs = NS.liveFirst(o.runs || [], { state: function (r) { return r.word; } });
    var ol = h("ol", { "aria-label": "Runs, the one that needs you first" });
    runs.forEach(function (r, i) {
      var f = NS.signalOf(r.word).family, live = f === "moving" || f === "needs" || r.word === "held after this round";
      var over = r.cap && r.projected > r.cap;
      var row = h("li", { "class": "brow " + f + (o.picked === r.id ? " picked" : ""), style: "--i:" + i }, [
        h("button", { type: "button", "class": "go", "aria-pressed": o.picked === r.id ? "true" : "false", onclick: function () { if (o.onPick) { o.onPick(r.id); } } }, [h("span", { "class": "t", text: r.goal }), h("span", { "class": "x", text: [r.team, r.project, r.round ? "round " + r.round : null].filter(Boolean).join(" · ") })]),
        boardLine(r.rounds || [], !live, live),
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
      var b = h("button", { type: "button", "class": "qrow", onclick: function (e) { if (o.onOpen) { o.onOpen(x, b.getBoundingClientRect()); } } }, [h("span", { "class": "t", text: x.title }), h("span", { "class": "x", text: [x.kind, x.where].filter(Boolean).join(" · ") }), NS.Signal(x.word)]);
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
    /* The dividers between the panes (main's WorkDesk split): split is [list, actions] as fractions of the width, kept by the
       page through onSplit; null for the stylesheet's widths. The list and the actions keep the floors they always had, the
       view a terminal's (40 columns by 8 rows at 13px). Until a divider is moved the panes are as the stylesheet draws them
       and the dividers say where that is; Enter or a double click gives a pane the width the stylesheet would. Below 1024px
       the panes stack and the dividers go. */
    var split = o.split && o.split.length === 2 ? o.split.slice() : null, wide = 0, SP1 = null, SP2 = null, ro = null;
    var VIEW_FLOOR = Math.ceil(40 * 13 * 0.6 + 24);
    function deskFloor(px) { return wide > 0 ? Math.min(0.5, px / wide) : 0; }
    function deskClamp(v, min, max) { return min > max ? (min + max) / 2 : Math.min(max, Math.max(min, v)); }
    function splitAt() {
      wide = el.getBoundingClientRect().width;
      return split || [wide ? listPane.getBoundingClientRect().width / wide : 0.25, wide ? actPane.getBoundingClientRect().width / wide : 0.2];
    }
    function paintCols() {
      if (split) { el.style.setProperty("--desk-cols", "minmax(250px, " + split[0].toFixed(4) + "fr) minmax(0, " + (1 - split[0] - split[1]).toFixed(4) + "fr) minmax(230px, " + split[1].toFixed(4) + "fr)"); } else { el.style.removeProperty("--desk-cols"); }
    }
    function syncSplit() {
      if (!SP1) { return; }
      var v = splitAt(), listMin = deskFloor(250), actsMin = deskFloor(230), viewMin = deskFloor(VIEW_FLOOR);
      SP1.set({ value: deskClamp(v[0], listMin, 1 - v[1] - viewMin), min: listMin, max: 1 - v[1] - viewMin, reset: deskFloor(320) });
      SP2.set({ value: deskClamp(1 - v[1], v[0] + viewMin, 1 - actsMin), min: v[0] + viewMin, max: 1 - actsMin, reset: 1 - deskFloor(280) });
    }
    function setSplit(next, quiet) {
      split = next && next.length === 2 ? next.slice() : null; paintCols(); syncSplit();
      if (!quiet) { (o.onSplit || o.onsplit || function () {})(split ? split.slice() : null); }
    }
    if (NS.Splitter) {
      SP1 = NS.Splitter({ "class": "first", label: "Width of the list", value: 0.25, reset: 0.25, onchange: function (to) { var v = splitAt(); setSplit([to, v[1]]); } });
      SP2 = NS.Splitter({ "class": "second", label: "Width of the actions", value: 0.8, reset: 0.8, onchange: function (b) { var v = splitAt(); setSplit([v[0], 1 - b]); } });
      /* After the panes, so the panes' own order is untouched. */
      el.appendChild(SP1.el); el.appendChild(SP2.el);
      if (typeof ResizeObserver === "function") { ro = new ResizeObserver(function () { syncSplit(); }); ro.observe(el); } else { setTimeout(syncSplit, 0); }
    }
    paintCols();
    if (!selected) { var first = all()[0]; selected = first ? first.id : null; }
    paintList(); paintView();
    return { el: el, select: select, focusList: focusList, openView: openView, closeView: closeView, refresh: function (g) { groups = g; if (!item(selected)) { var f = all()[0]; selected = f ? f.id : null; paintView(); } paintList(); }, repaintView: paintView, repaintActions: function () { paintActions(item(selected)); }, get selected() { return selected; },
      /** The widths the page keeps, as main's split prop: set them (null for the stylesheet's) without calling onSplit. */
      setSplit: function (s) { setSplit(s, true); }, get split() { return split ? split.slice() : null; },
      destroy: function () { document.removeEventListener("keydown", onKey); if (ro) { ro.disconnect(); } } };
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
    /* Copying waits for the clipboard: refused (no permission, no secure page), it says to select the line instead, and nothing is thrown. */
    function copy() {
      function no() { out.textContent = "Select the line to copy it."; }
      var wrote;
      try { wrote = navigator.clipboard.writeText((o.command || "").replace(/^\$ /, "")); } catch (e) { no(); return; }
      Promise.resolve(wrote).then(function () { said(out, "Copied. Paste it in a terminal."); }, no);
    }
    var el = h("div", { "class": "ls-tonly" }, [
      h("div", { "class": "top" }, [h("b", { text: o.title || "Done in a terminal" }), tag("terminal only", "", G.lock)]),
      o.why ? h("p", { "class": "why", text: o.why }) : null,
      h("div", { "class": "line" }, [h("code", { text: o.command || "" }), halo({ label: "Copy", size: "small", cmd: "copies the line \u00b7 runs nothing", onPress: copy })]),
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

  /* QuestionSet: the questions an agent asked about an idea, answered here: an option as a large chip, or your own words. Nothing is
     sent until one of the buttons at the foot is pressed; then each changed answer goes, and a refine after them when asked for.
     Answered questions fold to a line, and what you are looking at stays where it is. The rules are main's lib/ideas.ts. */
  function ideaCollapse(t) { return String(t || "").trim().replace(/\s+/g, " "); }
  function ideaAtWord(longer, shorter) { return longer.indexOf(shorter) === 0 && (longer.length === shorter.length || !/[A-Za-z0-9\u00c0-\uffff]/.test(longer.charAt(shorter.length))); }
  /* A question's chips: its options, with the recommendation marked or added, and any reason left over (offeredChoices). */
  function offeredChoices(x) {
    var choices = (x.options || []).slice(), wanted = ideaCollapse(x.recommendation);
    if (!wanted) { return { choices: choices, recommended: null, note: null }; }
    var said0 = wanted.toLowerCase(), exact = -1;
    choices.forEach(function (c, i) { if (exact < 0 && ideaCollapse(c).toLowerCase() === said0) { exact = i; } });
    if (exact >= 0) { return { choices: choices, recommended: exact, note: null }; }
    var best = -1, length = 0, tied = false;
    choices.forEach(function (c, i) {
      var op = ideaCollapse(c).toLowerCase(), shared = ideaAtWord(said0, op) ? op.length : ideaAtWord(op, said0) ? said0.length : 0;
      if (shared > length) { best = i; length = shared; tied = false; } else if (shared > 0 && shared === length) { tied = true; }
    });
    if (best < 0 || tied) { return { choices: choices.concat([wanted]), recommended: choices.length, note: null }; }
    var rest = wanted.slice(ideaCollapse(choices[best]).length).replace(/^[\s,;:.\-\u2013\u2014]+/, "");
    return { choices: choices, recommended: best, note: rest || null };
  }
  /* "Relates to L2 Storage: why it is asked", the context alone, the reference alone, or null (aboutLine). */
  function aboutLine(x) { return x.about && x.context ? "Relates to " + x.about + ": " + x.context : x.about ? "Relates to " + x.about : x.context || null; }
  /* The understanding to show above a question: its round's, when the question is that round's first (understoodBefore). */
  function understoodBefore(rounds, qid) { var r = (rounds || []).filter(function (one) { return (one.questions || [])[0] === qid; })[0]; return r && r.understanding ? r.understanding : null; }
  /* Where a page holds its place across a redraw: the focused element if on screen, else the first marked one (main's holdPlace). */
  function holdPlace(root) {
    function onScreen(e) { var b = e.getBoundingClientRect(); return b.bottom > 0 && b.top < (window.innerHeight || Infinity); }
    var sc = document.scrollingElement || document.documentElement;
    for (var at = root.parentElement; at; at = at.parentElement) { var ov = getComputedStyle(at).overflowY; if ((ov === "auto" || ov === "scroll") && at.scrollHeight > at.clientHeight) { sc = at; break; } }
    var act = document.activeElement, focused = act && act !== document.body && root.contains(act) && onScreen(act) ? act : null;
    var marked = Array.prototype.filter.call(root.querySelectorAll("[data-anchor]"), onScreen)[0] || null;
    var held = [focused, marked].filter(Boolean).map(function (e) { return { e: e, key: e.getAttribute("data-anchor"), top: e.getBoundingClientRect().top }; }), was = sc.scrollTop;
    return function () {
      for (var i = 0; i < held.length; i++) {
        var one = held[i], e = one.e.isConnected ? one.e : one.key ? root.querySelector("[data-anchor=\"" + one.key + "\"]") : null;
        if (!e) { continue; }
        var moved = e.getBoundingClientRect().top - one.top + (sc.scrollTop - was);
        if (Math.abs(moved) >= 1) { sc.scrollTop = sc.scrollTop + moved; }
        return;
      }
    };
  }
  /* A callback's answer: true, false, or a promise of either. Nothing returned counts as done, as before. */
  function settled(v, then) { if (v && typeof v.then === "function") { v.then(function (ok) { then(ok !== false); }, function () { then(false); }); } else { then(v !== false); } }
  function QuestionSet(o) {
    o = o || {};
    var qs = o.questions || [], rounds = o.rounds, drafts = {}, editing = {}, sent = {}, where = o.where || [], id = o.id || "<id>", sending = false, saidText = "";
    var ondraft = o.onDraft || o.ondraft, onsend = o.onSend || o.onsend;
    var name = uid("ls-q-");
    var el = h("div", { "class": "ls-qset" }), list = h("div", { "class": "qs" }), send = h("div", { "class": "send", "data-anchor": "send-answers" }), out = h("p", { "class": "ls-said held", role: "status" });
    el.appendChild(list); el.appendChild(send);
    function answerOf(x) { return Object.prototype.hasOwnProperty.call(sent, x.id) ? sent[x.id] : x.answer; }
    function dis() { return o.disabledReason || null; }
    function answerLine(x, text) { return lineOf("idea answer", [], {}, [id, x.id, "--answer=" + text].concat(where)); }
    /* Only the answers that changed and say something, against what was saved or sent (changedAnswers). */
    function changed() { return qs.filter(function (x) { var d = (drafts[x.id] || "").trim(); return d && d !== (answerOf(x) || ""); }); }
    function answers() { return changed().map(function (x) { var a = drafts[x.id].trim(); return { question: x.id, answer: a, words: [id, x.id, "--answer=" + a].concat(where), line: answerLine(x, a) }; }); }
    /* Drafts with every recommendation taken, for the questions still open with nothing drafted (acceptRecommendations). */
    function taken() {
      var next = Object.assign({}, drafts);
      qs.forEach(function (x) { var of = offeredChoices(x); if (of.recommended !== null && !answerOf(x) && !(next[x.id] || "").trim()) { next[x.id] = of.choices[of.recommended]; } });
      return next;
    }
    function tell() { if (ondraft) { ondraft(changed().map(function (x) { return x.id; }), Object.keys(sent)); } }
    /* A redraw keeps focus on the control that had it, by its key. */
    function keeping(fn) {
      var a = document.activeElement, k = a && el.contains(a) ? (a.closest("[data-k]") || a).getAttribute("data-k") : null;
      fn();
      if (k) { var b = el.querySelector("[data-k=\"" + k + "\"]"); if (b && b !== document.activeElement) { b.focus({ preventScroll: true }); } }
    }
    function paintSend() {
      keeping(function () {
        send.textContent = "";
        var c = answers(), n = c.length, reason = dis() || (n ? null : "answer a question first");
        var t = taken(), takeable = Object.keys(t).filter(function (k) { return t[k] !== drafts[k]; }).length, takeReason = dis() || (takeable ? null : "no recommendations left to take");
        send.appendChild(h("p", { "class": "count", text: n ? n + " answer" + (n === 1 ? "" : "s") + " ready to send." : "Answer what you can. Nothing is sent until you press a button." }));
        var row = h("div", { "class": "ls-answers flush" });
        row.appendChild(halo({ label: sending ? "Sending…" : "Send answers and refine", key: "qs-refine", next: "send-answers", kind: "needs", solid: !reason, glyph: "✓", cmd: n + " × loadout idea answer, then " + lineOf("idea refine", [], {}, [id].concat(where)), disabledReason: reason, onPress: function () { go(true); } }));
        row.appendChild(halo({ label: "Only save the answers", key: "qs-save", cmd: n + " × loadout idea answer", disabledReason: reason, onPress: function () { go(false); } }));
        row.appendChild(halo({ label: "Accept all recommendations", key: "qs-take", cmd: "fills " + takeable + " answer" + (takeable === 1 ? "" : "s") + "; sends nothing", disabledReason: takeReason, onPress: function () { drafts = taken(); paint(); } }));
        send.appendChild(row);
        if (n) { var d = h("details", {}, [h("summary", { text: "The commands, one per answer" })]); c.forEach(function (a) { d.appendChild(h("code", { text: a.line })); }); send.appendChild(d); }
        out.textContent = saidText; send.appendChild(out);
      });
      tell();
    }
    function go(refine) {
      if (!onsend || sending) { return; }
      var going = answers(), lines = going.map(function (a) { return a.line; });
      sending = true; saidText = ""; paintSend();
      settled(onsend(going, refine, lines), function (ok) {
        sending = false;
        if (!ok) { paintSend(); return; }
        var held = holdPlace(el);
        going.forEach(function (a) { sent[a.question] = a.answer; delete drafts[a.question]; editing[a.question] = false; });
        saidText = "✓ " + going.length + " answer" + (going.length === 1 ? "" : "s") + " saved" + (refine ? ". The next round has started: it runs on the daemon, and a toast says when it ends." : ".");
        paint(); held();
      });
    }
    /* Sends the saved answer again, which says it still stands and clears its possibly outdated marker (standingAnswer). */
    function stand(x) {
      var a = answerOf(x);
      if (!onsend || sending || !a) { return; }
      var again = { question: x.id, answer: a, words: [id, x.id, "--answer=" + a].concat(where), line: answerLine(x, a) };
      sending = true; saidText = ""; paintSend();
      settled(onsend([again], false, [again.line]), function (ok) { sending = false; if (ok) { saidText = "✓ " + x.id + " still stands."; } paintSend(); });
    }
    function mark(card, who, x) {
      var answered = answerOf(x) && !editing[x.id], typed = (drafts[x.id] || "").trim();
      card.className = "q" + (answered ? " answered" : typed ? " drafted" : "");
      who.textContent = [x.id, answered ? "answered" : typed ? "your answer, not sent yet" : "the agent asks"].join(" · ");
    }
    function paintQ(x, i) {
      var answer = answerOf(x), answered = !!answer && !editing[x.id], typed = (drafts[x.id] || "").trim(), of = offeredChoices(x), about = aboutLine(x), qid = name + "-" + x.id;
      var card = h("section", { "class": "q", "aria-labelledby": qid, "data-next": "question:" + x.id, "data-anchor": "question:" + x.id }), who = h("span", { "class": "who" });
      card.appendChild(who); mark(card, who, x);
      if (answered) {
        card.appendChild(h("p", { "class": "qt", id: qid, text: x.question }));
        if (about) { card.appendChild(h("p", { "class": "about", text: about })); }
        if (x.outdated) { card.appendChild(h("p", { "class": "outdated" }, ["Possibly outdated: " + x.outdated + ". Answering again clears it.", dis() ? null : h("button", { type: "button", "data-k": "qs-stand-" + x.id, onclick: function () { stand(x); }, text: "It still stands" })])); }
        card.appendChild(h("p", { "class": "was" }, ["You answered: ", h("b", { text: answer }), dis() ? null : h("button", { type: "button", onclick: function () { editing[x.id] = true; drafts[x.id] = answer || ""; repaint(i); }, text: "Change it" })]));
        return card;
      }
      var fs = h("fieldset", {}, [h("legend", { id: qid, text: x.question })]);
      if (about) { fs.appendChild(h("p", { "class": "about", text: about })); }
      if (x.outdated) { fs.appendChild(h("p", { "class": "outdated", text: "Possibly outdated: " + x.outdated + ". Answering clears it." })); }
      if (x.why) { fs.appendChild(h("p", { "class": "why", text: x.why })); }
      var own = h("textarea", { "class": "ls-text", id: qid + "-w", rows: 2, placeholder: "Or say it in your own words" });
      var opts = h("div", { "class": "opts" });
      of.choices.forEach(function (op, n) {
        var r = h("input", { type: "radio", name: qid + "-o", value: op, onchange: function () { drafts[x.id] = op; own.value = ""; mark(card, who, x); paintSend(); } });
        r.checked = typed === op;
        opts.appendChild(h("label", { "class": "opt" }, [r, op, of.recommended === n ? h("span", { "class": "rec", text: "recommended" }) : null]));
      });
      if (of.choices.length) { fs.appendChild(opts); }
      if (of.note && of.recommended !== null) { fs.appendChild(h("p", { "class": "why", text: "Why it recommends " + of.choices[of.recommended] + ": " + of.note })); }
      if (typed && of.choices.indexOf(typed) < 0) { own.value = drafts[x.id]; }
      own.addEventListener("input", function () { drafts[x.id] = own.value; var t = own.value.trim(); Array.prototype.forEach.call(opts.querySelectorAll("input"), function (r) { r.checked = r.value === t; }); mark(card, who, x); paintSend(); });
      fs.appendChild(h("div", { "class": "own" }, [h("label", { "for": qid + "-w", text: "Or in your own words" }), own]));
      card.appendChild(fs);
      return card;
    }
    function understood(x) { var u = understoodBefore(rounds, x.id); return u ? h("p", { "class": "understood", text: "Understood as: " + u }) : null; }
    function repaint(i) { paint(); var c = list.querySelector("[data-anchor=\"question:" + qs[i].id + "\"]"), f = c && c.querySelector("input, textarea"); if (f) { f.focus(); } }
    function paint() {
      keeping(function () {
        list.textContent = "";
        qs.forEach(function (x, i) { var u = understood(x); if (u) { list.appendChild(u); } list.appendChild(paintQ(x, i)); });
      });
      paintSend();
    }
    /* A fresh read: drafts that are now the saved answer are dropped, and so is what was sent once it reads back (settledDrafts). */
    function set(p) {
      p = p || {};
      if (p.rounds !== undefined) { rounds = p.rounds; }
      if (p.disabledReason !== undefined) { o.disabledReason = p.disabledReason; }
      if (p.questions) {
        qs = p.questions;
        Object.keys(drafts).forEach(function (k) { var x = qs.filter(function (one) { return one.id === k; })[0]; if (x && (x.answer || null) === drafts[k].trim()) { delete drafts[k]; } });
        Object.keys(sent).forEach(function (k) { var x = qs.filter(function (one) { return one.id === k; })[0]; if (x && x.answer === sent[k]) { delete sent[k]; } });
      }
      var held = holdPlace(el); paint(); held();
    }
    if (o.drafts) { Object.keys(o.drafts).forEach(function (k) { drafts[k] = o.drafts[k]; }); }
    paint();
    return { el: el, set: set, get changed() { return changed(); }, get answers() { return answers(); }, get drafts() { return Object.assign({}, drafts); },
      destroy: function () { if (ondraft) { ondraft([], []); } } };
  }

  /* PlanLayer: one layer of an idea's plan. Its options side by side with what is for and against each; choose one, keep or drop the layer, or ask for it to be reworked. An addition is drawn the same way, without options.
     A choice or a verdict shows the moment it is made, and goes back to what it was if it is not recorded (a callback that returns or resolves false).
     Nothing changes size when it does: the button and the chosen marker share one slot, and the status line keeps its height when empty. */
  function PlanLayer(o) {
    o = o || {};
    var L = o.layer || o.addition || {}, add = !!o.addition, id = o.idea || "<id>", where = o.where || [], dis = o.disabledReason || null, lock = !!o.accepted;
    var onrework = o.onRework || o.onrework, onrequest = o.onRequest || o.onrequest;
    /* Made here and not yet read back: shown in place of what the idea says. */
    var choosing = null, judging = null, chosenNow = L.chosen || null, verdictNow = null;
    /* A page that draws the layer again gives back what was typed and not sent (request), and the box opens with it. */
    var request = typeof o.request === "string" ? o.request : "", reworking = !lock && !!request.trim(), saidText = "", failed = false, pending = false;
    function cap(w) { w = String(w || "Undecided"); return w.charAt(0).toUpperCase() + w.slice(1).toLowerCase(); }
    function verdict() { return judging || verdictNow || cap(L.verdict); }
    function chosen() { return choosing || chosenNow; }
    function busy() { return !!choosing || !!judging; }
    var name = L.name || L.title || "";
    var el = h("section", { "aria-label": (add ? "Addition " : "Layer ") + L.id + ": " + name, "data-anchor": "piece:" + L.id });
    var out = h("p", { "class": "ls-said held", role: "status" });
    var ta = h("textarea", { "class": "ls-text", id: uid("rw-"), rows: 2, placeholder: "What should change, in a sentence", "aria-label": "What should change about " + L.id });
    ta.value = request;
    var sendBox = h("div", {});
    function tell() { var now = reworking && !!request.trim(); if (now !== pending) { pending = now; if (onrework) { onrework(now); } } }
    function keeping(fn) {
      var a = document.activeElement, k = a && el.contains(a) && a !== ta ? (a.closest("[data-k]") || a).getAttribute("data-k") : null;
      fn();
      if (k) { var b = el.querySelector("[data-k=\"" + k + "\"]"); if (b && b !== document.activeElement) { b.focus({ preventScroll: true }); } }
    }
    function paintSend() {
      sendBox.textContent = "";
      sendBox.appendChild(halo({ label: "Send it back", key: "pl-send", next: "rework:" + L.id, size: "small", kind: "needs", cmd: lineOf("idea improve", [], {}, [id, L.id, "--request=" + (request.trim() || "<what should change>")].concat(where)), disabledReason: request.trim() ? dis : "say what should change", inactive: busy(), onPress: sendBack }));
    }
    function choose(op) {
      if (choosing || !o.onChoose) { return; }
      choosing = op.id; saidText = ""; paint();
      settled(o.onChoose(op.id), function (ok) {
        choosing = null; failed = !ok;
        if (ok) { chosenNow = op.id; }
        saidText = ok ? "✓ Chose " + op.id + " for " + L.id : "✕ " + op.id + " was not recorded for " + L.id;
        paint();
      });
    }
    function judge(to, send, done) {
      if (judging) { return; }
      judging = to; saidText = ""; paint();
      settled(send(), function (ok) {
        judging = null; failed = !ok;
        if (ok) { verdictNow = to; }
        saidText = ok ? "✓ " + done : "✕ That was not recorded for " + L.id;
        paint();
      });
    }
    function sendBack() {
      var text = request.trim();
      reworking = false; request = ""; ta.value = ""; tell(); if (onrequest) { onrequest(""); }
      judge("Improve", function () { return o.onImprove ? o.onImprove(text) : false; }, "Sent " + L.id + " back to be reworked");
    }
    ta.addEventListener("input", function () { request = ta.value; paintSend(); tell(); if (onrequest) { onrequest(request); } });
    function paint() {
      keeping(function () {
        var v = verdict(), pick = chosen();
        el.className = "ls-layer" + (add ? " addition" : "") + (v === "Drop" ? " dropped" : "");
        el.textContent = "";
        el.appendChild(h("div", { "class": "lh" }, [h("span", { "class": "id", text: L.id }), h("h4", { text: name }), tag("verdict: " + v.toLowerCase(), { Keep: "good", Improve: "needs" }[v] || "")]));
        if (add) { el.appendChild(h("p", { "class": "why", text: L.why || "" })); }
        else if (L.purpose) { el.appendChild(h("p", { "class": "purpose", text: L.purpose })); }
        if (!add) {
          var grid = h("div", { "class": "opts", "data-next": "choose:" + L.id });
          (L.options || []).forEach(function (op) {
            var picked = pick === op.id, card = h("article", { "class": "card" + (picked ? " chosen" : ""), "aria-label": op.title + (picked ? ", chosen" : ""), "data-anchor": "option:" + L.id + ":" + op.id });
            card.appendChild(h("div", { "class": "ct" }, [h("span", { "class": "id", text: op.id }), h("b", { text: op.title }), op.recommended ? tag("recommended", "accent") : null]));
            if (op.detail) { card.appendChild(h("p", { text: op.detail })); }
            var pc = (op.pros || []).map(function (p) { return h("li", { "class": "pro" }, [h("i", { "aria-hidden": "true", text: "+" }), h("span", {}, [h("span", { "class": "ls-vh", text: "For: " }), p])]); })
              .concat((op.cons || []).map(function (p) { return h("li", { "class": "con" }, [h("i", { "aria-hidden": "true", text: "−" }), h("span", {}, [h("span", { "class": "ls-vh", text: "Against: " }), p])]); }));
            if (pc.length) { card.appendChild(h("ul", {}, pc)); }
            var markP = h("p", { "class": "mark" }, [tag(choosing === op.id ? "chosen · recording" : "chosen", "good", G.ok)]);
            if (!lock) {
              /* Both always drawn in one cell, the other one hidden, so choosing changes nothing's size. */
              var btn = h("div", { "class": picked ? "off" : "", "aria-hidden": picked ? "true" : null }, [halo({ label: "Choose this", key: "pl-choose-" + op.id, size: "small", cmd: lineOf("idea choose", [], {}, [id, L.id, op.id].concat(where)), disabledReason: dis, inactive: busy(), onPress: function () { choose(op); } })]);
              if (picked) { btn.inert = true; } else { markP.classList.add("off"); markP.setAttribute("aria-hidden", "true"); }
              card.appendChild(h("div", { "class": "slot" }, [btn, markP]));
            } else if (picked) { card.appendChild(markP); }
            grid.appendChild(card);
          });
          el.appendChild(grid);
        }
        if (!lock) {
          var acts = h("div", { "class": "acts", "data-next": "verdict:" + L.id });
          acts.appendChild(halo({ label: "Keep it", key: "pl-keep", size: "small", glyph: "✓", cmd: lineOf("idea keep", [], {}, [id, L.id].concat(where)), disabledReason: dis, inactive: busy() || v === "Keep", onPress: function () { judge("Keep", function () { return o.onVerdict ? o.onVerdict("Keep") : false; }, "Kept " + L.id); } }));
          acts.appendChild(halo({ label: "Drop it", key: "pl-drop", size: "small", glyph: "✕", cmd: lineOf("idea drop", [], {}, [id, L.id].concat(where)), disabledReason: dis, inactive: busy() || v === "Drop", onPress: function () { judge("Drop", function () { return o.onVerdict ? o.onVerdict("Drop") : false; }, "Dropped " + L.id); } }));
          acts.appendChild(halo({ label: "Ask for it to be reworked", key: "pl-rework", size: "small", glyph: "↻", cmd: "says what should change, then sends it", disabledReason: dis, inactive: busy(), onPress: function () { reworking = !reworking; paint(); tell(); if (reworking) { ta.focus(); } } }));
          el.appendChild(acts);
          paintSend();
          el.appendChild(h("div", { "class": "rework" + (reworking ? " on" : "") }, [ta, sendBox]));
        }
        out.className = "ls-said held" + (failed ? " no" : ""); out.textContent = saidText;
        el.appendChild(out);
      });
    }
    paint();
    tell();
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
    /* What waits on you there (sessions, runs, approvals, ideas with questions), first among its counts. */
    if (o.waiting) { counts.push(h("span", { "class": "needs waiting" }, [h("b", { text: String(o.waiting) }), " waiting on you"])); }
    if (o.doing !== undefined) { counts.push(h("span", {}, [h("b", { text: String(o.doing) }), " doing"])); }
    if (o.blocked) { counts.push(h("span", { "class": "needs" }, [h("b", { text: String(o.blocked) }), " blocked"])); }
    if (o.findings !== undefined) { counts.push(h("span", { "class": o.findings ? "needs" : "" }, o.findings ? [h("b", { text: String(o.findings) }), " to put right"] : ["\u2713 healthy"])); }
    if (o.ideas) { counts.push(h("span", {}, [h("b", { text: String(o.ideas) }), " idea" + (o.ideas === 1 ? "" : "s")])); }
    return h("a", { "class": "ls-ptile" + (away ? " away" : ""), href: o.href || "#", "aria-current": o.picked ? "page" : null }, [
      h("span", { "class": "nm" }, [p.name || p.id, p.pinned ? h("span", { "class": "pin", text: "pinned" }) : null]),
      h("span", { "class": "slug", text: p.id + (away ? "" : " \u00b7 " + (p.path || "")) }),
      h("span", { "class": "facts" }, facts), counts.length ? h("span", { "class": "counts" }, counts) : null]);
  }

  /* TaskBoard: a project's tasks as Open, Doing and Blocked, with done and dropped folded away. Each card offers only the moves its state
     allows, each with the command that says so. Given a project and onStart, a card also starts a session on its task, at once or through the
     launch sheet. History (or Details and history) opens the task in full. A board can hold more than one list's tasks (mixed), each card then
     naming its list; what moved away from a list is listed apart, never as one of its tasks. As main's TaskBoard.svelte and lib/tasks.ts. */
  var NEXT = { open: ["doing", "blocked", "dropped"], doing: ["done", "blocked", "open"], blocked: ["doing", "dropped"], done: ["open"], dropped: ["open"] };
  var MOVE = { doing: "Mark it doing", done: "Mark it done", blocked: "It is blocked", dropped: "Drop it", open: "Reopen it" };
  function taskWhereOf(project) { return project ? ["--project", project] : ["--global"]; }
  function taskWhereSaid(project) { return project || "the workspace-wide list"; }
  function taskListHref(project) { return project ? "#projects/" + encodeURIComponent(project) : "#projects/+tasks"; }
  /* A day as "3 Oct"; words that are not a date are shown as they are. */
  function taskDay(at) { if (!at) { return ""; } var d = new Date(at); return isNaN(d.getTime()) ? String(at) : d.toLocaleDateString([], { day: "numeric", month: "short" }); }
  /* 'missing' for open or blocked work without a done-when, 'proposalWaiting' when one is proposed for you to confirm. */
  function taskMarkWord(mark) { return mark === "missing" ? "missing" : mark === "proposalWaiting" ? "proposal waiting" : null; }
  function TaskBoard(o) {
    o = o || {};
    var tasks = o.tasks || [], where = o.where || [], el = h("div", { "class": "ls-tboard" }), name = uid("ls-tb-");
    var onstart = o.onStart || o.onstart, onread = o.onRead || o.onread, mixed = !!o.mixed;
    /* The list a task is on, by slug, or null for the workspace-wide one; the words naming it; the project a session on it starts in. */
    function listOf(t) { return mixed ? (t.project || null) : where[0] === "--project" ? where[1] : null; }
    function whereFor(t) { return mixed ? taskWhereOf(t.project) : where; }
    function startIn(t) { return mixed ? (t.project || null) : (o.project || null); }
    function read(t) {
      var at = Object.assign({}, t, { project: listOf(t) });
      if (onread) { onread(at); } else if (NS.TaskDetail) { NS.TaskDetail({ task: at, read: o.read, host: o.host, contained: o.contained, behind: o.behind }); }
    }
    function card(t) {
      var s = h("p", { "class": "said", role: "status" }), moves = h("div", { "class": "moves" }), into = startIn(t);
      if (into && onstart && t.state !== "dropped") {
        moves.appendChild(halo({ label: "Start", size: "small", solid: true, cmd: lineOf("launch", [], {}, [into, "--task-id", t.id]), disabledReason: o.disabledReason, onPress: function () { onstart(t, false); } }));
        moves.appendChild(halo({ label: "Start with options…", size: "small", cmd: "opens the launch sheet with this task chosen", disabledReason: o.disabledReason, onPress: function () { onstart(t, true); } }));
      }
      (NEXT[t.state] || []).forEach(function (n) { moves.appendChild(halo({ label: MOVE[n], size: "small", kind: n === "dropped" ? "danger" : "", cmd: lineOf("task declare", [], {}, [t.id, n].concat(whereFor(t))), disabledReason: o.disabledReason, onPress: function () { var was = t.state; t.state = n; if (o.onDeclare) { o.onDeclare(t, n, was); } paint(); } })); });
      if (onread || o.read) { moves.appendChild(halo({ label: t.hasDetail ? "Details and history" : "History", size: "small", cmd: lineOf("task show", [], {}, [t.id].concat(taskWhereOf(listOf(t)))), onPress: function () { read(t); } })); }
      var go = null;
      if ((o.onSession || o.onRun) && t.kind !== "idea" && (t.state === "open" || t.state === "doing" || t.state === "blocked")) {
        go = h("div", { "class": "go" });
        if (o.onSession) { go.appendChild(halo({ label: "Start a session on it", size: "small", glyph: "▸", cmd: "opens the launch sheet with this task · --task-id=" + t.id, disabledReason: o.disabledReason, onPress: function () { o.onSession(t); } })); }
        if (o.onRun) { go.appendChild(halo({ label: "Run a team on it", size: "small", glyph: "▶", cmd: "opens Start a run with this task · --task " + t.id, disabledReason: o.disabledReason || (/^team /.test(t.declaredBy || "") && t.state === "doing" ? "a team run is on it already" : null), onPress: function () { o.onRun(t); } })); }
      }
      var mw = taskMarkWord(t.mark);
      return h("article", { "class": "task " + t.state, "aria-label": t.title + ", " + t.state }, [h("span", { "class": "tt", text: t.title }),
        h("span", { "class": "tx", text: [mixed ? taskWhereSaid(t.project) : "", t.id, t.kind, t.declaredBy, taskDay(t.declared)].filter(Boolean).join(" · ") }),
        mw ? h("span", { "class": "ls-tag needs mark", text: mw }) : null,
        t.moved ? h("span", { "class": "came", text: "Moved here from " + taskWhereSaid(t.moved.from) + ", " + taskDay(t.moved.at) }) : null,
        t.note ? h("p", { "class": "note", text: t.note }) : null, moves, go, s]);
    }
    function paint() {
      el.textContent = "";
      var cols = h("div", { "class": "cols" });
      [["open", "Open"], ["doing", "Doing"], ["blocked", "Blocked"]].forEach(function (c) {
        var ts = tasks.filter(function (t) { return t.state === c[0]; }), sid = name + "-" + c[0];
        var col = h("section", { "class": "col", "aria-labelledby": sid }, [h("h4", { id: sid }, [NS.Signal ? NS.Signal(c[0]) : null, c[1], h("span", { "class": "n", text: String(ts.length) })])]);
        if (!ts.length) { col.appendChild(h("p", { "class": "empty", text: o.paged ? "None on this page." : { open: "Nothing waiting to start.", doing: "Nothing under way.", blocked: "Nothing blocked." }[c[0]] })); }
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
      var away = o.away || [];
      if (away.length) {
        el.appendChild(h("section", { "class": "away", "aria-labelledby": name + "-away" }, [h("h4", { id: name + "-away", text: "Moved away" }), h("ul", {}, away.map(function (g) {
          return h("li", {}, [(g.title || g.id) + " moved to " + taskWhereSaid(g.to) + ", " + taskDay(g.at) + ". ", h("a", { href: taskListHref(g.to), text: "Show it" })]);
        }))]));
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
      acts.appendChild(NS.HaloButton({ label: "Preview", size: "small", cmd: dis ? null : "adds --dry-run to each \u00b7 changes nothing", disabledReason: dis, onPress: function (e) { run(true, e && e.currentTarget); } }));
      acts.appendChild(NS.HaloButton({ label: armed ? "Press again to forget it everywhere" : (scope === "everywhere" ? "Forget it on every machine" : "Forget it here"), kind: "danger", size: "small", solid: armed && !dis, cmd: dis ? null : steps().length + " command" + (steps().length === 1 ? "" : "s") + ", in order; stops at the first that fails", disabledReason: dis, onPress: function (e) {
        if (scope === "everywhere" && !armed) { armed = true; armedEl.textContent = "Every machine that uses the workspace forgets " + slug + ". Press the button again within five seconds to go ahead."; clearTimeout(armT); armT = setTimeout(function () { disarm(); paint(); }, 5000); paint(); var b = acts.querySelectorAll("button")[1]; if (b) { b.focus(); } return; }
        disarm(); run(false, e && e.currentTarget);
      } }));
    }
    function disarm() { armed = false; clearTimeout(armT); armedEl.textContent = ""; }
    /* onRun may return a promise: the pressed button is busy until it settles. false, a throw or a rejection says "✕ why" and forgets nothing on screen. */
    function run(preview, btn) {
      var lines = steps().map(function (s) { return preview ? s + " --dry-run" : s; });
      var r = o.onRun ? o.onRun({ scope: scope, lines: lines, preview: preview, picked: Object.assign({}, picked) }) : null;
      function ok(v) { said.textContent = "\u2713 " + (typeof v === "string" ? v : preview ? "Previewed. Nothing changed." : "Forgotten."); paint(); }
      function bad(e) { said.textContent = "\u2715 " + ((e && e.message) || (typeof e === "string" && e) || o.failed || "It did not go through. Nothing was forgotten."); }
      if (r && typeof r.then === "function") {
        if (btn) { NS.HaloButton.setBusy(btn, preview ? "checking\u2026" : "forgetting\u2026"); }
        said.textContent = "";
        return r.then(function (v) { if (v === false) { throw new Error(o.failed || "It did not go through. Nothing was forgotten."); } if (btn) { NS.HaloButton.setBusy(btn, false); } ok(v); return v; })
          .catch(function (e) { if (btn) { NS.HaloButton.setBusy(btn, false); } bad(e); });
      }
      if (r === false) { bad(); return r; }
      ok(r); return r;
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
        tr.appendChild(h("td", { "class": cls(v) + (moved ? " moved" : "") + (k + 1 === now ? " now" : ""), "data-r": "R" + (k + 1) }, [h("i", { "aria-hidden": "true", text: m[0] }), h("span", { "class": "ls-vh", text: "Round " + (k + 1) + ": " + m[1] + (moved ? ", changed" : "") })]));
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
    return { el: el, set: function (xs) { var before = sig(); items = NS.liveFirst(xs || [], { group: function (x) { return x.level === "problem" ? "pickup" : "waiting"; } }); if (sig() !== before && quietSig && sig().indexOf(quietSig) < 0) { quietSig = null; } if (!items.length) { quietSig = null; } paint(); }, enable: function (v) { o.enabled = !!v; paint(); }, get showing() { return !el.hidden; }, destroy: function () { el.remove(); } };
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
  var SHOWN = ["plain", "map", "map-scan"];
  /** The studio-backdrop setting as kept: the plain ground (the default, and what anything unknown means), the field map still, or animated. What shows is resolveBackdrop()'s. */
  function setBackdrop(v, el) { (el || document.documentElement).setAttribute("data-backdrop", BACKDROPS.indexOf(v) >= 0 ? v : "plain"); }
  /* What is behind the page right now. In order: high contrast, Pure Focus and Ambient keep the plain ground; then the setting; the scan runs only under full motion while nothing needs you, the tab is visible and no terminal has the keyboard. */
  function resolveBackdrop(o) {
    o = o || {};
    if (o.theme === "hc-dark" || o.theme === "hc-light" || o.mode === "pure" || o.ambient) { return "plain"; }
    if (o.setting === "map") { return "map"; }
    if (o.setting !== "map-animated") { return "plain"; }
    return o.motion === "full" && !(o.waiting > 0) && !o.hidden && !o.terminalFocused ? "map-scan" : "map";
  }
  /** Writes resolveBackdrop()'s answer as data-fieldmap on <html>, which is all the CSS keys off. Unknown values are plain. */
  function setFieldmap(v, el) { (el || document.documentElement).setAttribute("data-fieldmap", SHOWN.indexOf(v) >= 0 ? v : "plain"); }

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

  /* FieldMap: the embossed survey map. Each contour is drawn three times: a light edge, a dark edge, and the line itself.
     It draws only when first wanted, after the first paint, and again 250ms after a resize; a change of setting, theme or mode
     never redraws it, since the CSS decides from data-fieldmap what shows. */
  var fmCount = 0;
  /* Runs then once the browser is idle, or a frame from now where it cannot say. */
  function later(then) {
    if (typeof window.requestIdleCallback === "function") { var hi = window.requestIdleCallback(then, { timeout: 1000 }); return function () { window.cancelIdleCallback(hi); }; }
    var hf = window.requestAnimationFrame(then); return function () { window.cancelAnimationFrame(hf); };
  }
  function FieldMap(o) {
    o = o || {};
    var el = document.createElement("div"); el.className = "ls-fieldmap"; el.setAttribute("aria-hidden", "true");
    var host = o.host || document.body, timer = null, drawn = false, cancel = null, watch = null, shown = o.shown;
    /* With no shown passed, it follows data-fieldmap on <html>, as main's App writes it. */
    function now() { return shown !== undefined ? shown : (document.documentElement.getAttribute("data-fieldmap") || "plain"); }
    function draw() {
      if (cancel) { cancel(); cancel = null; }
      var W = o.width || window.innerWidth, H = o.height || window.innerHeight, c = contours(W, H, o), sq = o.square || 200;
      var svg = s("svg", { viewBox: "0 0 " + W + " " + H, preserveAspectRatio: "none", focusable: "false" });
      /* Each set of lines is drawn once, in defs, and reused (dark edge, light edge, line, scan), so the page holds its path data once. */
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
      /* The scan covers the page area only, from the rail's collapsed edge, so it never shows through the rail. */
      var scan = s("svg", { viewBox: "0 0 " + W + " " + H, preserveAspectRatio: "none", "class": "scan", focusable: "false" }, [lines(MINOR, "ln", 1.1), lines(INDEX, "ix", 1.6)]);
      var zone = document.createElement("div"); zone.className = "scanzone"; zone.appendChild(scan);
      el.textContent = ""; el.appendChild(svg); el.appendChild(zone);
      drawn = true;
    }
    function want() { if (now() !== "plain" && !drawn && !cancel) { cancel = later(function () { cancel = null; draw(); }); } }
    host.insertBefore(el, host.firstChild);
    want();
    if (shown === undefined && typeof MutationObserver === "function") { watch = new MutationObserver(want); watch.observe(document.documentElement, { attributes: true, attributeFilter: ["data-fieldmap"] }); }
    /* While the map is hidden, forget it, so it is drawn afresh when it next shows. */
    function onResize() { clearTimeout(timer); timer = setTimeout(function () { if (now() === "plain") { drawn = false; el.textContent = ""; } else { draw(); } }, 250); }
    if (!o.width) { window.addEventListener("resize", onResize); }
    return {
      el: el,
      redraw: draw,
      set: function (v) { shown = v; want(); },
      destroy: function () { clearTimeout(timer); if (cancel) { cancel(); } if (watch) { watch.disconnect(); } window.removeEventListener("resize", onResize); el.remove(); }
    };
  }

  Object.assign(NS, { FieldMap: FieldMap, setBackdrop: setBackdrop, BACKDROPS: BACKDROPS, resolveBackdrop: resolveBackdrop, setFieldmap: setFieldmap });
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



/* ---------- Stage 14: what main's Studio built since the design ---------- */
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
    (kids || []).forEach(function (c) { if (c !== null && c !== undefined && c !== false) { el.appendChild(typeof c === "string" || typeof c === "number" ? document.createTextNode(String(c)) : c); } });
    return el;
  }
  function uid(p) { return p + Math.random().toString(36).slice(2, 7); }
  function q(v) { v = String(v); if (/^<[^<>\s]+>$/.test(v)) { return v; } return v === "" || /[\s"'$`\\|&;<>()*?!#~]/.test(v) ? "\"" + v.replace(/(["\\$`])/g, "\\$1") + "\"" : v; }
  function full() { return !NS.motion || NS.motion() === "full"; }

  /* --- AskPane --- */
  (function () {
    /* AskPane: the palette's conversation with Loadout: each request and its answer, newest
       last. An answer says who gave it; a near miss can be asked as it stands; a plan is a
       card to approve. Ported from main's AskPane.svelte. */

    function usd(amount) {
      return "$" + Number(amount).toLocaleString("en-GB", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    /* A reply as the command wrote it, with what --json left out filled in (main's arrived()). */
    function arrived(reply) {
      if (!reply) { return null; }
      var spend = reply.spend;
      return {
        query: reply.query, kind: reply.kind, answeredBy: reply.answeredBy || "loadout", summary: reply.summary || "",
        tasks: (reply.tasks || []).map(function (t) { return { project: t.project || "", id: t.id, title: t.title, score: t.score, reasons: t.reasons || [] }; }),
        nearMisses: reply.nearMisses || [], agentOffer: reply.agentOffer || null,
        agentCostUsd: reply.agentCostUsd === undefined ? null : reply.agentCostUsd, agentSession: reply.agentSession || null,
        spend: spend ? { models: (spend.models || []).map(function (m) { return { model: m.model, tokens: m.tokens || 0, usd: m.usd === undefined ? null : m.usd }; }), caveat: spend.caveat || null } : null,
        plan: reply.plan || null
      };
    }

    function AskPane(o) {
      var state = { turns: [], asking: false };
      var el = h("div", { "class": "ls-ask", "aria-live": "polite" });
      /* A plan card keeps what was typed into it across a redraw, as main's keyed each does. */
      var cards = {};

      function turnEl(turn) {
        var kids = [h("p", { "class": "q" }, [h("span", { "aria-hidden": "true", text: "?" }), " " + turn.query])];
        var reply = arrived(turn.reply);
        if (turn.failed) {
          kids.push(h("p", { "class": "failed", role: "alert", text: turn.failed }));
        } else if (!reply) {
          kids.push(h("p", { "class": "wait", text: "Asking…" }));
        } else {
          kids.push(h("p", { "class": "badge", text: reply.answeredBy === "agent" ? "Answered by the agent" + (reply.agentCostUsd !== null ? ", for " + usd(reply.agentCostUsd) : "") : "Answered by Loadout" }));
          kids.push(h("p", { "class": "a", text: reply.summary }));
          if (reply.spend) {
            kids.push(h("ul", { "class": "models", "aria-label": "By model" }, reply.spend.models.map(function (m) {
              return h("li", {}, [h("span", { text: m.model }), h("span", { text: m.tokens.toLocaleString("en-GB") + " tokens" }), h("span", { text: m.usd === null ? "no price" : usd(m.usd) })]);
            })));
            if (reply.spend.caveat) { kids.push(h("p", { "class": "caveat", text: reply.spend.caveat })); }
          }
          if (reply.tasks.length) {
            kids.push(h("ol", { "class": "tasks" }, reply.tasks.map(function (task) {
              return h("li", {}, [
                h("span", { "class": "says" }, [task.title + " ", h("span", { "class": "where", text: task.id + " · " + (task.project || "workspace") })]),
                task.reasons.length ? h("span", { "class": "why", text: task.reasons.join("; ") }) : null
              ]);
            })));
          }
          if (reply.plan) {
            if (!cards[turn.id] || cards[turn.id].source !== turn.reply.plan) { cards[turn.id] = { source: turn.reply.plan, card: NS.AskPlanCard({ plan: reply.plan, approve: o.approve, onapproved: function () { if (o.onapproved) { o.onapproved(); } } }) }; }
            kids.push(cards[turn.id].card.el);
          }
          if (reply.nearMisses.length) {
            var near = reply.nearMisses.map(function (n) {
              return h("li", {}, [h("button", { type: "button", "class": "ls-opt", onclick: function () { if (o.onrewrite) { o.onrewrite(n.rewrite); } } },
                [h("span", { "class": "g", "aria-hidden": "true", text: "?" }), h("span", { "class": "t", text: n.rewrite })])]);
            });
            if (reply.agentOffer) {
              near.push(h("li", {}, [h("button", { type: "button", "class": "ls-opt", "data-agent": true, disabled: state.asking, onclick: function () { if (o.onagent) { o.onagent(turn.query); } } }, [
                h("span", { "class": "g", "aria-hidden": "true", text: "✦" }),
                h("span", { "class": "t", text: "Ask the agent instead, for about " + usd(reply.agentOffer.estimatedUsd) }),
                h("span", { "class": "s", text: reply.agentOffer.basis + ". It only reads; anything it proposes comes back as a plan." })
              ])]));
            }
            kids.push(h("ul", { "class": "near", "aria-label": "Closest requests Loadout can answer" }, near));
          }
        }
        return h("section", { "class": "turn" }, kids);
      }

      /* Redraws in place: a turn already shown keeps its nodes, so the live region speaks only what is new. */
      var shown = {}, empty = null, clearBtn = null;
      function paint() {
        var want = [], live = {};
        if (!state.turns.length) {
          empty = empty || h("p", { "class": "ls-pempty", text: "Ask in plain words, then press Enter. Loadout answers what it can from its own records: what you spent, which tasks are easy or need work first, or what to queue and start." });
          want.push(empty);
        }
        state.turns.forEach(function (turn) {
          live[turn.id] = true;
          if (!shown[turn.id] || shown[turn.id].turn !== turn) { shown[turn.id] = { turn: turn, node: turnEl(turn) }; }
          var agent = shown[turn.id].node.querySelector(".near li:last-child .ls-opt[data-agent]");
          if (agent) { agent.disabled = state.asking; }
          want.push(shown[turn.id].node);
        });
        Object.keys(shown).forEach(function (k) { if (!live[k]) { delete shown[k]; delete cards[k]; } });
        if (state.turns.length) {
          clearBtn = clearBtn || h("button", { type: "button", "class": "clear", onclick: function () { if (o.onclear) { o.onclear(); } }, text: "Clear the conversation" });
          clearBtn.disabled = state.asking;
          want.push(clearBtn);
        }
        want.forEach(function (node, i) { if (el.childNodes[i] !== node) { el.insertBefore(node, el.childNodes[i] || null); } });
        while (el.childNodes.length > want.length) { el.removeChild(el.lastChild); }
      }

      function set(next) {
        next = next || {};
        Object.keys(next).forEach(function (k) { if (k === "turns" || k === "asking") { state[k] = next[k]; } else { o[k] = next[k]; } });
        paint();
      }

      /* Main's AskConversation, for a page that has no conversation of its own: the request is
         added at once, and its reply, or why there was none, when it comes. */
      var nextId = 1;
      function ask(query, asker) {
        var id = "ask-" + (nextId++), turns = state.turns.slice();
        function put(fields) { set({ turns: state.turns.map(function (t) { return t.id === id ? Object.assign({}, t, fields) : t; }), asking: false }); }
        turns.push({ id: id, query: query, reply: null, failed: null });
        set({ turns: turns, asking: true });
        var settled;
        try { settled = Promise.resolve(asker(query)); } catch (error) { settled = Promise.reject(error); }
        return settled.then(function (reply) { if (reply && reply.agentSession) { agentSession = reply.agentSession; } put({ reply: reply }); }, function (error) { put({ failed: error && error.message ? error.message : "Loadout could not answer that." }); });
      }
      /* The agent's conversation, once it has been asked, so a follow-up carries it on. */
      var agentSession = null;
      function clear() { agentSession = null; set({ turns: [], asking: false }); }

      o = o || {};
      set({ turns: o.turns || [], asking: !!o.asking });
      return { el: el, set: set, ask: ask, clear: clear,
        get turns() { return state.turns; }, get asking() { return state.asking; }, get agentSession() { return agentSession; } };
    }
    AskPane.arrived = arrived;
    NS.AskPane = AskPane;
  })();

  /* --- AskPlanCard --- */
  (function () {
    /* AskPlanCard: one plan from Ask Loadout: every step with why its task was picked, the
       team and the cap, and Approve. Nothing starts until Approve is pressed, and Approve
       stays held while a team or a needed cap is missing. Ported from main's AskPlanCard.svelte. */

    /* A dollar figure as an answer shows it. */
    function usd(amount) {
      return "$" + Number(amount).toLocaleString("en-GB", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    /* Why a plan can't be approved yet, or null when it can. */
    function approveReason(plan, team, cap) {
      if (!plan.steps.some(function (step) { return step.task; })) { return "No task qualified, so there is nothing to start."; }
      if (plan.shape === "team-queue" && !team) { return "Choose a team first."; }
      if (plan.capped) {
        var typed = String(cap).trim().replace(/^\$/, "");
        if (!typed) { return "Give a spending cap first. team-budget is not set, so there is no default."; }
        if (typed.toLowerCase() !== "none" && !(Number(typed) > 0)) { return "The cap is a figure in US dollars above zero, or none."; }
      }
      return null;
    }

    /* The cap the card starts with: team-budget's figure, 'none' when it says so, or blank. */
    function startingCap(plan) {
      return plan.cap !== null && plan.cap !== undefined ? String(plan.cap) : plan.capNone ? "none" : "";
    }

    /* The words of 'loadout team queue run-now' for an approved team plan. */
    function runNowWords(plan, team, cap) {
      var words = [team];
      plan.steps.forEach(function (step) {
        if (step.kind === "queue-task" && step.task && step.project) { words.push("--task", step.project + "/" + step.task); }
      });
      return words.concat(["--usd", String(cap).trim().replace(/^\$/, "")]);
    }

    /* A plan as the command wrote it, with what --json left out filled in. */
    function filled(plan) {
      return {
        shape: plan.shape, team: plan.team || null, teamChoices: plan.teamChoices || [],
        steps: (plan.steps || []).map(function (s) { return { kind: s.kind, project: s.project || null, task: s.task || null, says: s.says || "", reasons: s.reasons || [] }; }),
        capped: !!plan.capped, cap: plan.cap === undefined ? null : plan.cap, capNone: !!plan.capNone, left: plan.left || [], needs: plan.needs || []
      };
    }

    /* Main's HaloButton shows the reason in place of the command when it is held. */
    function halo(o) {
      return NS.HaloButton({ label: o.label, size: o.size, kind: o.kind, cmd: o.disabledReason ? undefined : o.cmd, disabledReason: o.disabledReason, onPress: o.onpress });
    }

    function AskPlanCard(o) {
      o = o || {};
      var plan = filled(o.plan || {});
      var id = uid("ls-ap-");
      var team = plan.team || "", cap = startingCap(plan), busy = false, done = null, failed = null;
      var starts = plan.steps.some(function (step) { return step.kind === "start-team"; });

      function command() {
        if (plan.shape === "terminals") { return "opens " + plan.steps.length + " terminal(s), each with its task"; }
        if (starts) { return "$ loadout team queue run-now " + runNowWords(plan, team || "<team>", cap || "<cap>").join(" "); }
        return "$ loadout team queue add " + (team || "<team>") + " <task> --project <project>, for each";
      }
      function reason() { return done ? "Approved." : busy ? "Starting…" : approveReason(plan, team || null, cap); }

      var el = h("div", { "class": "ls-askplan", role: "group", "aria-labelledby": id + "-h" }, [
        h("h4", { id: id + "-h", text: "Plan: nothing starts until you approve it" }),
        h("ol", {}, plan.steps.map(function (step) {
          return h("li", {}, [h("span", { "class": "says", text: step.says }), step.reasons.length ? h("span", { "class": "why", text: step.reasons.join("; ") }) : null]);
        })),
        plan.left.length ? h("p", { "class": "left", text: "Left out: " + plan.left.join("; ") }) : null
      ]);

      var select = null, input = null, fields = h("div", { "class": "fields" });
      if (plan.shape === "team-queue" && !plan.team) {
        select = h("select", { name: "ask-team", onchange: function () { team = select.value; paint(); } },
          [h("option", { value: "", text: "Choose a team" })].concat(plan.teamChoices.map(function (c) { return h("option", { value: c, text: c }); })));
        select.value = team;
        fields.appendChild(h("label", {}, [h("span", { text: "Team" }), select]));
      }
      if (plan.capped) {
        input = h("input", { name: "ask-cap", inputmode: "decimal", placeholder: plan.cap === null && !plan.capNone ? "Required: team-budget is not set" : "", autocomplete: "off", oninput: function () { cap = input.value; paint(); } });
        input.value = cap;
        fields.appendChild(h("label", {}, [h("span", { text: "Spending cap, US dollars, for the whole plan" }), input]));
      }
      el.appendChild(fields);
      var btnEl = null, doneEl = null, failEl = null;

      function press() {
        busy = true; failed = null; paint();
        var settle = function () { busy = false; paint(); };
        try {
          Promise.resolve(o.approve ? o.approve(plan, team || null, cap) : "Approved.").then(function (said) {
            done = said;
            /* Approve is held from here on, so focus left on it would go nowhere. A refusal leaves it where it is: the button can be pressed again. */
            if (o.onapproved) { o.onapproved(); }
            settle();
          }, function (error) { failed = error && error.message ? error.message : String(error); settle(); });
        } catch (error) { failed = error && error.message ? error.message : String(error); settle(); }
      }

      function paint() {
        var r = reason(), focused = btnEl && btnEl.contains(document.activeElement);
        var next = halo({ label: "Approve", size: "small", cmd: command(), disabledReason: r || undefined, onpress: press });
        if (btnEl) { btnEl.replaceWith(next); } else { el.appendChild(next); }
        btnEl = next;
        if (focused) { (next.querySelector("button") || next).focus(); }
        if (select) { select.disabled = !!done; }
        if (input) { input.disabled = !!done; }
        if (doneEl) { doneEl.remove(); doneEl = null; }
        if (failEl) { failEl.remove(); failEl = null; }
        if (done) { doneEl = el.appendChild(h("p", { "class": "done", role: "status", text: done })); }
        if (failed) { failEl = el.appendChild(h("p", { "class": "failed", role: "alert", text: failed })); }
      }
      paint();

      return { el: el, plan: plan };
    }
    AskPlanCard.usd = usd;
    AskPlanCard.approveReason = approveReason;
    AskPlanCard.startingCap = startingCap;
    AskPlanCard.runNowWords = runNowWords;
    NS.AskPlanCard = AskPlanCard;
  })();

  /* --- BlockedRuns --- */
  (function () {
    /* BlockedRuns: runs that ended short of their done-when, each with what Loadout suggests and why, and every other
       action open to it. Nothing is done until a person confirms: the suggestion is picked first, the person can pick
       another, and the confirm button shows the command it runs. A raise is filled in with the same again, beside what
       the run used, and can be changed before confirming. As main's BlockedRuns.svelte and lib/blocked.ts. */
    function lines(text) { return String(text || "").split(/\r?\n/).map(function (one) { return one.trim(); }).filter(Boolean); }
    function choiceFor(run) {
      return { action: run.suggested.action, answer: "", figure: run.raise ? String(run.raise.cap === "budget" ? Number(run.raise.figure).toFixed(2) : run.raise.figure) : "", title: "", detail: "", doneWhen: "" };
    }
    function notReady(run, choice) {
      if (!(run.offered || []).some(function (one) { return one.action === choice.action; })) { return "that is not open to this run"; }
      if (choice.action === "raise") {
        var figure = Number(choice.figure.trim().replace(/^\$/, ""));
        if (!choice.figure.trim() || !isFinite(figure) || figure <= 0) { return run.raise && run.raise.cap === "rounds" ? "say how many rounds" : "say how much it may spend"; }
        if (run.raise && run.raise.cap === "rounds" && Math.floor(figure) !== figure) { return "rounds are a whole number"; }
      }
      if (choice.action === "edit-requeue" && !choice.title.trim() && !choice.detail.trim() && !lines(choice.doneWhen).length) { return "say what changes: the title, the detail or the done-when"; }
      return null;
    }
    function resolveWords(run, choice) {
      var words = [run.id, "--action", choice.action];
      if (choice.answer.trim()) { words.push("--answer=" + choice.answer.trim()); }
      if (choice.action === "raise" && run.raise) { var figure = choice.figure.trim().replace(/^\$/, ""); words.push(run.raise.cap === "rounds" ? "--rounds=" + figure : "--usd=" + figure); }
      if (choice.action === "edit-requeue") {
        if (choice.title.trim()) { words.push("--title=" + choice.title.trim()); }
        if (choice.detail.trim()) { words.push("--detail=" + choice.detail.trim()); }
        lines(choice.doneWhen).forEach(function (c) { words.push("--done-when=" + c); });
      }
      return words;
    }
    function resolveLine(run, choice) {
      return "$ loadout team blocked resolve " + resolveWords(run, choice).map(function (w) { return /\s/.test(w) ? "\"" + w.replace(/"/g, "\\\"") + "\"" : w; }).join(" ");
    }
    function gotTo(run) {
      var money = "$" + Number(run.cost).toFixed(2) + (run.budget !== null && run.budget !== undefined ? " of $" + Number(run.budget).toFixed(2) : "");
      var branches = (run.merged || []).length || (run.unmerged || []).length ? " · " + run.merged.length + " merged, " + run.unmerged.length + " left on branches" : "";
      return run.rounds + " of " + run.roundLimit + " round(s) · " + money + branches;
    }
    function snapshot(c) { return { action: c.action, answer: c.answer, figure: c.figure, title: c.title, detail: c.detail, doneWhen: c.doneWhen }; }

    function BlockedRuns(o) {
      var cur = Object.assign({ runs: [] }, o || {});
      var choices = {}, said = {}, busy = null, rows = {};
      var el = h("div", { "class": "ls-blocked" });
      var empty = h("p", { "class": "calm", text: "No runs are waiting on you: every run that ended has been dealt with or got there." });
      var ul = h("ul", { "aria-label": "Runs that ended short of their done-when" });
      el.appendChild(empty); el.appendChild(ul);

      function field(lab, input) { return h("div", { "class": "ls-field" }, [h("label", { "class": "lab", "for": input.id, text: lab }), input]); }
      function text(runId, key, idp, area, rows_) {
        var c = choices[runId];
        var input = area ? h("textarea", { id: idp + "-" + runId, "class": "ls-text", rows: String(rows_), spellcheck: "false" }) : h("input", { id: idp + "-" + runId, "class": "ls-text", type: "text", autocomplete: "off", inputmode: key === "figure" ? "decimal" : null });
        input.value = c[key];
        input.addEventListener("input", function () { c[key] = input.value; rows[runId].refresh(); });
        return input;
      }

      function row(run) {
        var li = h("li"), extra = h("div"), answerLab = h("label", { "class": "lab", "for": "answer-" + run.id }), btn = h("div", { "class": "ls-answers" }), out = h("div");
        var r = { li: li, run: run };
        function paintExtra() {
          var c = choices[run.id];
          extra.textContent = "";
          if (c.action === "raise" && run.raise) {
            var rf = field(run.raise.cap === "rounds" ? "Rounds for the next run" : "What the next run may spend ($)", text(run.id, "figure", "raise"));
            rf.appendChild(h("span", { "class": "calm small", text: "The same again. " + (run.raise.cap === "rounds" ? "It used " + run.raise.used + " of " + run.raise.figure + " round(s)." : "It spent $" + Number(run.raise.used).toFixed(2) + " of $" + Number(run.raise.figure).toFixed(2) + ".") }));
            extra.appendChild(rf);
          }
          if (c.action === "edit-requeue") {
            extra.appendChild(field("New title", text(run.id, "title", "title")));
            extra.appendChild(field("New detail", text(run.id, "detail", "detail", true, 3)));
            extra.appendChild(field("Done when, one per line (replaces what it has)", text(run.id, "doneWhen", "done", true, 3)));
          }
          answerLab.textContent = c.action === "carry-on" || c.action === "raise" ? "What to tell the next lead" : "A note, kept on the run";
        }
        function refresh() {
          var c = choices[run.id], why = notReady(run, c);
          btn.textContent = "";
          var reason = cur.disabledReason || (busy === run.id ? "working on it" : (why || undefined));
          /* Main's HaloButton shows the reason in place of the command; the design system's shows the command when both
             are given, so the command goes only to an enabled one. */
          btn.appendChild(NS.HaloButton({ label: "Confirm", size: "small", cmd: reason ? undefined : resolveLine(run, c), disabledReason: reason,
            onPress: function () { confirm(run); } }));
          out.textContent = "";
          if (said[run.id]) { out.appendChild(h("p", { "class": said[run.id].ok ? "ls-said" : "refused", role: "status", text: (said[run.id].ok ? "✓ " : "") + said[run.id].text })); }
        }
        r.refresh = refresh; r.paintExtra = paintExtra;

        var lead = run.lead, ask = null;
        if (lead && (lead.blocker || (lead.questions || []).length)) {
          ask = h("p", { "class": "lead", text: (lead.blocker ? "Blocked on: " + lead.blocker + (lead.unblockedBy ? ". It said this would unblock it: " + lead.unblockedBy : "") + "." : "") + (lead.questions || []).map(function (qn) { return " Asked: " + qn; }).join("") });
        }
        var suggested = run.kind === "handover"
          ? h("p", { "class": "suggested" }, [h("b", { text: "It handed over what only you can do." }), " " + run.suggested.reason + " ", h("a", { href: "#teams/" + encodeURIComponent(run.id), text: "Tick them off on its page" }), "."])
          : h("p", { "class": "suggested" }, [h("b", { text: "Suggested: " + run.suggested.label + "." }), " " + run.suggested.reason]);
        var fs = h("fieldset", {}, [h("legend", { text: "What to do" })].concat((run.offered || []).map(function (one) {
          var radio = h("input", { type: "radio", name: "blocked-" + run.id, value: one.action });
          radio.checked = choices[run.id].action === one.action;
          radio.addEventListener("change", function () { if (radio.checked) { choices[run.id].action = one.action; paintExtra(); refresh(); } });
          return h("label", { "class": "pick" }, [radio, " " + one.label + (one.action === run.suggested.action ? " (suggested)" : "")]);
        })));
        var answer = h("textarea", { id: "answer-" + run.id, "class": "ls-text", rows: "2", spellcheck: "false" });
        answer.value = choices[run.id].answer;
        answer.addEventListener("input", function () { choices[run.id].answer = answer.value; refresh(); });

        [h("div", { "class": "head" }, [h("b", { text: run.id }), h("span", { text: run.team + (run.project ? " on " + run.project : "") + (run.task ? ", task " + run.task : "") }), h("span", { "class": "kind", text: run.kind })]),
          h("p", { "class": "goal", text: run.goal }),
          h("p", { "class": "calm small", text: (run.ended || (run.kind === "dead" ? "Its process went away while its journal still said it was running." : "It did not say how it ended.")) + " · " + gotTo(run) + (run.origin ? " · started by rule " + run.origin : "") + (run.chain > 1 ? " · run " + run.chain + " in a row on this task" : "") }),
          ask, suggested, fs, extra, h("div", { "class": "ls-field" }, [answerLab, answer]), btn, out
        ].forEach(function (k) { if (k) { li.appendChild(k); } });
        paintExtra(); refresh();
        return r;
      }

      function confirm(run) {
        busy = run.id; rows[run.id].refresh();
        var finish = function (ok, t) { said[run.id] = { ok: ok, text: t }; busy = null; if (rows[run.id]) { rows[run.id].refresh(); } };
        var got;
        try { got = cur.onresolve ? cur.onresolve(run, snapshot(choices[run.id])) : ""; } catch (e) { finish(false, e && e.message ? e.message : String(e)); return; }
        Promise.resolve(got).then(function (t) { finish(true, t === undefined || t === null ? "" : String(t)); }, function (e) { finish(false, e && e.message ? e.message : String(e)); });
      }

      function paint() {
        var runs = cur.runs || [];
        empty.hidden = !!runs.length;
        var keep = {};
        runs.forEach(function (run) {
          if (!choices[run.id]) { choices[run.id] = choiceFor(run); }
          var have = rows[run.id];
          if (have && have.run !== run) { var focused = document.activeElement && have.li.contains(document.activeElement) ? document.activeElement.id || null : null; have = row(run); rows[run.id] = have; have.focusId = focused; }
          keep[run.id] = have || (rows[run.id] = row(run));
        });
        Object.keys(rows).forEach(function (k) { if (!keep[k]) { delete rows[k]; } });
        ul.textContent = "";
        runs.forEach(function (run) { ul.appendChild(rows[run.id].li); });
        runs.forEach(function (run) { var r = rows[run.id]; if (r.focusId) { var f = document.getElementById(r.focusId); if (f) { f.focus(); } r.focusId = null; } else { r.refresh(); } });
      }
      paint();
      return { el: el, set: function (patch) { cur = Object.assign({}, cur, patch || {}); paint(); } };
    }
    NS.BlockedRuns = BlockedRuns;
  })();

  /* --- ContextMark --- */
  (function () {
    /* ContextMark: how much of a session's context window is in use, small enough for a tab or a list row: the figure
       and a short beam, turning to Needs as it fills and to Bad when it is nearly full. Nothing here is announced as it
       changes; the strip on the terminal says when a threshold is crossed. Ported from main's ContextMark.svelte. */
    var WARN = 70, URGENT = 90;
    function levelOf(p) { if (p === null || p === undefined || !isFinite(p)) { return "ok"; } return p >= URGENT ? "urgent" : p >= WARN ? "warn" : "ok"; }
    function levelWords(l) { return l === "urgent" ? "nearly full" : l === "warn" ? "filling" : ""; }
    function ContextMark(o) {
      o = o || {};
      var el = h("span", { "class": "ls-ctx" });
      function set(percent) {
        el.textContent = "";
        if (percent === null || percent === undefined) {
          el.hidden = true; el.className = "ls-ctx"; el.removeAttribute("role"); el.removeAttribute("aria-label"); el.removeAttribute("title");
          return;
        }
        var level = levelOf(percent), words = levelWords(level), said = "Context " + percent + "% used" + (words ? ", " + words : "");
        el.hidden = false;
        el.className = "ls-ctx " + level;
        el.setAttribute("role", "img"); el.setAttribute("aria-label", said); el.setAttribute("title", said);
        var fill = h("i"); fill.style.width = Math.min(100, Math.max(0, percent)) + "%";
        el.appendChild(h("i", { "class": "beam", "aria-hidden": "true" }, [fill]));
        el.appendChild(h("b", { "aria-hidden": "true", text: percent + "%" }));
        if (level === "urgent") { el.appendChild(h("span", { "class": "bang", "aria-hidden": "true", text: "!" })); }
      }
      set(o.percent === undefined ? null : o.percent);
      return { el: el, set: set };
    }
    NS.ContextMark = ContextMark;
  })();

  /* --- ConversationList --- */
  (function () {
    /* ConversationList: past conversations from both agents, each with what it was launched
       for and whether it is running, and the way to carry it on. Shared by Sessions, which
       owns the history, and a project's page, which shows its own. Ported from main's
       ConversationList.svelte. */

    /* The daemon's terminal already running a conversation, or null. */
    function hostedIn(running) {
      running = running || "";
      return running.indexOf("hosted:") === 0 ? running.slice(7) || null : null;
    }

    /* What carries over into a resume, as somebody would say it. */
    function carriedWords(c) {
      var l = c.launchedFor;
      if (!l) { return "nothing carried over: no launch on record"; }
      var parts = [l.task ? "task ‘" + l.task + "’" : null, l.mode ? "mode " + l.mode : null, l.profile ? "profile " + l.profile : null, l.worktree ? "worktree " + l.worktree : null].filter(Boolean);
      return parts.length ? "carries over " + parts.join(", ") : "launched with nothing to carry over";
    }

    /* The terminal command a resume runs, shown beneath the button. */
    function resumeWords(c) {
      var words = ["$ loadout resume", c.id];
      if (c.project) { words.push("--project " + c.project); }
      return words.join(" ");
    }

    function halo(o) {
      return NS.HaloButton({ label: o.label, size: o.size, cmd: o.disabledReason ? undefined : o.cmd, disabledReason: o.disabledReason, onPress: o.onpress });
    }

    function ConversationList(o) {
      o = Object.assign({ conversations: [], label: "Past conversations", inProject: false }, o || {});
      var busy = null;
      var el = h("div", { "class": "ls-convlist" });

      function now() { return o.now === undefined ? Date.now() : (typeof o.now === "number" ? o.now : Date.parse(o.now)); }
      function when(at) {
        var minutes = Math.max(0, Math.round((now() - Date.parse(at)) / 60000));
        return minutes < 1 ? "just now" : minutes < 60 ? minutes + " min ago" : minutes < 60 * 24 ? Math.round(minutes / 60) + " h ago" : Math.round(minutes / 1440) + " d ago";
      }
      function where(c) {
        return [o.inProject ? null : (c.project || "not a project · " + c.directory), c.agent, c.branch, when(c.lastActive)].filter(Boolean).join(" · ");
      }
      function running(c) {
        return hostedIn(c.running) ? "open in Studio" : c.running === "elsewhere" ? "looks to be running elsewhere" : null;
      }
      /* Why nothing can be started from here: watch-only, or a daemon that cannot open a terminal. */
      function unable() {
        return o.watchOnly ? (o.watchOnlyReason || "Studio is only watching: type the command in a terminal") : o.startsSessions === false ? "This daemon cannot open a terminal here" : undefined;
      }

      function resume(c) {
        busy = c.id; paint();
        var done = function (id) { busy = null; paint(); if (id && o.onopen) { o.onopen(id, true); } };
        try {
          Promise.resolve(o.onresume ? o.onresume(c) : null).then(done, function () { done(null); });
        } catch (e) { done(null); }
      }

      function row(c) {
        var there = hostedIn(c.running), r = running(c), stop = unable(), acts;
        if (there) {
          acts = [halo({ label: "Open", size: "small", cmd: "goes to its terminal", onpress: function () { if (o.onopen) { o.onopen(there, false); } } })];
        } else {
          acts = [halo({ label: c.registered ? "Resume" : "Register and resume", size: "small", cmd: busy === c.id ? "starting…" : resumeWords(c), disabledReason: stop || (busy ? "one at a time" : undefined), onpress: function () { resume(c); } })];
          if (o.onchanges && c.registered) {
            acts.push(h("button", { type: "button", "class": "linkish", disabled: !!stop, title: stop || null, onclick: function () { o.onchanges(c); }, text: "Resume with changes…" }));
          }
        }
        return h("li", { "class": "conv" }, [
          h("div", { "class": "what" }, [
            h("span", { "class": "t", text: c.title }),
            h("span", { "class": "x", text: where(c) }),
            h("span", { "class": "x carried", text: carriedWords(c) + (r ? " · " + r : "") })
          ]),
          h("div", { "class": "acts" }, acts)
        ]);
      }

      function paint() {
        var key = document.activeElement && el.contains(document.activeElement) ? document.activeElement.closest("li") : null;
        var at = key ? Array.prototype.indexOf.call(key.parentNode.children, key) : -1;
        var which = key ? Array.prototype.indexOf.call(key.querySelectorAll("button"), document.activeElement) : -1;
        el.textContent = "";
        if (o.conversations.length) {
          el.appendChild(h("ul", { "class": "slist convs", "aria-label": o.label }, o.conversations.map(row)));
        } else {
          el.appendChild(h("p", { "class": "calm", text: "No conversation from either agent is on record here yet." }));
        }
        if (at >= 0) { var li = el.querySelectorAll("li.conv")[at]; var b = li && li.querySelectorAll("button")[which]; if (b) { b.focus(); } }
      }

      function set(next) { Object.assign(o, next || {}); paint(); }
      paint();
      return { el: el, set: set };
    }
    ConversationList.carriedWords = carriedWords;
    ConversationList.resumeWords = resumeWords;
    ConversationList.hostedIn = hostedIn;
    NS.ConversationList = ConversationList;
  })();

  /* --- Handover --- */
  (function () {
    /* Handover: what a run handed to you because only you could do it: a push, a CI run or a check by hand. Each is
       ticked off here once it is done, which the journal keeps as review evidence in your name. The last tick judges
       the run again, with no turn for the lead. As main's Handover.svelte. */
    var NEEDS = { "needs-push": "a push", "needs-ci": "a CI run", "needs-person": "your check" };
    function checkLine(run, number) { return "$ loadout team handover " + run + " --check " + number; }
    function Handover(o) {
      var cur = Object.assign({ items: [], branches: [] }, o || {}), sending = null;
      var id = uid("ls-handover-");
      var el = h("section", { "class": "ls-gate handover", role: "group", "aria-labelledby": id });
      function press(number) {
        if (sending !== null) { return; }
        sending = number; paint();
        var done = function () { sending = null; paint(); };
        var r;
        try { r = cur.oncheck ? cur.oncheck(number) : undefined; } catch (e) { done(); throw e; }
        if (r && typeof r.then === "function") { r.then(done, done); } else { done(); }
      }
      function paint() {
        var items = cur.items || [], open = items.filter(function (one) { return !one.checkedBy; }).length;
        var keep = document.activeElement && el.contains(document.activeElement) ? document.activeElement.getAttribute("data-k") : null;
        el.textContent = "";
        el.appendChild(h("span", { "class": "who", text: "handed over · " + (open ? open + " of " + items.length + " still yours" : "all checked") }));
        el.appendChild(h("p", { "class": "q", id: id, text: "It handed these over: only you can do them." }));
        el.appendChild(h("ol", { "class": "readings" }, items.map(function (one, index) {
          var act;
          if (one.checkedBy) { act = h("span", { "class": "m", text: "✓ checked by " + one.checkedBy }); }
          else {
            var reason = cur.disabledReason || (sending !== null ? "ticking one off" : undefined);
            /* The reason in place of the command, as main's HaloButton draws it. */
            act = NS.HaloButton({ label: "Done: tick it off", size: "small",
              cmd: reason ? undefined : checkLine(cur.run, index + 1) + (open === 1 ? " · the last one, so it is judged again" : ""),
              disabledReason: reason,
              onPress: function () { press(index + 1); } });
            var b = act.tagName === "BUTTON" ? act : act.querySelector("button"); if (b) { b.setAttribute("data-k", "h" + index); }
          }
          return h("li", {}, [
            h("span", { "class": "c" }, [one.criterion + " ", h("span", { "class": "ls-chip", text: "needs " + (NEEDS[one.needs] || one.needs) })]),
            h("span", { "class": "m", text: one.todo }),
            act
          ]);
        })));
        if ((cur.branches || []).length) { el.appendChild(h("p", { "class": "ls-note", text: "The work is on " + cur.branches.join(", ") + "." })); }
        if (keep) { var f = el.querySelector('[data-k="' + keep + '"]') || el.querySelector("[data-k]"); if (f) { f.focus(); } }
      }
      paint();
      return { el: el, set: function (patch) { cur = Object.assign({}, cur, patch || {}); paint(); } };
    }
    NS.Handover = Handover;
  })();

  /* --- JobResult --- */
  (function () {
    /* JobResult: one command run from Studio: how it stands, the line it ran, and what it said once it ends. Main
       reads the job again whenever the job list changes, which is how the output arrives; here the page passes the
       job in and calls update(job) as it changes. Ported from main's JobResult.svelte. */
    /* The output as data when it is data, so it reads as a structure rather than one long line. */
    function pretty(output) { try { return JSON.stringify(JSON.parse(output), null, 2); } catch (e) { return output; } }
    function stateWord(state) { return state === "cancelled" ? "stopped" : state; }
    function when(at) { return new Date(at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }); }
    function seconds(one) { return one.endedAt ? Math.max(0, Math.round((Date.parse(one.endedAt) - Date.parse(one.startedAt)) / 1000)) : null; }

    function JobResult(o) {
      o = o || {};
      var job = o.job || null, problem = o.problem || null;
      /* Main draws the problem and the section side by side in its parent; this holds both without a box of its own. */
      var el = h("div", { "class": "ls-jobresult" });
      function paint() {
        el.textContent = "";
        if (problem) { el.appendChild(h("p", { "class": "offline", role: "alert", text: problem })); }
        if (!job) { return; }
        var sec = h("section", { "class": "jobresult", "aria-label": "Result of " + job.path });
        if (!o.compact) { sec.appendChild(h("h2", { "class": "topicname", text: job.path })); }
        var took = seconds(job);
        sec.appendChild(h("div", { "class": "jobhead" }, [NS.Signal(stateWord(job.state)),
          h("span", { "class": "calm small", text: "started " + when(job.startedAt) + (took !== null ? " · took " + took + "s" : "") + (job.exitCode !== null && job.exitCode !== undefined ? " · exit code " + job.exitCode : "") })]));
        if (!o.compact) { sec.appendChild(NS.Command({ command: "$ " + job.line })); }
        if (job.state === "running") {
          sec.appendChild(h("div", { "class": "ls-answers flush" }, [NS.HaloButton({ label: "Cancel", kind: "danger", size: "small", cmd: "stops it and anything it started", disabledReason: o.disabledReason || undefined,
            onPress: function () { if (o.oncancel) { var r = o.oncancel(job); if (r && r.then) { r.then(null, function (err) { set({ problem: (err && err.message) || String(err) }); }); } } } })]));
          sec.appendChild(h("p", { "class": "calm", role: "status", text: "Running. What it says appears here when it ends." }));
        } else {
          if (job.output) {
            sec.appendChild(h("h3", { "class": "ls-zone", text: "What it said" }));
            /* Focusable so a keyboard can scroll it: output wider than the page scrolls inside its box, not the page. */
            sec.appendChild(h("pre", { "class": "output", tabindex: "0", "aria-label": "What it said", text: pretty(job.output) }));
          } else {
            sec.appendChild(h("p", { "class": "calm", text: "It said nothing on standard output." }));
          }
          if (job.error) {
            sec.appendChild(h("h3", { "class": "ls-zone", text: "On standard error" }));
            sec.appendChild(h("pre", { "class": "output err", tabindex: "0", "aria-label": "On standard error", text: job.error }));
          }
          if (job.truncated) { sec.appendChild(h("p", { "class": "calm small", text: "It said more than Studio keeps; the end was left out." })); }
          if (o.onagain) {
            sec.appendChild(h("div", { "class": "ls-answers flush" }, [NS.HaloButton({ label: "Run it again", size: "small", cmd: "opens its form", onPress: function () { o.onagain(job); } })]));
          }
        }
        el.appendChild(sec);
      }
      function set(p) {
        p = p || {};
        if (p.job !== undefined) { job = p.job; }
        if (p.problem !== undefined) { problem = p.problem; }
        ["compact", "disabledReason", "onagain", "oncancel"].forEach(function (k) { if (p[k] !== undefined) { o[k] = p[k]; } });
        paint();
      }
      paint();
      return { el: el, set: set, update: function (next) { set({ job: next }); } };
    }
    NS.JobResult = JobResult;
  })();

  /* --- Journal --- */
  (function () {
    /* Journal: a run's journal or a node's stream, one line each: when, what kind, what was said, and how many times a
       repeat came round. As main's Journal.svelte, with its JournalLine shape ({ time, text, family, kind, sub, repeats }). */
    function line(l) {
      var cls = [l.family || "", l.sub ? "sub" : ""].filter(Boolean).join(" ") || null;
      return h("li", { "class": cls }, [
        h("span", { "class": "tm", text: l.time || "" }),
        h("span", { "class": "tx" }, [l.kind ? h("span", { "class": "k", text: l.kind }) : null, l.text || "", l.repeats ? h("span", { "class": "x", text: "×" + l.repeats }) : null])
      ]);
    }
    function Journal(o) {
      o = o || {};
      var el = h("ol", { "class": "log", tabindex: "0", "aria-label": o.label || "Journal" });
      function set(patch) {
        patch = patch || {};
        if (patch.label !== undefined) { el.setAttribute("aria-label", patch.label || "Journal"); }
        if (patch.lines) { el.textContent = ""; patch.lines.forEach(function (l) { el.appendChild(line(l)); }); }
      }
      set({ lines: o.lines || [] });
      return { el: el, set: set };
    }
    NS.Journal = Journal;
  })();

  /* --- Landings --- */
  (function () {
    /* Landings: the branches waiting to land: a queued run's own branch with its pull request, and each node branch
       its gate did not merge, pending. Each can be opened, marked ready, or squash-merged with a message the person
       writes, through the same commands as the terminal. As main's Landings.svelte. */

    /* The words for 'loadout pr merge', leaving out a body nobody wrote. Joined with '=' so a message that starts with
       a dash is still read as the value. */
    function mergeWords(one, subject, body) {
      var words = [one.branch, "--project", one.project, "--subject=" + String(subject).trim()];
      return String(body).trim() ? words.concat(["--body=" + String(body).trim()]) : words;
    }
    function keyOf(one) { return one.project + " " + one.branch; }
    function at(one) { return [one.branch, "--project", one.project]; }
    function waited(created, now) {
      var hours = (now.getTime() - new Date(created).getTime()) / 3600000;
      if (hours < 1) { return "under an hour"; }
      var count = hours < 24 ? Math.floor(hours) : Math.floor(hours / 24), unit = hours < 24 ? "hour" : "day";
      return count === 1 ? "1 " + unit : count + " " + unit + "s";
    }
    function from(one) { return one.node ? one.node + " in run " + one.run : one.run ? "run " + one.run : "no run"; }

    function Landings(o) {
      var cur = Object.assign({ landings: [] }, o || {});
      /* Kept here rather than on the row, so a refresh of the list every few seconds does not throw away a message half written. */
      var merging = null, subject = "", body = "", pre = uid("lm");
      var el = h("div", { "class": "ls-landings" });
      var list = h("div");
      el.appendChild(list);
      /* Main's line is a calm small p with a code.ls-cmd; the design system's From line is .ls-from, which follows the commands setting. */
      el.appendChild(h("p", { "class": "ls-from" }, ["From ", h("code", { text: "$ loadout pr list" }), "."]));

      function act(path, words, done) { return cur.act ? cur.act(path, words, done) : undefined; }
      /* An act may return a promise: the pressed button says what it is doing ("opening…", "marking…", "merging…") until it
         settles, and false, a throw or a rejection says "✕ why" in the status line under the list. */
      var said = h("p", { "class": "ls-said", role: "status" });
      el.appendChild(said);
      function working(e, word, path, words, done) {
        var b = e && e.currentTarget && e.currentTarget.closest ? e.currentTarget.closest("button") : null;
        said.textContent = ""; said.classList.remove("bad");
        var r;
        try { r = act(path, words, done); } catch (x) { r = Promise.reject(x); }
        if (!r || typeof r.then !== "function") { if (r === false) { failed(); } return r; }
        if (b) { NS.HaloButton.setBusy(b, word); }
        function back() { if (b && b.isConnected) { NS.HaloButton.setBusy(b, false); } }
        function failed(x) { said.textContent = "\u2715 " + ((x && x.message) || (typeof x === "string" && x) || "It did not go through. Nothing changed."); said.classList.add("bad"); }
        return r.then(function (ok) { back(); if (ok === false) { failed(); } return ok; }, function (x) { back(); failed(x); return false; });
      }
      function btn(label, cmd, press, extra) {
        var reason = extra && extra.reason !== undefined ? extra.reason : cur.disabledReason;
        /* Main's HaloButton shows the reason in place of the command; the design system's shows the command when both
           are given, so the command goes only to an enabled one. */
        return NS.HaloButton({ label: label, size: extra && extra.solid ? undefined : "small", solid: !!(extra && extra.solid), cmd: reason ? undefined : cmd, disabledReason: reason || undefined, onPress: press });
      }
      function subjectReason() { return !subject.trim() ? "Write the squash commit’s subject" : subject.indexOf("\n") >= 0 ? "The subject is one line" : undefined; }

      function merge(one, e) {
        Promise.resolve(working(e, "merging\u2026", "pr merge", mergeWords(one, subject, body), one.branch + " merges once its checks pass")).then(function (ok) {
          if (ok !== false) { merging = null; paint(); }
        });
      }

      function form(one, index) {
        var sid = pre + "-subject-" + index, bid = pre + "-body-" + index, slot = h("div");
        var si = h("input", { id: sid, "class": "ls-text", placeholder: "One line, as git log will show it" });
        var bi = h("textarea", { id: bid, "class": "ls-text", rows: "4", placeholder: "Left empty, the commit has no body" });
        si.value = subject; bi.value = body;
        function go() { slot.textContent = ""; slot.appendChild(btn("Squash-merge when checks pass", "$ loadout pr merge " + one.branch + " --subject …", function (e) { merge(one, e); }, { solid: true, reason: cur.disabledReason || subjectReason() })); }
        si.addEventListener("input", function () { subject = si.value; go(); });
        bi.addEventListener("input", function () { body = bi.value; });
        var f = h("form", { "class": "lmerge", "aria-label": "Squash-merge " + one.branch }, [
          h("div", { "class": "ls-field" }, [h("label", { "class": "lab", "for": sid, text: "Subject" }), si]),
          h("div", { "class": "ls-field" }, [h("label", { "class": "lab", "for": bid, text: "Body" }), bi]),
          slot
        ]);
        f.addEventListener("submit", function (e) { e.preventDefault(); if (!cur.disabledReason && !subjectReason()) { merge(one, { currentTarget: slot.querySelector("button") }); } });
        go();
        return f;
      }

      function row(one, index) {
        var now = cur.now || new Date();
        var li = h("li", { "class": "landing" }, [
          h("span", { "class": "lstate ls-chip", text: one.state }),
          h("code", { "class": "lbranch", text: one.branch }),
          one.gone ? h("span", { "class": "lgone", text: "gone from the repository" }) : null,
          h("p", { "class": "calm small", text: one.project + ", " + from(one) + (one.task ? ", task " + one.task : "") + ", waiting " + waited(one.created, now) }),
          one.pullRequest ? h("p", { "class": "small" }, [h("a", { href: one.pullRequest, target: "_blank", rel: "noreferrer", text: one.pullRequest })]) : null,
          one.mergeRequested ? h("p", { "class": "calm small", text: "Merges once its required checks pass." }) : null
        ]);
        if (cur.act) {
          var acts = h("div", { "class": "lacts" }), k = keyOf(one);
          if (one.state === "pending" && !one.gone) {
            acts.appendChild(btn("Open as a draft", "$ loadout pr open " + one.branch + " --draft", function (e) { working(e, "opening\u2026", "pr open", at(one).concat(["--draft"]), one.branch + " has a draft pull request"); }));
            acts.appendChild(btn("Open for review", "$ loadout pr open " + one.branch, function (e) { working(e, "opening\u2026", "pr open", at(one), one.branch + " has a pull request"); }));
          } else if (one.state === "draft" && one.pullRequest) {
            acts.appendChild(btn("Mark ready", "$ loadout pr ready " + one.branch, function (e) { working(e, "marking\u2026", "pr ready", at(one), one.branch + " is ready for review"); }));
          } else if (one.state === "ready" && one.pullRequest && !one.mergeRequested) {
            acts.appendChild(btn(merging === k ? "Not now" : "Merge…", merging === k ? undefined : "opens the squash message for " + one.branch, function () {
              if (merging === k) { merging = null; } else { merging = k; subject = ""; body = ""; }
              paint();
            }));
          }
          li.appendChild(acts);
          if (merging === k && one.state === "ready") { li.appendChild(form(one, index)); }
        }
        return li;
      }

      function paint() {
        var all = cur.landings || [];
        /* A refresh keeps the field being typed in focused. */
        var focused = document.activeElement && el.contains(document.activeElement) ? document.activeElement.id : null;
        list.textContent = "";
        if (!all.length) { list.appendChild(h("p", { "class": "calm", text: "Nothing is waiting to land." })); return; }
        var ul = h("ul", { "class": "landings" });
        all.forEach(function (one, i) { ul.appendChild(row(one, i)); });
        list.appendChild(ul);
        if (focused) { var f = document.getElementById(focused); if (f) { f.focus(); } }
      }
      paint();
      return { el: el, set: function (patch) { cur = Object.assign({}, cur, patch || {}); paint(); } };
    }
    Landings.mergeWords = mergeWords;
    NS.Landings = Landings;
  })();

  /* --- LayoutEdit --- */
  (function () {
    /* LayoutEdit: Edit layout for any page or mode surface, generalised from main's Home (pages/Home.svelte, pages/Panel.svelte and
       lib/layout.ts). A page's sections are shown in their saved order, hidden ones left out. "Edit layout" in the page's head opens
       the edit bar: drag a section by its name or use its arrows, Hide it, Add it back, Reset to the default, Done. A section that holds
       live work (locked) can be moved but not hidden while it has something in it.
       Every layout is kept in one setting, studio-layouts: a JSON object keyed "<page>" or "<page>/<surface>", then the mode, each
       { order, hidden }. "" (for the whole setting or one entry) is the default. Sizes are not kept here; they stay in the browser. */
    var KEY = "studio-layouts";
    var MODES = ["default", "terminals", "teams", "minimal", "pure"];

    function uniq(list) { return list.filter(function (one, i) { return list.indexOf(one) === i; }); }
    /* The layout a saved value describes, mended: unknown panels dropped, missing ones added at the end. As main's readLayout. */
    function readLayout(saved, panels) {
      var parsed = {};
      if (saved && typeof saved === "object") { parsed = saved; }
      else { try { parsed = saved ? JSON.parse(saved) : {}; } catch (e) { parsed = {}; } }
      if (!parsed || typeof parsed !== "object") { parsed = {}; }
      function known(list) { return Array.isArray(list) ? list.filter(function (one) { return typeof one === "string" && panels.indexOf(one) >= 0; }) : []; }
      var order = uniq(known(parsed.order));
      panels.forEach(function (p) { if (order.indexOf(p) < 0) { order.push(p); } });
      return { order: order, hidden: uniq(known(parsed.hidden)) };
    }
    /* Moves a panel by `by` places among all of them, hidden ones included. */
    function move(layout, panel, by) {
      var order = layout.order.slice(), from = order.indexOf(panel), to = Math.max(0, Math.min(order.length - 1, from + by));
      if (from < 0 || from === to) { return layout; }
      order.splice(from, 1); order.splice(to, 0, panel);
      return { order: order, hidden: layout.hidden.slice() };
    }
    /* Puts `panel` where `before` is, for a drop. */
    function place(layout, panel, before) {
      if (panel === before) { return layout; }
      var order = layout.order.filter(function (one) { return one !== panel; });
      order.splice(Math.max(0, order.indexOf(before)), 0, panel);
      return { order: order, hidden: layout.hidden.slice() };
    }
    /* Puts `panel` just before or just after `target`, for a drop that knows which half of the target it landed on. place()
       alone can't move a section past the next one, or to the end: dropping on the section below put it back where it was. */
    function drop(layout, panel, target, after) {
      if (panel === target) { return layout; }
      var order = layout.order.filter(function (one) { return one !== panel; }), at = order.indexOf(target);
      if (at < 0) { return layout; }
      order.splice(at + (after ? 1 : 0), 0, panel);
      return { order: order, hidden: layout.hidden.slice() };
    }
    function hide(layout, panel) { return { order: layout.order.slice(), hidden: uniq(layout.hidden.concat([panel])) }; }
    function show(layout, panel) { return { order: layout.order.slice(), hidden: layout.hidden.filter(function (one) { return one !== panel; }) }; }
    /* What Loadout keeps for one layout; the default keeps nothing, so a reset unsets it. */
    function saved(layout, panels) {
      var plain = !layout.hidden.length && layout.order.every(function (one, i) { return one === panels[i]; });
      return plain ? "" : JSON.stringify({ order: layout.order, hidden: layout.hidden });
    }

    function keyOf(page, surface) { return surface ? page + "/" + surface : page; }
    /* The whole setting as an object: {} for "" or anything that is not one. */
    function parseAll(value) {
      if (value && typeof value === "object") { return value; }
      try { var v = value ? JSON.parse(value) : {}; return v && typeof v === "object" && !Array.isArray(v) ? v : {}; } catch (e) { return {}; }
    }
    /* One page-and-mode's layout out of the whole setting. */
    function readFrom(value, key, mode, panels) {
      var all = parseAll(value), entry = all[key];
      return readLayout(entry && typeof entry === "object" ? entry[mode || "default"] : "", panels);
    }
    /* The whole setting with one page-and-mode's layout written in; a default layout takes its entry out, and nothing left is "". */
    function writeTo(value, key, mode, layout, panels) {
      var all = JSON.parse(JSON.stringify(parseAll(value))), m = mode || "default", one = saved(layout, panels);
      var entry = all[key] && typeof all[key] === "object" ? all[key] : {};
      if (one) { entry[m] = { order: layout.order, hidden: layout.hidden }; } else { delete entry[m]; }
      if (Object.keys(entry).length) { all[key] = entry; } else { delete all[key]; }
      return Object.keys(all).length ? JSON.stringify(all) : "";
    }
    /* The line that writes it, quoted for a shell: '…' around the JSON, "" for the default. */
    function command(value) {
      return "$ loadout config set " + KEY + " " + (value ? "'" + String(value).replace(/'/g, "'\\''") + "'" : "\"\"");
    }

    function LayoutEdit(o) {
      var cur = Object.assign({ mode: "default", editing: false, numbered: false, hideOnly: false }, o || {});
      var sections = (cur.sections || []).map(function (s) { return typeof s === "string" ? { id: s, name: (cur.names || {})[s] || s } : s; });
      var names = {}; sections.forEach(function (s) { names[s.id] = s.name || (cur.names || {})[s.id] || s.id; });
      var panels = (cur.order || sections.map(function (s) { return s.id; })).filter(function (id) { return names[id] !== undefined; });
      sections.forEach(function (s) { if (panels.indexOf(s.id) < 0) { panels.push(s.id); } });
      var key = keyOf(cur.page || "page", cur.surface), value = cur.value || "", dragging = null, bodies = {}, pending = false;
      var el = h("div", { "class": "ls-layout" + (cur.class ? " " + cur.class : "") });
      var bar = h("div", { "class": "editbar ls-layout-bar", role: "region", "aria-label": "Editing the layout", hidden: true });
      var slot = h("span", { "class": "ls-layout-btn" });
      var told = h("p", { "class": "ls-sr", role: "status" });

      function layout() {
        /* Until studio-layouts is written at all, an older key (Home's studio-layout-lens and -map) is read in its place. */
        if (!value && cur.legacy) { return readLayout(cur.legacy, panels); }
        return readFrom(value, key, cur.mode, panels);
      }
      function locked(id) { return !!(cur.locked && cur.locked(id)); }
      /* A hidden section that has live work in it again is shown: live work is never hidden. */
      function hiddenNow(L, id) { return L.hidden.indexOf(id) >= 0 && !locked(id); }
      function keep(next) {
        var v = writeTo(value, key, cur.mode, next, panels);
        value = v;
        paint();
        if (cur.onchange) { cur.onchange(KEY, v, command(v)); }
      }
      function edit(on) { cur.editing = !!on; paint(); if (cur.onedit) { cur.onedit(cur.editing); } }
      function bodyOf(id) {
        if (!(id in bodies)) { var b = cur.render ? cur.render(id) : null; bodies[id] = b; }
        return bodies[id];
      }

      function panelFor(L, id, position) {
        var name = names[id], editing = cur.editing, first = position === 0, last = position === L.order.length - 1;
        var wide = typeof cur.wide === "function" ? cur.wide(id) : Array.isArray(cur.wide) ? cur.wide.indexOf(id) >= 0 : false;
        var sec = h("section", { "class": "panel p-" + id + (wide ? " wide" : "") + (editing ? " editing" : "") + (dragging === id ? " dragging" : ""), "aria-labelledby": "z-" + key.replace(/\W/g, "-") + "-" + id, "data-section": id });
        if (editing) {
          var kids = [];
          if (cur.hideOnly) { kids.push(h("span", { "class": "grip still" }, [name])); }
          else {
            var grip = h("span", { "class": "grip", "aria-hidden": "true" }, [h("span", { text: "⠿" }), name]);
            var nudge = function (word, glyph, by, off) { return h("button", { type: "button", "aria-label": "Move " + name + " " + word, disabled: off, text: glyph, onclick: function () { keep(move(L, id, by)); } }); };
            kids.push(grip, h("span", { "class": "nudge", role: "group", "aria-label": "Move " + name }, [nudge("earlier", "←", -1, first), nudge("up", "↑", -1, first), nudge("down", "↓", 1, last), nudge("later", "→", 1, last)]));
          }
          if (locked(id)) {
            var wid = uid("ls-lay-why-");
            kids.push(h("span", { "class": "hidewrap" }, [h("button", { type: "button", "class": "hidebtn", "aria-disabled": "true", "aria-describedby": wid, text: "Hide" }), h("span", { "class": "why", id: wid, text: "holds live work, so it stays while it has something in it" })]));
          } else {
            kids.push(h("button", { type: "button", "class": "hidebtn", text: "Hide", onclick: function () { keep(hide(L, id)); } }));
          }
          var chrome = h("div", { "class": "chrome" + (cur.hideOnly ? "" : " drags") }, kids);
          if (!cur.hideOnly) { chrome.addEventListener("pointerdown", function (e) { startDrag(e, L, id, sec, chrome); }); }
          sec.appendChild(chrome);
        }
        var count = cur.count ? cur.count(id) : undefined;
        var number = cur.numbered ? (panels.indexOf(id) + 1 < 10 ? "0" : "") + (panels.indexOf(id) + 1) : "";
        sec.appendChild(h("h2", { "class": cur.quiet && !editing ? "ls-sr" : "ls-zone", id: "z-" + key.replace(/\W/g, "-") + "-" + id }, [number ? h("span", { "class": "no", text: number }) : null, name + (count === undefined || count === null ? "" : " · " + count)]));
        var body = bodyOf(id);
        (Array.isArray(body) ? body : [body]).forEach(function (b) { if (b) { sec.appendChild(b); } });
        return sec;
      }

      /* Dragging is by pointer, so a mouse, a pen and a finger all work: press anywhere on a section's bar but its buttons, and move.
         A line shows where it will land: before or after the section nearest the pointer, by which half of it the pointer is over
         (left or right when sections sit side by side). Letting go puts it there; Escape puts it back. The page doesn't redraw
         the sections meanwhile (paint waits), and data-dragging on the container tells a page that redraws by itself to wait too. */
      function startDrag(e, L, id, sec, handle) {
        if ((e.button && e.button > 0) || (e.target.closest && e.target.closest("button"))) { return; }
        var x0 = e.clientX, y0 = e.clientY, on = false, target = null, after = false;
        function clear() { Array.prototype.forEach.call(el.querySelectorAll(".panel.over"), function (s) { s.classList.remove("over", "before", "after", "across"); }); }
        function nearest(x, y) {
          var best = null, bd = Infinity;
          Array.prototype.forEach.call(el.children, function (s) {
            if (!s.getAttribute || !s.getAttribute("data-section")) { return; }
            var r = s.getBoundingClientRect(), dx = x < r.left ? r.left - x : x > r.right ? x - r.right : 0, dy = y < r.top ? r.top - y : y > r.bottom ? y - r.bottom : 0, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = s; }
          });
          return best;
        }
        function onMove(ev) {
          if (!on) {
            if (Math.abs(ev.clientX - x0) + Math.abs(ev.clientY - y0) < 6) { return; }
            on = true; dragging = id; sec.classList.add("dragging"); el.setAttribute("data-dragging", id);
          }
          ev.preventDefault();
          var s = nearest(ev.clientX, ev.clientY); clear(); target = null;
          if (s) {
            var r = s.getBoundingClientRect(), across = r.width < el.getBoundingClientRect().width * 0.75;
            after = across ? ev.clientX > r.left + r.width / 2 : ev.clientY > r.top + r.height / 2;
            target = s.getAttribute("data-section");
            if (target !== id) { s.classList.add("over", after ? "after" : "before"); if (across) { s.classList.add("across"); } told.textContent = names[id] + " goes " + (after ? "after " : "before ") + names[target] + "."; }
            else { told.textContent = ""; }
          }
          if (ev.clientY < 48) { window.scrollBy(0, -14); } else if (ev.clientY > window.innerHeight - 48) { window.scrollBy(0, 14); }
        }
        function end(ok) {
          handle.removeEventListener("pointermove", onMove); handle.removeEventListener("pointerup", up); handle.removeEventListener("pointercancel", back);
          document.removeEventListener("keydown", key, true);
          var was = on; clear(); sec.classList.remove("dragging"); el.removeAttribute("data-dragging"); dragging = null;
          if (was && ok && target && target !== id) { told.textContent = names[id] + " moved."; keep(drop(L, id, target, after)); }
          else if (pending) { pending = false; paint(); }
          if (was && cur.ondragend) { cur.ondragend(); }
        }
        function up() { end(true); }
        function back() { end(false); }
        function key(k) { if (k.key === "Escape") { k.preventDefault(); k.stopPropagation(); told.textContent = names[id] + " stays where it was."; end(false); } }
        try { handle.setPointerCapture(e.pointerId); } catch (x) { /* an old browser: moves still reach the bar while the pointer is over it */ }
        handle.addEventListener("pointermove", onMove); handle.addEventListener("pointerup", up); handle.addEventListener("pointercancel", back);
        document.addEventListener("keydown", key, true);
      }

      function paintButton() {
        slot.textContent = "";
        slot.appendChild(NS.HaloButton({ label: cur.editing ? "Done" : "Edit layout", size: "small", key: "edit-layout-" + key, disabledReason: cur.disabledReason || undefined, onPress: function () { edit(!cur.editing); } }));
      }
      function paintBar(L) {
        bar.hidden = !cur.editing; bar.textContent = "";
        if (!cur.editing) { return; }
        var what = cur.label ? "the " + cur.label + " layout" : "this layout";
        bar.appendChild(h("p", { text: cur.hideOnly ? "Editing " + what + ". Hide a section, or add it back; the order stays as it is here." : "Editing " + what + ". Drag a section by its bar, or use its arrows. A line shows where it will go." }));
        L.hidden.filter(function (id) { return !locked(id); }).forEach(function (id) {
          var next = show(L, id);
          bar.appendChild(NS.HaloButton({ label: "Add back " + names[id], size: "small", cmd: command(writeTo(value, key, cur.mode, next, panels)), onPress: function () { keep(next); } }));
        });
        var reset = writeTo(value, key, cur.mode, readLayout("", panels), panels), plain = !saved(L, panels);
        bar.appendChild(NS.HaloButton({ label: "Reset", size: "small", cmd: plain ? undefined : command(reset), disabledReason: plain ? "it is already the default" : undefined, onPress: function () { keep(readLayout("", panels)); } }));
        bar.appendChild(NS.HaloButton({ label: "Done", size: "small", solid: true, onPress: function () { edit(false); } }));
        bar.appendChild(h("code", { "class": "ls-cmd", text: "Kept in Loadout's config: " + command(value) }));
        bar.appendChild(told);
      }
      function paint() {
        if (dragging) { pending = true; return; }
        var L = layout(), focused = document.activeElement, hadFocus = focused && (el.contains(focused) || bar.contains(focused)), label = hadFocus ? focused.getAttribute("aria-label") || focused.textContent : null;
        el.classList.toggle("editing", !!cur.editing);
        Array.prototype.slice.call(el.children).forEach(function (c) { c.remove(); });
        var shown = L.order.filter(function (id) { return !hiddenNow(L, id); });
        L.order.forEach(function (id, position) { if (!hiddenNow(L, id)) { el.appendChild(panelFor(L, id, position)); } });
        if (!shown.length) { el.appendChild(h("p", { "class": "calm", text: "Every section is hidden. Add one back from the bar above, or Reset." })); }
        paintButton(); paintBar(L);
        /* Keep focus on the control that was pressed, across the redraw. */
        if (label) {
          var again = Array.prototype.filter.call(el.querySelectorAll("button, [role=button]"), function (b) { return (b.getAttribute("aria-label") || b.textContent) === label && !b.disabled; })[0];
          if (again) { again.focus(); }
        }
      }
      paint();
      return {
        el: el, bar: bar, button: slot, key: key,
        get layout() { return layout(); }, get editing() { return !!cur.editing; }, get dragging() { return dragging; }, get value() { return value; },
        edit: edit,
        /* New props from the page: the saved value, editing, locked, the mode. */
        set: function (p) { p = p || {}; if ("value" in p) { value = p.value || ""; } ["editing", "locked", "mode", "legacy", "disabledReason", "count", "label"].forEach(function (k) { if (k in p) { cur[k] = p[k]; } }); paint(); },
        /* Draws a section's body again (all of them with no id). */
        refresh: function (id) { if (id) { delete bodies[id]; } else { bodies = {}; } paint(); }
      };
    }
    LayoutEdit.readLayout = readLayout; LayoutEdit.move = move; LayoutEdit.place = place; LayoutEdit.drop = drop; LayoutEdit.hide = hide; LayoutEdit.show = show; LayoutEdit.saved = saved;
    LayoutEdit.keyOf = keyOf; LayoutEdit.read = readFrom; LayoutEdit.write = writeTo; LayoutEdit.command = command; LayoutEdit.KEY = KEY; LayoutEdit.MODES = MODES;
    NS.LayoutEdit = LayoutEdit;
  })();

  /* --- ListToolbar --- */
  (function () {
    /* ListToolbar: the same search, filter and sort controls over every list. The box takes the query typed; the chips
       are its pieces as the daemon read them, and a chip's x takes that piece away; the menus add a filter or choose a
       sort. All of them edit the one query string, so they cannot disagree. '/' puts you in the box from anywhere on
       the page that is not already a field. As main's ListToolbar.svelte and lib/lists.ts. */
    var SORT = "sort:";
    function withoutPart(parts, index) { return parts.filter(function (_, at) { return at !== index; }).join(" "); }
    function withTerm(parts, term) { return (parts.indexOf(term) >= 0 ? parts : parts.concat([term])).join(" "); }
    function sortOf(parts) { var p = parts.filter(function (x) { return x.indexOf(SORT) === 0; })[0]; return p ? p.slice(SORT.length) : ""; }
    function withSort(parts, sort) { var kept = parts.filter(function (x) { return x.indexOf(SORT) !== 0; }); return (sort ? kept.concat([SORT + sort]) : kept).join(" "); }
    function partSaid(part) {
      if (part.indexOf(SORT) === 0) {
        return "sorted by " + part.slice(SORT.length).split(",").map(function (f) { return f.charAt(0) === "-" ? f.slice(1) + ", descending" : f; }).join(", then ");
      }
      return part;
    }
    function exampleQuery(fields) {
      var choice = fields.filter(function (f) { return f.type === "choice" && (f.choices || []).length > 0; })[0];
      var date = fields.filter(function (f) { return f.type === "date"; })[0];
      return [choice ? choice.name + ":" + choice.choices[0] : null, date ? "sort:-" + date.name : null].filter(Boolean).join(" ");
    }
    function typingIn(node) {
      return !!node && (node.tagName === "INPUT" || node.tagName === "TEXTAREA" || node.tagName === "SELECT" || node.isContentEditable || !!(node.closest && node.closest(".ls-term, .xterm, .ls-dialog")));
    }

    function ListToolbar(o) {
      var cur = Object.assign({ noun: "items", query: "", shape: null, shown: null, recorded: null, problem: null, slash: true }, o || {});
      var id = uid("ls-lt-"), timer, typed = cur.query || "";
      var el = h("div", { "class": "ls-ltool", role: "search" });
      var lab = h("label", { "class": "lab", "for": id });
      var box = h("input", { id: id, "class": "ls-text q", type: "search", autocomplete: "off", spellcheck: "false", "aria-keyshortcuts": "/" });
      var row = h("div", { "class": "row" }, [lab, box]);
      el.appendChild(row);
      var tail = [];

      function send(next) { clearTimeout(timer); if (cur.onchange) { cur.onchange(String(next).trim()); } }
      box.addEventListener("input", function () {
        typed = box.value; clearTimeout(timer);
        /* Each keystroke would be a read of the list; wait for a pause. */
        timer = setTimeout(function () { send(typed); }, 350);
      });
      box.addEventListener("keydown", function (e) {
        if (e.key === "Enter") { e.preventDefault(); send(typed); }
        else if (e.key === "Escape" && typed) { e.preventDefault(); typed = cur.query || ""; box.value = typed; }
      });
      function slash(e) {
        if (cur.slash !== false && e.key === "/" && !e.ctrlKey && !e.metaKey && !e.altKey && !typingIn(e.target)) { e.preventDefault(); box.focus(); box.select(); }
      }
      window.addEventListener("keydown", slash);

      function paint(queryChanged) {
        var shape = cur.shape, parts = (shape && shape.parts) || [], fields = (shape && shape.fields) || [];
        var sort = sortOf(parts), choices = fields.filter(function (f) { return f.type === "choice"; }), example = exampleQuery(fields);
        var atDefault = String(cur.query || "").trim() === String((shape && shape.defaultQuery) || "").trim();
        var hidden = cur.shown !== null && cur.shown !== undefined && cur.recorded !== null && cur.recorded !== undefined ? cur.recorded - cur.shown : 0;
        var focusKey = document.activeElement && el.contains(document.activeElement) && document.activeElement !== box ? document.activeElement.getAttribute("data-k") : null;

        el.setAttribute("aria-label", "Filter and sort the " + cur.noun);
        lab.textContent = "Find " + cur.noun;
        if (queryChanged) { typed = cur.query || ""; if (box.value !== typed) { box.value = typed; } }
        box.placeholder = example ? "Type to filter, or " + example : "Type to filter";
        if (cur.problem) { box.setAttribute("aria-invalid", "true"); box.setAttribute("aria-describedby", id + "-problem"); } else { box.removeAttribute("aria-invalid"); box.removeAttribute("aria-describedby"); }

        while (row.children.length > 2) { row.removeChild(row.lastChild); }
        if (choices.length) {
          var add = h("select", { "class": "ls-text menu", "aria-label": "Add a filter", "data-k": "add" }, [h("option", { value: "", text: "Add a filter…" })].concat(choices.map(function (f) {
            return h("optgroup", { label: f.name }, [].concat.apply([], (f.choices || []).map(function (c) {
              return [h("option", { value: f.name + ":" + c, text: f.name + " is " + c }), h("option", { value: "-" + f.name + ":" + c, text: f.name + " is not " + c })];
            })));
          })));
          add.addEventListener("change", function () { var v = add.value; if (v !== "") { send(withTerm(parts, v)); } add.value = ""; });
          row.appendChild(add);
        }
        if (fields.length) {
          var opts = [h("option", { value: "", text: "Default order" })];
          fields.forEach(function (f) { opts.push(h("option", { value: f.name, text: f.name })); opts.push(h("option", { value: "-" + f.name, text: f.name + ", descending" })); });
          if (sort && !fields.some(function (f) { return f.name === sort || "-" + f.name === sort; })) { opts.push(h("option", { value: sort, text: sort })); }
          var by = h("select", { "class": "ls-text menu", "aria-label": "Sort by", "data-k": "sort" }, opts);
          by.value = sort;
          by.addEventListener("change", function () { send(withSort(parts, by.value)); });
          row.appendChild(by);
        }

        tail.forEach(function (n) { if (n.parentNode) { n.parentNode.removeChild(n); } }); tail = [];
        if (cur.problem) { tail.push(h("p", { "class": "problem", id: id + "-problem", role: "alert", text: cur.problem })); }
        if (parts.length || !atDefault) {
          var chips = h("div", { "class": "chips" }, parts.map(function (part, index) {
            return h("span", { "class": "chip" }, [h("span", { "class": "said", text: partSaid(part) }),
              h("button", { type: "button", "aria-label": "Remove " + partSaid(part), "data-k": "chip" + index, text: "×", onclick: function () { send(withoutPart(parts, index)); } })]);
          }));
          if (shape && !atDefault) { chips.appendChild(h("button", { type: "button", "class": "reset", "data-k": "reset", text: "Reset to default", onclick: function () { send(shape.defaultQuery); } })); }
          tail.push(chips);
        }
        if (hidden > 0) {
          tail.push(h("p", { "class": "count", role: "status" }, ["Showing " + cur.shown + " of " + cur.recorded + ". ", cur.shown === 0 ? "Nothing matches. " : null,
            h("button", { type: "button", "class": "clear", "data-k": "clear", text: "Show all " + cur.recorded, onclick: function () { send(""); } })]));
        }
        tail.forEach(function (n) { el.appendChild(n); });
        if (focusKey) { var f = el.querySelector('[data-k="' + focusKey + '"]') || el.querySelector('[data-k^="chip"]') || box; f.focus(); }
      }
      paint(true);
      return {
        el: el,
        set: function (patch) { patch = patch || {}; var qc = patch.query !== undefined && patch.query !== cur.query; cur = Object.assign({}, cur, patch); paint(qc); },
        focus: function () { box.focus(); box.select(); },
        destroy: function () { clearTimeout(timer); window.removeEventListener("keydown", slash); }
      };
    }
    NS.ListToolbar = ListToolbar;
  })();

  /* --- MapFilterBar --- */
  (function () {
    /* MapFilterBar: the filters over Home's Map and Lens: the presets, then a chip per kind and per state, then the
       projects to hide. Every change goes straight to config through onchange; the bar keeps nothing of its own, so it
       reads what loadout config set it to as well as what it set. As main's MapFilterBar.svelte and lib/mapFilter.ts. */
    var KINDS = ["runs", "sessions", "ideas", "tasks"];
    var STATES = ["waiting", "running", "failed", "ended"];
    var NO_PROJECT = "(no project)";
    var KIND_NAMES = { runs: "Team runs", sessions: "Sessions", ideas: "Ideas", tasks: "Tasks" };
    var STATE_NAMES = { waiting: "Waiting on you", running: "Running", failed: "Failed", ended: "Ended" };
    /* In flight draws what the map drew before there were filters: no ideas, and nothing settled. Runs and sessions is
       the same with tasks off as well. Everything is every line Home has. */
    var PRESETS = {
      inflight: { kinds: ["runs", "sessions", "tasks"], states: ["waiting", "running", "failed"], projects: [] },
      runs: { kinds: ["runs", "sessions"], states: ["waiting", "running", "failed"], projects: [] },
      all: { kinds: KINDS.slice(), states: STATES.slice(), projects: [] }
    };
    var PRESET_NAMES = { inflight: "In flight", runs: "Runs and sessions", all: "Everything" };

    function listOf(v) { return String(v === null || v === undefined ? "" : v).split(",").map(function (p) { return p.trim(); }).filter(Boolean); }
    function same(a, b) { return a.length === b.length && a.every(function (x) { return b.indexOf(x) >= 0; }); }
    function copy(f) { return { kinds: f.kinds.slice(), states: f.states.slice(), projects: f.projects.slice() }; }
    /* The filters as config holds them (mapKinds, mapStates, mapProjects, and the old mapScope). A kind or state key
       that is unset, or names nothing this page knows, reads as In flight's, so the map is never blank by accident. */
    function filterOf(prefs) {
      if (prefs && prefs.mapKinds === undefined && prefs.mapScope === "all") { return copy(PRESETS.all); }
      var kinds = KINDS.filter(function (k) { return listOf(prefs && prefs.mapKinds).indexOf(k) >= 0; });
      var states = STATES.filter(function (s) { return listOf(prefs && prefs.mapStates).indexOf(s) >= 0; });
      return { kinds: kinds.length ? kinds : PRESETS.inflight.kinds.slice(), states: states.length ? states : PRESETS.inflight.states.slice(), projects: listOf(prefs && prefs.mapProjects) };
    }
    /* The preset the filters match, or null for Custom. */
    function presetOf(f) {
      var found = null;
      Object.keys(PRESETS).forEach(function (n) { var p = PRESETS[n]; if (!found && same(f.kinds, p.kinds) && same(f.states, p.states) && same(f.projects, p.projects)) { found = n; } });
      return found;
    }
    /* The three keys and the values that write a filter, in the order the command line would take them. */
    function keysOf(f) { return [["studio-map-kinds", f.kinds.join(", ")], ["studio-map-states", f.states.join(", ")], ["studio-map-projects", f.projects.join(", ")]]; }
    /* One chip turned on or off. The last kind or state on stays on: a filter showing nothing cannot be reached from the bar, as it cannot from config. */
    function toggled(on, item, order) {
      if (on.indexOf(item) >= 0) { return on.length > 1 ? on.filter(function (x) { return x !== item; }) : on.slice(); }
      return order.filter(function (x) { return x === item || on.indexOf(x) >= 0; });
    }
    /* As main's command.setting: an empty value is "", anything else quoted where the shell needs it. */
    function settingLine(key, value) { return "$ loadout config set " + key + " " + (value === "" ? "\"\"" : q(value)); }

    function MapFilterBar(o) {
      var cur = Object.assign({ filter: copy(PRESETS.inflight), projects: [] }, o || {});
      var el = h("div", { "class": "ls-mapfilters" });
      var open = false;
      function change(next) { if (cur.onchange) { cur.onchange(next); } }

      function chip(label, pressed, last, title, press) {
        return h("button", { type: "button", "class": "chip", "aria-pressed": pressed ? "true" : "false", "aria-disabled": last ? "true" : null, title: last ? title : null, onclick: press, text: label });
      }

      function paint() {
        var f = cur.filter, projects = cur.projects || [], preset = presetOf(f);
        var hiddenProjects = f.projects.filter(function (p) { return projects.indexOf(p) >= 0; });
        var focusKey = document.activeElement && el.contains(document.activeElement) ? document.activeElement.getAttribute("data-k") : null;
        el.textContent = "";
        var bar = h("div", { "class": "mapfilters", role: "region", "aria-label": "What Home shows" });
        bar.appendChild(h("div", { "class": "views", role: "group", "aria-label": "Presets" }, Object.keys(PRESETS).map(function (n) {
          return h("button", { type: "button", "data-k": "p:" + n, "aria-pressed": preset === n ? "true" : "false", onclick: function () { change(copy(PRESETS[n])); }, text: PRESET_NAMES[n] });
        })));
        if (!preset) { bar.appendChild(h("span", { "class": "custom", text: "Custom" })); }
        bar.appendChild(h("div", { "class": "chips", role: "group", "aria-label": "Kind" }, KINDS.map(function (k) {
          var on = f.kinds.indexOf(k) >= 0, b = chip(KIND_NAMES[k], on, on && f.kinds.length === 1, "One kind stays on", function () { change({ kinds: toggled(f.kinds, k, KINDS), states: f.states.slice(), projects: f.projects.slice() }); });
          b.setAttribute("data-k", "k:" + k); return b;
        })));
        bar.appendChild(h("div", { "class": "chips", role: "group", "aria-label": "State" }, STATES.map(function (s) {
          var on = f.states.indexOf(s) >= 0, b = chip(STATE_NAMES[s], on, on && f.states.length === 1, "One state stays on", function () { change({ kinds: f.kinds.slice(), states: toggled(f.states, s, STATES), projects: f.projects.slice() }); });
          b.setAttribute("data-k", "s:" + s); return b;
        })));
        if (projects.length) {
          var summary = h("summary", { "data-k": "sum", text: "Projects · " + (hiddenProjects.length ? hiddenProjects.join(", ") + " hidden" : "all shown") });
          var det = h("details", { "class": "projects" }, [summary, h("div", { "class": "chips", role: "group", "aria-label": "Projects shown" }, projects.map(function (p) {
            var hidden = f.projects.indexOf(p) >= 0;
            var b = chip(p, !hidden, false, null, function () { change({ kinds: f.kinds.slice(), states: f.states.slice(), projects: hidden ? f.projects.filter(function (x) { return x !== p; }) : f.projects.concat([p]) }); });
            b.setAttribute("data-k", "j:" + p); return b;
          }))]);
          det.open = open;
          det.addEventListener("toggle", function () { open = det.open; });
          bar.appendChild(det);
        }
        el.appendChild(bar);
        /* Main's bar shows no command: Home writes the keys through loadout config set. The design system shows them
           as a From line, which follows the commands setting. In flight is the three keys unset, as Home writes it. */
        var inflight = preset === "inflight";
        el.appendChild(h("p", { "class": "ls-from" }, ["The same as "].concat(keysOf(f).map(function (kv, i) {
          return h("code", { text: settingLine(kv[0], inflight ? "" : kv[1]) + (i < 2 ? ";" : "") });
        }).reduce(function (acc, c, i) { if (i) { acc.push(" "); } acc.push(c); return acc; }, [])).concat(["."])));
        if (focusKey) { var back = el.querySelector("[data-k=\"" + focusKey.replace(/"/g, "\\\"") + "\"]"); if (back) { back.focus(); } }
      }
      paint();
      return { el: el, set: function (patch) { cur = Object.assign({}, cur, patch || {}); paint(); } };
    }
    Object.assign(MapFilterBar, { KINDS: KINDS, STATES: STATES, NO_PROJECT: NO_PROJECT, KIND_NAMES: KIND_NAMES, STATE_NAMES: STATE_NAMES, PRESETS: PRESETS, PRESET_NAMES: PRESET_NAMES, filterOf: filterOf, presetOf: presetOf, keysOf: keysOf, toggled: toggled });
    NS.MapFilterBar = MapFilterBar;
  })();

  /* --- MiniLine --- */
  (function () {
    /* MiniLine: a run's rounds as a line small enough for a board row. A stop for each round with a tick per node,
       the one running lit, and a dashed stop for what comes next or a solid one where it ended. Decoration: the row
       beside it says the same in words. As main's MiniLine.svelte. */
    function stopOf(word) {
      var family = NS.signalOf(word || "done").family;
      return word === "running" || word === "round running" ? "cur" : family === "needs" ? "wait" : family === "bad" ? "bad" : "";
    }
    function MiniLine(o) {
      var el = h("div", { "class": "ls-mini", "aria-hidden": "true" });
      function paint(p) {
        el.textContent = "";
        (p.rounds || []).forEach(function (round, index) {
          var stop = stopOf(round.word);
          if (index) { el.appendChild(h("span", { "class": "seg" + (stop === "cur" ? " into" : "") })); }
          el.appendChild(h("span", { "class": "st " + stop }, [h("i"), h("span", { "class": "nd" }, (round.nodes || []).map(function (n) {
            return h("u", { "class": NS.signalOf(n[1]).family, title: n[0] + " · " + n[1] });
          }))]));
        });
        if (p.running) { el.appendChild(h("span", { "class": "seg dash" })); el.appendChild(h("span", { "class": "st next" }, [h("i")])); }
        else if (p.end) { el.appendChild(h("span", { "class": "seg" })); el.appendChild(h("span", { "class": "st end" }, [h("i")])); }
      }
      var cur = Object.assign({ rounds: [], end: false, running: false }, o || {});
      paint(cur);
      return { el: el, set: function (patch) { cur = Object.assign({}, cur, patch || {}); paint(cur); } };
    }
    NS.MiniLine = MiniLine;
  })();

  /* --- Peek --- */
  (function () {
    /* Peek: one overlay over any piece of work (a session, a team run, an idea, a task, or a run's crew node) that shows its live state and
       its main actions without leaving the page. It opens beside a row on hover after a short delay, or at once on focus;
       the row's peek button pins it; Escape closes it before anything else that claims Escape, but never while you are
       typing in a terminal. Below 600px it is a sheet from the foot of the screen. Three more forms: the waiting list
       (the mode bar's count), an expansion under the row (Minimal) and a preview drawn into a pane (Pure). New design for
       WORKFLOW.md; main has no peek yet. */
    var KIND = { session: "session", run: "team run", idea: "idea", task: "task", approval: "approval", node: "crew node" };
    var TERMINAL = ".xterm, .ls-term .screen, [data-terminal]";
    var FOCUSABLE = "a[href], button:not([disabled]), input:not([disabled]):not([type=hidden]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex=\"-1\"])";
    function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }
    function family(word) { return NS.signalOf ? NS.signalOf(word).family : "rest"; }
    function sig(word) { return NS.Signal ? NS.Signal(word) : h("b", { text: word }); }
    function halo(o) { return NS.HaloButton(Object.assign({ size: "small" }, o)); }
    function titleOf(it) { return it.kind === "node" ? it.node || it.id || "" : it.title || it.goal || it.id || ""; }
    function inTerminal(n) { return !!(n && n.closest && n.closest(TERMINAL)); }
    function shown(n) { return !!(n.offsetWidth || n.offsetHeight || n.getClientRects().length); }
    function focusables(root) { return Array.prototype.filter.call(root.querySelectorAll(FOCUSABLE), function (n) { return shown(n) && !n.closest("[hidden]") && n.getAttribute("aria-hidden") !== "true"; }); }
    function overlaps(a, b) { return a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom; }
    function where(it) { return it.where || (it.project ? ["--project", it.project] : ["--global"]); }
    function line(words) { return "$ loadout " + words.map(function (w) { return /^--[\w-]+=/.test(w) ? w.split("=")[0] + "=" + q(w.slice(w.indexOf("=") + 1)) : /^--[\w-]+$/.test(w) ? w : q(w); }).join(" "); }
    function ago(ms, now) {
      if (typeof ms === "string") { ms = Date.parse(ms); }
      if (!ms || isNaN(ms)) { return ""; }
      var s = Math.max(0, Math.round((now - ms) / 1000));
      return s < 45 ? "just now" : s < 3600 ? Math.round(s / 60) + " min" : s < 86400 ? Math.round(s / 3600) + " h" : Math.round(s / 86400) + " d";
    }
    function openQuestions(it, local) { return (it.questions || []).filter(function (x) { return !x.answer && !local[it.id + "/" + x.id]; }); }
    function wordOf(it, local) {
      if (it.word) { return it.word; }
      if (it.kind === "session") { return it.lost ? "ended when the daemon stopped" : it.ended ? "ended" : it.waitingFor ? "waiting for you" : "working"; }
      if (it.kind === "run") { return it.gate ? "needs a decision" : it.held ? "held after this round" : "running"; }
      if (it.kind === "idea") { return openQuestions(it, local || {}).length ? "waiting for you" : "shaping"; }
      if (it.kind === "task") { return it.state || "open"; }
      if (it.kind === "approval") { return "needs approval"; }
      if (it.kind === "node") { return it.quiet ? "quiet" : "working"; }
      return "unknown";
    }
    /* What a screen reader hears from a focused row: the peek's gist, before anyone opens it. */
    function gist(it, local) {
      var w = wordOf(it, local), first = it.kind === "idea" ? openQuestions(it, local || {})[0] : null;
      return [KIND[it.kind] || it.kind, it.kind === "node" ? it.role : null, w, it.waitingFor || (it.gate && it.gate.question) || (first && first.question) || it.note || null].filter(Boolean).join(" · ") + ". The peek button keeps it open.";
    }

    function Peek(o) {
      o = o || {};
      var mode = o.mode === "inline" || o.mode === "preview" ? o.mode : "float", delay = o.delay === undefined ? 350 : o.delay;
      var every = o.tailEvery === undefined ? 2000 : o.tailEvery, lines = o.lines || (mode === "inline" ? 4 : 8);
      var pid = uid("ls-peek-"), tid = pid + "-t";
      var cur = { item: null, row: null, get: null, btn: null, pinned: false, list: null, anchor: null, back: false };
      var drafts = {}, local = {}, busy = null, said = "", saidBad = false, tail = null, tailEl = null, poll = null, openTimer = null, closeTimer = null, lastWord = null;
      var inner = h("div", { "class": "in" }), live = h("span", { "class": "ls-vh", role: "status", "aria-live": "polite" });
      var panel = h("section", { "class": "ls-peek " + mode, id: pid, tabindex: "-1", hidden: mode === "preview" ? null : true }, [inner, live]);
      var host = mode === "preview" ? o.container : (o.host || document.body);
      if (mode !== "inline" && host) { host.appendChild(panel); }
      function now() { return o.now || Date.now(); }
      function isOpen() { return mode === "preview" ? !!cur.item || !!cur.list : !panel.hidden; }
      function call(name) { var f = o[name]; return typeof f === "function" ? f : null; }

      /* ---------- what it says and does ---------- */
      function keeping(fn) {
        var a = document.activeElement, k = a && panel.contains(a) ? (a.closest("[data-k]") || a).getAttribute("data-k") : null;
        fn();
        if (k) { var b = panel.querySelector("[data-k=\"" + k + "\"]"); if (b && b !== document.activeElement) { b.focus({ preventScroll: true }); } }
      }
      /* An action that changes something: busy while it goes, then ✓ with its command, or ✕ and why. False, a throw or a rejection failed. */
      function act(key, busyWord, done, cmd, fn) {
        if (busy || !fn) { return; }
        var v;
        try { v = fn(); } catch (e) { settle(false, e, done, cmd); return; }
        if (v && typeof v.then === "function") {
          busy = { key: key, word: busyWord }; said = ""; paint();
          v.then(function (r) { busy = null; settle(r, null, done, cmd); }, function (e) { busy = null; settle(false, e, done, cmd); });
        } else { settle(v, null, done, cmd); }
      }
      function settle(r, e, done, cmd) {
        saidBad = r === false;
        if (!saidBad && done === null && typeof r !== "string") { said = ""; paint(); return; }
        said = saidBad ? "✕ " + (e && e.message ? e.message : "It did not go through. Nothing changed.") : "✓ " + (typeof r === "string" ? r : done) + (cmd && cmd.indexOf("$ ") === 0 ? " · " + cmd : "");
        paint();
      }
      /* An action that goes somewhere else: the peek gets out of the way, unless it is the pane. */
      function go(fn) { return function () { var it = cur.item; if (mode !== "preview") { close(false); } fn(it); }; }
      function button(key, b) {
        var spin = busy && busy.key === key;
        var el = halo(Object.assign({}, b, { key: "pk-" + key, label: spin ? busy.word : b.label, inactive: !!busy && !b.disabledReason }));
        if (spin) { (el.querySelector ? el.querySelector("button") || el : el).setAttribute("aria-busy", "true"); }
        return el;
      }

      /* ---------- the bodies: session, run, idea, task, node ---------- */
      function facts(bits) { bits = bits.filter(Boolean); return bits.length ? h("p", { "class": "facts", text: bits.join(" · ") }) : null; }
      function paintTail() {
        if (!tailEl) { return; }
        var t = tail || cur.item.tail || null;
        tailEl.pre.textContent = t ? (t.lines || String(t.text || "").split("\n")).slice(-lines).join("\n") : "Reading its last lines…";
        tailEl.cap.textContent = ["Its last lines", "read-only", t && t.running === false ? "it has stopped" : null, t && t.at ? t.at : null].filter(Boolean).join(" · ");
      }
      /* A session's tail and a node's journal are read the same way: on opening, then every `every` ms while open. */
      function reader(it) { return it ? (it.kind === "session" && !it.lost ? call("tail") : it.kind === "node" ? call("journal") : null) : null; }
      function readTail() {
        var it = cur.item, f = reader(it);
        if (!f) { return; }
        var v; try { v = f(it); } catch (e) { return; }
        Promise.resolve(v).then(function (t) { if (t && cur.item === it) { tail = t; if (it.kind === "node") { paintJournal(); } else { paintTail(); } } }, function () {});
      }
      /* A crew node: what it did last (its journal's last lines, as [time, text, family?] or { at, text, family }). */
      function paintJournal() {
        if (!tailEl || !tailEl.jl) { return; }
        var got = tail || cur.item.journal || null, rows = got ? (got.lines || got).slice(-Math.min(lines, 6)) : null;
        tailEl.jl.textContent = "";
        if (!rows) { tailEl.jl.appendChild(h("li", { "class": "none", text: "Reading what it did last…" })); return; }
        if (!rows.length) { tailEl.jl.appendChild(h("li", { "class": "none", text: "It has not done anything yet." })); return; }
        rows.forEach(function (r) {
          var at = Array.isArray(r) ? r[0] : r.at || r.time, text = Array.isArray(r) ? r[1] : r.text, fam = Array.isArray(r) ? r[2] : r.family;
          tailEl.jl.appendChild(h("li", { "class": fam ? "f-" + fam : null }, [at ? h("time", { text: at }) : null, h("span", { text: text || "" })]));
        });
      }
      var says = {};
      function node(it, kids, acts) {
        var w = wordOf(it), gone = /done|failed|stopped|ended|abandoned/.test(w);
        kids.push(facts([it.role, it.model, it.goal || it.runTitle, it.turns !== undefined ? it.turns + " turn" + (it.turns === 1 ? "" : "s") : null]));
        if (it.branch) { kids.push(h("p", { "class": "branch" }, [h("span", { "class": "k", text: "Works on " }), h("code", { text: it.branch })])); }
        var jl = h("ol", { "class": "jl", "aria-label": "What it did last" });
        tailEl = { jl: jl }; paintJournal();
        kids.push(h("div", { "class": "last" }, [h("p", { "class": "cap", text: "What it did last" }), jl]));
        if (it.quiet) { kids.push(h("p", { "class": "quiet-n", text: "Quiet for " + it.quiet + ", three times its usual turn." })); }
        /* Say to it: SayToNode, kept across redraws so what is typed stays. */
        if (call("onSay") && NS.SayToNode) {
          var say = it.say || {}, key = it.run + "/" + it.node + "/" + (gone ? "ended" : say.state || "locked");
          if (!says[key]) {
            says[key] = NS.SayToNode({ run: it.run, node: it.node, state: gone ? "ended" : say.state || "locked", minutesLeft: say.minutesLeft, disabledReason: it.disabledReason,
              onUnlock: function (pass) { return o.onUnlock ? o.onUnlock(it, pass) : pass.length > 0; },
              onSend: function (message) { return o.onSay(cur.item || it, message); } });
          }
          kids.push(says[key]);
        }
      }
      function session(it, kids, acts) {
        if (it.lost) { lostSession(it, kids, acts); return; }
        var ended = !!it.ended || wordOf(it) === "ended";
        kids.push(facts([it.project, it.agent, it.mode, it.since ? "for " + ago(it.since, now()) : null]));
        var pre = h("pre", { "class": "tail", "aria-label": "Its last lines, read-only" }), cap = h("p", { "class": "cap" });
        tailEl = { pre: pre, cap: cap }; paintTail();
        var well = h("div", { "class": "well" }, [cap, pre]);
        if (it.status && NS.StatusStrip && mode !== "inline") { var s = NS.StatusStrip({ status: it.status, now: o.now }); well.appendChild(h("div", { "class": "foot" }, [s.el || s])); }
        kids.push(well);
        if (it.waitingFor) { kids.push(h("div", { "class": "wait" }, [h("p", {}, [h("b", { text: "It is waiting for: " }), it.waitingFor + ". Answer it in its terminal: Studio never answers for it."])])); }
        if (call("onTypeHere")) { acts.push(button("type", { label: "Type here", kind: it.waitingFor ? "needs" : "", solid: !!it.waitingFor, glyph: "›", cmd: "opens its terminal with the keyboard in it · changes nothing", disabledReason: ended ? "it has ended" : it.disabledReason, onPress: go(o.onTypeHere) })); }
        if (call("onOpenBeside")) { acts.push(button("beside", { label: "Open beside", cmd: "opens it beside this page · changes nothing", onPress: go(o.onOpenBeside) })); }
        if (call("onEnd") && !ended) { acts.push(button("end", { label: "End session…", kind: "danger", cmd: line(["session", "end", it.hid || it.id, "--yes"]) + " · asks first", disabledReason: it.disabledReason, onPress: function () { act("end", "ending…", null, line(["session", "end", it.hid || it.id, "--yes"]), function () { return o.onEnd(it); }); } })); }
      }
      /* A session that ended when the daemon stopped: its terminal went with the daemon, so there are no lines to show. What it was,
         what it was doing, and Resume, which carries the same conversation on in a new terminal (and starts an agent). */
      function lostSession(it, kids, acts) {
        var L = it.lost || {};
        kids.push(facts([it.project, it.agent, it.branch ? "on " + it.branch : null, it.mode]));
        var said1 = "It ended when the daemon stopped" + (ago(L.endedAt, now()) ? (ago(L.endedAt, now()) === "just now" ? ", just now" : ", " + ago(L.endedAt, now()) + " ago") : "") + ". Its terminal went with the daemon, so there are no lines to show.";
        kids.push(h("div", { "class": "lostwell" }, [h("p", { text: said1 }), L.task ? h("p", {}, [h("b", { text: "It was working on: " }), L.task]) : null,
          h("p", { "class": "cap", text: "Resume carries the same conversation on in a new terminal. Nothing restarts by itself: it starts an agent." })]));
        if (call("onResume")) { acts.push(button("resume", { label: "Resume", kind: "needs", solid: true, glyph: "▸", cmd: L.resumeCmd || line(["resume", L.agentSession || "--last"]), disabledReason: it.disabledReason, onPress: function () { act("resume", "resuming…", "Resumed in a new terminal", L.resumeCmd, function () { return o.onResume(it); }); } })); }
        if (call("onDismiss")) { acts.push(button("dismiss", { label: "Not this one", cmd: "stops offering it here · changes nothing else", disabledReason: it.disabledReason, onPress: function () { act("dismiss", "…", "No longer offered", null, function () { return o.onDismiss(it); }); } })); }
      }
      function run(it, kids, acts) {
        var w = wordOf(it), over = /done|failed|stopped|abandoned|ended|accepted/.test(w);
        kids.push(facts([it.team, it.project, it.round ? "round " + it.round + (it.rounds ? " of " + it.rounds : "") : null, it.elapsed]));
        if (it.line && NS.MiniLine) {
          /* Stage 14's MiniLine takes one object; the base one before it took (rounds, end, running). */
          var m = NS.MiniLine.length > 1 ? NS.MiniLine(it.line.rounds || [], !!it.line.end, !!it.line.running) : NS.MiniLine(it.line);
          kids.push(h("div", { "class": "mini" }, [m.el || m]));
        }
        if (it.working) {
          kids.push(h("div", { "class": "now" }, [h("span", { "class": "k", text: "Working now" }), h("p", {}, [h("b", { text: it.working.node }), it.working.role ? " (" + it.working.role + ")" : "", it.working.doing ? ": " + it.working.doing : ""]), sig(it.working.word || "working")]));
        } else if (!over && !it.gate) { kids.push(h("p", { "class": "quiet", text: "No node is working: the lead is deciding the next round." })); }
        if (it.gate && NS.Gate) {
          kids.push(NS.Gate(Object.assign({ compact: true, run: it.id, disabledReason: it.disabledReason }, it.gate, {
            onAnswer: function (answer, extra) { if (o.onAnswer) { var v = o.onAnswer(it, answer, extra); if (v && v.then) { v.then(null, function () {}); } } } })));
        }
        if (it.spend && NS.Spend) { var sp = NS.Spend(it.spend); kids.push(sp.el || sp); }
        if (over) { return; }
        var hold = line(["team", "halt", it.id, "--pause"]), carry = line(["team", "halt", it.id, "--resume"]), stop = line(["team", "halt", it.id]);
        if (it.held) { if (call("onCarryOn")) { acts.push(button("carry", { label: "Let it carry on", cmd: carry, disabledReason: it.disabledReason, onPress: function () { act("carry", "carrying on…", "It carries on", carry, function () { return o.onCarryOn(it); }); } })); } }
        else if (call("onHold")) { acts.push(button("hold", { label: "Hold after this round", glyph: "‖", cmd: hold, disabledReason: it.disabledReason, onPress: function () { act("hold", "holding…", "It holds after this round", hold, function () { return o.onHold(it); }); } })); }
        if (call("onStop")) { acts.push(button("stop", { label: "Stop after this round", kind: "danger", glyph: "■", cmd: stop, disabledReason: it.disabledReason, onPress: function () { act("stop", "stopping…", "It stops after this round", stop, function () { return o.onStop(it); }); } })); }
      }
      function idea(it, kids, acts) {
        var open = openQuestions(it, local), x = open[0];
        kids.push(facts([it.project || (where(it)[0] === "--global" ? "workspace" : null), it.round ? "round " + it.round : null, open.length ? open.length + " question" + (open.length === 1 ? "" : "s") + " open" : "no questions open"]));
        if (NS.IdeaStages) { kids.push(h("div", { "class": "stages" }, [NS.IdeaStages({ stage: it.stage || "Captured", word: wordOf(it, local) })])); }
        if (x) {
          var key = it.id + "/" + x.id, typed = (drafts[key] || "").trim(), qid = pid + "-q";
          var cmd = line(["idea", "answer", it.id, x.id, "--answer=" + (typed || "<answer>")].concat(where(it)));
          var fs = h("fieldset", {}, [h("legend", { id: qid, text: x.question })]);
          if (x.why) { fs.appendChild(h("p", { "class": "why", text: x.why })); }
          var own = h("textarea", { "class": "ls-text", id: qid + "-w", rows: 2, "data-k": "pk-own", placeholder: "Or say it in your own words" });
          var opts = h("div", { "class": "opts" });
          (x.options || []).forEach(function (op) {
            var r = h("input", { type: "radio", name: qid + "-o", value: op, "data-k": "pk-o-" + op, onchange: function () { drafts[key] = op; own.value = ""; paint(); } });
            r.checked = typed === op;
            opts.appendChild(h("label", { "class": "opt" }, [r, op, x.recommendation === op ? h("span", { "class": "rec", text: "recommended" }) : null]));
          });
          if ((x.options || []).length) { fs.appendChild(opts); }
          if (typed && (x.options || []).indexOf(typed) < 0) { own.value = drafts[key]; }
          own.addEventListener("input", function () { drafts[key] = own.value; refreshAnswer(); });
          fs.appendChild(h("div", { "class": "own" }, [h("label", { "for": qid + "-w", text: "Or in your own words" }), own]));
          var row = h("div", { "class": "ls-answers flush" });
          function refreshAnswer() {
            var t = (drafts[key] || "").trim(), c = line(["idea", "answer", it.id, x.id, "--answer=" + (t || "<answer>")].concat(where(it)));
            Array.prototype.forEach.call(opts.querySelectorAll("input"), function (r) { r.checked = r.value === t; });
            keeping(function () {
              row.textContent = "";
              row.appendChild(button("answer", { label: "Answer", kind: "needs", solid: !!t, glyph: "✓", cmd: c, disabledReason: it.disabledReason || (t ? null : "pick an answer or write one"),
                onPress: function () { act("answer", "answering…", "Answered " + x.id + (open.length > 1 ? ". The next question is below." : ". Refine when you are ready."), c, function () {
                  var v = o.onAnswerIdea(it, x, t, c);
                  function keep(ok) { if (ok !== false) { local[key] = t; delete drafts[key]; } return ok; }
                  return v && v.then ? v.then(keep) : keep(v);
                }); } }));
            });
          }
          if (call("onAnswerIdea")) { refreshAnswer(); }
          kids.push(h("div", { "class": "ls-qset one" }, [h("section", { "class": "q" + (typed ? " drafted" : ""), "aria-labelledby": qid }, [
            h("span", { "class": "who", text: [x.id, typed ? "your answer, not sent yet" : "the agent asks", open.length > 1 ? "1 of " + open.length : null].filter(Boolean).join(" · ") }), fs, row])]));
        }
        if (call("onRefine")) {
          var ref = line(["idea", "refine", it.id].concat(where(it)));
          acts.push(button("refine", { label: "Refine", glyph: "↻", cmd: ref, disabledReason: it.disabledReason, onPress: function () { act("refine", "refining…", "The next round has started: it runs on the daemon, and a toast says when it ends", ref, function () { return o.onRefine(it); }); } }));
        }
      }
      function task(it, kids, acts) {
        var state = it.state || "open";
        kids.push(facts([it.project || "workspace", it.taskKind, it.declaredBy ? "by " + it.declaredBy : null, it.declaredAt]));
        if (it.note) { kids.push(h("p", { "class": "note" + (state === "blocked" ? " bad" : ""), text: it.note })); }
        var start = it.project ? line(["launch", it.project, "--task-id", it.id]) : null;
        if (call("onStart")) { acts.push(button("start", { label: "Start", solid: true, glyph: "▸", cmd: start || "opens the launch sheet with this task chosen", disabledReason: state === "dropped" ? "it was dropped" : state === "done" ? "it is done" : it.disabledReason, onPress: function () { act("start", "starting…", "Started a session on it", start, function () { return o.onStart(it); }); } })); }
        if (call("onHistory")) { acts.push(button("history", { label: it.hasDetail ? "Details and history" : "History", cmd: line(["task", "show", it.id].concat(where(it))), onPress: go(o.onHistory) })); }
      }

      /* ---------- the waiting list: every waiting item, oldest first, each with its main action ---------- */
      function sorted(items) { return items.slice().sort(function (a, b) { return (a.since || Infinity) - (b.since || Infinity); }); }
      function mainOf(it) {
        if (it.kind === "session" && call("onTypeHere")) { return button("w-" + it.id, { label: "Type here", kind: "needs", glyph: "›", cmd: "opens its terminal with the keyboard in it · changes nothing", onPress: function () { close(false); o.onTypeHere(it); } }); }
        if (it.kind === "approval" && call("onApprove")) { var ap = "$ loadout trigger approve " + it.id; return button("w-" + it.id, { label: "Approve", kind: "needs", solid: true, glyph: "✓", cmd: ap, onPress: function () { act("w-" + it.id, "approving…", "Approved: " + titleOf(it), ap, function () { return o.onApprove(it); }); } }); }
        if (it.kind === "run" || it.kind === "idea") { return button("w-" + it.id, { label: "Answer", kind: "needs", glyph: "?", cmd: "shows its question here · changes nothing", onPress: function () { drill(it); } }); }
        return null;
      }
      function paintList(kids) {
        var items = sorted(cur.list || []);
        if (!items.length) { kids.push(h("p", { "class": "calm", text: "Nothing is waiting on you." })); return; }
        kids.push(h("ul", { "class": "wlist" }, items.map(function (it) {
          return h("li", { "class": "w " + family(wordOf(it, local)) }, [
            h("div", { "class": "top" }, [h("span", { "class": "kind", text: KIND[it.kind] || it.kind }), it.since ? h("span", { "class": "age", text: "waiting " + ago(it.since, now()) }) : null]),
            it.kind === "approval" ? h("p", { "class": "t", text: titleOf(it) }) : h("button", { type: "button", "class": "t", "data-k": "pk-t-" + it.id, text: titleOf(it), "aria-label": "Peek at " + titleOf(it), onclick: function () { drill(it); } }),
            h("div", { "class": "row" }, [sig(wordOf(it, local)), mainOf(it)])]);
        })));
      }
      function drill(it) { var inside = panel.contains(document.activeElement); cur.item = it; cur.back = true; said = ""; tail = null; startTail(); paint(); var f = focusables(inner)[0]; if (f && inside) { f.focus(); } }

      /* ---------- drawing ---------- */
      function paint() {
        keeping(function () {
          inner.textContent = ""; tailEl = null;
          var it = cur.item, listing = !!cur.list && !it, kids = [], acts = [];
          var w = it ? wordOf(it, local) : "", fam = it ? family(w) : listing && (cur.list || []).length ? "needs" : "rest";
          panel.className = "ls-peek " + mode + " " + fam + (cur.pinned ? " pinned" : "") + (o.framed ? " framed" : "") + (cur.sheet || o.framed === "sheet" ? " sheet" : "");
          if (!it && !listing) {
            inner.appendChild(h("p", { "class": "calm", text: o.empty || "Move through the list to see each one here." }));
            panel.removeAttribute("role"); panel.removeAttribute("aria-labelledby"); panel.setAttribute("aria-label", "Preview"); return;
          }
          var head = h("div", { "class": "hd" });
          if (cur.back && it) { head.appendChild(h("button", { type: "button", "class": "back", "data-k": "pk-back", text: "← Waiting on you (" + (cur.list || []).length + ")", onclick: function () { var inside = panel.contains(document.activeElement); cur.item = null; cur.back = false; stopTail(); said = ""; paint(); var f = focusables(inner)[0]; if (f && inside) { f.focus(); } } })); }
          var name = listing ? h("h2", { "class": "ttl count", id: tid }, [h("span", { "class": "no", text: String((cur.list || []).length) }), " waiting on you"])
            : h("h2", { "class": "ttl", id: tid }, [h("span", { "class": "kind", text: KIND[it.kind] || it.kind }), h("span", { "class": "nm", text: titleOf(it) })]);
          head.appendChild(h("div", { "class": "name" }, [name, it ? sig(w) : null]));
          if (cur.pinned || mode === "inline") { head.appendChild(h("button", { type: "button", "class": "x", "data-k": "pk-x", "aria-label": "Close the peek", onclick: function () { close(true); } }, [h("span", { "aria-hidden": "true", text: "✕" }), h("span", { text: "Close" })])); }
          inner.appendChild(head);
          if (listing) { paintList(kids); }
          else { ({ session: session, run: run, idea: idea, task: task, node: node, approval: function (x, k) { k.push(facts([x.rule, x.project, x.detail])); } }[it.kind] || function () {})(it, kids, acts); }
          kids.forEach(function (k) { if (k) { inner.appendChild(k); } });
          if (it && call("onOpen")) { acts.push(button("open", { label: "Open", glyph: "↗", cmd: it.kind === "node" ? "opens its run at this node, in the mode you are in · changes nothing" : "opens it in full, in the mode you are in · changes nothing", onPress: go(o.onOpen) })); }
          if (acts.length) { inner.appendChild(h("div", { "class": "acts" }, acts)); }
          inner.appendChild(h("p", { "class": "ls-said" + (saidBad ? " no" : ""), role: "status", text: said }));
          if ((mode === "float" || o.framed) && !cur.pinned && !listing) { inner.appendChild(h("p", { "class": "hint", text: "The peek button keeps it open for the keyboard · Esc closes it" })); }
          /* Pinned, it is a non-modal dialog named by the item; the list is named by its count; otherwise a group the row points at. */
          if (mode === "float") { panel.setAttribute("role", cur.pinned || listing ? "dialog" : "group"); if (cur.pinned || listing) { panel.setAttribute("aria-modal", "false"); } else { panel.removeAttribute("aria-modal"); } }
          else { panel.setAttribute("role", "region"); panel.removeAttribute("aria-modal"); }
          panel.removeAttribute("aria-label"); panel.setAttribute("aria-labelledby", tid);
        });
        place();
      }

      /* ---------- where it goes ---------- */
      function anchorEl() { return cur.list && !cur.row ? cur.anchor : cur.row; }
      function place() {
        if (mode !== "float" || panel.hidden) { return; }
        var a = anchorEl(), vw = document.documentElement.clientWidth, vh = window.innerHeight, m = 8, gap = 8;
        var narrow = vw <= 600;
        if (narrow !== !!cur.sheet) { cur.sheet = narrow; panel.classList.toggle("sheet", narrow); }
        if (narrow || !a) { panel.style.left = ""; panel.style.top = ""; return; }
        var r = a.getBoundingClientRect(), w = panel.offsetWidth, ht = panel.offsetHeight, left, top;
        if (r.right + gap + w <= vw - m) { left = r.right + gap; top = r.top; }
        else if (r.left - gap - w >= m) { left = r.left - gap - w; top = r.top; }
        else {
          left = clamp(r.left, m, Math.max(m, vw - w - m)); top = r.bottom + 6;
          if (top + ht > vh - m && r.top - 6 - ht >= m) { top = r.top - 6 - ht; }
        }
        panel.style.left = Math.round(left) + "px"; panel.style.top = Math.round(clamp(top, m, Math.max(m, vh - ht - m))) + "px";
      }
      function onMove() { if (!isOpen() || mode !== "float") { return; } var a = anchorEl(); if (a && !a.isConnected) { close(false); return; } place(); }

      /* ---------- opening and closing ---------- */
      function startTail() { stopTail(); readTail(); if (every > 0 && reader(cur.item)) { poll = setInterval(readTail, every); } }
      function stopTail() { if (poll) { clearInterval(poll); poll = null; } }
      function mark(open) {
        if (cur.btn) { cur.btn.setAttribute("aria-expanded", open ? "true" : "false"); }
        if (cur.anchor) { cur.anchor.setAttribute("aria-expanded", open ? "true" : "false"); }
      }
      function show(item, row, how) {
        how = how || {};
        clearTimeout(openTimer); clearTimeout(closeTimer);
        var same = cur.item && item && cur.item.kind === item.kind && cur.item.id === item.id && !cur.list;
        if (!same) { said = ""; saidBad = false; busy = null; tail = null; }
        mark(false);
        cur.item = item; cur.list = null; cur.back = false; cur.anchor = null;
        cur.row = row || null; cur.btn = row ? row.__peekBtn || null : null; cur.pinned = !!how.pin;
        lastWord = item ? wordOf(item, local) : null;
        if (mode === "inline" && row) { var at = row.__peekAfter || row; if (panel.previousSibling !== at || !panel.isConnected) { if (at === row && /^(UL|OL)$/.test(row.parentNode && row.parentNode.tagName)) { row.appendChild(panel); } else { at.parentNode.insertBefore(panel, at.nextSibling); } } }
        var was = panel.hidden;
        panel.hidden = false;
        paint();
        /* Never over a terminal you are typing in: an opening that would cover it does not happen. */
        var act = document.activeElement;
        if (!how.pin && mode === "float" && inTerminal(act)) {
          var t = act.closest(".ls-term, .xterm, [data-terminal]") || act;
          if (overlaps(t.getBoundingClientRect(), panel.getBoundingClientRect())) { panel.hidden = true; cur.item = null; cur.row = null; stopTail(); return false; }
        }
        if (was && mode !== "preview" && full()) { panel.classList.remove("anim"); void panel.offsetWidth; panel.classList.add("anim"); }
        mark(true); startTail(); watch(true);
        return true;
      }
      function list(items, anchor) {
        if (cur.list && cur.anchor === anchor && isOpen() && !cur.item) { close(true); return; }
        clearTimeout(openTimer); clearTimeout(closeTimer); mark(false); stopTail();
        cur.item = null; cur.row = null; cur.btn = null; cur.list = items || []; cur.anchor = anchor || null; cur.pinned = true; cur.back = false; said = "";
        if (anchor) { anchor.setAttribute("aria-controls", pid); anchor.setAttribute("aria-haspopup", "dialog"); }
        var was = panel.hidden; panel.hidden = false; paint();
        if (was && full()) { panel.classList.remove("anim"); void panel.offsetWidth; panel.classList.add("anim"); }
        mark(true); watch(true);
      }
      function close(refocus) {
        clearTimeout(openTimer); clearTimeout(closeTimer); stopTail();
        var back = refocus && (panel.contains(document.activeElement) || document.activeElement === document.body) ? (cur.btn || cur.anchor || null) : null;
        mark(false);
        cur.item = null; cur.list = null; cur.pinned = false; cur.back = false; busy = null; said = "";
        if (mode === "preview") { paint(); } else { panel.hidden = true; panel.classList.remove("anim"); if (mode === "inline" && panel.parentNode) { panel.remove(); } }
        cur.row = null; cur.btn = null; cur.anchor = null;
        watch(false);
        if (back && back.isConnected) { hush = true; back.focus(); hush = false; }
      }
      function pin() { if (!isOpen() || cur.pinned) { return; } cur.pinned = true; paint(); }
      var watching = false, hush = false;
      function watch(on) {
        if (mode !== "float" || on === watching) { return; }
        watching = on;
        var f = on ? "addEventListener" : "removeEventListener";
        window[f]("scroll", onMove, true); window[f]("resize", onMove);
      }

      /* Escape closes it first, before anything else that claims Escape: never from a terminal, nor from a modal dialog over it. */
      function onKey(e) {
        if (e.key !== "Escape" || mode === "preview" || !isOpen()) { return; }
        var a = document.activeElement;
        if (inTerminal(a) || inTerminal(e.target)) { return; }
        if (a && a.closest && a.closest(".ls-dialog, [aria-modal=\"true\"]") && !panel.contains(a)) { return; }
        e.preventDefault(); e.stopPropagation(); if (e.stopImmediatePropagation) { e.stopImmediatePropagation(); }
        close(true);
      }
      /* A click away closes one that is not pinned. */
      function onDown(e) {
        if (!isOpen() || mode === "preview" || cur.pinned) { return; }
        var t = e.target;
        if (panel.contains(t) || (cur.row && cur.row.contains(t))) { return; }
        close(false);
      }
      /* On hover a command floats under its button; inside the panel it is kept within the panel, which clips what overflows. */
      function fitFloats() {
        var box = panel.getBoundingClientRect();
        Array.prototype.forEach.call(panel.querySelectorAll(".ls-cmd:not(.reason), .ls-cmdline"), function (n) {
          var cs = getComputedStyle(n);
          if (cs.position !== "absolute" || cs.display === "none") { return; }
          n.style.maxWidth = Math.max(160, box.width - 28) + "px"; n.style.marginLeft = "";
          var r = n.getBoundingClientRect(), over = r.right - (box.right - 10);
          if (over > 0) { n.style.marginLeft = -Math.min(over, Math.max(0, r.left - box.left - 10)) + "px"; }
        });
      }
      function queueFit() { requestAnimationFrame(function () { requestAnimationFrame(fitFloats); }); }
      panel.addEventListener("pointerover", queueFit); panel.addEventListener("focusin", queueFit); panel.addEventListener("keyup", queueFit);
      window.addEventListener("keydown", onKey, true);
      document.addEventListener("pointerdown", onDown, true);
      panel.addEventListener("pointerenter", function () { clearTimeout(closeTimer); });
      panel.addEventListener("pointerleave", function () { later(); });
      /* Using it pins it: a press inside an open peek keeps it. */
      panel.addEventListener("pointerdown", function () { if (mode === "float" && isOpen() && !cur.pinned) { pin(); } });
      panel.addEventListener("focusout", function () { setTimeout(function () { if (isOpen() && !cur.pinned && mode !== "preview" && !panel.contains(document.activeElement) && !(cur.row && cur.row.contains(document.activeElement))) { close(false); } }, 0); });
      /* Tab off either end goes back to the row's peek button, or on past it. */
      panel.addEventListener("keydown", function (e) {
        if (e.key !== "Tab" || mode !== "float") { return; }
        var f = focusables(panel), from = cur.btn || cur.anchor;
        if (!f.length || !from) { return; }
        if (e.shiftKey && document.activeElement === f[0]) { e.preventDefault(); from.focus(); }
        else if (!e.shiftKey && document.activeElement === f[f.length - 1]) {
          var all = focusables(document.body).filter(function (n) { return !panel.contains(n); }), i = all.indexOf(from), next = all[i + 1];
          e.preventDefault(); close(false); (next || from).focus();
        }
      });
      function later() {
        clearTimeout(closeTimer);
        if (!isOpen() || cur.pinned || mode === "preview") { return; }
        closeTimer = setTimeout(function () { if (!cur.pinned && !panel.contains(document.activeElement) && !panel.matches(":hover") && !(cur.row && cur.row.matches(":hover"))) { close(false); } }, 220);
      }

      /* ---------- wiring a row ---------- */
      function attach(row, get, opt) {
        opt = opt || {};
        var btn = opt.button || row.querySelector("[data-peek]");
        if (!btn) {
          /* word: the button says it, for a place where a bare ring would read as an empty button (a toast's actions). */
          btn = h("button", { type: "button", "class": "ls-peek-btn" + (opt.word ? " worded" : ""), "data-peek": "" }, [h("span", { "class": "ring", "aria-hidden": "true" }), h("span", { "class": opt.word ? "w" : "ls-vh", text: opt.word || "Peek" })]);
          (opt.into || row).appendChild(btn);
        }
        var target = opt.target || (row.hasAttribute("tabindex") ? row : row.querySelector("a[href], button:not([data-peek]), [tabindex]")) || row;
        var sid = uid("ls-pk-s-"), sum = h("span", { "class": "ls-vh", id: sid });
        row.appendChild(sum);
        row.__peekBtn = btn; if (opt.after) { row.__peekAfter = opt.after; }
        function item() { var it = get(); sum.textContent = it ? gist(it, local) : ""; return it; }
        item();
        btn.setAttribute("aria-controls", pid); btn.setAttribute("aria-expanded", "false");
        if (mode === "float") { btn.setAttribute("aria-haspopup", "dialog"); }
        if (!btn.getAttribute("aria-label")) { btn.setAttribute("aria-label", "Peek at " + titleOf(get() || {})); }
        target.setAttribute("aria-describedby", [target.getAttribute("aria-describedby"), sid].filter(Boolean).join(" "));
        target.setAttribute("aria-controls", pid);
        var pointing = false;
        function down() { pointing = true; document.addEventListener("pointerup", function up() { document.removeEventListener("pointerup", up, true); setTimeout(function () { pointing = false; }, 0); }, true); }
        var mine = function () { return cur.row === row && isOpen(); };
        var taken = function () { return isOpen() && cur.pinned && cur.row !== row; };
        function enter() {
          if (mode !== "float" || taken() || mine()) { clearTimeout(closeTimer); return; }
          clearTimeout(openTimer);
          openTimer = setTimeout(function () { if (row.matches(":hover") && !taken()) { show(item(), row); } }, delay);
        }
        function leave() { clearTimeout(openTimer); if (mine()) { later(); } }
        function focusIn(e) {
          /* Focus from a press is not a keyboard visit: the press itself decides (a sheet opened at pointerdown would take the click). */
          if (pointing) { return; }
          if (hush || panel.contains(e.target) || taken() || (mine() && cur.pinned)) { return; }
          if (e.target === btn && mine()) { return; }
          clearTimeout(openTimer); show(item(), row);
        }
        function focusOut() { setTimeout(function () { var a = document.activeElement; if (mine() && !cur.pinned && mode !== "preview" && !row.contains(a) && !panel.contains(a)) { close(false); } }, 0); }
        function press(e) { e.preventDefault(); e.stopPropagation(); if (mine() && cur.pinned) { close(true); } else { show(item(), row, { pin: true }); } }
        function tab(e) { if (e.key === "Tab" && !e.shiftKey && mine() && cur.pinned && mode !== "preview") { var f = focusables(panel)[0]; if (f) { e.preventDefault(); f.focus(); } } }
        row.addEventListener("pointerenter", enter); row.addEventListener("pointerleave", leave); row.addEventListener("pointerdown", down);
        row.addEventListener("focusin", focusIn); row.addEventListener("focusout", focusOut);
        btn.addEventListener("click", press); btn.addEventListener("keydown", tab);
        return function detach() {
          row.removeEventListener("pointerenter", enter); row.removeEventListener("pointerleave", leave); row.removeEventListener("pointerdown", down);
          row.removeEventListener("focusin", focusIn); row.removeEventListener("focusout", focusOut);
          btn.removeEventListener("click", press); btn.removeEventListener("keydown", tab);
          if (mine()) { close(false); }
          sum.remove();
        };
      }

      /* A fresh read of what is open: drawn again in place, keeping what you typed; a change of state is said politely. */
      function set(item) {
        if (!item) { return; }
        if (cur.list && !cur.item) { return; }
        if (!cur.item || cur.item.kind !== item.kind || cur.item.id !== item.id) { return; }
        cur.item = item; if (item.tail) { tail = item.tail; }
        var w = wordOf(item, local);
        if (w !== lastWord) { live.textContent = titleOf(item) + ": " + w + (item.waitingFor ? ", waiting for " + item.waitingFor : "") + "."; lastWord = w; }
        paint();
      }
      function setList(items) { if (cur.list) { cur.list = items || []; if (!cur.item) { live.textContent = cur.list.length + " waiting on you."; paint(); } } }
      function destroy() { close(false); window.removeEventListener("keydown", onKey, true); document.removeEventListener("pointerdown", onDown, true); watch(false); panel.remove(); }
      if (mode === "preview") { paint(); }
      return {
        el: panel, attach: attach, show: function (item, row, how) { return show(item, row, how); }, list: list, pin: pin, close: function () { close(true); }, set: set, setList: setList, destroy: destroy,
        get item() { return cur.item; }, get pinned() { return cur.pinned; }, get open() { return isOpen(); }
      };
    }
    NS.Peek = Peek;
  })();

  /* --- ProgressRing --- */
  (function () {
    /* ProgressRing: a ring that fills for stages, the Meter's round form. As main's ProgressRing.svelte. */
    var C = 2 * Math.PI * 48, SVGNS = "http://www.w3.org/2000/svg";
    function circle(cls, extra) {
      var c = document.createElementNS(SVGNS, "circle");
      c.setAttribute("class", cls); c.setAttribute("cx", "60"); c.setAttribute("cy", "60"); c.setAttribute("r", "48");
      if (extra) { c.setAttribute("transform", extra); }
      return c;
    }
    function ProgressRing(o) {
      o = o || {};
      var wrap = h("div", { "class": "ls-progress" });
      var svg = document.createElementNS(SVGNS, "svg");
      svg.setAttribute("viewBox", "0 0 120 120"); svg.setAttribute("aria-hidden", "true");
      var fg = circle("fg", "rotate(-90 60 60)");
      svg.appendChild(circle("bg")); svg.appendChild(fg);
      var pct = h("span", { "class": "pct", "aria-hidden": "true" });
      wrap.appendChild(svg); wrap.appendChild(pct);
      function set(next) {
        var v = typeof next === "number" ? next : (next && next.value !== undefined ? next.value : 0);
        var f = Math.max(0, Math.min(1, Number(v) || 0));
        fg.style.strokeDasharray = (C * f).toFixed(1) + " " + C.toFixed(1);
        pct.textContent = Math.round(f * 100) + "%";
      }
      set(o.value || 0);
      return { el: wrap, set: set };
    }
    NS.ProgressRing = ProgressRing;
  })();

  /* --- QueuePanel --- */
  (function () {
    /* QueuePanel: one team's queue of fully specified tasks: what waits, in order, and what would
       be refused now and why; what it has run; and what it is allowed. The draft pull request
       grant is said first when it is on, because it is the one thing here that reaches outside
       this machine, and it is turned off from the same line that says so. */

    /** A team's name as an owner: builders' and docs-crew's. */
    function whose(team) { return /s$/.test(team) ? team + "'" : team + "'s"; }
    function money(usd) { return "$" + Number(usd).toFixed(2); }
    function at(iso) { return new Date(iso).toLocaleString("en-GB", { dateStyle: "short", timeStyle: "short" }); }
    function span(minutes) {
      return minutes % 60 === 0 ? minutes / 60 + "h" : minutes > 60 ? Math.floor(minutes / 60) + "h" + (minutes % 60) + "m" : minutes + "m";
    }

    // main's HaloButton, including inactive: does nothing and says so, where the reason is written beside it.
    function halo(o) {
      var x = {}; Object.keys(o).forEach(function (k) { if (k !== "onpress" && k !== "inactive") { x[k] = o[k]; } });
      // main shows the reason in place of the command (disabledReason ?? cmd).
      if (o.disabledReason) { delete x.cmd; }
      x.onPress = function (e) { if (!o.inactive && o.onpress) { o.onpress(e); } };
      var el = NS.HaloButton(x);
      if (o.inactive) { var b = el.tagName === "BUTTON" ? el : el.querySelector("button"); if (b) { b.setAttribute("aria-disabled", "true"); } }
      return el;
    }
    var FOCUSABLE = "button, input, select, textarea, a[href]";
    function focusables(root) { return Array.prototype.slice.call(root.querySelectorAll(FOCUSABLE)); }
    // Redraws without losing the place: focus goes back to the same control inside the same [data-k].
    function keep(root, fn) {
      var a = document.activeElement, key = null, idx = -1;
      if (a && root.contains(a) && a.closest) { var w = a.closest("[data-k]"); if (w && root.contains(w)) { key = w.getAttribute("data-k"); idx = focusables(w).indexOf(a); } }
      fn();
      if (key === null) { return; }
      var nw = root.querySelector("[data-k=\"" + key + "\"]");
      if (!nw) { return; }
      var list = focusables(nw), t = list[idx] || list[0];
      if (t) { t.focus(); }
    }
    function input(id, value, attrs, oninput) {
      var a = { id: id, "class": "ls-text", type: "text", autocomplete: "off" };
      Object.keys(attrs || {}).forEach(function (k) { a[k] = attrs[k]; });
      var el = h(a.rows ? "textarea" : "input", a);
      if (a.rows) { el.removeAttribute("type"); }
      el.value = value;
      el.addEventListener("input", function () { oninput(el.value); });
      return el;
    }
    function lab(id, words) { return h("label", { "class": "lab", "for": id, text: words }); }
    function select(id, value, options, attrs, onchange) {
      var a = { id: id, "class": "ls-text" };
      Object.keys(attrs || {}).forEach(function (k) { a[k] = attrs[k]; });
      var el = h("select", a, options.map(function (op) { return h("option", { value: op[0], text: op[1] }); }));
      el.value = value;
      el.addEventListener("change", function () { onchange(el.value); });
      return el;
    }

    function QueuePanel(o) {
      o = o || {};
      var queue = o.queue, projects = o.projects || [], tasksOf = o.tasksOf, propose = o.propose, actFn = o.act, readPlan = o.readPlan, disabledReason = o.disabledReason;
      var now = o.now || function () { return Date.now(); };
      var id = uid("ls-q-");
      var el = h("section", { "class": "ls-queue" });

      function act(path, words, done) {
        return Promise.resolve().then(function () { return actFn ? actFn(path, words, done) : undefined; }).then(function (r) { return r !== false; });
      }

      // UI state, kept across a new read of the queue.
      var timing = false, opensAt = "22:00", openFor = "8h";
      var adding = false, project = "", tasks = null, task = "", after = "";
      var conditions = "", proposing = false, proposedWhy = null, proposeError = null;
      var editing = false, usd = "", runs = "", heldCap = "";
      var plan = null, planError = null;

      function team() { return queue.team; }
      function settings() { return queue.settings || {}; }
      function offered() { return (tasks || []).filter(function (one) { return one.kind === "task" && one.state !== "done" && one.state !== "dropped"; }); }
      function chosen() { return offered().filter(function (one) { return one.id === task; })[0] || null; }
      function lacking() { var c = chosen(); return !!c && !!c.doneWhen && c.doneWhen.length === 0; }
      function lines() { return conditions.split("\n").map(function (x) { return x.trim(); }).filter(function (x) { return x.length > 0; }); }
      function windowWords() { return [team(), "--at", opensAt.trim(), "--for", openFor.trim()]; }
      function addWords() { return [team(), task, "--project", project].concat(after ? ["--after", after] : []); }
      // As option=value, because a condition can start with a dash, which the command line would read as an option of its own.
      function declareWords() { var c = chosen(); return [task, c ? c.state : "open", "--project", project].concat(lines().map(function (x) { return "--done-when=" + x; })); }
      // Only what changed is sent, so saving one limit never rewrites another somebody changed from a terminal meanwhile.
      function limitWords() {
        var s = settings(), words = [];
        if (usd.trim() !== (s.windowUsd || "")) { words.push("--window-usd", usd.trim() || "none"); }
        if (runs.trim() !== (s.windowRuns ? String(s.windowRuns) : "")) { words.push("--window-runs", runs.trim() || "none"); }
        if (heldCap.trim() && heldCap.trim() !== String(s.heldCap)) { words.push("--held-cap", heldCap.trim()); }
        return words;
      }

      // Cleared when a different task is chosen, so one task's conditions are never saved onto another.
      function setTask(next) { if (next !== task) { conditions = ""; proposedWhy = null; proposeError = null; } task = next; }

      function editWindow() {
        var w = queue.window || null;
        opensAt = (w && w.at) || "22:00"; openFor = w ? span(w.lengthMinutes) : "8h"; timing = true; paint();
      }
      function saveWindow() { act("team queue window", windowWords(), whose(team()) + " queue has its window").then(function (ok) { if (ok) { timing = false; paint(); } }); }
      function pickProject(slug) {
        project = slug; setTask(""); after = ""; tasks = null; paint();
        if (slug && tasksOf) {
          Promise.resolve().then(function () { return tasksOf(slug); }).catch(function () { return []; }).then(function (t) { if (project === slug) { tasks = t || []; paint(); } });
        }
      }
      function askForThem() {
        if (!propose || !task) { return; }
        proposing = true; proposeError = null; paint();
        var p = project, t = task;
        Promise.resolve().then(function () { return propose(p, t); }).then(function (proposed) {
          conditions = proposed.proposed.join("\n");
          proposedWhy = proposed.why ? proposed.why + (proposed.costUsd > 0 ? " (" + money(proposed.costUsd) + ")" : "") : null;
        }, function (error) { proposeError = (error && error.message) || String(error); }).then(function () { proposing = false; paint(); });
      }
      function saveThem() {
        var t = task, kept = lines();
        act("task declare", declareWords(), t + " has its done-when").then(function (ok) {
          if (!ok) { return; }
          tasks = (tasks || []).map(function (one) { if (one.id !== t) { return one; } var c = {}; Object.keys(one).forEach(function (k) { c[k] = one[k]; }); c.doneWhen = kept; return c; });
          conditions = ""; proposedWhy = null; paint();
        });
      }
      function add() {
        var t = task;
        act("team queue add", addWords(), t + " is in " + whose(team()) + " queue").then(function (ok) { if (ok) { adding = false; setTask(""); after = ""; paint(); } });
      }
      function edit() {
        var s = settings();
        usd = s.windowUsd || ""; runs = s.windowRuns ? String(s.windowRuns) : ""; heldCap = String(s.heldCap); editing = true; paint();
      }
      function saveLimits() { act("team queue settings", [team()].concat(limitWords()), whose(team()) + " queue limits are saved").then(function (ok) { if (ok) { editing = false; paint(); } }); }
      function showPlan() {
        planError = null;
        Promise.resolve().then(function () { return readPlan(); }).then(function (p) { plan = p; paint(); }, function (error) { plan = null; planError = (error && error.message) || String(error); paint(); });
      }

      // Slots for buttons whose line follows what is typed, so typing never redraws the field.
      function slot(key, make) {
        var s = h("div", { "class": "slot", "data-k": key });
        function fill() { keep(s, function () { s.textContent = ""; s.appendChild(make()); }); }
        fill(); s.fill = fill;
        return s;
      }

      function paint() {
        keep(el, function () {
          el.textContent = "";
          var T = team(), s = settings(), owner = whose(T);
          var items = queue.items || [], allRuns = queue.runs || [];
          var recent = allRuns.slice().reverse().slice(0, 8);
          var held = allRuns.filter(function (r) { return r.state === "held"; }).length;
          var going = allRuns.filter(function (r) { return r.state === "running"; })[0] || null;
          // The window being worked in when there is one, since that is what its caps count; otherwise the last day's.
          var since = queue.windowOpened ? Date.parse(queue.windowOpened) : now() - 86400000;
          var daySpend = allRuns.filter(function (r) { return Date.parse(r.started) >= since; }).reduce(function (sum, r) { return sum + r.costUsd; }, 0);
          var spendWhen = queue.windowOpened ? "in this window" : "in the last day";
          var opening = queue.window || null;
          var limits = [s.windowUsd ? "$" + s.windowUsd + " a window" : "no spending cap of its own", s.windowRuns ? s.windowRuns + " run(s) a window" : "no cap on runs", "at most " + s.heldCap + " held"].join(" · ");

          el.setAttribute("aria-label", owner + " queue");
          el.appendChild(h("header", {}, [h("b", { text: T }), h("em", { text: limits + (daySpend > 0 ? " · " + money(daySpend) + " " + spendWhen : "") })]));

          el.appendChild(h("div", { "class": "grant", role: "group", "aria-label": "When " + owner + " queue runs", "data-k": id + "-win" }, opening ? [
            h("p", {}, ["Worked through daily from ", h("strong", { text: opening.at }), " for " + span(opening.lengthMinutes) + ", " + opening.autonomy + (opening.open ? " · open now" : opening.next ? " · next from " + at(opening.next) : "") + "."]),
            halo({ label: "No window", size: "small", cmd: "$ loadout team queue window " + T + " --off", disabledReason: disabledReason, onpress: function () { act("team queue window", [T, "--off"], owner + " queue has no window"); } }),
            halo({ label: timing ? "Not now" : "Change the window", size: "small", cmd: timing ? undefined : "opens " + owner + " queue window", disabledReason: disabledReason, onpress: function () { if (timing) { timing = false; paint(); } else { editWindow(); } } })
          ] : [
            h("p", { text: "No window: its runs start only when you press Run the next one." }),
            halo({ label: timing ? "Not now" : "Give it a window", size: "small", cmd: timing ? undefined : "opens " + owner + " queue window", disabledReason: disabledReason, onpress: function () { if (timing) { timing = false; paint(); } else { editWindow(); } } })
          ]));

          if (timing) {
            var saveWin = slot(id + "-savewin", function () {
              return halo({ label: "Save the window", solid: true, cmd: "$ loadout team queue window " + windowWords().join(" "), disabledReason: disabledReason || (!opensAt.trim() || !openFor.trim() ? "Say when it opens and for how long" : undefined), onpress: saveWindow });
            });
            el.appendChild(h("form", { "class": "qform", "aria-label": owner + " queue window", onsubmit: function (e) { e.preventDefault(); saveWindow(); } }, [
              h("div", { "class": "ls-field" }, [lab(id + "-at", "Opens each day at, this machine's time"), input(id + "-at", opensAt, { placeholder: "22:00" }, function (v) { opensAt = v; saveWin.fill(); })]),
              h("div", { "class": "ls-field" }, [lab(id + "-for", "Stays open for"), input(id + "-for", openFor, { placeholder: "8h" }, function (v) { openFor = v; saveWin.fill(); })]),
              h("p", { "class": "calm small", text: "The daemon has to be running for anything to happen in it." }),
              saveWin]));
          }

          if (s.prMode === "ready") {
            el.appendChild(h("div", { "class": "grant on", role: "note", "data-k": id + "-pr" }, [
              h("p", {}, ["It may push a branch and open a ", h("strong", { text: "pull request ready for review" }), " when a queued run meets its done-when. Merging still needs you."]),
              halo({ label: "Stop that", size: "small", kind: "danger", cmd: "$ loadout team queue settings " + T + " --pr-mode none", disabledReason: disabledReason, onpress: function () { act("team queue settings", [T, "--pr-mode", "none"], owner + " queue opens no pull requests"); } })]));
          } else if (s.draftPullRequests) {
            el.appendChild(h("div", { "class": "grant on", role: "note", "data-k": id + "-pr" }, [
              h("p", {}, ["It may push a branch and open a ", h("strong", { text: "draft pull request" }), " when a queued run meets its done-when. Merging still needs you."]),
              halo({ label: "Stop that", size: "small", kind: "danger", cmd: "$ loadout team queue settings " + T + " --draft-prs off", disabledReason: disabledReason, onpress: function () { act("team queue settings", [T, "--draft-prs", "off"], owner + " queue opens no pull requests"); } })]));
          } else {
            el.appendChild(h("div", { "class": "grant", role: "note", "data-k": id + "-pr" }, [
              h("p", { text: "It opens no pull requests: a run that meets its done-when keeps its branch on this machine." }),
              halo({ label: "Let it open drafts", size: "small", cmd: "$ loadout team queue settings " + T + " --draft-prs on", disabledReason: disabledReason, onpress: function () { act("team queue settings", [T, "--draft-prs", "on"], owner + " queue may open draft pull requests"); } })]));
          }

          if (going || held) {
            el.appendChild(h("p", { "class": "ls-note needs", text: (going ? going.task + " is running." : "") + (held ? " " + held + " held, waiting for an answer." : "") }));
          }

          if (!items.length) {
            el.appendChild(h("p", { "class": "ls-note", text: "Nothing is waiting." }));
          } else {
            el.appendChild(h("ol", { "aria-label": "Waiting in " + owner + " queue" }, items.map(function (item, index) {
              var first = index === 0, last = index === items.length - 1;
              return h("li", { "class": item.refused ? "refused" : null }, [
                h("div", { "class": "t" }, [h("b", { text: item.position + ". " + item.task }), h("span", { text: item.project + (item.after ? ", after " + item.after : "") }),
                  item.refused ? h("em", { "class": "why", text: "Would be refused: " + item.refused }) : null]),
                h("div", { "class": "acts", "data-k": id + "-i-" + item.project + "-" + item.task }, [
                  halo({ label: "Up", size: "small", inactive: first, cmd: first ? "already first" : "$ loadout team queue move " + T + " " + item.task + " " + (item.position - 1), disabledReason: disabledReason,
                    onpress: function () { act("team queue move", [T, item.task, String(item.position - 1), "--project", item.project], item.task + " moved up"); } }),
                  halo({ label: "Down", size: "small", inactive: last, cmd: last ? "already last" : "$ loadout team queue move " + T + " " + item.task + " " + (item.position + 1), disabledReason: disabledReason,
                    onpress: function () { act("team queue move", [T, item.task, String(item.position + 1), "--project", item.project], item.task + " moved down"); } }),
                  halo({ label: "Take out", size: "small", cmd: "$ loadout team queue remove " + T + " " + item.task, disabledReason: disabledReason,
                    onpress: function () { act("team queue remove", [T, item.task, "--project", item.project], item.task + " is out of the queue"); } })])]);
            })));
          }

          el.appendChild(h("div", { "class": "row", "data-k": id + "-row" }, [
            halo({ label: "Run the next one", cmd: "$ loadout team queue run-next " + T, disabledReason: disabledReason, onpress: function () { act("team queue run-next", [T], owner + " queue started its next task"); } }),
            halo({ label: "What would run", cmd: "$ loadout team queue plan " + T, onpress: showPlan }),
            halo({ label: adding ? "Not now" : "Add a task", cmd: adding ? undefined : "opens Add a task to " + owner + " queue", disabledReason: disabledReason, onpress: function () { adding = !adding; paint(); } }),
            halo({ label: editing ? "Not now" : "Change its limits", cmd: editing ? undefined : "opens " + owner + " queue limits", disabledReason: disabledReason, onpress: function () { if (editing) { editing = false; paint(); } else { edit(); } } })]));

          if (plan) {
            var p = plan;
            el.appendChild(h("div", { "class": "plan", role: "region", "aria-label": "What " + owner + " queue would run" }, [
              p.order.length ? h("ol", {}, p.order.map(function (one) { return h("li", {}, [one.task + " ", h("span", { text: "on " + one.project })]); })) : h("p", { text: "Nothing would run." })]
              .concat(p.left.map(function (one) { return h("p", { "class": "why", text: "Left: " + one.task + " on " + one.project + ". " + one.reason }); }))
              // The rules' reason for waiting is the left-out reasons joined, said once above.
              .concat([h("p", { "class": "calm small", text: (p.left.some(function (one) { return p.stops.indexOf(one.reason) >= 0; }) ? "" : p.stops + " ") + "If every run met its done-when." })])));
          } else if (planError) {
            el.appendChild(h("p", { "class": "ls-note needs", text: planError }));
          }

          if (adding) {
            var off = offered(), c = chosen();
            var formKids = [
              h("div", { "class": "ls-field", "data-k": id + "-project" }, [lab(id + "-project", "Project"),
                select(id + "-project", project, [["", "Choose a project"]].concat(projects.map(function (slug) { return [slug, slug]; })), {}, pickProject)]),
              h("div", { "class": "ls-field", "data-k": id + "-task" }, [lab(id + "-task", "Task"),
                select(id + "-task", task, [["", project && tasks === null ? "Reading…" : "Choose a task"]].concat(off.map(function (one) { return [one.id, one.id + " · " + one.title + (one.doneWhen && !one.doneWhen.length ? " (no done-when yet)" : "")]; })),
                  { disabled: !project || tasks === null }, function (v) { setTask(v); paint(); })]),
              h("div", { "class": "ls-field", "data-k": id + "-after" }, [lab(id + "-after", "After, if it waits for one"),
                select(id + "-after", after, [["", "Nothing, run it in turn"]].concat(off.filter(function (one) { return one.id !== task; }).map(function (one) { return [one.id, one.id]; })),
                  { disabled: !task }, function (v) { after = v; paint(); })])];
            if (lacking()) {
              var saveDone = slot(id + "-savedone", function () {
                return halo({ label: "Save them", cmd: "$ loadout task declare " + task + " " + (c ? c.state : "open") + " --project " + project + " --done-when …",
                  disabledReason: disabledReason || (!lines().length ? "Write or propose at least one" : undefined), onpress: saveThem });
              });
              formKids.push(h("div", { "class": "ls-field", role: "group", "aria-label": "Done-when for " + task }, [
                lab(id + "-done", task + " has no done-when yet, so the queue would refuse it. One condition a line:"),
                input(id + "-done", conditions, { rows: "4", placeholder: "What must be true for it to be finished" }, function (v) { conditions = v; saveDone.fill(); }),
                proposedWhy ? h("p", { "class": "calm small", text: proposedWhy }) : null,
                proposeError ? h("p", { "class": "ls-note needs", text: proposeError }) : null,
                h("div", { "class": "row", "data-k": id + "-donerow" }, [
                  propose ? halo({ label: proposing ? "Asking an agent…" : "Propose them", cmd: "$ loadout task propose-done-when " + task + " --project " + project,
                    disabledReason: disabledReason || (proposing ? "An agent is already working on it" : undefined), onpress: askForThem }) : null,
                  saveDone])]));
            }
            formKids.push(h("p", { "class": "calm small", text: "Only a task with a title and a done-when gets in, and you are told why otherwise." }));
            formKids.push(h("div", { "class": "slot", "data-k": id + "-addit" }, [halo({ label: "Add it", solid: true, cmd: "$ loadout team queue add " + addWords().join(" "), disabledReason: disabledReason || (!task ? "Choose a task first" : undefined), onpress: add })]));
            el.appendChild(h("form", { "class": "qform", "aria-label": "Add a task to " + owner + " queue", onsubmit: function (e) { e.preventDefault(); add(); } }, formKids));
          }

          if (editing) {
            var saveLim = slot(id + "-savelim", function () {
              var w = limitWords();
              return halo({ label: "Save them", solid: true, cmd: ("$ loadout team queue settings " + T + " " + w.join(" ")).trim(), disabledReason: disabledReason || (!w.length ? "Nothing has changed" : undefined), onpress: saveLimits });
            });
            el.appendChild(h("form", { "class": "qform", "aria-label": owner + " queue limits", onsubmit: function (e) { e.preventDefault(); saveLimits(); } }, [
              h("div", { "class": "ls-field" }, [lab(id + "-usd", "Most a window may spend, US dollars"), input(id + "-usd", usd, { placeholder: "none" }, function (v) { usd = v; saveLim.fill(); })]),
              h("div", { "class": "ls-field" }, [lab(id + "-runs", "Most runs a window may start"), input(id + "-runs", runs, { inputmode: "numeric", placeholder: "none" }, function (v) { runs = v; saveLim.fill(); })]),
              h("div", { "class": "ls-field" }, [lab(id + "-held", "Runs that may wait at a gate at once"), input(id + "-held", heldCap, { inputmode: "numeric" }, function (v) { heldCap = v; saveLim.fill(); })]),
              saveLim]));
          }

          if (recent.length) {
            el.appendChild(h("h4", { text: "What it ran" }));
            el.appendChild(h("ul", { "aria-label": "Runs " + owner + " queue started" }, recent.map(function (r) {
              return h("li", { "class": "st-" + r.state }, [
                h("div", { "class": "t" }, [h("b", {}, [r.task + " ", h("span", { "class": "state", text: r.state })]),
                  h("em", { text: [r.project, at(r.started), r.run, r.costUsd > 0 ? money(r.costUsd) : null].filter(Boolean).join(" · ") }),
                  r.reason ? h("span", { text: r.reason }) : null]),
                r.run ? h("a", { href: "#teams/" + encodeURIComponent(r.run), text: "The run" }) : null]);
            })));
          }
        });
      }

      paint();

      return {
        el: el,
        set: function (n) {
          n = n || {};
          if ("queue" in n) { queue = n.queue; }
          if ("projects" in n) { projects = n.projects || []; }
          if ("tasksOf" in n) { tasksOf = n.tasksOf; }
          if ("propose" in n) { propose = n.propose; }
          if ("act" in n) { actFn = n.act; }
          if ("readPlan" in n) { readPlan = n.readPlan; }
          if ("disabledReason" in n) { disabledReason = n.disabledReason; }
          if ("now" in n) { now = n.now || function () { return Date.now(); }; }
          paint();
        }
      };
    }

    QueuePanel.whose = whose;
    NS.QueuePanel = QueuePanel;
  })();

  /* --- Spend --- */
  (function () {
    /* Spend: the spend line. What a run has spent, of what, and where it is projected to end. Past its budget is said
       in words with a Needs edge, never in colour alone. As main's Spend.svelte. */
    function money(n) { return "$" + Number(n === null || n === undefined ? 0 : n).toFixed(2); }
    function Spend(o) {
      var el = h("div", { "class": "ls-spend" });
      function paint(p) {
        var limit = p.uncapped ? null : (p.cap === undefined ? null : p.cap), projected = p.projected === undefined ? null : p.projected;
        var over = !!(limit && projected && projected > limit), percent = limit ? Math.min(100, (p.spent / limit) * 100) : 0;
        el.className = "ls-spend" + (over ? " over" : ""); el.textContent = "";
        el.appendChild(h("div", { "class": "w" }, [
          h("b", { text: money(p.spent) }),
          h("span", { text: limit ? "of " + money(limit) : "no cap, on purpose" }),
          p.rate ? h("span", { text: "· " + p.rate }) : null,
          over ? h("em", { text: "projected " + money(projected) + ", past its budget" }) : null
        ]));
        if (limit) {
          var fill = h("i"); fill.style.width = percent.toFixed(1) + "%";
          var mark = null;
          if (!over && projected) { mark = h("u"); mark.style.left = Math.min(100, (projected / limit) * 100).toFixed(1) + "%"; }
          el.appendChild(h("div", { "class": "bar", role: "meter", "aria-label": "Spent", "aria-valuemin": "0", "aria-valuemax": String(limit), "aria-valuenow": String(p.spent),
            "aria-valuetext": money(p.spent) + " of " + money(limit) + (over ? ", projected " + money(projected) + ", past its budget" : "") }, [fill, mark]));
        }
      }
      var cur = Object.assign({ spent: 0 }, o || {});
      paint(cur);
      return { el: el, set: function (patch) { cur = Object.assign({}, cur, patch || {}); paint(cur); } };
    }
    NS.Spend = Spend;
  })();

  /* --- SplitCross --- */
  (function () {
    /* SplitCross: where a column divider and a row divider cross: dragging here moves both, and a double click puts
       both back. It is for the pointer only; the two dividers either side of it already take the keys, so it takes no
       focus and a screen reader is not told about it twice. Ported from main's SplitCross.svelte. */
    var SETTLED = "ls-split-settled";
    function clampSplit(v, min, max) { if (min > max) { return (min + max) / 2; } return Math.min(max, Math.max(min, v)); }
    function SplitCross(o) {
      o = o || {};
      var column = o.column, row = o.row, drag = null;
      var lim = { colMin: 0.1, colMax: 0.9, rowMin: 0.1, rowMax: 0.9 };
      Object.keys(lim).forEach(function (k) { if (o[k] !== undefined) { lim[k] = o[k]; } });
      var reset = o.reset || [0.5, 0.5];
      var el = h("div", { "class": "ls-cross", "aria-hidden": "true", title: "Drag to move both dividers; a double click puts them back" });
      /* The parent's width and height, less one gap each way. */
      function lengths() {
        var parent = el.parentElement;
        if (!parent) { return [0, 0]; }
        var box = parent.getBoundingClientRect(), style = getComputedStyle(parent);
        function gap(v) { return isFinite(parseFloat(v)) ? parseFloat(v) : 0; }
        return [box.width - gap(style.columnGap), box.height - gap(style.rowGap)];
      }
      function tell(c, r) { column = c; row = r; if (o.onchange) { o.onchange(c, r); } }
      function settle() { document.dispatchEvent(new CustomEvent(SETTLED)); }
      el.addEventListener("pointerdown", function (e) {
        var l = lengths();
        if (e.button !== 0 || l[0] <= 0 || l[1] <= 0) { return; }
        e.preventDefault();
        if (el.setPointerCapture) { try { el.setPointerCapture(e.pointerId); } catch (x) { /* a synthetic pointer */ } }
        drag = { x: e.clientX, y: e.clientY, column: column, row: row, wide: l[0], tall: l[1], pointer: e.pointerId };
        el.classList.add("dragging");
      });
      el.addEventListener("pointermove", function (e) {
        if (!drag || e.pointerId !== drag.pointer) { return; }
        var c = clampSplit(drag.column + (e.clientX - drag.x) / drag.wide, lim.colMin, lim.colMax);
        var r = clampSplit(drag.row + (e.clientY - drag.y) / drag.tall, lim.rowMin, lim.rowMax);
        if (c !== column || r !== row) { tell(c, r); }
      });
      function up(e) {
        if (!drag || e.pointerId !== drag.pointer) { return; }
        if (el.releasePointerCapture) { try { el.releasePointerCapture(e.pointerId); } catch (x) { /* already let go */ } }
        drag = null;
        el.classList.remove("dragging");
        settle();
      }
      el.addEventListener("pointerup", up);
      el.addEventListener("pointercancel", up);
      el.addEventListener("dblclick", function () { tell(clampSplit(reset[0], lim.colMin, lim.colMax), clampSplit(reset[1], lim.rowMin, lim.rowMax)); settle(); });
      return {
        el: el,
        /** New props from the page: { column?, row?, colMin?, colMax?, rowMin?, rowMax?, reset? }. */
        set: function (p) {
          p = p || {};
          if (p.column !== undefined) { column = p.column; } if (p.row !== undefined) { row = p.row; } if (p.reset) { reset = p.reset; }
          Object.keys(lim).forEach(function (k) { if (p[k] !== undefined) { lim[k] = p[k]; } });
        }
      };
    }
    NS.SplitCross = SplitCross;
  })();

  /* --- Splitter --- */
  (function () {
    /* Splitter: the divider between two panes of a split, for every split Studio has, so they all drag and take keys
       the same way. It sits in the gap of its grid, placed there by the page's own CSS, and only says where the
       boundary should be: the page turns that into its tracks. The arrows move it a little, Shift with an arrow
       further, Home and End to its limits, and Enter or a double click puts it back where it started.
       Ported from main's Splitter.svelte. */
    var SETTLED = "ls-split-settled", STEP = 0.02, LEAP = 0.1;
    function clampSplit(v, min, max) { if (min > max) { return (min + max) / 2; } return Math.min(max, Math.max(min, v)); }
    function Splitter(o) {
      o = o || {};
      var value = o.value, min = o.min === undefined ? 0.1 : o.min, max = o.max === undefined ? 0.9 : o.max, reset = o.reset;
      var orientation = o.orientation || "vertical", across = orientation === "vertical", drag = null;
      var el = h("div", { "class": ["ls-split", orientation, o["class"] || ""].filter(Boolean).join(" "), role: "separator", tabindex: "0",
        "aria-orientation": orientation, "aria-label": o.label, "aria-controls": o.controls || null,
        title: "Drag, or the arrow keys; Enter or a double click puts it back" }, [h("span", { "class": "grip", "aria-hidden": "true" })]);
      function percent(f) { return Math.round(f * 100); }
      function aria() { el.setAttribute("aria-valuenow", percent(value)); el.setAttribute("aria-valuemin", percent(min)); el.setAttribute("aria-valuemax", percent(max)); }
      function length() {
        if (o.span) { return o.span(); }
        var parent = el.parentElement;
        if (!parent) { return 0; }
        var box = parent.getBoundingClientRect(), gap = parseFloat(getComputedStyle(parent)[across ? "columnGap" : "rowGap"]);
        return (across ? box.width : box.height) - (isFinite(gap) ? gap : 0);
      }
      function move(to) {
        var next = clampSplit(to, min, max);
        if (next !== value) { value = next; aria(); if (o.onchange) { o.onchange(next); } }
      }
      /* Terminals sized while it moved tell their sessions now. */
      function settle() { document.dispatchEvent(new CustomEvent(SETTLED)); }
      el.addEventListener("pointerdown", function (e) {
        if (e.button !== 0) { return; }
        var total = length();
        if (total <= 0) { return; }
        e.preventDefault();
        if (el.setPointerCapture) { try { el.setPointerCapture(e.pointerId); } catch (x) { /* a synthetic pointer */ } }
        el.focus({ preventScroll: true });
        drag = { from: across ? e.clientX : e.clientY, at: value, length: total, pointer: e.pointerId };
        el.classList.add("dragging");
      });
      el.addEventListener("pointermove", function (e) {
        if (!drag || e.pointerId !== drag.pointer) { return; }
        move(drag.at + ((across ? e.clientX : e.clientY) - drag.from) / drag.length);
      });
      function up(e) {
        if (!drag || e.pointerId !== drag.pointer) { return; }
        if (el.releasePointerCapture) { try { el.releasePointerCapture(e.pointerId); } catch (x) { /* already let go */ } }
        drag = null;
        el.classList.remove("dragging");
        settle();
      }
      el.addEventListener("pointerup", up);
      el.addEventListener("pointercancel", up);
      el.addEventListener("keydown", function (e) {
        if (e.ctrlKey || e.altKey || e.metaKey) { return; }
        var back = across ? "ArrowLeft" : "ArrowUp", on = across ? "ArrowRight" : "ArrowDown", by = e.shiftKey ? LEAP : STEP, to = null;
        if (e.key === back) { to = value - by; } else if (e.key === on) { to = value + by; } else if (e.key === "Home") { to = min; } else if (e.key === "End") { to = max; } else if (e.key === "Enter") { to = reset; }
        if (to === null) { return; }
        /* Kept here, so a page's own arrows and Home and End don't also act. */
        e.preventDefault();
        e.stopPropagation();
        move(to);
      });
      el.addEventListener("keyup", function (e) { if (/^(Arrow(Left|Right|Up|Down)|Home|End|Enter)$/.test(e.key)) { settle(); } });
      el.addEventListener("dblclick", function () { move(reset); settle(); });
      aria();
      return {
        el: el,
        /** New props from the page, which may hold the boundary somewhere else: { value?, min?, max?, reset? }. */
        set: function (p) {
          p = p || {};
          if (p.value !== undefined) { value = p.value; } if (p.min !== undefined) { min = p.min; } if (p.max !== undefined) { max = p.max; } if (p.reset !== undefined) { reset = p.reset; }
          aria();
        },
        get value() { return value; }
      };
    }
    /** Two tracks sharing a length by a fraction, as CSS (main's splits.ts tracks). */
    Splitter.tracks = function (first) { return "minmax(0, " + first.toFixed(4) + "fr) minmax(0, " + (1 - first).toFixed(4) + "fr)"; };
    Splitter.clamp = clampSplit;
    Splitter.SETTLED = SETTLED;
    NS.Splitter = Splitter;
  })();

  /* --- StatusBlock --- */
  (function () {
    /* StatusBlock: a hosted session's status in full, for its Details: the context window and what fills it, what the
       session has cost, each of the plan's usage windows with when it resets, and every specialist it was given, by
       name and linked to what it says. Its only action is a command, shown whole, for bringing a line back to the
       terminal: nothing here types into the session. Ported from main's StatusBlock.svelte. */
    var WARN = 70, URGENT = 90;
    function isStale(s, now) { if (!s.refreshSeconds) { return false; } return now - Date.parse(s.at) > s.refreshSeconds * 2000; }
    function pad2(n) { return (n < 10 ? "0" : "") + n; }
    function ago(at, now) {
      var s = Math.max(0, Math.round((now - Date.parse(at)) / 1000));
      if (s < 10) { return "just now"; }
      if (s < 60) { return s + "s ago"; }
      var m = Math.floor(s / 60);
      return m < 60 ? m + "m ago" : Math.floor(m / 60) + "h" + pad2(m % 60) + "m ago";
    }
    function resetsIn(at, now) {
      if (!at) { return null; }
      var m = Math.max(0, Math.round((Date.parse(at) - now) / 60000));
      if (m < 60) { return "resets in " + m + "m"; }
      if (m < 48 * 60) { return "resets in " + Math.floor(m / 60) + "h" + pad2(m % 60) + "m"; }
      return "resets in " + Math.round(m / 1440) + "d";
    }
    function dollars(usd) { if (usd === null || usd === undefined || !isFinite(usd)) { return null; } return usd < 10 ? "$" + usd.toFixed(2) : "$" + Math.round(usd); }
    function tokens(n) { if (n === null || n === undefined || !isFinite(n)) { return null; } if (n < 1000) { return String(n); } return n < 1000000 ? Math.round(n / 1000) + "k" : (n / 1000000).toFixed(1) + "M"; }
    function pct(v) { return Math.round(v) + "%"; }

    function StatusBlock(o) {
      o = o || {};
      var status = o.status, now = o.now === undefined ? Date.now() : o.now;
      var el = h("section", { "class": "ls-sblock", "aria-label": "Status" });
      function pairs(rows) {
        var dl = h("dl");
        rows.forEach(function (r) { if (r) { dl.appendChild(h("dt", { text: r[0] })); dl.appendChild(h("dd", {}, r[1])); } });
        return dl;
      }
      function paint() {
        var s = status || {}, c = s.context, percent = c && c.usedPercent !== null && c.usedPercent !== undefined ? c.usedPercent : null;
        var limits = s.limits, stale = isStale(s, now);
        el.textContent = "";
        el.appendChild(h("h2", { text: "Status" }));
        el.appendChild(h("p", { "class": "when" + (stale ? " stale" : ""),
          text: stale ? "These figures are " + ago(s.at, now).replace(" ago", "") + " old: the session has not redrawn its line since." : "Updated " + ago(s.at, now) + "." }));
        if (percent !== null) {
          el.appendChild(NS.Meter({ label: "Context window used", value: percent, max: 100, threshold: WARN, thresholdLabel: WARN + "%", overWords: percent >= URGENT ? "nearly full" : "filling",
            format: pct, of: function () { return c.windowSize ? "of " + tokens(c.windowSize) + " tokens" : ""; } }).el);
        }
        if (c) {
          el.appendChild(pairs([
            c.inputTokens !== null && c.inputTokens !== undefined ? ["In the window", [tokens(c.inputTokens) + " tokens"]] : null,
            c.cacheReadTokens !== null && c.cacheReadTokens !== undefined ? ["Read from cache", [tokens(c.cacheReadTokens)]] : null,
            c.cacheWriteTokens !== null && c.cacheWriteTokens !== undefined ? ["Written to cache", [tokens(c.cacheWriteTokens)]] : null,
            c.uncachedTokens !== null && c.uncachedTokens !== undefined ? ["Not cached", [tokens(c.uncachedTokens)]] : null,
            c.outputTokens !== null && c.outputTokens !== undefined ? ["Last reply", [tokens(c.outputTokens)]] : null
          ]));
        }
        if (s.cost) {
          var added = s.cost.linesAdded, removed = s.cost.linesRemoved, hasLines = (added !== null && added !== undefined) || (removed !== null && removed !== undefined);
          el.appendChild(pairs([
            dollars(s.cost.usd) ? ["Cost so far", [dollars(s.cost.usd) + " ", h("span", { "class": "src", text: "Claude\u2019s estimate" })]] : null,
            hasLines ? ["Lines", ["+" + (added || 0) + " \u2212" + (removed || 0)]] : null
          ]));
        }
        [["Five-hour window", limits && limits.fiveHour], ["Seven-day window", limits && limits.sevenDay]].forEach(function (one) {
          var w = one[1];
          if (!w) { return; }
          el.appendChild(NS.Meter({ label: one[0], value: w.usedPercent, max: 100, threshold: WARN, thresholdLabel: WARN + "%", overWords: "getting close",
            format: pct, of: function () { return resetsIn(w.resetsAt, now) || ""; } }).el);
        });
        if (limits && limits.spend) {
          var sp = limits.spend, words = "";
          if (sp.usedUsd !== null && sp.usedUsd !== undefined && sp.limitUsd !== null && sp.limitUsd !== undefined) { words = dollars(sp.usedUsd) + " of " + dollars(sp.limitUsd); }
          else if (sp.usedPercent !== null && sp.usedPercent !== undefined) { words = Math.round(sp.usedPercent) + "%"; }
          if (sp.period) { words += " " + sp.period; }
          if (resetsIn(sp.resetsAt, now)) { words += " \u00b7 " + resetsIn(sp.resetsAt, now); }
          el.appendChild(pairs([["Spend limit", [words]]]));
        }
        if (!limits) {
          el.appendChild(h("p", { "class": "calm small", text: "No usage limits: Claude sends them for a subscription, or behind a gateway with a spend limit." }));
        }
        if (s.specialists && s.specialists.length) {
          el.appendChild(h("h3", { text: "Specialists" }));
          el.appendChild(h("ul", { "class": "specs" }, s.specialists.map(function (id) {
            return h("li", {}, [s.project ? h("a", { href: "#context/" + encodeURIComponent(s.project || "") + "/specialist/" + encodeURIComponent(id), text: id }) : id]);
          })));
        }
        var terminalWords = s.terminal === "full"
          ? (s.wrapping ? "your own status line, run for you" : "the launcher\u2019s full line")
          : s.terminal === "minimal" ? "a short line" : "no line: Studio draws it here";
        el.appendChild(h("p", { "class": "calm small", text: "The terminal shows " + terminalWords + "." }));
        el.appendChild(NS.Command({ why: "Change what the terminal shows:", command: "loadout config set statusline-in-studio " + (s.terminal === "off" ? "full" : "off") }));
      }
      paint();
      return {
        el: el,
        /** New props: { status?, now? }. */
        set: function (p) { p = p || {}; if (p.status) { status = p.status; } if (p.now !== undefined) { now = p.now; } paint(); }
      };
    }
    NS.StatusBlock = StatusBlock;
  })();

  /* --- StatusStrip --- */
  (function () {
    /* StatusStrip: a hosted session's status, one row on the foot of its terminal, from what its status line last
       handed over: the model, the context, what it has cost, the plan's five-hour window and how many specialists it
       was given. Every figure is Claude's; one it did not send is left out rather than drawn as nought. Old figures
       say so. A screen reader hears it only when the context crosses a threshold, and politely.
       Ported from main's StatusStrip.svelte. */
    var WARN = 70, URGENT = 90;
    function levelOf(p) { if (p === null || p === undefined || !isFinite(p)) { return "ok"; } return p >= URGENT ? "urgent" : p >= WARN ? "warn" : "ok"; }
    function levelWords(l) { return l === "urgent" ? "nearly full" : l === "warn" ? "filling" : ""; }
    function contextPercent(s) { var u = s && s.context ? s.context.usedPercent : null; return u === null || u === undefined || !isFinite(u) ? null : Math.round(u); }
    function isStale(s, now) { if (!s.refreshSeconds) { return false; } return now - Date.parse(s.at) > s.refreshSeconds * 2000; }
    function pad2(n) { return (n < 10 ? "0" : "") + n; }
    function ago(at, now) {
      var s = Math.max(0, Math.round((now - Date.parse(at)) / 1000));
      if (s < 10) { return "just now"; }
      if (s < 60) { return s + "s ago"; }
      var m = Math.floor(s / 60);
      return m < 60 ? m + "m ago" : Math.floor(m / 60) + "h" + pad2(m % 60) + "m ago";
    }
    function resetsIn(at, now) {
      if (!at) { return null; }
      var m = Math.max(0, Math.round((Date.parse(at) - now) / 60000));
      if (m < 60) { return "resets in " + m + "m"; }
      if (m < 48 * 60) { return "resets in " + Math.floor(m / 60) + "h" + pad2(m % 60) + "m"; }
      return "resets in " + Math.round(m / 1440) + "d";
    }
    function dollars(usd) { if (usd === null || usd === undefined || !isFinite(usd)) { return null; } return usd < 10 ? "$" + usd.toFixed(2) : "$" + Math.round(usd); }
    function beam(p) { var f = h("i"); f.style.width = Math.min(100, p) + "%"; return h("i", { "class": "beam", "aria-hidden": "true" }, [f]); }

    function StatusStrip(o) {
      o = o || {};
      var status = o.status, now = o.now === undefined ? Date.now() : o.now, last = null;
      var live = h("span", { "class": "ls-vh", role: "status", "aria-live": "polite" });
      var el = h("div", { "class": "ls-sstrip", role: "group", "aria-label": "Session status" });
      function paint() {
        var s = status || {}, percent = contextPercent(s), level = levelOf(percent), cost = dollars(s.cost ? s.cost.usd : null);
        var five = s.limits && s.limits.fiveHour ? s.limits.fiveHour : null, spend = s.limits && s.limits.spend ? s.limits.spend : null;
        var stale = isStale(s, now), count = s.specialists ? s.specialists.length : 0;
        el.textContent = "";
        el.classList.toggle("stale", stale);
        if (s.model) { el.appendChild(h("span", { "class": "model", text: s.model })); }
        if (percent !== null) {
          el.appendChild(h("span", { "class": "gauge " + level }, [h("span", { "class": "k", text: "ctx" }), beam(percent), h("b", { text: percent + "%" }), level !== "ok" ? h("em", { text: levelWords(level) }) : null]));
        }
        if (cost) { el.appendChild(h("span", { "class": "cost" }, [h("span", { "class": "k", text: "cost" }), " ", h("b", { text: cost })])); }
        if (five) {
          el.appendChild(h("span", { "class": "gauge " + levelOf(five.usedPercent), title: resetsIn(five.resetsAt, now) }, [h("span", { "class": "k", text: "5h" }), beam(five.usedPercent), h("b", { text: Math.round(five.usedPercent) + "%" })]));
        } else if (spend && spend.usedPercent !== null && spend.usedPercent !== undefined) {
          el.appendChild(h("span", { "class": "gauge " + levelOf(spend.usedPercent) }, [h("span", { "class": "k", text: "spend" }), beam(spend.usedPercent), h("b", { text: Math.round(spend.usedPercent) + "%" })]));
        }
        if (count) { el.appendChild(h("span", { "class": "spec" }, [h("b", { text: String(count) }), " ", h("span", { "class": "k", text: count === 1 ? "specialist" : "specialists" })])); }
        el.appendChild(h("span", { "class": "grow" }));
        el.appendChild(h("span", { "class": "when", text: stale ? "old: " + ago(s.at, now) : ago(s.at, now) }));
        el.appendChild(live);
        /* Said once each time the context moves from one level to another, never on the first reading. */
        if (last !== null && last !== level) {
          live.textContent = level === "ok" ? "Context back to " + percent + "%" : "Context " + percent + "% used, " + levelWords(level);
        }
        last = level;
      }
      paint();
      return {
        el: el,
        /** New props: { status?, now? }. */
        set: function (p) { p = p || {}; if (p.status) { status = p.status; } if (p.now !== undefined) { now = p.now; } paint(); }
      };
    }
    NS.StatusStrip = StatusStrip;
  })();

  /* --- TaskDetail --- */
  (function () {
    /* TaskDetail: one task in full, opened from its card: the detail a list leaves out, the
       note, and every change of state with who said so and when. Read with 'task show', so it
       says what a terminal would. The card already has the title and the moves; this is for
       reading. Ported from main's TaskDetail.svelte. */

    /* The words naming a task's list, and those for reading one task in full after 'task show'. */
    function showWords(id, project) { return [id].concat(project ? ["--project", project] : ["--global"]); }
    function whereSaid(project) { return project || "the workspace-wide list"; }
    function ideaHref(project, id) { return "#ideas/" + encodeURIComponent(project === null || project === undefined ? "~" : project) + "/" + encodeURIComponent(id); }
    /* A word as a shell would need it quoted, matching the daemon's own quoting. */
    function quoted(w) { return /^[A-Za-z0-9\-_./:=@,+%]+$/.test(w) ? w : "'" + w.replace(/'/g, "'\\''") + "'"; }
    function lineFor(path, words) { return "$ loadout " + [path].concat(words.map(quoted)).join(" "); }

    /* One task in full, from what 'task show --json' says. */
    function fromShow(read) {
      var t = read.task || {};
      return { id: t.id, title: t.title || "", state: t.state || "", kind: t.kind, declaredBy: t.by || "", declared: t.declared || "",
        note: t.note || null, project: read.project === undefined ? null : read.project, detail: t.detail || "", history: t.history || [], idea: read.idea || null };
    }

    function TaskDetail(o) {
      o = o || {};
      var task = o.task || { id: "", title: "", state: "" };
      var shown = o.shown || null, problem = o.problem || null, dialog = null, panel = null, body = null;
      var words = showWords(task.id, task.project);
      var when = function (at) { return new Date(at).toLocaleString([], { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" }); };

      function close() { if (dialog) { dialog.close(); } }
      function section(key, heading, kids) {
        return h("section", { "aria-labelledby": key }, [h("h3", { id: key, text: heading })].concat(kids));
      }

      function readPart() {
        if (problem) { return [h("p", { "class": "offline", role: "alert", text: problem })]; }
        if (!shown) { return [h("p", { "class": "calm", role: "status", text: "Reading…" })]; }
        var f = shown, out = [], k = uid("td-");
        if (f.idea) {
          out.push(h("p", { "class": "from" }, ["From an idea accepted " + when(f.idea.accepted) + ": the plan is at ", h("code", { text: f.idea.plan }), ". ",
            h("a", { href: ideaHref(task.project, task.id), onclick: function () { if (o.onidea) { o.onidea(ideaHref(task.project, task.id)); } close(); }, text: "Open the idea" })]));
        }
        if (f.note) { out.push(section(k + "-note", "Note", [h("p", { text: f.note })])); }
        if (f.detail) { out.push(section(k + "-detail", "Detail", [NS.Markdown(f.detail)])); }
        var hist;
        if (f.history.length) {
          hist = h("ol", { "class": "history" }, f.history.map(function (one) {
            return h("li", {}, [h("span", { "class": "st" }, [NS.Signal(one.state)]), " ", h("span", { "class": "by", text: one.by || "nobody named" }), " ",
              h("time", { datetime: one.at, text: when(one.at) }), one.reason ? h("span", { "class": "why", text: one.reason }) : null]);
          }));
        } else {
          /* Recorded before there was a history: the latest claim is all that was kept. */
          hist = h("p", { "class": "calm", text: f.state + ", said by " + (f.declaredBy || "nobody named") + (f.declared ? " on " + when(f.declared) : "") + ". Nothing earlier was kept." });
        }
        out.push(section(k + "-history", "History", [hist]));
        return out;
      }

      function chipKids() {
        var state = (shown && shown.state) || task.state;
        return [state ? h("span", { "class": "ls-tag" }, [NS.Signal(state)]) : null,
          h("span", { "class": "ls-tag", text: task.id }),
          task.project !== undefined ? h("span", { "class": "ls-tag", text: whereSaid(task.project) }) : null];
      }

      /* The panel is the Dialog's. The chips, the line and Close stay put; only what was read is
         redrawn, between the chips and the line, as main's {#if} blocks do. */
      var chips = null, from = null, reads = [];
      function paint() {
        if (!panel) { return; }
        var title = panel.querySelector("h2");
        if (title) { title.textContent = (shown && shown.title) || task.title || task.id; }
        if (!chips || !panel.contains(chips)) {
          chips = h("p", { "class": "chips" }, chipKids());
          from = h("p", { "class": "ls-from" }, ["The same as ", h("code", { text: lineFor("task show", words) }), "."]);
          panel.appendChild(chips); panel.appendChild(from);
          panel.appendChild(h("div", { "class": "close" }, [NS.HaloButton({ label: "Close", size: "small", onPress: close })]));
          reads = [];
        } else {
          chips.textContent = ""; chipKids().forEach(function (n) { if (n) { chips.appendChild(n); } });
        }
        reads.forEach(function (n) { n.remove(); });
        reads = readPart();
        reads.forEach(function (n) { panel.insertBefore(n, from); });
      }

      function set(next) {
        next = next || {};
        if ("shown" in next) { shown = next.shown; }
        if ("read" in next && next.read) { shown = fromShow(next.read); }
        if ("problem" in next) { problem = next.problem; }
        paint();
      }

      function open() {
        body = h("div", {});
        dialog = NS.Dialog({ title: (shown && shown.title) || task.title || task.id, extraClass: "ls-tdetail", initialFocus: ".close button",
          host: o.host, contained: o.contained, behind: o.behind, onClose: function () { panel = null; if (o.onclose) { o.onclose(); } } });
        panel = dialog.open();
        paint();
        if (!shown && !problem && o.read) {
          var got;
          try { got = Promise.resolve(o.read("task show", words)); } catch (e) { got = Promise.reject(e); }
          got.then(function (read) { set({ read: read }); }, function (error) {
            /* Signed out is the page's to handle, as main sets studio.signedOut. */
            if (error && error.name === "SignedOut" && o.onsignedout) { o.onsignedout(); } else { set({ problem: (error && error.message) || String(error) }); }
          });
        }
        return panel;
      }

      open();
      return { get el() { return panel; }, open: open, close: close, set: set };
    }
    TaskDetail.fromShow = fromShow;
    TaskDetail.showWords = showWords;
    NS.TaskDetail = TaskDetail;
  })();

  /* --- TriggerRules --- */
  (function () {
    /* TriggerRules: each rule on this machine with where it stands against its caps, a form that
       writes and changes them, the rules teams suggest, who is trusted beyond the collaborators,
       and what the rules decided lately. Everything is read with a 'trigger ...' command and done
       with one; the form shows the line it will run, and Preview runs it with --dry-run first. */

    // main's quoted() from lib/commands.ts, kept apart from q(): it quotes only what a shell reads.
    function quoted(value) {
      value = String(value);
      return value === "" || /[\s"'$`\\]/.test(value) ? "\"" + value.replace(/(["\\$`])/g, "\\$1") + "\"" : value;
    }
    function money(amount) { return "$" + Number(amount == null ? 0 : amount).toFixed(2); }

    /* ---------- lib/triggerRule.ts, as built ---------- */

    var RULE_DEFAULTS = { capRuns: "10", window: "24h" };

    function ruleValues(preset) {
      var v = { name: "", on: "pr-opened", starts: "team", team: "", builtin: "docs audit", goal: "", autonomy: "autonomous", projects: [], where: [],
        capUsd: "proposed", budgetUsd: "", capRuns: RULE_DEFAULTS.capRuns, window: RULE_DEFAULTS.window, debounce: "", clock: "every", every: "1h", at: "09:00", disabled: false };
      Object.keys(preset || {}).forEach(function (k) { v[k] = preset[k]; });
      return v;
    }

    function spanWords(span) {
      var found = /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})/.exec(span || "");
      if (!found) { return ""; }
      var minutes = Number(found[1] || 0) * 1440 + Number(found[2]) * 60 + Number(found[3]);
      if (minutes === 0) { return ""; }
      if (minutes % 1440 === 0) { return minutes / 1440 + "d"; }
      return minutes % 60 === 0 ? minutes / 60 + "h" : minutes + "m";
    }

    function valuesFromRule(rule) {
      var team = rule.action.kind === "team";
      var w = spanWords(rule.window);
      return ruleValues({
        name: rule.id, on: rule.on, starts: team ? "team" : "builtin", team: team ? rule.action.name : "", builtin: team ? "docs audit" : rule.action.name,
        goal: rule.action.goal || "", autonomy: rule.action.autonomy === "supervised" ? "supervised" : "autonomous", projects: (rule.projects || []).slice(),
        where: Object.keys(rule.where || {}).map(function (f) { return { field: f, value: rule.where[f] }; }),
        capUsd: rule.capUsd == null ? "" : String(rule.capUsd), budgetUsd: rule.budgetUsd == null ? "" : String(rule.budgetUsd), capRuns: String(rule.capRuns),
        window: w === "" || w === "1d" ? RULE_DEFAULTS.window : w, debounce: spanWords(rule.debounce), clock: rule.at ? "at" : "every",
        every: spanWords(rule.every) || "1h", at: (rule.at || "09:00").slice(0, 5), disabled: !rule.enabled
      });
    }

    function ruleWords(v) {
      var words = [v.name || "<name>", v.on];
      if (v.starts === "team") { words.push("--team", v.team || "<team>", "--goal", v.goal.trim() || "…", "--autonomy", v.autonomy); } else { words.push("--builtin", v.builtin); }
      v.projects.forEach(function (p) { words.push("--project", p); });
      v.where.filter(function (e) { return e.field && e.value.trim(); }).forEach(function (one) { words.push("--where", one.field + "=" + one.value.trim()); });
      if (v.on === "clock") { if (v.clock === "at") { words.push("--at", v.at); } else { words.push("--every", v.every.trim()); } }
      if (v.starts === "team" && v.capUsd.trim()) { words.push("--cap-usd", v.capUsd.trim()); }
      if (v.starts === "team" && v.budgetUsd.trim()) { words.push("--budget-usd", v.budgetUsd.trim()); }
      if (v.capRuns.trim() && v.capRuns.trim() !== RULE_DEFAULTS.capRuns) { words.push("--cap-runs", v.capRuns.trim()); }
      if (v.window.trim() && v.window.trim() !== RULE_DEFAULTS.window) { words.push("--window", v.window.trim()); }
      if (v.debounce.trim()) { words.push("--debounce", v.debounce.trim()); }
      if (v.disabled) { words.push("--disabled"); }
      return words;
    }

    function ruleCommand(v) { return "$ loadout trigger add " + ruleWords(v).map(quoted).join(" "); }

    var DURATION = /^\d+\s*(m|h|d)$/, MONEY = /^\d+(\.\d+)?$/;

    function ruleProblem(v, kinds, teams) {
      if (!v.name) { return "give the rule a name first"; }
      if (!/^[a-z0-9]+(-[a-z0-9]+)*$/.test(v.name)) { return "a rule’s name is lowercase words joined by hyphens"; }
      var kind = kinds.filter(function (one) { return one.kind === v.on; })[0];
      if (!kind) { return "pick what it waits for"; }
      if (v.starts === "team") {
        if (!v.team) { return "pick the team it starts"; }
        if (teams.indexOf(v.team) < 0) { return "there is no team called " + v.team; }
        if (!v.goal.trim()) { return "say what to ask the team for"; }
      } else if (!v.builtin) { return "pick the built-in it starts"; }
      for (var i = 0; i < v.where.length; i++) {
        var one = v.where[i];
        if (!one.field) { return "pick the field each condition is about"; }
        if (kind.fields.indexOf(one.field) < 0) { return "a " + v.on + " event carries no " + one.field; }
        if (!one.value.trim()) { return "say what " + one.field + " must be"; }
      }
      if (v.on === "clock") {
        if (v.clock === "every") {
          if (!DURATION.test(v.every.trim())) { return "say how often as 30m, 2h or 1d"; }
          if (/^\d+\s*m$/.test(v.every.trim()) && parseInt(v.every, 10) < 5) { return "a clock rule comes round every five minutes at the most"; }
        } else if (!/^\d{2}:\d{2}$/.test(v.at)) { return "say the time of day as 09:00"; }
        if (v.starts === "team" && v.projects.length !== 1) { return "a clock rule that starts a team names the one project it runs on"; }
      }
      if (v.starts === "team") {
        var cap = v.capUsd.trim();
        if (!cap) { return "a rule that starts a team needs a spend cap, or proposed for one worked out from its runs"; }
        if (cap !== "proposed" && (!MONEY.test(cap) || Number(cap) <= 0)) { return "the spend cap is a figure in US dollars above nothing, or proposed"; }
        var budget = v.budgetUsd.trim();
        if (budget && budget !== "proposed" && (!MONEY.test(budget) || Number(budget) <= 0)) { return "the budget for each run is a figure in US dollars above nothing, or proposed"; }
        if (budget && MONEY.test(budget) && MONEY.test(cap) && Number(budget) > Number(cap)) { return "a run’s budget cannot be more than the spend cap, or not even one run could start"; }
      }
      if (!/^\d+$/.test(v.capRuns.trim()) || Number(v.capRuns) < 1) { return "the most runs in its window is a whole number, at least one"; }
      if (!DURATION.test(v.window.trim())) { return "write the window as 24h or 7d"; }
      if (v.debounce.trim() && !DURATION.test(v.debounce.trim())) { return "write how long to wait as 10m or 1h"; }
      return null;
    }

    /* ---------- small helpers ---------- */

    function halo(o) {
      var x = {}; Object.keys(o).forEach(function (k) { if (k !== "onpress") { x[k] = o[k]; } });
      // main shows the reason in place of the command (disabledReason ?? cmd).
      if (o.disabledReason) { delete x.cmd; }
      if (o.onpress) { x.onPress = o.onpress; }
      return NS.HaloButton(x);
    }
    var FOCUSABLE = "button, input, select, textarea, a[href]";
    function focusables(root) { return Array.prototype.slice.call(root.querySelectorAll(FOCUSABLE)); }
    // Redraws without losing the place: focus goes back to the same control inside the same [data-k].
    function keep(root, fn) {
      var a = document.activeElement, key = null, idx = -1;
      if (a && root.contains(a) && a.closest) { var w = a.closest("[data-k]"); if (w && root.contains(w)) { key = w.getAttribute("data-k"); idx = focusables(w).indexOf(a); } }
      fn();
      if (key === null) { return; }
      var nw = root.querySelector("[data-k=\"" + key + "\"]");
      if (!nw) { return; }
      var list = focusables(nw), t = list[idx] || list[0];
      if (t) { t.focus(); }
    }
    function field(id, label, input, hint, wide) {
      return h("div", { "class": "ls-field" + (wide ? " wide" : ""), "data-k": id }, [h("label", { "class": "lab", "for": id, text: label }), input, hint == null ? null : h("span", { "class": "hint", text: hint })]);
    }
    function text(id, value, attrs, oninput) {
      var a = { id: id, name: id, "class": "ls-text", type: "text", autocomplete: "off" };
      Object.keys(attrs || {}).forEach(function (k) { a[k] = attrs[k]; });
      var el = h(a.rows ? "textarea" : "input", a);
      if (a.rows) { el.removeAttribute("type"); }
      el.value = value == null ? "" : value;
      el.addEventListener("input", function () { oninput(el.value); });
      return el;
    }
    // ModePicker calls onChange while it is built; only a person's pick counts.
    function modes(key, o, onchange) {
      var ready = false;
      var el = NS.ModePicker({ label: o.label, selected: o.selected, options: o.options, onChange: function (name, i) { if (ready) { onchange(name, i); } } });
      ready = true;
      el.setAttribute("data-k", key);
      return el;
    }
    function picker(key, o) { var el = NS.Picker(o); el.setAttribute("data-k", key); return el; }

    function TriggerRules(o) {
      o = o || {};
      var teams = o.teams || [], projects = o.projects || [], disabledReason = o.disabledReason, onfail = o.onfail;
      var base = uid("tr-");
      var el = h("div", { "class": "ls-trules" });

      var rules = null, kinds = [], builtins = [], suggested = [], allowed = [], journal = [];
      var read = o.read || function () { return Promise.resolve([]); };
      var run = o.run || function () { return Promise.resolve({ output: "" }); };
      function soft(p) { return p.catch(function () { return []; }); }

      function load() {
        return Promise.all([
          read("trigger list"),
          read("trigger events"),
          soft(read("trigger suggested")),
          soft(read("trigger allowed")),
          soft(read("trigger journal", ["--limit", "25"]))
        ]).then(function (r) {
          rules = r[0] || []; kinds = (r[1] && r[1].kinds) || []; builtins = (r[1] && r[1].builtins) || [];
          suggested = r[2] || []; allowed = r[3] || []; journal = (r[4] || []).slice().reverse();
          paint();
        }, function (error) { if (onfail) { onfail(error); } });
      }

      /* ---------- the rules' own buttons ---------- */

      function line(path, words) { return "loadout " + path + " " + words.map(quoted).join(" "); }
      function act(path, words, after, rule) {
        return Promise.resolve().then(function () { return run(path, words); }).then(function (job) {
          if (after === "show") { tried(rule, ((job && job.output) || "").trim() || "It said nothing."); }
        }, function (error) { if (onfail) { onfail(error); } }).then(function () { return load(); });
      }
      function starts(rule) { return rule.action.kind === "team" ? "starts " + rule.action.name + " (" + rule.action.autonomy + ")" : "runs " + rule.action.name; }
      function when(rule) {
        if (rule.on !== "clock") { return "on " + rule.on; }
        return rule.at ? "daily at " + rule.at.slice(0, 5) : "every " + (rule.every || "");
      }
      function standing(one) {
        var cap = one.rule.capUsd == null ? "" : " of " + money(one.rule.capUsd);
        return one.runs + " of " + one.rule.capRuns + " runs · " + money(one.landedUsd) + " spent" + cap + " · " + money(one.reservedUsd) + " set aside";
      }

      /* ---------- the allow-list ---------- */

      var login = "", scope = "everywhere", where = "";
      var SCOPES = ["everywhere", "repository", "owner"];
      function allowWords() { return [login.trim() || "<login>"].concat(scope === "repository" ? ["--repository", where.trim()] : scope === "owner" ? ["--owner", where.trim()] : []); }
      function allowProblem() { return !login.trim() ? "name the GitHub account first" : scope !== "everywhere" && !where.trim() ? "say which " + scope : null; }
      var trustSlot = h("div", { "data-k": base + "-trust" });
      function paintTrust() {
        keep(trustSlot, function () {
          trustSlot.textContent = "";
          var words = allowWords();
          trustSlot.appendChild(halo({ label: "Trust them", disabledReason: allowProblem() || disabledReason, cmd: line("trigger allow", words),
            onpress: function () { act("trigger allow", words); login = ""; where = ""; paint(); } }));
        });
      }

      /* ---------- the page ---------- */

      function rule(one) {
        var r = one.rule;
        var wh = Object.keys(r.where || {}).map(function (f) { return " · " + f + " = " + r.where[f]; }).join("");
        return h("li", { "class": "rule" + (!r.enabled || one.paused ? " off" : "") }, [
          h("div", { "class": "top" }, [
            h("strong", { text: r.id }),
            h("span", { "class": "calm small", text: "v" + r.version + " · " + when(r) + " · " + starts(r) }),
            one.paused ? h("span", { "class": "tag needs", text: "paused at a cap" }) : null,
            !r.enabled ? h("span", { "class": "tag", text: "disabled" }) : null,
            one.suggestionChanged ? h("span", { "class": "tag", text: "the team changed its suggestion" }) : null]),
          r.action.goal ? h("p", { "class": "goal", text: r.action.goal }) : null,
          h("p", { "class": "calm small", text: (r.projects.length ? "Watches " + r.projects.join(", ") : "Watches every project") + wh }),
          h("p", { "class": "calm small", text: standing(one) }),
          one.problem ? h("p", { "class": "prob", role: "alert", text: "✕ Not acted on: " + one.problem }) : null,
          h("div", { "class": "acts", "data-k": base + "-r-" + r.id }, [
            halo({ size: "small", label: "Change", disabledReason: disabledReason, cmd: "loadout trigger add " + r.id + " …", onpress: function () { change(r); } }),
            halo({ size: "small", label: "Try it", cmd: line("trigger test", [r.id]), onpress: function () { act("trigger test", [r.id], "show", r.id); } }),
            one.paused ? halo({ size: "small", label: "Resume", kind: "needs", disabledReason: disabledReason, cmd: line("trigger resume", [r.id]), onpress: function () { act("trigger resume", [r.id]); } }) : null,
            halo({ size: "small", label: "Remove", kind: "danger", disabledReason: disabledReason, cmd: line("trigger remove", [r.id]), onpress: function () { removing(r); } })])]);
      }

      function paint() {
        keep(el, function () {
          el.textContent = "";
          var rulesId = base + "-rules";
          el.appendChild(h("section", { "class": "tblock", "aria-labelledby": rulesId }, [
            h("div", { "class": "headrow", "data-k": base + "-head" }, [h("h2", { "class": "topicname", id: rulesId, text: "Trigger rules" }),
              halo({ label: "Add a rule", solid: true, glyph: "+", disabledReason: disabledReason, cmd: "loadout trigger add …", onpress: add })]),
            h("p", { "class": "calm small", text: "A rule starts a team or a built-in when something happens. Each that can spend has a cap, and a run that would write waits for your approval in Needs you." }),
            rules === null ? h("p", { "class": "calm", text: "Reading the rules…" })
              : !rules.length ? h("p", { "class": "calm", text: "There are no rules on this machine yet." })
              : h("ul", { "class": "rules" }, rules.map(rule))]));

          if (suggested.length) {
            var sId = base + "-suggested";
            el.appendChild(h("section", { "class": "tblock", "aria-labelledby": sId }, [
              h("h2", { "class": "topicname", id: sId, text: "Suggested by teams" }),
              h("ul", { "class": "rules" }, suggested.map(function (one) {
                return h("li", { "class": "rule" }, [
                  h("div", { "class": "top" }, [h("strong", { text: one.team + " · " + one.name }), h("span", { "class": "calm small", text: "on " + one.suggested.on }),
                    one.accepted && one.changed ? h("span", { "class": "tag needs", text: "changed since you accepted it" }) : one.accepted ? h("span", { "class": "tag", text: "accepted as " + one.rule }) : null]),
                  one.suggested.goal ? h("p", { "class": "goal", text: one.suggested.goal }) : null,
                  !one.accepted || one.changed ? h("div", { "class": "acts", "data-k": base + "-s-" + one.team + "-" + one.name }, [
                    halo({ size: "small", label: one.accepted ? "Take the change" : "Accept it", disabledReason: disabledReason, cmd: line("trigger accept", [one.team, one.name]),
                      onpress: function () { act("trigger accept", [one.team, one.name]); } })]) : null]);
              }))]));
          }

          var tId = base + "-trusted", loginId = base + "-login", whereId = base + "-where";
          el.appendChild(h("section", { "class": "tblock", "aria-labelledby": tId }, [
            h("h2", { "class": "topicname", id: tId, text: "Trusted beyond the collaborators" }),
            h("p", { "class": "calm small", text: "Owners, members and collaborators are trusted as GitHub says. Anybody else's words reach a run quoted as theirs, unless they are here." }),
            allowed.length ? h("ul", { "class": "rules" }, allowed.map(function (one) {
              return h("li", { "class": "rule" }, [
                h("div", { "class": "top" }, [h("strong", { text: one.login }), h("span", { "class": "calm small", text: one.scope }),
                  one.nowCalled ? h("span", { "class": "tag needs", text: "now called " + one.nowCalled }) : null]),
                h("div", { "class": "acts", "data-k": base + "-a-" + one.id + "-" + one.scope }, [
                  halo({ size: "small", label: "Stop trusting", kind: "danger", disabledReason: disabledReason, cmd: line("trigger disallow", [one.login]), onpress: function () { act("trigger disallow", [one.login]); } })])]);
            })) : null,
            h("div", { "class": "allow" }, [
              field(loginId, "GitHub login", text(loginId, login, { placeholder: "for example: dependabot[bot]" }, function (v) { login = v; paintTrust(); })),
              modes(base + "-scope", { label: "Where", selected: SCOPES.indexOf(scope), options: [{ name: "Everywhere", desc: "every repository" }, { name: "One repository", desc: "--repository owner/name" }, { name: "One owner", desc: "--owner name" }] },
                function (_, i) { scope = SCOPES[i]; paint(); }),
              scope !== "everywhere" ? field(whereId, scope === "repository" ? "The repository, as owner/name" : "The owner", text(whereId, where, {}, function (v) { where = v; paintTrust(); })) : null,
              trustSlot])]));
          paintTrust();

          var jId = base + "-journal";
          el.appendChild(h("section", { "class": "tblock", "aria-labelledby": jId }, [
            h("h2", { "class": "topicname", id: jId, text: "What the rules decided lately" }),
            journal.length ? h("ul", { "class": "journal" }, journal.map(function (one) {
              return h("li", {}, [h("span", { "class": "calm small", text: new Date(one.at).toLocaleString() }), " ", h("strong", { text: one.kind }),
                (one.rule ? " · " + one.rule : "") + (one.subject ? " · " + one.subject : "") + (one.reason ? " · " + one.reason : "")]);
            })) : h("p", { "class": "calm", text: "Nothing in the last day." }),
            h("p", { "class": "calm small" }, ["From ", h("code", { "class": "ls-cmd", text: "loadout trigger journal --limit 25" }), "; the terminal reads further back with --since."])]));
        });
      }

      /* ---------- the form ---------- */

      var editing = null, said = "", refused = false, sending = false, sheetDlg = null, sheet = null, live = null;

      function add() { said = ""; editing = { values: ruleValues({ team: teams[0] || "", builtin: builtins[0] || "docs audit" }), changing: false }; openSheet(); }
      function change(r) { said = ""; editing = { values: valuesFromRule(r), changing: true }; openSheet(); }
      function closeSheet() { if (sheetDlg) { var d = sheetDlg; sheetDlg = null; d.close(); } }

      function openSheet() {
        /* Closing a sheet that's already open clears editing (its onClose); keep the rule just picked. */
        var next = editing; closeSheet(); editing = next;
        sheet = h("div", { "class": "ls-sheet" });
        paintSheet();
        var v = editing.values;
        sheetDlg = NS.Dialog({ title: editing.changing ? "Change " + v.name : "Add a trigger rule", label: "Trigger rule", extraClass: "sheet ls-trules-dlg", cancelLabel: "Close", content: sheet, host: o.host, contained: o.contained,
          onClose: function () { sheetDlg = null; editing = null; } });
        sheetDlg.open();
      }

      function send(dry) {
        if (!editing || sending) { return; }
        sending = true; live();
        var words = ruleWords(editing.values).concat(dry ? ["--dry-run"] : []);
        Promise.resolve().then(function () { return run("trigger add", words); }).then(function (job) {
          said = ((job && job.output) || "").trim() || (dry ? "Nothing would change." : "Saved.");
          refused = false;
          if (!dry) { sending = false; closeSheet(); load(); return; }
        }, function (error) { said = (error && error.message) || String(error); refused = true; }).then(function () { sending = false; if (editing) { live(); } });
      }

      function paintSheet() {
        if (!editing) { return; }
        keep(sheet, function () {
          sheet.textContent = "";
          var v = editing.values, changing = editing.changing;
          var kind = kinds.filter(function (one) { return one.kind === v.on; })[0] || null;
          var k = base + "-f";
          var cmdline = h("code", { "class": "ls-cmdline" });
          var foot = h("div", { "class": "foot", "data-k": k + "-foot" });
          var saidEl = null;
          var projHint = h("p", { "class": "hint" });

          live = function () {
            if (!editing) { return; }
            var problem = ruleProblem(v, kinds, teams);
            cmdline.textContent = ruleCommand(v);
            projHint.textContent = v.projects.length ? "" : "No project ticked: events from every project count.";
            keep(foot, function () {
              foot.textContent = "";
              foot.appendChild(halo({ label: "Preview", disabledReason: problem || (sending ? "sending…" : undefined), cmd: problem ? undefined : "runs it with --dry-run", onpress: function () { send(true); } }));
              foot.appendChild(halo({ label: changing ? "Save the change" : "Save the rule", solid: true, glyph: "▶", disabledReason: problem || disabledReason || (sending ? "sending…" : undefined), cmd: problem ? undefined : "runs the command above", onpress: function () { send(false); } }));
              foot.appendChild(halo({ label: "Close", size: "small", onpress: closeSheet }));
            });
            if (!said) { if (saidEl) { saidEl.remove(); saidEl = null; } return; }
            if (!saidEl) { saidEl = h("pre", { role: "status" }); sheet.appendChild(saidEl); }
            saidEl.className = refused ? "ls-said no output" : "ls-said output";
            saidEl.textContent = said;
          };
          function set(key) { return function (val) { v[key] = val; live(); }; }
          function again() { paintSheet(); }

          sheet.appendChild(h("div", { "class": "two" }, [
            field(k + "-name", "Its name", text(k + "-name", v.name, { placeholder: "for example: review-pulls", readonly: changing ? true : null }, set("name")),
              changing ? "Saving keeps the name and counts its version on." : "Lowercase words joined by hyphens."),
            picker(k + "-on", { label: "When this happens", options: kinds.map(function (one) { return { name: one.kind, sub: (one.github ? "GitHub · " : "") + "carries " + one.fields.join(", ") }; }),
              selected: Math.max(0, kinds.map(function (one) { return one.kind; }).indexOf(v.on)),
              onPick: function (option) {
                v.on = option.name;
                var nk = kinds.filter(function (x) { return x.kind === option.name; })[0];
                v.where = v.where.filter(function (one) { return nk && nk.fields.indexOf(one.field) >= 0; });
                again();
              } })]));

          if (v.on === "clock") {
            sheet.appendChild(h("fieldset", {}, [h("legend", { text: "When it comes round" }),
              modes(k + "-clock", { label: "When", selected: v.clock === "at" ? 1 : 0, options: [{ name: "An interval", desc: "--every 1h" }, { name: "A time of day", desc: "--at 09:00" }] }, function (_, i) { v.clock = i ? "at" : "every"; again(); }),
              v.clock === "at" ? field(k + "-at", "The time of day, this machine’s own", text(k + "-at", v.at, { type: "time" }, set("at")))
                : field(k + "-every", "How often", text(k + "-every", v.every, { placeholder: "1h" }, set("every")), "30m, 2h or 1d. At least five minutes.")]));
          }

          var startsKids = [h("legend", { text: "What it starts" }),
            modes(k + "-starts", { label: "Starts", selected: v.starts === "builtin" ? 1 : 0, options: [{ name: "A team", desc: "a run, with a cap" }, { name: "A built-in", desc: "costs nothing" }] }, function (_, i) { v.starts = i ? "builtin" : "team"; again(); })];
          if (v.starts === "team") {
            startsKids.push(picker(k + "-team", { label: "Team", options: teams.map(function (n) { return { name: n, sub: "" }; }), selected: Math.max(0, teams.indexOf(v.team)), onPick: function (op) { v.team = op.name; live(); } }));
            startsKids.push(field(k + "-goal", "What to ask it for", text(k + "-goal", v.goal, { rows: "2", placeholder: "for example: Review {title} and say what would break" }, set("goal")),
              (kind ? "{" + kind.fields.join("}, {") + "} are filled in from the event." : "") + " An outside author’s words are quoted as theirs.", true));
            startsKids.push(modes(k + "-posture", { label: "Posture", selected: v.autonomy === "supervised" ? 0 : 1, options: [{ name: "supervised", desc: "holds at the gates the team names" }, { name: "autonomous", desc: "holds for nothing, within its caps" }] }, function (name) { v.autonomy = name; live(); }));
          } else {
            startsKids.push(picker(k + "-builtin", { label: "Built-in", options: builtins.map(function (n) { return { name: n, sub: "reads and reports" }; }), selected: Math.max(0, builtins.indexOf(v.builtin)), onPick: function (op) { v.builtin = op.name; live(); } }));
          }
          sheet.appendChild(h("fieldset", {}, startsKids));

          var countKids = [h("legend", { text: "Which events count" }), projHint,
            h("div", { "class": "projects", "data-k": k + "-projects" }, projects.map(function (name) {
              return NS.Switch({ label: name, checked: v.projects.indexOf(name) >= 0, onChange: function (on) { v.projects = on ? v.projects.concat([name]) : v.projects.filter(function (x) { return x !== name; }); live(); } });
            }))];
          var fields = (kind && kind.fields) || [];
          v.where.forEach(function (condition, index) {
            var vid = base + "-value-" + index;
            countKids.push(h("div", { "class": "two" }, [
              picker(k + "-wf-" + index, { label: "Field", options: fields.map(function (n) { return { name: n, sub: "" }; }), selected: Math.max(0, fields.indexOf(condition.field)), onPick: function (op) { v.where[index].field = op.name; live(); } }),
              h("div", { "class": "ls-field", "data-k": k + "-wv-" + index }, [h("label", { "class": "lab", "for": vid, text: "Must be" }),
                text(vid, condition.value, {}, function (val) { v.where[index].value = val; live(); }),
                halo({ size: "small", label: "Remove this condition", onpress: function () { v.where = v.where.filter(function (_, at) { return at !== index; }); again(); } })])]));
          });
          if (fields.length) {
            countKids.push(h("div", { "data-k": k + "-addcond" }, [halo({ size: "small", label: "Add a condition", glyph: "+", onpress: function () { v.where = v.where.concat([{ field: fields[0], value: "" }]); again(); } })]));
          }
          if (kind && kind.github) { countKids.push(h("p", { "class": "hint", text: "Drafts are passed by unless a condition asks for draft = true, and closed pull requests unless the rule waits for the closing." })); }
          sheet.appendChild(h("fieldset", {}, countKids));

          var spend = [];
          if (v.starts === "team") {
            spend.push(field(k + "-cap", "Spend cap, in US dollars", text(k + "-cap", v.capUsd, { placeholder: "proposed" }, set("capUsd")), "Or proposed, worked out from the team’s last runs. Preview shows the figure."));
            spend.push(field(k + "-budget", "Each run may spend", text(k + "-budget", v.budgetUsd, { placeholder: "the team’s own" }, set("budgetUsd")), "A figure, or proposed. Needed when the team has no budget of its own."));
          }
          spend.push(field(k + "-runs", "Runs at most in the window", text(k + "-runs", v.capRuns, { inputmode: "numeric" }, set("capRuns"))));
          spend.push(field(k + "-window", "The window", text(k + "-window", v.window, {}, set("window")), "24h or 7d; both caps count over it."));
          spend.push(field(k + "-debounce", "Wait for the subject to go quiet", text(k + "-debounce", v.debounce, { placeholder: "no wait" }, set("debounce")), "10m turns a burst of pushes into one run."));
          var off = NS.Switch({ label: "Record it without letting it act yet", sub: "--disabled", checked: v.disabled, onChange: function (on) { v.disabled = on; live(); } });
          off.setAttribute("data-k", k + "-disabled");
          sheet.appendChild(h("fieldset", {}, [h("legend", { text: "What it may spend and how often" }), h("div", { "class": "two" }, spend), off]));

          sheet.appendChild(cmdline);
          sheet.appendChild(foot);
          live();
        });
      }

      /* ---------- the two small dialogs ---------- */

      function removing(r) {
        NS.Dialog({ danger: true, role: "alertdialog", title: "Remove " + r.id + "?", host: o.host, contained: o.contained,
          why: "It stops acting at once, and anything waiting to start under it, or waiting for your approval, is withdrawn. Its history stays in the journal.",
          actions: [{ label: "Remove it", kind: "danger", disabledReason: disabledReason, cmd: line("trigger remove", [r.id]), busy: "removing\u2026", onPress: function () { return Promise.resolve().then(function () { return run("trigger remove", [r.id]); }).then(function () { return load(); }); } }] }).open();
      }

      function tried(ruleId, said) {
        var d = null;
        var body = h("div", {}, [h("pre", { "class": "output", text: said }), halo({ label: "Close", size: "small", onpress: function () { if (d) { d.close(); } } })]);
        d = NS.Dialog({ title: "What " + ruleId + " would have done", label: "Try " + ruleId, cancelLabel: "Close", content: body, extraClass: "ls-trules-dlg", host: o.host, contained: o.contained });
        d.open();
      }

      paint();
      load();

      return {
        el: el,
        load: load,
        add: add,
        change: function (id) { var one = (rules || []).filter(function (x) { return x.rule.id === id; })[0]; if (one) { change(one.rule); } },
        set: function (n) {
          n = n || {};
          if ("teams" in n) { teams = n.teams || []; }
          if ("projects" in n) { projects = n.projects || []; }
          if ("disabledReason" in n) { disabledReason = n.disabledReason; }
          if ("onfail" in n) { onfail = n.onfail; }
          paint();
          if (editing && sheet) { paintSheet(); }
        }
      };
    }

    NS.TriggerRules = TriggerRules;
  })();
})();
/* end stage 14 */
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

  /* Confirmations wait, then close. An act may return a promise: the button (or the dialog's) is busy until it settles. It goes through
     on anything but false, a throw or a rejection; then the line under it says "✕ reason" and an asking dialog stays open with the same. */
  function failWord(a, e) { return (e && e.message) || (typeof e === "string" && e) || a.failed || "It did not go through. Nothing changed."; }
  function settle(a, r, ok, out) {
    function bad(e) { said(out, "\u2715 " + failWord(a, e), true); }
    if (r && typeof r.then === "function") {
      return r.then(function (v) { if (v === false) { throw new Error(failWord(a)); } ok(v); return v; }).catch(function (e) { bad(e); throw e; });
    }
    if (r === false) { bad(); return false; }
    ok(r); return r;
  }
  function confirmThen(a, go, btn) {
    if (!a.ask) {
      var r = go();
      if (r && typeof r.then === "function") {
        var done = function () { if (btn) { NS.HaloButton.setBusy(btn, false); } };
        if (btn) { NS.HaloButton.setBusy(btn, a.busy || true); }
        r.then(done, done); /* its line already says why it failed */
      }
      return r;
    }
    NS.Dialog({ title: a.askTitle || a.label, why: a.ask, danger: !!a.danger, role: "alertdialog", failed: a.failed, actions: [{ label: a.yes || a.label, kind: a.danger ? "danger" : "", solid: true, cmd: a.cmd, busy: a.busy || true, onPress: function () { return go(); } }] }).open();
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
      return settle(o, r, function (v) {
        result.textContent = "";
        if (v && v.nodeType) { result.appendChild(v); said(out, preview ? "Previewed: nothing changed" : (o.said || "Done")); return; }
        said(out, typeof v === "string" ? v : (preview ? "Previewed: nothing changed" : (o.said || "Done")));
      }, out);
    }
    function paint() {
      line.textContent = lineFor(o.path, fields, values, acting(false));
      var set = more.filter(function (f) { var v = values[f.name]; return f.kind === "flag" ? !!v : v !== "" && v !== undefined && v !== null; }).length;
      moreCount.textContent = more.length ? "\u00b7 " + more.length + (set ? ", " + set + " set" : "") : "";
      acts.textContent = "";
      var dis = o.disabledReason || missing();
      if (effect === "session") { acts.appendChild(NS.HaloButton({ label: o.label || "Open it in a terminal", size: "small", glyph: "\u25b8", solid: true, cmd: o.disabledReason || missing() ? null : EFFECT.session, disabledReason: dis, onPress: function (e) { confirmThen({ busy: o.busy }, function () { return run(false); }, e && e.currentTarget); } })); return; }
      var r = reads();
      if (!r && (o.dryRun || o.applyWith)) { acts.appendChild(NS.HaloButton({ label: o.previewLabel || "Preview", size: "small", cmd: o.previewWith ? "adds " + o.previewWith.join(" ") + " \u00b7 changes nothing" : o.applyWith ? "runs it without " + o.applyWith.join(" ") + " \u00b7 changes nothing" : "adds --dry-run \u00b7 changes nothing", disabledReason: missing(), onPress: function (e) { confirmThen({ busy: "checking\u2026" }, function () { return run(true); }, e && e.currentTarget); } })); }
      var a = { label: o.label || (r ? "Show it" : "Do it"), size: "small", solid: !r, kind: o.danger ? "danger" : "", cmd: r ? EFFECT.reads : EFFECT.changes, disabledReason: (r ? missing() : dis), ask: o.ask, yes: o.yes };
      a.onPress = function (e) { confirmThen({ label: a.label, ask: o.ask, yes: o.yes, danger: o.danger, busy: o.busy, failed: o.failed, cmd: lineFor(o.path, fields, values, acting(false)) }, function () { return run(false); }, e && e.currentTarget); };
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
      row.appendChild(NS.HaloButton({ label: a.label, size: "small", kind: a.danger ? "danger" : "", glyph: a.glyph, cmd: a.cmd, disabledReason: a.disabledReason, onPress: function (e) { confirmThen(a, function () { return settle(a, a.onPress && a.onPress(), function (r) { said(out, typeof r === "string" ? r : (a.said || "Done")); }, out); }, e && e.currentTarget); } }));
    });
    return h("div", { "class": "ls-arow" }, [o.title ? h("h4", { "class": "t", text: o.title }) : null, o.why ? h("p", { "class": "why", text: o.why }) : null, row, out]);
  }

  /* SettingRow: a value a command keeps. What it is now, a control to change it, and the line that sets or clears it.
     disabledReason holds the control and its buttons and says why in place of the line, as a disabled button does. */
  function SettingRow(o) {
    o = o || {};
    var cur = o.value === undefined || o.value === null ? "" : String(o.value), draft = cur, id = uid("ls-sr-"), why = o.disabledReason || null;
    var now = h("span", { "class": "now" }), line = h("code", { "class": "ls-cmd", id: id + "-line" }), out = h("p", { "class": "ls-said", role: "status" }), acts = h("div", { "class": "acts" });
    var ctl;
    if (o.kind === "switch") {
      ctl = NS.Switch({ label: o.label, sub: o.sub, checked: cur === "on" || cur === "true", onChange: function (on) { draft = on ? "on" : "off"; commit(); } });
    } else if (o.kind === "choice") {
      ctl = h("select", { "class": "ls-text", id: id, onchange: function () { draft = ctl.value; paint(); } }, (cur ? [] : [h("option", { value: "", text: o.choose || "Choose one\u2026" })]).concat((o.options || []).map(function (x) { var val = Array.isArray(x) ? x[0] : x; var op = h("option", { value: val, text: Array.isArray(x) ? x[1] : x }); if (val === cur) { op.selected = true; } return op; })));
    } else {
      ctl = h("input", { "class": "ls-text", id: id, type: o.kind === "number" ? "number" : "text", placeholder: o.placeholder || null, autocomplete: "off", spellcheck: "false", oninput: function () { draft = ctl.value; paint(); } });
      ctl.value = cur;
    }
    if (why) { ctl.disabled = true; ctl.setAttribute("aria-disabled", "true"); ctl.setAttribute("aria-describedby", id + "-line"); }
    function commit() { if (why) { return; } var v = draft.trim(); var r = v ? (o.onSet && o.onSet(v)) : (o.onClear && o.onClear()); cur = v; said(out, typeof r === "string" ? r : (v ? (o.setSaid || "Set") : (o.clearSaid || "Cleared"))); paint(); }
    function paint() {
      var v = String(draft).trim();
      now.textContent = cur ? (o.show ? o.show(cur) : cur) : (o.unset || "not set");
      now.classList.toggle("unset", !cur);
      line.textContent = v ? o.setCmd(v) : (cur && o.clearCmd ? o.clearCmd : o.setCmd("<" + (o.valueWord || "value") + ">"));
      acts.textContent = "";
      if (o.kind === "switch") { line.textContent = o.setCmd(cur === "on" ? "off" : "on"); }
      /* Held: the reason stands where the command would, and the buttons point at it rather than each saying it again. */
      line.classList.toggle("reason", !!why);
      if (why) { line.textContent = why; }
      if (o.kind === "switch") { return; }
      acts.appendChild(NS.HaloButton({ label: o.setLabel || "Set it", size: "small", solid: !why, inactive: !!why, describedBy: why ? id + "-line" : null, disabledReason: why ? null : !v ? "write a value first" : v === cur ? "that is already the value" : null, onPress: commit }));
      if (o.clearCmd && cur) { acts.appendChild(NS.HaloButton({ label: o.clearLabel || "Clear it", size: "small", inactive: !!why, describedBy: why ? id + "-line" : null, onPress: function () { if (why) { return; } draft = ""; if (ctl.value !== undefined && o.kind !== "choice") { ctl.value = ""; } commit(); } })); }
    }
    paint();
    var head = o.kind === "switch" ? [ctl] : [h("label", { "class": "nm", "for": id, text: o.label }), o.sub ? h("span", { "class": "sub", text: o.sub }) : null, h("span", { "class": "nowwrap" }, [h("span", { "class": "k", text: "now " }), now])];
    return h("div", { "class": "ls-setrow" + (o.kind === "switch" ? " sw" : "") + (why ? " held" : "") }, [h("div", { "class": "head" }, head), o.kind === "switch" ? null : h("div", { "class": "ctl" }, [ctl, acts]), line, out]);
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
          confirmThen({ label: a.label, askTitle: a.askTitle, ask: a.ask, yes: a.yes, danger: a.danger, cmd: a.cmd, busy: a.busy, failed: a.failed }, function () {
          return settle(a, a.onPress && a.onPress(it), function (r) {
          if (a.removes) { items = items.filter(function (x) { return x !== it; }); paint(); }
          if (r && r.nodeType && det) { det.textContent = ""; det.appendChild(r); det.hidden = false; }
          said(out, typeof r === "string" ? r : (a.said ? (typeof a.said === "function" ? a.said(it) : a.said) : a.label + ": done")); }, out); }); });
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

  /* Commands shown: always, on hover (the default) or never. Studio keeps it as studio-commands. */
  var COMMANDS = ["always", "hover", "never"];
  function setCommands(v, el) { (el || document.documentElement).setAttribute("data-commands", COMMANDS.indexOf(v) >= 0 ? v : "hover"); }
  function commandsShown() { return document.documentElement.getAttribute("data-commands") || "hover"; }
  /* On hover, a command floats in beside what it belongs to; one that would run past the screen's right edge is moved back in. */
  var FLOATS = ".ls-cmd:not(.reason), .ls-cmdline, .ls-from, .ls-ilist .from, .ls-readout .from", floatQueued = false;
  function keepFloatsInView() {
    floatQueued = false;
    if (commandsShown() !== "hover") { return; }
    Array.prototype.forEach.call(document.querySelectorAll(FLOATS), function (n) {
      if (n.style.marginLeft) { n.style.marginLeft = ""; }
      var cs = getComputedStyle(n);
      if (cs.position !== "absolute" || cs.display === "none") { return; }
      var r = n.getBoundingClientRect(), over = r.right - (document.documentElement.clientWidth - 8);
      if (over > 0) { n.style.marginLeft = -Math.min(over, Math.max(0, r.left - 8)) + "px"; }
    });
  }
  function queueFloats() { if (!floatQueued) { floatQueued = true; requestAnimationFrame(keepFloatsInView); } }
  if (typeof document !== "undefined" && !NS.__floatsWatched) {
    NS.__floatsWatched = true;
    ["pointerover", "focusin", "keyup"].forEach(function (t) { document.addEventListener(t, queueFloats, true); });
  }

  Object.assign(NS, { ActionForm: ActionForm, ActionRow: ActionRow, SettingRow: SettingRow, ItemList: ItemList, Readout: Readout, controlLine: lineFor, COMMANDS: COMMANDS, setCommands: setCommands, commandsShown: commandsShown });
})();
/* end stage 13 */
