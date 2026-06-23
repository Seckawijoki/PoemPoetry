using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PoemPoetry.Data
{
    /// <summary>
    /// Reads raw UTF-8 text from the read-only content root, relative paths.
    /// The Android implementation (StreamingAssets inside the APK) lives in the Unity
    /// app assembly and uses UnityWebRequest; this file-based loader serves Editor,
    /// desktop, iOS, and off-engine unit tests.
    /// </summary>
    public interface IRawTextLoader
    {
        Task<string> ReadTextAsync(string relativePath);
        bool Exists(string relativePath);
    }

    public sealed class FileRawTextLoader : IRawTextLoader
    {
        private readonly string _root;

        public FileRawTextLoader(string root) { _root = root; }

        public bool Exists(string relativePath) => File.Exists(Path.Combine(_root, relativePath));

        public Task<string> ReadTextAsync(string relativePath)
        {
            var path = Path.Combine(_root, relativePath);
            return Task.FromResult(File.ReadAllText(path, Encoding.UTF8));
        }
    }
}
