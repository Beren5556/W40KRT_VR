using System;

namespace RTMaquetaXR
{
    internal static class SpaceBoardLevelPolicy58
    {
        internal static ViewPose LevelInHeading76(ViewPose anchor,ViewPose reference,Point3 trackedHead,float scale,Rotation4 entryYaw)
        {
            // Horizon is defined in the player's entry heading, not the fixed
            // tracking-room axes. Otherwise turning the head at entry can turn
            // the requested downward pitch into world roll and erase it.
            Rotation4 basis=anchor.rotation*reference.rotation.Inverse();
            var aligned=new ViewPose(anchor.position,basis*entryYaw);
            var level=Level(aligned,new ViewPose(default(Point3),Rotation4.Identity),default(Point3),1);
            Rotation4 next=level.rotation*entryYaw.Inverse();
            Point3 offset=trackedHead-reference.position;
            Point3 worldHead=anchor.position+basis.Rotate(offset)*scale;
            return new ViewPose(worldHead-next.Rotate(offset)*scale,next*reference.rotation);
        }
        internal static ViewPose Level(ViewPose anchor,ViewPose reference,Point3 trackedHead,float scale)
        {
            Rotation4 basis=anchor.rotation*reference.rotation.Inverse();
            Point3 forward=basis.Rotate(new Point3(0,0,1));
            double horizontal=Math.Sqrt(forward.x*forward.x+forward.z*forward.z);
            if(horizontal<.0001)return anchor; // No defined horizon at the pole.
            float yaw=(float)(Math.Atan2(forward.x,forward.z)*180/Math.PI);
            float pitch=(float)(Math.Atan2(-forward.y,horizontal)*180/Math.PI);
            Rotation4 level=TouchTabletopState.AxisAngle(new Point3(0,1,0),yaw)*TouchTabletopState.AxisAngle(new Point3(1,0,0),pitch);
            Point3 offset=trackedHead-reference.position;
            Point3 worldHead=anchor.position+basis.Rotate(offset)*scale;
            return new ViewPose(worldHead-level.Rotate(offset)*scale,level*reference.rotation);
        }
    }
}
