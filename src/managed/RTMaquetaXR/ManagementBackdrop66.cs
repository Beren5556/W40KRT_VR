using UnityEngine;
using UnityEngine.UI;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Image _managementBackdrop66;
        static Material _managementBackdropMaterial66;
        static void SetManagementBackdrop66(Component view)
        {
            if(view==null)
            {
                if(_managementBackdrop66!=null)_managementBackdrop66.gameObject.SetActive(false);
                return;
            }
            var canvas=view.GetComponentInParent<Canvas>();
            if(canvas==null){SetManagementBackdrop66(null);return;}
            // The native canvas owns dimensions and picking. Place black behind
            // its native panels, never on a separate overlay above their buttons.
            var parent=canvas.rootCanvas.transform;
            if(_managementBackdrop66==null)
            {
                var obj=new GameObject("RTMaquetaXR management black",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
                _managementBackdrop66=obj.GetComponent<Image>();
                _managementBackdrop66.color=Color.black;
                _managementBackdrop66.raycastTarget=false;
                _managementBackdropMaterial66=CreateLiveUiMaterial("RTMaquetaXR management black material");
                _managementBackdrop66.material=_managementBackdropMaterial66;
            }
            var rect=_managementBackdrop66.rectTransform;
            if(rect.parent!=parent)
            {
                rect.SetParent(parent,false);rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
                rect.offsetMin=rect.offsetMax=Vector2.zero;rect.localScale=Vector3.one;rect.localRotation=Quaternion.identity;
            }
            _managementBackdrop66.gameObject.layer=parent.gameObject.layer;
            if(rect.GetSiblingIndex()!=0)rect.SetAsFirstSibling();
            if(!_managementBackdrop66.gameObject.activeSelf)_managementBackdrop66.gameObject.SetActive(true);
        }
        static void ReleaseManagementBackdrop66()
        {
            if(_managementBackdrop66!=null)Object.Destroy(_managementBackdrop66.gameObject);
            if(_managementBackdropMaterial66!=null)Object.Destroy(_managementBackdropMaterial66);
            _managementBackdrop66=null;_managementBackdropMaterial66=null;
        }
    }
}
