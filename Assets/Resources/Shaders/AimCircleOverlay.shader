Shader "Katabasis/AimCircleOverlay"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 0.9)
        _Thickness("Thickness", Range(0, 0.5)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "AimCircleOverlay"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZTest Always
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Thickness;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float distanceFromCenter = length(input.uv - float2(0.5, 0.5));
                float antialiasing = max(fwidth(distanceFromCenter), 0.0005);
                float innerRadius = max(0.0, 0.5 - _Thickness);
                float outerMask = 1.0 - smoothstep(0.5 - antialiasing, 0.5, distanceFromCenter);
                float innerMask = smoothstep(
                    innerRadius - antialiasing,
                    innerRadius,
                    distanceFromCenter);
                half4 result = _BaseColor;
                result.a *= outerMask * innerMask;
                return result;
            }
            ENDHLSL
        }
    }
}
