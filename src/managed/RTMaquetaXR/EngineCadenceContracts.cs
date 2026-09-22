using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;

namespace RTMaquetaXR
{
    // Only the audited implementation is eligible. A new game build keeps its
    // original updates until its changed bodies and dependencies are reviewed.
    internal sealed class EngineCadenceContracts
    {
        internal MethodInfo[] Targets, Invalidators;
        internal const string AuditedModule = "f564230e-b1b2-45d8-ac09-1010963ed370";
        struct Signature
        {
            internal int Token;
            internal string Type, Name, Hash;
            internal bool Static;
            internal Signature(int token, string type, string name, bool isStatic, string hash)
            { Token=token; Type=type; Name=name; Static=isStatic; Hash=hash; }
        }
        static readonly Signature[] Signatures = {
            new Signature(0x06000deb, "Kingmaker.Visual.WindController", "Update", false, "1DC66D10C1D9139105680C8FD6009B2C5CA8DB09B3CD52981CF71C8DCDAC555E"),
            new Signature(0x06001126, "Kingmaker.Visual.Particles.FxHelper", "ApplyMaterialAnimations", true, "8D19B410DCCD50636F271D4B5CD43CAA43DCBDEB8424A2F52A02DBE208113618"),
            new Signature(0x06001297, "Kingmaker.Visual.MaterialEffects.StandardMaterialController", "SetupMaterials", false, "855DBCBF08E7C3A08CDB9729C568BDE3B421E8CBBF86B176149CAA041CB23004"),
            new Signature(0x0600129d, "Kingmaker.Visual.MaterialEffects.StandardMaterialController", "ReinitRenderersAndMaterials", false, "AFCA7F723AD7560941796242EB2E66B74B3D8B13A58FAEF72D4B7C645CE08498"),
            new Signature(0x0600129e, "Kingmaker.Visual.MaterialEffects.StandardMaterialController", "UpdateMaterialProperties", false, "29C124D27F30C11974B3810BC3C25DC2A60B97B5F670071E86EB3978A731E9BA"),
            new Signature(0x060012a9, "Kingmaker.Visual.MaterialEffects.RimLighting.RimLightingAnimationController", "Update", false, "DA5AB16064BA6C34BE897E9DA842B6E45037E88B3E7DCB063A3D24148998452E"),
            new Signature(0x060012ab, "Kingmaker.Visual.MaterialEffects.RimLighting.RimLightingAnimationController", "UpdateAnimation", false, "4AF55CA6F60A92279505D86845E0D6194D365F722E75273E5C42BA06CBB70C20"),
            new Signature(0x060012ac, "Kingmaker.Visual.MaterialEffects.RimLighting.RimLightingAnimationController", "EvaluateNormalized", false, "061711F37094FFD82ED242CD84F27EF35864F8C21DC91EEB1973D8679519ECD1"),
            new Signature(0x060012fe, "Kingmaker.Visual.MaterialEffects.LayeredMaterial.LayeredMaterialController", "RemoveAnimation", false, "5F6DFAD9D0BB55087E31383E9E247C8546A1611171304C25DA4376EEC9CC627A"),
            new Signature(0x060012ff, "Kingmaker.Visual.MaterialEffects.LayeredMaterial.LayeredMaterialController", "Update", false, "CA9A2C72C2999E23F1E5BCD7EBB845F459A224160169E5ECFA0BF2A2037D73DB"),
            new Signature(0x06001300, "Kingmaker.Visual.MaterialEffects.LayeredMaterial.LayeredMaterialController", "UpdateAdditionalMaterials", false, "0547CFFAFD19C8A3893341485764F510555DB847B64A97082B747A3E45B96E6C"),
            new Signature(0x06001301, "Kingmaker.Visual.MaterialEffects.LayeredMaterial.LayeredMaterialController", "UpdateMaterialProperties", false, "BD7C97FA8ABF329A4EA6A68FD02F2D560460FADEF454ADEC8AA25646E10B02BC"),
            new Signature(0x06001302, "Kingmaker.Visual.MaterialEffects.LayeredMaterial.LayeredMaterialController", "RemoveInvalidRendererData", false, "48F56318F979A590E6D3580F3E795263A01CA3E51AB7B0673651BDC8D5C07279"),
            new Signature(0x0600130a, "Kingmaker.Visual.MaterialEffects.LayeredMaterial.LayeredMaterialRenderer", "SetMaterials", false, "47E0F6FE3D19E4E87D7AAE03AE6AE540793BCA733AA125FFEAF4DBABACB07875"),
            new Signature(0x06001323, "Kingmaker.Visual.MaterialEffects.LayeredMaterial.ScriptPropertiesSnapshot", "CaptureDynamicProperties", false, "A63230FE7FECEBC44B45318D645D2480CECBBCD973933B9989F30D55CB3E9D21"),
            new Signature(0x06001347, "Kingmaker.Visual.MaterialEffects.LayeredMaterial.Timeline", "UpdatePlayingTracks", false, "716E735074B0DEF8187E2CCBD202840F7F60B1E1B951FDEA95644BDC5F747642"),
            new Signature(0x06001349, "Kingmaker.Visual.MaterialEffects.LayeredMaterial.Timeline", "GetPlayingTracks", false, "56D7950E5189552F72BBB76B1026937687586672FDA4F109F53EDB3162B040F0"),
            new Signature(0x0600134a, "Kingmaker.Visual.MaterialEffects.LayeredMaterial.Timeline", "GetPlayingTracksMaterials", false, "AFA6879682F40D97B8148C93DDE9F48DED504688C8987137450471AD76558628"),
            new Signature(0x0600134e, "Kingmaker.Visual.MaterialEffects.LayeredMaterial.Track", "Sample", false, "9999D788421E0829577063150D64DABA442C72FF20B456AC962D21E6C35E77D9"),
            new Signature(0x06001396, "Kingmaker.Visual.MaterialEffects.CustomMaterialProperty.CustomMaterialPropertyAnimationController", "RemoveAnimation", false, "B39A0C83B7BA1AA0F13B560C210E236EFE92D91D18E12B448E9C46599469D166"),
            new Signature(0x06001397, "Kingmaker.Visual.MaterialEffects.CustomMaterialProperty.CustomMaterialPropertyAnimationController", "Update", false, "8C5E3A60BB129C5E320DE3C9DBD1875F1D58B2BCDF776360F80D29D2955E3429"),
            new Signature(0x060013a1, "Kingmaker.Visual.MaterialEffects.CustomMaterialProperty.CustomMaterialPropertyAnimationController+PropertyAnimator", "AddClip", false, "DE78B5AD82C2494DC817F8204184668355B0BE2AB2E5423528C03ED794370DF7"),
            new Signature(0x060013a4, "Kingmaker.Visual.MaterialEffects.CustomMaterialProperty.CustomMaterialPropertyAnimationController+PropertyAnimator", "Update", false, "4405F01BD85C0B0EE3005DCCB64AC7B3173D5064A2784904E18D340E15CD37B6"),
            new Signature(0x060013a7, "Kingmaker.Visual.MaterialEffects.CustomMaterialProperty.CustomMaterialPropertyAnimationController+PropertyAnimator", "TrySample", false, "00C04CEE4AE2334005BDBA81A86383DB11E4942F18F9C7B6C8246BE867F7B603"),
            new Signature(0x060013b1, "Kingmaker.Visual.MaterialEffects.ColorTint.ColorTintAnimationController", "Update", false, "4F927E774C3CF964CDACF048E1E11443CE227C2A2783AA4DD30C093AC603E647"),
            new Signature(0x060013b3, "Kingmaker.Visual.MaterialEffects.ColorTint.ColorTintAnimationController", "UpdateAnimation", false, "D679C52D0F587095B5AC5B701580D98A0D68619786E2BF16A38DB93439B4D55F"),
            new Signature(0x06002200, "Kingmaker.View.StopAnimationsOnDestroy", "OnDisable", false, "E6E6CB918770C827C04DE1296B9BBD28E69B0EE5FE3932743DFD1CAAA30EDD27"),
        };
        internal static EngineCadenceContracts Create(Func<string, Type> lookup)
        {
            var wind = lookup("Kingmaker.Visual.WindController");
            if (wind == null || wind.Assembly.GetName().Name != "Code" || wind.Module.ModuleVersionId.ToString() != AuditedModule)
                throw new InvalidOperationException("Visual cadence requires the audited native game implementation");
            var targets = new List<MethodInfo>();
            var invalidators = new List<MethodInfo>();
            using (var sha = SHA256.Create())
            foreach (var signature in Signatures)
            {
                var method = wind.Module.ResolveMethod(signature.Token) as MethodInfo;
                if (method == null || method.Name != signature.Name || method.DeclaringType.FullName != signature.Type || method.IsStatic != signature.Static)
                    throw new MissingMethodException(signature.Type, signature.Name);
                byte[] body = method.GetMethodBody()?.GetILAsByteArray();
                if (body == null || BitConverter.ToString(sha.ComputeHash(body)).Replace("-", "") != signature.Hash)
                    throw new InvalidOperationException("Visual cadence dependency changed: " + signature.Type + "." + signature.Name);
                bool target = signature.Name == "Update" && (signature.Type == "Kingmaker.Visual.WindController" ||
                    signature.Type.EndsWith(".LayeredMaterialController", StringComparison.Ordinal) ||
                    signature.Type.EndsWith(".CustomMaterialPropertyAnimationController", StringComparison.Ordinal) ||
                    signature.Type.EndsWith(".ColorTintAnimationController", StringComparison.Ordinal) ||
                    signature.Type.EndsWith(".RimLightingAnimationController", StringComparison.Ordinal));
                bool invalidator = signature.Name == "ApplyMaterialAnimations" ||
                    (signature.Type == "Kingmaker.View.StopAnimationsOnDestroy" && signature.Name == "OnDisable") ||
                    (signature.Type == "Kingmaker.Visual.MaterialEffects.StandardMaterialController" && signature.Name == "SetupMaterials");
                if (target)
                {
                    if (method.IsStatic || method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
                        throw new InvalidOperationException("Visual cadence Update signature changed");
                    targets.Add(method);
                }
                if (invalidator) invalidators.Add(method);
            }
            if (targets.Count != 5 || invalidators.Count != 3)
                throw new InvalidOperationException("Incomplete visual cadence targets or material lifecycle coverage");
            return new EngineCadenceContracts { Targets = targets.ToArray(), Invalidators = invalidators.ToArray() };
        }
    }
}
