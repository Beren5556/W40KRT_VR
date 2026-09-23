using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
namespace RTMaquetaXR
{
    internal static class RuntimeDiscovery80
    {
        static string ReadRegistry(RegistryView view,string subkey,string name)
        {
            try {using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view))using(var key=machine.OpenSubKey(subkey,false))return key?.GetValue(name) as string;}
            catch(Exception e) when(e is System.Security.SecurityException||e is UnauthorizedAccessException||e is IOException){return null;}
        }
        internal static List<string> Candidates(int value,string explicitPath=null)
        {
            var paths=new List<string>();if(!string.IsNullOrWhiteSpace(explicitPath))paths.Add(explicitPath);
            foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
            {
                paths.Add(ReadRegistry(view,@"SOFTWARE\Khronos\OpenXR\1","ActiveRuntime"));
                try {using(var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view))using(var key=machine.OpenSubKey(@"SOFTWARE\Khronos\OpenXR\1\AvailableRuntimes",false))if(key!=null)paths.AddRange(key.GetValueNames());}
                catch(Exception e) when(e is System.Security.SecurityException||e is UnauthorizedAccessException||e is IOException){}
                string oculus=ReadRegistry(view,@"SOFTWARE\Oculus VR, LLC\Oculus","Base");
                if(!string.IsNullOrWhiteSpace(oculus))paths.Add(Path.Combine(oculus,@"Support\oculus-runtime\oculus_openxr_64.json"));
            }
            foreach(string folder in new[]{Environment.GetEnvironmentVariable("ProgramW6432"),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)})
            {
                if(string.IsNullOrWhiteSpace(folder))continue;
                paths.Add(Path.Combine(folder,@"Virtual Desktop Streamer\OpenXR\virtualdesktop-openxr.json"));
                paths.Add(Path.Combine(folder,@"Oculus\Support\oculus-runtime\oculus_openxr_64.json"));
                // Registered/custom paths take precedence. Probe only bounded
                // vendor folders; never search drives or select another vendor.
                foreach(string name in new[]{"PiOpenXR.json","PiOpenXR_64.json","PimaxOpenXR.json"})
                    foreach(string relative in new[]{@"Pimax\Runtime",@"Pimax\Runtime\openxr",@"Pimax\PimaxPlay\Runtime",@"Pimax\PimaxPlay\Runtime\openxr"})paths.Add(Path.Combine(folder,relative,name));
            }
            paths.Add(Environment.GetEnvironmentVariable("XR_RUNTIME_JSON"));
            return paths;
        }
        internal static string Resolve(int value,string explicitPath=null)=>
            OpenXrRuntimePolicy.FindManifest(value,Candidates(value,explicitPath),RuntimeManifest80.IsUsable);
    }
}
