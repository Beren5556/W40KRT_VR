namespace RTMaquetaXR
{
    internal static class TouchRadialFocusPolicy
    {
        internal static bool Center(TouchTabletopState table, Point3 unitPosition)
        {
            if (table == null || !table.Initialized || !TouchTabletopState.Finite(unitPosition)) return false;
            var snapshot = table.Capture();
            var previous = table.MapPoint(snapshot.PivotReference);
            table.Translate(unitPosition - previous);
            table.Cancel(true);
            return true;
        }
    }
}
