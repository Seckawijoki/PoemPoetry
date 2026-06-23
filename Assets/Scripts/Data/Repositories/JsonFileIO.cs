using System.IO;
using System.Text;

namespace PoemPoetry.Data
{
    /// <summary>Small helper for atomic JSON read/write of user data files.</summary>
    internal static class JsonFileIO
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static T ReadOrDefault<T>(string path) where T : new()
        {
            if (!File.Exists(path)) return new T();
            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return new T();
            return PoemJson.Deserialize<T>(json) ?? new T();
        }

        public static void Write(string path, object value)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Write to a temp file then atomically swap, so a crash mid-write can't corrupt the file.
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, PoemJson.Serialize(value), Utf8NoBom);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }
}
