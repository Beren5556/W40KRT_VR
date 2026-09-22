using System;
using System.Collections.Generic;
using HarmonyLib;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class InitiativeScope58 {internal InitiativeScope58 Previous;internal object Units;internal bool Ready;}
        static InitiativeScope58 _initiativeScope58;
        static readonly Stack<InitiativeScope58> _initiativePool58=new Stack<InitiativeScope58>();
        static void InstallInitiativeReuse58()
        {
            var turn=EngineMethod58("Kingmaker.Controllers.TurnBased.TurnController","TryRollInitiative",0);
            var units=EngineMethod58(turn.DeclaringType.FullName,"get_UnitsInCombat",0);
            var entity=units.ReturnType.GetGenericArguments()[0];
            EnginePatch58(8,turn,nameof(InitiativeStart58),finalizer:nameof(InitiativeEnd58));
            EngineConcreteHooks58.Patch(_harmony,units,AccessTools.Method(typeof(Main),nameof(InitiativeUnitsPrefix58)).MakeGenericMethod(entity),AccessTools.Method(typeof(Main),nameof(InitiativeUnitsPostfix58)).MakeGenericMethod(entity));
            EngineObserve58(8,turn);
        }
        static void InitiativeStart58(out InitiativeScope58 __state)
        {
            __state=null;if(!Engine58(8))return;
            __state=_initiativePool58.Count>0?_initiativePool58.Pop():new InitiativeScope58();
            __state.Previous=_initiativeScope58;__state.Ready=false;_initiativeScope58=__state;
        }
        static Exception InitiativeEnd58(Exception __exception,InitiativeScope58 __state)
        {
            if(__state!=null)
            {
                _initiativeScope58=__state.Previous;__state.Previous=null;
                if(__state.Units is System.Collections.IList list)list.Clear();
                __state.Ready=false;if(_initiativePool58.Count<8)_initiativePool58.Push(__state);
            }
            return __exception;
        }
        public static bool InitiativeUnitsPrefix58<T>(ref IEnumerable<T> __result)
        {
            if(!Engine58(8)||_initiativeScope58==null)return true;
            ++_engineBlocks58[8].Calls;
            if(_initiativeScope58.Ready&&_initiativeScope58.Units is List<T> units){__result=units;++_engineBlocks58[8].Reused;return false;}
            return true;
        }
        public static void InitiativeUnitsPostfix58<T>(ref IEnumerable<T> __result)
        {
            if(!Engine58(8)||_initiativeScope58==null||_initiativeScope58.Ready||__result==null)return;
            // Membership is shared only by the two consecutive read-only
            // enumerations before InitiativeHelper.Roll. Nothing crosses ticks.
            var units=_initiativeScope58.Units as List<T>??new List<T>();units.Clear();units.AddRange(__result);
            _initiativeScope58.Units=units;_initiativeScope58.Ready=true;__result=units;
        }
    }
}
