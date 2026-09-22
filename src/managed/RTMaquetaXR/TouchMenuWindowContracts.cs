using System;
using System.Collections.Generic;

namespace RTMaquetaXR
{
    internal sealed class TouchMenuWindowContract
    {
        internal string Name;
        internal bool Common;
        internal PcUiPath View, ModelView;
        internal Func<object, object> Model;
        internal Action<object> Close;
        internal Func<object, bool> InternalWindow;
        internal object ReadModel(object root)
        {
            var view = (ModelView ?? View).Read(root);
            return view == null ? null : Model(view);
        }
        internal bool CanClose(object currentModel, object rememberedModel) =>
            currentModel != null && ReferenceEquals(currentModel, rememberedModel) && (InternalWindow == null || !InternalWindow(currentModel));
    }

    internal sealed class TouchMenuWindowContracts
    {
        internal TouchMenuWindowContract[] Windows;
        internal PcUiPath MessageBox;
        internal Func<object, object> MessageModel;
        internal static TouchMenuWindowContracts Create(PcHudVisibilityContracts ui)
        {
            var list = new List<TouchMenuWindowContract>();
            // Child modal windows take precedence over their persistent Esc parent.
            list.Add(Window("Settings", true, PcUiPath.Create(ui.CommonType, "m_SettingsPCView", "View"), "Close"));
            list.Add(Window("Save / Load", true, PcUiPath.Create(ui.CommonType, "m_SaveLoadPCView", "View"), "OnClose"));
            list.Add(Window("Co-op roles", true, PcUiPath.Create(ui.CommonType, "m_NetRolesPCView", "View"), "OnClose"));
            var esc = Window("Game menu", true, PcUiPath.Create(ui.CommonType, "m_EscMenuContextPCView", "m_EscMenuPCView"), "OnClose");
            esc.InternalWindow = PcUiPath.BooleanField(
                PcUiPath.Field(PcUiPath.Property(esc.View.ValueType, "ViewModel").PropertyType, "InternalWindowOpened"));
            list.Add(esc);
            list.Add(Window("Formation", false, PcUiPath.Create(ui.SurfaceType, "m_StaticPartPCView", "m_FormationPCView", "View"), "Close"));
            foreach (var root in new[] { ui.SurfaceType, ui.SpaceType })
            {
                var service = Window("Management", false,
                    PcUiPath.Create(root, "m_StaticPartPCView", "m_ServiceWindowsPCView", "m_ServiceWindowMenuPcView"), "Close");
                service.ModelView = service.View;
                service.View = PcUiPath.Create(root, "m_StaticPartPCView", "m_ServiceWindowsPCView");
                list.Add(service);
            }
            // Original character inspection windows, including native details,
            // icons, scrolling and Close callback. Never rebuild their content.
            list.Add(Window("Character information", false,
                PcUiPath.Create(ui.SurfaceType, "m_StaticPartPCView", "SurfaceHUDView", "m_InspectPCView", "m_InfoWindow"), "OnClose"));
            list.Add(Window("Ship information", false,
                PcUiPath.Create(ui.SpaceType, "m_StaticPartPCView", "m_InspectPCView", "m_InfoWindow"), "OnClose"));
            var messageBox = PcUiPath.Create(ui.CommonType, "m_MessageBoxPCView");
            return new TouchMenuWindowContracts { Windows = list.ToArray(), MessageBox = messageBox,
                MessageModel = PcUiPath.Getter(PcUiPath.Property(messageBox.ValueType, "ViewModel")) };
        }
        static TouchMenuWindowContract Window(string name, bool common, PcUiPath view, string close)
        {
            var property = PcUiPath.Property(view.ValueType, "ViewModel");
            return new TouchMenuWindowContract {
                Name = name, Common = common, View = view, Model = PcUiPath.Getter(property),
                Close = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>),
                    TouchSelectionCallFactory.ExactMethod(property.PropertyType, close, typeof(void), false))
            };
        }
    }
}
