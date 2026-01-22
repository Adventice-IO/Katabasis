Shader "Custom/GridShader"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _CheckerSize("Checker Size", Float) = 1.0
        _Thickness("Line Thickness", Range(0, 0.5)) = 0.05
        _Falloff("Falloff", Range(0, 1)) = 0.1
        _Radius("Radius", Float) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

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
                UNITY_VERTEX_OUTPUT_STEREO // Required for VR Single Pass
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _CheckerSize;
                float _Thickness;
                float _Falloff;
                float _Radius;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN); 
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 color = _BaseColor;

                float2 scaledUV = IN.uv / _CheckerSize;
                float2 grid = abs(frac(scaledUV - 0.5) - 0.5) / fwidth(scaledUV);
                float gline = min(grid.x, grid.y);
                float alpha = smoothstep( _Thickness + _Falloff, _Thickness, gline);
                if(alpha == 0) discard;

                float dist = length(IN.uv - float2(0.5, 0.5)) / _Radius;
                if (dist > 1) discard;

                float edgeFade = smoothstep(1,0, dist);
                alpha *= edgeFade;

                color.a *=  alpha;
                return color;
            }
            ENDHLSL
        }
    }
}
