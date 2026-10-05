// DOM helpers. Everything is built with textContent only: save data is untrusted.
export function h(tag, attrs, ...kids) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (k === "class") el.className = v;
    else if (k === "style" && typeof v === "object") Object.assign(el.style, v);
    else if (k.startsWith("on")) el.addEventListener(k.slice(2), v);
    else if (v !== false && v != null) el.setAttribute(k, v === true ? "" : v);
  }
  for (const kid of kids.flat(Infinity)) if (kid != null && kid !== false) el.append(kid.nodeType ? kid : document.createTextNode(kid));
  return el;
}
export const $ = (id) => document.getElementById(id);
export const fmtNum = (n) => Number(n).toLocaleString();
export const fmtTime = (t) => new Date(t * 1000).toLocaleString();
export const fmtSize = (n) => (n / 1048576 >= 1 ? (n / 1048576).toFixed(1) + " MB" : Math.ceil(n / 1024) + " KB");
export const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
export const initials = (name) => {
  const words = name.split(/[\s-]+/).filter((w) => /^[A-Za-z]/.test(w));
  if (words.length >= 2) return (words[0][0] + words[1][0]).toUpperCase();
  return (words[0] || name).slice(0, 2).toUpperCase();
};
export const BTN = "btn";
