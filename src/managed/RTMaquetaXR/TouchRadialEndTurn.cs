using System;
using System.Reflection;
using HarmonyLib;
namespace RTMaquetaXR
{
    internal sealed class TouchRadialEndTurnContracts
    {
        internal Type View;
        internal PropertyInfo Model;
        internal FieldInfo[] Buttons;
        internal Func<object,bool> TurnBased,Shown,CanEnd;
        internal static TouchRadialEndTurnContracts Create(Func<string,Type> resolve)
        {
            var c=new TouchRadialEndTurnContracts();
            c.View=resolve("Kingmaker.Code.UI.MVVM.View.SurfaceCombat.PC.SurfaceHUDPCView")??throw new TypeLoadException("SurfaceHUDPCView");
            c.Model=TouchRadialContracts.Property(c.View,"ViewModel");
            c.Buttons=new[]{TouchRadialContracts.Field(c.View,"m_EndTurnButton")};
            c.TurnBased=TouchRadialPartyContracts.Reactive<bool>(c.Model.PropertyType,"IsTurnBasedActive");
            c.Shown=TouchRadialPartyContracts.Reactive<bool>(c.Model.PropertyType,"ShowEndTurn");
            c.CanEnd=TouchRadialPartyContracts.Reactive<bool>(c.Model.PropertyType,"CanEndTurn");
            return c;
        }
        internal bool Available(object model)=>model!=null&&TurnBased(model)&&Shown(model)&&CanEnd(model);
    }
    public static partial class Main
    {
        static TouchRadialEndTurnContracts _touchRadialEndTurnContracts;
        static Type _spaceEndTurnView;
        static PropertyInfo _spaceEndTurnModel;
        static FieldInfo[] _spaceEndTurnButtons;
        static Func<object,bool> _spaceCanEndTurn,_spacePlayerTurn,_spaceTorpedoTurn;
        internal static bool TouchRadialEndTurnAvailable => _touchRadialEndTurnContracts!=null&&_touchRadialContracts!=null;
        static void InstallTouchRadialEndTurn()
        {
            try{_touchRadialEndTurnContracts=TouchRadialEndTurnContracts.Create(AccessTools.TypeByName);
                InstallEndTurnConfirmation();
                _spaceEndTurnView=AccessTools.TypeByName("Kingmaker.UI.MVVM.View.SpaceCombat.PC.SpaceCombatServicePanelPCView");
                _spaceEndTurnModel=TouchRadialContracts.Property(_spaceEndTurnView,"ViewModel");
                _spaceEndTurnButtons=new[]{TouchRadialContracts.Field(_spaceEndTurnView,"m_EndTurnButton")};
                _spaceCanEndTurn=TouchRadialPartyContracts.Reactive<bool>(_spaceEndTurnModel.PropertyType,"CanEndTurn");
                _spacePlayerTurn=TouchRadialPartyContracts.Reactive<bool>(_spaceEndTurnModel.PropertyType,"IsPlayerTurn");
                _spaceTorpedoTurn=TouchRadialPartyContracts.Reactive<bool>(_spaceEndTurnModel.PropertyType,"IsTorpedoesTurn");}
            catch(Exception error){_touchRadialEndTurnContracts=null;_log.Error("[touch/radial] Native End turn unavailable; keep original button: "+error.Message);}
        }
        static void AddTouchRadialEndTurn()
        {
            AddTouchNativeStartBattle();
            if(_touchRadialSpace)
            {
                if(_touchRadial.Side!=0||_spaceEndTurnView==null)return;
                int start=_touchRadialEntries.Count;
                AddTouchRadialMenus(_spaceEndTurnView,_spaceEndTurnModel,_spaceEndTurnButtons,new[]{"End turn"});
                for(int i=start;i<_touchRadialEntries.Count;++i){_touchRadialEntries[i].EndTurn=true;_touchRadialEntries[i].SpaceEndTurn=true;}
                return;
            }
            if(!_touchRadialCombat||_touchRadial.Side!=0||_touchRadialLeftMode!=RTMaquetaXR.TouchRadialLeftMode.Actions||!TouchRadialEndTurnAvailable)return;
            int before=_touchRadialEntries.Count;var c=_touchRadialEndTurnContracts;
            AddTouchRadialMenus(c.View,c.Model,c.Buttons,new[]{"End turn"});
            for(int i=before;i<_touchRadialEntries.Count;++i)_touchRadialEntries[i].EndTurn=true;
        }
    }
}
