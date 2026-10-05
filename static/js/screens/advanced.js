// Advanced: the original flat field list (every editable value grouped as the save stores it)
// and the Raw property tree. Kept so nothing is lost while the game-style screens take over.
import { h, fmtNum } from "../dom.js";
import { S, getValue, setEdit, valid, isChanged } from "../store.js";
import { numInput } from "../widgets.js";
import { api } from "../api.js";
import { go } from "../shell.js";

const GROUPS = ["Resources", "Operators", "Abilities", "Bonds", "Galaxy", "Inventory", "Progression"];
let filter = "";

function fieldRow(f) {
  return h("div", { class: "flex items-center gap-3 px-3 py-1.5 text-sm border-b hair" },
    h("div", { class: "min-w-0 flex-1" }, h("div", {}, f.label),
      f.note ? h("div", { class: "text-xs text-mute" }, f.note) : null),
    h("span", { class: "text-xs text-mute w-28 text-right" }, "now " + fmtNum(f.value)),
    numInput(f, { cls: "w-36" }));
}

function fieldsPanel(tab) {
  const q = filter.toLowerCase();
  const bySection = new Map();
  for (const f of S.state.fields) {
    if (f.group !== tab) continue;
    if (q && !(f.label + " " + f.section).toLowerCase().includes(q)) continue;
    if (!bySection.has(f.section)) bySection.set(f.section, []);
    bySection.get(f.section).push(f);
  }
  if (!bySection.size) return h("p", { class: "text-sm text-mute" }, "Nothing matches.");
  return h("div", {}, ...[...bySection].map(([sec, fields]) =>
    h("section", { class: "panel mb-3" }, h("div", { class: "panel-h" }, sec), ...fields.map(fieldRow))));
}

// ---- raw property tree
async function loadRaw(container, id, depth) {
  const rows = await api("/api/tree" + (id ? "?id=" + encodeURIComponent(id) : ""));
  container.replaceChildren(...rows.map((r) => rawRow(r, depth)));
}
function rawRow(r, depth) {
  const kids = h("div", {});
  let open = false;
  const label = h("span", { class: "cursor-pointer " + (r.expandable ? "text-amber" : "text-slate-300") }, (r.expandable ? "▸ " : "· ") + r.name);
  const head = h("div", { class: "flex items-center gap-2 py-0.5 hover:bg-deck", style: { paddingLeft: depth * 14 + "px" } },
    label, h("span", { class: "text-mute" }, r.type + (r.count != null ? " [" + r.count + "]" : "") + (r.opaque ? " (raw)" : "")));
  if (r.field) {
    head.append(numInput({ id: r.field, value: r.value, min: r.kind === "float" ? -3.4e38 : -2147483648, max: r.kind === "float" ? 3.4e38 : 2147483647, kind: r.kind }, { cls: "w-36" }));
  } else if (r.value !== undefined) {
    head.append(h("span", { class: "text-ok truncate max-w-xl" }, String(r.value)));
  }
  if (r.expandable) label.addEventListener("click", async () => {
    open = !open; label.firstChild.textContent = (open ? "▾ " : "▸ ") + r.name;
    if (open) await loadRaw(kids, r.id, depth + 1); else kids.replaceChildren();
  });
  return h("div", {}, head, kids);
}

export function render(args) {
  const counts = {};
  for (const f of S.state.fields) counts[f.group] = (counts[f.group] || 0) + 1;
  const tabs = GROUPS.filter((g) => counts[g]).concat(["Raw"]);
  const tab = tabs.includes(args[0]) ? args[0] : tabs[0];
  const body = h("div", {});
  const draw = () => {
    if (tab === "Raw") {
      const root = h("div", { class: "font-mono text-xs" });
      body.replaceChildren(h("p", { class: "mb-3 text-xs text-mute" },
        "Raw property tree. Numbers (int/float) can be edited in place; the file size never changes. Use with care."), root);
      loadRaw(root, null, 0);
    } else body.replaceChildren(fieldsPanel(tab));
  };
  draw();
  const search = h("input", { type: "search", placeholder: "Filter…", value: filter, class: "num w-60 text-left", "aria-label": "Filter fields" });
  search.addEventListener("input", () => { filter = search.value; draw(); });
  return h("div", {},
    h("p", { class: "text-xs text-mute mb-2" }, "Advanced: every editable value, grouped the way the save stores it."),
    h("div", { class: "flex flex-wrap items-center gap-1 mb-3" },
      ...tabs.map((t) => h("button", { type: "button", class: "tab-btn" + (t === tab ? " active" : ""), onclick: () => go("advanced", t) },
        t + (counts[t] ? " " + counts[t] : ""))),
      h("div", { class: "ml-auto" }, tab === "Raw" ? null : search)),
    body);
}
