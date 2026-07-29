// BCG Building Generator — fake-interiors facade shader (Built-in + URP 17 / Unity 6).
// Parallax "room behind the glass" interior mapping derived purely from the facade strip-atlas UV,
// so it auto-aligns on every generated building with no per-building material params.
//   Window glass  (mask.r = 1) : shows a parallax interior room, flat normal, raised smoothness, night glow.
//   Optional _SpecGlossMap (keyword _SPECGLOSSMAP, bound by Fix Materials): RGB = specular color,
//   A = per-texel smoothness — replaces the scalar wall/glass smoothness lerp on both SubShaders.
//   Wall / roof / doors (mask.r = 0) : plain lit albedo (storefront doors stay opaque).
// Interior math lives in the shared BCG_InteriorMapping.hlsl (both SubShaders include it).
// HDRP is intentionally NOT provided — FallBack "Standard" keeps HDRP (and Built-in shadow casting) on stock Lit.
Shader "BCG/BuildingGen/FacadeInterior" {

    Properties {
        _MainTex ("Albedo Atlas", 2D) = "white" {}
        _BumpMap ("Normal Atlas", 2D) = "bump" {}
        _SpecGlossMap ("Specular (RGB) Smoothness (A)", 2D) = "black" {}
        _EmissionMap ("Emission Atlas", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.12
        _MaskTex ("Window Mask (R, linear)", 2D) = "black" {}
        _RoomAtlas ("Interior Room Atlas", 2D) = "gray" {}
        _RoomDepth ("Room Depth (match bake = 0.5)", Range(0.05, 0.95)) = 0.5
        _InteriorLitFraction ("Lit Room Fraction (night)", Range(0,1)) = 0.35
        _GlassSmoothness ("Glass Smoothness", Range(0,1)) = 0.6
        _InteriorVisibility ("Interior Visibility (day)", Range(0,1)) = 0.45
        _CurtainFraction ("Curtained Window Fraction", Range(0,1)) = 0.30
    }

    // ============================ URP 17 (Unity 6) ============================
    //  SRP-Batcher-compatible: every material scalar/color lives in the UnityPerMaterial CBUFFER.
    //  Passes: UniversalForward + ShadowCaster + DepthOnly + DepthNormalsOnly + Meta.
    SubShader {
        //  Unity compiles every SubShader regardless of the active pipeline, so without this gate
        //  a project that lacks the URP package logs 5 include-not-found errors (one per pass).
        //  Unmet requirement = this whole SubShader is skipped; the Built-in SubShader still renders.
        //  Any-version on purpose: the asset's Unity floor already pins URP >= 17, and a version
        //  range here would fail silently (skipped SubShader) instead of loudly on a mismatch.
        PackageRequirements {
            "com.unity.render-pipelines.universal"
        }
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma shader_feature_local_fragment _SPECGLOSSMAP

            // URP 17 forward lighting / shadows / GI / fog (Unity 6 default renderer is Forward+).
            //  _ADDITIONAL_LIGHTS_VERTEX is deliberately NOT declared: this pass never feeds
            //  inputData.vertexLighting, so the variant only cost compile time (city filler -
            //  the Built-in SubShader skips point/spots entirely; URP per-pixel/Forward+ light them).
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            //  _CLUSTER_LIGHT_LOOP is the URP 17.1+ (Unity 6000.1+) name for the Forward+ keyword;
            //  the deprecated _FORWARD_PLUS alias would warn and dodge variant prefiltering. Fine
            //  because the store minimum is the 6000.3 upload baseline - do NOT revert this to
            //  support 6000.0 without also re-adding _FORWARD_PLUS.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            //  Specular workflow: UniversalFragmentPBR consumes surfaceData.specular as F0.
            //  Without a SpecGloss map we feed the dielectric default 0.04, which matches the
            //  previous metallic-0 look, so keyword-off rendering is unchanged.
            #define _SPECULAR_SETUP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "BCG_InteriorMapping.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _EmissionColor;
                float  _Glossiness;
                float  _RoomDepth;
                float  _InteriorLitFraction;
                float  _GlassSmoothness;
                float  _InteriorVisibility;
                float  _CurtainFraction;
            CBUFFER_END

            //  Declared as sampler2D so the shared include's tex2D/sampler2D calls compile identically
            //  under both pipelines. Textures stay OUTSIDE the CBUFFER (SRP Batcher requirement).
            sampler2D _MainTex;
            sampler2D _BumpMap;
            sampler2D _EmissionMap;
            sampler2D _MaskTex;
            sampler2D _RoomAtlas;
            sampler2D _SpecGlossMap;

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 tangentWS  : TEXCOORD3;   // xyz = tangent, w = sign
                half   fogFactor  : TEXCOORD4;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 5);
            };

            Varyings vert (Attributes IN) {
                Varyings OUT = (Varyings)0;

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.uv         = IN.uv;
                OUT.normalWS   = nrmInputs.normalWS;
                OUT.tangentWS  = float4(nrmInputs.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
                OUT.fogFactor  = ComputeFogFactor(posInputs.positionCS.z);

                OUTPUT_LIGHTMAP_UV(IN.lightmapUV, unity_LightmapST, OUT.staticLightmapUV);
                OUTPUT_SH(OUT.normalWS.xyz, OUT.vertexSH);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target {
                float2 uv = IN.uv;

                half  mask   = tex2D(_MaskTex, uv).r;
                half3 albedo = tex2D(_MainTex, uv).rgb;

                //  Tangent basis (world space).
                float3 normalWS    = normalize(IN.normalWS);
                float3 tangentWS   = normalize(IN.tangentWS.xyz);
                float3 bitangentWS = normalize(cross(normalWS, tangentWS) * IN.tangentWS.w);

                //  Camera -> fragment direction expressed in tangent space (unnormalized; the include normalizes).
                float3 camToFrag = IN.positionWS - _WorldSpaceCameraPos;
                float3 viewTS;
                viewTS.x = dot(camToFrag, tangentWS);
                viewTS.y = dot(camToFrag, bitangentWS);
                viewTS.z = dot(camToFrag, normalWS);

                float  litHash, openness;
                half3  interior = BCG_SampleInterior(_RoomAtlas, uv, viewTS, _RoomDepth,
                                                     _CurtainFraction, litHash, openness);

                //  Composite the room BEHIND the tinted glass, not instead of it: the dark-glass
                //  albedo stays the base layer. Fresnel (geometric facade normal) collapses grazing
                //  views back to reflective glass; per-window openness varies clarity / curtains.
                float3 V = normalize(_WorldSpaceCameraPos - IN.positionWS);
                half   fresnel    = pow(1.0 - saturate(dot(V, normalWS)), 4.0);
                half   visibility = mask * openness * (1.0 - fresnel) * _InteriorVisibility;
                albedo = lerp(albedo, interior, visibility);

                //  Flat glass normal + glossier glass where masked.
                half3 normalTS   = lerp(UnpackNormal(tex2D(_BumpMap, uv)), half3(0, 0, 1), mask);
                //  SpecGloss map (RGB = specular color, A = smoothness) REPLACES the scalar
                //  wall/glass smoothness lerp when bound; keyword off keeps the legacy scalars.
                half3 specColor;
                half  smoothness;
            #if defined(_SPECGLOSSMAP)
                half4 specGloss = tex2D(_SpecGlossMap, uv);
                specColor  = specGloss.rgb;
                smoothness = specGloss.a;
            #else
                specColor  = half3(0.04, 0.04, 0.04);
                smoothness = lerp(_Glossiness, _GlassSmoothness, mask);
            #endif
                float3 N = normalize(mul(normalTS, float3x3(tangentWS, bitangentWS, normalWS)));

                //  Emission: authored emission + a per-cell night interior glow (only some rooms lit).
                half3 emis     = tex2D(_EmissionMap, uv).rgb * _EmissionColor.rgb;
                half  nightLit = step(litHash, _InteriorLitFraction) * mask;
                emis += interior * _EmissionColor.rgb * nightLit * 0.6;

                InputData inputData = (InputData)0;
                inputData.positionWS               = IN.positionWS;
                inputData.normalWS                 = N;
                inputData.viewDirectionWS          = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord              = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord                 = IN.fogFactor;
                inputData.normalizedScreenSpaceUV  = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.bakedGI                  = SAMPLE_GI(IN.staticLightmapUV, IN.vertexSH, N);
                //  Shadowmask / subtractive mixed lighting: the macro resolves per-variant (lightmap
                //  shadowmask, probe occlusion, or fully-unoccluded 1). Without it lightmapped facades
                //  lose all baked shadows past the realtime shadow distance in Shadowmask mode.
                inputData.shadowMask               = SAMPLE_SHADOWMASK(IN.staticLightmapUV);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo     = albedo;
                surfaceData.metallic   = 0.0;
                surfaceData.specular   = specColor;
                surfaceData.smoothness = smoothness;
                surfaceData.normalTS   = normalTS;
                surfaceData.emission   = emis;
                surfaceData.occlusion  = 1.0;
                surfaceData.alpha      = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, IN.fogFactor);
                return half4(color.rgb, 1.0);
            }
            ENDHLSL
        }

        //  ShadowCaster — standard URP position-only boilerplate.
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma target 3.0
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _EmissionColor;
                float  _Glossiness;
                float  _RoomDepth;
                float  _InteriorLitFraction;
                float  _GlassSmoothness;
                float  _InteriorVisibility;
                float  _CurtainFraction;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            float4 GetShadowPositionHClip(Attributes input) {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            Varyings ShadowPassVertex (Attributes input) {
                Varyings output;
                output.positionCS = GetShadowPositionHClip(input);
                return output;
            }

            half4 ShadowPassFragment (Varyings input) : SV_TARGET { return 0; }
            ENDHLSL
        }

        //  DepthOnly — standard URP position-only boilerplate (depth prepass / SSAO).
        Pass {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _EmissionColor;
                float  _Glossiness;
                float  _RoomDepth;
                float  _InteriorLitFraction;
                float  _GlassSmoothness;
                float  _InteriorVisibility;
                float  _CurtainFraction;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings DepthOnlyVertex (Attributes input) {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthOnlyFragment (Varyings input) : SV_TARGET { return 0; }
            ENDHLSL
        }

        //  DepthNormalsOnly — writes world-space normals for the DepthNormals prepass (SSAO's
        //  "Depth Normals" source, decals). Without it the facades are absent from the normals
        //  texture and AO breaks around them. Tag is "DepthNormalsOnly" (not "DepthNormals"):
        //  URP's partial-prepass path matches only the former; the full prepass matches both.
        Pass {
            Name "DepthNormalsOnly"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _EmissionColor;
                float  _Glossiness;
                float  _RoomDepth;
                float  _InteriorLitFraction;
                float  _GlassSmoothness;
                float  _InteriorVisibility;
                float  _CurtainFraction;
            CBUFFER_END

            sampler2D _BumpMap;
            sampler2D _MaskTex;

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 tangentWS  : TEXCOORD2;   // xyz = tangent, w = sign
            };

            Varyings DepthNormalsVertex (Attributes IN) {
                Varyings OUT = (Varyings)0;
                VertexNormalInputs nrmInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = IN.uv;
                OUT.normalWS   = nrmInputs.normalWS;
                OUT.tangentWS  = float4(nrmInputs.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
                return OUT;
            }

            //  Same normal as ForwardLit: bump-mapped walls flattened to the glass plane where
            //  masked (keep the lerp in lockstep with the forward passes of both SubShaders).
            half4 DepthNormalsFragment (Varyings IN) : SV_Target {
                half   mask        = tex2D(_MaskTex, IN.uv).r;
                float3 normalWS    = normalize(IN.normalWS);
                float3 tangentWS   = normalize(IN.tangentWS.xyz);
                float3 bitangentWS = normalize(cross(normalWS, tangentWS) * IN.tangentWS.w);
                half3  normalTS    = lerp(UnpackNormal(tex2D(_BumpMap, IN.uv)), half3(0, 0, 1), mask);
                float3 N = normalize(mul(normalTS, float3x3(tangentWS, bitangentWS, normalWS)));
                return half4(NormalizeNormalPerPixel(N), 0.0);
            }
            ENDHLSL
        }

        //  Meta — lightmapper albedo/emission extraction. The Progressive Lightmapper renders this
        //  pass to learn what each texel reflects/emits; without it baked GI treats the facade as
        //  black and the night-window glow never reaches neighbouring surfaces.
        Pass {
            Name "Meta"
            Tags { "LightMode"="Meta" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex MetaVert
            #pragma fragment MetaFrag
            #pragma target 3.0
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _EmissionColor;
                float  _Glossiness;
                float  _RoomDepth;
                float  _InteriorLitFraction;
                float  _GlassSmoothness;
                float  _InteriorVisibility;
                float  _CurtainFraction;
            CBUFFER_END

            sampler2D _MainTex;
            sampler2D _EmissionMap;

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv0        : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                float2 uv2        : TEXCOORD2;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            #ifdef EDITOR_VISUALIZATION
                float2 VizUV      : TEXCOORD1;
                float4 LightCoord : TEXCOORD2;
            #endif
            };

            Varyings MetaVert (Attributes IN) {
                Varyings OUT = (Varyings)0;
                OUT.positionCS = UnityMetaVertexPosition(IN.positionOS.xyz, IN.uv1, IN.uv2);
                OUT.uv = IN.uv0;
            #ifdef EDITOR_VISUALIZATION
                UnityEditorVizData(IN.positionOS.xyz, IN.uv0, IN.uv1, IN.uv2, OUT.VizUV, OUT.LightCoord);
            #endif
                return OUT;
            }

            //  Albedo = plain atlas colour; emission = the authored window-band glow. The parallax
            //  interior and its per-cell night lights are view-dependent and deliberately NOT baked
            //  (keep in lockstep with the Built-in Meta pass).
            half4 MetaFrag (Varyings IN) : SV_Target {
                UnityMetaInput meta = (UnityMetaInput)0;
                meta.Albedo = tex2D(_MainTex, IN.uv).rgb;
                meta.Emission = tex2D(_EmissionMap, IN.uv).rgb * _EmissionColor.rgb;
            #ifdef EDITOR_VISUALIZATION
                meta.VizUV = IN.VizUV;
                meta.LightCoord = IN.LightCoord;
            #endif
                return UnityMetaFragment(meta);
            }
            ENDHLSL
        }
    }

    // ============================ Built-in (fallback, LAST) ============================
    //  ForwardBase: ambient SH + main directional light + shadow attenuation. Point/spot lights are
    //  intentionally ignored (city filler). FallBack "Standard" supplies the Built-in ShadowCaster pass.
    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass {
            Name "ForwardBase"
            Tags { "LightMode"="ForwardBase" }
            Cull Back
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fwdbase
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _SPECGLOSSMAP

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            #include "BCG_InteriorMapping.hlsl"

            sampler2D _MainTex;
            sampler2D _BumpMap;
            sampler2D _EmissionMap;
            sampler2D _MaskTex;
            sampler2D _RoomAtlas;
            sampler2D _SpecGlossMap;
            float4 _EmissionColor;
            float  _Glossiness;
            float  _RoomDepth;
            float  _InteriorLitFraction;
            float  _GlassSmoothness;
            float  _InteriorVisibility;
            float  _CurtainFraction;

            struct appdata {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                float4 tangent : TANGENT;
                float2 uv      : TEXCOORD0;
            };

            struct v2f {
                float4 pos         : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 tangentWS   : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float3 worldPos    : TEXCOORD4;
                SHADOW_COORDS(5)
                UNITY_FOG_COORDS(6)
            };

            v2f vert (appdata v) {
                v2f o;
                o.pos         = UnityObjectToClipPos(v.vertex);
                o.uv          = v.uv;
                o.worldPos    = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normalWS    = UnityObjectToWorldNormal(v.normal);
                o.tangentWS   = UnityObjectToWorldDir(v.tangent.xyz);
                o.bitangentWS = cross(o.normalWS, o.tangentWS) * v.tangent.w * unity_WorldTransformParams.w;
                TRANSFER_SHADOW(o)
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                float2 uv = i.uv;

                half  mask   = tex2D(_MaskTex, uv).r;
                half3 albedo = tex2D(_MainTex, uv).rgb;

                float3 normalWS    = normalize(i.normalWS);
                float3 tangentWS   = normalize(i.tangentWS);
                float3 bitangentWS = normalize(i.bitangentWS);

                float3 camToFrag = i.worldPos - _WorldSpaceCameraPos;
                float3 viewTS;
                viewTS.x = dot(camToFrag, tangentWS);
                viewTS.y = dot(camToFrag, bitangentWS);
                viewTS.z = dot(camToFrag, normalWS);

                float  litHash, openness;
                half3  interior = BCG_SampleInterior(_RoomAtlas, uv, viewTS, _RoomDepth,
                                                     _CurtainFraction, litHash, openness);

                //  Same glass-preserving composite as the URP SubShader (keep in lockstep).
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                half   fresnel    = pow(1.0 - saturate(dot(V, normalWS)), 4.0);
                half   visibility = mask * openness * (1.0 - fresnel) * _InteriorVisibility;
                albedo = lerp(albedo, interior, visibility);

                half3  normalTS = lerp(UnpackNormal(tex2D(_BumpMap, uv)), half3(0, 0, 1), mask);
                float3 N = normalize(mul(normalTS, float3x3(tangentWS, bitangentWS, normalWS)));

                float3 L      = normalize(_WorldSpaceLightPos0.xyz);
                half   ndl    = saturate(dot(N, L));
                half   shadow = SHADOW_ATTENUATION(i);
                half3  ambient  = ShadeSH9(float4(N, 1.0));
                half3  lighting = ambient + _LightColor0.rgb * (ndl * shadow);

                //  SpecGloss map (RGB = specular color, A = smoothness) REPLACES the legacy
                //  no-specular look when bound; keyword off keeps a near-zero dielectric
                //  highlight that reads flat, matching the previous Lambert-only output.
                //  (Keep the spec-color/smoothness split in lockstep with the URP SubShader.)
                half3 specColor;
                half  smoothness;
            #if defined(_SPECGLOSSMAP)
                half4 specGloss = tex2D(_SpecGlossMap, uv);
                specColor  = specGloss.rgb;
                smoothness = specGloss.a;
            #else
                specColor  = half3(0.04, 0.04, 0.04);
                smoothness = lerp(_Glossiness, _GlassSmoothness, mask);
            #endif

                //  Blinn-Phong specular from the main directional light (city filler: point/spot
                //  lights stay diffuse-only, matching the pass's existing lighting model).
                half3 H        = normalize(L + V);
                half  specPow  = exp2(smoothness * 10.0 + 1.0);
                half3 specular = _LightColor0.rgb * specColor
                               * pow(saturate(dot(N, H)), specPow) * shadow * step(0.001, ndl);

                half3 emis     = tex2D(_EmissionMap, uv).rgb * _EmissionColor.rgb;
                half  nightLit = step(litHash, _InteriorLitFraction) * mask;
                emis += interior * _EmissionColor.rgb * nightLit * 0.6;

                half3 rgb = albedo * lighting + specular + emis;
                UNITY_APPLY_FOG(i.fogCoord, rgb);
                return fixed4(rgb, 1.0);
            }
            ENDCG
        }

        //  Meta — lightmapper albedo/emission extraction (kept in lockstep with the URP Meta pass).
        //  FallBack "Standard" would lend one, but Standard's meta gates emission on the _EMISSION
        //  keyword, which the interior materials never enable — so the glow must be emitted here.
        Pass {
            Name "Meta"
            Tags { "LightMode"="Meta" }
            Cull Off

            CGPROGRAM
            #pragma vertex MetaVert
            #pragma fragment MetaFrag
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "UnityCG.cginc"
            #include "UnityMetaPass.cginc"

            sampler2D _MainTex;
            sampler2D _EmissionMap;
            float4 _EmissionColor;

            struct v2f_bcgmeta {
                float4 pos        : SV_POSITION;
                float2 uv         : TEXCOORD0;
            #ifdef EDITOR_VISUALIZATION
                float2 vizUV      : TEXCOORD1;
                float4 lightCoord : TEXCOORD2;
            #endif
            };

            v2f_bcgmeta MetaVert (appdata_full v) {
                v2f_bcgmeta o;
                UNITY_INITIALIZE_OUTPUT(v2f_bcgmeta, o);
                o.pos = UnityMetaVertexPosition(v.vertex, v.texcoord1.xy, v.texcoord2.xy, unity_LightmapST, unity_DynamicLightmapST);
                o.uv = v.texcoord.xy;
            #ifdef EDITOR_VISUALIZATION
                o.vizUV = 0;
                o.lightCoord = 0;
                if (unity_VisualizationMode == EDITORVIZ_TEXTURE)
                    o.vizUV = UnityMetaVizUV(unity_EditorViz_UVIndex, v.texcoord.xy, v.texcoord1.xy, v.texcoord2.xy, unity_EditorViz_Texture_ST);
                else if (unity_VisualizationMode == EDITORVIZ_SHOWLIGHTMASK) {
                    o.vizUV = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
                    o.lightCoord = mul(unity_EditorViz_WorldToLight, mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)));
                }
            #endif
                return o;
            }

            half4 MetaFrag (v2f_bcgmeta i) : SV_Target {
                UnityMetaInput o;
                UNITY_INITIALIZE_OUTPUT(UnityMetaInput, o);
                o.Albedo = tex2D(_MainTex, i.uv).rgb;
                o.Emission = tex2D(_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
            #ifdef EDITOR_VISUALIZATION
                o.VizUV = i.vizUV;
                o.LightCoord = i.lightCoord;
            #endif
                return UnityMetaFragment(o);
            }
            ENDCG
        }
    }

    FallBack "Standard"
}
