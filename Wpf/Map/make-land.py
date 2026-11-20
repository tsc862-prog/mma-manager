"""Natural Earth 110m admin-0 countries (public domain) -> compact text for the World Map.

Line per country: CODE;REGION;Name;ring|ring|...  ring = "lon,lat lon,lat ..." in tenths of a degree.
Antarctica dropped. Consecutive duplicate points (after rounding) dropped.

Regenerate (not part of the build; the output is checked in):
  download https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_110m_admin_0_countries.geojson
  python -I make-land.py ne_110m_admin_0_countries.geojson world-countries-110m.txt
"""
import json, sys

src, dst = sys.argv[1], sys.argv[2]
data = json.load(open(src, encoding="utf-8"))

def region(p):
    cont, sub = p.get("CONTINENT"), p.get("SUBREGION")
    if cont == "North America": return "NA"
    if cont == "South America": return "SA"
    if cont == "Europe": return "EU"
    if cont == "Africa": return "AF"
    if cont == "Oceania": return "OC"
    if cont == "Asia": return "ME" if sub == "Western Asia" else "AS"
    return None

lines, pts = [], 0
for f in data["features"]:
    p = f["properties"]
    code = p["ADM0_A3"]
    if code == "ATA": continue
    reg = region(p)
    if reg is None: continue
    g = f["geometry"]
    polys = g["coordinates"] if g["type"] == "MultiPolygon" else [g["coordinates"]]
    rings = []
    for poly in polys:
        for ring in poly[:1]:   # outer rings only (no 110m country has a hole worth drawing)
            out, last = [], None
            for lon, lat in ring:
                q = (round(lon * 10), round(lat * 10))
                if q != last: out.append(q); last = q
            if len(out) >= 4:
                rings.append(" ".join(f"{x},{y}" for x, y in out)); pts += len(out)
    name = p["NAME"].replace(";", ",")
    lines.append(f"{code};{reg};{name};{'|'.join(rings)}")

header = ("# World land for the World Map: Natural Earth 1:110m Cultural Vectors, Admin 0 - Countries (v5).\n"
          "# Source: https://www.naturalearthdata.com (via github.com/nvkelso/natural-earth-vector, geojson/ne_110m_admin_0_countries.geojson).\n"
          "# Natural Earth is in the public domain: \"All versions of Natural Earth raster + vector map data found on this\n"
          "# website are in the public domain.\" (naturalearthdata.com/about/terms-of-use). Antarctica removed; outer rings only;\n"
          "# coordinates rounded to 0.1 degree. Format: ADM0_A3;REGION;Name;ring|ring  ring = 'lon,lat ...' in tenths of a degree.\n")
open(dst, "w", encoding="utf-8", newline="\n").write(header + "\n".join(lines) + "\n")
print(len(lines), "countries", pts, "points")
