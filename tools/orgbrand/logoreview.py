"""Org-logo review app (stdlib only).

Serves every generated logo with its org name / region / layout / motif, on a
neutral tile (logos are transparent). Approve / Decline+note / Re-roll per logo;
decisions land in logos/review.json. logogen.py reads it: DECLINED and RE-ROLL
logos are regenerated (fresh seed) on the next run; APPROVED are kept.

Run:  python tools/orgbrand/logoreview.py   ->  http://localhost:8771
"""
import json
import os
import time
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
from pathlib import Path

# LOGO_KIND=team reviews the team-logos bucket on its own port; default = orgs.
_KIND = os.environ.get("LOGO_KIND", "org")
_SUB = "team-logos" if _KIND == "team" else "logos"
LOGOS = Path(__file__).resolve().parent / _SUB
MANIFEST = LOGOS / "manifest.json"
REVIEW = LOGOS / "review.json"
PORT = 8772 if _KIND == "team" else 8771


def load(p, default):
    return json.loads(p.read_text(encoding="utf-8-sig")) if p.exists() else default


def save_review(d):
    REVIEW.write_text(json.dumps(d, indent=2, ensure_ascii=False), encoding="utf-8")


PAGE = """<!doctype html><html><head><meta charset="utf-8"><title>__KIND__ Logo Review</title>
<style>
 body{background:#16191E;color:#E8E6E0;font:14px 'Segoe UI',sans-serif;margin:0;}
 header{position:sticky;top:0;background:#101318;padding:10px 18px;border-bottom:2px solid #D9B44A;
        display:flex;gap:14px;align-items:center;z-index:5;}
 h1{font-size:17px;margin:0;color:#D9B44A;}
 .tab{cursor:pointer;padding:4px 10px;border-radius:6px;background:#22262E;}
 .tab.active{background:#D9B44A;color:#16191E;font-weight:600;}
 main{display:flex;flex-wrap:wrap;gap:14px;padding:16px;}
 .card{background:#1C2027;border:1px solid #2A2F38;border-radius:10px;padding:12px;width:250px;}
 .card.approved{border-color:#4E9A5E;} .card.declined{border-color:#B03A34;} .card.reroll{border-color:#D9813F;}
 .logo{height:190px;border-radius:8px;display:flex;align-items:center;justify-content:center;
       background:repeating-conic-gradient(#3a3f47 0% 25%,#2b2f36 0% 50%) 50%/22px 22px;}
 .logo img{max-height:180px;max-width:230px;}
 .nm{font-weight:600;margin:8px 0 1px;} .meta{color:#9aa;font-size:12px;margin-bottom:8px;}
 .tag{display:inline-block;background:#22262e;border-radius:8px;padding:0 7px;margin-right:4px;font-size:11px;color:#bcd;}
 .row{display:flex;gap:6px;margin-top:8px;}
 button{border:0;border-radius:6px;padding:6px 10px;cursor:pointer;font-weight:600;font-size:12px;}
 .ok{background:#4E9A5E;color:#fff;} .no{background:#B03A34;color:#fff;} .rr{background:#D9813F;color:#fff;} .clr{background:#3A3F47;color:#ddd;}
 textarea{width:100%;box-sizing:border-box;margin-top:6px;background:#14171c;color:#E8E6E0;border:1px solid #2A2F38;
          border-radius:6px;padding:5px;min-height:30px;resize:vertical;font:12px 'Segoe UI';}
 .badge{font-size:11px;font-weight:700;} .approved .badge{color:#6FCF82;} .declined .badge{color:#E57373;} .reroll .badge{color:#F0A868;}
</style></head><body>
<header><h1>__KIND__ Logo Review</h1>
 <span class="tab" data-f="pending">Pending</span><span class="tab" data-f="approved">Approved</span>
 <span class="tab" data-f="declined">Declined</span><span class="tab" data-f="reroll">Re-roll</span>
 <span class="tab" data-f="all">All</span>
 <button class="clr" id="refresh">&#8635; Refresh</button><span id="counts" style="color:#9aa"></span>
</header><main id="cards"></main>
<script>
let DATA=null, FILTER=localStorage.getItem('logoFilter')||'pending';
async function load(){DATA=await (await fetch('/data')).json();
 document.querySelectorAll('.tab').forEach(x=>x.classList.toggle('active',x.dataset.f===FILTER));render();}
function render(){const c=document.getElementById('cards');c.innerHTML='';
 let n={all:0,pending:0,approved:0,declined:0,reroll:0};
 for(const L of DATA.logos){const st=(DATA.review[L.ab]||{}).status||'pending';n.all++;n[st]=(n[st]||0)+1;
  if(FILTER!=='all'&&st!==FILTER)continue;
  const r=DATA.review[L.ab]||{};const card=document.createElement('div');card.className='card '+(st!=='pending'?st:'');
  card.innerHTML=`<div class="logo"><img src="/img/${L.file}?t=${Date.now()}"></div>
   <div class="nm">${L.name} <span class="badge">${st!=='pending'?st.toUpperCase():''}</span></div>
   <div class="meta">${L.ab} · ${L.region}</div>
   <div><span class="tag">${L.layout}</span><span class="tag">${L.motif||''}</span></div>
   <div class="row"><button class="ok">Approve</button><button class="rr">Re-roll</button>
    <button class="no">Decline</button><button class="clr">Clear</button></div>
   <textarea placeholder="note (optional)">${r.note||''}</textarea>`;
  const note=card.querySelector('textarea');
  card.querySelector('.ok').onclick=()=>decide(L.ab,'approved',note.value);
  card.querySelector('.rr').onclick=()=>decide(L.ab,'reroll',note.value);
  card.querySelector('.no').onclick=()=>decide(L.ab,'declined',note.value);
  card.querySelector('.clr').onclick=()=>decide(L.ab,'pending','');
  c.appendChild(card);}
 document.getElementById('counts').textContent=`${n.pending} pending · ${n.approved} approved · ${n.declined} declined · ${n.reroll} re-roll`;}
async function decide(ab,status,note){await fetch('/decide',{method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({ab,status,note})});DATA.review[ab]=status==='pending'?{}:{status,note};render();}
document.querySelectorAll('.tab').forEach(t=>t.onclick=()=>{FILTER=t.dataset.f;localStorage.setItem('logoFilter',FILTER);
 document.querySelectorAll('.tab').forEach(x=>x.classList.remove('active'));t.classList.add('active');render();});
document.getElementById('refresh').onclick=load;load();
</script></body></html>"""


class H(BaseHTTPRequestHandler):
    def log_message(self, *a): pass

    def _send(self, code, body, ctype="text/html; charset=utf-8"):
        self.send_response(code); self.send_header("Content-Type", ctype)
        self.send_header("Cache-Control", "no-store"); self.end_headers()
        self.wfile.write(body if isinstance(body, bytes) else body.encode("utf-8"))

    def do_GET(self):
        if self.path == "/":
            self._send(200, PAGE.replace("__KIND__", "Team" if _KIND == "team" else "Org"))
        elif self.path == "/data":
            man = load(MANIFEST, {})
            logos = [{"ab": ab, **info} for ab, info in
                     sorted(man.items(), key=lambda kv: (kv[1]["region"], kv[0]))]
            self._send(200, json.dumps({"logos": logos, "review": load(REVIEW, {})}),
                       "application/json")
        elif self.path.startswith("/img/"):
            f = LOGOS / Path(self.path[5:].split("?")[0]).name
            self._send(200, f.read_bytes(), "image/png") if f.exists() else self._send(404, "no", "text/plain")
        else:
            self._send(404, "no", "text/plain")

    def do_POST(self):
        if self.path != "/decide":
            self._send(404, "no", "text/plain"); return
        n = int(self.headers.get("Content-Length", 0))
        req = json.loads(self.rfile.read(n))
        review = load(REVIEW, {})
        if req.get("status") in (None, "", "pending"):
            review.pop(req["ab"], None)
        else:
            review[req["ab"]] = {"status": req["status"], "note": req.get("note", ""), "ts": time.time()}
        save_review(review)
        self._send(200, "{}", "application/json")


if __name__ == "__main__":
    print(f"{_KIND} logo review -> http://localhost:{PORT}  ({LOGOS})")
    ThreadingHTTPServer(("127.0.0.1", PORT), H).serve_forever()
