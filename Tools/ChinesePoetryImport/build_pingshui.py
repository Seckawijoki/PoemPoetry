#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
One-off importer: build Assets/StreamingAssets/PoemData/pingshui_rhyme.json
(字 -> 平水韵 韵部 id[]) from an open 平水韵 table, for the 选择题 distractor
"韵脚细分" optimization (Part B of plan 234-2).

Source: LingDong-/cope  data/rhymebooks.json  (key "平水韵").
That table is a list of two sections, already in SIMPLIFIED Chinese:
  section 0 = 30 平声 韵部 (上平一东…十五删, 下平一先…十五咸)
  section 1 = 76 仄声 韵部 (上声29 + 去声30 + 入声17)
Each 韵部 is a string of its member characters.

韵部 id is data-derived and self-documenting: "<P|Z><NN><首字>"
  P = 平声, Z = 仄声; NN = 1-based index within the section; 首字 = 韵目 representative char.
  e.g. 东->P01东, 删->P15删, 董->Z01董. A 多音字 maps to several 韵部 (e.g. 间->[P15删,Z45谏]).

Chars absent from the table get no entry; RhymeService.PingshuiForChar then returns ""
and QuestionGenerator.Score falls back to the pinyin-final (新韵) bonus — graceful degrade.

This script is NOT part of the Unity/C# build; run it by hand when refreshing the table.

Usage:
  python build_pingshui.py            # fetch source from jsDelivr CDN
  python build_pingshui.py rhymebooks.json   # use a local copy
"""
import json, os, sys, urllib.request

CDN = "https://cdn.jsdelivr.net/gh/LingDong-/cope@master/data/rhymebooks.json"
OUT = os.path.normpath(os.path.join(
    os.path.dirname(__file__), "..", "..",
    "Assets", "StreamingAssets", "PoemData", "pingshui_rhyme.json"))
WS = set("　 \t\r\n")


def load_rhymebooks(arg):
    if arg and os.path.exists(arg):
        with open(arg, encoding="utf-8") as f:
            return json.load(f)
    with urllib.request.urlopen(CDN, timeout=30) as r:
        return json.loads(r.read().decode("utf-8"))


def build(rb):
    ps = next(rb[k] for k in rb if "平水" in k)
    entries = {}
    for sec in (0, 1):
        prefix = "P" if sec == 0 else "Z"
        for idx, grp in enumerate(ps[sec]):
            first = grp[0]
            yid = "%s%02d%s" % (prefix, idx + 1, first)
            for ch in grp:
                if ch in WS:
                    continue
                lst = entries.setdefault(ch, [])
                if yid not in lst:
                    lst.append(yid)
    return entries


def main():
    arg = sys.argv[1] if len(sys.argv) > 1 else None
    rb = load_rhymebooks(arg)
    entries = build(rb)
    # Stable order for a clean diff.
    ordered = {k: entries[k] for k in sorted(entries)}
    doc = {"schemaVersion": 1, "entries": ordered}
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=0, separators=(",", ":"))
    multi = sum(1 for v in entries.values() if len(v) > 1)
    print("wrote %s  (%d chars, %d 多音)" % (OUT, len(entries), multi))


if __name__ == "__main__":
    main()
