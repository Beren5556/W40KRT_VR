using UnityEngine;
using UnityEngine.EventSystems;

namespace RTMaquetaXR
{
    // The installed Standalone/KingmakerInputModule retains its click/drag/UI
    // logic. Only its documented input source is replaced while VR owns input.
    public sealed class TouchUiInput : BaseInput
    {
        public override bool mousePresent => Main.TouchInputOwned || base.mousePresent;
        public override Vector2 mousePosition => Main.TouchInputOwned ? Main.TouchPointerScreen : base.mousePosition;
        public override Vector2 mouseScrollDelta => Main.TouchInputOwned ? Main.TouchScroll : base.mouseScrollDelta;
        public override bool GetMouseButtonDown(int button) => Main.TouchInputOwned ? Main.TouchMouseDown(button) : base.GetMouseButtonDown(button);
        public override bool GetMouseButtonUp(int button) => Main.TouchInputOwned ? Main.TouchMouseUp(button) : base.GetMouseButtonUp(button);
        public override bool GetMouseButton(int button) => Main.TouchInputOwned ? Main.TouchMouseHeld(button) : base.GetMouseButton(button);
        public override bool touchSupported => Main.TouchInputOwned ? false : base.touchSupported;
        public override int touchCount => Main.TouchInputOwned ? 0 : base.touchCount;
        public override float GetAxisRaw(string name) => Main.TouchInputOwned ? 0 : base.GetAxisRaw(name);
        public override bool GetButtonDown(string name) => Main.TouchInputOwned ? false : base.GetButtonDown(name);
    }
}
