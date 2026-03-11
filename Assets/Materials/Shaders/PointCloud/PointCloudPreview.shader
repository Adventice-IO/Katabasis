Shader "Custom/PointCloudDistanceFade"
{
    Properties
    {
        [Header(Appearance)]
        _MinAlpha ("Minimum Visibility", Range(0, 1)) = 0.1 // <--- NEW: Floor for opacity
        
        [Header(Distance Settings)]
        _NearFadeStart ("Near Fade Start (Faded)", Float) = 0.5
        _NearFadeEnd ("Near Fade End (Fully Visible)", Float) = 2.0
        
        _FarFadeStart ("Far Fade Start (Fully Visible)", Float) = 50.0
        _FarFadeEnd ("Far Fade End (Faded)", Float) = 100.0

        _MaskFeather("Mask Feather", Range(0, 1)) = 0.1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
				float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float camDist : TEXCOORD1;
				float4 color : COLOR;
                float3 worldPos : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            // Buffers
             struct OrientedMaskBox {
                float4x4 worldToLocal;
                float3 extents;
                float alpha;
            };
            StructuredBuffer<OrientedMaskBox> _MaskBoxes;
            int _MaskCount;
            float _MaskFeather;
            

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float, _MinAlpha) // <--- Added to Instancing
                UNITY_DEFINE_INSTANCED_PROP(float, _NearFadeStart)
                UNITY_DEFINE_INSTANCED_PROP(float, _NearFadeEnd)
                UNITY_DEFINE_INSTANCED_PROP(float, _FarFadeStart)
                UNITY_DEFINE_INSTANCED_PROP(float, _FarFadeEnd)
            UNITY_INSTANCING_BUFFER_END(Props)



            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                
                // Calculate distance in vertex shader for performance
                o.camDist = distance(o.worldPos, _WorldSpaceCameraPos);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                
                fixed4 col =  i.color;

                float dist = i.camDist;

                float minAlpha = UNITY_ACCESS_INSTANCED_PROP(Props, _MinAlpha);
                float nearStart = UNITY_ACCESS_INSTANCED_PROP(Props, _NearFadeStart);
                float nearEnd = UNITY_ACCESS_INSTANCED_PROP(Props, _NearFadeEnd);
                float farStart = UNITY_ACCESS_INSTANCED_PROP(Props, _FarFadeStart);
                float farEnd = UNITY_ACCESS_INSTANCED_PROP(Props, _FarFadeEnd);

                // Calculate Fade Factors (0 to 1)
                float nearAlpha = smoothstep(nearStart, nearEnd, dist);
                float farAlpha = 1.0 - smoothstep(farStart, farEnd, dist);
                
                // Combine fades to get the "Distance Opacity"
                float distanceOpacity = nearAlpha * farAlpha;

                //Masking
                 float maskAlpha = 1.0;
                 if(_MaskCount == 0) {
                    discard;
                 }

                 int maskHit = -1;
                [loop] // Keep register count low for Unity 6.3 
                for (int j = 0; j < _MaskCount; j++) {
                    // Transform world position to the box's local space [cite: 19]
                    float3 localPos = mul(_MaskBoxes[j].worldToLocal, float4(i.worldPos, 1.0)).xyz;
                    float3 d = abs(localPos);

                     if(j ==2) {
                        col.rgb = float3(localPos.x, localPos.y, localPos.z);
                        break;
                    }
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

                    // If we're inside a mask, mark it
                    if(maskAlpha < 0.999 && maskHit == -1)
                    {
                        maskHit = j;
                    }

                    // Optimization: if we're already invisible, stop [cite: 20]
                    if (maskAlpha <= 0.001) break; 
                }




                // Ensure opacity never drops below MinAlpha
                // We use max() to clamp the lower bound
                col.a *= max(distanceOpacity, minAlpha) *maskAlpha;
                return col;
            }
            ENDCG
        }
    }
}