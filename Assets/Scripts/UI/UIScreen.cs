using PoemPoetry.Core;
using UnityEngine;

namespace PoemPoetry.UI
{
    /// <summary>
    /// Base for code-driven screens. Visibility/interactivity via CanvasGroup; subclasses
    /// build their widgets in <see cref="BuildUI"/> and react in the lifecycle hooks.
    /// </summary>
    public abstract class UIScreen : MonoBehaviour
    {
        protected ScreenNavigator Nav { get; private set; }
        protected AppServices Services => Nav != null ? Nav.Services : null;

        private CanvasGroup _cg;

        public void Bind(ScreenNavigator nav)
        {
            Nav = nav;
            _cg = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        }

        public void Enter(object args)
        {
            BuildUI();
            OnShow(args);
            SetInteractable(true);
        }

        public void ExitScreen() => OnHide();

        public void Blur()
        {
            SetInteractable(false);
            gameObject.SetActive(false);
        }

        public void Focus()
        {
            gameObject.SetActive(true);
            SetInteractable(true);
            OnFocus();
        }

        private void SetInteractable(bool v)
        {
            if (_cg == null) return;
            _cg.interactable = v;
            _cg.blocksRaycasts = v;
        }

        protected virtual void BuildUI() { }
        protected virtual void OnShow(object args) { }
        protected virtual void OnHide() { }
        protected virtual void OnFocus() { }
    }
}
