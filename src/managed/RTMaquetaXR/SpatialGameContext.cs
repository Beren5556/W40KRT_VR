using System;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    internal enum SpatialGameDomain { Terrestrial, GalacticMap, StarSystemMap, SpaceCombat }

    internal static class SpatialGameContextPolicy
    {
        internal static SpatialGameDomain Domain(string areaMode, string mode, bool gameAvailable)
        {
            if (!gameAvailable || mode == "None" || mode == "MainMenu") return SpatialGameDomain.Terrestrial;
            // Area identity outlives Pause, Dialogue, tutorials and native windows.
            // Never retain a last spatial mode after a different area is loaded.
            string value = mode == "GlobalMap" || mode == "StarSystem" || mode == "SpaceCombat" ? mode : areaMode ?? mode;
            return value == "GlobalMap" ? SpatialGameDomain.GalacticMap :
                value == "StarSystem" ? SpatialGameDomain.StarSystemMap :
                value == "SpaceCombat" ? SpatialGameDomain.SpaceCombat : SpatialGameDomain.Terrestrial;
        }
    }

    internal sealed class SpatialGameContextContracts
    {
        internal Func<object> Game;
        internal Func<object, object> Area, Turn, CurrentUnit, Player, PlayerShip, View;
        internal Func<object, object> AreaMode, Mode, ModeName;
        internal Func<object, Vector3> Position;
        internal Type ModeType;
        internal static SpatialGameContextContracts Create(Func<string, Type> lookup)
        {
            var game = lookup("Kingmaker.Game"); var area = lookup("Kingmaker.Blueprints.Area.BlueprintArea");
            var mode = lookup("Kingmaker.GameModes.GameModeType");
            return new SpatialGameContextContracts {
                ModeType = mode,
                Game = (Func<object>)TouchSelectionCallFactory.Build(typeof(Func<object>), TouchSelectionCallFactory.ExactMethod(game, "get_Instance", game, true)),
                Area = Getter(game, "get_CurrentlyLoadedArea", area),
                AreaMode = Getter(area, "get_AreaStatGameMode", mode),
                Mode = Getter(game, "get_CurrentMode", mode), ModeName = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(mode, "Name", typeof(string)))
            };
        }
        internal void EnsureCamera(Func<string, Type> lookup)
        {
            if (Position != null) return;
            var game = lookup("Kingmaker.Game"); var player = lookup("Kingmaker.Player");
            var turn = lookup("Kingmaker.Controllers.TurnBased.TurnController");
            var unit = lookup("Kingmaker.EntitySystem.Entities.MechanicEntity");
            var ship = lookup("Kingmaker.EntitySystem.Entities.StarshipEntity");
            var view = lookup("Kingmaker.View.Mechanics.MechanicEntityView");
            Turn = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(game, "TurnController", turn));
            CurrentUnit = Getter(turn, "get_CurrentUnit", unit); Player = Getter(game, "get_Player", player);
            PlayerShip = Getter(player, "get_PlayerShip", ship); View = Getter(unit, "get_View", view);
            Position = (Func<object, Vector3>)TouchSelectionCallFactory.Build(typeof(Func<object, Vector3>), TouchSelectionCallFactory.ExactMethod(unit, "get_Position", typeof(Vector3), false));
        }
        static Func<object, object> Getter(Type owner, string name, Type result) =>
            (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), TouchSelectionCallFactory.ExactMethod(owner, name, result, false));
    }

    public static partial class Main
    {
        static SpatialGameContextContracts _spatialGameContracts;
        static SpatialGameDomain _spatialGameDomain;
        static object _spatialGameArea;
        static int _spatialGameFrame = -1; static object _spatialGameModeValue;
        static string _spatialGameMode, _spatialGameAreaMode, _spatialGameContextFault;
        static long _spatialGameRevision;
        static bool _spatialGameBindAttempted;
        internal static bool InNavigationMap { get { RefreshSpatialGameContext(); return _spatialGameDomain == SpatialGameDomain.GalacticMap || _spatialGameDomain == SpatialGameDomain.StarSystemMap; } }
        internal static bool InGalacticMap { get { RefreshSpatialGameContext(); return _spatialGameDomain == SpatialGameDomain.GalacticMap; } }
        internal static bool InStarSystemMap { get { RefreshSpatialGameContext(); return _spatialGameDomain == SpatialGameDomain.StarSystemMap; } }
        internal static bool InSpaceCombat { get { RefreshSpatialGameContext(); return _spatialGameDomain == SpatialGameDomain.SpaceCombat; } }
        internal static void RefreshSpatialGameContext()
        {
            if (_spatialGameFrame == Time.frameCount) return;
            _spatialGameFrame = Time.frameCount;
            try
            {
                if (!_spatialGameBindAttempted) { _spatialGameBindAttempted = true; _spatialGameContracts = SpatialGameContextContracts.Create(AccessTools.TypeByName); }
                if (_spatialGameContracts == null) return;
                var c = _spatialGameContracts; object game = c.Game(); object area = game == null ? null : c.Area(game);
                if (!ReferenceEquals(area, _spatialGameArea))
                {
                    _spatialGameArea = area; ++_spatialGameRevision;
                    var areaMode = area == null ? null : c.AreaMode(area);
                    _spatialGameAreaMode = areaMode == null ? null : c.ModeName(areaMode) as string;
                }
                object mode = game == null ? null : c.Mode(game);
                if (!ReferenceEquals(mode, _spatialGameModeValue)) { _spatialGameModeValue = mode; _spatialGameMode = mode == null ? null : c.ModeName(mode) as string; }
                var domain = SpatialGameContextPolicy.Domain(_spatialGameAreaMode, _spatialGameMode, game != null);
                if (domain != _spatialGameDomain) { _spatialGameDomain = domain; ++_spatialGameRevision; }
            }
            catch (Exception error)
            {
                _spatialGameDomain = SpatialGameDomain.Terrestrial;
                if (_spatialGameContextFault != error.Message) { _spatialGameContextFault = error.Message; _log?.Error("[space/context] " + error.Message); }
            }
        }
        internal static long SpatialGameContextRevision { get { RefreshSpatialGameContext(); return _spatialGameRevision; } }
        internal static string SpatialGameMode { get { RefreshSpatialGameContext(); return _spatialGameMode; } }
        static void TouchSpatialAreaActivated() { _spatialGameFrame = -1; ++_spatialGameRevision; }
        internal static object SpatialGameContextSnapshot() { RefreshSpatialGameContext(); return new {
            Domain = _spatialGameDomain.ToString(), AreaMode = _spatialGameAreaMode, Mode = _spatialGameMode,
            Revision = _spatialGameRevision, Error = _spatialGameContextFault
        }; }
    }
}



