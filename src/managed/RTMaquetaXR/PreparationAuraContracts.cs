using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace RTMaquetaXR
{
    internal sealed class PreparationAuraContracts
    {
        internal MethodInfo SpawnMethod, DisposeMethod;
        internal Func<object> FxRoot, Game;
        internal Func<object, object> PreparationReference, ResolveBlueprint, Blueprint, Player, Party, Buffs;
        internal Func<object, object, object> GetBuff;
        internal Func<object, bool> Disposed, Attached, Active;
        internal Action<object> Clear, Spawn;

        internal static PreparationAuraContracts Create(Func<string, Type> find)
        {
            var c = new PreparationAuraContracts();
            var buff = find("Kingmaker.UnitLogic.Buffs.Buff");
            var blueprint = find("Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff");
            var reference = find("Kingmaker.Blueprints.BlueprintBuffReference");
            var fx = find("Kingmaker.Blueprints.Root.Fx.FxRoot");
            var game = find("Kingmaker.Game"); var player = find("Kingmaker.Player");
            var unit = find("Kingmaker.EntitySystem.Entities.BaseUnitEntity");
            var buffs = find("Kingmaker.UnitLogic.Buffs.BuffCollection");
            if (buff == null || blueprint == null || reference?.BaseType == null || fx == null || game == null || player == null || unit == null || buffs == null)
                throw new MissingMemberException("Preparation aura: installed game contract unavailable");
            c.SpawnMethod = Method(buff, "SpawnParticleEffect", typeof(void));
            c.DisposeMethod = Method(buff, "OnDispose", typeof(void));
            c.Spawn = Bind<Action<object>>(c.SpawnMethod);
            c.Clear = Bind<Action<object>>(Method(buff, "ClearParticleEffect", typeof(void)));
            c.FxRoot = Bind<Func<object>>(TouchSelectionCallFactory.ExactMethod(fx, "get_Instance", fx, true));
            c.PreparationReference = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(fx, "PreparationTurnVisualBuff", reference));
            c.ResolveBlueprint = Bind<Func<object, object>>(TouchSelectionCallFactory.ExactMethod(reference.BaseType, "op_Implicit", blueprint, true, reference.BaseType));
            c.Blueprint = Bind<Func<object, object>>(Method(buff, "get_Blueprint", blueprint));
            c.Disposed = Bind<Func<object, bool>>(Method(buff, "get_IsDisposed", typeof(bool)));
            c.Attached = Bind<Func<object, bool>>(Method(buff, "get_IsAttached", typeof(bool)));
            c.Active = Bind<Func<object, bool>>(Method(buff, "get_IsActive", typeof(bool)));
            c.Game = Bind<Func<object>>(TouchSelectionCallFactory.ExactMethod(game, "get_Instance", game, true));
            c.Player = Bind<Func<object, object>>(Method(game, "get_Player", player));
            c.Party = Bind<Func<object, object>>(Method(player, "get_PartyAndPets", typeof(List<>).MakeGenericType(unit)));
            c.Buffs = Bind<Func<object, object>>(Method(unit, "get_Buffs", buffs));
            c.GetBuff = Bind<Func<object, object, object>>(Method(buffs, "GetBuff", buff, blueprint));
            return c;
        }
        static MethodInfo Method(Type type, string name, Type result, params Type[] arguments) =>
            TouchSelectionCallFactory.ExactMethod(type, name, result, false, arguments);
        static T Bind<T>(MethodInfo method) where T : class => (T)(object)TouchSelectionCallFactory.Build(typeof(T), method);
    }
}
