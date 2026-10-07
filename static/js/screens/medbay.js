// Medbay: beds, the bacta tank, who is injured (read-only; treat operators in the game).
import { h, fmtNum } from "../dom.js";
import { S } from "../store.js";
import { face } from "../widgets.js";
import { go } from "../shell.js";

function slotIcon(kind, busy) {
  return h("div", {
    class: "w-14 h-14 flex items-center justify-center border-2 rounded-lg font-display text-xl " +
      (busy ? "border-amber text-amber" : "border-edge text-glow"),
    title: kind === "tank" ? "Bacta tank" : "Medical bed", "aria-label": kind === "tank" ? "Bacta tank" : "Medical bed",
  }, kind === "tank" ? "T" : "B");
}

function treatCard(title, tag, lines, cost) {
  return h("section", { class: "panel" },
    h("div", { class: "panel-h flex items-center" }, title, h("span", { class: "ml-auto text-xs text-glow normal-case tracking-normal font-body" }, tag)),
    h("div", { class: "p-3 text-sm space-y-2" }, ...lines.map((t) => h("p", {}, t)),
      h("p", { class: "text-glow font-display text-lg tracking-wide" }, cost)));
}

export function render() {
  const mb = S.state.view.medbay;
  const busyBeds = mb.treating.filter((t) => t.slot.startsWith("Bed")).length;
  const tankBusy = mb.treating.some((t) => t.slot === "Bacta tank");

  const slots = h("section", { class: "panel" }, h("div", { class: "panel-h" }, "Slots"),
    h("div", { class: "p-3 flex flex-wrap gap-2 items-center" },
      ...Array.from({ length: mb.beds }, (_, i) => slotIcon("bed", i < busyBeds)),
      mb.tanks ? h("span", { class: "w-px h-10 bg-edge mx-2" }) : null,
      ...Array.from({ length: mb.tanks }, () => slotIcon("tank", tankBusy))),
    h("p", { class: "px-3 pb-3 text-xs text-mute" },
      mb.beds + (mb.beds === 1 ? " bed" : " beds") + " and " + mb.tanks + (mb.tanks === 1 ? " bacta tank" : " bacta tanks") +
      ". More beds come from the Medical Bed upgrades."));

  const injured = h("section", { class: "panel" }, h("div", { class: "panel-h" }, "Injured"));
  if (!mb.injured.length) injured.append(h("p", { class: "p-4 text-sm text-mute" }, "Nobody on the roster is injured."));
  for (const o of mb.injured) {
    injured.append(h("button", { type: "button", onclick: () => go("personnel", o.guid, "overview"),
      class: "w-full text-left flex items-center gap-3 px-3 py-2 border-b hair hover:bg-deck" },
      face(o, "face w-14 h-14 rounded-full overflow-hidden border-2 border-edge flex items-center justify-center bg-deck"),
      h("div", {}, h("div", { class: "font-display text-2xl text-cream leading-none" }, o.name),
        h("div", { class: "text-bad caps text-sm" }, o.injuries + (o.injuries === 1 ? " injury" : " injuries")))));
  }

  const treating = mb.treating.length ? h("section", { class: "panel" }, h("div", { class: "panel-h" }, "In treatment"),
    ...mb.treating.map((t) => h("div", { class: "flex items-center gap-3 px-3 py-2 border-b hair text-sm" },
      h("span", { class: "text-amber caps font-display w-24" }, t.slot),
      h("span", { class: "flex-1" }, t.operators.join(", ") || "-"),
      t.started != null && t.started >= 0 ? h("span", { class: "text-mute" }, "since turn " + t.started) : null))) : null;

  return h("div", { class: "space-y-3" },
    h("div", { class: "grid gap-3 lg:grid-cols-[1fr,1fr]" },
      h("div", { class: "space-y-3" }, slots, injured, treating),
      h("div", { class: "space-y-3" },
        treatCard("Medical bed", "Takes a cycle",
          ["Heals all injuries on one operator. Uses a bed slot and one cycle; the operator can't go on missions or operations that cycle."],
          "1 bed · " + fmtNum(mb.cost.bed) + " credits"),
        treatCard("Bacta tank", "Instant",
          ["Instantly heals all injuries on one operator, using this cycle's bacta tank charge."],
          "1 charge · " + fmtNum(mb.cost.tank) + " credits"))),
    h("p", { class: "text-xs text-mute" },
      "This screen shows what your save says. Treating operators isn't available in the editor yet (it needs a closer look at how the game records a heal); use the Medbay in the game."));
}
