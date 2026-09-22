using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Rotation4 _spaceLevelHeading76=Rotation4.Identity;
        static ViewPose SpacePlacement76(Vector3 headPosition,Vector3 forward,XrFrame frame,Vector3 referencePosition,Quaternion referenceRotation,float scale)
        {
            // Establish a level control frame first. Only physical yaw is
            // compensated; physical head roll/pitch must remain head motion,
            // never become artificial roll of the world after entry.
            Quaternion physical=frame.head.Rotation;
            Vector3 flat=Vector3.ProjectOnPlane(physical*Vector3.forward,Vector3.up).normalized;
            Quaternion yaw=flat.sqrMagnitude>.5f?Quaternion.LookRotation(flat,Vector3.up):Quaternion.identity;
            _spaceLevelHeading76=TableRotation(yaw);
            Quaternion basis=Quaternion.LookRotation(forward,Vector3.up)*Quaternion.Inverse(yaw);
            var candidate=new ViewPose(TablePoint(headPosition),TableRotation(basis*referenceRotation));
            // Remove any roll introduced by composition, before positioning.
            var level=SpaceBoardLevelPolicy58.LevelInHeading76(candidate,new ViewPose(TablePoint(referencePosition),TableRotation(referenceRotation)),TablePoint(referencePosition),scale,_spaceLevelHeading76);
            basis=TableQuaternion(level.rotation)*Quaternion.Inverse(referenceRotation);
            Vector3 anchor=headPosition-basis*(frame.head.Position-referencePosition)*scale;
            return new ViewPose(TablePoint(anchor),level.rotation);
        }
        static bool SpaceHullRenderer76(Renderer renderer)
        {
            if(renderer==null||!renderer.enabled||(!(renderer is MeshRenderer)&&!(renderer is SkinnedMeshRenderer)))return false;
            // Ship visual meshes, excluding translucent effect meshes and
            // decals. Query only at explicit framing, never on the eye path.
            foreach(var material in renderer.sharedMaterials)
            {
                if(material==null||material.shader==null)continue;
                string shader=material.shader.name.ToLowerInvariant();
                if(material.renderQueue<=2500&&!shader.Contains("particle")&&!shader.Contains("decal")&&!shader.Contains("vfx"))return true;
            }
            return false;
        }
    }
}
