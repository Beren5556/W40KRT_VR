using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace RTMaquetaXR
{
    internal sealed class CombatVisualContracts
    {
        internal Type SurfaceType, MarkType, UnitType, ViewType;
        internal MethodInfo SurfaceEnable, SurfaceDisplay, MarkEnable, DecalActive, DecalMaterial, UnitEnable, RendererInfos, ColorGetter, SetupJobs, RenderHighlights;
        internal Func<object, object> Fill, Outline, Decals, DecalRenderer, Highlighter;
        internal Func<object, Color> Color;
        internal Func<object, float> Transition;
        internal Action<object> UpdateColors;
        internal Func<object, object> ViewEntity;
        internal Func<object, bool> Selected;

        internal static CombatVisualContracts Create(Func<string, Type> find)
        {
            var c = new CombatVisualContracts();
            c.SurfaceType = find("Kingmaker.UI.SurfaceCombatHUD.CombatHudSurfaceRenderer");
            c.MarkType = find("Kingmaker.UI.Selection.UnitMark.BaseSurfaceUnitMark");
            c.UnitType = find("Kingmaker.Visual.UnitMultiHighlight");
            c.ViewType = find("Kingmaker.View.UnitEntityView");
            var entity = find("Kingmaker.EntitySystem.Entities.BaseUnitEntity");
            var decal = find("Kingmaker.UI.Selection.UnitMark.UnitMarkDecal");
            var highlighter = find("Owlcat.Runtime.Visual.Highlighting.Highlighter");
            var info = highlighter?.GetNestedType("RendererInfo", BindingFlags.Public | BindingFlags.NonPublic);
            if (info == null || c.ViewType == null) throw new MissingMemberException("Combat visuals: installed unit/highlighter types unavailable");
            foreach (var component in new[] { c.SurfaceType, c.MarkType, c.UnitType, c.ViewType })
                if (component == null || !typeof(Component).IsAssignableFrom(component))
                    throw new MissingMemberException("Combat visuals: expected a native Unity component for discovery/ownership");
            var list = typeof(List<>).MakeGenericType(info);
            c.SurfaceEnable = Method(c.SurfaceType, "OnEnable", typeof(void));
            c.SurfaceDisplay = Method(c.SurfaceType, "Display", typeof(void));
            c.MarkEnable = Method(c.MarkType, "OnEnabled", typeof(void));
            c.DecalActive = Method(decal, "SetActive", typeof(void), typeof(bool));
            c.DecalMaterial = Method(decal, "SetMaterial", typeof(void), typeof(Material));
            c.UnitEnable = Method(c.UnitType, "OnEnabled", typeof(void));
            c.RendererInfos = Method(highlighter, "GetRendererInfos", list);
            c.ColorGetter = Method(highlighter, "get_CurrentColor", typeof(Color));
            c.Fill = Field(c.SurfaceType, "m_FillMeshRenderer", typeof(MeshRenderer));
            c.Outline = Field(c.SurfaceType, "m_OutlineMeshRenderer", typeof(MeshRenderer));
            c.Decals = Field(c.MarkType, "AllUnitMarkDecal", typeof(List<>).MakeGenericType(decal));
            c.DecalRenderer = Field(decal, "DecalMeshRenderer", typeof(MeshRenderer));
            c.Highlighter = (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), Method(c.UnitType, "get_Highlighter", highlighter));
            c.ViewEntity = (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), Method(c.ViewType, "get_EntityData", entity));
            c.Selected = (Func<object, bool>)TouchSelectionCallFactory.Build(typeof(Func<object, bool>), Method(entity, "get_IsSelected", typeof(bool)));
            c.Color = (Func<object, Color>)TouchSelectionCallFactory.Build(typeof(Func<object, Color>), c.ColorGetter);
            c.UpdateColors = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>), Method(highlighter, "UpdateColors", typeof(void)));
            var transition = TouchSelectionCallFactory.ExactField(highlighter, "m_TransitionValue", typeof(float));
            var getter = new DynamicMethod("RTMaquetaXR_HighlightTransition", typeof(float), new[] { typeof(object) }, typeof(CombatVisualContracts), true);
            var il = getter.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, highlighter); il.Emit(OpCodes.Ldfld, transition); il.Emit(OpCodes.Ret);
            c.Transition = (Func<object, float>)getter.CreateDelegate(typeof(Func<object, float>));
            var feature = find("Owlcat.Runtime.Visual.Waaagh.RendererFeatures.Highlighting.HighlightingFeature");
            var data = find("Owlcat.Runtime.Visual.Waaagh.RenderingData");
            if (data == null) throw new MissingMemberException("Combat visuals: rendering data missing");
            c.SetupJobs = Method(feature, "StartSetupJobs", typeof(void), data.MakeByRefType());
            var pass = find("Owlcat.Runtime.Visual.Waaagh.RendererFeatures.Highlighting.Passes.HighlighterPass");
            var passData = find("Owlcat.Runtime.Visual.Waaagh.RendererFeatures.Highlighting.Passes.HighlighterPassData");
            c.RenderHighlights = Method(pass, "Render", typeof(void), passData, typeof(UnityEngine.Rendering.RenderGraphModule.RenderGraphContext));
            return c;
        }
        static MethodInfo Method(Type type, string name, Type result, params Type[] arguments) => TouchSelectionCallFactory.ExactMethod(type, name, result, false, arguments);
        static Func<object, object> Field(Type type, string name, Type result) => TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(type, name, result));
    }
}
