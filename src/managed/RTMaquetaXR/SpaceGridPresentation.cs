using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    // The native board grid is separate from the movement/ability surface.
    // Limit this presentation repair to GridVisualizer's own SpaceCombatGrid
    // material; no pathfinding, tactical cells or effect budgets are modified.
    internal static class SpaceGridPolicy
    {
        internal static bool IsBoardMaterial(string name) => name == "SpaceCombatGrid" || name == "SpaceCombatGrid (Instance)";
        internal static float ReadableAlpha(float alpha) => float.IsNaN(alpha) || float.IsInfinity(alpha) ? alpha : Math.Max(alpha,.22f);
    }
    public static partial class Main
    {
        sealed class SpaceGridLease
        {
            internal Renderer Renderer;
            internal bool Enabled;
            internal Material[] Original, Applied;
            internal readonly List<Material> Owned = new List<Material>();
        }
        static Type _spaceGridType;
        static Func<object,object> _spaceGridRenderers;
        static Action<object,bool> _spaceGridNativeMode;
        static readonly List<SpaceGridLease> _spaceGridLeases=new List<SpaceGridLease>();
        static long _spaceGridRevision=-1;
        static float _spaceGridNext;
        static int _spaceGridAttempts;
        static string _spaceGridFault;

        static void UpdateSpaceGridPresentation()
        {
            if(!_active||!InSpaceCombat){RestoreSpaceGridPresentation();return;}
            if(!_attached||_modeFlat||ObservedNativeLoading||NativeTutorialInputBlocked)
            {RestoreSpaceGridPresentation();return;}
            if(_spaceGridRevision!=SpatialGameContextRevision)
            {RestoreSpaceGridPresentation();_spaceGridRevision=SpatialGameContextRevision;_spaceGridAttempts=0;_spaceGridNext=0;}
            if(_spaceGridLeases.Count!=0||_spaceGridAttempts>=8||Time.unscaledTime<_spaceGridNext)return;
            _spaceGridNext=Time.unscaledTime+1;++_spaceGridAttempts;
            try
            {
                if(_spaceGridType==null)
                {
                    var gridType=AccessTools.TypeByName("Kingmaker.UI.CoverVisualiserSystem.GridVisualizer");
                    var field=PcUiPath.Field(gridType,"Renderers");
                    if(field.FieldType!=typeof(Renderer[]))throw new MissingFieldException("GridVisualizer.Renderers");
                    _spaceGridRenderers=TouchSelectionCallFactory.FieldGetter(field);
                    _spaceGridNativeMode=(Action<object,bool>)TouchSelectionCallFactory.Build(typeof(Action<object,bool>),
                        TouchSelectionCallFactory.ExactMethod(gridType,"HandleTurnBasedModeSwitched",typeof(void),false,typeof(bool)));
                    _spaceGridType=gridType;
                }
                foreach(var item in UnityEngine.Object.FindObjectsByType(_spaceGridType,FindObjectsSortMode.None))
                {
                    var view=item as Behaviour;
                    if(view==null||!view.isActiveAndEnabled)continue;
                    var renderers=_spaceGridRenderers(view) as Renderer[];if(renderers==null)continue;
                    bool board=false;
                    foreach(var renderer in renderers)if(renderer!=null)
                        foreach(var material in renderer.sharedMaterials)
                            if(material!=null&&SpaceGridPolicy.IsBoardMaterial(material.name))board=true;
                    if(!board)continue;
                    // Loading may enable this component before the native turn
                    // mode notification. Ask its original callback to rebuild
                    // from the actual graph and enable its own renderers once.
                    int firstLease=_spaceGridLeases.Count;
                    // Record every renderer BEFORE calling native rebuilding:
                    // it may change enabled/material state and then throw.
                    foreach(var renderer in renderers)
                        if(renderer!=null)
                        {
                            var original=renderer.sharedMaterials;
                            _spaceGridLeases.Add(new SpaceGridLease{Renderer=renderer,Enabled=renderer.enabled,
                                Original=original,Applied=(Material[])original.Clone()});
                        }
                    _spaceGridNativeMode(view,true);
                    for(int i=firstLease;i<_spaceGridLeases.Count;++i)
                    {
                        var lease=_spaceGridLeases[i];var renderer=lease.Renderer;if(renderer==null)continue;
                        var original=renderer.sharedMaterials;var applied=(Material[])original.Clone();
                        // The callback owns any material instance created by
                        // Renderer.material. Restore that native result, not a
                        // stale shared reference captured before its rebuild.
                        lease.Original=original;lease.Applied=applied;
                        for(int m=0;m<original.Length;++m)
                        {
                            var source=original[m];if(source==null||!SpaceGridPolicy.IsBoardMaterial(source.name))continue;
                            var copy=new Material(source){name="RTVR stable native board grid"};lease.Owned.Add(copy);applied[m]=copy;
                            // Preserve the game's texture, cell spacing, colours
                            // and depth testing. Animated UV noise otherwise
                            // shimmers across the two eye histories.
                            if(copy.HasProperty("_Noise0Scale"))copy.SetFloat("_Noise0Scale",0);
                            foreach(string property in new[]{"_UV1Speed","_Noise0Speed"})
                                if(copy.HasProperty(property))copy.SetVector(property,Vector4.zero);
                            foreach(string property in new[]{"_BaseColor","_Color"})
                                if(copy.HasProperty(property)){var colour=copy.GetColor(property);colour.a=SpaceGridPolicy.ReadableAlpha(colour.a);copy.SetColor(property,colour);}
                        }
                        if(lease.Owned.Count!=0)renderer.sharedMaterials=applied;
                    }
                }
            }
            catch(Exception error)
            {
                long revision=_spaceGridRevision;
                RestoreSpaceGridPresentation();_spaceGridRevision=revision;
                if(_spaceGridFault!=error.Message){_spaceGridFault=error.Message;_log.Log("[space/grid] Native grid retained: "+error.Message);}
            }
        }
        static void RestoreSpaceGridPresentation()
        {
            foreach(var lease in _spaceGridLeases)
            {
                if(lease.Renderer!=null)
                {
                    var current=lease.Renderer.sharedMaterials;bool changed=false;
                    for(int i=0;i<current.Length&&i<lease.Applied.Length;++i)
                        if(lease.Owned.Contains(current[i])){current[i]=lease.Original[i];changed=true;}
                    if(changed)lease.Renderer.sharedMaterials=current;
                    if(lease.Renderer.enabled)lease.Renderer.enabled=lease.Enabled;
                }
                foreach(var material in lease.Owned)if(material!=null)UnityEngine.Object.Destroy(material);
            }
            _spaceGridLeases.Clear();_spaceGridRevision=-1;
        }
    }
}
