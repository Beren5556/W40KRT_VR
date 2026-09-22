using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly List<CombatRendererVisual> _combatRenderers = new List<CombatRendererVisual>();
        static readonly HashSet<Renderer> _combatRendererSet = new HashSet<Renderer>();
        static readonly CombatVisualRestoreQueue<CombatBlock> _combatPendingBlocks = new CombatVisualRestoreQueue<CombatBlock>();
        static readonly System.Action<CombatBlock> _combatRestoreBlock = RestoreCombatBlock;
        static readonly string[] CombatOpacityNames = { "_AlphaMulty", "_FinalAlphaMult", "_AlphaScale", "_BaseColor", "_Color", "_TintColor" };
        static int[] _combatOpacityIds;
        static long _combatAppliedBlocks;
        static int _combatUnsupportedMaterials;
        static Material _combatCircleMaterial;
        static int _combatCircleCount;

        static void TrackCombatRenderer(Renderer renderer, bool surface)
        {
            if (renderer == null || !_combatRendererSet.Add(renderer)) return;
            if (_combatOpacityIds == null)
            {
                _combatOpacityIds = new int[CombatOpacityNames.Length];
                for (int i = 0; i < _combatOpacityIds.Length; ++i) _combatOpacityIds[i] = Shader.PropertyToID(CombatOpacityNames[i]);
            }
            _combatRenderers.Add(new CombatRendererVisual(renderer, surface));
        }
        static void PruneCombatRenderers()
        {
            for (int i = _combatRenderers.Count - 1; i >= 0; --i)
                if (_combatRenderers[i].Renderer == null)
                { _combatRendererSet.Remove(_combatRenderers[i].Renderer); _combatRenderers.RemoveAt(i); }
        }
        static void ApplyCombatBlocks()
        {
            float surface = CombatVisualPolicy.Intensity(_cfg.combatSurfaceIntensity, .25f);
            float marks = CombatVisualPolicy.Intensity(_cfg.combatUnitFxIntensity, .25f);
            if (surface >= 1f && marks >= 1f) return;
            _combatUnsupportedMaterials = 0;
            foreach (var renderer in _combatRenderers) renderer.Apply(renderer.Surface ? surface : marks);
        }
        static void RestoreCombatBlocks()
        {
            // Reverse order, including a partially prepared eye after failure.
            _combatPendingBlocks.RestoreAll(_combatRestoreBlock);
        }
        static void RestoreCombatBlock(CombatBlock block)
        {
            if (block.Renderer != null) block.Renderer.SetPropertyBlock(block.WasEmpty ? null : block.Saved, block.Index);
        }
        sealed class CombatBlock
        {
            internal readonly Renderer Renderer;
            internal readonly int Index;
            internal readonly MaterialPropertyBlock Saved = new MaterialPropertyBlock(), Applied = new MaterialPropertyBlock();
            internal bool WasEmpty;
            internal Material Material;
            internal Shader Shader;
            internal int Property = -1;
            internal CombatBlock(Renderer renderer, int index) { Renderer = renderer; Index = index; }
            internal void Apply(Material material, float intensity)
            {
                if (Material != material || Shader != material.shader)
                {
                    Material = material; Shader = material.shader;
                    Property = CombatVisualPolicy.OpacityProperty(material.HasProperty(_combatOpacityIds[0]), material.HasProperty(_combatOpacityIds[1]),
                        material.HasProperty(_combatOpacityIds[2]), material.HasProperty(_combatOpacityIds[3]), material.HasProperty(_combatOpacityIds[4]), material.HasProperty(_combatOpacityIds[5]));
                }
                if (Property < 0) { ++_combatUnsupportedMaterials; return; }
                Saved.Clear(); Applied.Clear();
                Renderer.GetPropertyBlock(Saved, Index); Renderer.GetPropertyBlock(Applied, Index);
                WasEmpty = Saved.isEmpty;
                // An indexed MPB takes precedence over the renderer-wide MPB.
                // Preserve that wide block when introducing our first indexed one.
                if (WasEmpty) Renderer.GetPropertyBlock(Applied);
                int id = _combatOpacityIds[Property];
                if (Property < 3)
                {
                    float original = Applied.HasFloat(id) ? Applied.GetFloat(id) : material.GetFloat(id);
                    Applied.SetFloat(id, original * intensity);
                }
                else
                {
                    Color color = Applied.HasColor(id) ? Applied.GetColor(id) : material.GetColor(id);
                    color.a *= intensity; Applied.SetColor(id, color);
                }
                // Save before invoking the engine setter: even an exceptional
                // partial application is restored by the outer camera handler.
                _combatPendingBlocks.Add(this);
                Renderer.SetPropertyBlock(Applied, Index); ++_combatAppliedBlocks;
            }
        }
        sealed class CombatRendererVisual
        {
            internal readonly Renderer Renderer;
            internal readonly bool Surface;
            readonly List<Material> materials = new List<Material>();
            readonly List<CombatBlock> blocks = new List<CombatBlock>();
            internal CombatRendererVisual(Renderer renderer, bool surface) { Renderer = renderer; Surface = surface; }
            internal void Apply(float intensity)
            {
                if (intensity >= 1f || Renderer == null || !Renderer.enabled || !Renderer.gameObject.activeInHierarchy) return;
                // The game may replace submaterials while displaying a new AoE.
                // Reuse the list and per-slot blocks; never clone shared materials.
                materials.Clear(); Renderer.GetSharedMaterials(materials);
                for (int i = 0; i < materials.Count; ++i)
                {
                    if (i == blocks.Count) blocks.Add(new CombatBlock(Renderer, i));
                    if (materials[i] != null) blocks[i].Apply(materials[i], intensity);
                }
            }
        }

        sealed class CombatUnitVisual
        {
            readonly Component owner, view;
            readonly object highlighter;
            readonly CapsuleCollider capsule;
            LineRenderer circle;
            bool visible;
            float circleRadius = -1;
            Color circleColor;
            internal bool Alive => owner != null && view != null && highlighter is UnityEngine.Object o && o != null;
            internal CombatUnitVisual(Component owner, Component view, object highlighter)
            {
                this.owner = owner; this.view = view; this.highlighter = highlighter;
                capsule = view.GetComponent<CapsuleCollider>();
            }
            internal void Update(int mode)
            {
                visible = false;
                if (mode != 2 && mode != 3) { Show(false); return; }
                // Native DoUpdate continues its transitions. Rendering normally
                // refreshes colors, but that branch is skipped with empty infos.
                // Keep those presentation colors current without touching Play,
                // selection, faction, turn order or interaction controllers.
                _combatContracts.UpdateColors(highlighter);
                Color color = _combatContracts.Color(highlighter);
                // The highlight transition is a click/hover animation, not the
                // selection state. This native flag is maintained by SelectUnit,
                // MultiSelect and UnselectUnit, including keyboard/party changes.
                // Bound delegates read it directly: no manager scan or reflection.
                object entity = _combatContracts.ViewEntity(view);
                bool selected = entity != null && _combatContracts.Selected(entity);
                visible = CombatVisualPolicy.PersistentCircleVisible(_combatActive, mode, owner.gameObject.activeInHierarchy, selected,
                    _combatContracts.Transition(highlighter), color.a);
                if (!visible) { Show(false); return; }
                if (selected) { color = LiveAccent; color.a = 1; }
                if (circle == null) CreateCircle();
                Vector3 center = view.transform.position;
                float radius = .45f;
                if (capsule != null && capsule.enabled)
                {
                    Bounds bounds = capsule.bounds;
                    center = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                    radius = CombatVisualPolicy.Radius(Mathf.Max(bounds.extents.x, bounds.extents.z));
                }
                center.y += .025f;
                if (Mathf.Abs(circleRadius - radius) > .001f)
                {
                    circleRadius = radius;
                    for (int i = 0; i < 48; ++i)
                    {
                        float angle = i * Mathf.PI * 2f / 48;
                        circle.SetPosition(i, new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius);
                    }
                    circle.startWidth = circle.endWidth = Mathf.Clamp(radius * .045f, .018f, .07f);
                }
                if ((circle.transform.position - center).sqrMagnitude > .00000001f) circle.transform.position = center;
                if (circleColor != color) { circleColor = color; circle.startColor = circle.endColor = color; }
                circle.enabled = false; // Only exact eye callbacks expose it.
            }
            void CreateCircle()
            {
                if (_combatCircleMaterial == null)
                {
                    _combatCircleMaterial = CreateLiveUiMaterial("RTMaquetaXR native highlight base circle");
                    _combatCircleMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
                    SetLiveMaterialInt(_combatCircleMaterial, "_ZTest", (int)CompareFunction.LessEqual);
                    SetLiveMaterialInt(_combatCircleMaterial, "_ZTestMode", (int)CompareFunction.LessEqual);
                }
                var go = new GameObject("RTMaquetaXR unit highlight base circle", typeof(LineRenderer));
                go.layer = 5; UnityEngine.Object.DontDestroyOnLoad(go);
                circle = go.GetComponent<LineRenderer>(); circle.enabled = false;
                ++_combatCircleCount;
                circle.sharedMaterial = _combatCircleMaterial; circle.useWorldSpace = false;
                circle.loop = true; circle.positionCount = 48;
                circle.shadowCastingMode = ShadowCastingMode.Off; circle.receiveShadows = false;
                circle.lightProbeUsage = LightProbeUsage.Off; circle.reflectionProbeUsage = ReflectionProbeUsage.Off;
                circle.generateLightingData = false;
            }
            internal void Show(bool eye) { if (circle != null) circle.enabled = visible && eye; }
            internal void Dispose()
            {
                if (!ReferenceEquals(circle, null)) --_combatCircleCount;
                if (circle != null) UnityEngine.Object.Destroy(circle.gameObject);
                circle = null; visible = false;
            }
        }
    }
}
