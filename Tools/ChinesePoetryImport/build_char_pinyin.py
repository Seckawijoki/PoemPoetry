#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Fill the gaps in Assets/StreamingAssets/PoemData/char_pinyin.json (字 -> 拼音读音[]).

The shipped dictionary was hand-curated for the original small corpus and never grew
with the seed, so ~70% of the current corpus characters (incl. many 韵脚字) had NO
reading. With no reading, RhymeService leaves RhymeFinal/RhymeGroup empty and
QuestionGenerator skips that line — whole poems (元日 / 忆江南 / 芙蓉楼送辛渐 /
寄扬州韩绰判官 / 塞下曲 / 贾生 …) produced zero questions.

This importer is ADDITIVE and non-destructive:
  * every existing entry is kept verbatim, in its original order (hand-disambiguated
    readings like 还=huan2 / 间=jian1,jian4 are preserved — no regression);
  * any corpus character missing from the dict gets readings from `pypinyin`
    (heteronym=True, Style.TONE3), most-common reading first.

pypinyin's TONE3 spelling already matches PinyinRhyme.Final's expectations
(e.g. guang1, shao3/shao4, lv4 for ü, huan2) — no conversion needed.

This script is NOT part of the Unity/C# build; run it by hand when the corpus grows,
then re-run Tools/TestHarness/build_and_test.ps1 to re-annotate + regenerate questions.

Usage:
  pip install pypinyin
  python build_char_pinyin.py
"""
import json, os, re, sys

try:
    from pypinyin import pinyin, Style
except ImportError:
    sys.exit("pypinyin not installed — run: pip install pypinyin")

HERE = os.path.dirname(__file__)
DATA = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "StreamingAssets", "PoemData"))
OUT = os.path.join(DATA, "char_pinyin.json")
# Corpus sources whose characters must all have a reading.
CORPUS = [
    os.path.normpath(os.path.join(HERE, "..", "SampleContent", "poems_seed.json")),
    os.path.normpath(os.path.join(HERE, "..", "SampleContent", "word_bank_seed.json")),
]

CJK = re.compile(r"[一-鿿]")
TOKEN = re.compile(r"^[a-zü]+[0-9]?$")


def corpus_chars():
    """Every distinct Han character appearing in any corpus JSON (recursively over strings)."""
    chars = set()

    def walk(node):
        if isinstance(node, str):
            chars.update(CJK.findall(node))
        elif isinstance(node, dict):
            for v in node.values():
                walk(v)
        elif isinstance(node, list):
            for v in node:
                walk(v)

    for path in CORPUS:
        if not os.path.exists(path):
            print("  (skip missing corpus: %s)" % path)
            continue
        with open(path, encoding="utf-8") as f:
            walk(json.load(f))
    return chars


def readings_for(ch):
    """pypinyin readings for one char, TONE3, most-common first, deduped, junk filtered."""
    out = []
    for grp in pinyin(ch, heteronym=True, style=Style.TONE3):
        for r in grp:
            r = r.strip().lower()
            if TOKEN.match(r) and r not in out:
                out.append(r)
    return out


def main():
    with open(OUT, encoding="utf-8") as f:
        doc = json.load(f)
    entries = doc.get("entries", {})  # preserves insertion order (py3.7+)

    missing = sorted(c for c in corpus_chars() if c not in entries)
    added, unresolved = 0, []
    for ch in missing:
        rs = readings_for(ch)
        if rs:
            entries[ch] = rs
            added += 1
        else:
            unresolved.append(ch)

    doc["entries"] = entries
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=2)
        f.write("\n")

    print("wrote %s" % OUT)
    print("  entries: %d (+%d added)" % (len(entries), added))
    if unresolved:
        print("  %d corpus chars had no pypinyin reading: %s" % (len(unresolved), "".join(unresolved)))


if __name__ == "__main__":
    main()
