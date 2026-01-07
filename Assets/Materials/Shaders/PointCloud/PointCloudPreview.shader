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

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                
                // Calculate distance in vertex shader for performance
                o.camDist = distance(worldPos, _WorldSpaceCameraPos);

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

                // Ensure opacity never drops below MinAlpha
                // We use max() to clamp the lower bound
                col.a *= max(distanceOpacity, minAlpha);

                return col;
            }
            ENDCG
        }
    }
}