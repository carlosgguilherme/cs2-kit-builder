"""Generate color-index.json for every skin in the ByMykel/CSGO-API catalog.

Usage: python build_color_index.py --out out/color-index.json [--cache .cache]
"""
import argparse, datetime, json, os, sys, time, urllib.request
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kitbuilder_pipeline.palette import extract  # noqa: E402

CATALOG_URL = "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/en/skins.json"
UA = {"User-Agent": "kitbuilder-data-pipeline"}


def fetch(url, timeout=60):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=timeout) as r:
        return r.read()


def download(args):
    skin, cache = args
    path = os.path.join(cache, skin["id"] + ".png")
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return path
    for attempt in range(4):
        try:
            data = fetch(skin["image"] + "/256fx256f")
            with open(path + ".tmp", "wb") as f:
                f.write(data)
            os.replace(path + ".tmp", path)
            return path
        except Exception:
            time.sleep(2 ** attempt)
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--cache", default=".cache/images")
    ap.add_argument("--catalog", default=CATALOG_URL, help="URL or local path of skins.json")
    ap.add_argument("--limit", type=int, default=0, help="process only N skins (testing)")
    a = ap.parse_args()
    os.makedirs(a.cache, exist_ok=True)
    raw = open(a.catalog, "rb").read() if os.path.exists(a.catalog) else fetch(a.catalog, 300)
    skins = [s for s in json.loads(raw) if s.get("image")]
    if a.limit:
        skins = skins[: a.limit]
    with ThreadPoolExecutor(8) as ex:
        paths = list(ex.map(download, [(s, a.cache) for s in skins]))
    entries, failed = [], []
    for s, p in zip(skins, paths):
        res = extract(p, s["name"]) if p else None
        if res is None:
            failed.append(s["id"])
            continue
        entries.append({"skinId": s["id"], "name": s["name"], "phase": s.get("phase"), **res})
    entries.sort(key=lambda e: e["skinId"])
    doc = {"schemaVersion": 1,
           "generatedAt": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
           "catalogSource": "ByMykel/CSGO-API skins.json", "colorSpace": "OKLab",
           "count": len(entries), "skins": entries}
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, separators=(",", ":"))
    print(f"indexed={len(entries)} failed={len(failed)} {failed[:10]}")
    # Fail the job if a large part of the catalog could not be processed.
    if len(failed) > max(20, len(skins) * 0.02):
        sys.exit(1)


if __name__ == "__main__":
    main()
