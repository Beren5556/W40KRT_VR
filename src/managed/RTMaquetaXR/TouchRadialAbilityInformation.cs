using System;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    // Read the action's existing native template and use the game's original
    // InfoWindowPCView. No synthetic text, model clone or invisible button click.
    internal sealed class TouchRadialAbilityInformationContracts
    {
        internal Func<object> Root;
        internal Func<object, object> Common, Model, Template, Current, Window, Header, ActionIdentity;
        internal Action<object, object, object, bool> Invoke;
        internal Action<object> Close, FinishClose;
        internal PcUiPath ContextPath;
        internal static TouchRadialAbilityInformationContracts Create(Func<string, Type> resolve)
        {
            Func<string, Type> type = n => resolve(n) ?? throw new TypeLoadException(n);
            var c = new TouchRadialAbilityInformationContracts();
            var root = type("Kingmaker.Code.UI.MVVM.RootUIContext");
            c.Root = (Func<object>)TouchSelectionCallFactory.Build(typeof(Func<object>),
                TouchSelectionCallFactory.ExactMethod(root, "get_Instance", root, true));
            c.Common = TouchSelectionCallFactory.FieldGetter(PcUiPath.Field(root, "m_CommonView"));
            c.ContextPath = PcUiPath.Create(type("Kingmaker.Code.UI.MVVM.View.Common.PC.CommonPCView"), "m_TooltipContextPCView");
            var view = type("Kingmaker.Code.UI.MVVM.View.Tooltip.PC.TooltipContextPCView");
            var model = type("Kingmaker.Code.UI.MVVM.VM.Tooltip.TooltipContextVM");
            c.Model = PcUiPath.Getter(PcUiPath.Property(view, "ViewModel"));
            c.Template = TouchRadialPartyContracts.Reactive<object>(type("Kingmaker.Code.UI.MVVM.VM.ActionBar.ActionBarSlotVM"), "Tooltip");
            c.ActionIdentity = PcUiPath.Getter(PcUiPath.Property(type("Kingmaker.Code.UI.MVVM.VM.ActionBar.ActionBarSlotVM"), "MechanicActionBarSlot"));
            c.Current = TouchRadialPartyContracts.Reactive<object>(model, "InfoWindowVM");
            var windowField = PcUiPath.Field(view, "m_InfoWindowPCView");
            c.Window = TouchSelectionCallFactory.FieldGetter(windowField);
            c.Header = TouchSelectionCallFactory.FieldGetter(PcUiPath.Field(windowField.FieldType, "m_HeaderContainer"));
            // Parameter types come from exact native fields/method metadata;
            // the toolkit resides outside Code.dll on supported installations.
            var templateType = PcUiPath.Property(PcUiPath.Field(type("Kingmaker.Code.UI.MVVM.VM.ActionBar.ActionBarSlotVM"), "Tooltip").FieldType, "Value").PropertyType;
            var invoke = model.GetMethod("HandleInfoRequest");
            if (invoke == null || invoke.GetParameters().Length != 3 || invoke.GetParameters()[0].ParameterType != templateType ||
                invoke.GetParameters()[2].ParameterType != typeof(bool) || invoke.ReturnType != typeof(void))
                throw new MissingMethodException(model.FullName, "HandleInfoRequest");
            c.Invoke = (Action<object, object, object, bool>)TouchSelectionCallFactory.Build(typeof(Action<object, object, object, bool>), invoke);
            c.Close = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>),
                TouchSelectionCallFactory.ExactMethod(model, "DisposeInfoWindow", typeof(void), false));
            var animatorField = PcUiPath.Field(windowField.FieldType, "m_Animator");
            var tweenField = PcUiPath.Field(animatorField.FieldType, "m_DisappearTween");
            var readAnimator = TouchSelectionCallFactory.FieldGetter(animatorField);
            var readTween = TouchSelectionCallFactory.FieldGetter(tweenField);
            var tween = tweenField.FieldType.Assembly.GetType("DG.Tweening.Tween", true);
            var extension = tweenField.FieldType.Assembly.GetType("DG.Tweening.TweenExtensions", true);
            var complete = (Action<object, bool>)TouchSelectionCallFactory.Build(typeof(Action<object, bool>),
                TouchSelectionCallFactory.ExactMethod(extension, "Complete", typeof(void), true, tween, typeof(bool)));
            c.FinishClose = context => { var window = c.Window(context); var animator = window == null ? null : readAnimator(window);
                var hidden = animator == null ? null : readTween(animator); if (hidden != null) complete(hidden, true); };
            return c;
        }
        internal object ReadView() { var root = Root(); return ContextPath.Read(root == null ? null : Common(root)); }
    }

    internal sealed class TouchRadialAbilityInformationLease
    {
        TouchRadialAbilityInformationContracts contracts;
        object view, model, template, current, actionModel, actionIdentity;
        internal int Revision { get; private set; }
        internal bool OwnsCurrent => contracts != null && view != null && model != null && current != null &&
            ReferenceEquals(contracts.Model(view), model) && ReferenceEquals(contracts.Current(model), current);
        internal object Window => OwnsCurrent ? contracts.Window(view) : null;
        internal bool Show(TouchRadialAbilityInformationContracts c, object context, object action)
        {
            if (c == null || context == null || action == null) { Clear(); return false; }
            object nextModel = c.Model(context);
            object nextIdentity = c.ActionIdentity(action);
            // Slot resource refreshes can replace Tooltip with an equivalent
            // native template. A continuous hover must not destroy/recreate
            // the parchment every refresh. A different action, context, native
            // close or a fresh hover still fetches the current native template.
            // Native slot VMs are reused when changing weapons/actors. Match
            // the underlying mechanic slot as well as the visual VM identity.
            if (nextIdentity!=null && OwnsCurrent && ReferenceEquals(view,context) &&
                ReferenceEquals(actionModel,action) && ReferenceEquals(actionIdentity,nextIdentity)) return true;
            object nextTemplate = c.Template(action);
            if (nextModel == null || nextTemplate == null) { Clear(); return false; }
            if (OwnsCurrent && ReferenceEquals(view, context) && ReferenceEquals(template, nextTemplate))
            { actionModel=action; actionIdentity=nextIdentity; return true; }
            Clear();
            using (Main.BeginNativeCardLayout()) c.Invoke(nextModel, nextTemplate, null, false);
            object nextCurrent = c.Current(nextModel);
            if (nextCurrent == null) return false;
            contracts = c; view = context; model = nextModel; template = nextTemplate; actionModel = action; actionIdentity = nextIdentity; current = nextCurrent; ++Revision;
            return true;
        }
        internal void Clear()
        {
            bool close = OwnsCurrent; var c = contracts; var originalView = view; var originalModel = model;
            contracts = null; view = model = template = current = actionModel = actionIdentity = null;
            if (close) { c.Close(originalModel); c.FinishClose(originalView); }
        }
    }

    public static partial class Main
    {
        static TouchRadialAbilityInformationContracts _touchRadialAbilityInfoContracts;
        static readonly TouchRadialAbilityInformationLease _touchRadialAbilityInfoLease = new TouchRadialAbilityInformationLease();
        static TouchRadialEntry _touchRadialAbilityInfoEntry;
        static string _touchRadialAbilityInfoFault;
        internal static bool TouchRadialAbilityMode => _touchRadial.Side == 0 && !TouchRadialModePolicy.IsCharacters(_touchRadialLeftMode);
        internal static bool TouchRadialInformationIsAbility => _touchRadial.Visible && TouchRadialAbilityMode && _touchRadialAbilityInfoLease.OwnsCurrent;
        internal static string TouchRadialInformationActionLabel => _touchRadialAbilityInfoEntry?.DisplayLabel;
        static void InstallTouchRadialAbilityInformation()
        {
            InstallNativeCardLayoutBatch();
            try { _touchRadialAbilityInfoContracts = TouchRadialAbilityInformationContracts.Create(AccessTools.TypeByName); }
            catch (Exception error) { _touchRadialAbilityInfoContracts = null; ReportTouchRadialAbilityInformation(error); }
        }
        static void UpdateTouchRadialAbilityInformation(int index)
        {
            var entry = index >= 0 && index < _touchRadialEntries.Count ? _touchRadialEntries[index] : null;
            if (!TouchRadialInfoGripHeld || !TouchRadialAbilityMode || entry == null || !entry.Ability || entry.Model == null)
            { ClearTouchRadialAbilityInformation(); return; }
            if (entry.Space != null && !TouchSpaceRadialSlotBound(entry)) { ClearTouchRadialAbilityInformation(); return; }
            if (entry.VariantParent != null && !TouchRadialVariantBound(entry)) { ClearTouchRadialAbilityInformation(); return; }
            var c = _touchRadialAbilityInfoContracts; if (c == null) return;
            try
            {
                object view = c.ReadView();
                if (_touchRadialAbilityInfoLease.Show(c, view, entry.Model)) _touchRadialAbilityInfoEntry = entry;
            }
            catch (Exception error) { ClearTouchRadialAbilityInformation(); ReportTouchRadialAbilityInformation(error); }
        }
        static void ClearTouchRadialAbilityInformation()
        {
            _touchRadialAbilityInfoEntry = null;
            try { _touchRadialAbilityInfoLease.Clear(); }
            catch (Exception error) { ReportTouchRadialAbilityInformation(error); }
        }
        static void ReportTouchRadialAbilityInformation(Exception error)
        {
            if (_touchRadialAbilityInfoFault == error.Message) return;
            _touchRadialAbilityInfoFault = error.Message; _log.Error("[touch/radial] Native ability information: " + error.Message);
        }
    }
}
