namespace RTMaquetaXR
{
    // While reading a grip-held card, a short opposite trigger still uses its
    // action; a full four-button hold opens settings. Defer the short click
    // until release so the opening chord cannot execute an ability first.
    internal sealed class TouchRadialSettingsChordPolicy
    {
        internal bool Deferring { get; private set; }
        internal bool Pulse { get; private set; }
        bool pending;
        int target;
        double started,last;
        internal void Step(bool wheel, bool lt, bool lg, bool rt, bool rg, bool valid, int pointed, double now)
        {
            Deferring=Pulse=false;
            if(!valid || !wheel || !lt || !lg || double.IsNaN(now) || double.IsInfinity(now) || (pending&&(now<last||now-last>.3)))
            {pending=false;return;}
            last=now;
            bool all=rt&&rg;
            if(!pending && all){pending=true;started=now;target=pointed;}
            if(!pending)return;
            if(pointed!=target)target=-1; // Do not use an option reached mid-press.
            if(all && now-started<TouchOverlayChordPolicy.HoldSeconds){Deferring=true;return;}
            if(!all && rg && !rt && now-started<TouchOverlayChordPolicy.HoldSeconds)
            {Deferring=true;Pulse=target>=0 && pointed==target;}
            pending=false;
        }
    }
}
