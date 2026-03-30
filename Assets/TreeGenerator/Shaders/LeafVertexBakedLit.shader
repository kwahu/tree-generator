Shader "TreeGenerator/Leaf Vertex Baked Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)

        [Header(Baked lighting in vertex color)]
        _GIInfluence("Vertex GI Influence", Range(0, 1)) = 1
        _GIContrast("GI Contrast", Range(0, 2)) = 1
        _GIBrightness("GI Brightness", Range(-1, 1)) = 0

        [Header(Leaf Wind)]
        _LeafWindEnabled("Leaf Wind Enabled", Float) = 0
        _LeafWindStrength("Wind Strength", Float) = 0.12
        _LeafWindFrequency("Wind Frequency", Float) = 2
        _LeafWindTurbulence("Wind Turbulence", Range(0, 2)) = 0.65
        _LeafWindPhaseScale("Wind Phase Scale", Float) = 2.5
        _LeafWindMaskExponent("Tip Mask Exponent", Range(0.25, 4)) = 2
        _LeafWindDirection("Wind Direction (world)", Vector) = (1, 0.05, 0.3, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "UniversalMaterialType" = "Lit"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend One Zero
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "LeafWind.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4 vertexColor : TEXCOORD2;
                float fogCoord    : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float _GIInfluence;
                float _GIContrast;
                float _GIBrightness;
                float _LeafWindEnabled;
                float _LeafWindStrength;
                float _LeafWindFrequency;
                float _LeafWindTurbulence;
                float _LeafWindPhaseScale;
                float _LeafWindMaskExponent;
                float4 _LeafWindDirection;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // Vertex colors hold linear irradiance (HDR). Do not use (gi - 0.5) — that is for ~LDR lightmap GI tuning.
            inline half3 ApplyVertexBakedGIControls(half3 gi)
            {
                half3 t = gi * _GIContrast + half3(_GIBrightness, _GIBrightness, _GIBrightness);
                return max(t, 0.0h);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                posWS = TreeGeneratorApplyLeafWindWS(
                    posWS,
                    input.uv,
                    _LeafWindEnabled,
                    _LeafWindStrength,
                    _LeafWindFrequency,
                    _LeafWindTurbulence,
                    _LeafWindPhaseScale,
                    _LeafWindMaskExponent,
                    _LeafWindDirection.xyz,
                    _Time.y);

                output.positionCS = TransformWorldToHClip(posWS);
                output.positionWS = posWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.vertexColor = half4(input.color);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 baseCol = tex * _BaseColor;

                half3 baked = input.vertexColor.rgb;
                half3 tunedGI = ApplyVertexBakedGIControls(baked);
                half3 giMul = lerp(1.0h.xxx, tunedGI, _GIInfluence);

                half3 finalRGB = baseCol.rgb * giMul;
                finalRGB = MixFog(finalRGB, input.fogCoord);

                return half4(finalRGB, baseCol.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertLeaf
            #pragma fragment ShadowFragLeaf

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "LeafWind.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float _GIInfluence;
                float _GIContrast;
                float _GIBrightness;
                float _LeafWindEnabled;
                float _LeafWindStrength;
                float _LeafWindFrequency;
                float _LeafWindTurbulence;
                float _LeafWindPhaseScale;
                float _LeafWindMaskExponent;
                float4 _LeafWindDirection;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct ShadowAtt
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct ShadowVar
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            ShadowVar ShadowVertLeaf(ShadowAtt input)
            {
                ShadowVar output;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                posWS = TreeGeneratorApplyLeafWindWS(
                    posWS,
                    input.uv,
                    _LeafWindEnabled,
                    _LeafWindStrength,
                    _LeafWindFrequency,
                    _LeafWindTurbulence,
                    _LeafWindPhaseScale,
                    _LeafWindMaskExponent,
                    _LeafWindDirection.xyz,
                    _Time.y);
                output.positionCS = TransformWorldToHClip(posWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 ShadowFragLeaf(ShadowVar input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex UniversalVertexMeta
            #pragma fragment LeafFragmentMeta

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float _GIInfluence;
                float _GIContrast;
                float _GIBrightness;
                float _LeafWindEnabled;
                float _LeafWindStrength;
                float _LeafWindFrequency;
                float _LeafWindTurbulence;
                float _LeafWindPhaseScale;
                float _LeafWindMaskExponent;
                float4 _LeafWindDirection;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UniversalMetaPass.hlsl"

            half4 LeafFragmentMeta(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 baseCol = tex * _BaseColor;

                MetaInput metaInput;
                metaInput.Albedo = baseCol.rgb;
                metaInput.Emission = half3(0, 0, 0);
                return UniversalFragmentMeta(input, metaInput);
            }
            ENDHLSL
        }
    }
}
