#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
One-off importer: enrich Assets/StreamingAssets/PoemData/semantic_categories.json
with vocabulary drawn from 声律启蒙, for the 逐字填空 distractor optimization (Part A of
plan 234-2). Richer per-category char pools => WordClozeGenerator's "同类同平仄" distractor
layer (_catToneChars) has more, better confusers.

How it stays honest & data-linked:
  - Candidate members per category are curated from the 声律启蒙 / 对韵 tradition (below).
  - Each candidate is KEPT only if it actually occurs in 声律启蒙 (after 繁->简 via TC2SC).
    Candidates not attested there are dropped and reported.
  - Chars already present in ANY existing category are skipped (keep one char -> one category).
  - Two new categories 天文 / 地理 (声律启蒙's biggest 门类 not yet represented) are added.
    Remember to also list them in WordClozeGenerator.CategoryPriority.

Sources (jsDelivr CDN of chinese-poetry / LingDong-cope):
  蒙学/shenglvqimeng.json   data/TC2SC.json

This script is NOT part of the Unity/C# build; run it by hand to refresh categories.

Usage:
  python build_categories.py                         # fetch sources from CDN
  python build_categories.py shenglvqimeng.json TC2SC.json   # use local copies
"""
import json, os, sys, urllib.request

SL_CDN = "https://cdn.jsdelivr.net/gh/chinese-poetry/chinese-poetry@master/%E8%92%99%E5%AD%A6/shenglvqimeng.json"
TC_CDN = "https://cdn.jsdelivr.net/gh/LingDong-/cope@master/data/TC2SC.json"
OUT = os.path.normpath(os.path.join(
    os.path.dirname(__file__), "..", "..",
    "Assets", "StreamingAssets", "PoemData", "semantic_categories.json"))

# Curated candidate members per category, in the 声律启蒙 / 对韵 tradition.
# (Final additions = candidates attested in 声律启蒙 and not already categorized.)
CANDIDATES = {
    "颜色": list("赤绛缁皂绯缃黝缥黄苍翠"),
    "动物": list("鸿鹏鸠鸾鸡犬牛羊虎豹麟鲲蛟蜂蛙鼠兔鳌鹏蚁螺鸬鹚鹡鸰鹏鹏"),
    "植物": list("桐椿槐榆棠蕉芷蒲菱薇蕙茅葵橘棣棕楸蓼荇萝薜"),
    "数字": list("两兆亿寻仞"),
    "方位": list("旁畔隅际涯表里"),
    "时间": list("午辰旬岁载曙更晡曛申酉戌亥子丑寅卯"),
    # New categories:
    "天文": list("天日月星辰云雨风雪霜露雷电虹霞雾烟霄汉阳阴晖曜霓雹霰岚晕曦"),
    "地理": list("山水江河湖海川峰岭峦岩壑涧溪泉渊潭滩岸汀洲渚坡原野陵谷峡岗崖麓巅池沼塘堤矶嶂"),
}
NEW_CATEGORIES = ["天文", "地理"]


def fetch(arg_idx, cdn):
    if len(sys.argv) > arg_idx and os.path.exists(sys.argv[arg_idx]):
        with open(sys.argv[arg_idx], encoding="utf-8") as f:
            return json.load(f)
    with urllib.request.urlopen(cdn, timeout=30) as r:
        return json.loads(r.read().decode("utf-8"))


def shenglv_chars(sl, tc):
    """Set of simplified characters attested in 声律启蒙."""
    chars = set()

    def walk(node):
        if isinstance(node, str):
            for ch in node:
                chars.add(tc.get(ch, ch))
        elif isinstance(node, list):
            for x in node:
                walk(x)
        elif isinstance(node, dict):
            for x in node.values():
                walk(x)
    walk(sl)
    return chars


def main():
    sl = fetch(1, SL_CDN)
    tc = fetch(2, TC_CDN)
    attested = shenglv_chars(sl, tc)

    with open(OUT, encoding="utf-8") as f:
        doc = json.load(f)
    cats = doc["categories"]
    present = {ch for members in cats.values() for ch in members}

    added, dropped = {}, {}
    for cat, cands in CANDIDATES.items():
        cats.setdefault(cat, [])
        seen = set(cats[cat])
        for ch in cands:
            if ch in present or ch in seen:
                continue
            if ch not in attested:
                dropped.setdefault(cat, []).append(ch)
                continue
            cats[cat].append(ch)
            seen.add(ch)
            present.add(ch)
            added.setdefault(cat, []).append(ch)

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=2)
        f.write("\n")

    for cat in CANDIDATES:
        a = "".join(added.get(cat, []))
        d = "".join(dropped.get(cat, []))
        print("%s  +[%s]%s" % (cat, a, ("  dropped(未见于声律启蒙):[%s]" % d) if d else ""))
    print("\nNEW categories to add to WordClozeGenerator.CategoryPriority:", NEW_CATEGORIES)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
