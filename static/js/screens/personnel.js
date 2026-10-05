// Personnel: roster strip (portraits) + Overview / Bonds / Focus Tree, as in the game.
import { h, fmtNum } from "../dom.js";
import { S, getValue, setEdit, isChanged } from "../store.js";
import { numInput, stepper, pips, face, live } from "../widgets.js";
import { go } from "../shell.js";

const TABS = [["overview", "Overview"], ["bonds", "Bonds"], ["focus", "Focus Tree"]];
const KIND_LABELS = { Class: "Class abilities", Passive: "Passives", Identity: "Signature", Defense: "Defense" };
let selBond = null;                  // partner guid selected on the Bonds tab

function unspent(op) { return op.focus ? getValue(op.focus) : 0; }

// ----------------------------------------------------------------- roster strip
function strip(ops, memorial, cur, tab) {
  const chip = (op) => {
    const fp = op.focus ? h("span", { class: "fp", title: "Unspent focus points" }) : null;
    if (fp) live(fp, () => String(unspent(op)));
    return h("button", {
      type: "button", class: "roster-chip" + (op.guid === cur.guid ? " sel" : "") + (op.dead ? " dead" : ""),
      onclick: () => go("personnel", op.guid, tab), "aria-label": op.name,
    }, face(op), fp, h("div", { class: "text-[13px] mt-1 leading-tight truncate caps font-display" }, op.name));
  };
  const strip = h("div", { class: "panel px-3 py-3 flex gap-2 overflow-x-auto thin-scroll items-start", role: "tablist" });
  ops.forEach((o) => strip.append(chip(o)));
  if (memorial.length) {
    strip.append(h("div", { class: "self-stretch w-px bg-edge mx-2" }),
      h("div", { class: "self-center text-[11px] text-mute caps rotate-0 px-1" }, "Memorial"));
    memorial.forEach((o) => strip.append(chip(o)));
  }
  return strip;
}

function tabs(cur, tab) {
  return h("div", { class: "flex justify-center gap-1 mb-3", role: "tablist" },
    TABS.map(([k, l]) => h("button", { type: "button", role: "tab", "aria-selected": String(tab === k),
      class: "tab-btn" + (tab === k ? " active" : ""), onclick: () => go("personnel", cur.guid, k) }, l)));
}

// -------------------------------------------------------------------- overview
function overview(op) {
  const left = h("div", { class: "space-y-3" });
  left.append(h("section", { class: "panel p-4 flex items-center gap-4" },
    face(op, "face w-24 h-24 rounded-full overflow-hidden border-2 border-edge flex items-center justify-center bg-deck"),
    h("div", {},
      h("div", { class: "font-display text-3xl text-cream leading-none" }, op.name),
      h("div", { class: "text-glow caps text-sm" }, op.dead ? "Fallen · Memorial" : (op.role || "Operator")),
      op.injuries ? h("div", { class: "text-bad text-xs caps mt-1" }, "Injured (" + op.injuries + ")") : null)));

  if (op.focus) {
    const total = h("b", { class: "text-cream" });
    live(total, () => fmtNum((op.focus.total ?? 0) + (getValue(op.focus) - op.focus.value)));
    left.append(h("section", { class: "panel" }, h("div", { class: "panel-h" }, "Focus points"),
      h("div", { class: "flex items-center gap-3 px-3 py-3" },
        h("div", { class: "flex-1" }, h("div", { class: "text-lg" }, "Unspent focus points"),
          h("div", { class: "text-xs text-mute" }, "Total focus moves with it, so points spent stay the same.")),
        numInput(op.focus, { cls: "w-24" })),
      h("div", { class: "px-3 pb-3 text-sm text-mute" }, "Total earned: ", total)));
  }

  const eff = h("section", { class: "panel" }, h("div", { class: "panel-h" }, "Training & stat effects"));
  if (!op.effects.length) eff.append(h("p", { class: "p-3 text-sm text-mute" }, "No stat effects yet."));
  for (const e of op.effects) {
    const mags = e.magnitudes.length
      ? h("details", { class: "mt-1 text-xs" }, h("summary", { class: "text-mute cursor-pointer" }, "Value per stack (experimental)"),
          h("div", { class: "flex flex-wrap gap-2 mt-1" }, e.magnitudes.map((m) => numInput(m, { cls: "w-24" }))))
      : null;
    eff.append(h("div", { class: "flex items-start gap-3 px-3 py-2 border-b hair" },
      h("div", { class: "flex-1 min-w-0" }, h("div", {}, e.title),
        e.description ? h("div", { class: "text-xs text-mute" }, e.description) : null, mags),
      e.stacks ? stepper(e.stacks) : null));
  }
  left.append(eff);
  return left;
}

// ----------------------------------------------------------------------- bonds
function bonds(op, roster) {
  const wrap = h("div", { class: "space-y-3" });
  const draw = () => {
    const sel = op.bonds.find((b) => b.partner === selBond);
    const scale = h("div", { class: "panel px-3 pt-4 pb-3" });
    const cols = h("div", { class: "grid gap-1 items-end", style: { gridTemplateColumns: "repeat(9, minmax(0,1fr))", minHeight: "150px" } });
    for (let lvl = 0; lvl <= 8; lvl++) {
      const col = h("div", { class: "bond-col justify-end" });
      op.bonds.filter((b) => b.level && Math.round(getValue(b.level) + 4) === lvl).forEach((b) => {
        const p = roster.get(b.partner) || { name: b.partner_name, portrait: null };
        col.append(h("button", {
          type: "button", title: b.partner_name + " — " + lvl,
          class: "bond-face" + (b.partner === selBond ? " sel" : "") + (isChanged(b.level) ? " changed" : ""),
          onclick: () => { selBond = b.partner; draw(); },
        }, face(p, "contents")));
      });
      cols.append(col);
    }
    const labels = h("div", { class: "grid text-center text-sm text-mute mt-2", style: { gridTemplateColumns: "repeat(9, minmax(0,1fr))" } });
    for (let i = 0; i <= 8; i++)
      labels.append(h("div", { class: i === 4 ? "text-cream" : "" }, String(i)));
    const words = h("div", { class: "flex justify-between text-xs caps text-mute mt-1" },
      h("span", { class: "text-amber" }, "Very low"), h("span", { class: "text-cream" }, "Neutral"), h("span", { class: "text-ok" }, "Very high"));
    scale.append(cols, h("div", { class: "scale mt-2" }), labels, words);

    const detail = h("section", { class: "panel" }, h("div", { class: "panel-h" }, sel ? "Bond with " + sel.partner_name : "Select a partner"));
    if (sel) {
      const lvlVal = h("b", { class: "text-cream w-6 text-center" });
      const slider = h("input", { type: "range", min: "0", max: "8", step: "1", value: String(getValue(sel.level) + 4),
        class: "flex-1 accent-amber", "aria-label": "Bond level (0-8)" });
      slider.addEventListener("input", () => { setEdit(sel.level, Number(slider.value) - 4); lvlVal.textContent = slider.value; });
      slider.addEventListener("change", draw);
      lvlVal.textContent = slider.value;
      detail.append(
        h("div", { class: "flex items-center gap-3 px-3 py-2 border-b hair" }, h("div", { class: "w-44" }, "Bond level (0–8)"), slider, lvlVal),
        h("div", { class: "flex items-center gap-3 px-3 py-2 border-b hair" }, h("div", { class: "flex-1" }, "Progress to next level"), numInput(sel.progress, { cls: "w-24" })),
        h("div", { class: "flex items-center gap-3 px-3 py-2 border-b hair" }, h("div", { class: "flex-1" }, "Cross training available"), numInput(sel.cross, { cls: "w-24" })),
        sel.highest ? h("div", { class: "flex items-center gap-3 px-3 py-2" },
          h("div", { class: "flex-1" }, "Highest level reached", h("div", { class: "text-xs text-mute" }, "Game scale 0–8 = this + 4")), numInput(sel.highest, { offset: 4, cls: "w-24" })) : null);
    } else {
      detail.append(h("p", { class: "p-3 text-sm text-mute" }, "Click a face on the scale to edit that bond."));
    }
    wrap.replaceChildren(scale, detail);
  };
  draw();
  return wrap;
}

// ------------------------------------------------------------------ focus tree
let freeFocus = false;               // false: levelling spends unspent focus; true: it grants the focus

// Set an ability to level L using the game's own cumulative cost table, keeping
// total = focus spent + unspent (the invariant every saved operator obeys).
function setAbilityLevel(op, a, L, notice) {
  const t = a.thresholds;
  const newSpent = t[L - 1], d = newSpent - getValue(a.spent);
  const avail = getValue(op.focus), total = getValue(op.focus.total_ref);
  if (!freeFocus && d > avail) {
    notice.textContent = a.name + " level " + L + " needs " + d + " more focus but only " + avail +
      " is unspent. Switch to \"Grant the focus\" to level it anyway.";
    return false;
  }
  if (!freeFocus && avail - d > op.focus.max) { notice.textContent = "That would refund more focus than the game allows."; return false; }
  if (freeFocus && (total + d < op.focus.total_ref.min || total + d > op.focus.total_ref.max)) {
    notice.textContent = "Total focus would leave its allowed range."; return false;
  }
  notice.textContent = "";
  setEdit(a.level, L);
  setEdit(a.spent, newSpent);
  if (d !== 0) {
    if (freeFocus) setEdit(op.focus.total_ref, total + d);
    else setEdit(op.focus, avail - d, { link: false });       // total stays: points just move
  }
  return true;
}

function levelPips(op, a, redraw, notice) {
  const wrap = h("span", { class: "pips", role: "group", "aria-label": a.name + " level" });
  const cur = getValue(a.level);
  a.thresholds.forEach((cost, i) => {
    const L = i + 1;
    wrap.append(h("button", {
      type: "button", class: "pip" + (L <= cur ? " on" : "") + (isChanged(a.level) ? " changed" : ""),
      title: "Level " + L + (cost ? " · " + cost + " focus in total" : " · free"), "aria-label": "Set level " + L,
      onclick: () => { if (setAbilityLevel(op, a, L, notice)) redraw(); },
    }));
  });
  return wrap;
}

function focusTree(op) {
  const wrap = h("div", { class: "space-y-3" });
  const notice = h("p", { class: "text-sm text-amber min-h-[1.25rem]", role: "status", "aria-live": "polite" });
  const draw = () => {
    const groups = new Map();
    for (const a of op.abilities) {
      const k = KIND_LABELS[a.kind] || a.kind || "Abilities";
      if (!groups.has(k)) groups.set(k, []);
      groups.get(k).push(a);
    }
    const spentTotal = op.abilities.reduce((n, a) => n + (a.spent ? getValue(a.spent) : 0), 0);
    const mode = h("div", { class: "panel px-3 py-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-sm" },
      h("span", { class: "text-mute caps font-display" }, "Levelling"),
      ...[[false, "Spend unspent focus"], [true, "Grant the focus"]].map(([v, label]) => h("label", { class: "flex items-center gap-1 cursor-pointer" },
        h("input", { type: "radio", name: "focusmode", class: "accent-amber", checked: freeFocus === v,
          onchange: () => { freeFocus = v; notice.textContent = ""; draw(); } }), label)),
      h("span", { class: "ml-auto text-mute" }, "Spent ", h("b", { class: "text-cream" }, fmtNum(spentTotal)),
        op.focus ? [" · Unspent ", h("b", { class: "text-amber" }, fmtNum(getValue(op.focus)))] : null));
    const body = [mode, notice];
    for (const [name, list] of groups) {
      const sec = h("section", { class: "panel" }, h("div", { class: "panel-h" }, name));
      for (const a of list) {
        let ctl, info;
        if (a.thresholds && a.level && op.focus && op.focus.total_ref) {
          const lvl = getValue(a.level), nxt = lvl < a.thresholds.length ? a.thresholds[lvl] - a.thresholds[lvl - 1] : null;
          ctl = levelPips(op, a, draw, notice);
          info = h("div", { class: "text-xs text-mute text-right w-40" },
            "Level " + lvl + "/" + a.thresholds.length + " · " + a.thresholds[lvl - 1] + " spent",
            h("div", {}, nxt == null ? "Max level" : "Next level costs " + nxt));
        } else if (a.level) {                                   // no cost table known: edit independently
          ctl = pips(a.level, 6);
          info = a.spent ? h("div", { class: "flex items-center gap-1 text-xs text-mute" }, "spent", numInput(a.spent, { cls: "w-16" })) : null;
        } else {
          ctl = h("span", { class: "text-xs text-mute" }, "Level " + a.current_level + " (fixed)");
          info = null;
        }
        sec.append(h("div", { class: "flex items-center gap-3 px-3 py-2 border-b hair" },
          h("div", { class: "flex-1 min-w-0" }, h("div", {}, a.name)), ctl, info));
      }
      body.push(sec);
    }
    if (!op.abilities.length) body.push(h("p", { class: "text-sm text-mute" }, "No focus tree data for this operator."));
    wrap.replaceChildren(...body);
  };
  draw();
  return wrap;
}

function crossBox(personnel) {
  const n = h("b", { class: "text-3xl font-display text-cream" });
  live(n, () => String(S.state.view.personnel.roster.concat(S.state.view.personnel.memorial)
    .flatMap((o) => o.bonds.filter((b) => b.cross && o.guid < b.partner))
    .reduce((s, b) => s + getValue(b.cross), 0)));
  return h("div", { class: "panel p-3 flex items-center gap-3" }, n,
    h("div", { class: "text-sm" }, h("div", { class: "caps font-display text-lg leading-none" }, "Cross training available"),
      h("div", { class: "text-xs text-mute" }, "whole roster · sum of every bond")));
}

// ------------------------------------------------------------------------ main
export function render(args) {
  const p = S.state.view.personnel;
  const all = p.roster.concat(p.memorial);
  const cur = all.find((o) => o.guid === args[0]) || all[0];
  if (!cur) return h("p", { class: "text-mute" }, "No operators found in this save.");
  const tab = TABS.some(([k]) => k === args[1]) ? args[1] : "overview";
  if (!cur.bonds.some((b) => b.partner === selBond)) selBond = null;
  if (!selBond && tab === "bonds" && cur.bonds.length) selBond = cur.bonds[0].partner;     // strongest bond first
  const byGuid = new Map(all.map((o) => [o.guid, o]));

  const body = tab === "overview" ? overview(cur) : tab === "bonds" ? bonds(cur, byGuid) : focusTree(cur);
  const side = h("aside", { class: "space-y-3" },
    h("div", { class: "panel p-3 flex flex-col items-center" },
      face(cur, "face w-48 h-48 rounded-full overflow-hidden border-2 border-edge flex items-center justify-center bg-deck"),
      h("div", { class: "font-display text-2xl text-cream mt-2" }, cur.name),
      h("div", { class: "text-glow caps text-sm" }, cur.dead ? "Fallen" : cur.role)),
    tab !== "overview" || cur.focus ? h("div", { class: "panel p-3 flex items-center gap-3" },
      (() => { const b = h("b", { class: "text-3xl font-display text-amber" }); live(b, () => String(unspent(cur))); return b; })(),
      h("div", { class: "caps font-display text-lg leading-none" }, "Focus points remaining")) : null,
    tab === "bonds" ? crossBox(p) : null);

  return h("div", { class: "space-y-3" },
    tabs(cur, tab),
    h("div", { class: "grid gap-3 lg:grid-cols-[1fr,300px]" }, h("div", {}, body), side),
    strip(p.roster, p.memorial, cur, tab));
}
