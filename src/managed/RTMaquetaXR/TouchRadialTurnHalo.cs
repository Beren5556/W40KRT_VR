using UnityEngine;
using UnityEngine.Rendering;
namespace RTMaquetaXR
{
    // Separate static mesh, no Canvas/Graphic mesh rebuild to animate a turn.
    internal sealed class TouchRadialTurnHalo
    {
        internal GameObject Root;
        internal MeshRenderer Renderer;
        Mesh mesh;
        internal TouchRadialTurnHalo(Transform parent, Material material, float radius)
        {
            Root=new GameObject("Active player turn energy",typeof(MeshFilter),typeof(MeshRenderer)); Root.layer=5; Root.transform.SetParent(parent,false);
            var vertices=new Vector3[128];var colors=new Color[128];var uv=new Vector2[128];var indices=new int[192];
            for(int i=0;i<32;++i)
            {
                float a=(i/16*Mathf.PI)+(i%16)*Mathf.PI/24,b=a+Mathf.PI/24;
                int v=i*4,t=i*6;
                vertices[v]=new Vector3(Mathf.Sin(a),Mathf.Cos(a),-.4f)*(radius+2);
                vertices[v+1]=new Vector3(Mathf.Sin(a),Mathf.Cos(a),-.4f)*(radius+7);
                vertices[v+2]=new Vector3(Mathf.Sin(b),Mathf.Cos(b),-.4f)*(radius+7);
                vertices[v+3]=new Vector3(Mathf.Sin(b),Mathf.Cos(b),-.4f)*(radius+2);
                for(int n=0;n<4;++n){vertices[v+n].z=-.5f; colors[v+n]=new Color(.44f,1,.3f,.65f+(i%16)/48f);}
                indices[t]=v;indices[t+1]=v+1;indices[t+2]=v+2;indices[t+3]=v;indices[t+4]=v+2;indices[t+5]=v+3;
            }
            mesh=new Mesh { name="RTMaquetaXR static turn energy arcs",vertices=vertices,colors=colors,uv=uv,triangles=indices };mesh.RecalculateBounds();
            Root.GetComponent<MeshFilter>().sharedMesh=mesh;
            Renderer=Root.GetComponent<MeshRenderer>();Renderer.sharedMaterial=material;
            Renderer.shadowCastingMode=ShadowCastingMode.Off;Renderer.receiveShadows=false;Renderer.lightProbeUsage=LightProbeUsage.Off;Renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;Renderer.enabled=false;
        }
        internal void Animate(float time) { if(Renderer!=null&&Renderer.enabled)Root.transform.localRotation=Quaternion.Euler(0,0,-time*74); }
        internal void Destroy() { if(Root!=null)Object.Destroy(Root);if(mesh!=null)Object.Destroy(mesh);Root=null;mesh=null;Renderer=null; }
    }
}
