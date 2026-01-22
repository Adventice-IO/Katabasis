Shader "Point Cloud"
{
	Properties
	{
		_Alpha("Alpha", Range(0,1)) = 1
		_MaxDistance ("Max Distance", float) = 50
		_DistFade("Distance Fade", float) = 10
		_Reveal("Reveal", Range(0,1)) = 0
	}

	SubShader
	{
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
		// Blend SrcAlpha OneMinusSrcAlpha
		Blend One One
		ZWrite Off
		Cull Off

		Pass
		{
			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag

			#include "UnityCG.cginc"
			#include "Common.cginc"

			struct attribute
			{
				float4 position : POSITION;
				float4 color : COLOR;
				float2 uv : TEXCOORD0;
				float2 quantity : TEXCOORD1;
				uint id : SV_VertexID;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct varying
			{
				float4 position : SV_POSITION;
				float4 color : COLOR;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			float _Alpha;
			float _MaxDistance;
			float _DistFade;
			float _Reveal;

			varying vert(attribute v)
			{
				varying o;

				UNITY_INITIALIZE_OUTPUT(varying, o);
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				// world space coordinates
				float3 world = mul(UNITY_MATRIX_M, v.position).xyz;
				float d = length(_WorldSpaceCameraPos - world);
				

				//fade with max dist
				float distFade = saturate( (_MaxDistance - d) / _DistFade );
				// v.color.a *= distFade * _Reveal;
				o.uv = v.uv;
				o.position = UnityObjectToClipPos(v.position);
				o.color = v.color;
				o.color *=  _Alpha * distFade * _Reveal;
				return o;
			}

			float4 frag(varying o) : COLOR
			{
				float d = length(o.uv);
				d = smoothstep(.0,-.5,d-.5);
				return o.color;//* clamp(1-d,0,1) / max(0.01, d);
			}

			ENDCG
		}
	}
}
