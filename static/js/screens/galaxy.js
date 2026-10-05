// Galaxy (Holotable): one card per map region with its influence, contacts and reward tier.
import { h } from "../dom.js";
import { S } from "../store.js";
import { numInput, stepper } from "../widgets.js";

function field(label, control, note) {
  return h("div", { class: "flex items-center gap-3 py-1" },
    h("div", { class: "flex-1" }, label, note ? h("div", { class: "text-[11px] text-mute" }, note) : null), control);
}

export function render() {
  const regions = S.state.view.galaxy.regions;
  if (!regions.length) return h("p", { class: "text-mute" }, "No galaxy regions in this save.");
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
    h("p", { class: "text-xs text-mute" }, "Influence in each region of the galaxy map, as shown on the Holotable."), grid);
}
