using System;
using System.Collections.Generic;
using HarmonyLib;

namespace RTMaquetaXR
{
    internal sealed class NativePetPreparationRecord
    {
        public string Utc,ExceptionType,Message,OwnerId,OwnerType,NodeType,GraphType,ProbeFailure;
        public bool? OwnerMissing,NodeMissing,NodeDestroyed,GraphMissing;
        public bool PositionRead;
        public float OwnerX,OwnerY,OwnerZ;
    }

    public static partial class Main
    {
        const int NativePetPreparationLimit=8;
        static NativePetPreparationContracts _nativePetPreparation;
        static readonly List<NativePetPreparationRecord> _nativePetPreparationRecords=new List<NativePetPreparationRecord>(NativePetPreparationLimit);
        static string _nativePetPreparationFailure;
        static long _nativePetPreparationErrors,_nativePetPreparationSuppressed;

        static void InstallNativePetPreparationDiagnostics()
        {
            _nativePetPreparation=null;_nativePetPreparationFailure=null;
            _nativePetPreparationErrors=_nativePetPreparationSuppressed=0;_nativePetPreparationRecords.Clear();
            try {
                var contract=NativePetPreparationContracts.Create(AccessTools.TypeByName);
                _touchHarmony.Patch(contract.Target,finalizer:new HarmonyMethod(typeof(Main),nameof(NativePetPreparationFinalizer)));
                _nativePetPreparation=contract;
            } catch(Exception error) {
                _nativePetPreparationFailure=error.Message;
                _log.Error("[diagnostics/pet] Optional preparation-failure probe unavailable; game behavior retained: "+error.Message);
            }
        }

        static Exception NativePetPreparationFinalizer(object __instance,Exception __exception)
        {
            // Normal preparation has no reflection, scene reads, timestamp,
            // allocation or counter updates. Never replace or swallow the
            // game's exception, including when collecting evidence fails.
            if(__exception==null||!DiagnosticsRecording||!TouchInputOwned||_nativePetPreparation==null)return __exception;
            ++_nativePetPreparationErrors;
            if(_nativePetPreparationRecords.Count>=NativePetPreparationLimit) { ++_nativePetPreparationSuppressed;return __exception; }
            try {
                var record=new NativePetPreparationRecord { Utc=DateTime.UtcNow.ToString("o"),ExceptionType=__exception.GetType().FullName,Message=__exception.Message };
                _nativePetPreparationRecords.Add(record);
                try {
                    var owner=_nativePetPreparation.Owner(__instance);record.OwnerMissing=ReferenceEquals(owner,null);
                    if(owner==null)return __exception;
                    record.OwnerType=owner.GetType().FullName;record.OwnerId=_nativePetPreparation.OwnerId(owner);
                    var position=_nativePetPreparation.Position(owner);
                    record.OwnerX=position.x;record.OwnerY=position.y;record.OwnerZ=position.z;record.PositionRead=true;
                    var node=_nativePetPreparation.CachedNode(owner);record.NodeMissing=ReferenceEquals(node,null);
                    if(node==null)return __exception;
                    record.NodeType=node.GetType().FullName;record.NodeDestroyed=_nativePetPreparation.Destroyed(node);
                    var graph=_nativePetPreparation.Graph(node);record.GraphMissing=ReferenceEquals(graph,null);
                    if(graph!=null)record.GraphType=graph.GetType().FullName;
                } catch(Exception probeError) { record.ProbeFailure=probeError.GetType().FullName+": "+probeError.Message; }
            } catch { /* Preserve the exact original exception even if recording fails. */ }
            return __exception;
        }

        static void StopNativePetPreparationDiagnostics()=>_nativePetPreparation=null;
        internal static object NativePetPreparationSnapshot()=>new {
            Ready=_nativePetPreparation!=null&&TouchInputOwned,Failure=_nativePetPreparationFailure,
            ErrorsObserved=_nativePetPreparationErrors,Suppressed=_nativePetPreparationSuppressed,
            EventLimit=NativePetPreparationLimit,Events=DetachedPetPreparationEvents(),
            Scope="Preparation failures only; cached node read without refresh; original exception, actions and unit positions retained"
        };
        static object[] DetachedPetPreparationEvents()
        {
            // The worker deliberately rejects mutable custom classes, even an
            // empty array of them. Copy fields into detached value snapshots on
            // the main thread; never hand the live records to the serializer.
            var events = new object[_nativePetPreparationRecords.Count];
            for (int i = 0; i < events.Length; ++i)
            {
                var record = _nativePetPreparationRecords[i];
                events[i] = new { record.Utc, record.ExceptionType, record.Message,
                    record.OwnerId, record.OwnerType, record.NodeType, record.GraphType, record.ProbeFailure,
                    record.OwnerMissing, record.NodeMissing, record.NodeDestroyed, record.GraphMissing,
                    record.PositionRead, record.OwnerX, record.OwnerY, record.OwnerZ };
            }
            return events;
        }
    }
}
