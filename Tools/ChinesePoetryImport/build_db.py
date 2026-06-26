#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Compile the shipped JSON content into a single embedded SQLite database (content.db),
the runtime read-only store for all three game modes. Pure Python stdlib (sqlite3) —
no external deps. NOT part of the Unity/C# build; run it after the JSON pipeline.

Pipeline position:
  Tools/TestHarness/build_and_test.ps1   # generate + validate the JSON (source of truth)
  python build_db.py                     # compile JSON -> content.db (this script)

Reads:
  Assets/StreamingAssets/PoemData/{poems,questions,word_questions,char_pinyin,
                                    rhyme_groups,pingshui_rhyme}.json
  Tools/SampleContent/{semantic_categories,word_bank}.json   # build-time inputs
Writes:
  Assets/StreamingAssets/PoemData/content.db

Design notes:
  * Nested-but-queried collections -> child tables (poem_lines, cluster_lines,
    wordcloze_blanks, semantic_categories). Nested-but-bulk-only -> JSON columns
    (tags, tile_pool, answer_chars, pinyin, sources, line_indices).
  * QuestionOption / PoemLine fields that are empty are OMITTED from the JSON
    (Newtonsoft NullValueHandling) -> every read uses dict.get(key, default).
  * CJK full-text search: SQLite FTS5 unicode61 does not split Han runs, so we store a
    per-character SPACE-SPLIT projection of line text/title/author in poem_fts. The C#
    query side must space-split the user query the same way (see tok()).
  * meta(schema_version, content_version, built_utc): the runtime copies content.db out
    of StreamingAssets to persistentDataPath when content_version changes.

Usage:
  python build_db.py                 # default paths
  python build_db.py path/to/out.db  # custom output
"""
import json
import os
import sqlite3
import sys
from datetime import datetime, timezone

# Bump CONTENT_VERSION whenever the shipped data changes so clients re-copy content.db.
SCHEMA_VERSION = 1
CONTENT_VERSION = 6  # bumped: 校勘 26 处原文用字 (芙蓉楼/从军行/琵琶行/阁夜等，统一通行本)

# FTS5 is OFF by default: the native sqlite3 bundled with SQLite4Unity3d is too old to even
# parse the FTS5 shadow tables a modern sqlite writes ("malformed database schema
# (poem_fts_config)"). The core speed win (filtering/凑题/distractors) needs only the plain
# indexed tables below. Re-enable once the platform native libs are modernized (P2 search),
# or replace with a normalized char-index table queried via GLOB/LIKE.
ENABLE_FTS = False

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
DATA_DIR = os.path.join(ROOT, "Assets", "StreamingAssets", "PoemData")
SAMPLE_DIR = os.path.join(ROOT, "Tools", "SampleContent")
DEFAULT_OUT = os.path.join(DATA_DIR, "content.db")


def load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def tok(s):
    """Per-character space split for CJK FTS5 (must mirror the C# query tokenizer)."""
    return " ".join(s or "")


SCHEMA = """
PRAGMA journal_mode = OFF;
PRAGMA synchronous  = OFF;

CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);

-- ---- read-only content ---------------------------------------------------
CREATE TABLE poems (
    id           TEXT PRIMARY KEY,
    dynasty      TEXT,
    author       TEXT,
    title        TEXT,
    type         TEXT,
    cipai        TEXT,
    fame         TEXT,
    difficulty   INTEGER,
    source       TEXT,
    translation  TEXT,
    appreciation TEXT,
    tags_json    TEXT
);

CREATE TABLE poem_lines (
    poem_id               TEXT,
    line_index            INTEGER,
    text                  TEXT,
    char_count            INTEGER,
    last_char             TEXT,
    rhyme_final           TEXT,
    rhyme_group           TEXT,
    pingshui_rhyme        TEXT,
    tone                  TEXT,
    is_rhyme_line         INTEGER,
    pos_pattern           TEXT,
    couplet_partner_index INTEGER,
    grp                   INTEGER,
    famous                INTEGER,
    diff                  INTEGER,
    PRIMARY KEY (poem_id, line_index)
);

CREATE TABLE clusters (
    id          INTEGER PRIMARY KEY,
    char_count  INTEGER,
    rhyme_group TEXT,
    tone_type   TEXT
);

CREATE TABLE cluster_lines (
    cluster_id     INTEGER,
    idx            INTEGER,
    text           TEXT,
    char_count     INTEGER,
    last_char      TEXT,
    rhyme_final    TEXT,
    rhyme_group    TEXT,
    pingshui       TEXT,
    pos_pattern    TEXT,
    source_poem_id TEXT,
    PRIMARY KEY (cluster_id, idx)
);

CREATE TABLE questions (
    id                    TEXT PRIMARY KEY,
    poem_id               TEXT,
    blank_line_index      INTEGER,
    cluster_id            INTEGER,
    correct_text          TEXT,
    correct_char_count    INTEGER,
    correct_last_char     TEXT,
    correct_rhyme_final   TEXT,
    correct_rhyme_group   TEXT,
    correct_pingshui      TEXT,
    correct_pos_pattern   TEXT,
    correct_source_poem_id TEXT,
    difficulty            INTEGER,
    explanation           TEXT,
    source_mode           TEXT
);

CREATE TABLE wordcloze_questions (
    id                TEXT PRIMARY KEY,
    poem_id           TEXT,
    blank_line_index  INTEGER,
    line_indices_json TEXT,
    tile_pool_json    TEXT,
    difficulty        INTEGER,
    blank_count       INTEGER
);

CREATE TABLE wordcloze_blanks (
    question_id       TEXT,
    blank_index       INTEGER,
    line_index        INTEGER,
    start             INTEGER,
    count             INTEGER,
    answer_chars_json TEXT,
    pos               TEXT,
    semantic          TEXT,
    PRIMARY KEY (question_id, blank_index)
);

CREATE TABLE word_bank (
    text       TEXT PRIMARY KEY,
    pos        TEXT,
    char_count INTEGER,
    pinyin_json TEXT,
    tone       TEXT,
    semantic   TEXT,
    sources_json TEXT
);

CREATE TABLE semantic_categories (
    category TEXT,
    char     TEXT,
    PRIMARY KEY (category, char)
);

CREATE TABLE char_pinyin   (char  TEXT PRIMARY KEY, pinyins_json TEXT);
CREATE TABLE rhyme_groups  (final TEXT PRIMARY KEY, group_id TEXT);
CREATE TABLE pingshui_rhyme(char  TEXT PRIMARY KEY, ids_json TEXT);
"""

# Per-character space-split FTS over poem lines (carries poem/title/author for search).
# Created only when ENABLE_FTS — see note at top of file.
FTS_SCHEMA = """
CREATE VIRTUAL TABLE poem_fts USING fts5(
    line_text, title, author,
    poem_id    UNINDEXED,
    line_index UNINDEXED
);
"""

INDEXES = """
CREATE INDEX ix_poems_dynasty       ON poems(dynasty);
CREATE INDEX ix_poems_type          ON poems(type);
CREATE INDEX ix_poems_difficulty    ON poems(difficulty);
CREATE INDEX ix_lines_cc_rg         ON poem_lines(char_count, rhyme_group);
CREATE INDEX ix_lines_famous        ON poem_lines(famous);
CREATE INDEX ix_lines_poem          ON poem_lines(poem_id);
CREATE INDEX ix_q_poem              ON questions(poem_id);
CREATE INDEX ix_q_cluster           ON questions(cluster_id);
CREATE INDEX ix_q_difficulty        ON questions(difficulty);
CREATE INDEX ix_cl_cluster          ON cluster_lines(cluster_id);
CREATE INDEX ix_cl_cc_rg            ON cluster_lines(char_count, rhyme_group);
CREATE INDEX ix_cl_source           ON cluster_lines(source_poem_id);
CREATE INDEX ix_wc_poem_diff        ON wordcloze_questions(poem_id, difficulty);
CREATE INDEX ix_sem_char            ON semantic_categories(char);
"""


def build(out_path):
    poems   = load(os.path.join(DATA_DIR, "poems.json"))
    qbank   = load(os.path.join(DATA_DIR, "questions.json"))
    wcbank  = load(os.path.join(DATA_DIR, "word_questions.json"))
    cpin    = load(os.path.join(DATA_DIR, "char_pinyin.json"))
    rgroups = load(os.path.join(DATA_DIR, "rhyme_groups.json"))
    pingshui = load(os.path.join(DATA_DIR, "pingshui_rhyme.json"))
    cats    = load(os.path.join(SAMPLE_DIR, "semantic_categories.json"))
    wbank   = load(os.path.join(SAMPLE_DIR, "word_bank.json"))

    if os.path.exists(out_path):
        os.remove(out_path)
    db = sqlite3.connect(out_path)
    db.executescript(SCHEMA)
    if ENABLE_FTS:
        db.executescript(FTS_SCHEMA)

    j = lambda v: json.dumps(v, ensure_ascii=False)

    # meta
    db.executemany("INSERT INTO meta(key,value) VALUES(?,?)", [
        ("schema_version", str(SCHEMA_VERSION)),
        ("content_version", str(CONTENT_VERSION)),
        ("built_utc", datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")),
    ])

    # poems + poem_lines
    for p in poems["poems"]:
        db.execute(
            "INSERT INTO poems VALUES (?,?,?,?,?,?,?,?,?,?,?,?)",
            (p["id"], p.get("dynasty", ""), p.get("author", ""), p.get("title", ""),
             p.get("type", ""), p.get("cipai", ""), p.get("fame", ""),
             int(p.get("difficulty", 0)), p.get("source", ""),
             p.get("translation", ""), p.get("appreciation", ""),
             j(p.get("tags", []))))
        for i, ln in enumerate(p.get("lines", [])):
            db.execute(
                "INSERT INTO poem_lines VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                (p["id"], i, ln.get("text", ""), int(ln.get("charCount", 0)),
                 ln.get("lastChar", ""), ln.get("rhymeFinal", ""), ln.get("rhymeGroup", ""),
                 ln.get("pingshuiRhyme", ""), ln.get("tone", ""),
                 1 if ln.get("isRhymeLine", False) else 0, ln.get("posPattern", ""),
                 int(ln.get("coupletPartnerIndex", -1)), int(ln.get("group", -1)),
                 1 if ln.get("famous", False) else 0, int(ln.get("diff", -1))))
            if ENABLE_FTS:
                db.execute(
                    "INSERT INTO poem_fts(line_text,title,author,poem_id,line_index) VALUES (?,?,?,?,?)",
                    (tok(ln.get("text", "")), tok(p.get("title", "")), tok(p.get("author", "")),
                     p["id"], i))

    # clusters + cluster_lines
    for c in qbank.get("clusters", []):
        db.execute("INSERT INTO clusters VALUES (?,?,?,?)",
                   (int(c["id"]), int(c.get("charCount", 0)),
                    c.get("rhymeGroup", ""), c.get("toneType", "")))
        for i, o in enumerate(c.get("lines", [])):
            db.execute(
                "INSERT INTO cluster_lines VALUES (?,?,?,?,?,?,?,?,?,?)",
                (int(c["id"]), i, o.get("text", ""), int(o.get("charCount", 0)),
                 o.get("lastChar", ""), o.get("rhymeFinal", ""), o.get("rhymeGroup", ""),
                 o.get("pingshui", ""), o.get("posPattern", ""), o.get("sourcePoemId", "")))

    # questions (correct option flattened to columns)
    for q in qbank.get("questions", []):
        co = q.get("correct", {}) or {}
        db.execute(
            "INSERT INTO questions VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
            (q["id"], q.get("poemId", ""), int(q.get("blankLineIndex", 0)),
             int(q.get("clusterId", -1)), co.get("text", ""), int(co.get("charCount", 0)),
             co.get("lastChar", ""), co.get("rhymeFinal", ""), co.get("rhymeGroup", ""),
             co.get("pingshui", ""), co.get("posPattern", ""), co.get("sourcePoemId", ""),
             int(q.get("difficulty", 0)), q.get("explanation", ""), q.get("sourceMode", "")))

    # wordcloze questions + blanks
    for w in wcbank.get("questions", []):
        blanks = w.get("blanks", [])
        db.execute(
            "INSERT INTO wordcloze_questions VALUES (?,?,?,?,?,?,?)",
            (w["id"], w.get("poemId", ""), int(w.get("blankLineIndex", 0)),
             j(w.get("lineIndices", [])), j(w.get("tilePool", [])),
             int(w.get("difficulty", 0)), len(blanks)))
        for i, b in enumerate(blanks):
            db.execute(
                "INSERT INTO wordcloze_blanks VALUES (?,?,?,?,?,?,?,?)",
                (w["id"], i, int(b.get("lineIndex", 0)), int(b.get("start", 0)),
                 int(b.get("count", 0)), j(b.get("answerChars", [])),
                 b.get("pos", ""), b.get("semantic", "")))

    # word bank
    for e in wbank.get("words", []):
        db.execute(
            "INSERT INTO word_bank VALUES (?,?,?,?,?,?,?)",
            (e["text"], e.get("pos", ""), int(e.get("charCount", 0)),
             j(e.get("pinyin", [])), e.get("tone", ""), e.get("semantic", ""),
             j(e.get("sources", []))))

    # semantic categories (exploded category -> char rows)
    for cat, chars in cats.get("categories", {}).items():
        for ch in chars:
            db.execute("INSERT OR IGNORE INTO semantic_categories VALUES (?,?)", (cat, ch))

    # rhyme dictionaries
    for ch, readings in cpin.get("entries", {}).items():
        db.execute("INSERT INTO char_pinyin VALUES (?,?)", (ch, j(readings)))
    for final, gid in rgroups.get("groups", {}).items():
        db.execute("INSERT INTO rhyme_groups VALUES (?,?)", (final, gid))
    for ch, ids in pingshui.get("entries", {}).items():
        db.execute("INSERT OR IGNORE INTO pingshui_rhyme VALUES (?,?)", (ch, j(ids)))

    db.executescript(INDEXES)
    db.commit()
    db.execute("VACUUM")
    db.commit()
    db.close()


def verify(out_path):
    db = sqlite3.connect(out_path)
    tables = ["poems", "poem_lines", "clusters", "cluster_lines", "questions",
              "wordcloze_questions", "wordcloze_blanks", "word_bank",
              "semantic_categories", "char_pinyin", "rhyme_groups", "pingshui_rhyme"]
    if ENABLE_FTS:
        tables.append("poem_fts")
    counts = {t: db.execute(f"SELECT COUNT(*) FROM {t}").fetchone()[0] for t in tables}
    meta = dict(db.execute("SELECT key,value FROM meta").fetchall())
    # FTS smoke test: a 2-char CJK query must hit (char-split tokenization).
    hit = db.execute(
        "SELECT poem_id,line_index FROM poem_fts WHERE poem_fts MATCH ? LIMIT 3",
        (tok("明月"),)).fetchall() if ENABLE_FTS else []
    db.close()
    return counts, meta, hit


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT
    build(out)
    # Sidecar version marker: lets the runtime decide whether to re-copy content.db out of the
    # APK without opening the (Android-uncopyable) shipped DB. Mirrors meta.content_version.
    with open(out + ".version", "w", encoding="utf-8") as vf:
        vf.write(str(CONTENT_VERSION))
    counts, meta, hit = verify(out)
    size = os.path.getsize(out)
    print(f"wrote {out}  ({size/1024:.0f} KB)")
    print("meta:", meta)
    for t, n in counts.items():
        print(f"  {t:22} {n}")
    print(f"FTS '明月' -> {len(hit)} line hit(s): {hit}" if ENABLE_FTS
          else "FTS: disabled (ENABLE_FTS=False; bundled native sqlite too old)")
