using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    internal struct TacticalAreaCell70
    {
        internal int X, Y;
        internal Color Color;
        internal string Label;
    }

    internal sealed class TacticalAreaGraphic70 : MaskableGraphic
    {
        readonly List<TacticalAreaCell70> cells = new List<TacticalAreaCell70>();
        int minX, maxX, minY, maxY;

        internal void SetCells(List<TacticalAreaCell70> value)
        {
            cells.Clear();
            if (value != null) cells.AddRange(value);
            minX = minY = int.MaxValue; maxX = maxY = int.MinValue;
            foreach (var cell in cells)
            { minX = Math.Min(minX, cell.X); maxX = Math.Max(maxX, cell.X); minY = Math.Min(minY, cell.Y); maxY = Math.Max(maxY, cell.Y); }
            SetVerticesDirty();
        }

        internal bool TryCellRect(int index, out Rect result)
        {
            result=default(Rect);if(index<0||index>=cells.Count||cells.Count==0)return false;
            Rect area=GetPixelAdjustedRect();int columns=Math.Max(1,maxX-minX+1),rows=Math.Max(1,maxY-minY+1);
            float side=Mathf.Min(area.width/columns,area.height/rows),ox=area.x+(area.width-side*columns)*.5f,oy=area.y+(area.height-side*rows)*.5f;
            var cell=cells[index];result=new Rect(ox+(cell.X-minX)*side,oy+(maxY-cell.Y)*side,side,side);return true;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (cells.Count == 0) return;
            Rect area = GetPixelAdjustedRect();
            int columns = Math.Max(1, maxX - minX + 1), rows = Math.Max(1, maxY - minY + 1);
            float side = Mathf.Min(area.width / columns, area.height / rows), ox = area.x + (area.width - side * columns) * .5f;
            float oy = area.y + (area.height - side * rows) * .5f;
            foreach (var cell in cells)
            {
                float x = ox + (cell.X - minX) * side, y = oy + (maxY - cell.Y) * side;
                AddQuad(vh, new Rect(x + 1, y + 1, Math.Max(1, side - 2), Math.Max(1, side - 2)), cell.Color);
            }
        }

        static void AddQuad(VertexHelper vh, Rect rect, Color color)
        {
            int start = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert; v.color = color;
            v.position = new Vector3(rect.xMin, rect.yMin); vh.AddVert(v);
            v.position = new Vector3(rect.xMin, rect.yMax); vh.AddVert(v);
            v.position = new Vector3(rect.xMax, rect.yMax); vh.AddVert(v);
            v.position = new Vector3(rect.xMax, rect.yMin); vh.AddVert(v);
            vh.AddTriangle(start, start + 1, start + 2); vh.AddTriangle(start + 2, start + 3, start);
        }
    }

    public static partial class Main
    {
        sealed class WorldInformationSource70
        {
            internal Component View;
            internal Transform SourceRoot72;
            internal object ViewModel;
            internal readonly PresentationBinding75 Binding75=new PresentationBinding75();
            internal int Family;
            internal readonly List<CanvasRenderer> Renderers = new List<CanvasRenderer>(24);
            internal readonly List<Graphic> Texts = new List<Graphic>(16);
            internal readonly List<Graphic> BarkTexts = new List<Graphic>(4);
            internal readonly List<CanvasRenderer> InteractionTextRenderers = new List<CanvasRenderer>(8);
            internal readonly List<Component> Parts = new List<Component>(32);
            internal readonly List<Graphic> InteractionGraphics = new List<Graphic>(8);
            internal readonly List<Graphic> TacticalGraphics72 = new List<Graphic>(16);
            internal Component InteractionButton72;
            internal Graphic InteractionImage72;
            internal RectTransform InteractionRect72;
            internal bool ProximityOnly73,ProximityClassified73;
            internal object Unit;
            internal Transform UnitView;
            internal int DirectChildren;
            internal int PresentationState74=-1;
            internal readonly List<object> OccupiedNodes = new List<object>();
            internal object OccupiedAt;
            internal float OccupiedOrientation = float.NaN;
        }

        static readonly Dictionary<Component, WorldInformationSource70> _worldInformationSources70 = new Dictionary<Component, WorldInformationSource70>();
        static readonly Dictionary<Transform, WorldInformationSource70> _legacyWorldInformation70 = new Dictionary<Transform, WorldInformationSource70>();
        static readonly Dictionary<CanvasRenderer, bool> _worldInformationCull70 = new Dictionary<CanvasRenderer, bool>();
        static readonly HashSet<Graphic> _interactionGraphics70 = new HashSet<Graphic>();
        static readonly Dictionary<Graphic, GameObject> _interactionTargets70 = new Dictionary<Graphic, GameObject>();
        static readonly Dictionary<Transform, int> _legacyInteractionRoots70 = new Dictionary<Transform, int>();
        static readonly Dictionary<Transform, List<Graphic>> _legacyInteractionGraphics70 = new Dictionary<Transform, List<Graphic>>();
        static readonly List<Graphic> _interactionDead70 = new List<Graphic>(16);
        static readonly List<Transform> _legacyWorldDead70 = new List<Transform>(16);
        static readonly Dictionary<Type, Dictionary<string, FieldInfo>> _worldFields70 = new Dictionary<Type, Dictionary<string, FieldInfo>>();
        static MethodInfo _occupiedNodesMethod70;
        static readonly List<CanvasRenderer> _worldRenderersWork70 = new List<CanvasRenderer>(64);
        static readonly List<Graphic> _worldTextsWork70 = new List<Graphic>(32);
        static readonly List<Component> _worldPartsWork70 = new List<Component>(64);
        static readonly List<string> _worldLines70 = new List<string>(24);
        static readonly HashSet<string> _worldLineSet70 = new HashSet<string>(StringComparer.Ordinal);
        static readonly List<string> _tacticalNativeLines70 = new List<string>(16);
        static readonly HashSet<string> _tacticalNativeSet70 = new HashSet<string>(StringComparer.Ordinal);
        static readonly List<TacticalAreaCell70> _tacticalCells70 = new List<TacticalAreaCell70>(96);
        static readonly List<object> _tacticalNodes70 = new List<object>(96);

        static GameObject _stableInteractionTarget70;

        static long _interactionRegistryBuilds70, _interactionRegistryReads70, _sourceHierarchyRebuilds70, _suppressedWorldDraws70;
        static long _panelContentChanges70, _patternChanges70, _interactionTargetChanges70;
        static readonly HashSet<WorldInformationSource70> _tacticalInspectionSources71 = new HashSet<WorldInformationSource70>();
        static bool _tacticalInspectionHeld71;
        static float _interactionRefreshNext71;

        static GameObject _worldInformationRoot70;
        static Canvas _worldInformationCanvas70;
        static CanvasGroup _worldInformationGroup70;
        static Text _worldDialogueText70, _worldTacticalText70, _worldGridLabels70;
        static readonly List<Text> _worldGridCellLabels70=new List<Text>(24);
        static TacticalAreaGraphic70 _worldGrid70;
        static Image _worldInformationBackdrop70, _worldInformationDivider70;
        static bool _worldInformationVisible70;
        static string _lastDialogue70, _lastTactical70, _lastGridLabels70;
        static Sprite _worldDialogueBackdropSprite71;
        static Color _worldDialogueBackdropColor71=new Color(.015f,.035f,.03f,.91f),_worldDialogueTextColor71=Color.white;
        static Font _worldDialogueFont71;
        static object _lastTacticalPattern70;
        static Component _nativeTacticalPreview70;
        static float _lastTacticalPatternAt70;
        static int _lastTacticalPatternHash70;
        static Vector3 _lastTacticalDirection70;
        static int _patternOriginX70,_patternOriginY70;
        static object _builtTacticalPattern70;
        static Vector3 _builtTacticalDirection70;
        static int _builtTacticalNativeSignature70;
        static string _cachedGridLabels70;
        static int _gridContentRevision70,_gridAppliedRevision70=-1;

        internal static object WorldInformationSnapshot70() => new {
            Presentation74 = CombatPresentationSnapshot74(),
            Sources = _worldInformationSources70.Count + _legacyWorldInformation70.Count,
            InteractionGraphics = _interactionGraphics70.Count,
            InteractionRegistryBuilds = _interactionRegistryBuilds70,
            InteractionRegistryReads = _interactionRegistryReads70,
            SourceHierarchyRebuilds = _sourceHierarchyRebuilds70,
            SuppressedWorldDraws = _suppressedWorldDraws70,
            PanelChanges = _panelContentChanges70,
            PatternChanges = _patternChanges70,
            InteractionTargetChanges = _interactionTargetChanges70,
            InspectionHeld = _tacticalInspectionHeld71, InspectionSources = _tacticalInspectionSources71.Count,
            InspectionStarts = _worldInspectionStarts72, InspectionStops = _worldInspectionStops72,
            InspectionReconciles = _worldInspectionReconciles72, TacticalFinalDraws = _worldTacticalDraws72,
            TacticalNativeFallback = _worldTacticalFallback72, HiddenRebuildsSkipped = _worldRebuildsSkipped72,
            VisibilityTransitions = _worldVisibilityTransitions72,
            Visible = _worldInformationVisible70,
            Path = "Interaction icons remain world-space; dialogue uses one fixed final-resolution panel; native tactical information is revealed only while Y is held"
        };

        static void RegisterWorldInformationSource70(Component view, object vm, bool nativeBind75=false)
        {
            if (view == null) return;
            int family = WorldHudPolicy.ProtectedFamily(view.GetType());
            if (family == 0) return;
            if (_worldInformationSources70.TryGetValue(view, out var source))
            {
                if (!ReferenceEquals(source.ViewModel,vm)||nativeBind75)
                { RemovePresentationSource74(source); source.ViewModel = vm;RegisterPresentationSource74(source);source.ProximityClassified73=false;source.ProximityOnly73=false; ResolveWorldInformationUnit70(source); RefreshWorldInformationSourceHierarchy70(source); }
                if ((source.Family & WorldHudPolicy.InteractionFamily) != 0 && source.DirectChildren != view.transform.childCount)
                    RefreshWorldInformationSourceHierarchy70(source);
                return;
            }
            source = BuildWorldInformationSource70(view, vm, family);
            _worldInformationSources70.Add(view, source);
            if ((family & WorldHudPolicy.InteractionFamily) != 0)
                RefreshInteractionSource70(source);
            RegisterWorldGraphics72(source);
            RegisterPresentationSource74(source);
        }

        static void RefreshInteractionSource70(WorldInformationSource70 source)
        {
            if(source==null||source.View==null)return;
            RegisterInteractionTarget72(source);
            foreach(var graphic in source.InteractionGraphics)
            {_interactionGraphics70.Remove(graphic);_interactionTargets70.Remove(graphic);}
            ReleaseCoverSource76(source);
            source.InteractionGraphics.Clear();source.InteractionTextRenderers.Clear();source.DirectChildren=source.View.transform.childCount;
            object buttonValue=ReadField70(source.View,"m_Button");
            Component nativeButton=buttonValue as Component;GameObject nativeButtonObject=buttonValue as GameObject;
            Transform buttonRoot=nativeButton!=null?nativeButton.transform:nativeButtonObject==null?null:nativeButtonObject.transform;
            foreach(var graphic in source.Texts)
            {
                if(graphic==null)continue;
                bool textual=graphic.GetType().Name.IndexOf("Text",StringComparison.OrdinalIgnoreCase)>=0;
                if(textual)
                { if(graphic.canvasRenderer!=null)source.InteractionTextRenderers.Add(graphic.canvasRenderer);continue; }
                if(buttonRoot!=null&&!graphic.transform.IsChildOf(buttonRoot)&&graphic.transform!=buttonRoot)continue;
                source.InteractionGraphics.Add(graphic);
                if(_interactionGraphics70.Add(graphic))++_interactionRegistryBuilds70;
                GameObject exact=nativeButton!=null?nativeButton.gameObject:nativeButtonObject;
                _interactionTargets70[graphic]=exact??CanonicalInteractionTarget70(graphic,source.View);
            }
            ++_sourceHierarchyRebuilds70;
        }

        static void RefreshWorldInformationSourceHierarchy70(WorldInformationSource70 source)
        {
            if(source==null||source.View==null)return;
            ReleaseCoverSource76(source);
            source.DirectChildren=source.View.transform.childCount;
            source.Renderers.Clear();source.Texts.Clear();source.BarkTexts.Clear();source.InteractionTextRenderers.Clear();source.Parts.Clear();
            source.View.GetComponentsInChildren(true,source.Renderers);
            source.View.GetComponentsInChildren(true,source.Texts);
            source.View.GetComponentsInChildren(true,source.Parts);
            foreach(var part in source.Parts)
            {
                if(part==null||part.GetType().Name.IndexOf("Bark",StringComparison.OrdinalIgnoreCase)<0)continue;
                _worldTextsWork70.Clear();part.GetComponentsInChildren(true,_worldTextsWork70);
                foreach(var text in _worldTextsWork70)if(text!=null&&!source.BarkTexts.Contains(text))source.BarkTexts.Add(text);
            }
            if((source.Family&WorldHudPolicy.InteractionFamily)!=0)RefreshInteractionSource70(source);
            else ++_sourceHierarchyRebuilds70;
            RegisterWorldGraphics72(source);
        }

        static WorldInformationSource70 BuildWorldInformationSource70(Component view, object vm, int family)
        {
            var source = new WorldInformationSource70 { View = view, SourceRoot72 = view.transform, ViewModel = vm, Family = family };
            source.DirectChildren=view.transform.childCount;
            view.GetComponentsInChildren(true, source.Renderers);
            view.GetComponentsInChildren(true, source.Texts);
            view.GetComponentsInChildren(true, source.Parts);
            foreach (var part in source.Parts)
            {
                if (part == null || part.GetType().Name.IndexOf("Bark", StringComparison.OrdinalIgnoreCase) < 0) continue;
                _worldTextsWork70.Clear(); part.GetComponentsInChildren(true, _worldTextsWork70);
                foreach (var text in _worldTextsWork70) if (text != null && !source.BarkTexts.Contains(text)) source.BarkTexts.Add(text);
            }
            ResolveWorldInformationUnit70(source);
            return source;
        }

        static void ResolveWorldInformationUnit70(WorldInformationSource70 source)
        {
            if(source==null)return;
            source.Unit=null;source.UnitView=null;
            if (source.ViewModel == null) return;
            object unit = TryGetProp(source.ViewModel, "Unit") ?? TryGetProp(source.ViewModel,"DestructibleEntity");
            if (unit == null) unit = TryGetProp(source.ViewModel, "UnitUIWrapper");
            if (unit != null && unit.GetType().Name.IndexOf("Wrapper", StringComparison.OrdinalIgnoreCase) >= 0)
                unit = TryGetProp(unit, "Unit") ?? TryGetProp(unit, "MechanicEntity");
            source.Unit = unit;
            object view = TryGetProp(unit, "View");
            if (view is Component component) source.UnitView = component.transform;
            else if (view is GameObject gameObject) source.UnitView = gameObject.transform;
        }

        static void RegisterLegacyWorldInformation70(Transform widget)
        {
            if (widget == null || _legacyWorldInformation70.ContainsKey(widget)) return;
            Component view = null; _worldPartsWork70.Clear(); widget.GetComponents(_worldPartsWork70);
            foreach (var candidate in _worldPartsWork70) if (candidate != null && GetViewModel(candidate) != null) { view = candidate; break; }
            if (view == null) view = widget;
            _legacyWorldInformation70.Add(widget, BuildWorldInformationSource70(view, GetViewModel(view), WorldHudPolicy.AttackFamily));
        }

        static void RegisterLegacyInteraction70(Transform widget)
        {
            if (widget == null) return;
            if (_legacyInteractionRoots70.TryGetValue(widget, out int children) && children == widget.childCount) return;
            _legacyInteractionRoots70[widget] = widget.childCount;
            if(!_legacyInteractionGraphics70.TryGetValue(widget,out var registered))
            {_legacyInteractionGraphics70.Add(widget,registered=new List<Graphic>(8));}
            foreach(var prior in registered)
            {_interactionGraphics70.Remove(prior);_interactionTargets70.Remove(prior);}
            registered.Clear();_worldTextsWork70.Clear();_worldPartsWork70.Clear();widget.GetComponentsInChildren(true,_worldTextsWork70);widget.GetComponentsInChildren(true,_worldPartsWork70);
            foreach(var graphic in _worldTextsWork70)
            {
                if(graphic==null||graphic.GetType().Name.IndexOf("Text",StringComparison.OrdinalIgnoreCase)>=0)continue;registered.Add(graphic);
                if(_interactionGraphics70.Add(graphic))++_interactionRegistryBuilds70;
                _interactionTargets70[graphic]=CanonicalInteractionTarget70(graphic,graphic);
            }
            ++_sourceHierarchyRebuilds70;
            foreach (var part in _worldPartsWork70)
            {
                if (part == null || WorldHudPolicy.ProtectedFamily(part.GetType()) != WorldHudPolicy.InteractionFamily) continue;
                RegisterWorldInformationSource70(part,GetViewModel(part));
            }
        }

        static void SuppressWorldInformationSource70(Component view)
        {
            if (view == null || !_worldInformationSources70.TryGetValue(view, out var source)) return;
            SuppressWorldInformationSource70(source);
        }

        static void SuppressLegacyWorldInformation70(Transform widget)
        {
            if (widget != null && _legacyWorldInformation70.TryGetValue(widget, out var source)) SuppressWorldInformationSource70(source);
        }

        static void SuppressWorldInformationSource70(WorldInformationSource70 source)
        {
            if (source.View != null && _worldSourceRoots72.ContainsKey(source.View.transform))
            { ApplyWorldSourcePresentation72(source); return; }
            if(source.View!=null&&source.View.transform.childCount!=source.DirectChildren)
                RefreshWorldInformationSourceHierarchy70(source);
            foreach (var renderer in source.Renderers)
            {
                if (renderer == null) continue;
                if (!_worldInformationCull70.ContainsKey(renderer)) _worldInformationCull70.Add(renderer, renderer.cull);
                if (!renderer.cull) { renderer.cull = true; ++_suppressedWorldDraws70; }
            }
        }

        static void SuppressInteractionText70(WorldInformationSource70 source)
        {
            if(source==null)return;
            ApplyWorldSourcePresentation72(source);
        }

        static bool ShouldRevealWorldInformation71(Component view)
        {
            return _tacticalInspectionHeld71&&view!=null&&_worldInformationSources70.TryGetValue(view,out var source)&&
                _tacticalInspectionSources71.Contains(source);
        }

        static void CollectInteractionGraphics70(List<Graphic> destination)
        {
            ++_interactionRegistryReads70; _interactionDead70.Clear();
            foreach (var graphic in _interactionGraphics70)
            {
                if (graphic == null) { _interactionDead70.Add(graphic); continue; }
                destination.Add(graphic);
            }
            foreach (var dead in _interactionDead70) { _interactionGraphics70.Remove(dead); _interactionTargets70.Remove(dead); }
        }

        static GameObject CanonicalInteractionTarget70(Graphic graphic, Component view)
        {
            if (graphic == null) return view == null ? null : view.gameObject;
            GameObject target=ExecuteEvents.GetEventHandler<IPointerClickHandler>(graphic.gameObject);
            if(target==null)target=ExecuteEvents.GetEventHandler<IPointerDownHandler>(graphic.gameObject);
            if(target==null)target=ExecuteEvents.GetEventHandler<ISubmitHandler>(graphic.gameObject);
            return target ?? (view==null?graphic.gameObject:view.gameObject);
        }

        internal static void ObserveTacticalPattern70(object pattern)
        {
            if (pattern == null) return;
            _lastTacticalPattern70 = pattern; _lastTacticalPatternAt70 = Time.unscaledTime;
        }

        static void ObserveTacticalPreviewPostfix70(object __instance)
        {
            if (!_active || !_attached || _modeFlat || InSpaceCombat) return;
            _nativeTacticalPreview70 = __instance as Component;
            object caster = ReadField70(__instance,"m_CachedCasterNode"), target = ReadField70(__instance,"m_CachedTargetNode");
            if (TryGetProp(caster,"Vector3Position") is Vector3 origin && TryGetProp(target,"Vector3Position") is Vector3 destination)
                _lastTacticalDirection70 = destination-origin;
            ObserveTacticalPattern70(ReadField70(__instance,"m_CurrentPattern"));
        }

        static bool NativeTacticalPreviewActive70 => _nativeTacticalPreview70 != null &&
            _nativeTacticalPreview70.gameObject.activeInHierarchy &&
            ReadField70(_nativeTacticalPreview70,"m_IsActive") is bool visible && visible;

        static bool TouchWorldInspectionRequested70 => _active && _attached && !_modeFlat && _touchY.Held &&
            !TouchOverlayOpen && !TouchOverlayChordCaptured && !TouchRadialCaptured &&
            !TouchRadialChordPressed(_touchSample) && !TouchMenuWindowVisible && !NativeTutorialInputBlocked &&
            !InSpaceCombat && !InNavigationMap && TouchTacticalPlayerTurn71();

        static bool TouchTacticalPlayerTurn71()
        {
            try
            {
                var c=_touchCombatHeadContracts;if(c==null||_touchGroupContracts==null)return false;
                object game=_touchGroupContracts.Game(),turn=game==null?null:c.Turn(game);
                return turn!=null&&c.Active(turn)&&c.PlayerTurn(turn)&&!c.Preparation(turn);
            }
            catch{return false;}
        }

        internal static void PrepareWorldInformation71()
        { UpdateCoverageState74(); ReconcileWorldGraphics72(); ReconcileTacticalInspection72(TouchWorldInspectionRequested70); FlushVisiblePresentation74(); }

        static void ScheduleInteractionRefresh71()
        { _interactionRefreshNext71=0; }

        internal static void UpdateWorldInformation70(Camera left, Camera right)
        {
            long panelStarted=DiagnosticTimestamp();
            try
            {
            if (!_active || !_attached || _modeFlat)
            { HideWorldInformation70(); return; }

            long suppressionStarted=DiagnosticTimestamp();
            if(!Presentation74(4)||Time.frameCount%120==0)PruneWorldInformationSources70();
            RefreshNativeInteractions72();
            EnforceWorldPresentation72();
            if(!Presentation74(4)||Time.frameCount%120==0)
                foreach (var source in _legacyWorldInformation70.Values)
                    if (source.View != null && source.View.gameObject.activeInHierarchy) SuppressWorldInformationSource70(source);
            RecordModStage("WorldInformationSuppression70",suppressionStarted);

            if (left == null || right == null)
            { HideWorldInformation70(); return; }

            if(Time.unscaledTime>=_worldDialogueNext72)
            { _worldDialogueCached72=Presentation74(4)?CollectDialogue74():CollectDialogue70(); _worldDialogueNext72=Time.unscaledTime+.10f; }
            string dialogue = _worldDialogueCached72;
            if (string.IsNullOrWhiteSpace(dialogue))
            { HideWorldInformation70(); return; }

            EnsureWorldInformationPanel70();
            SetWorldInformationContent70(dialogue, null, null, false);
            PositionWorldInformationPanel70(left, right);
            _worldInformationVisible70 = true; _worldInformationCanvas70.enabled = true;
            }
            finally { RecordModStage("WorldInformationPanel70",panelStarted); }
        }

        static void PruneWorldInformationSources70()
        {
            _nativeWorldDead66.Clear();
            foreach (var pair in _worldInformationSources70) if (pair.Key == null) _nativeWorldDead66.Add(pair.Key);
            foreach (var dead in _nativeWorldDead66)
            {
                if(_worldInformationSources70.TryGetValue(dead,out var source))RemoveWorldInformationSource70(source);
                _worldInformationSources70.Remove(dead);
            }
            _legacyWorldDead70.Clear();
            foreach (var pair in _legacyWorldInformation70) if (pair.Key == null) _legacyWorldDead70.Add(pair.Key);
            foreach (var dead in _legacyWorldDead70) _legacyWorldInformation70.Remove(dead);
            _legacyWorldDead70.Clear();
            foreach(var pair in _legacyInteractionRoots70)if(pair.Key==null)_legacyWorldDead70.Add(pair.Key);
            foreach(var dead in _legacyWorldDead70)
            {
                if(_legacyInteractionGraphics70.TryGetValue(dead,out var graphics))
                    foreach(var graphic in graphics){_interactionGraphics70.Remove(graphic);_interactionTargets70.Remove(graphic);}
                _legacyInteractionGraphics70.Remove(dead);_legacyInteractionRoots70.Remove(dead);
            }
        }

        static void RemoveWorldInformationSource70(WorldInformationSource70 source)
        {
            if(source==null)return;
            if(source.ViewModel!=null&&(source.Family&WorldHudPolicy.AttackFamily)!=0)PresentationRootDisposed74(source.ViewModel);
            RemovePresentationSource74(source);
            _tacticalInspectionSources71.Remove(source); _worldDirtySources72.Remove(source);
            if(!ReferenceEquals(source.SourceRoot72,null))_worldSourceRoots72.Remove(source.SourceRoot72);
            foreach(var graphic in source.Texts)
                if(!ReferenceEquals(graphic,null)&&_worldGraphicStates72.TryGetValue(graphic,out var state)&&ReferenceEquals(state.Source,source))
                { if(graphic!=null&&state.Hidden){graphic.canvasRenderer.cull=state.NativeCull;graphic.SetAllDirty();} _worldGraphicStates72.Remove(graphic); }
            foreach(var graphic in source.InteractionGraphics)
            {_interactionGraphics70.Remove(graphic);_interactionTargets70.Remove(graphic);}
            ReleaseCoverSource76(source);
            source.InteractionGraphics.Clear();
            foreach(var renderer in source.Renderers)if(!ReferenceEquals(renderer,null))_worldInformationCull70.Remove(renderer);
            if(source.View!=null&&ReferenceEquals(source.View.gameObject,_stableInteractionTarget70))ClearStableInteractionTargetImmediately70();
        }

        static void ClearStableInteractionTargetImmediately70()
        {
            _stableInteractionTarget70=null;
        }

        static string CollectDialogue70()
        {
            _worldLines70.Clear(); _worldLineSet70.Clear();
            foreach (var source in _worldInformationSources70.Values)
            {
                if (source.View == null || !source.View.gameObject.activeInHierarchy) continue;
                // Read the native bark lifetime and localized text, not the UI
                // implementation (the game also uses TextMeshPro components).
                object bark = ReadField70(source.ViewModel, "BarkBlockVM") ?? source.ViewModel;
                if (ReactiveValue70(ReadField70(bark, "IsBarkActive")) is bool active && active)
                {
                    string value = ReactiveValue70(ReadField70(bark, "Text")) as string;
                    if (!string.IsNullOrWhiteSpace(value) && _worldLineSet70.Add(value))
                    { _worldLines70.Add(value); CacheWorldDialogueTheme71(source); }
                }
                if((source.Family&WorldHudPolicy.InteractionFamily)!=0)
                    foreach(var graphic in source.Texts)
                        if(graphic!=null&&source.InteractionTextRenderers.Contains(graphic.canvasRenderer)&&AddWorldGraphicLine71(graphic))
                            CacheWorldDialogueTheme71(source,graphic);
            }
            return _worldLines70.Count == 0 ? null : string.Join("\n", _worldLines70);
        }

        static bool AddWorldGraphicLine71(Graphic graphic)
        {
            if(graphic==null||!graphic.isActiveAndEnabled||graphic.canvasRenderer==null||
                graphic.canvasRenderer.GetInheritedAlpha()<=.001f)return false;
            string value=graphic is Text legacy?legacy.text:TryGetProp(graphic,"text") as string;
            if(string.IsNullOrWhiteSpace(value))return false;
            value=value.Trim();if(!_worldLineSet70.Add(value))return false;
            _worldLines70.Add(value);return true;
        }

        static void CacheWorldDialogueTheme71(WorldInformationSource70 source)
        {
            if(source==null)return;
            foreach(var graphic in source.BarkTexts)CacheWorldDialogueTheme71(source,graphic);
        }

        static void CacheWorldDialogueTheme71(WorldInformationSource70 source,Graphic graphic)
        {
            if(source==null||graphic==null)return;
            if(graphic is Text text)
            {
                if(text.font!=null)_worldDialogueFont71=text.font;
                _worldDialogueTextColor71=text.color;
            }
            for(Transform node=graphic.transform;node!=null&&source.View!=null&&node.IsChildOf(source.View.transform);node=node.parent)
            {
                var image=node.GetComponent<Image>();if(image==null||image.sprite==null)continue;
                _worldDialogueBackdropSprite71=image.sprite;_worldDialogueBackdropColor71=image.color;break;
            }
        }

        static void AddWorldLine70(Text text, bool allowed)
        {
            if (!allowed || text == null || !text.isActiveAndEnabled || text.canvasRenderer == null ||
                text.canvasRenderer.GetInheritedAlpha() <= .001f || string.IsNullOrWhiteSpace(text.text)) return;
            string value = text.text.Trim(); if (_worldLineSet70.Add(value)) _worldLines70.Add(value);
        }

        static WorldInformationSource70 FindPointedWorldInformation70()
        {
            GameObject pointer = PointerObject70("PointerOn") ?? PointerObject70("OvertipObject");
            foreach (var source in _worldInformationSources70.Values)
            {
                if ((source.Family & WorldHudPolicy.AttackFamily) == 0 || source.View == null || !source.View.gameObject.activeInHierarchy) continue;
                ResolveWorldInformationUnit70(source);
                if (pointer != null && source.UnitView != null &&
                    (pointer.transform == source.UnitView || pointer.transform.IsChildOf(source.UnitView) || source.UnitView.IsChildOf(pointer.transform))) return source;
            }
            return null;
        }

        static GameObject PointerObject70(string member)
        {
            object value=TryGetProp(_touchGamePointer,member);
            if(value is GameObject gameObject)return gameObject;
            if(value is Component component)return component.gameObject;
            object view=TryGetProp(value,"View");
            if(view is GameObject viewObject)return viewObject;
            if(view is Component viewComponent)return viewComponent.gameObject;
            return null;
        }

        static string CollectTacticalText70(WorldInformationSource70 source)
        {
            _tacticalNativeLines70.Clear(); _tacticalNativeSet70.Clear();
            foreach (var text in source.Texts)
            {
                if (source.BarkTexts.Contains(text)) continue;
                if(text==null||!text.isActiveAndEnabled||text.canvasRenderer==null||
                    text.canvasRenderer.GetInheritedAlpha()<=.001f)continue;
                string nativeText = text is Text legacy ? legacy.text : TryGetProp(text,"text") as string;
                if (string.IsNullOrWhiteSpace(nativeText)) continue;
                string value=nativeText.Trim();if(_tacticalNativeSet70.Add(value))_tacticalNativeLines70.Add(value);
            }
            if (_tacticalNativeLines70.Count == 0)
            {
                object name = TryGetProp(source.Unit, "CharacterName") ?? TryGetProp(source.Unit, "Name");
                if (name != null) _tacticalNativeLines70.Add(name.ToString());
            }
            return _tacticalNativeLines70.Count == 0 ? null : string.Join("   ", _tacticalNativeLines70.ToArray());
        }

        static bool BuildTacticalGrid70(out string labels)
        {
            int nativeSignature=TacticalNativeSignature70();
            if(TacticalPatternUnchanged70()&&_lastTacticalDirection70==_builtTacticalDirection70&&
                nativeSignature==_builtTacticalNativeSignature70)
            {labels=_cachedGridLabels70;return _tacticalCells70.Count>0;}
            labels = null; _tacticalCells70.Clear(); _tacticalNodes70.Clear();
            object nodes = TryGetProp(_lastTacticalPattern70, "Nodes");
            if (!(nodes is IEnumerable enumerable)) return false;
            object application=TryGetProp(_lastTacticalPattern70,"ApplicationNode");
            _patternOriginX70=ReadInt70(application,"XCoordinateInGrid",0);_patternOriginY70=ReadInt70(application,"ZCoordinateInGrid",0);
            int hash = 17;
            foreach (object node in enumerable)
            {
                if (node == null) continue;
                int x = ReadInt70(node, "XCoordinateInGrid", int.MinValue), y = ReadInt70(node, "ZCoordinateInGrid", int.MinValue);
                if (x == int.MinValue || y == int.MinValue) continue;
                TransformPatternCoordinate70(x,y,out int shownX,out int shownY);
                _tacticalNodes70.Add(node); _tacticalCells70.Add(new TacticalAreaCell70 { X = shownX, Y = shownY, Color = new Color(.32f,.34f,.34f,.82f) });
                hash = unchecked(hash * 31 + x * 397 + y);
            }
            if (_tacticalCells70.Count == 0) return false;
            _worldLines70.Clear(); _worldLineSet70.Clear();
            foreach (var source in _worldInformationSources70.Values)
            {
                if ((source.Family & WorldHudPolicy.AttackFamily) == 0 || source.Unit == null || source.View == null || !source.View.gameObject.activeInHierarchy) continue;
                object node = WorldUnitNode70(source.Unit);
                int x = ReadInt70(node, "XCoordinateInGrid", int.MinValue), y = ReadInt70(node, "ZCoordinateInGrid", int.MinValue);
                TransformPatternCoordinate70(x,y,out int shownX,out int shownY);
                UpdateOccupiedNodes70(source,node);
                string info = TacticalNativeValues70(source, out bool enemy, out bool currentTarget);
                bool represented=false;
                foreach(var occupied in source.OccupiedNodes)
                {
                    TransformPatternCoordinate70(ReadInt70(occupied,"XCoordinateInGrid",int.MinValue),ReadInt70(occupied,"ZCoordinateInGrid",int.MinValue),out shownX,out shownY);
                    int index=_tacticalCells70.FindIndex(cell=>cell.X==shownX&&cell.Y==shownY);
                    if(index<0)continue;
                    var cell=_tacticalCells70[index];cell.Color=enemy?new Color(.72f,.10f,.08f,.9f):new Color(.08f,.55f,.24f,.9f);
                    if(!represented)cell.Label=info;
                    _tacticalCells70[index]=cell;represented=true;
                }
                if(!represented)continue;
                string native=currentTarget?CollectTacticalText70(source):null;
                if (!string.IsNullOrEmpty(info)||!string.IsNullOrEmpty(native))
                    _worldLines70.Add("■ "+info+(string.IsNullOrEmpty(native)?"":"   "+native));
            }
            labels = _worldLines70.Count == 0 ? null : string.Join("   ", _worldLines70.ToArray());
            _builtTacticalPattern70=_lastTacticalPattern70;_builtTacticalDirection70=_lastTacticalDirection70;
            _builtTacticalNativeSignature70=nativeSignature;_cachedGridLabels70=labels;
            ++_gridContentRevision70;
            if (hash != _lastTacticalPatternHash70) { _lastTacticalPatternHash70 = hash; ++_patternChanges70; }
            return true;
        }

        static int TacticalNativeSignature70()
        {
            int hash=17;
            foreach(var source in _worldInformationSources70.Values)
            {
                if((source.Family&WorldHudPolicy.AttackFamily)==0||source.Unit==null||source.View==null||!source.View.gameObject.activeInHierarchy)continue;
                hash=unchecked(hash*31+RuntimeHelpers.GetHashCode(source.Unit));
                object node=WorldUnitNode70(source.Unit);
                hash=unchecked(hash*31+ReadInt70(node,"XCoordinateInGrid",0));hash=unchecked(hash*31+ReadInt70(node,"ZCoordinateInGrid",0));
                object health=ReadField70(source.ViewModel,"HealthBlockVM");
                object target=FindPreviewTargetData70(source.Unit);
                hash=unchecked(hash*31+TacticalTargetSignature70(target));
                hash=unchecked(hash*31+ReactiveIntField70(health,"HitPointTotalLeft",-1));
                hash=unchecked(hash*31+ReactiveIntField70(health,"HitPointTotalMax",-1));
                hash=unchecked(hash*31+ReadFloat70(source.Unit,"Orientation",0).GetHashCode());
            }
            return hash;
        }

        static int TacticalTargetSignature70(object target)
        {
            if(target==null)return 0;
            int hash=17;
            foreach(string name in new[]{"MinDamage","MaxDamage","Lines","BurstIndex"})
                hash=unchecked(hash*31+ReadInt70(target,name,-1));
            foreach(string name in new[]{"HitWithAvoidanceChance","InitialHitChance","DodgeChance","ParryChance","CoverChance","BlockChance","EvasionChance"})
                hash=unchecked(hash*31+Mathf.RoundToInt(ReadFloat70(target,name,-1)*1000));
            object burst=TryGetProp(target,"BurstHitChances");
            if(burst is IEnumerable values)foreach(object value in values)
                if(value is float chance)hash=unchecked(hash*31+Mathf.RoundToInt(chance*1000));
            return hash;
        }

        static bool TacticalPatternUnchanged70()
        {
            if (_builtTacticalPattern70 == null || !(TryGetProp(_lastTacticalPattern70,"Nodes") is IEnumerable nodes)) return false;
            int index=0;
            foreach(object node in nodes)
            {
                if(node==null)continue;
                if(index>=_tacticalNodes70.Count||!ReferenceEquals(node,_tacticalNodes70[index++]))return false;
            }
            return index==_tacticalNodes70.Count;
        }

        static void TransformPatternCoordinate70(int x,int y,out int shownX,out int shownY)
        {
            WorldInformationPolicy70.RotateCell(x,y,_patternOriginX70,_patternOriginY70,
                _lastTacticalDirection70.x,_lastTacticalDirection70.z,out shownX,out shownY);
        }

        static string TacticalNativeValues70(WorldInformationSource70 source, out bool enemy, out bool currentTarget)
        {
            enemy = false; object health = source.ViewModel == null ? null : ReadField70(source.ViewModel, "HealthBlockVM");
            object target = FindPreviewTargetData70(source.Unit);
            currentTarget=target!=null;
            float chance = ReadFloat70(target, "HitWithAvoidanceChance", -1);
            int min = ReadInt70(target, "MinDamage", -1), max = ReadInt70(target, "MaxDamage", -1);
            int hp = ReactiveIntField70(health, "HitPointTotalLeft", -1), hpMax = ReactiveIntField70(health, "HitPointTotalMax", -1);
            if(TryGetProp(health,"HideRealHealthInUI") is bool hidden && hidden)hp=hpMax=-1;
            object hostile = TryGetProp(health, "IsPLayerEnemy"); if (hostile is bool b) enemy = b;
            string value = chance >= 0 ? Mathf.RoundToInt(chance) + "%" : "—";
            if (min >= 0) value += "  " + min + (max > min ? "–" + max : "");
            if (hp >= 0) value += "  " + hp + (hpMax > 0 ? "/" + hpMax : "");
            return value;
        }

        static object FindPreviewTargetData70(object unit)
        {
            if (unit == null || _nativeTacticalPreview70 == null) return null;
            object previewAbility = TryGetProp(_nativeTacticalPreview70,"Ability") ?? ReadField70(_nativeTacticalPreview70,"Ability");
            object targets = ReadField70(_nativeTacticalPreview70,"m_AbilityTargets");
            if (previewAbility == null || !(targets is IEnumerable enumerable)) return null;
            foreach (object target in enumerable)
            {
                if (target == null || !ReferenceEquals(TryGetProp(target,"Ability"),previewAbility)) continue;
                if (ReferenceEquals(TryGetProp(target,"Target"),unit)) return target;
            }
            return null;
        }

        static object ReadField70(object owner, string name)
        {
            if (owner == null) return null;
            Type ownerType=owner.GetType();
            if (!_worldFields70.TryGetValue(ownerType,out var fields))
                _worldFields70.Add(ownerType,fields=new Dictionary<string, FieldInfo>(StringComparer.Ordinal));
            if (!fields.TryGetValue(name,out var field))
            {
                for (Type type=ownerType;type!=null;type=type.BaseType)
                {
                    field=type.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                    if(field!=null)break;
                }
                fields.Add(name,field);
            }
            try{return field?.GetValue(owner);}catch{return null;}
        }
        static object WorldUnitNode70(object unit)
        {
            // MechanicEntity.CurrentNode is NNInfo, not CustomGridNodeBase.
            object nearest = TryGetProp(unit,"CurrentNode");
            return ReadField70(nearest,"node") ?? TryGetProp(nearest,"node");
        }
        static void UpdateOccupiedNodes70(WorldInformationSource70 source,object node)
        {
            float orientation=ReadFloat70(source.Unit,"Orientation",0);
            if(ReferenceEquals(node,source.OccupiedAt)&&orientation==source.OccupiedOrientation)return;
            source.OccupiedAt=node;source.OccupiedOrientation=orientation;source.OccupiedNodes.Clear();
            if(_occupiedNodesMethod70==null)
            {
                Type helper=HarmonyLib.AccessTools.TypeByName("Kingmaker.Pathfinding.GridAreaHelper");
                if(helper!=null)foreach(var method in helper.GetMethods(BindingFlags.Public|BindingFlags.Static))
                    if(method.Name=="GetOccupiedNodes"&&method.GetParameters().Length==1&&method.GetParameters()[0].ParameterType.IsInstanceOfType(source.Unit))
                    {_occupiedNodesMethod70=method;break;}
            }
            object native=null;
            try
            {
                native=_occupiedNodesMethod70?.Invoke(null,new[]{source.Unit});
                if(native is IEnumerable nodes)foreach(object occupied in nodes)source.OccupiedNodes.Add(occupied);
            }
            finally{(native as IDisposable)?.Dispose();}
            if(source.OccupiedNodes.Count==0&&node!=null)source.OccupiedNodes.Add(node);
        }
        static object ReactiveValue70(object value) => value == null ? null : TryGetProp(value, "Value") ?? value;
        static int ReactiveIntField70(object owner, string name, int fallback)
        { object value = ReactiveValue70(ReadField70(owner,name)); return value is int number ? number : fallback; }
        static int ReadInt70(object owner, string name, int fallback)
        { object value = TryGetProp(owner,name) ?? ReadField70(owner,name); return value is int number ? number : fallback; }
        static float ReadFloat70(object owner, string name, float fallback)
        { object value = TryGetProp(owner,name) ?? ReadField70(owner,name); return value is float number ? number : fallback; }

        static void EnsureWorldInformationPanel70()
        {
            if (_worldInformationRoot70 != null) return;
            _worldInformationRoot70 = new GameObject("RTMaquetaXR fixed world information", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            UnityEngine.Object.DontDestroyOnLoad(_worldInformationRoot70); _worldInformationRoot70.layer = 5;
            var root = (RectTransform)_worldInformationRoot70.transform; root.sizeDelta = new Vector2(760, 320);
            _worldInformationCanvas70 = _worldInformationRoot70.GetComponent<Canvas>(); _worldInformationCanvas70.renderMode = RenderMode.WorldSpace;
            _worldInformationCanvas70.overrideSorting = true; _worldInformationCanvas70.sortingOrder = 32760;
            _worldInformationGroup70 = _worldInformationRoot70.GetComponent<CanvasGroup>();
            _worldInformationGroup70.blocksRaycasts = false; _worldInformationGroup70.interactable = false;
            _worldInformationBackdrop70 = WorldPanelImage70("Background", root, new Rect(0,0,760,320), _worldDialogueBackdropColor71);
            _worldInformationBackdrop70.enabled=false;
            if(_worldDialogueBackdropSprite71!=null){_worldInformationBackdrop70.sprite=_worldDialogueBackdropSprite71;_worldInformationBackdrop70.type=Image.Type.Sliced;}
            _worldDialogueText70 = WorldPanelText70("Ambient dialogue", root, new Rect(28,-24,704,264), 22, TextAnchor.UpperLeft);
            _worldDialogueText70.color=_worldDialogueTextColor71;if(_worldDialogueFont71!=null)_worldDialogueText70.font=_worldDialogueFont71;
            RenderPipelineManager.beginCameraRendering += WorldInformationBeginCamera70;
            InvalidateHudHierarchy();
        }

        static Image WorldPanelImage70(string name, RectTransform parent, Rect rect, Color color)
        {
            var item=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));item.layer=5;
            var transform=(RectTransform)item.transform;transform.SetParent(parent,false);transform.anchorMin=transform.anchorMax=transform.pivot=Vector2.zero;
            transform.anchoredPosition=new Vector2(rect.x,rect.y);transform.sizeDelta=new Vector2(rect.width,rect.height);
            var image=item.GetComponent<Image>();image.color=color;image.raycastTarget=false;if(_liveImageMaterial!=null)image.material=_liveImageMaterial;return image;
        }

        static Text WorldPanelText70(string name, RectTransform parent, Rect rect, int size, TextAnchor alignment)
        {
            var text=LiveText(name,parent,size,FontStyle.Normal,Color.white,rect.x,rect.y,rect.width,rect.height);
            text.alignment=alignment;text.resizeTextForBestFit=false;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;
            text.raycastTarget=false;SharpenRadialText(text);OutlineRadialText(text);return text;
        }

        static void SetWorldInformationContent70(string dialogue, string tactical, string gridLabels, bool grid)
        {
            dialogue = dialogue ?? ""; tactical = tactical ?? ""; gridLabels = gridLabels ?? "";
            if(_worldInformationBackdrop70!=null)
            {
                _worldInformationBackdrop70.enabled=false;
                if(_worldInformationBackdrop70.sprite!=_worldDialogueBackdropSprite71)_worldInformationBackdrop70.sprite=_worldDialogueBackdropSprite71;
                _worldInformationBackdrop70.type=_worldDialogueBackdropSprite71==null?Image.Type.Simple:Image.Type.Sliced;
            }
            if(_worldDialogueText70!=null)
            { _worldDialogueText70.color=_worldDialogueTextColor71;if(_worldDialogueFont71!=null)_worldDialogueText70.font=_worldDialogueFont71; }
            if (_lastDialogue70 != dialogue) { _lastDialogue70=dialogue;_worldDialogueText70.text=dialogue;++_panelContentChanges70; }
            if (_lastTactical70 != tactical) { _lastTactical70=tactical;if(_worldTacticalText70!=null)_worldTacticalText70.text=tactical;++_panelContentChanges70; }
            if (_lastGridLabels70 != gridLabels) { _lastGridLabels70=gridLabels;if(_worldGridLabels70!=null)_worldGridLabels70.text=gridLabels;++_panelContentChanges70; }
            bool haveDialogue=dialogue.Length>0,haveTactical=tactical.Length>0||grid;
            _worldDialogueText70.gameObject.SetActive(haveDialogue);if(_worldInformationDivider70!=null)_worldInformationDivider70.gameObject.SetActive(haveDialogue&&haveTactical);
            if(_worldTacticalText70!=null)_worldTacticalText70.gameObject.SetActive(haveTactical&&!grid);
            if(_worldGrid70!=null)_worldGrid70.gameObject.SetActive(grid);if(_worldGridLabels70!=null)_worldGridLabels70.gameObject.SetActive(grid);
            if(grid&&_worldGrid70!=null&&_gridAppliedRevision70!=_gridContentRevision70)
            {_worldGrid70.SetCells(_tacticalCells70);UpdateWorldGridCellLabels70();_gridAppliedRevision70=_gridContentRevision70;}
            else if (!grid) foreach(var label in _worldGridCellLabels70)if(label!=null)label.gameObject.SetActive(false);
            if (grid&&_worldGrid70!=null) UpdateWorldGridCellLabels70();
            float height=haveDialogue&&haveTactical?620:haveDialogue?320:300;
            ((RectTransform)_worldInformationRoot70.transform).sizeDelta=new Vector2(760,height);
            _worldInformationBackdrop70.rectTransform.sizeDelta=new Vector2(760,height);
            _worldDialogueText70.rectTransform.anchoredPosition=new Vector2(28,-24);
            float tacticalY=haveDialogue?-326:-24;
            if(_worldTacticalText70!=null)_worldTacticalText70.rectTransform.anchoredPosition=new Vector2(28,tacticalY);
            if(_worldGridLabels70!=null)_worldGridLabels70.rectTransform.anchoredPosition=new Vector2(410,tacticalY);
        }

        static void UpdateWorldGridCellLabels70()
        {
            int used=0;
            for(int i=0;i<_tacticalCells70.Count;i++)
            {
                string value=_tacticalCells70[i].Label;if(string.IsNullOrEmpty(value)||!_worldGrid70.TryCellRect(i,out Rect cell))continue;
                Text label;
                if(used<_worldGridCellLabels70.Count)label=_worldGridCellLabels70[used];
                else
                {
                    label=WorldPanelText70("AoE cell value "+used,(RectTransform)_worldGrid70.transform,new Rect(0,0,80,32),16,TextAnchor.MiddleCenter);
                    label.horizontalOverflow=HorizontalWrapMode.Overflow;label.verticalOverflow=VerticalWrapMode.Overflow;_worldGridCellLabels70.Add(label);
                }
                label.gameObject.SetActive(true);label.text=value.Replace("  ","\n");
                var rect=label.rectTransform;rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.zero;rect.anchoredPosition=new Vector2(cell.x,cell.y);rect.sizeDelta=new Vector2(cell.width*2,cell.height*2);
                used++;
            }
            for(int i=used;i<_worldGridCellLabels70.Count;i++)if(_worldGridCellLabels70[i]!=null)_worldGridCellLabels70[i].gameObject.SetActive(false);
        }

        static void PositionWorldInformationPanel70(Camera left, Camera right)
        {
            float near=Mathf.Max(left.nearClipPlane,right.nearClipPlane),far=Mathf.Min(left.farClipPlane,right.farClipPlane);
            if(!LiveOverlayPolicy.TryDistance(near,far,WorldScale,out float distance))return;
            Quaternion rotation=Quaternion.Slerp(left.transform.rotation,right.transform.rotation,.5f);
            Vector3 head=(left.transform.position+right.transform.position)*.5f;
            float scale=distance*.60f/760f;
            float visibleHeight=2f*distance*Mathf.Tan(Mathf.Deg2Rad*((left.fieldOfView+right.fieldOfView)*.25f));
            float y=WorldInformationPolicy70.PanelCenterY(distance,((RectTransform)_worldInformationRoot70.transform).rect.height,scale)-visibleHeight*.20f;
            PositionHudHelper(_worldInformationRoot70.transform,head+rotation*new Vector3(0,y,distance),rotation,scale);
            Camera textCamera=!_hudCaptureFault&&_hudBlackCamera!=null?_hudBlackCamera:null;
            if(_worldInformationCanvas70.worldCamera!=textCamera)_worldInformationCanvas70.worldCamera=textCamera;
        }

        static void WorldInformationBeginCamera70(ScriptableRenderContext context, Camera camera)
        {
            if(_worldInformationCanvas70==null)return;
            bool isolated=HudCaptureActive&&_worldInformationRoot70!=null&&_worldInformationRoot70.layer==_hudLayer;
            bool visible=_worldInformationVisible70&&!_modeFlat&&isolated&&IsHudCaptureCamera(camera);
            if(_worldInformationCanvas70.enabled!=visible)_worldInformationCanvas70.enabled=visible;
        }

        internal static void HideWorldInformation70()
        { _worldInformationVisible70=false;if(_worldInformationCanvas70!=null)_worldInformationCanvas70.enabled=false; }

        static void ReleaseWorldInformation70()
        {
            ReleasePresentation74();
            ReleaseWorldInteraction72();
            ReleaseWorldPresentation72();
            RenderPipelineManager.beginCameraRendering-=WorldInformationBeginCamera70;
            foreach(var item in _worldInformationCull70)if(item.Key!=null)item.Key.cull=item.Value;
            _worldInformationCull70.Clear();_worldInformationSources70.Clear();_legacyWorldInformation70.Clear();_interactionGraphics70.Clear();_interactionTargets70.Clear();_legacyInteractionRoots70.Clear();_legacyInteractionGraphics70.Clear();
            _presentationViews75.Clear();
            _stableInteractionTarget70=null;_lastTacticalPattern70=null;_lastTacticalPatternAt70=0;_nativeTacticalPreview70=null;
            _tacticalInspectionSources71.Clear();_tacticalInspectionHeld71=false;
            _builtTacticalPattern70=null;_cachedGridLabels70=null;_builtTacticalNativeSignature70=0;
            _gridContentRevision70=0;_gridAppliedRevision70=-1;
            _interactionRefreshNext71=0;
            if(_worldInformationRoot70!=null)UnityEngine.Object.Destroy(_worldInformationRoot70);
            _worldInformationRoot70=null;_worldInformationCanvas70=null;_worldInformationGroup70=null;_worldInformationBackdrop70=null;
            _worldInformationDivider70=null;_worldDialogueText70=_worldTacticalText70=_worldGridLabels70=null;_worldGrid70=null;
            _worldGridCellLabels70.Clear();
            _lastDialogue70=_lastTactical70=_lastGridLabels70=null;_worldInformationVisible70=false;
        }
    }
}
