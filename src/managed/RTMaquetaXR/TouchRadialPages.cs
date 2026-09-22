using System.Collections.Generic;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly List<TouchRadialEntry> _touchRadialCatalogue = new List<TouchRadialEntry>(64);
        static int _touchRadialInnerCount = -1;
        static void ClearTouchRadialCatalogueState()
        {
            TouchRadialPolicy.SetSectorSides(null);
            _touchRadialCatalogue.Clear(); _touchRadialInnerCount=-1;
            _radialActiveWeaponMain=_radialActiveWeaponOff=null; _radialActiveWeaponName=null; _radialActiveWeaponSet=0;
        }
        static bool InnerWeaponEntry(TouchRadialEntry entry)
        {
            var root=entry.VariantParent??entry;
            return root.WeaponSet || root.Space != null && root.Space.Kind==0 || root.Ability && root.Space==null && root.PartKind==2;
        }
        // All native options stay visible. Equipment and its attacks occupy
        // the inner ring; general abilities occupy the outer ring. No pages.
        static void BuildTouchRadialRings()
        {
            _touchRadialCatalogue.Clear();_touchRadialCatalogue.AddRange(_touchRadialEntries);
            int weapons=0;foreach(var entry in _touchRadialCatalogue)if(InnerWeaponEntry(entry))++weapons;
            _touchRadialInnerCount=-1;
            if(weapons>0&&weapons<_touchRadialCatalogue.Count)
            {
                _touchRadialEntries.Clear();
                foreach(var entry in _touchRadialCatalogue)if(InnerWeaponEntry(entry))_touchRadialEntries.Add(entry);
                foreach(var entry in _touchRadialCatalogue)if(!InnerWeaponEntry(entry))_touchRadialEntries.Add(entry);
                _touchRadialInnerCount=weapons;
            }
            RefreshTouchRadialWeaponIdentity();
            int[] sides = null;
            if (_touchRadialSpace && _touchRadial.Side == 0 && !TouchRadialModePolicy.IsCharacters(_touchRadialLeftMode))
            {
                sides=new int[_touchRadialEntries.Count];
                for(int i=0;i<sides.Length;i++)
                {
                    var root=_touchRadialEntries[i].VariantParent??_touchRadialEntries[i];
                    // Native enum identity, never the translated group label.
                    string mount=root.Space?.WeaponKey?.ToString();
                    sides[i]=mount=="Port"?-1:mount=="Starboard"?1:0;
                }
            }
            TouchRadialPolicy.SetSectorSides(sides,_touchRadialInnerCount);
        }
        static string TouchRadialRingCaption() => _touchRadialInnerCount>0 ?
            ModLocalization.Text("INNER RING · WEAPON OPTIONS\nOUTER RING · ACTIONS") :
            TouchRadialPolicy.Rings(_touchRadialEntries.Count)>1 ? ModLocalization.Text("TWO RINGS · ALL OPTIONS") : "";
    }
}
