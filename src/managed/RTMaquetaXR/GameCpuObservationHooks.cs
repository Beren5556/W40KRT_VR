using System;
using System.Reflection;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        // Harmony keys __state by the patch method's declaring TYPE, not its
        // name or Harmony id. Main also patches the ability method with an
        // AbilityReuseScope. Keeping both scopes in Main produced invalid IL
        // when the observer was installed after ability-slot reuse.
        static class GameCpuObservationHooks
        {
            internal static void Prefix(MethodBase __originalMethod, out GameCpuScope __state)
            { GameCpuPrefix(__originalMethod, out __state); }
            internal static Exception Finalizer(Exception __exception, GameCpuScope __state)
            { return GameCpuFinalizer(__exception, __state); }
        }
    }
}
