namespace PoemPoetry.Data
{
    /// <summary>Presentation helpers for poem metadata (display-only; never mutates stored data).</summary>
    public static class PoemFormat
    {
        // 节选 poems are stored as "正文·节选"; show the marker in brackets instead: 「离骚·节选」→「离骚（节选）」.
        private const string ExcerptSuffix = "·节选";

        /// <summary>Title as shown to the user — folds a trailing "·节选" into "（节选）".</summary>
        public static string DisplayTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return title;
            if (title.EndsWith(ExcerptSuffix))
                return title.Substring(0, title.Length - ExcerptSuffix.Length) + "（节选）";
            return title;
        }
    }
}
