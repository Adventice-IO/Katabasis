Shader "Custom/MagnetShader"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MagnetPos] _MagnetPosition("Magnet Position", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO // Required for VR Single Pass

            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _MagnetPosition;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                
                UNITY_SETUP_INSTANCE_ID(IN); 
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.positionOS.xy;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Base magnet-driven color logic
                half4 baseCol;
                if(IN.uv.y < _MagnetPosition)
                {
                    baseCol = _BaseColor;

                }
                

                 else if(IN.uv.x > .5)
                {
                    baseCol = float4(0,0,1,1);
                }
                
                else
                {
                    baseCol = float4(1,0,0,1);
                }

                // Simple Lambert lighting with main light
                Light mainLight = GetMainLight();
                float3 normalWS = normalize(IN.normalWS);
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float3 litColor = baseCol.rgb * (mainLight.color * NdotL + 0.2); // small ambient term

                return half4(litColor, baseCol.a);

               
            }
            ENDHLSL
        }
    }
}
