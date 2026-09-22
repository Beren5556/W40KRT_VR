using System;

namespace RTMaquetaXR
{
    internal static class WorldInformationPolicy70
    {
        // The native footprint already contains the exact cells. This rotates
        // their integer coordinates only for presentation, so the attack
        // direction is always at the top of the compact panel.
        internal static void RotateCell(int x, int y, int originX, int originY,
            float directionX, float directionZ, out int shownX, out int shownY)
        {
            int dx=x-originX,dy=y-originY;
            int octant=(int)Math.Round(Math.Atan2(directionX,directionZ)*4.0/Math.PI,MidpointRounding.AwayFromZero)&7;
            switch(octant)
            {
                case 1: shownX=dx-dy;shownY=dx+dy;break;
                case 2: shownX=-dy;shownY=dx;break;
                case 3: shownX=-dx-dy;shownY=dx-dy;break;
                case 4: shownX=-dx;shownY=-dy;break;
                case 5: shownX=-dx+dy;shownY=-dx-dy;break;
                case 6: shownX=dy;shownY=-dx;break;
                case 7: shownX=dx+dy;shownY=-dx+dy;break;
                default: shownX=dx;shownY=dy;break;
            }
        }

        internal static float PanelCenterY(float distance,float pixelHeight,float scale) =>
            distance*.405f-pixelHeight*scale*.5f;
    }
}
