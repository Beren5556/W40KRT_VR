using System;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool StandardCover75(WorldInformationSource70 source)
        {
            if(source?.View==null||!WorldHudPolicy.DestructibleCoverView78(source.View.GetType()))return false;
            // Custom blueprints can also be passive cover. Preserve named
            // directly attackable objects, never classify by translated names.
            object entity=ReadMember73(source.ViewModel,"DestructibleEntity");
            object view=ReadMember73(entity,"View");
            return ReadMember73(view,"m_UseCustomBlueprint") is bool custom&&
                (!custom || ReadMember73(view,"CanBeAttackedDirectly") is bool attackable&&!attackable);
        }
        static void RegisterCoverPieces76(WorldInformationSource70 source,bool cover)
        {
            if(!cover)return;
            foreach(string field in new[]{"m_NameBlockPCView","m_OvertipTargetNameView","m_HealthBlockView"})
            {
                var part=ReadMember73(source.View,field) as Component;
                if(part==null)continue;
                foreach(var graphic in part.GetComponentsInChildren<Graphic>(true))RegisterCoverageGraphic74(graphic,source);
            }
        }
        static void ReleaseCoverSource76(WorldInformationSource70 source)
        {
            ReleaseCoverageOwner77(source);
        }
        static bool CoverDecoration75(Component part,bool standardCover)
        {
            if(part==null)return false;
            string name=part.GetType().Name;
            return name.IndexOf("Overtip",StringComparison.Ordinal)>=0&&
                (name.IndexOf("Cover",StringComparison.Ordinal)>=0||standardCover&&
                (name.IndexOf("Health",StringComparison.Ordinal)>=0||name.IndexOf("Name",StringComparison.Ordinal)>=0));
        }
    }
}
