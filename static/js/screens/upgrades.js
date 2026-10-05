// Upgrades: tabs (Facilities / Crew / Weapons), a row per upgrade line on a Den Level 1-10
// timeline, Build Slots and a detail panel - queueing starts/expedites through the existing actions.
import { h, fmtNum } from "../dom.js";
import { S, toggle, emitPending } from "../store.js";
import { go } from "../shell.js";

const RES = { Credits: "credits", UpgradeFacilityResource: "capacitors" };
const COLS = 10;
let sel = null;                       // selected node name

const allNodes = (u) => u.tabs.flatMap((t) => t.rows.flatMap((r) => r.nodes));
const roman = (n) => ["", "I", "II", "III", "IV", "V", "VI"][n] || String(n);

function stateOf(n) {
  if (n.status === "Completed") return "done";
  if (n.status === "InProgress") return "building";
  return n.can_start ? "open" : "locked";
}

function node(n, idx, u, redraw) {
  const st = stateOf(n);
  const queued = (n.id && (S.starts.has(n.id) || S.expedites.has(n.id)));
  const b = h("button", {
    type: "button", title: n.title + " — " + n.status,
    class: "up-node " + st + (n.major ? " major" : "") + (sel === n.name ? " sel" : "") + (queued ? " queued" : ""),
    style: n.major ? { width: "36px", height: "36px" } : {},
    onclick: () => { sel = n.name; redraw(); },
    "aria-label": n.title + ", " + n.status,
  }, h("span", {}, st === "done" ? "✓" : roman(idx + 1)));
  return b;
}

function row(r, u, redraw) {
  const box = h("div", { class: "relative grid items-center border-b hair", style: { gridTemplateColumns: `124px repeat(${COLS}, minmax(0,1fr))`, height: "58px" } });
  box.append(h("div", { class: "font-display text-lg text-cream leading-tight pr-2", style: { gridColumn: "1", gridRow: "1" } }, r.label));
  const dens = r.nodes.map((n) => n.den || 1);
  const lo = Math.min(...dens), hi = Math.max(...dens);
  if (hi > lo) box.append(h("div", { class: "absolute h-0.5 bg-edge", style: {
    left: `calc(124px + (100% - 124px) * ${lo - 0.5} / ${COLS})`, right: `calc((100% - 124px) * ${COLS - hi + 0.5} / ${COLS})`, top: "50%" } }));
  r.nodes.forEach((n, i) => {
    box.append(h("div", { class: "flex justify-center relative", style: { gridColumn: String((n.den || 1) + 1), gridRow: "1" } }, node(n, i, u, redraw)));
  });
  return box;
}

function axis(u) {
  const a = h("div", { class: "grid items-center text-center font-display text-lg text-mute border-b hair",
    style: { gridTemplateColumns: `124px repeat(${COLS}, minmax(0,1fr))`, height: "40px" } },
    h("div", { class: "text-left text-glow caps" }, "Den level"));
  for (let i = 1; i <= COLS; i++)
    a.append(h("div", { class: i === u.level ? "text-cream relative" : i < u.level ? "text-glow" : "" }, String(i),
      i === u.level ? h("span", { class: "absolute left-1/2 -bottom-2 text-amber text-xs", style: { transform: "translateX(-50%)" } }, "◆") : null));
  return a;
}

function costLine(n) {
  const parts = [];
  if (n.duration != null) parts.push("⌛ " + n.duration + (n.duration === 1 ? " turn" : " turns"));
  for (const [k, v] of Object.entries(n.cost || {})) parts.push(fmtNum(v) + " " + (RES[k] || k));
  return parts.join("  ·  ");
}

function detail(u, redraw) {
  const n = allNodes(u).find((x) => x.name === sel);
  const box = h("section", { class: "panel" });
  if (!n) {
    box.append(h("p", { class: "p-3 text-sm text-mute" }, "Select an upgrade on the timeline."));
    return box;
  }
  box.append(h("div", { class: "p-3 border-b hair" },
    h("div", { class: "font-display text-2xl text-cream leading-none" }, n.title),
    h("div", { class: "text-glow caps text-sm mt-1" }, n.major ? "Major upgrade" : "Upgrade", " · ",
      n.status === "InProgress" ? "Building" : n.status)));
  box.append(h("div", { class: "p-3 text-sm space-y-2" },
    n.description ? h("p", {}, n.description) : null,
    costLine(n) ? h("p", { class: "text-glow font-display text-lg tracking-wide" }, costLine(n)) : null,
    n.den ? h("p", { class: n.den_met ? "text-mute" : "text-amber" },
      "Den Level " + n.den + (n.den_met ? "" : " — you are at " + u.level + " (the editor can still start it)")) : null,
    n.reason ? h("p", { class: "text-bad" }, n.reason) : null));
  const act = h("div", { class: "p-3 pt-0 flex flex-wrap gap-2" });
  if (n.status === "Available" && n.can_start) {
    const on = S.starts.has(n.id);
    act.append(h("button", { type: "button", class: "btn " + (on ? "" : "btn-primary"), onclick: () => { toggle(S.starts, n.id); redraw(); } },
      on ? "Remove from queue" : S.alsoExpedite ? "Queue: start + expedite" : "Queue: start"));
  } else if (n.status === "InProgress") {
    const on = S.expedites.has(n.id);
    act.append(n.can_expedite
      ? h("button", { type: "button", class: "btn " + (on ? "" : "btn-primary"), onclick: () => { toggle(S.expedites, n.id); redraw(); } },
          on ? "Remove from queue" : "Queue: expedite")
      : h("span", { class: "text-xs text-mute self-center" }, "Already expedited — finishes at the next turn change."));
  } else if (n.status === "Completed") {
    act.append(h("span", { class: "text-ok caps font-display" }, "✓ Completed"));
  }
  box.append(act);
  return box;
}

function slots(u, redraw) {
  const box = h("section", { class: "panel" }, h("div", { class: "panel-h flex items-center" }, "Build slots",
    h("span", { class: "ml-auto text-xs text-mute normal-case tracking-normal font-body" }, u.in_progress + " in progress")));
  if (!u.slots.length) box.append(h("p", { class: "p-3 text-sm text-mute" }, "Nothing is being built."));
  for (const n of u.slots) {
    box.append(h("button", { type: "button", onclick: () => { sel = n.name; redraw(); },
      class: "w-full text-left flex items-center gap-2 px-3 py-2 border-b hair hover:bg-deck" },
      h("span", { class: "text-amber caps font-display" }, "Building"),
      h("span", { class: "flex-1 truncate" }, n.title),
      n.remaining != null ? h("span", { class: "px-2 bg-amber text-black font-display font-bold" }, "⌛ " + n.remaining) : null));
  }
  return box;
}

function toolbar(u, redraw) {
  const nodes = allNodes(u);
  const startable = nodes.filter((n) => n.can_start), expeditable = nodes.filter((n) => n.can_expedite);
  const chk = h("input", { type: "checkbox", class: "h-4 w-4 accent-amber", checked: S.alsoExpedite });
  chk.addEventListener("change", () => { S.alsoExpedite = chk.checked; emitPending(); redraw(); });
  return h("div", { class: "panel px-3 py-2 flex flex-wrap items-center gap-x-4 gap-y-2 text-sm" },
    h("label", { class: "flex items-center gap-2 cursor-pointer" }, chk, "Also expedite upgrades I start"),
    h("button", { type: "button", class: "btn", disabled: !startable.length,
      onclick: () => { startable.forEach((n) => S.starts.add(n.id)); emitPending(); redraw(); } }, "Queue all startable (" + startable.length + ")"),
    h("button", { type: "button", class: "btn", disabled: !expeditable.length,
      onclick: () => { expeditable.forEach((n) => S.expedites.add(n.id)); emitPending(); redraw(); } }, "Expedite all building (" + expeditable.length + ")"),
    h("button", { type: "button", class: "btn", disabled: !S.starts.size && !S.expedites.size,
      onclick: () => { S.starts.clear(); S.expedites.clear(); emitPending(); redraw(); } }, "Clear queue"),
    h("span", { class: "text-xs text-mute ml-auto" }, "Starting works like the game's Build button; the game finishes upgrades at the next turn change. No cost is charged."));
}

export function render(args) {
  const u = S.state.view.upgrades;
  const tab = u.tabs.some((t) => t.key === args[0]) ? args[0] : "Facilities";
  const wrap = h("div", { class: "space-y-3" });
  const tabNodes = u.tabs.find((x) => x.key === tab).rows.flatMap((r) => r.nodes);
  if (!tabNodes.some((n) => n.name === sel))                  // pre-select something useful on arrival
    sel = (tabNodes.find((n) => n.status === "InProgress") || tabNodes.find((n) => n.can_start) || tabNodes[0] || {}).name || null;
  const draw = () => {
    const t = u.tabs.find((x) => x.key === tab);
    const left = h("section", { class: "panel overflow-x-auto thin-scroll" },
      h("div", { style: { minWidth: "560px" } }, axis(u), ...t.rows.map((r) => row(r, u, draw))));
    if (!t.rows.length) left.append(h("p", { class: "p-3 text-sm text-mute" }, "No upgrades in this category."));
    wrap.replaceChildren(
      h("div", { class: "flex justify-center gap-1", role: "tablist" },
        u.tabs.map((x) => h("button", { type: "button", role: "tab", "aria-selected": String(x.key === tab),
          class: "tab-btn" + (x.key === tab ? " active" : ""), onclick: () => go("upgrades", x.key) }, x.key))),
      toolbar(u, draw),
      h("div", { class: "grid gap-3 lg:grid-cols-[1fr,340px]" }, left, h("div", { class: "space-y-3" }, slots(u, draw), detail(u, draw))));
  };
  draw();
  return wrap;
}
