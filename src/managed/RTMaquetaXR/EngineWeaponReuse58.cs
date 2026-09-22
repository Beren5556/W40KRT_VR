using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class WeaponScope58
        {
            internal WeaponScope58 Previous;
            internal object Part;
            internal readonly List<object> Slots=new List<object>();
        }
        static WeaponScope58 _weaponScope58;
        static Func<object,object>[] _weaponLists58;
        static void InstallWeaponReuse58()
        {
            if(!_abilityReuseInstalled||_abilityCreateSlot==null)throw new InvalidOperationException("Audited slot binding unavailable");
            var type=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.ActionBar.Surface.SurfaceActionBarPartWeaponSetVM");
            var names=new[]{"MainHandSlots","OffHandSlots","ComboHandsSlots"};
            _weaponLists58=new Func<object,object>[names.Length];
            for(int i=0;i<names.Length;i++)_weaponLists58[i]=TouchSelectionCallFactory.FieldGetter(AccessTools.Field(type,names[i]));
            EnginePatch58(2,EngineMethod58(type.FullName,"UpdateSlots",0),nameof(WeaponScopeStart58),finalizer:nameof(WeaponScopeEnd58));
            EnginePatch58(2,EngineMethod58(type.FullName,"ClearAll",0),nameof(WeaponClear58));
            EnginePatch58(2,EngineMethod58(type.FullName,"TryAddAbilityToSlots",3),transpiler:nameof(WeaponSlotTranspiler58));
            EngineObserve58(2,EngineMethod58(type.FullName,"UpdateSlots",0));
        }
        static void WeaponScopeStart58(object __instance,out WeaponScope58 __state)
        {
            __state=null;if(!Engine58(2))return;
            __state=new WeaponScope58{Part=__instance,Previous=_weaponScope58};_weaponScope58=__state;
        }
        static bool WeaponClear58(object __instance)
        {
            var scope=_weaponScope58;
            if(!Engine58(2)||scope==null||!ReferenceEquals(scope.Part,__instance))return true;
            // Detach only this synchronous rebuild's slots; native weapon,
            // variant and availability discovery still runs in full.
            foreach(var getter in _weaponLists58)
            {
                var list=(IList)_abilityRawList(getter(__instance));
                foreach(var item in list)scope.Slots.Add(item);
                list.Clear();
            }
            ++_engineBlocks58[2].Calls;return false;
        }
        static object WeaponSlot58(object mechanic,int index,bool character,object move,int weapon)
        {
            var scope=_weaponScope58;
            if(Engine58(2)&&scope!=null)
                for(int i=0;i<scope.Slots.Count;i++)
                {
                    var old=scope.Slots[i];if(old==null||!_abilityCanReuse(old,index,character,move,weapon))continue;
                    _abilityPrepareSlot(old);_abilityResetSlot(old);_abilitySetMechanic(old,mechanic);
                    scope.Slots[i]=null;++_engineBlocks58[2].Reused;return old;
                }
            return _abilityCreateSlot(mechanic,index,character,move,weapon);
        }
        static Exception WeaponScopeEnd58(Exception __exception,WeaponScope58 __state)
        {
            if(__state==null)return __exception;_weaponScope58=__state.Previous;
            foreach(var slot in __state.Slots)
                try{(slot as IDisposable)?.Dispose();}catch(Exception e){if(__exception==null)__exception=e;}
            return __exception;
        }
        static IEnumerable<CodeInstruction> WeaponSlotTranspiler58(IEnumerable<CodeInstruction> code)
        {
            int replacements=0;
            foreach(var instruction in code)
            {
                if(instruction.opcode==OpCodes.Newobj&&Equals(instruction.operand,_abilitySlotConstructor))
                {
                    instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(Main),nameof(WeaponSlot58));
                    yield return instruction;yield return new CodeInstruction(OpCodes.Castclass,_abilitySlotConstructor.DeclaringType);++replacements;
                }
                else yield return instruction;
            }
            if(replacements!=1)throw new InvalidOperationException("Weapon slot constructor contract changed");
        }
    }
}
