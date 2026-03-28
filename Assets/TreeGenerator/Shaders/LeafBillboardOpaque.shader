Shader "TreeGenerator/Leaf Billboard Opaque"
{
    Properties
    {
        [MainTexture] _BaseMap("Sprite", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)

        [Header(Leaf wind)]
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
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Off
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "LeafWind.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _LeafWindEnabled;
                float _LeafWindStrength;
                float _LeafWindFrequency;
                float _LeafWindTurbulence;
                float _LeafWindPhaseScale;
                float _LeafWindMaskExponent;
                float4 _LeafWindDirection;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 centerOS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 centerWS = TransformObjectToWorld(input.centerOS);
                float3 camWS = _WorldSpaceCameraPos;
                float3 viewDirWS = normalize(camWS - centerWS);

                float3 upWS = abs(viewDirWS.y) < 0.999f ? float3(0, 1, 0) : float3(0, 0, 1);
                float3 rightWS = normalize(cross(upWS, viewDirWS));
                upWS = normalize(cross(viewDirWS, rightWS));

                float3 cornerOffsetOS = input.positionOS.xyz;
                float3 worldPos = centerWS + rightWS * cornerOffsetOS.x + upWS * cornerOffsetOS.y;

                worldPos = TreeGeneratorApplyLeafWindWS(
                    worldPos,
                    input.uv,
                    _LeafWindEnabled,
                    _LeafWindStrength,
                    _LeafWindFrequency,
                    _LeafWindTurbulence,
                    _LeafWindPhaseScale,
                    _LeafWindMaskExponent,
                    _LeafWindDirection.xyz,
                    _Time.y);

                output.positionCS = TransformWorldToHClip(worldPos);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
