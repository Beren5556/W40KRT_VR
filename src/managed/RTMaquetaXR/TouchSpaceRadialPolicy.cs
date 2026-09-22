using System;

namespace RTMaquetaXR
{
    internal struct TouchSpaceRadialContext
    {
        internal object Area, View, Model, Turn, Unit, SelectedUnit, PlayerShip, Weapons, Posts;
        internal int Round;
        internal bool PlayerTurn, Ending, Locked, Exit;
        internal bool Ready => Area != null && View != null && Model != null && Turn != null && Unit != null;
        internal bool CanAct => Ready && PlayerTurn && !Ending && !Locked && !Exit && ReferenceEquals(Unit, SelectedUnit);
        internal bool MainShip => Unit != null && ReferenceEquals(Unit, PlayerShip);
        internal bool Same(TouchSpaceRadialContext other) => Ready && other.Ready &&
            ReferenceEquals(Area, other.Area) && ReferenceEquals(View, other.View) && ReferenceEquals(Model, other.Model) &&
            ReferenceEquals(Turn, other.Turn) && ReferenceEquals(Unit, other.Unit) && ReferenceEquals(SelectedUnit, other.SelectedUnit) &&
            ReferenceEquals(PlayerShip, other.PlayerShip) && ReferenceEquals(Weapons, other.Weapons) && ReferenceEquals(Posts, other.Posts) &&
            Round == other.Round && PlayerTurn == other.PlayerTurn && Ending == other.Ending && Locked == other.Locked && Exit == other.Exit;
    }
}
