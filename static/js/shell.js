// App frame: top resource bar, facility navigation, hash router, banner, pending-changes bar.
import { h, $, fmtNum } from "./dom.js";
import {
  S, onState, onPending, emitState, setBanner, pendingCount, discard, submit, getValue, isChanged,
  setEdit, valid, describeEdit, actionList, loadSaves, refresh,
} from "./store.js";
import * as saves from "./screens/saves.js";
import * as command from "./screens/command.js";
import * as personnel from "./screens/personnel.js";
import * as upgrades from "./screens/upgrades.js";
import * as armory from "./screens/armory.js";
import * as galaxy from "./screens/galaxy.js";
import * as medbay from "./screens/medbay.js";
import * as advanced from "./screens/advanced.js";

const SCREENS = { saves, command, personnel, upgrades, armory, medbay, galaxy, advanced };
const NAV = [
  ["command", "Command"], ["personnel", "Personnel"], ["armory", "Armory"],
  ["upgrades", "Upgrades"], ["medbay", "Medbay"], ["galaxy", "Galaxy"], ["advanced", "Advanced"],
];

export function route() {
  const parts = location.hash.replace(/^#\/?/, "").split("/").filter(Boolean).map(decodeURIComponent);
  let name = parts[0] || (S.state ? "command" : "saves");
  if (!SCREENS[name] || (!S.state && name !== "saves")) name = "saves";
  return { name, args: parts.slice(1) };
}
export const go = (...parts) => { location.hash = "#/" + parts.map(encodeURIComponent).join("/"); };

// ------------------------------------------------------------------- top bar
function chip(icon, label, ref, offset = 0, prefix = "") {
  const el = h("button", { type: "button", class: "chip", title: label });
  const paint = () => {
    el.classList.toggle("changed", isChanged(ref));
    el.replaceChildren(h("span", { class: "ic" }, icon), prefix + fmtNum(getValue(ref) + offset));
  };
  el.addEventListener("click", () => {
    const input = h("input", { type: "number", value: getValue(ref) + offset, min: ref.min + offset, max: ref.max + offset, "aria-label": label });
    const done = () => {
      const v = Number(input.value) - offset;
      if (input.value !== "" && valid(ref, v)) setEdit(ref, v);
      paint();
    };
    input.addEventListener("keydown", (e) => { if (e.key === "Enter") input.blur(); if (e.key === "Escape") { input.value = getValue(ref) + offset; input.blur(); } });
    input.addEventListener("blur", done);
    el.replaceChildren(h("span", { class: "ic" }, icon), prefix, input);
    input.focus(); input.select();
  });
  paint();
  const off = onPending(() => { if (!el.isConnected) { off(); return; } if (!el.querySelector("input")) paint(); });
  return el;
}

const RES_ICON = { Credits: "◈", Intelligence: "✦", UpgradeFacilityResource: "▣", Contacts: "☍" };

function topbar() {
  const bar = h("header", { class: "panel mx-3 mt-3 px-4 py-2 flex flex-wrap items-center gap-x-5 gap-y-2" });
  bar.append(h("div", { class: "mr-auto" },
    h("div", { class: "font-display text-xl text-cream leading-none" }, "Zero Company"),
    h("div", { class: "text-[11px] text-mute caps" }, "Save editor")));
  if (S.state) {
    const hd = S.state.view.header;
    bar.append(
      h("div", { class: "hidden md:block text-right leading-tight" },
        h("div", { class: "font-display text-lg text-cream" }, S.state.view.command.save.title || S.state.name),
        h("div", { class: "text-[11px] text-mute" }, S.state.name + " · " + S.state.dir)));
    const res = h("div", { class: "flex flex-wrap items-center gap-1" });
    for (const r of hd.resources) res.append(chip(RES_ICON[r.key] || "◇", r.label, r));
    if (hd.level) {
      res.append(chip("⬡", "Level (LV)", hd.level, hd.level.offset, "LV "));
    }
    if (hd.turn) res.append(chip("⟳", "Strategy turn", hd.turn, 0, "Turn "));
    bar.append(res);
  }
  return bar;
}

function navbar(current) {
  const nav = h("nav", { class: "mx-3 mt-2 flex items-center gap-1 overflow-x-auto thin-scroll whitespace-nowrap sm:flex-wrap", "aria-label": "Facilities" });
  if (S.state) {
    for (const [key, label] of NAV)
      nav.append(h("button", { type: "button", class: "nav-btn" + (current === key ? " active" : ""), onclick: () => go(key) }, label));
  }
  nav.append(h("button", { type: "button", class: "nav-btn ml-auto" + (current === "saves" ? " active" : ""),
    onclick: async () => { await loadSaves(); go("saves"); } }, S.state ? "Change save" : "Saves"));
  return nav;
}

function banner() {
  if (!S.banner) return null;
  const cls = { info: "text-glow", ok: "text-ok", err: "text-bad" }[S.banner.kind] || "text-glow";
  return h("div", { role: "status", class: "panel mx-3 mt-2 px-4 py-2 text-sm flex items-start gap-3 " + cls },
    h("div", { class: "flex-1" }, S.banner.text),
    h("button", { type: "button", class: "text-mute hover:text-cream", "aria-label": "Dismiss", onclick: () => setBanner(null) }, "✕"));
}

// --------------------------------------------------------------- action bar
let listOpen = false;
function actionbar() {
  const n = pendingCount();
  const wrap = h("div", { id: "actionbar", class: "fixed inset-x-0 bottom-0 z-30 " + (n ? "" : "hidden") });
  if (!n) return wrap;
  const edits = [...S.edits.keys()].map(describeEdit);
  const acts = actionList().map((a) => {
    if (a.type === "heal_operator") {
      const who = S.state.view.medbay.injured.find((o) => o.guid === a.guid);
      return { where: "Medbay", label: "Heal " + (who ? who.name : "operator") + " (free, instant)" };
    }
    if (a.type === "reorder_roster") {
      const names = new Map(S.state.view.personnel.roster.map((o) => [o.guid, o.name]));
      return { where: "Personnel", label: "Roster order: " + a.order.map((g) => names.get(g) || "?").join(", ") };
    }
    if (a.type === "remove_coil_upgrades") {
      const label = (c) => (S.state.view.coil.active.find((u) => u.id === c.id) || { name: c.id }).name + (c.to === "Prevented" ? " (prevent)" : "");
      return { where: "Galaxy", label: "Remove Coil upgrade" + (a.changes.length === 1 ? ": " : "s: ") + a.changes.map(label).join(", ") };
    }
    const row = S.state.upgrades.items.find((x) => x.id === a.id);
    return { where: "Upgrades", label: ({ start_upgrade: "Start ", start_expedite_upgrade: "Start + expedite ", expedite_upgrade: "Expedite " })[a.type] + (row ? row.title : a.name) };
  });
  const list = h("div", { class: "panel mx-3 mb-1 max-h-56 overflow-auto thin-scroll text-sm " + (listOpen ? "" : "hidden") },
    ...edits.map((e) => h("div", { class: "px-3 py-1 flex gap-3 border-b hair" },
      h("span", { class: "text-mute w-40 truncate" }, e.where), h("span", { class: "flex-1" }, e.label),
      h("span", { class: "text-mute" }, e.from == null ? "" : fmtNum(e.from) + " →"), h("span", { class: "text-amber" }, fmtNum(e.to)))),
    ...acts.map((a) => h("div", { class: "px-3 py-1 flex gap-3 border-b hair" },
      h("span", { class: "text-mute w-40 truncate" }, a.where), h("span", { class: "flex-1 text-amber" }, a.label))));
  const force = h("input", { type: "checkbox", id: "force", class: "h-4 w-4 accent-amber" });
  const bar = h("div", { class: "panel mx-3 mb-3 px-4 py-2 flex flex-wrap items-center gap-3 shadow-2xl", style: { background: "#0d141d" } },
    h("button", { type: "button", class: "text-amber font-display text-lg tracking-wide hover:underline",
      onclick: () => { listOpen = !listOpen; render(); } }, n + " pending change" + (n === 1 ? "" : "s") + (listOpen ? " ▾" : " ▸")),
    S.state.game_running ? h("label", { class: "text-xs text-bad flex items-center gap-1" }, force, "Game seems to be running — override") : null,
    h("div", { class: "ml-auto flex gap-2" },
      h("button", { type: "button", class: "btn", onclick: discard }, "Discard"),
      h("button", { type: "button", class: "btn", disabled: S.busy, onclick: () => run(true, force.checked) }, "Save as copy"),
      h("button", { type: "button", class: "btn btn-primary", disabled: S.busy, onclick: () => run(false, force.checked) },
        S.busy ? "Writing & verifying…" : "Apply to save")));
  wrap.append(list, bar);
  return wrap;
}

async function run(copy, force) {
  const total = pendingCount(), acts = actionList().length;
  if (!copy && !confirm("Apply " + total + " change(s) to " + S.state.name +
      (acts ? "\n\nThis includes " + acts + " action(s) such as starting upgrades or healing operators; upgrades finish at the next turn change." : "") +
      "\nA backup is made first.")) return;
  await submit(copy, force);
  window.scrollTo({ top: 0, behavior: "smooth" });
}

// -------------------------------------------------------------------- render
export function render() {
  const { name, args } = route();
  const root = $("app");
  const keepScroll = window.scrollY;
  const screen = h("main", { id: "screen", class: "flex-1 mx-3 my-3 pb-24" });
  try {
    screen.append(SCREENS[name].render(args));
  } catch (e) {
    console.error(e);
    screen.append(h("div", { class: "panel p-4 text-bad" }, "This screen failed to draw: " + e.message));
  }
  root.replaceChildren(topbar(), navbar(name), ...[banner()].filter(Boolean), screen, actionbar());
  window.scrollTo({ top: keepScroll });
}

export function start() {
  onState(render);
  onPending(() => {                         // only the action bar changes while typing
    const old = $("actionbar");
    if (old) old.replaceWith(actionbar());
  });
  window.addEventListener("hashchange", () => {
    if (S.banner && S.banner.kind !== "err") S.banner = null;     // errors stay until dismissed
    render();
  });
  loadSaves()
    .then(() => refresh().catch(() => {}))          // resume a save the server already has open (e.g. after a page reload)
    .then(render)
    .catch((e) => { S.banner = { text: e.message, kind: "err" }; render(); });
}
