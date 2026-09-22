using System;
using System.Reflection;

namespace RTMaquetaXR
{
    internal static class TouchPcStartupPolicy
    {
        internal static bool Owned(bool active, bool autoStartArmed, bool quitting) => !quitting && (active || autoStartArmed);
    }
    internal sealed class TouchPcStartupContracts
    {
        internal MethodInfo Tick;
        internal Func<object, object> ViewModel;
        internal Action<object> Keyboard;
        internal static TouchPcStartupContracts Create(Func<string, Type> find)
        {
            var view = find("Kingmaker.Code.UI.MVVM.View.ChoseControllerMode.ChoseControllerModeWindowView");
            var vm = find("Kingmaker.Code.UI.MVVM.VM.ChoseControllerMode.GamepadConnectDisconnectVM");
            if (view == null || vm == null) throw new TypeLoadException("Native startup controller view/VM unavailable");
            return new TouchPcStartupContracts {
                Tick = TouchSelectionCallFactory.ExactMethod(view, "OnLateUpdate", typeof(void), false),
                ViewModel = (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>),
                    TouchSelectionCallFactory.ExactMethod(view, "get_ViewModel", vm, false)),
                Keyboard = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>),
                    TouchSelectionCallFactory.ExactMethod(vm, "SetKeyboardMode", typeof(void), false))
            };
        }
    }
}
