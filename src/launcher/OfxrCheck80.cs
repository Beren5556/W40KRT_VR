using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;
namespace RogueTraderLauncher
{
    internal static class OfxrCheck80
    {
        [DataContract] sealed class Document { [DataMember(Name="api_layer")] public Layer Layer; }
        [DataContract] sealed class Layer {
            [DataMember(Name="name")] public string Name;
            [DataMember(Name="disable_environment")] public string Disable;
            [DataMember(Name="enable_environment")] public string Enable;
        }
        static bool Other(string name)=>!string.IsNullOrEmpty(name)&&(name.IndexOf("ofxr",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("XRFrameBridge",StringComparison.OrdinalIgnoreCase)>=0);
        internal static string Problem(string mod)
        {
            string folder=Path.Combine(mod,"Optional","OFXR");
            foreach(string name in new[]{"RTMaquetaXR.Ofxr77.dll","XR_APILAYER_XRFrameBridge_diagnostic.dll"})
                if(!File.Exists(Path.Combine(folder,name)))return "The optional OFXR module is incomplete. Reinstall the complete VR package or turn OFXR off.";
            foreach(string name in (Environment.GetEnvironmentVariable("XR_ENABLE_API_LAYERS")??"").Split(';'))
                if(Other(name))return "Another OFXR layer is enabled. Disable it before using the mod's optional OFXR.";
            try {
                foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})
                using(var root=RegistryKey.OpenBaseKey(hive,RegistryView.Registry64))
                using(var key=root.OpenSubKey(@"SOFTWARE\Khronos\OpenXR\1\ApiLayers\Implicit",false))
                    if(key!=null)foreach(string path in key.GetValueNames())
                    {
                        if(!(key.GetValue(path) is int enabled)||enabled!=0)continue;
                        using(var stream=File.OpenRead(path)) {
                            var layer=((Document)new DataContractJsonSerializer(typeof(Document)).ReadObject(stream))?.Layer;
                            if(layer==null||!Other(layer.Name))continue;
                            bool disabled=!string.IsNullOrEmpty(layer.Disable)&&!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(layer.Disable));
                            bool allowed=string.IsNullOrEmpty(layer.Enable)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(layer.Enable));
                            if(!disabled&&allowed)return "Another OFXR layer is active. Disable it before using the mod's optional OFXR.";
                        }
                    }
            } catch(Exception e) when(e is IOException||e is UnauthorizedAccessException||e is SerializationException||e is System.Security.SecurityException) {
                return "Cannot verify the installed OpenXR layers: "+e.Message;
            }
            return null;
        }
    }
}
