using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class LayoutStamp58 {internal int Frame;internal long Version;}
        struct LayoutCall58 {internal RectTransform Rect;internal bool Record;}
        static readonly ConditionalWeakTable<RectTransform,LayoutStamp58> _engineLayouts58=new ConditionalWeakTable<RectTransform,LayoutStamp58>();
        static long _layoutVersion58;
        static void InstallPresentationReuse58()
        {
            EnginePatch58(3,AccessTools.Method(typeof(LayoutRebuilder),"MarkLayoutForRebuild"),nameof(LayoutChanged58));
            RectTransform.reapplyDrivenProperties+=LayoutDriven58;
            EnginePatch58(3,AccessTools.Method(typeof(LayoutRebuilder),"ForceRebuildLayoutImmediate"),nameof(LayoutPrefix58),nameof(LayoutPostfix58));
        }
        static void LayoutChanged58(){++_layoutVersion58;}
        static void LayoutDriven58(RectTransform unused){if(Engine58(3))++_layoutVersion58;}
        static bool LayoutPrefix58(RectTransform __0,out LayoutCall58 __state)
        {
            __state=default;if(!Engine58(3)||__0==null)return true;
            ++_engineBlocks58[3].Calls;
            if(_engineLayouts58.TryGetValue(__0,out var stamp)&&stamp.Frame==Time.frameCount&&stamp.Version==_layoutVersion58)
            {++_engineBlocks58[3].Reused;return false;}
            __state=new LayoutCall58{Rect=__0,Record=true};return true;
        }
        static void LayoutPostfix58(LayoutCall58 __state)
        {
            if(!__state.Record||__state.Rect==null)return;
            var stamp=_engineLayouts58.GetOrCreateValue(__state.Rect);stamp.Frame=Time.frameCount;stamp.Version=_layoutVersion58;
        }
    }
}
