// Single source of truth: the open save's state plus all pending (unapplied) changes.
// Two notification channels so typing never rebuilds the screen under the cursor:
//   onState  - the state or route changed -> re-render the current screen
//   onPending - a pending edit changed -> light updates only (action bar, header marks)
import { api } from "./api.js";

export const S = {
  saves: null,          // {dirs, saves}
  state: null,          // /api/open|state|apply response for the open save
  edits: new Map(),     // field id -> new value
  refs: new Map(),      // field id -> ref {id,value,min,max,kind} seen by the UI (for labels / revert)
  nolink: new Set(),    // edit ids whose "total follows unspent" link is switched off
  starts: new Set(),    // upgrade ids to start
  expedites: new Set(), // upgrade ids to expedite
  heals: new Set(),     // injured operator guids to heal
  rosterOrder: null,    // pending roster order (array of guids) or null
  coilChanges: new Map(), // Coil upgrade id -> "Available" | "Prevented" (take it away)
  alsoExpedite: false,
  banner: null,         // {text, kind}
  busy: false,
};

const stateSubs = new Set(), pendingSubs = new Set();
export const onState = (fn) => (stateSubs.add(fn), () => stateSubs.delete(fn));
export const onPending = (fn) => (pendingSubs.add(fn), () => pendingSubs.delete(fn));
export const emitState = () => stateSubs.forEach((f) => f());
export const emitPending = () => pendingSubs.forEach((f) => f());

export const pendingCount = () => S.edits.size + S.starts.size + S.expedites.size + S.heals.size + S.coilChanges.size + (S.rosterOrder ? 1 : 0);
export const getValue = (ref) => (S.edits.has(ref.id) ? S.edits.get(ref.id) : ref.value);
export const isChanged = (ref) => S.edits.has(ref.id);

export function setBanner(text, kind = "info") { S.banner = text ? { text, kind } : null; emitState(); }

// Record an edit in *save* units. Equal-to-original removes the pending edit.
export function setEdit(ref, value, { link = true } = {}) {
  S.refs.set(ref.id, ref);
  if (value === ref.value) { S.edits.delete(ref.id); S.nolink.delete(ref.id); }
  else {
    S.edits.set(ref.id, value);
    if (link) S.nolink.delete(ref.id); else S.nolink.add(ref.id);
  }
  emitPending();
}
export function valid(ref, value) {
  return Number.isFinite(value) && value >= ref.min && value <= ref.max && (ref.kind === "float" || Number.isInteger(value));
}

export function discard() {
  S.edits.clear(); S.nolink.clear(); S.starts.clear(); S.expedites.clear(); S.heals.clear(); S.coilChanges.clear(); S.rosterOrder = null;
  emitState();
}

export function toggle(set, id, on) {
  if (on === undefined ? !set.has(id) : on) set.add(id); else set.delete(id);
  emitPending();
}

// ----------------------------------------------------------------- data flows
// Roster order as shown (pending if the user moved someone), and moving one operator a place earlier/later.
export const rosterOrder = () => S.rosterOrder || S.state.view.personnel.roster.map((o) => o.guid);
function setRosterOrder(order) {
  const saved = S.state.view.personnel.roster.map((o) => o.guid);
  S.rosterOrder = order.every((g, k) => g === saved[k]) ? null : order;
  emitState();
}
export function moveOperator(guid, delta) {
  const order = rosterOrder().slice();
  const i = order.indexOf(guid), j = i + delta;
  if (i < 0 || j < 0 || j >= order.length) return;
  [order[i], order[j]] = [order[j], order[i]];
  setRosterOrder(order);
}
// Drop `guid` so that `index` other operators come before it.
export function moveOperatorTo(guid, index) {
  const order = rosterOrder().filter((g) => g !== guid);
  if (!rosterOrder().includes(guid)) return;
  order.splice(Math.max(0, Math.min(index, order.length)), 0, guid);
  setRosterOrder(order);
}

export async function loadSaves() {
  S.saves = await api("/api/saves");
  emitState();
}

function adopt(state) {
  S.state = state;
  S.edits.clear(); S.nolink.clear(); S.starts.clear(); S.expedites.clear(); S.heals.clear(); S.coilChanges.clear(); S.rosterOrder = null; S.refs.clear();
  S.fieldIndex = new Map(state.fields.map((f) => [f.id, f]));
}

export async function openSave(dir, name) {
  S.busy = true;
  try {
    adopt(await api("/api/open", { dir, name }));
    S.banner = S.state.game_running
      ? { text: "The game appears to be running (" + S.state.game_running + "). Close it before applying, or the game may overwrite your edit.", kind: "err" }
      : null;
  } catch (e) { S.banner = { text: e.message, kind: "err" }; }
  S.busy = false;
  emitState();
}

export async function refresh() {
  adopt(await api("/api/state"));
  emitState();
}

export function actionList() {
  const mk = (type) => (id) => ({ type, id, name: S.state.upgrades.items.find((x) => x.id === id).name });
  return [...S.starts].map(mk(S.alsoExpedite ? "start_expedite_upgrade" : "start_upgrade"))
    .concat([...S.expedites].map(mk("expedite_upgrade")))
    .concat([...S.heals].map((guid) => ({ type: "heal_operator", guid })))
    .concat(S.rosterOrder ? [{ type: "reorder_roster", order: S.rosterOrder }] : [])
    .concat(S.coilChanges.size ? [{ type: "remove_coil_upgrades", changes: [...S.coilChanges].map(([id, to]) => ({ id, to })) }] : []);
}

export async function submit(copy, force) {
  const changes = [...S.edits].map(([id, value]) => (S.nolink.has(id) ? { id, value, link: false } : { id, value }));
  const actions = actionList();
  if (!changes.length && !actions.length) return false;
  S.busy = true; emitPending();
  try {
    const r = await api("/api/apply", { changes, actions, copy, force });
    if (copy) {
      S.banner = { text: "Saved a copy: " + r.written, kind: "ok" };
      S.edits.clear(); S.nolink.clear(); S.starts.clear(); S.expedites.clear(); S.heals.clear(); S.coilChanges.clear(); S.rosterOrder = null;
    } else {
      adopt(r.state);
      S.banner = { text: "Applied " + r.count + " change(s). Backup: " + r.backup, kind: "ok" };
    }
    S.busy = false; emitState();
    return true;
  } catch (e) {
    S.banner = { text: e.message, kind: "err" };
    S.busy = false; emitState();
    return false;
  }
}

export async function restore(file, force) {
  try {
    const r = await api("/api/restore", { file, force });
    adopt(r.state);
    S.banner = { text: "Restored " + r.restored + ". Previous version kept as " + r.previous_saved_as, kind: "ok" };
  } catch (e) { S.banner = { text: e.message, kind: "err" }; }
  emitState();
}

// Human description of a pending edit for the "what will change" list.
export function describeEdit(id) {
  const f = S.fieldIndex && S.fieldIndex.get(id);
  return f ? { where: f.section || f.group, label: f.label, from: f.value, to: S.edits.get(id) }
           : { where: "Raw", label: id, from: null, to: S.edits.get(id) };
}
