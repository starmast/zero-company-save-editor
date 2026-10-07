// Galaxy (Holotable): one card per map region with its influence, contacts and reward tier.
import { h } from "../dom.js";
import { S, toggle } from "../store.js";
import { numInput, stepper } from "../widgets.js";

function field(label, control, note) {
  return h("div", { class: "flex items-center gap-3 py-1" },
    h("div", { class: "flex-1" }, label, note ? h("div", { class: "text-[11px] text-mute" }, note) : null), control);
}

// One button per Coil upgrade (queues its removal) plus "remove all".
function removeButton(u) {
  const b = h("button", { type: "button", class: "btn shrink-0" });
  const paint = () => {
    const on = S.coilRemoves.has(u.id);
    b.textContent = on ? "Queued - undo" : "Remove";
    b.classList.toggle("btn-primary", !on);
    b.setAttribute("aria-pressed", String(on));
  };
  b.addEventListener("click", () => { toggle(S.coilRemoves, u.id); paint(); });
  paint();
  return b;
}

function coilPanel() {
  const list = S.state.view.coil.active;
  const panel = h("section", { class: "panel" },
    h("div", { class: "panel-h flex items-center" }, "Active Coil upgrades: " + list.length,
      list.length > 1 ? h("button", { type: "button", class: "btn ml-auto normal-case tracking-normal font-body",
        onclick: () => { const all = list.every((u) => S.coilRemoves.has(u.id)); for (const u of list) toggle(S.coilRemoves, u.id, !all); panel.replaceWith(coilPanel()); } },
        list.every((u) => S.coilRemoves.has(u.id)) ? "Undo all" : "Remove all") : null));
  if (!list.length) panel.append(h("p", { class: "p-4 text-sm text-mute" }, "The Coil have no permanent upgrades."));
  for (const u of list) {
    panel.append(h("div", { class: "flex items-center gap-3 px-3 py-2 border-b hair" },
      h("div", { class: "flex-1 min-w-0" },
        h("div", { class: "text-xs text-mute caps" }, u.unit + " · " + u.tier),
        h("div", { class: "font-display text-xl text-cream leading-tight" }, u.name),
        u.description ? h("div", { class: "text-sm text-mute" }, u.description) : null),
      removeButton(u)));
  }
  panel.append(h("p", { class: "px-3 py-2 text-xs text-mute" },
    "Upgrades the Coil keep permanently after a Crisis Mission or Operation is failed. Removing one puts that crisis back to available, " +
    "so it can be failed (and the upgrade gained) again."));
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
