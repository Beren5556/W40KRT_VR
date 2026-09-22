using System;
using System.Reflection;

namespace RTMaquetaXR
{
    internal sealed class TouchGuideContracts
    {
        internal MethodInfo LoadGame, NewGame, AreaReady;
        internal static TouchGuideContracts Create(Func<string, Type> find)
        {
            var game = find("Kingmaker.Game");
            var save = find("Kingmaker.EntitySystem.Persistence.SaveInfo");
            var preset = find("Kingmaker.Blueprints.Area.BlueprintAreaPreset");
            if (save == null || preset == null) throw new MissingMemberException("Touch guide save/preset contracts missing");
            return new TouchGuideContracts {
                LoadGame = TouchSelectionCallFactory.ExactMethod(game, "LoadGameForce", typeof(void), false, save, typeof(Action)),
                NewGame = TouchSelectionCallFactory.ExactMethod(game, "LoadNewGame", typeof(void), false, preset, save),
                AreaReady = TouchSelectionCallFactory.ExactMethod(game, "OnAreaLoadGameModeSet", typeof(void), false)
            };
        }
    }
}
