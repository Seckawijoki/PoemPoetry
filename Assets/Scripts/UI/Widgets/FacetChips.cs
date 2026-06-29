using System;
using System.Collections.Generic;
using PoemPoetry.Services;
using TMPro;
using UnityEngine;

namespace PoemPoetry.UI
{
    /// <summary>Renders 朝代 filter chips from <see cref="DynastyFacet"/>s. Each chip folds several raw
    /// dynasties (汉魏六朝 / 五代十国) behind one label: toggling it adds/removes ALL of the facet's member
    /// dynasties in <paramref name="selected"/> (which stays a flat set of raw p.Dynasty values, so the
    /// pool filter and last-selection persistence are unchanged).</summary>
    public static class FacetChips
    {
        public static void BuildDynasty(Transform parent, IList<DynastyFacet> facets,
            HashSet<string> selected, Action onChanged, int perRow)
        {
            if (facets == null || facets.Count == 0)
            {
                UiKit.Text("none", parent, "（无）", 28, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
                return;
            }
            if (perRow < 1) perRow = 1;
            Transform row = null;
            for (int i = 0; i < facets.Count; i++)
            {
                if (i % perRow == 0)
                {
                    var p = UiKit.Panel("Row", parent);
                    UiKit.Pref(p, minH: 92);
                    UiKit.HorizontalGroup(p, spacing: 14);
                    row = p.transform;
                }
                var facet = facets[i];
                var b = UiKit.Button("Chip", row, facet.Label, out var lbl, Design.SurfaceHigh, 30);
                Design.SetChip(b, lbl, IsOn(facet, selected));
                b.onClick.AddListener(() =>
                {
                    bool on = IsOn(facet, selected);
                    foreach (var m in facet.Members)
                    {
                        if (on) selected.Remove(m);
                        else selected.Add(m);
                    }
                    Design.SetChip(b, lbl, IsOn(facet, selected));
                    onChanged?.Invoke();
                });
            }
        }

        // A facet shows selected only when every one of its (present) member dynasties is selected.
        private static bool IsOn(DynastyFacet f, HashSet<string> selected)
        {
            if (f.Members.Count == 0) return false;
            foreach (var m in f.Members) if (!selected.Contains(m)) return false;
            return true;
        }
    }
}
