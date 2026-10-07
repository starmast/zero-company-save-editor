// Command: campaign overview, progression, save info, backups/restore (the "hub" screen).
import { h, fmtTime, fmtSize } from "../dom.js";
import { S, restore } from "../store.js";
import { numInput } from "../widgets.js";

function row(label, control, note) {
  return h("div", { class: "flex items-center gap-3 px-3 py-2 border-b hair" },
    h("div", { class: "flex-1 min-w-0" }, h("div", {}, label), note ? h("div", { class: "text-xs text-mute" }, note) : null), control);
}

export function render() {
  const v = S.state.view, c = v.command, hd = v.header;
  const grid = h("div", { class: "grid gap-3 lg:grid-cols-2" });

  const info = c.save;
  grid.append(h("section", { class: "panel" }, h("div", { class: "panel-h" }, "Save"),
    h("div", { class: "p-3 text-sm space-y-1" },
      h("div", { class: "font-display text-2xl text-cream" }, info.title || S.state.name),
      h("div", { class: "text-mute" }, S.state.name + " (" + S.state.dir + ")"),
      h("div", {}, [info.world, info.mode, info.type === "AutoSave" ? "Autosave" : "Manual save"].filter(Boolean).join(" · ")),
      h("div", { class: "text-mute" }, "Difficulty level " + (info.difficulty ?? "?") + (info.permadeath ? " · Permadeath on" : "")),
      h("div", { class: "text-mute" }, "Created " + (info.created || "?")),
      h("div", { class: "text-xs text-mute pt-2" }, S.state.view.personnel.roster.length + " operators on the roster · " +
        S.state.view.personnel.memorial.length + " in the Memorial · " +
        S.state.upgrades.items.filter((u) => u.status === "Completed").length + " base upgrades completed."))));

  const p = c.progression;
  const prog = h("section", { class: "panel" }, h("div", { class: "panel-h" }, "Campaign"));
  if (p.turn) prog.append(row("Strategy turn", numInput(p.turn, { cls: "w-24" }), "The save's info file keeps its own copy, which is not updated."));
  if (p.roster_level) prog.append(row("Roster level (LV)", numInput(p.roster_level, { offset: 1, cls: "w-24" }), "Shown as LV in the game; Den Level is the same scale."));
  if (p.roster_xp) prog.append(row("Roster XP", numInput(p.roster_xp, { cls: "w-28" })));
  if (p.base_focus) prog.append(row("Base total focus points", numInput(p.base_focus, { cls: "w-24" }), "Raised by the Crew Focus upgrades."));
  grid.append(prog);

  grid.append(h("section", { class: "panel lg:col-span-2" }, h("div", { class: "panel-h" }, "Stockpile"),
    ...hd.resources.map((r) => row(r.label, numInput(r, { cls: "w-36" }), r.description))));

  const bk = h("section", { class: "panel lg:col-span-2" }, h("div", { class: "panel-h" }, "Backups"),
    h("p", { class: "px-3 py-2 text-xs text-mute" },
      "A backup is made before every write and the very first version is kept as the original. Restoring first saves the current file as a new backup, so nothing is lost."));
  for (const b of S.state.backups) {
    bk.append(h("div", { class: "flex items-center gap-3 px-3 py-2 border-t hair text-sm" },
      h("div", { class: "flex-1 min-w-0" }, h("span", {}, b.file),
        b.original ? h("span", { class: "ml-2 text-xs text-ok caps" }, "original") : null,
        h("div", { class: "text-xs text-mute" }, fmtTime(b.mtime) + " · " + fmtSize(b.size))),
      h("button", { type: "button", class: "btn",
        onclick: () => {
          if (!confirm("Restore " + b.file + " over the current save?")) return;
          let force = !!document.getElementById("force")?.checked;
          if (!force && S.state.game_running &&
              !confirm("The game appears to be running (" + S.state.game_running + "). If it has this save loaded it may overwrite the restore. Restore anyway?")) return;
          restore(b.file, force || !!S.state.game_running);
        } }, "Restore")));
  }
  grid.append(bk);
  return grid;
}
