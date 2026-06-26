using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace PoemPoetry.App
{
    /// <summary>
    /// Makes the shipped read-only <c>content.db</c> openable at runtime. It lives in
    /// StreamingAssets (inside the compressed APK on Android, so it can't be opened in place);
    /// we copy it to a writable, path-addressable location (persistentDataPath) on first run and
    /// whenever its version marker changes, then hand back that absolute path.
    ///
    /// Version gating uses the tiny <c>content.db.version</c> sidecar emitted by build_db.py, so we
    /// never have to open the (Android-uncopyable) shipped DB just to read its version.
    /// </summary>
    public static class ContentDbProvisioner
    {
        private const string ShippedDb = "PoemData/content.db";
        private const string ShippedVersion = "PoemData/content.db.version";
        private const string LocalDb = "content.db";
        private const string LocalVersion = "content.db.version";

        /// <summary>Ensure content.db exists locally and is current; return its absolute path.</summary>
        public static async Task<string> EnsureAsync(string streamingRoot, string persistentRoot)
        {
            string localDb = Path.Combine(persistentRoot, LocalDb);
            string localVer = Path.Combine(persistentRoot, LocalVersion);

            string shippedVer = (await ReadTextAsync(Path.Combine(streamingRoot, ShippedVersion)))?.Trim();
            string haveVer = File.Exists(localVer) ? File.ReadAllText(localVer, Encoding.UTF8).Trim() : null;

            bool needCopy = !File.Exists(localDb) || haveVer == null || haveVer != shippedVer;
            if (needCopy)
            {
                var bytes = await ReadBytesAsync(Path.Combine(streamingRoot, ShippedDb));
                if (bytes == null || bytes.Length == 0)
                    throw new IOException("content.db is missing or empty in StreamingAssets — run build_db.py.");
                File.WriteAllBytes(localDb, bytes);
                if (!string.IsNullOrEmpty(shippedVer)) File.WriteAllText(localVer, shippedVer, Encoding.UTF8);
            }
            return localDb;
        }

        private static async Task<string> ReadTextAsync(string path)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var req = UnityWebRequest.Get(path))
            {
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (req.result != UnityWebRequest.Result.Success) return null; // missing sidecar -> force copy
                return req.downloadHandler.text;
            }
#else
            await Task.Yield();
            if (!File.Exists(path)) return null;
            // FileShare.ReadWrite so a concurrent holder (e.g. DB Browser, a lingering SQLite handle)
            // doesn't turn the copy into a sharing-violation "cannot open".
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
                return sr.ReadToEnd();
#endif
        }

        private static async Task<byte[]> ReadBytesAsync(string path)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var req = UnityWebRequest.Get(path))
            {
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (req.result != UnityWebRequest.Result.Success)
                    throw new IOException($"Failed to read {path}: {req.error}");
                return req.downloadHandler.data;
            }
#else
            await Task.Yield();
            if (!File.Exists(path)) return null;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var buf = new byte[fs.Length];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = fs.Read(buf, read, buf.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                return buf;
            }
#endif
        }
    }
}
