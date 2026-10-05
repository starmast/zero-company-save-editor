// Save picker: cards with the save's own screenshot, in-game name and key facts.
import { h, fmtTime, fmtSize } from "../dom.js";
import { S, openSave } from "../store.js";
import { go } from "../shell.js";

const DIR_NAMES = { game: "Game folder", project: "Project copy" };

function card(dir, s) {
  const img = h("img", { alt: "", loading: "lazy", src: `/api/thumb/${encodeURIComponent(dir)}/${encodeURIComponent(s.name)}` });
  img.addEventListener("error", () => img.replaceWith(h("div", { class: "aspect-video bg-deck flex items-center justify-center text-mute caps" }, "No screenshot")));
  const kind = s.autosave && s.autosave !== "None" ? s.autosave + " autosave" : "Manual save";
  const open = async () => { await openSave(dir, s.name); go("command"); };
  return h("button", { type: "button", class: "panel save-card text-left overflow-hidden block", onclick: open, "aria-label": "Open " + (s.title || s.name) },
    img,
    h("div", { class: "p-3" },
      h("div", { class: "font-display text-lg text-cream truncate" }, s.title || s.name),
      h("div", { class: "text-xs text-glow caps" }, [kind, s.turn != null ? "Turn " + s.turn : null].filter(Boolean).join(" · ")),
      h("div", { class: "text-[11px] text-mute truncate mt-1" }, s.name),
      h("div", { class: "text-[11px] text-mute" }, fmtTime(s.mtime) + " · " + fmtSize(s.size))));
}

export function render() {
  const root = h("div", {});
  if (!S.saves) return h("div", { class: "text-mute p-6" }, "Loading saves…");
  root.append(h("p", { class: "text-sm text-mute mb-3" },
    "Pick a save to open. Every write is backed up first. Tip: try the project copy before your real save."));
  const { dirs, saves } = S.saves;
  for (const [id, path] of Object.entries(dirs)) {
    const rows = saves.filter((s) => s.dir === id);
    const editable = rows.filter((s) => s.editable), other = rows.filter((s) => !s.editable);
    root.append(h("h2", { class: "font-display text-xl text-amber mt-5 mb-2" }, DIR_NAMES[id] || id,
      h("span", { class: "ml-3 text-xs text-mute normal-case tracking-normal font-body" }, path)));
    const grid = h("div", { class: "grid gap-3", style: { gridTemplateColumns: "repeat(auto-fill,minmax(230px,1fr))" } });
    editable.forEach((s) => grid.append(card(id, s)));
    root.append(editable.length ? grid : h("p", { class: "text-sm text-mute" }, "No saves here."));
    if (other.length) root.append(h("p", { class: "text-xs text-mute mt-2" },
      "Not editable here: " + other.map((s) => s.name).join(", ")));
  }
  return root;
}
