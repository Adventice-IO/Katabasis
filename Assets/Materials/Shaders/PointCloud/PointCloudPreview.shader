Shader "Custom/PointCloudDistanceFade"
{
    Properties
    {
        [Header(Appearance)]
        _MinAlpha ("Minimum Visibility", Range(0, 1)) = 0.1 // <--- NEW: Floor for opacity
        
        [Header(Correction)]
        _ColorKey ("Purple Key Color", Color) = (0, 1, 0, 1)

        [Header(Distance Settings)]
        _NearFadeStart ("Near Fade Start (Faded)", Float) = 0.5
        _NearFadeEnd ("Near Fade End (Fully Visible)", Float) = 2.0
        
        _FarFadeStart ("Far Fade Start (Fully Visible)", Float) = 50.0
        _FarFadeEnd ("Far Fade End (Faded)", Float) = 100.0
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _ColorKey;
            

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float, _MinAlpha) // <--- Added to Instancing
                UNITY_DEFINE_INSTANCED_PROP(float, _NearFadeStart)
                UNITY_DEFINE_INSTANCED_PROP(float, _NearFadeEnd)
                UNITY_DEFINE_INSTANCED_PROP(float, _FarFadeStart)
                UNITY_DEFINE_INSTANCED_PROP(float, _FarFadeEnd)
            UNITY_INSTANCING_BUFFER_END(Props)

            
            float saturation(float4 col, float rw, float bw)
            {
                return col.g - (col.r*rw+col.b*bw);
            }

            // ervwin94
            // https://www.shadertoy.com/view/MtBGWR
            float4 color_key(float4 color, float4 reference_color, float red_weight, float blue_weight)
            {
                float col_sat = saturation(color, red_weight, blue_weight);
                float ref_sat = saturation(reference_color, red_weight, blue_weight);
                float key = (1.0-clamp(col_sat / ref_sat, 0.0, 1.0))*color.a;
                // subtract green
                float4 result = clamp(color-reference_color*(1.0-key), 0.0, 1.0);
                result.a = key;
                // despill
                result.g = min(result.g, 0.5*result.r+0.5*result.b);
                return result;
            }

            // Sam Hocevar
            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                
                // Calculate distance in vertex shader for performance
                o.camDist = distance(worldPos, _WorldSpaceCameraPos);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color*v.color;

				// fix purple tint
				//float purple = smoothstep(.0,.1,color_key(_ColorKey, v.color, 0.5, 0.1)-.9);
				//o.color = lerp(o.color, Luminance(o.color*2.), purple);
                //o.color = color_key(_ColorKey, v.color, 0.5, 0.1).r;
                //o.color = 0.1/max(0.001,length((_ColorKey - v.color)));
                //o.color = 0.01/max(0.0001,length(rgb2hsv(_ColorKey) - rgb2hsv(v.color)));
                float purple = saturate(0.01/max(0.0001,length(rgb2hsv(_ColorKey) - rgb2hsv(v.color))));
                //if (o.vertex.x > 0.)
                //o.color = lerp(o.color, 1.0, purple);
                //.color = _ColorKey;
                
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

                // Ensure opacity never drops below MinAlpha
                // We use max() to clamp the lower bound
                col.a *= max(distanceOpacity, minAlpha);


                //Color pass : compensate dark points so they remain visible
                // Brighten dark colors smoothly
                float brightness = dot(col.rgb, float3(0.299, 0.587, 0.114));
                float darknessThreshold = 0.1;
                float brightnessFactor = smoothstep(0.0, darknessThreshold, brightness);
                
                // Brighten dark colors
                col.rgb = lerp(col.rgb * 10, col.rgb, brightnessFactor);
                
                // Desaturate as they get darker
                float3 gray = float3(brightness, brightness, brightness);
                col.rgb = lerp(gray, col.rgb, brightnessFactor);

                return col;
            }
            ENDCG
        }
    }
}