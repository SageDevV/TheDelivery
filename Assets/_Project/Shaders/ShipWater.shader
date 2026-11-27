// Água parada do porão inundado (Ato 5, 2026-11-26). Superfície escura e lisa, iluminada pela URP (PBR): no breu só
// aparece onde a lanterna do celular ou um LED bate. O relevo é ANALÍTICO, sem textura: (1) ondulação lenta e sutil da
// água "viva"; (2) até 24 anéis de gota (_Ripples: x, z, instante, amplitude), cada um um pacote de 2–3 cristas que se
// abre a _RippleSpeed m/s, alarga e perde força — o brilho da lanterna escorre pelas cristas como na água de verdade.
Shader "TheDelivery/ShipWater"
{
    Properties
    {
        _BaseColor("Cor", Color) = (0.06, 0.07, 0.065, 1)
        _Smoothness("Suavidade", Range(0, 1)) = 0.93
        _Metallic("Metálico", Range(0, 1)) = 0
        _WaveStrength("Ondulação ambiente", Float) = 0.5
        _RippleStrength("Força dos anéis", Float) = 1
        _RippleSpeed("Velocidade dos anéis (m/s)", Float) = 0.32
        _RippleFreq("Frequência das cristas", Float) = 55
        _RippleLife("Vida do anel (s)", Float) = 2.4
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        float4 _ShipSlosh; // amplitude and world X of the flooded hold center
        float3 SloshingPosition(float3 positionOS)
        {
            float3 p = TransformObjectToWorld(positionOS);
            p.y += (p.x - _ShipSlosh.y) * _ShipSlosh.x * 0.0035;
            return p;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Smoothness, _Metallic;
                float _WaveStrength, _RippleStrength, _RippleSpeed, _RippleFreq, _RippleLife;
            CBUFFER_END
            float4 _Ripples[24];
            float _RippleNow;
            float4 _Wader;        // quem anda na água: x, z, intensidade (0 = parada/fora), direção do passo em w (radianos)     // relógio do script (o mesmo dos instantes em _Ripples)

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float fogFactor : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = SloshingPosition(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // gradiente (dh/dx, dh/dz) da superfície
            // gradiente (dh/dx, dh/dz) da superfície; crest = altura das cristas dos anéis (para o brilho difuso)
            float2 SurfaceGradient(float2 p, float t, out float crest)
            {
                float2 g = 0; crest = 0;
                // água "viva": ondas lentas e longas, quase imperceptíveis
                float a1 = p.x * 1.3 + p.y * 0.45 + t * 0.55;
                float a2 = p.y * 1.9 - p.x * 0.7 - t * 0.4;
                float a3 = (p.x + p.y) * 3.7 + t * 0.9;
                g += float2(1.3, 0.45) * cos(a1) * 0.010;
                g += float2(-0.7, 1.9) * cos(a2) * 0.008;
                g += float2(3.7, 3.7) * cos(a3) * 0.0025;
                g *= _WaveStrength;
                // Long returning swell under the existing drip rings and footsteps.
                float wave = (p.x - _ShipSlosh.y) * 1.2 + 0.28 * sin(p.y * 0.8) + _ShipSlosh.x * 1.4;
                g.x += _ShipSlosh.x * (0.0035 + 0.06 * cos(wave));
                crest += 0.055 * max(0.0, _ShipSlosh.x * sin(wave));

                // ondas contínuas saindo das pernas de quem anda na água (acompanham a caminhada)
                if (_Wader.z > 0.001)
                {
                    float2 d = p - _Wader.xy;
                    float dist = length(d) + 1e-4;
                    float2 dir = float2(sin(_Wader.w), cos(_Wader.w));
                    float ahead = 0.65 + 0.35 * dot(d / dist, dir);              // mais forte à frente (a água empurrada)
                    float env = exp(-dist * 3.4) * saturate(dist * 6.0) * _Wader.z * ahead;
                    float ang = atan2(d.y, d.x);
                    // irregular: a frente da onda treme com o ângulo e o tempo (pernas não são um ponto)
                    float ph = dist * 44.0 - _RippleNow * 7.5 + sin(ang * 3.0 + _RippleNow * 1.3) * 0.9 + sin(ang * 7.0 - _RippleNow * 2.1) * 0.35;
                    float breakup = 0.6 + 0.4 * sin(ang * 5.0 + dist * 9.0 + _RippleNow * 0.7);
                    g += (d / dist) * env * cos(ph) * 0.26 * breakup;
                    crest += env * sin(ph) * 0.75 * breakup;
                }

                [unroll]
                for (int k = 0; k < 24; k++)
                {
                    float4 r = _Ripples[k];
                    float age = _RippleNow - r.z;
                    if (age <= 0.0 || age >= _RippleLife) continue;
                    float2 d = p - r.xy;
                    float dist = length(d) + 1e-4;
                    float front = age * _RippleSpeed + 0.01;
                    float x = dist - front;                                   // < 0 = atrás da frente (onde ficam as cristas)
                    float w = 0.025 + 0.07 * age;                             // o pacote alarga com o tempo
                    float env = exp(-(x * x) / (w * w)) * step(x, w * 1.2);   // nada na frente da frente de onda
                    float life = 1.0 - age / _RippleLife;
                    float amp = r.w * life * life / (1.0 + dist * 2.5);
                    float freq = _RippleFreq / (1.0 + age * 0.6);             // cristas se espaçam ao abrir
                    float dhdr = amp * env * freq * cos(x * freq) * 0.03;
                    g += (d / dist) * dhdr * _RippleStrength;
                    crest += amp * env * sin(x * freq);
                }
                return g;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float crest;
                float2 g = SurfaceGradient(i.positionWS.xz, t, crest);
                float3 n = normalize(float3(-g.x, 1.0, -g.y));

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                // a crista do anel pega mais luz (lâmina d'água mais inclinada / espuma fina): o anel se desenha na lanterna
                s.albedo = _BaseColor.rgb * (1.0 + saturate(crest) * 5.0 * _RippleStrength);
                s.metallic = _Metallic;
                s.smoothness = _Smoothness;
                s.occlusion = 1;
                s.alpha = 1;

                half4 c = UniversalFragmentPBR(inputData, s);
                c.rgb = MixFog(c.rgb, i.fogFactor);
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformWorldToHClip(SloshingPosition(v.positionOS.xyz)); return o; }
            half frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
