using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PoemPoetry.UI
{
    /// <summary>
    /// Drag-select over a grid: reports the cell index under the pointer on down/drag and a
    /// release event. Lives on the grid container (which must have a raycast-target Image).
    /// </summary>
    public sealed class GridSelector : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public System.Action<int> Down;
        public System.Action<int> Move;
        public System.Action Up;
        public readonly List<RectTransform> Cells = new List<RectTransform>();

        private int Hit(PointerEventData e)
        {
            for (int i = 0; i < Cells.Count; i++)
                if (Cells[i] != null &&
                    RectTransformUtility.RectangleContainsScreenPoint(Cells[i], e.position, e.pressEventCamera))
                    return i;
            return -1;
        }

        public void OnPointerDown(PointerEventData e) => Down?.Invoke(Hit(e));
        public void OnDrag(PointerEventData e) => Move?.Invoke(Hit(e));
        public void OnPointerUp(PointerEventData e) => Up?.Invoke();
    }
}
