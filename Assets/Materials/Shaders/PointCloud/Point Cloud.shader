Shader "Point Cloud/Optimized_Masked"
{
    Properties
    {
        _Alpha("Alpha", Range(0,1)) = 1
        _MaxDistance ("Max Distance", float) = 50
        _DistFade("Distance Fade", float) = 10
        _Reveal("Reveal", Range(0,1)) = 0
        _MaskFeather("Mask Feather", Range(0, 1)) = 0.1
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
            // Use shader model 4.5+ for StructuredBuffer support
            #pragma target 4.5 

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

            struct OrientedMaskBox {
                float4x4 worldToLocal;
                float3 extents;
                float alpha;
            };

            // Uniforms
            float _Alpha;
            float _MaxDistance;
            float _DistFade;
            float _Reveal;
            float _MaskFeather;
            
            // Buffers
            StructuredBuffer<OrientedMaskBox> _MaskBoxes;
            int _MaskCount;

            varying vert(attribute v)
            {
                varying o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.position = UnityObjectToClipPos(v.position);
                o.uv = v.uv;


                // 1. Quick Global Exit
                float globalAlpha = _Alpha * _Reveal;
                if (globalAlpha <= 0.0) {
                    o.position = float4(0,0,0,0);
                    return o;
                }

                // 2. Space Transformations
                float3 worldPos = mul(unity_ObjectToWorld, v.position).xyz;
                float d = distance(_WorldSpaceCameraPos, worldPos);
                
                // 3. Distance Cull/Fade
                // If point is further than MaxDistance, we collapse it immediately
                if (d > _MaxDistance) {
                    o.position = float4(0,0,0,0);
                    return o;
                }
                float distFade = saturate((_MaxDistance - d) / _DistFade);

                // 4. Masking Logic
                float maskAlpha = 1.0;
                [loop] // Keep register count low for Unity 6.3 
                for (int j = 0; j < _MaskCount; j++) {
                    // Transform world position to the box's local space [cite: 19]
                    float3 localPos = mul(_MaskBoxes[j].worldToLocal, float4(worldPos, 1.0)).xyz;
                    float3 d = abs(localPos);
    
                    // Calculate distance to the box edge (extents) [cite: 8, 19]
                    // Positive result = inside the box
                    float3 distToEdge = _MaskBoxes[j].extents - d;
                    float boxSDF = min(distToEdge.x, min(distToEdge.y, distToEdge.z));

                    // If boxSDF > 0, we are inside. 
                    // We calculate a 0-1 gradient based on the feather distance.
                    float featherFactor = saturate(boxSDF / max(0.001, _MaskFeather));
    
                    // If the box is intended to hide points (alpha < 1):
                    // We blend the point's current visibility with the box's alpha.
                    // If featherFactor is 1 (deep inside), we use the box's alpha.
                    // If featherFactor is 0 (outside), we keep current visibility.
                    float boxEffect = lerp(1.0, _MaskBoxes[j].alpha, featherFactor);
    
                    maskAlpha *= boxEffect;

                    // Optimization: if we're already invisible, stop [cite: 20]
                    if (maskAlpha <= 0.001) break; 
                }

                // 5. Final Visibility Check
                float visibility = globalAlpha * distFade * maskAlpha;
                if (visibility <= 0.001) {
                    o.position = float4(0,0,0,0);
                    return o;
                }

                o.color = v.color * visibility;
                
                return o;
            }

            fixed4 frag(varying i) : SV_Target
            {
                // Simple circular point shape
                float d = dot(i.uv, i.uv);
                if (d > 0.25) discard; // Hard circle crop (0.5 radius squared)
                
                return i.color;
            }
            ENDCG
        }
    }
}