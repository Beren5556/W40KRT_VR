using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
namespace RTMaquetaXR
{
    internal static class RuntimeManifest80
    {
        [DataContract] sealed class Document { [DataMember(Name="runtime")] public Runtime Runtime; }
        [DataContract] sealed class Runtime { [DataMember(Name="library_path")] public string Library; }
        internal static bool IsUsable(string path)
        {
            try
            {
                if(string.IsNullOrWhiteSpace(path)||!File.Exists(path)||new FileInfo(path).Length>65536)return false;
                Document document;
                using(var stream=File.OpenRead(path))document=(Document)new DataContractJsonSerializer(typeof(Document)).ReadObject(stream);
                string library=document?.Runtime?.Library;
                if(string.IsNullOrWhiteSpace(library)||library.IndexOfAny(new[]{'\r','\n','\0'})>=0)return false;
                library=Path.GetFullPath(Path.IsPathRooted(library)?library:Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)),library));
                using(var stream=File.OpenRead(library))using(var reader=new BinaryReader(stream))
                {
                    if(stream.Length<64||reader.ReadUInt16()!=0x5a4d)return false;
                    stream.Position=60;int pe=reader.ReadInt32();
                    if(pe<64||pe>stream.Length-24)return false;
                    stream.Position=pe;
                    return reader.ReadUInt32()==0x4550 && reader.ReadUInt16()==0x8664;
                }
            }
            catch(Exception e) when(e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is SerializationException||e is System.Xml.XmlException||e is System.Security.SecurityException||e is NotSupportedException){return false;}
        }
    }
}
