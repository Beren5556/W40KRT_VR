using System;
using System.Reflection;
using UnityEngine;

namespace RTMaquetaXR
{
    internal sealed class NativePetPreparationContracts
    {
        internal MethodInfo Target;
        internal Func<object,object> Owner,CachedNode,Graph;
        internal Func<object,string> OwnerId;
        internal Func<object,Vector3> Position;
        internal Func<object,bool> Destroyed;

        internal static NativePetPreparationContracts Create(Func<string,Type> lookup)
        {
            var part=lookup("Kingmaker.UnitLogic.Parts.UnitPartPetOwner");
            var unit=lookup("Kingmaker.EntitySystem.Entities.BaseUnitEntity");
            var mechanic=lookup("Kingmaker.EntitySystem.Entities.MechanicEntity");
            var node=lookup("Kingmaker.Pathfinding.CustomGridNode");
            var graphNode=lookup("Pathfinding.GraphNode");
            var graph=lookup("Pathfinding.NavGraph");
            return new NativePetPreparationContracts {
                Target=TouchSelectionCallFactory.ExactMethod(part,"HandleBeginPreparationTurn",typeof(void),false,typeof(bool)),
                Owner=(Func<object,object>)TouchSelectionCallFactory.Build(typeof(Func<object,object>),
                    TouchSelectionCallFactory.ExactMethod(part,"get_Owner",unit,false)),
                OwnerId=(Func<object,string>)TouchSelectionCallFactory.Build(typeof(Func<object,string>),
                    TouchSelectionCallFactory.ExactMethod(unit,"get_UniqueId",typeof(string),false)),
                Position=(Func<object,Vector3>)TouchSelectionCallFactory.Build(typeof(Func<object,Vector3>),
                    TouchSelectionCallFactory.ExactMethod(unit,"get_Position",typeof(Vector3),false)),
                // Do not call CurrentUnwalkableNode: that getter refreshes game
                // caches and could replace the evidence we want to inspect.
                CachedNode=TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(mechanic,"m_CurrentUnwalkableNode",node)),
                Graph=(Func<object,object>)TouchSelectionCallFactory.Build(typeof(Func<object,object>),
                    TouchSelectionCallFactory.ExactMethod(graphNode,"get_Graph",graph,false)),
                Destroyed=(Func<object,bool>)TouchSelectionCallFactory.Build(typeof(Func<object,bool>),
                    TouchSelectionCallFactory.ExactMethod(graphNode,"get_Destroyed",typeof(bool),false))
            };
        }
    }
}
