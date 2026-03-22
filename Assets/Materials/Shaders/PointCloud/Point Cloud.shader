Shader "Point Cloud/Optimized_Masked_VR"
{
    Properties
    {
        _Alpha("Alpha", Range(0,1)) = 1
        _MaxDistance ("Max Distance", float) = 50
        _DistFade("Distance Fade", float) = 10
        _Reveal("Reveal", Range(0,1)) = 0
        _BoxMin("Box Min", Vector) = (0,0,0,0)
        _BoxMax("Box Max", Vector) = (1,1,1,0)
        _BoxFeather("Box Feather", float) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            // Explicitly force the compiler to validate structured buffers for the target API
            #pragma require structuredbuffer 

            #include "UnityCG.cginc"

            struct attribute
            {
                float4 position : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct varying
            {
                float4 position : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Hardware-aligned struct (96 bytes total stride)
            struct OrientedMaskBox {
                float4x4 worldToLocal;
                float3 extents;
                float alpha;
                float4 settings; // x: feather, y: soloWhenInside, zw: padding
            };

            // Uniforms
            float _Alpha;
            float _MaxDistance;
            float _DistFade;
            float _Reveal;
            float3 _BoxMin;
            float3 _BoxMax;
            float _BoxFeather;

            // Buffers
            StructuredBuffer<OrientedMaskBox> _MaskBoxes;
            int _MaskCount;

            varying vert(attribute v)
            {
                varying o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // 1. Quick Global Exit
                float globalAlpha = _Alpha * _Reveal;
                if (globalAlpha <= 0.0) {
                    o.position = float4(0,0,0,0);
                    o.color = float4(0,0,0,0);
                    return o;
                }

                // 2. Space Transformations
                float3 worldPos = mul(unity_ObjectToWorld, v.position).xyz;
                float d = distance(_WorldSpaceCameraPos, worldPos);
                
                // 3. Distance Cull/Fade
                if (d > _MaxDistance) {
                    o.position = float4(0,0,0,0);
                    o.color = float4(0,0,0,0);
                    return o;
                }
                float distFade = saturate((_MaxDistance - d) / _DistFade);

                // 4. Masking Logic
                float maskAlpha = 1.0;
                
                [loop] // Keep register count low for mobile [cite: 18]
                for (int j = 0; j < _MaskCount; j++) {
                    OrientedMaskBox mask = _MaskBoxes[j]; // Cache locally to reduce buffer lookups
                    
                    float3 localPos = mul(mask.worldToLocal, float4(worldPos, 1.0)).xyz;
                    float3 localD = abs(localPos);
    
                    float3 distToEdge = mask.extents - localD;
                    float boxSDF = min(distToEdge.x, min(distToEdge.y, distToEdge.z));

                    // Extract packed settings
                    float feather = max(0.001, mask.settings.x);
                    float soloSignal = mask.settings.y;

                    float featherFactor = saturate(boxSDF / feather);
                    float boxEffect = lerp(1.0, mask.alpha, featherFactor);
    
                    maskAlpha *= boxEffect;

                    if (soloSignal > 0.5) {
                        float3 camLocalPos = mul(mask.worldToLocal, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                        float3 camD = abs(camLocalPos);
                        float3 camDistToEdge = mask.extents - camD;
                        float camBoxSDF = min(camDistToEdge.x, min(camDistToEdge.y, camDistToEdge.z));
                        
                        bool isInside = boxSDF > 0;
                        if (!isInside) {
                           float camFeatherFactor = 1.0 - saturate(camBoxSDF / feather);
                           maskAlpha *= camFeatherFactor;
                        }   
                    }

                    // Optimization: if we're already invisible, stop [cite: 30]
                    if (maskAlpha <= 0.001) break;
                }

                // Box feather around bounds
                float3 boxCenter = (_BoxMin + _BoxMax) * 0.5;
                float3 boxSize = abs(_BoxMax - _BoxMin);
                float3 localPoint = worldPos - boxCenter;
                float3 halfSize = boxSize * 0.5;
                
                float3 boundsDistToEdge = halfSize - abs(localPoint);
                float minDist = min(boundsDistToEdge.x, boundsDistToEdge.z);
                
                // Fixed implicit cast warning by using length() for scalar division
                float boxFeatherAlpha = saturate(minDist / max(0.001, _BoxFeather * length(boxSize)));

                // 5. Final Visibility Check
                float visibility = globalAlpha * distFade * maskAlpha * boxFeatherAlpha;

                // Move heavy clip space math to AFTER visibility check
                // This saves massive overhead on culled points
                if (visibility <= 0.001) {
                    o.position = float4(0,0,0,0);
                    o.color = float4(0,0,0,0);
                    return o;
                }

                o.position = UnityObjectToClipPos(v.position);
                o.uv = v.uv;

                // Color pass: compensate dark points
                float brightness = dot(v.color.rgb, float3(0.299, 0.587, 0.114));
                float darknessThreshold = 0.1;
                float brightnessFactor = smoothstep(0.0, darknessThreshold, brightness);

                float3 brightened = lerp(v.color.rgb * 10, v.color.rgb, brightnessFactor);
                float3 gray = float3(brightness, brightness, brightness);
                brightened = lerp(gray, brightened, brightnessFactor);

                // Fixed: Apply the brightened color that was calculated but never used
                o.color = float4(brightened, v.color.a) * visibility;
                
                return o;
            }

            fixed4 frag(varying i) : SV_Target
            {
                // Simple circular point shape
                float d = dot(i.uv, i.uv);
                if (d > 0.25) discard; // Hard circle crop [cite: 43]
                
                return i.color;
            }
            ENDCG
        }
    }
}