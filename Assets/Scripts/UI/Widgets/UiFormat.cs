using System;
using System.Globalization;

namespace PoemPoetry.UI
{
    public static class UiFormat
    {
        public static string Duration(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

        public static string Date(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            return iso;
        }

        public static string Mode(string mode)
        {
            switch (mode)
            {
                case "wrongbook": return "错题复习";
                case "slide": return "拼诗句";
                case "wordcloze": return "逐词填空";
                default: return "挑战";
            }
        }
    }
}
