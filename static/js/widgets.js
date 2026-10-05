// Reusable controls. All take a *ref* ({id,value,min,max,kind}) from the view model and write
// through store.setEdit in save units; `offset` lets the UI show game units (e.g. LV = save + 1).
import { h, initials } from "./dom.js";
import { getValue, isChanged, setEdit, valid, onPending } from "./store.js";

// Plain number box.
export function numInput(ref, { offset = 0, cls = "w-28", onchange } = {}) {
  const input = h("input", {
    type: "number", class: "num " + cls, value: getValue(ref) + offset,
    min: ref.min + offset, max: ref.max + offset, step: ref.kind === "float" ? "any" : "1",
  });
  const sync = () => input.classList.toggle("changed", isChanged(ref));
  input.addEventListener("input", () => {
    const v = Number(input.value) - offset;
    const ok = input.value !== "" && valid(ref, v);
    input.classList.toggle("invalid", !ok);
    if (ok) setEdit(ref, v);
    sync();
    if (ok && onchange) onchange(v);
  });
  sync();
  return input;
}

// - [n] + stepper for small integers (stacks, levels).
export function stepper(ref, { offset = 0, onchange } = {}) {
  const val = h("span", {}, String(getValue(ref) + offset));
  const box = h("span", { class: "stepper" });
  const set = (v) => {
    v = Math.min(ref.max, Math.max(ref.min, v));
    setEdit(ref, v);
    val.textContent = String(v + offset);
    box.classList.toggle("changed", isChanged(ref));
    if (onchange) onchange(v);
  };
  box.append(h("button", { type: "button", onclick: () => set(getValue(ref) - 1), "aria-label": "decrease" }, "−"), val,
             h("button", { type: "button", onclick: () => set(getValue(ref) + 1), "aria-label": "increase" }, "+"));
  box.classList.toggle("changed", isChanged(ref));
  return box;
}

// Row of diamond pips (1..max); clicking a pip sets that level.
export function pips(ref, max = 6, { onchange } = {}) {
  const wrap = h("span", { class: "pips", role: "group" });
  const paint = () => {
    const v = getValue(ref);
    [...wrap.children].forEach((p, i) => {
      p.classList.toggle("on", i < v);
      p.classList.toggle("changed", isChanged(ref));
    });
  };
  for (let i = 1; i <= max; i++) {
    wrap.append(h("button", {
      type: "button", class: "pip", title: "Level " + i, "aria-label": "Set level " + i,
      onclick: () => { setEdit(ref, i); paint(); if (onchange) onchange(i); },
    }));
  }
  paint();
  return wrap;
}

// Portrait circle (image, or initials badge when the save has no portrait for this operator).
export function face(op, cls = "face") {
  const box = h("div", { class: cls });
  if (op.portrait) {
    const img = h("img", { src: op.portrait, alt: op.name, loading: "lazy" });
    img.addEventListener("error", () => box.replaceChildren(h("span", { class: "initials" }, initials(op.name))));
    box.append(img);
  } else {
    box.append(h("span", { class: "initials" }, initials(op.name)));
  }
  return box;
}

// Keep a live total (or any derived text) in sync with pending edits without re-rendering.
export function live(el, compute) {
  const update = () => { el.textContent = compute(); };
  update();
  const off = onPending(() => { if (!el.isConnected) { off(); return; } update(); });
  return el;
}
