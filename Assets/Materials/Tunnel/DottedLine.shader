Shader "Katabasis/DottedLine"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [Length] _Length("Length", float) = 10
        [Width] _Width("Width", float) = 1
        [Gap] _Gap("Gap", Range(.1,2)) = 1
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


            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Gap;
                float _Round;
                float _Width;
                float _Length;
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
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float ratio = _Length / _Width;
                float relX = IN.uv.x * ratio / 2;
                float modPos = fmod(relX, _Gap) * 2.0 / _Gap;
                
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

                half4 color = _BaseColor;
                return color;
            }
            ENDHLSL
        }
    }
}