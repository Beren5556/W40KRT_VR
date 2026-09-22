using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
namespace RTMaquetaXR
{
    internal sealed class TouchWeaponComparisonContracts
    {
        internal Type View;
        internal Func<object,object> Model,Weapon,Templates;
        internal Action<object,object> Show;
        internal static TouchWeaponComparisonContracts Create(Func<string,Type> resolve)
        {
            var c=new TouchWeaponComparisonContracts();
            c.View=resolve("Kingmaker.Code.UI.MVVM.View.Slots.ItemSlotPCView");
            var model=PcUiPath.Property(c.View,"ViewModel");c.Model=PcUiPath.Getter(model);
            c.Weapon=PcUiPath.Getter(PcUiPath.Property(model.PropertyType,"ItemWeapon"));
            c.Templates=TouchRadialPartyContracts.Reactive<object>(model.PropertyType,"Tooltip");
            var templates=PcUiPath.Property(PcUiPath.Field(model.PropertyType,"Tooltip").FieldType,"Value").PropertyType;
            Type sequence=typeof(System.Collections.Generic.IEnumerable<>).MakeGenericType(templates.GetGenericArguments()[0]);
            var helper=resolve("Kingmaker.Code.UI.MVVM.VM.Tooltip.Utils.TooltipHelper");
            foreach(var method in helper.GetMethods())
            {
                var args=method.GetParameters();
                if(method.Name=="ShowInfo"&&method.IsStatic&&args.Length==2&&args[0].ParameterType==sequence)
                    c.Show=(Action<object,object>)TouchSelectionCallFactory.Build(typeof(Action<object,object>),method);
            }
            if(c.Show==null)throw new MissingMethodException(helper.FullName,"ShowInfo(IEnumerable<>, navigation)");
            return c;
        }
    }
    public static partial class Main
    {
        static TouchWeaponComparisonContracts _weaponComparison;
        static void InstallTouchWeaponComparison()
        {
            try{_weaponComparison=TouchWeaponComparisonContracts.Create(AccessTools.TypeByName);}
            catch(Exception error){_weaponComparison=null;_log.Error("[touch/comparison] Native comparison unavailable: "+error.Message);}
        }
        static void ProcessTouchWeaponComparison()
        {
            // Y is otherwise unassigned in PC management. Native hover already
            // supplies comparisons with the equipped weapons: reuse that exact
            // list and its original multi-column InfoWindow, never copy stats.
            if(!_touchY.Down||!TouchGameInputAllowed||!TouchMenuWindowVisible||!_touchOverUi||_touchTarget==null||
                _touchPrimary.Held||_touchSecondary.Held||_weaponComparison==null)return;
            try
            {
                var c=_weaponComparison;var view=_touchTarget.GetComponentInParent(c.View);
                if(view==null)return;var model=c.Model(view);
                if(model==null||c.Weapon(model)==null)return;
                var templates=c.Templates(model) as IList;if(templates==null||templates.Count==0)return;
                CancelTouchPointerPress();c.Show(templates,null);
            }
            catch(Exception error){_log.Error("[touch/comparison] Native weapon information: "+error.Message);}
        }
    }
}
