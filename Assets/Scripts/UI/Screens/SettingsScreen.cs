using System;
using TMPro;
using UnityEngine;

namespace PoemPoetry.UI
{
    public sealed class SettingsScreen : UIScreen
    {
        protected override void OnShow(object args)
        {
            var body = UiKit.ScreenRoot(gameObject, "设置", () => Nav.Pop());
            UiKit.VerticalGroup(body.gameObject, spacing: 18, padX: 40, padY: 20, align: TextAnchor.UpperCenter);

            var s = Services.Settings.Current;
            AddToggle(body, "音效", () => s.SfxOn, v => { s.SfxOn = v; Save(); });
            AddToggle(body, "音乐", () => s.MusicOn, v => { s.MusicOn = v; Save(); });

            var about = UiKit.Text("About", body,
                "唐诗宋词测试 · 本地版\n题库可在 Unity 菜单「唐诗宋词/内容工具」导入与扩充", 28,
                TextAlignmentOptions.Center, UiKit.Muted, wrap: true);
            UiKit.MinHeight(about.gameObject, 130);
        }

        private void AddToggle(Transform body, string label, Func<bool> get, Action<bool> set)
        {
            var btn = UiKit.Button(label, body, "", out var lbl, UiKit.Card, 36);
            UiKit.Pref(btn.gameObject, minH: 100);
            lbl.text = $"{label}：{(get() ? "开" : "关")}";
            btn.onClick.AddListener(() =>
            {
                set(!get());
                lbl.text = $"{label}：{(get() ? "开" : "关")}";
            });
        }

        private void Save() => _ = Services.Settings.SaveAsync();
    }
}
