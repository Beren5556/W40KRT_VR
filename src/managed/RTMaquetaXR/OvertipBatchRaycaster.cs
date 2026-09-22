using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    // A nested Canvas changes GraphicRegistry ownership. Preserve the original
    // raycaster's camera, priorities, enabled state and result module rather
    // than silently removing its controls from EventSystem.RaycastAll.
    public sealed class OvertipBatchRaycaster : GraphicRaycaster
    {
        internal GraphicRaycaster Source;
        internal Canvas SourceCanvas;
        public override Camera eventCamera => Source != null ? Source.eventCamera : null;
        public override int sortOrderPriority => Source != null ? Source.sortOrderPriority : base.sortOrderPriority;
        public override int renderOrderPriority => Source != null ? Source.renderOrderPriority : base.renderOrderPriority;
        public override void Raycast(PointerEventData eventData, List<RaycastResult> results)
        {
            if (Source == null || SourceCanvas == null || !Source.isActiveAndEnabled) return;
            ignoreReversedGraphics = Source.ignoreReversedGraphics;
            blockingObjects = Source.blockingObjects; blockingMask = Source.blockingMask;
            int start = results.Count;
            base.Raycast(eventData, results);
            for (int i = start; i < results.Count; ++i)
            {
                RaycastResult hit = results[i];
                hit.module = Source; hit.sortingLayer = SourceCanvas.sortingLayerID; hit.sortingOrder = SourceCanvas.sortingOrder;
                results[i] = hit;
            }
        }
    }
}
