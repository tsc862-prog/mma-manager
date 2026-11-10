"""Fighter pack review app.

Serves a local page showing every generated fighter (all life stages, face +
body) with Approve / Decline + note per identity. Decisions land in
generated-pack/review.json; regeneration scripts read the declined entries and
feed the notes back into the prompt as reviewer context.

Run:  python tools/reviewapp.py   ->  http://localhost:8765
Zero dependencies (Python stdlib only).
"""
import json
import re
import sys
import time
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
from pathlib import Path

import os
sys.path.insert(0, str(Path(__file__).resolve().parent / "packgen"))
import packmeta
# Pack + port are env-switchable: run one instance per style pack, e.g.
#   MMA_PACK_DIR=...\generated-pack-anime MMA_PACK_PORT=8766 python reviewapp.py
PACK = Path(os.environ.get("MMA_PACK_DIR",
                           Path(__file__).resolve().parent.parent / "generated-pack"))
REVIEW = PACK / "review.json"
PORT = int(os.environ.get("MMA_PACK_PORT", "8765"))

STAGE_ORDER = {"y": 0, "p": 1, "v": 2}
STAGE_NAME = {"y": "Youth", "p": "Prime", "v": "Veteran"}


def _band(w):
    lo = "x" if w.get("min") is None else str(w["min"])
    hi = "x" if w.get("max") is None else str(w["max"])
    return f"{lo}_{hi}"


STYLE_NAMES = {"wr": "Wrestler", "gr": "Grappler", "bjj": "BJJ", "st": "Striker",
               "kb": "Kickboxer", "cl": "Clinch", "br": "Brawler", "wl": "Well-rounded"}


def _specs():
    f = PACK / "specs.json"
    return json.loads(f.read_text(encoding="utf-8-sig")) if f.exists() else {}


def _weight_label(w):
    lo, hi = w.get("min"), w.get("max")
    if lo is None and hi is None:
        return "any weight"
    if lo is None:
        return f"up to {hi} lbs"
    if hi is None:
        return f"{lo}+ lbs"
    return f"{lo}–{hi} lbs"


def identities():
    """Fighters from manifest.json (ALL metadata lives there — filenames are
    opaque). Card key = ident; images are explicit per-stage filenames; meta
    is a human-readable summary of everything known about the fighter."""
    groups = {}
    specs = _specs()
    for ident, e in packmeta.load(PACK)["fighters"].items():
        stages = sorted(e.get("images", {}).keys(),
                        key=lambda s: STAGE_ORDER.get(s, 9))
        look = e.get("look") or ""
        if not look:
            spec = specs.get(ident) or next(
                (v for k, v in specs.items() if k.startswith(ident + "_")), None)
            look = (spec or {}).get("skin", "")
        parts = ["woman" if e["sex"] == "f" else "man",
                 e["eth"],
                 _weight_label(e.get("weight") or {}),
                 STYLE_NAMES.get(e.get("style"), "any style")]
        if look:
            parts.append(look)
        if e.get("cultures"):
            parts.append("cultures: " + ", ".join(e["cultures"]))
        groups[ident] = {
            "stages": stages,
            "raw": False,
            "images": e.get("images", {}),
            "slot": {"sex": e["sex"], "weight": _band(e.get("weight") or {}),
                     "style": e.get("style") or "x"},
            "tags": e.get("tags", []),
            "meta": " · ".join(parts),
        }
    return groups


def override_slot(ident, weight, style):
    """Weight/style correction at approval: a pure manifest edit — no file is
    renamed, the ident never changes."""
    def mut(m):
        f = m["fighters"].get(ident)
        if not f:
            return
        if weight:
            lo, hi = weight.split("_")
            f["weight"] = {"min": None if lo == "x" else int(lo),
                           "max": None if hi == "x" else int(hi)}
        if style:
            f["style"] = None if style == "x" else style
    packmeta.locked_update(PACK, mut)     # locked: can't race the generator


def load_review():
    if REVIEW.exists():
        return json.loads(REVIEW.read_text(encoding="utf-8"))
    return {}


def decline_counts():
    """How many past declines each slot has (its replacement attempt number).
    Log entries may carry legacy slot bases — normalize to idents."""
    log = PACK / "decline-log.jsonl"
    counts = {}
    if log.exists():
        for line in log.read_text(encoding="utf-8").splitlines():
            if line.strip():
                base = json.loads(line).get("base") or ""
                ident = "_".join(base.split("_")[:3])
                counts[ident] = counts.get(ident, 0) + 1
    return counts


def save_review(data):
    REVIEW.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")


PAGE = """<!doctype html>
<html><head><meta charset="utf-8"><title>Fighter Pack Review</title>
<style>
  body { background:#16191E; color:#E8E6E0; font:14px 'Segoe UI',sans-serif; margin:0; }
  header { position:sticky; top:0; background:#101318; padding:10px 18px; border-bottom:2px solid #D9B44A;
           display:flex; gap:18px; align-items:center; z-index:5; }
  h1 { font-size:17px; margin:0; color:#D9B44A; }
  .tab { cursor:pointer; padding:4px 10px; border-radius:6px; background:#22262E; }
  .tab.active { background:#D9B44A; color:#16191E; font-weight:600; }
  main { display:flex; flex-wrap:wrap; gap:14px; padding:16px; }
  .card { background:#1C2027; border:1px solid #2A2F38; border-radius:10px; padding:14px; }
  .card.approved { border-color:#4E9A5E; }
  .card.declined { border-color:#B03A34; }
  .card.retry { border-color:#D9813F; }
  .name { font-weight:600; margin-bottom:2px; }
  .meta { color:#9aa; font-size:12px; margin-bottom:8px; }
  .stages { display:flex; gap:10px; }
  .stage { text-align:center; }
  .stage .lbl { color:#D9B44A; font-size:11px; letter-spacing:1px; margin-bottom:4px; }
  .imgs { display:flex; gap:8px; align-items:flex-end; background:#14171c; border-radius:6px; padding:8px; }
  .imgs img { cursor:zoom-in; }
  .imgs img.face { width:180px; height:180px; object-fit:contain; }
  .imgs img.body { height:380px; object-fit:contain; }
  .imgs img.rawbody { height:420px; object-fit:contain; }
  .rawtag { color:#D9813F; font-size:11px; font-weight:700; letter-spacing:1px; }
  label.db { color:#ccc; display:flex; gap:5px; align-items:center; white-space:nowrap; }
  select.slotsel { background:#14171c; color:#E8E6E0; border:1px solid #2A2F38; border-radius:6px; padding:4px; }
  .slotlbl { color:#9aa; font-size:12px; }
  .row { display:flex; gap:8px; margin-top:10px; align-items:center; }
  button { border:0; border-radius:6px; padding:6px 14px; cursor:pointer; font-weight:600; }
  .ok { background:#4E9A5E; color:#fff; } .no { background:#B03A34; color:#fff; }
  .rt { background:#D9813F; color:#fff; }
  .redo { background:#8A5A2F; color:#fff; font-size:11px; padding:3px 8px; margin-top:4px; }
  .redo.sel { background:#D9813F; }
  .clr { background:#3A3F47; color:#ddd; }
  textarea { flex:1; background:#14171c; color:#E8E6E0; border:1px solid #2A2F38; border-radius:6px;
             padding:6px; min-height:34px; resize:vertical; }
  .badge { font-size:12px; font-weight:700; }
  .tagchip { display:inline-block; background:#3d2f14; color:#FFD98A; border:1px solid #D9B44A;
             border-radius:10px; padding:1px 8px; font-size:11px; font-weight:700;
             letter-spacing:1px; margin-left:6px; vertical-align:middle; }
  .tagx { cursor:pointer; margin-left:5px; color:#E57373; font-weight:900;
          font-size:12px; }
  .tagx:hover { color:#ff9d9d; }
  .tagadd { display:inline-block; cursor:pointer; margin-left:6px; color:#7fb387;
            border:1px dashed #4E9A5E; border-radius:10px; padding:1px 7px;
            font-size:11px; font-weight:900; vertical-align:middle; }
  .tagadd:hover { background:#1e2a20; }
  select.tagpick { background:#14171c; color:#E8E6E0; border:1px solid #4E9A5E;
                   border-radius:6px; padding:2px; margin-left:6px; font-size:11px; }
  .badge.approved { color:#6FCF82; } .badge.declined { color:#E57373; }
  .badge.retry { color:#F0A868; }
</style></head><body>
<header>
  <h1>Fighter Pack Review</h1>
  <span class="tab" data-f="pending">Pending</span>
  <span class="tab" data-f="approved">Approved</span>
  <span class="tab" data-f="declined">Declined</span>
  <span class="tab" data-f="all">All</span>
  <button class="clr" id="refresh" title="Reload fighters from disk">&#8635; Refresh</button>
  <span id="counts" style="color:#9aa"></span>
</header>
<main id="cards"></main>
<script>
let DATA = null;
let FILTER = localStorage.getItem('packFilter') || 'pending';   // survives F5
async function load() {
  DATA = await (await fetch('/data')).json();
  document.querySelectorAll('.tab').forEach(x =>
    x.classList.toggle('active', x.dataset.f === FILTER));
  render();
}
function computeCounts() {
  const counts = {all:0, pending:0, approved:0, declined:0, retry:0};
  for (const base of Object.keys(DATA.identities)) {
    const st = (DATA.review[base] || {}).status || 'pending';
    counts.all++; counts[st] = (counts[st] || 0) + 1;
  }
  return counts;
}
const TAB_LABELS = {pending:'Pending', approved:'Approved', declined:'Declined', all:'All'};
function updateHeader(counts, fresh) {
  document.querySelectorAll('.tab').forEach(t =>
    t.textContent = `${TAB_LABELS[t.dataset.f]} (${counts[t.dataset.f] || 0})`);
  document.getElementById('counts').textContent =
    `queue: ${counts.pending} awaiting review`
    + (counts.retry ? ` · ${counts.retry} in retry` : '')
    + ` · ${counts.approved} approved · ${counts.declined} declined`
    + (fresh ? ` · ${fresh} NEW on disk — hit Refresh` : '');
}
let RENDERED = new Set();   // identities shown by the last render (poll diffs against this)
function render() {
  const cards = document.getElementById('cards'); cards.innerHTML = '';
  RENDERED = new Set(Object.keys(DATA.identities));
  updateHeader(computeCounts(), 0);
  let entries = Object.entries(DATA.identities);
  if (FILTER === 'approved' || FILTER === 'declined') {
    // most recent decision first (entries without a ts predate timestamping
    // and sink to the bottom in their original order)
    entries.sort((a, b) => ((DATA.review[b[0]] || {}).ts || 0) - ((DATA.review[a[0]] || {}).ts || 0));
  }
  for (const [base, info] of entries) {
    const r = DATA.review[base] || {};
    const st = r.status || 'pending';
    if (FILTER !== 'all' && st !== FILTER) continue;
    const card = document.createElement('div');
    card.className = 'card ' + (st !== 'pending' ? st : '');
    let stages;
    if (info.raw) {
      stages = `<div class="stage">
        <div class="lbl">RAW PREVIEW <span class="rawtag">not finalized</span></div>
        <div class="imgs">
          <img class="rawbody" src="/raw/${base}_p.png" loading="lazy"
               onclick="window.open(this.src,'_blank')" title="Click to open full size">
        </div></div>`;
    } else {
      stages = info.stages.map(s => {
        const img = info.images[s] || {};
        return `<div class="stage">
          <div class="lbl">${DATA.stageNames[s] || 'Single'}</div>
          <div class="imgs">
            <img class="face" src="/img/${img.face}" loading="lazy"
                 onclick="window.open(this.src,'_blank')" title="Click to open full size">
            <img class="body" src="/img/${img.body}" loading="lazy"
                 onclick="window.open(this.src,'_blank')" title="Click to open full size">
          </div>
          ${s ? `<button class="redo" data-stage="${s}" title="Queue just this stage image to be regenerated (add a note below)">&#8635; Redo ${DATA.stageNames[s]}</button>` : ''}
          </div>`;
      }).join('');
    }
    const badge = info.raw && st === 'approved' ? 'QUEUED FOR FINALIZE'
                : st !== 'pending' ? st.toUpperCase() : '';
    const nDecl = DATA.declines[base] || 0;
    const replTag = nDecl > 0 && st === 'pending'
      ? ` <span class="rawtag">NEW PERSON — replacement #${nDecl} for this slot</span>` : '';
    const tagChips = (DATA.tags[base] || []).map(t =>
      `<span class="tagchip">★ ${t.toUpperCase()}<span class="tagx" data-tag="${t}"
        title="Remove this tag (image stays, tag doesn't fit)">&times;</span></span>`).join('')
      + `<span class="tagadd" title="Add a tag this image earns">+</span>`;
    card.innerHTML = `
      <div class="name">${base}${tagChips} <span class="badge ${st}">${badge}</span>${replTag}</div>
      <div class="meta">${info.meta || ''}${info.meta ? ' · ' : ''}${info.stages.length} stage(s)</div>
      <div class="stages">${stages}</div>`;
    const STYLES = {wr:'Wrestler', gr:'Grappler', bjj:'BJJ', st:'Striker', kb:'Kickboxer',
                    cl:'Clinch', br:'Brawler', wl:'Well-rounded', x:'WILDCARD (any)'};
    let slotSel = '';
    if (info.slot) {
      // min and max are independent (manifest era) — each with a wildcard
      // division walk-around boundaries — covers every generic/targeted band
      // (x_135, 136_155, 156_185, 186_205, 206_x, 206_265, ...) so each shows
      // as a clean in-order option, not an injected one-off
      const STEPS = ['x','106','115','125','135','136','145','155','156','170','171','185','186','205','206','230','265'];
      const [curLo, curHi] = info.slot.weight.split('_');
      const mkSel = (cur, cls, anyLabel) => {
        const vals = STEPS.includes(cur) ? STEPS : [cur].concat(STEPS);
        return `<select class="slotsel ${cls}">` + vals.map(v =>
          `<option value="${v}"${v===cur?' selected':''}>${v==='x' ? anyLabel : v + ' lbs'}</option>`).join('') + `</select>`;
      };
      const sOpts = Object.entries(STYLES).map(([k,v]) => `<option value="${k}"${k===info.slot.style?' selected':''}>${v}</option>`).join('');
      slotSel = `<span class="slotlbl">min</span> ${mkSel(curLo, 'wmin', 'any (no min)')}
                 <span class="slotlbl">max</span> ${mkSel(curHi, 'wmax', 'any (no max)')}
                 <span class="slotlbl">style</span> <select class="slotsel ssel">${sOpts}</select>`;
    }
    card.insertAdjacentHTML('beforeend', `
      <div class="row">
        <button class="ok">Approve</button>
        ${slotSel}
        ${info.raw ? '<label class="db"><input type="checkbox" class="dbx"' + (r.debrand !== false ? ' checked' : '') + '> clean-up pass (logos / tattoos)</label>' : ''}
        ${info.raw ? '<button class="rt" title="Regenerate this same fighter, applying your note">Try Again</button>' : ''}
        <button class="no" title="Discard and generate a completely new fighter">Decline</button>
        <button class="clr">Clear</button>
        <textarea placeholder="Note — required for Try Again / Redo (what to fix); optional for Decline (new fighter either way)">${r.note || ''}</textarea>
      </div>`);
    const note = card.querySelector('textarea');
    const dbx = card.querySelector('.dbx');
    const wmin = card.querySelector('.wmin'), wmax = card.querySelector('.wmax'),
          ssel = card.querySelector('.ssel');
    card.querySelector('.ok').onclick = () => {
      let weight = null;
      if (wmin && wmax) {
        const lo = wmin.value, hi = wmax.value;
        if (lo !== 'x' && hi !== 'x' && parseInt(lo) > parseInt(hi)) {
          note.placeholder = `min ${lo} is above max ${hi} — fix the weight bounds`;
          wmin.style.borderColor = '#B03A34'; wmax.style.borderColor = '#B03A34';
          return;
        }
        weight = lo + '_' + hi;
      }
      decide(base, 'approved', note.value, dbx ? dbx.checked : false, null,
             weight, ssel ? ssel.value : null);
    };
    const rt = card.querySelector('.rt');
    if (rt) rt.onclick = () => {
      if (!note.value.trim()) { note.focus(); note.placeholder = 'Try Again needs a note: what should change?'; return; }
      decide(base, 'retry', note.value, dbx ? dbx.checked : false);
    };
    card.querySelectorAll('.redo').forEach(b => b.onclick = () => {
      if (!note.value.trim()) { note.focus(); note.placeholder = 'Redo needs a note: what is wrong with this stage?'; return; }
      const stages = (DATA.review[base] && DATA.review[base].stages) || [];
      if (!stages.includes(b.dataset.stage)) stages.push(b.dataset.stage);
      decide(base, 'retry', note.value, false, stages);
      b.classList.add('sel');
    });
    card.querySelectorAll('.tagx').forEach(x => x.onclick = async () => {
      await fetch('/untag', {method:'POST', headers:{'Content-Type':'application/json'},
        body: JSON.stringify({base, tag: x.dataset.tag})});
      DATA.tags[base] = (DATA.tags[base] || []).filter(t => t !== x.dataset.tag);
      if (!DATA.tags[base].length) delete DATA.tags[base];
      render();
    });
    const addBtn = card.querySelector('.tagadd');
    if (addBtn) addBtn.onclick = () => {
      const KNOWN = ['glam','adonis','menace','inked','babyface','unathletic','wildhair'];
      const have = DATA.tags[base] || [];
      const opts = KNOWN.filter(t => !have.includes(t));
      const sel = document.createElement('select');
      sel.className = 'tagpick';
      sel.innerHTML = '<option value="">add tag...</option>'
        + opts.map(t => `<option value="${t}">${t}</option>`).join('')
        + '<option value="__custom__">custom...</option>';
      addBtn.replaceWith(sel);
      sel.focus();
      sel.onchange = async () => {
        let t = sel.value;
        if (t === '__custom__') t = (window.prompt('Custom tag name (lowercase, no spaces):') || '').trim().toLowerCase().replace(/\\s+/g, '-');
        if (t) {
          await fetch('/tag', {method:'POST', headers:{'Content-Type':'application/json'},
            body: JSON.stringify({base, tag: t})});
          DATA.tags[base] = (DATA.tags[base] || []).concat([t]);
        }
        render();
      };
      sel.onblur = () => render();
    };
    card.querySelector('.no').onclick = () => decide(base, 'declined', note.value, false);
    card.querySelector('.clr').onclick = () => decide(base, 'pending', '', false);
    note.onchange = () => { if ((DATA.review[base]||{}).status) decide(base, DATA.review[base].status, note.value, dbx ? dbx.checked : false); };
    cards.appendChild(card);
  }
}
// Live queue counts while a batch generates: poll /data and update ONLY the
// header (re-rendering cards would wipe a note mid-typing). New identities on
// disk get a "hit Refresh" nudge instead of appearing unasked.
setInterval(async () => {
  try {
    DATA = await (await fetch('/data')).json();
    const fresh = Object.keys(DATA.identities).filter(b => !RENDERED.has(b)).length;
    updateHeader(computeCounts(), fresh);
  } catch (e) {}
}, 15000);
async function decide(base, status, note, debrand, stages, weight, style) {
  await fetch('/decide', {method:'POST', headers:{'Content-Type':'application/json'},
    body: JSON.stringify({base, status, note, debrand, stages, weight, style})});
  if (status === 'approved' && (weight || style)) { load(); return; }  // slot may have been renamed
  DATA.review[base] = status === 'pending' ? {} : {status, note, debrand, stages, ts: Date.now() / 1000};
  render();   // on the Pending queue, a decided card disappears immediately
}
document.querySelectorAll('.tab').forEach(t => t.onclick = () => {
  document.querySelectorAll('.tab').forEach(x => x.classList.remove('active'));
  t.classList.add('active'); FILTER = t.dataset.f;
  localStorage.setItem('packFilter', FILTER);
  render();
});
document.getElementById('refresh').onclick = load;
load();
</script></body></html>"""


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def _send(self, code, body, ctype="text/html; charset=utf-8"):
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body if isinstance(body, bytes) else body.encode("utf-8"))

    def do_GET(self):
        if self.path == "/":
            self._send(200, PAGE)
        elif self.path == "/data":
            idents = identities()
            data = {
                "identities": idents,
                "review": load_review(),
                "declines": decline_counts(),
                "stageNames": STAGE_NAME,
                "tags": {k: v["tags"] for k, v in idents.items() if v["tags"]},
            }
            self._send(200, json.dumps(data), "application/json")
        elif self.path.startswith("/img/"):
            name = Path(self.path[5:]).name          # no traversal
            f = PACK / name
            if f.exists():
                self._send(200, f.read_bytes(), "image/png")
            else:
                self._send(404, "not found", "text/plain")
        elif self.path.startswith("/raw/"):
            name = Path(self.path[5:]).name          # no traversal
            f = PACK / "raw" / name
            if f.exists():
                self._send(200, f.read_bytes(), "image/png")
            else:
                self._send(404, "not found", "text/plain")
        else:
            self._send(404, "not found", "text/plain")

    def do_POST(self):
        if self.path in ("/untag", "/tag"):
            # reviewer curates tags by eye: strip one the render doesn't earn,
            # or add one it does (both pure manifest edits)
            n = int(self.headers.get("Content-Length", 0))
            req = json.loads(self.rfile.read(n))
            tag = (req.get("tag") or "").strip()
            add = self.path == "/tag"
            def mut(m):
                f = m["fighters"].get(req.get("base"))
                if f is None or not tag:
                    return
                tags = f.setdefault("tags", [])
                if add and tag not in tags:
                    tags.append(tag)
                elif not add and tag in tags:
                    tags.remove(tag)
            packmeta.locked_update(PACK, mut)     # locked: can't race generator
            self._send(200, "{}", "application/json")
            return
        if self.path != "/decide":
            self._send(404, "not found", "text/plain")
            return
        n = int(self.headers.get("Content-Length", 0))
        req = json.loads(self.rfile.read(n))
        review = load_review()
        base = req["base"]
        if req.get("status") in (None, "", "pending"):
            review.pop(base, None)
        else:
            # weight/style override on approval -> manifest edit, no renames
            if req["status"] == "approved" and (req.get("weight") or req.get("style")):
                override_slot(base, req.get("weight"), req.get("style"))
            entry = {"status": req["status"], "note": req.get("note", ""),
                     "debrand": bool(req.get("debrand")),
                     "ts": time.time()}       # decision time (drives "newest first")
            if req.get("stages"):
                entry["stages"] = req["stages"]      # per-stage redo (retry)
            review[base] = entry
        save_review(review)
        self._send(200, "{}", "application/json")


if __name__ == "__main__":
    print(f"Fighter pack review -> http://localhost:{PORT}  (pack: {PACK})")
    # Threading: image transfers must not block decide() clicks (single-threaded
    # server made Approve/Decline feel dead while big body PNGs streamed).
    ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
