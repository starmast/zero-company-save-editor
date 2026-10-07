// Galaxy (Holotable): one card per map region with its influence, contacts and reward tier.
import { h } from "../dom.js";
import { S, emitPending } from "../store.js";
import { numInput, stepper } from "../widgets.js";

function field(label, control, note) {
  return h("div", { class: "flex items-center gap-3 py-1" },
    h("div", { class: "flex-1" }, label, note ? h("div", { class: "text-[11px] text-mute" }, note) : null), control);
}

// Each Coil upgrade can be taken away two ways: back to Available (it can be gained again) or Prevented (as if won).
function choice(u, to, label, hint, repaintAll) {
  const b = h("button", { type: "button", class: "btn shrink-0", title: hint });
  const paint = () => {
    const on = S.coilChanges.get(u.id) === to;
    b.textContent = on ? label + " (queued) - undo" : label;
    b.classList.toggle("btn-primary", on);
    b.setAttribute("aria-pressed", String(on));
  };
  b.addEventListener("click", () => {
    if (S.coilChanges.get(u.id) === to) S.coilChanges.delete(u.id); else S.coilChanges.set(u.id, to);
    emitPending();
    repaintAll();
  });
  b._paint = paint;
  paint();
  return b;
}

function coilPanel() {
  const list = S.state.view.coil.active;
  const allOn = (to) => list.length > 0 && list.every((u) => S.coilChanges.get(u.id) === to);
  const bulk = (to) => {
    const all = allOn(to);
    for (const u of list) { if (all) S.coilChanges.delete(u.id); else S.coilChanges.set(u.id, to); }
    emitPending();
    panel.replaceWith(coilPanel());
  };
  const repaintAll = () => panel.querySelectorAll("button").forEach((x) => x._paint && x._paint());
  const panel = h("section", { class: "panel" },
    h("div", { class: "panel-h flex items-center gap-2" }, "Active Coil upgrades: " + list.length,
      list.length ? h("span", { class: "ml-auto flex gap-2 normal-case tracking-normal font-body" },
        h("button", { type: "button", class: "btn", onclick: () => bulk("Available") }, allOn("Available") ? "Undo all" : "Remove all"),
        h("button", { type: "button", class: "btn", onclick: () => bulk("Prevented") }, allOn("Prevented") ? "Undo all" : "Prevent all")) : null));
  if (!list.length) panel.append(h("p", { class: "p-4 text-sm text-mute" }, "The Coil have no permanent upgrades."));
  for (const u of list) {
    panel.append(h("div", { class: "flex items-center gap-3 px-3 py-2 border-b hair flex-wrap" },
      h("div", { class: "flex-1 min-w-[14rem]" },
        h("div", { class: "text-xs text-mute caps" }, u.unit + " · " + u.tier),
        h("div", { class: "font-display text-xl text-cream leading-tight" }, u.name),
        u.description ? h("div", { class: "text-sm text-mute" }, u.description) : null),
      h("div", { class: "flex gap-2" },
        choice(u, "Available", "Remove", "Take it away; the crisis can be failed (and the upgrade gained) again", repaintAll),
        choice(u, "Prevented", "Prevent", "Take it away as if the crisis had been won", repaintAll))));
  }
  panel.append(h("p", { class: "px-3 py-2 text-xs text-mute" },
    "Upgrades the Coil keep permanently after a Crisis Mission or Operation is failed. Remove puts that crisis back to available, " +
    "so it can be failed (and the upgrade gained) again. Prevent marks it as prevented, as if you had won it. Neither changes the paired A/B choice of the same crisis."));
  return panel;
}

export function render() {
  const regions = S.state.view.galaxy.regions;
  if (!regions.length) return h("div", { class: "space-y-3" }, h("p", { class: "text-mute" }, "No galaxy regions in this save."), coilPanel());
  const grid = h("div", { class: "grid gap-3", style: { gridTemplateColumns: "repeat(auto-fill,minmax(270px,1fr))" } });
  for (const r of regions) {
    grid.append(h("section", { class: "panel" },
      h("div", { class: "panel-h" }, r.name),
      h("div", { class: "px-3 py-2 text-sm" },
        r.influence ? field("Influence", numInput(r.influence, { cls: "w-24" })) : null,
        r.contacts ? field("Contacts", numInput(r.contacts, { cls: "w-24" })) : null,
        r.reward ? field("Reward tier claimed", stepper(r.reward), "-1 = none yet") : null)));
  }
  return h("div", { class: "space-y-3" },
    h("p", { class: "text-xs text-mute" }, "Influence in each region of the galaxy map, as shown on the Holotable."), grid, coilPanel());
}
