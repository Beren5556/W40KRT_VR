using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    internal sealed class SpatialConfirmGlyph : MaskableGraphic
    {
        public override Texture mainTexture => ControlIconAtlas.Texture(false);
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            // This legacy prompt is currently hidden. If reused, it shares the
            // exact approved artwork rather than drawing a second button family.
            ControlIconDrawing.Mesh(vh,new Rect(-266,-32,64,64),"A",Color.white);
            ControlIconDrawing.Mesh(vh,new Rect(202,-32,64,64),"RT",Color.white);
        }
    }
    public static partial class Main
    {
        static GameObject _spatialInfoHint;
        static Text _spatialInfoHintText;
        static int _spatialInfoHintLanguage=-1;
        static void UpdateSpatialInformationHint(bool visible)
        {
            if(visible && _spatialRoot!=null && _spatialInfoHint==null)
            {
                _spatialInfoHint=new GameObject("Native ability confirmation",typeof(RectTransform),typeof(Canvas),typeof(CanvasRenderer),typeof(SpatialConfirmGlyph));
                var rect=(RectTransform)_spatialInfoHint.transform;rect.SetParent(_spatialRoot.transform,false);
                rect.sizeDelta=new Vector2(600,72);
                var canvas=_spatialInfoHint.GetComponent<Canvas>();canvas.overrideSorting=true;canvas.sortingOrder=32767;
                canvas.worldCamera=_spatialBlackCamera;
                var art=_spatialInfoHint.GetComponent<SpatialConfirmGlyph>();art.raycastTarget=false;art.material=_touchRadialMaterial;
                _spatialInfoHintText=RadialText("Confirm ability",rect,30,Vector2.zero,new Vector2(400,64));
                _spatialInfoHintText.resizeTextForBestFit=true;_spatialInfoHintText.resizeTextMinSize=24;_spatialInfoHintText.resizeTextMaxSize=30;
                SharpenRadialText(_spatialInfoHintText);
                OutlineRadialText(_spatialInfoHintText);
                _spatialInfoHintLanguage=-1;_spatialScanFrame=0;
            }
            if(_spatialInfoHint==null)return;
            if(_spatialInfoHint.activeSelf!=visible)_spatialInfoHint.SetActive(visible);
            if(visible)
            {
                var rect=(RectTransform)_spatialInfoHint.transform;
                Vector3 target=SpatialInformationLayout.HintPosition(_spatialNative==null?new Vector2(1100,950):_spatialNative.PlacedSize);
                if(rect.anchoredPosition3D!=target)rect.anchoredPosition3D=target;
                // Native tooltip canvases can be appended after this retained
                // prompt. Own highest-order canvas plus a closer plane keeps
                // the confirmation visibly on top of their parchment.
                if(rect.GetSiblingIndex()!=_spatialRoot.transform.childCount-1)rect.SetAsLastSibling();
            }
            if(visible && _spatialInfoHintLanguage!=ModLocalization.Revision)
            {
                _spatialInfoHintText.text=ModLocalization.Text("Confirm");_spatialInfoHintLanguage=ModLocalization.Revision;
            }
        }
        static void DestroySpatialInformationHint()
        {
            if(_spatialInfoHint!=null)UnityEngine.Object.Destroy(_spatialInfoHint);
            _spatialInfoHint=null;_spatialInfoHintText=null;_spatialInfoHintLanguage=-1;
        }
    }
}
