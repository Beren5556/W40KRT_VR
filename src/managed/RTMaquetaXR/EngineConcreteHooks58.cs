using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RTMaquetaXR
{
    // Harmony shipped by this Unity title loses a closed generic patch's type
    // arguments when another patch rebuilds the same method. Persist concrete
    // method signatures, with prefix/postfix on the SAME declaring type so
    // Harmony's per-declaring-type __state binding remains valid.
    internal static class EngineConcreteHooks58
    {
        static ModuleBuilder module;
        static int next;
        internal static void Patch(Harmony harmony,MethodInfo original,MethodInfo prefix,MethodInfo postfix)
        {
            if(module==null)
            {
                var assembly=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RTMaquetaXR.EngineHooks58"),AssemblyBuilderAccess.Run);
                module=assembly.DefineDynamicModule("EngineHooks58");
            }
            var type=module.DefineType("RTMaquetaXR.EngineProxy58_"+(++next),TypeAttributes.Public|TypeAttributes.Abstract|TypeAttributes.Sealed);
            Emit(type,"Prefix",prefix);Emit(type,"Postfix",postfix);
            var concrete=type.CreateType();
            harmony.Patch(original,prefix:new HarmonyMethod(concrete.GetMethod("Prefix")),postfix:new HarmonyMethod(concrete.GetMethod("Postfix")));
        }
        static void Emit(TypeBuilder type,string name,MethodInfo target)
        {
            var parameters=target.GetParameters();var signature=new Type[parameters.Length];
            for(int i=0;i<parameters.Length;i++)signature[i]=parameters[i].ParameterType;
            var method=type.DefineMethod(name,MethodAttributes.Public|MethodAttributes.Static,target.ReturnType,signature);
            for(int i=0;i<parameters.Length;i++)method.DefineParameter(i+1,parameters[i].Attributes,parameters[i].Name);
            var il=method.GetILGenerator();for(int i=0;i<parameters.Length;i++)il.Emit(OpCodes.Ldarg,i);
            il.Emit(OpCodes.Call,target);il.Emit(OpCodes.Ret);
        }
    }
    internal static class EngineTimingHooks58
    {
        static void Prefix(MethodBase __originalMethod,out Main.EngineMeasure58 __state)=>Main.EngineMeasurePrefix58(__originalMethod,out __state);
        static Exception Finalizer(Exception __exception,Main.EngineMeasure58 __state)=>Main.EngineMeasureFinal58(__exception,__state);
    }
}
