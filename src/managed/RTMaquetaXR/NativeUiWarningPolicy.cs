using System;
using System.Collections.Generic;

namespace RTMaquetaXR
{
    // Bounded, per-VR-session de-duplication of one native diagnostic. This
    // policy cannot skip binding registration, callbacks or disposable tokens.
    internal sealed class NativeUiWarningPolicy
    {
        internal const int Capacity=64;
        readonly Dictionary<string,long> counts=new Dictionary<string,long>(StringComparer.Ordinal);
        internal long Emitted, Suppressed;
        internal bool ShouldEmit(bool active,string name)
        {
            if(!active||string.IsNullOrEmpty(name)||name.Length>128)return true;
            if(counts.TryGetValue(name,out long n))
            { counts[name]=n==long.MaxValue?n:n+1; if(Suppressed<long.MaxValue)++Suppressed; return false; }
            if(Emitted<long.MaxValue)++Emitted;
            if(counts.Count<Capacity)counts.Add(name,1);
            return true;
        }
        internal Dictionary<string,long> Snapshot()=>new Dictionary<string,long>(counts,StringComparer.Ordinal);
        internal void Reset(){counts.Clear();Emitted=Suppressed=0;}
    }
}
