using System;

namespace RTMaquetaXR
{
    // Every retained hitch is a complete value copy. No arrays, dictionaries,
    // delegates or references to the live engine/sample ring reach the writer.
    internal readonly struct EngineLoopPhaseEvidence
    {
        public readonly double CpuWallMs;
        public readonly int Invocations;
        internal EngineLoopPhaseEvidence(EngineLoopSamples samples, int frame, int phase)
        { CpuWallMs = Math.Round(samples.Time(frame, phase), 4); Invocations = samples.Calls(frame, phase); }
    }
    internal readonly struct EngineLoopPhaseValues
    {
        public readonly EngineLoopPhaseEvidence ExecuteMainThreadJobs, UpdatePreloading,
            ScriptRunBehaviourFixedUpdate, PhysicsFixedUpdate, PhysicsUpdate,
            ScriptRunBehaviourUpdate, DirectorUpdate, ScriptRunDelayedTasks,
            DirectorUpdateAnimationBegin, DirectorUpdateAnimationEnd, LegacyAnimationUpdate,
            ParticleSystemBeginUpdateAll, ScriptRunBehaviourLateUpdate, EndGraphicsJobsAfterScriptUpdate,
            PlayerUpdateCanvases, ParticleSystemEndUpdateAll, EndGraphicsJobsAfterScriptLateUpdate,
            VFXUpdate, UpdateAllRenderers, UpdateAllSkinnedMeshes, PlayerEmitCanvasGeometry,
            FinishFrameRendering, PresentAfterDraw;
        internal EngineLoopPhaseValues(EngineLoopSamples samples, int frame)
        {
            ExecuteMainThreadJobs = new EngineLoopPhaseEvidence(samples, frame, 0);
            UpdatePreloading = new EngineLoopPhaseEvidence(samples, frame, 1);
            ScriptRunBehaviourFixedUpdate = new EngineLoopPhaseEvidence(samples, frame, 2);
            PhysicsFixedUpdate = new EngineLoopPhaseEvidence(samples, frame, 3);
            PhysicsUpdate = new EngineLoopPhaseEvidence(samples, frame, 4);
            ScriptRunBehaviourUpdate = new EngineLoopPhaseEvidence(samples, frame, 5);
            DirectorUpdate = new EngineLoopPhaseEvidence(samples, frame, 6);
            ScriptRunDelayedTasks = new EngineLoopPhaseEvidence(samples, frame, 7);
            DirectorUpdateAnimationBegin = new EngineLoopPhaseEvidence(samples, frame, 8);
            DirectorUpdateAnimationEnd = new EngineLoopPhaseEvidence(samples, frame, 9);
            LegacyAnimationUpdate = new EngineLoopPhaseEvidence(samples, frame, 10);
            ParticleSystemBeginUpdateAll = new EngineLoopPhaseEvidence(samples, frame, 11);
            ScriptRunBehaviourLateUpdate = new EngineLoopPhaseEvidence(samples, frame, 12);
            EndGraphicsJobsAfterScriptUpdate = new EngineLoopPhaseEvidence(samples, frame, 13);
            PlayerUpdateCanvases = new EngineLoopPhaseEvidence(samples, frame, 14);
            ParticleSystemEndUpdateAll = new EngineLoopPhaseEvidence(samples, frame, 15);
            EndGraphicsJobsAfterScriptLateUpdate = new EngineLoopPhaseEvidence(samples, frame, 16);
            VFXUpdate = new EngineLoopPhaseEvidence(samples, frame, 17);
            UpdateAllRenderers = new EngineLoopPhaseEvidence(samples, frame, 18);
            UpdateAllSkinnedMeshes = new EngineLoopPhaseEvidence(samples, frame, 19);
            PlayerEmitCanvasGeometry = new EngineLoopPhaseEvidence(samples, frame, 20);
            FinishFrameRendering = new EngineLoopPhaseEvidence(samples, frame, 21);
            PresentAfterDraw = new EngineLoopPhaseEvidence(samples, frame, 22);
        }
    }
    internal readonly struct EngineLoopFrameEvidence
    {
        public readonly int Frame;
        public readonly bool Available;
        public readonly EngineLoopPhaseValues Phases;
        internal EngineLoopFrameEvidence(EngineLoopSamples samples, int frame)
        { Frame = frame; Available = samples.HasFrame(frame); Phases = new EngineLoopPhaseValues(samples, frame); }
    }
}
