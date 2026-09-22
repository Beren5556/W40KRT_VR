using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly TouchAimMovementPolicy _touchMoveAim=new TouchAimMovementPolicy();
        static bool TryTouchMovementAim(object unit,out float yaw)
        {
            yaw = 0;
            var hand = _touchSample.right;
            // Xbox-style horizontal view basis. Capture it before follow advances;
            // keep it for the whole deflection. HMD and Touch aim never steer.
            // Manual gestures stop movement and re-arm from neutral.
            if (unit==null || !_touchTabletop.Initialized) return false;
            float length = new Vector2(hand.stickX, hand.stickY).magnitude;
            return _touchMoveAim.TryYaw(_touchTabletop.Pose.rotation,_touchGroupMove.Moving,
                length<=TouchGroupMovementPolicy.DeadZone,out yaw);
        }
    }
}
