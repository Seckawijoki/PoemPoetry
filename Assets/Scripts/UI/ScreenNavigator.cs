using System.Collections.Generic;
using PoemPoetry.Core;
using UnityEngine;

namespace PoemPoetry.UI
{
    /// <summary>Stack-based screen manager. Screens are created on demand (no prefabs).</summary>
    public sealed class ScreenNavigator : MonoBehaviour
    {
        public AppServices Services { get; private set; }

        private RectTransform _root;
        private readonly Stack<UIScreen> _stack = new Stack<UIScreen>();

        public int Depth => _stack.Count;

        public void Init(RectTransform root, AppServices services)
        {
            _root = root;
            Services = services;
        }

        public T Push<T>(object args = null) where T : UIScreen
        {
            if (_stack.Count > 0) _stack.Peek().Blur();
            var go = new GameObject(typeof(T).Name, typeof(RectTransform));
            go.transform.SetParent(_root, false);
            UiKit.StretchFull(go);
            var screen = go.AddComponent<T>();
            screen.Bind(this);
            _stack.Push(screen);
            screen.Enter(args);
            return screen;
        }

        public void Pop()
        {
            if (_stack.Count == 0) return;
            var top = _stack.Pop();
            top.ExitScreen();
            Destroy(top.gameObject);
            if (_stack.Count > 0) _stack.Peek().Focus();
        }

        public T Replace<T>(object args = null) where T : UIScreen
        {
            if (_stack.Count > 0)
            {
                var top = _stack.Pop();
                top.ExitScreen();
                Destroy(top.gameObject);
            }
            return Push<T>(args);
        }

        public void PopToRoot()
        {
            while (_stack.Count > 1)
            {
                var top = _stack.Pop();
                top.ExitScreen();
                Destroy(top.gameObject);
            }
            if (_stack.Count > 0) _stack.Peek().Focus();
        }

        public void HandleBack()
        {
            if (_stack.Count > 1) Pop();
        }
    }
}
