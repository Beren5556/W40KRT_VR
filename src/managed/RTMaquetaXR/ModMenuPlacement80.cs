using UnityEngine;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly Plane[] _menuCameraPlanes80 = new Plane[6];
        static readonly HudFrustumPlane[] _menuPlanes80 = new HudFrustumPlane[8];
        static bool _livePlacementLimited80;
        static void MenuEyePlanes80(Camera camera, Vector3 center, Quaternion inverse, int first)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, _menuCameraPlanes80);
            for(int i=0;i<4;i++)
            {
                var p=_menuCameraPlanes80[i]; Vector3 n=inverse*p.normal;
                _menuPlanes80[first+i]=new HudFrustumPlane{X=n.x,Y=n.y,Z=n.z,Offset=p.GetDistanceToPoint(center)};
            }
        }
        static void PlaceModMenu80(Camera left, Camera right, Vector3 center, Quaternion rotation,float distance)
        {
            var inverse=Quaternion.Inverse(rotation);
            MenuEyePlanes80(left,center,inverse,0); MenuEyePlanes80(right,center,inverse,4);
            bool ordinary=!TurnConfirmationVisible;
            float width=TurnConfirmationVisible?distance*.52f:WorldScale*.75f*(TouchQuickGuideVisible?TouchQuickGuideLayout.WidthAtDistance:.9f);
            float height=width*CurrentLivePanelHeight/CurrentLivePanelWidth;
            var cover=HudPanelLayout.CoverViews(distance,1,float.MaxValue,CurrentLivePanelWidth,_menuPlanes80);
            float x=ordinary?cover.Width*_cfg.modMenuOffsetX:0;
            float y=ordinary?cover.Height*(-.20f+_cfg.modMenuOffsetY):0;
            // A common envelope keeps the anchor stable when opening the guide.
            float envelopeWidth=ordinary?Mathf.Max(width,WorldScale*.75f*.9f):width;
            float envelopeHeight=ordinary?Mathf.Max(height,envelopeWidth*.65f):height;
            if(PanelFit80.Fit(distance,envelopeWidth,envelopeHeight,x,y,_menuPlanes80,out var fitted))
            {x=fitted.X;y=fitted.Y;width*=fitted.Width/envelopeWidth;_livePlacementLimited80=fitted.Limited;}
            else {x=y=0;_livePlacementLimited80=true;}
            PositionHudHelper(_liveRoot.transform,center+rotation*new Vector3(x,y,distance),rotation,width/CurrentLivePanelWidth);
        }
    }
}
