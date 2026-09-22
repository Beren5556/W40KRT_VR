using UnityEngine;
namespace RTMaquetaXR
{
    internal static class EyeBackgroundPolicy
    {
        internal static bool NeedsColourClear(CameraClearFlags flags)=>flags==CameraClearFlags.Depth||flags==CameraClearFlags.Nothing;
    }
    // Camera.CopyFrom does not copy components. Preserve a camera-local skybox
    // and clear independent eye targets even when the native camera is stacked
    // over a previous pass. Otherwise old scene colour can survive transitions.
    internal sealed class EyeBackground
    {
        Camera source;
        Skybox sourceSkybox,leftSkybox,rightSkybox;
        int observedFrame=-1;
        internal void Apply(Camera eye,Camera native,bool left,bool space)
        {
            if(source!=native||observedFrame<0||Time.frameCount-observedFrame>=120)
            {source=native;sourceSkybox=native.GetComponent<Skybox>();observedFrame=Time.frameCount;}
            Skybox local=left?leftSkybox:rightSkybox;
            if(sourceSkybox!=null&&local==null)
            {local=eye.gameObject.AddComponent<Skybox>();if(left)leftSkybox=local;else rightSkybox=local;}
            if(local!=null)
            {
                bool enabled=!space&&sourceSkybox!=null&&sourceSkybox.enabled;
                if(local.enabled!=enabled)local.enabled=enabled;
                Material material=enabled?sourceSkybox.material:null;
                if(local.material!=material)local.material=material;
            }
            if(EyeBackgroundPolicy.NeedsColourClear(eye.clearFlags))eye.clearFlags=CameraClearFlags.SolidColor;
            // The empty space backdrop is black, including skybox clearing.
            // Nebula geometry, lighting and effects retain their native colours.
            if(space) { eye.clearFlags=CameraClearFlags.SolidColor; eye.backgroundColor=Color.black; }
        }
    }
}
