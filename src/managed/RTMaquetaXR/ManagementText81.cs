using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class ManagementFont81
        {
            internal Component Component;
            internal PropertyInfo Size, Min, Max;
            readonly FontScaleLease81 size=new FontScaleLease81(),min=new FontScaleLease81(),max=new FontScaleLease81();
            void Write(PropertyInfo p,FontScaleLease81 lease,float factor,bool restore)
            {
                if(p==null)return;
                float current=Convert.ToSingle(p.GetValue(Component,null));
                float next=restore?lease.Restore(current):lease.Apply(current,factor,p.PropertyType==typeof(int));
                if(Math.Abs(next-current)>.001f)p.SetValue(Component,p.PropertyType==typeof(int)?(object)Mathf.RoundToInt(next):next,null);
            }
            internal void Apply(float factor,bool restore=false) { if(Component==null)return;Write(Size,size,factor,restore);Write(Min,min,factor,restore);Write(Max,max,factor,restore); }
        }
        static readonly Dictionary<Component,ManagementFont81> _managementFonts81=new Dictionary<Component,ManagementFont81>();
        static readonly List<Component> _managementTextNodes81=new List<Component>();
        static Component _managementSurface81,_managementCommon81;
        static float _managementTextNext81,_managementTextFactor81=-1;
        static void RestoreManagementText81()
        {
            foreach(var font in _managementFonts81.Values)font.Apply(1,true);
            _managementFonts81.Clear();_managementTextNodes81.Clear();
            _managementSurface81=_managementCommon81=null;_managementTextFactor81=-1;_managementTextNext81=0;
        }
        static void UpdateManagementText81()
        {
            // Only the flat native-management path needs this lease. The stereo
            // HUD uses its existing viewport scaling, never both at once.
            if(!_active || !_modeFlat || _pcHud==null || !ManagementScreen81 || NavigationPresentation81) { if(_managementFonts81.Count>0)RestoreManagementText81();return; }
            RefreshPcUiRoots();
            if(_managementSurface81!=(_pcHudView as Component) || _managementCommon81!=(_pcHudCommon as Component))RestoreManagementText81();
            _managementSurface81=_pcHudView as Component;_managementCommon81=_pcHudCommon as Component;
            float factor=MenuWindowScale;
            if(Time.unscaledTime<_managementTextNext81 && factor==_managementTextFactor81)return;
            _managementTextNext81=Time.unscaledTime+.5f;_managementTextFactor81=factor;
            AuditManagementText81(_managementSurface81,factor);
            if(_managementCommon81!=_managementSurface81)AuditManagementText81(_managementCommon81,factor);
        }
        static void AuditManagementText81(Component root,float factor)
        {
            if(root==null)return;
            _managementTextNodes81.Clear();root.GetComponentsInChildren(false,_managementTextNodes81);
            foreach(var component in _managementTextNodes81) {
                if(component==null)continue;
                if(!_managementFonts81.TryGetValue(component,out var font)) {
                    var property=FontProperty81(component);if(property==null)continue;
                    var type=component.GetType();
                    font=new ManagementFont81{Component=component,Size=property,Min=type.GetProperty("fontSizeMin"),Max=type.GetProperty("fontSizeMax")};
                    _managementFonts81.Add(component,font);
                }
                font.Apply(factor);
            }
        }
    }
}
