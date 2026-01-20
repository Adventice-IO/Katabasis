Shader "Katabasis/DottedLine"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [Gap] _Gap("Gap", Range(0.001, .01)) = 0.1
        [Round] _Round("Round Corners", Range(0,1))= 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

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

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                float _Gap;
                float _Round;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                // FIX 1: Initialize the struct directly to 0 (replaces UNITY_INITIALIZE_OUTPUT)
                Varyings OUT = (Varyings)0; 

                UNITY_SETUP_INSTANCE_ID(IN); 
                // UNITY_INITIALIZE_OUTPUT(Varyings, OUT); // <--- DELETE THIS LINE
                
                // FIX 2: This is still required for VR!
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float modPos = fmod(IN.uv.x, _Gap) * 2.0 / _Gap;
                
                if(modPos > 1)
                {
                    discard;
                }

                // Round corners logic
                float rc = _Round / 2.0;
                float2 relUV = float2(modPos, IN.uv.y);
                
                if(relUV.x < rc)
                {
                    if(relUV.y < rc)
                    {
                        float dist = distance(relUV, float2(rc, rc));
                        if(dist > rc) discard;
                    }
                    else if(relUV.y > 1.0 - rc)
                    {
                        float dist = distance(relUV, float2(rc, 1.0 - rc));
                        if(dist > rc) discard;
                    }
                }
                else if(relUV.x > 1.0 - rc)
                {
                    if(relUV.y < rc)
                    {
                        float dist = distance(relUV, float2(1.0 - rc, rc));
                        if(dist > rc) discard;
                    }
                    else if(relUV.y > 1.0 - rc)
                    {
                        float dist = distance(relUV, float2(1.0 - rc, 1.0 - rc));
                        if(dist > rc) discard;
                    }
                }

                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                return color;
            }
            ENDHLSL
        }
    }
}