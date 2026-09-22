using System;
using System.Reflection;
using UnityEngine;

namespace RTMaquetaXR
{
    internal sealed class TouchLocalMapContracts
    {
        internal Func<object> Game;
        internal Func<object, object> Root, UiView, Model, Image, Frame, FrameBlock, MenuModel;
        internal PcUiPath MapPath, ServicePath, MenuPath;
        internal Action<object, Vector2> Pan;
        internal Action<object, float> Zoom;
        internal Action<object> Close;
        internal MethodInfo RotateMethod, FrameAngleMethod;
        internal static TouchLocalMapContracts Create(Func<string, Type> resolve)
        {
            Func<string, Type> type = n => resolve(n) ?? throw new TypeLoadException(n);
            var c = new TouchLocalMapContracts();
            var game = type("Kingmaker.Game");
            var surface = type("Kingmaker.Code.UI.MVVM.View.Surface.PC.SurfacePCView");
            var view = type("Kingmaker.Code.UI.MVVM.View.ServiceWindows.LocalMap.PC.LocalMapPCView");
            c.Game = (Func<object>)TouchSelectionCallFactory.Build(typeof(Func<object>),
                TouchSelectionCallFactory.ExactMethod(game, "get_Instance", game, true));
            var root = PcUiPath.Property(game, "RootUiContext"); c.Root = PcUiPath.Getter(root);
            c.UiView = TouchSelectionCallFactory.FieldGetter(PcUiPath.Field(root.PropertyType, "m_UIView"));
            c.MapPath = PcUiPath.Create(surface, "m_StaticPartPCView", "m_ServiceWindowsPCView", "m_LocalMapPCView");
            c.ServicePath = PcUiPath.Create(surface, "m_StaticPartPCView", "m_ServiceWindowsPCView");
            c.MenuPath = PcUiPath.Create(surface, "m_StaticPartPCView", "m_ServiceWindowsPCView", "m_ServiceWindowMenuPcView");
            c.Model = PcUiPath.Getter(PcUiPath.Property(view, "ViewModel"));
            c.Image = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(view, "m_Image", typeof(UnityEngine.UI.RawImage)));
            c.Frame = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(view, "m_Frame", typeof(RectTransform)));
            c.FrameBlock = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(view, "m_FrameBlock", typeof(RectTransform)));
            c.Pan = (Action<object, Vector2>)TouchSelectionCallFactory.Build(typeof(Action<object, Vector2>),
                TouchSelectionCallFactory.ExactMethod(view, "UpdateMapPosition", typeof(void), false, typeof(Vector2)));
            c.Zoom = (Action<object, float>)TouchSelectionCallFactory.Build(typeof(Action<object, float>),
                TouchSelectionCallFactory.ExactMethod(view, "SetMapScale", typeof(void), false, typeof(float)));
            c.RotateMethod = TouchSelectionCallFactory.ExactMethod(view, "SetMapRotation", typeof(void), false, typeof(float));
            c.FrameAngleMethod = TouchSelectionCallFactory.ExactMethod(view, "SetFrameAngle", typeof(void), false, typeof(float));
            var menuModel = PcUiPath.Property(c.MenuPath.ValueType, "ViewModel"); c.MenuModel = PcUiPath.Getter(menuModel);
            c.Close = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>),
                TouchSelectionCallFactory.ExactMethod(menuModel.PropertyType, "Close", typeof(void), false));
            return c;
        }
        internal object ReadSurface()
        {
            var game = Game(); var root = game == null ? null : Root(game);
            return root == null ? null : UiView(root);
        }
    }
}
