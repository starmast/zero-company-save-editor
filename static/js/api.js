export async function api(path, body) {
  const r = await fetch(path, body === undefined ? {} : {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  const j = await r.json().catch(() => ({ error: "bad response" }));
  if (!r.ok) throw new Error(j.error || r.statusText);
  return j;
}
