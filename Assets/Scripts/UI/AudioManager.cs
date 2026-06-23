using PoemPoetry.Core;
using UnityEngine;

namespace PoemPoetry.UI
{
    /// <summary>
    /// Minimal SFX hub. Clips are optional — assign them on the GameObject (or via the scene
    /// builder) and they play through settings-gated volume; with no clips it is silent.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        public AudioClip clickClip;
        public AudioClip correctClip;
        public AudioClip wrongClip;

        private AudioSource _source;

        private void Awake()
        {
            Instance = this;
            _source = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            DontDestroyOnLoad(gameObject);
        }

        public void PlayClick() => Play(clickClip);
        public void PlayCorrect() => Play(correctClip);
        public void PlayWrong() => Play(wrongClip);

        private void Play(AudioClip clip)
        {
            if (clip == null || _source == null) return;
            var settings = GameApp.Services != null ? GameApp.Services.Settings : null;
            if (settings != null && (!settings.Current.SfxOn)) return;
            float vol = settings != null ? settings.Current.MasterVolume : 1f;
            _source.PlayOneShot(clip, vol);
        }
    }
}
