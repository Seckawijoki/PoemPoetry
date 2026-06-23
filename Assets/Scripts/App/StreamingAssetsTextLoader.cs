using System.IO;
using System.Text;
using System.Threading.Tasks;
using PoemPoetry.Data;
using UnityEngine;
using UnityEngine.Networking;

namespace PoemPoetry.App
{
    /// <summary>
    /// Reads shipped content text from StreamingAssets. On Android the files live inside the
    /// compressed APK, so they must be read asynchronously via UnityWebRequest; everywhere else
    /// direct file IO works.
    /// </summary>
    public sealed class StreamingAssetsTextLoader : IRawTextLoader
    {
        private readonly string _root;

        public StreamingAssetsTextLoader(string root) { _root = root; }

        public bool Exists(string relativePath)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return true; // cannot stat inside the APK jar; assume present, let the read surface errors
#else
            return File.Exists(Path.Combine(_root, relativePath));
#endif
        }

        public async Task<string> ReadTextAsync(string relativePath)
        {
            string path = Path.Combine(_root, relativePath);
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var req = UnityWebRequest.Get(path))
            {
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
                if (req.result != UnityWebRequest.Result.Success)
                    throw new IOException($"Failed to read {path}: {req.error}");
                return req.downloadHandler.text;
            }
#else
            await Task.Yield();
            return File.ReadAllText(path, Encoding.UTF8);
#endif
        }
    }
}
