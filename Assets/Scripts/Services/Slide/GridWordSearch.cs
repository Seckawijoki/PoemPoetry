using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PoemPoetry.Data;

namespace PoemPoetry.Services
{
    /// <summary>
    /// Pure model for 滑动找诗 (grid word-search): places poem-line "words" into a Cols×Rows grid
    /// and matches traced paths back to them. UnityEngine-free and deterministic (seeded rng).
    ///
    /// Direction level: 1=straight 4-dir, 2=straight 8-dir, 3=snake 4-dir, 4=snake 8-dir.
    /// </summary>
    public sealed class GridWordSearch
    {
        public sealed class Target
        {
            public string Text;
            public string[] Chars;
            public string Title;
            public string PoemId;
            public readonly List<int> Cells = new List<int>(); // grid indices (r*Cols+c) in reading order
            public bool Found;
        }

        // 0..3 orthogonal (R,D,L,U), 4..7 diagonal.
        private static readonly int[] DR = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] DC = { 1, 0, -1, 0, 1, -1, 1, -1 };

        private readonly List<Target> _targets = new List<Target>();
        private readonly IRandomSource _rng;

        public int Cols { get; }
        public int Rows { get; }
        public int Size => Cols;                         // compat alias (square grids)
        public int Level { get; }
        public bool AllowOverlap { get; }
        public string[] Cells { get; }                   // length Cols*Rows; null until filled
        public IReadOnlyList<Target> Targets => _targets;

        public bool Diagonal => Level == 2 || Level == 4;
        public bool Snake => Level == 3 || Level == 4;
        private int DirCount => Diagonal ? 8 : 4;

        public GridWordSearch(int cols, int rows, int level, bool allowOverlap, IRandomSource rng)
        {
            Cols = cols < 4 ? 4 : cols;
            Rows = rows < 4 ? 4 : rows;
            Level = level < 1 ? 1 : (level > 4 ? 4 : level);
            AllowOverlap = allowOverlap;
            _rng = rng ?? new SystemRandomSource();
            Cells = new string[Cols * Rows];
        }

        public GridWordSearch(int size, int level, bool allowOverlap, IRandomSource rng)
            : this(size, size, level, allowOverlap, rng) { }

        /// <summary>Reconstruct a grid (for replay/review) from a stored snapshot.</summary>
        public static GridWordSearch FromSnapshot(SlideSnapshot snap)
        {
            int cols = snap.Size > 0 ? snap.Size : 9;
            int rows = snap.Rows > 0 ? snap.Rows : cols;
            // Level 4 => 8-directional adjacency, so any placed path (incl. diagonal/snake) is traceable on replay.
            var g = new GridWordSearch(cols, rows, 4, false, new SystemRandomSource(1));
            for (int i = 0; i < snap.Cells.Count && i < g.Cells.Length; i++) g.Cells[i] = snap.Cells[i];
            foreach (var ts in snap.Targets)
            {
                var t = new Target { Text = ts.Text, Title = ts.Title, PoemId = ts.PoemId, Found = ts.Found, Chars = CharsOf(ts.Text) };
                if (ts.Cells != null) foreach (var c in ts.Cells) t.Cells.Add(c);
                g._targets.Add(t);
            }
            return g;
        }

        private static string[] CharsOf(string s)
        {
            var si = new StringInfo(s ?? "");
            int n = si.LengthInTextElements;
            var r = new string[n];
            for (int i = 0; i < n; i++) r[i] = si.SubstringByTextElements(i, 1);
            return r;
        }

        public bool TryPlace(string text, string[] chars, string title, string poemId = "", int maxAttempts = 400)
        {
            if (chars == null) return false;
            int len = chars.Length;
            if (len < 2 || len > Cols * Rows) return false;
            int maxLine = Cols > Rows ? Cols : Rows;
            if (!Snake && len > maxLine) return false; // a straight run can't exceed the longer side

            // Without overlap: take the first fitting placement.
            // With overlap (重叠字交叉优先): random placements almost never land a matching char on an
            // existing one, so crossings stayed theoretical. Instead we *construct* them crossword-style:
            // for every already-placed cell whose char also appears in this line, lay the line straight
            // through that cell along each allowed direction and keep the most-interlocked fit. Random
            // attempts remain as the fallback (first word, or when no shared char lines up).
            List<int> best = null;
            int bestCross = -1;

            if (AllowOverlap)
            {
                for (int fi = 0; fi < Cells.Length; fi++)
                {
                    string fc = Cells[fi];
                    if (fc == null) continue;                // only existing word cells anchor a crossing
                    for (int j = 0; j < len; j++)
                    {
                        if (chars[j] != fc) continue;        // line's j-th char must match what's here
                        int fr = fi / Cols, fcol = fi % Cols;
                        for (int d = 0; d < DirCount; d++)
                        {
                            // Place so chars[j] sits on (fr,fcol): start j steps back along direction d.
                            var path = StraightFrom(fr - j * DR[d], fcol - j * DC[d], d, len);
                            if (path == null || !FitsAndFree(path, chars)) continue;
                            if (HasAdjacentFilled(path)) continue;   // 只允许点交叉，禁止与已有诗句并行重叠
                            int cross = Crossings(path);
                            if (cross > bestCross) { bestCross = cross; best = path; }
                        }
                    }
                }
                if (bestCross >= len - 1 && best != null) goto done; // maximally interlocked
            }

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var path = Snake ? RandomSnake(len) : RandomStraight(len);
                if (path == null) continue;
                if (!FitsAndFree(path, chars)) continue;
                if (HasAdjacentFilled(path)) continue;   // 禁止与已有诗句并行重叠（相邻两格都已占用）

                if (!AllowOverlap) { best = path; break; }
                int cross = Crossings(path);                 // non-null cells == matching chars (FitsAndFree guaranteed)
                if (cross > bestCross) { bestCross = cross; best = path; }
                if (cross >= len - 1) break;                 // already maximally interlocked
            }
        done:
            if (best == null) return false;

            var t = new Target { Text = text, Chars = chars, Title = title, PoemId = poemId };
            for (int k = 0; k < len; k++)
            {
                Cells[best[k]] = chars[k];
                t.Cells.Add(best[k]);
            }
            _targets.Add(t);
            return true;
        }

        // How many cells of this path land on an already-filled cell (a char crossing).
        private int Crossings(List<int> path)
        {
            int n = 0;
            foreach (var idx in path) if (Cells[idx] != null) n++;
            return n;
        }

        // True if two consecutive cells of the path are both already filled. That means the new line
        // would run *alongside* an existing one (sharing a 2+ char run, e.g. 黄鹤 / 明月) rather than
        // crossing it at a single point — which looks like one block doing double duty. Reject those so
        // overlaps are always clean point-crossings.
        private bool HasAdjacentFilled(List<int> path)
        {
            for (int k = 1; k < path.Count; k++)
                if (Cells[path[k]] != null && Cells[path[k - 1]] != null) return true;
            return false;
        }

        public void FillEmpty(IList<string> pool)
        {
            if (pool == null || pool.Count == 0) pool = new List<string> { "诗" };
            for (int i = 0; i < Cells.Length; i++)
                if (Cells[i] == null) Cells[i] = pool[_rng.Next(pool.Count)];
        }

        /// <summary>Match a traced path to an unfound target (forward-only; no reverse).</summary>
        public Target TryMatch(IList<int> path)
        {
            if (path == null || path.Count < 2) return null;
            var sb = new StringBuilder(path.Count);
            foreach (var idx in path)
            {
                if (idx < 0 || idx >= Cells.Length) return null;
                sb.Append(Cells[idx]);
            }
            string s = sb.ToString();
            foreach (var t in _targets)
            {
                if (t.Found) continue;
                if (s == string.Concat(t.Chars)) { t.Found = true; return t; }
            }
            return null;
        }

        /// <summary>Are cells a and b one allowed step apart (for accumulating a drag path)?</summary>
        public bool Adjacent(int a, int b)
        {
            if (a == b) return false;
            int dr = System.Math.Abs(a / Cols - b / Cols);
            int dc = System.Math.Abs(a % Cols - b % Cols);
            if (dr > 1 || dc > 1) return false;
            if (dr + dc == 1) return true; // orthogonal
            return Diagonal;               // diagonal only on 8-dir levels
        }

        /// <summary>
        /// Snap a drag (start→end) to the nearest allowed straight line and return its cells.
        /// Used by straight levels (L1/L2) so diagonal lines are easy to drag without zig-zag.
        /// </summary>
        public List<int> StraightPath(int start, int end)
        {
            int r0 = start / Cols, c0 = start % Cols, r1 = end / Cols, c1 = end % Cols;
            int dr = r1 - r0, dc = c1 - c0;
            int adr = System.Math.Abs(dr), adc = System.Math.Abs(dc);
            int sr = dr > 0 ? 1 : (dr < 0 ? -1 : 0);
            int sc = dc > 0 ? 1 : (dc < 0 ? -1 : 0);

            int stepR, stepC, len;
            bool diag = Diagonal && adr > 0 && adc > 0 && System.Math.Max(adr, adc) <= 2 * System.Math.Min(adr, adc);
            if (diag) { stepR = sr; stepC = sc; len = System.Math.Max(adr, adc) + 1; }
            else if (adc >= adr) { stepR = 0; stepC = sc; len = adc + 1; }
            else { stepR = sr; stepC = 0; len = adr + 1; }

            var path = new List<int>(len);
            int r = r0, c = c0;
            for (int k = 0; k < len; k++)
            {
                if (r < 0 || r >= Rows || c < 0 || c >= Cols) break;
                path.Add(r * Cols + c);
                r += stepR; c += stepC;
            }
            return path;
        }

        public bool AllFound()
        {
            if (_targets.Count == 0) return false;
            foreach (var t in _targets) if (!t.Found) return false;
            return true;
        }

        // Build a straight run of `len` cells from (r,c) along direction `dir`; null if it leaves the grid.
        private List<int> StraightFrom(int r, int c, int dir, int len)
        {
            var path = new List<int>(len);
            for (int k = 0; k < len; k++)
            {
                if (r < 0 || r >= Rows || c < 0 || c >= Cols) return null;
                path.Add(r * Cols + c);
                r += DR[dir];
                c += DC[dir];
            }
            return path;
        }

        private List<int> RandomStraight(int len)
        {
            int dir = _rng.Next(DirCount);
            int r = _rng.Next(Rows), c = _rng.Next(Cols);
            var path = new List<int>(len);
            for (int k = 0; k < len; k++)
            {
                if (r < 0 || r >= Rows || c < 0 || c >= Cols) return null;
                path.Add(r * Cols + c);
                r += DR[dir];
                c += DC[dir];
            }
            return path;
        }

        private List<int> RandomSnake(int len)
        {
            int r = _rng.Next(Rows), c = _rng.Next(Cols);
            var path = new List<int>(len);
            var used = new HashSet<int>();
            int cur = r * Cols + c;
            path.Add(cur);
            used.Add(cur);
            for (int k = 1; k < len; k++)
            {
                bool moved = false;
                foreach (var d in ShuffledDirs())
                {
                    int nr = r + DR[d], nc = c + DC[d];
                    if (nr < 0 || nr >= Rows || nc < 0 || nc >= Cols) continue;
                    int idx = nr * Cols + nc;
                    if (used.Contains(idx)) continue; // self-avoiding
                    r = nr; c = nc; cur = idx;
                    path.Add(cur);
                    used.Add(cur);
                    moved = true;
                    break;
                }
                if (!moved) return null;
            }
            return path;
        }

        private bool FitsAndFree(List<int> path, string[] chars)
        {
            for (int k = 0; k < path.Count; k++)
            {
                var existing = Cells[path[k]];
                if (existing == null) continue;
                if (AllowOverlap && existing == chars[k]) continue; // cross only where chars match
                return false;
            }
            return true;
        }

        private int[] ShuffledDirs()
        {
            int n = DirCount;
            var arr = new int[n];
            for (int i = 0; i < n; i++) arr[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                int t = arr[i]; arr[i] = arr[j]; arr[j] = t;
            }
            return arr;
        }
    }
}
