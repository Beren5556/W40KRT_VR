using System;

namespace RTMaquetaXR
{
    internal sealed class TouchRadialInformationContracts
    {
        internal Type InGameModel, BaseUnit;
        internal Func<object> Game;
        internal Func<object, object> Root, UiView, Model, Unit, Tooltip, InfoWindow, Header;
        internal Func<object, bool> Allowed;
        internal Action<object, object> Invoke;
        internal Action<object> Close;
        internal Action<object> FinishClose = delegate { };
        internal bool ImmediateCloseAvailable;
        internal PcUiPath InspectPath, SpaceInspectPath;

        internal static TouchRadialInformationContracts Create(Func<string, Type> resolve)
        {
            Func<string, Type> type = n => resolve(n) ?? throw new TypeLoadException(n);
            var c = new TouchRadialInformationContracts();
            var game = type("Kingmaker.Game");
            var view = type("Kingmaker.Code.UI.MVVM.View.Inspect.InspectPCView");
            var model = type("Kingmaker.UI.MVVM.VM.Inspect.InspectVM");
            c.InGameModel = type("Kingmaker.UI.MVVM.VM.Inspect.InGameInspectVM");
            c.BaseUnit = type("Kingmaker.EntitySystem.Entities.BaseUnitEntity");
            c.Game = (Func<object>)TouchSelectionCallFactory.Build(typeof(Func<object>),
                TouchSelectionCallFactory.ExactMethod(game, "get_Instance", game, true));
            var root = PcUiPath.Property(game, "RootUiContext");
            c.Root = PcUiPath.Getter(root);
            c.UiView = TouchSelectionCallFactory.FieldGetter(PcUiPath.Field(root.PropertyType, "m_UIView"));
            c.InspectPath = PcUiPath.Create(type("Kingmaker.Code.UI.MVVM.View.Surface.PC.SurfacePCView"),
                "m_StaticPartPCView", "SurfaceHUDView", "m_InspectPCView");
            // Space has its own bound InspectPCView and InGameInspectVM. The
            // surface hierarchy is not present there, even though both views
            // use exactly the same original native information window.
            c.SpaceInspectPath = PcUiPath.Create(type("Kingmaker.Code.UI.MVVM.View.Space.PC.SpacePCView"),
                "m_StaticPartPCView", "m_InspectPCView");
            c.Model = PcUiPath.Getter(PcUiPath.Property(view, "ViewModel"));
            c.InfoWindow = TouchSelectionCallFactory.FieldGetter(PcUiPath.Field(view, "m_InfoWindow"));
            c.Header = TouchSelectionCallFactory.FieldGetter(PcUiPath.Field(PcUiPath.Field(view, "m_InfoWindow").FieldType, "m_HeaderContainer"));
            c.Unit = TouchRadialPartyContracts.Reactive<object>(c.InGameModel, "m_Unit");
            c.Tooltip = TouchRadialPartyContracts.Reactive<object>(model, "Tooltip");
            c.Allowed = (Func<object, bool>)TouchSelectionCallFactory.Build(typeof(Func<object, bool>),
                TouchSelectionCallFactory.ExactMethod(type("Kingmaker.Inspect.InspectUnitsHelper"), "IsInspectAllow", typeof(bool), true,
                    type("Kingmaker.EntitySystem.Entities.MechanicEntity")));
            c.Invoke = (Action<object, object>)TouchSelectionCallFactory.Build(typeof(Action<object, object>),
                TouchSelectionCallFactory.ExactMethod(model, "HandleUnitRightClick", typeof(void), false,
                    type("Kingmaker.Mechanics.Entities.AbstractUnitEntity")));
            c.Close = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>),
                TouchSelectionCallFactory.ExactMethod(view, "Close", typeof(void), false));
            // The native hide animation otherwise remains active after the
            // preview is detached, briefly reopening it as a full HUD window.
            // Complete only its existing disappearance tween, with callbacks;
            // never invent a Close action or alter animation duration globally.
            try
            {
                var windowField = PcUiPath.Field(view, "m_InfoWindow");
                var animatorField = PcUiPath.Field(windowField.FieldType, "m_Animator");
                var tweenField = PcUiPath.Field(animatorField.FieldType, "m_DisappearTween");
                var readAnimator = TouchSelectionCallFactory.FieldGetter(animatorField);
                var readTween = TouchSelectionCallFactory.FieldGetter(tweenField);
                var tweenAssembly = tweenField.FieldType.Assembly;
                var tweenType = tweenAssembly.GetType("DG.Tweening.Tween", true);
                var extensions = tweenAssembly.GetType("DG.Tweening.TweenExtensions", true);
                var complete = (Action<object, bool>)TouchSelectionCallFactory.Build(typeof(Action<object, bool>),
                    TouchSelectionCallFactory.ExactMethod(extensions, "Complete", typeof(void), true, tweenType, typeof(bool)));
                c.FinishClose = originalView =>
                {
                    object window = c.InfoWindow(originalView);
                    object animator = window == null ? null : readAnimator(window);
                    object tween = animator == null ? null : readTween(animator);
                    if (tween != null) complete(tween, true);
                };
                c.ImmediateCloseAvailable = true;
            }
            catch (MissingMemberException) { }
            catch (TypeLoadException) { }
            return c;
        }

        internal object ReadView()
        {
            var game = Game(); var root = game == null ? null : Root(game);
            var ui = root == null ? null : UiView(root);
            return InspectPath.Read(ui) ?? SpaceInspectPath.Read(ui);
        }
    }

    // Own only the native tooltip the wheel opened. A native close, dialogue
    // or a different inspection can replace that window at any moment.
    internal sealed class TouchRadialInformationLease
    {
        TouchRadialInformationContracts _contracts;
        object _view, _model, _unit, _tooltip;
        bool _committed;
        internal int Revision { get; private set; }
        internal bool OwnsCurrent => _contracts != null && _view != null && _model != null && _tooltip != null &&
            ReferenceEquals(_contracts.Model(_view), _model) && ReferenceEquals(_contracts.Tooltip(_model), _tooltip) &&
            ReferenceEquals(_contracts.Unit(_model), _unit);
        internal bool Show(TouchRadialInformationContracts c, object view, object model, object unit, bool commit)
        {
            if (c == null || view == null || model == null || unit == null || !c.BaseUnit.IsInstanceOfType(unit) || !c.Allowed(unit)) return false;
            if (ReferenceEquals(_view, view) && ReferenceEquals(_model, model) && ReferenceEquals(_unit, unit) &&
                _tooltip != null && ReferenceEquals(c.Tooltip(model), _tooltip) && ReferenceEquals(c.Unit(model), unit))
            { _committed |= commit; return true; }
            Clear(false);
            using (Main.BeginNativeCardLayout()) c.Invoke(model, unit);
            object tooltip = c.Tooltip(model);
            if (tooltip == null || !ReferenceEquals(c.Unit(model), unit)) return false;
            _contracts = c; _view = view; _model = model; _unit = unit; _tooltip = tooltip; _committed = commit;
            ++Revision;
            return true;
        }
        internal void Clear(bool force)
        {
            var c = _contracts; var view = _view; var model = _model; var unit = _unit; var tooltip = _tooltip;
            bool close = force || !_committed;
            _contracts = null; _view = _model = _unit = _tooltip = null; _committed = false;
            if (c != null && close && ReferenceEquals(c.Model(view), model) && ReferenceEquals(c.Tooltip(model), tooltip) &&
                ReferenceEquals(c.Unit(model), unit))
            { c.Close(view); c.FinishClose?.Invoke(view); }
        }
    }
}
