// Armory: Utility Items and Weapon Mods as cards (name, tier, rarity, description, count).
import { h } from "../dom.js";
import { S } from "../store.js";
import { stepper, numInput } from "../widgets.js";
import { go } from "../shell.js";

const TABS = [["Utility", "Utility items"], ["Modification", "Weapon mods"], ["Other", "Other"]];
let filter = "";

function card(it) {
  return h("article", { class: "panel p-3 flex gap-3 items-start" },
    h("div", { class: "flex-1 min-w-0" },
      h("div", { class: "font-display text-xl text-cream leading-tight" }, it.name),
      h("div", { class: "text-xs caps text-glow" },
        [it.tier ? "Tier " + it.tier : null, it.rarity || null].filter(Boolean).join(" · ")),
      it.description ? h("div", { class: "text-sm text-mute mt-1" }, it.description) : null),
    h("div", { class: "flex flex-col items-end gap-1" },
      h("span", { class: "text-[11px] text-mute caps" }, "Owned"),
      it.count.value <= 99 ? stepper(it.count) : numInput(it.count, { cls: "w-20" })));
}

export function render(args) {
  const items = S.state.view.armory.items;
  const have = TABS.filter(([k]) => items.some((i) => i.kind === k));
  const tab = have.some(([k]) => k === args[0]) ? args[0] : (have[0] || TABS[0])[0];
  const body = h("div", {});
  const draw = () => {
    const q = filter.toLowerCase();
    const list = items.filter((i) => i.kind === tab && (!q || (i.name + " " + i.description).toLowerCase().includes(q)));
    body.replaceChildren(list.length
      ? h("div", { class: "grid gap-3", style: { gridTemplateColumns: "repeat(auto-fill,minmax(300px,1fr))" } }, list.map(card))
      : h("p", { class: "text-sm text-mute" }, items.length ? "Nothing matches." : "Nothing in the inventory yet."));
  };
  draw();
  const search = h("input", { type: "search", placeholder: "Search items…", value: filter, class: "num w-56 text-left", "aria-label": "Search items" });
  search.addEventListener("input", () => { filter = search.value; draw(); });
  return h("div", { class: "space-y-3" },
    h("div", { class: "flex flex-wrap items-center gap-1" },
      ...have.map(([k, l]) => h("button", { type: "button", class: "tab-btn" + (k === tab ? " active" : ""), onclick: () => go("armory", k) },
        l + " " + items.filter((i) => i.kind === k).length)),
      h("div", { class: "ml-auto" }, search)),
    body);
}
