using System;

namespace RTMaquetaXR
{
    // A Unity component can outlive its native binding (pooled view). Reference
    // validity is not binding validity. A fault is local to this binding only.
    internal sealed class PresentationBinding75
    {
        internal object Model { get; private set; }
        internal long Revision { get; private set; }
        internal bool Faulted { get; private set; }
        internal void Bind(object model) { Model=model; Faulted=false; ++Revision; }
        internal void Retire() { Model=null; Faulted=false; ++Revision; }
        internal void Fault() { Faulted=true; }
        internal bool IsCurrent(object current) => !Faulted && Model!=null && ReferenceEquals(Model,current);
    }
}
