using UnityEngine;
using UnityEngine.UI;
namespace RTMaquetaXR
{
    // Small control silhouettes, not decorative enclosures around text.
    // Shared geometry is rasterized only when the contextual binding changes.
    internal sealed class ContextHintBadge : MaskableGraphic
    {
        string symbol = "";
        internal void SetSymbol(string value) { if (symbol == value) return; symbol = value; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            foreach(var path in ContextControlGlyph.Paths(symbol))Path(mesh,path);
        }
        internal static void DrawFlat(Rect rect,string symbol,Color ink)
        {
            var savedMatrix=GUI.matrix;var savedColor=GUI.color;
            try {
                GUI.color=ink;
                foreach(var path in ContextControlGlyph.Paths(symbol))
                    for(int i=2;i<path.Length;i+=2){
                        var p=new Vector2(rect.x+path[i-2],rect.y+28-path[i-1]);
                        var q=new Vector2(rect.x+path[i],rect.y+28-path[i+1]);
                        var delta=q-p;GUI.matrix=savedMatrix;
                        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,p);
                        GUI.DrawTexture(new Rect(p.x,p.y-.9f,delta.magnitude,1.8f),Texture2D.whiteTexture);
                    }
            }finally{GUI.matrix=savedMatrix;GUI.color=savedColor;}
        }
        void Path(VertexHelper mesh, params float[] points)
        {
            Rect r = rectTransform.rect;
            for (int i=2;i<points.Length;i+=2)
            {
                var p = new Vector2(r.xMin+points[i-2],r.yMin+points[i-1]);
                var q = new Vector2(r.xMin+points[i],r.yMin+points[i+1]);
                var d=(q-p).normalized;var n=new Vector2(-d.y,d.x)*.9f;
                int k=mesh.currentVertCount;
                mesh.AddVert(p-n,color,Vector2.zero);mesh.AddVert(p+n,color,Vector2.zero);
                mesh.AddVert(q+n,color,Vector2.zero);mesh.AddVert(q-n,color,Vector2.zero);
                mesh.AddTriangle(k,k+1,k+2);mesh.AddTriangle(k,k+2,k+3);
            }
        }
    }
}
