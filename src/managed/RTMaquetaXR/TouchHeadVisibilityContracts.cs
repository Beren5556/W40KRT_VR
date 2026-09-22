using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using Owlcat.Runtime.Visual.Waaagh;

namespace RTMaquetaXR
{
    internal sealed class TouchHeadVisibilityContracts
    {
        internal MethodInfo Render, AvatarChanged, EquipmentChanged, RendererInfos, HighlightSetup, HighlightRender;
        internal Type HighlighterType;
        internal static TouchHeadVisibilityContracts Create(Func<string,Type> lookup)
        {
            var pipe=lookup("Owlcat.Runtime.Visual.Waaagh.WaaaghPipeline");
            var view=lookup("Kingmaker.View.Mechanics.Entities.AbstractUnitEntityView");
            var unitView=lookup("Kingmaker.View.UnitEntityView");
            var avatar=lookup("Kingmaker.Visual.CharacterSystem.Character");
            var highlighter=lookup("Owlcat.Runtime.Visual.Highlighting.Highlighter");
            var info=highlighter?.GetNestedType("RendererInfo",BindingFlags.NonPublic|BindingFlags.Public);
            if(info==null) throw new MissingMemberException("Head visibility: highlighter renderer contract unavailable");
            // Bind the worker that RT-backed eyes actually use, before its Cull.
            var result=new TouchHeadVisibilityContracts {
                Render=TouchSelectionCallFactory.ExactMethod(pipe,"RenderSingleCamera",typeof(void),false,
                    typeof(ScriptableRenderContext),typeof(CameraData).MakeByRefType()),
                AvatarChanged=TouchSelectionCallFactory.ExactMethod(view,"CharacterAvatarUpdated",typeof(void),false,avatar),
                EquipmentChanged=TouchSelectionCallFactory.ExactMethod(unitView,"HandleUnitChangeActiveEquipmentSet",typeof(void),false),
                RendererInfos=TouchSelectionCallFactory.ExactMethod(highlighter,"GetRendererInfos",typeof(List<>).MakeGenericType(info),false),
                HighlighterType=highlighter,
                HighlightSetup=TouchSelectionCallFactory.ExactMethod(lookup("Owlcat.Runtime.Visual.Waaagh.RendererFeatures.Highlighting.HighlightingFeature"),
                    "StartSetupJobs",typeof(void),false,typeof(RenderingData).MakeByRefType()),
                HighlightRender=TouchSelectionCallFactory.ExactMethod(lookup("Owlcat.Runtime.Visual.Waaagh.RendererFeatures.Highlighting.Passes.HighlighterPass"),
                    "Render",typeof(void),false,lookup("Owlcat.Runtime.Visual.Waaagh.RendererFeatures.Highlighting.Passes.HighlighterPassData"),
                    typeof(UnityEngine.Rendering.RenderGraphModule.RenderGraphContext))
            };
            TouchSelectionCallFactory.ExactMethod(typeof(Renderer),"get_forceRenderingOff",typeof(bool),false);
            TouchSelectionCallFactory.ExactMethod(typeof(Renderer),"set_forceRenderingOff",typeof(void),false,typeof(bool));
            return result;
        }
    }
}
