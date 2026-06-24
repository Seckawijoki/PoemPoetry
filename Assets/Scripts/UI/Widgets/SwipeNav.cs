using UnityEngine;

namespace PoemPoetry.UI
{
    /// <summary>
    /// Detects quick horizontal swipes anywhere on the screen and fires prev/next callbacks.
    /// Reads raw <see cref="Input"/> so it coexists with a vertical ScrollRect (which only
    /// consumes vertical drags). Swipe left → next, swipe right → previous.
    /// </summary>
    public sealed class SwipeNav : MonoBehaviour
    {
        public System.Action OnSwipeLeft;   // 向左滑 → 下一首 / 下一组
        public System.Action OnSwipeRight;  // 向右滑 → 上一首 / 上一组

        private const float MaxSeconds = 0.8f;       // ignore slow drags
        private const float MinFracOfWidth = 0.15f;  // horizontal travel must exceed this fraction of screen width
        private const float OffAxisRatio = 0.7f;     // |dy| must stay below this * |dx| to count as horizontal

        private Vector2 _start;
        private float _startTime;
        private bool _tracking;

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                _start = Input.mousePosition;
                _startTime = Time.unscaledTime;
                _tracking = true;
            }
            else if (_tracking && Input.GetMouseButtonUp(0))
            {
                _tracking = false;
                Vector2 d = (Vector2)Input.mousePosition - _start;
                if (Time.unscaledTime - _startTime > MaxSeconds) return;
                if (Mathf.Abs(d.x) < Screen.width * MinFracOfWidth) return;
                if (Mathf.Abs(d.y) > Mathf.Abs(d.x) * OffAxisRatio) return;
                if (d.x < 0f) OnSwipeLeft?.Invoke();
                else OnSwipeRight?.Invoke();
            }
        }
    }
}
