Shader "Point Cloud"
{
	Properties
	{
		[Toggle] _ModePoint ("Mode Point", float) = 1
		[Toggle] _Preview ("Preview", float) = 0
		_ColorKey ("Color Key", Color) = (1,1,1,1)
		_TimeIn ("Time In", Range(0,1)) = 0
		_FadeDistanceRange ("Fade Range", float) = 20
		_FadeDistanceFeather ("Fade Feather", float) = 5
		_DensityCropRange ("Density Crop Range", float) = 2
		_DensityCropFeather ("Density Crop Feather", float) = 5
		_DensityCropMax ("Density Crop Max", Range(0,1)) = 0.9
		_Explode("Explode Particles", Range(0,100)) = 0
		// _hideCave("hide Particles under some y level", Range(0,100)) = 0
		// _hideCaveHeight("hide Particles under some y level", Range(-100,100)) = 0
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

			float4 _ColorKey;
			float3 _Effector;
			float _TimeIn, _Alpha, _ModePoint, _Preview;
			float _FadeDistanceFeather, _FadeDistanceRange, _DensityCropFeather, _DensityCropRange, _DensityCropMax, _Explode;//,_hideCave,_hideCaveHeight;

			varying vert(attribute v)
			{
				varying o;

				UNITY_INITIALIZE_OUTPUT(varying, o);
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				// world space coordinates
				float3 world = mul(UNITY_MATRIX_M, v.position).xyz;
				float d = length(_WorldSpaceCameraPos - world);
				float effector = smoothstep(.2,.0,length(_Effector - world)-.1);

				// world += (hash33(floor(world*10.))-.5)*step(.9, hash13(floor(world)+floor(_Time.y)))*.1*hash11(floor(_Time.y*100.));//*sin(_Time.y);

				// float grid = 10.;
				// float cell = step(.95, hash13(floor(world*grid)));
				// float3 stack = floor(world*grid)/grid + 0.5/grid;
				// stack.y += fmod(v.id+_Time.y, 100.) * .01;
				// world = lerp(world, stack, cell);

				float id;
				if (_ModePoint)
				{
					id = v.id;
				}
				else
				{
					id = v.quantity.y;

					// float3 z = normalize(hash31(id)-.5);
					float3 z = (hash31(id)-.5);

					z = normalize(lerp(z, hash32(float2(id, 1234))-.5, effector));

					float3 x = normalize(cross(z, float3(0,1,0)));
					float3 y = normalize(cross(x, z));
	
					float size = lerp(0.001, 0.005, smoothstep(.0, 10., d));
					size *= smoothstep(.0, 0.3, d-.1);
					world += (x * v.uv.x + y * v.uv.y) * size * 10;
					// world += (hash13(id)-.5)*.1*smoothstep(0,5,d-5);
				}


				v.position.xyz = mul(unity_WorldToObject, float4(world, 1));

				// float t = frac(_Time.y*.1+hash13(floor(mul(UNITY_MATRIX_M, float4(0,0,0,1)))));
				// v.position.xyz *= 1.+.5*t;
				// v.color.a *= sin(t*3.14);

				// v.color.rgb = max(v.color.rgb, .05);

				v.color *= _Alpha;
				v.color *= _TimeIn;
				// v.color.a *= step(.1, hash13(floor(world)+floor(_Time.y)));
				
				o.color = v.color;
				float3 hsv = rgb2hsv(o.color.rgb);
				hsv.y *= 1.5;
				o.color.rgb = hsv2rgb(hsv);
				// o.color.rgb = pow(o.color.rgb, 1/2.22);
				float gray = Luminance(o.color);
				// o.color.rgb = lerp(o.color.rgb, 1, step(.9, hash11(id)));
				// o.color.rgb = lerp(o.color.rgb, gray, 0.5);
				// o.color = 1;
				// fix purple tint
				float purple = smoothstep(.0,.1,key_green(_ColorKey, v.color, 0.5, 0.1)-.9);
				o.color = lerp(o.color, Luminance(o.color*2.), purple);

				// fade distance
				o.color *= smoothstep(_FadeDistanceFeather,0,d-_FadeDistanceRange);

				// density crop
				o.color *= step(smoothstep(0,_DensityCropFeather,d-_DensityCropRange)*_DensityCropMax, hash11(id));
				if (_Preview) o.color = 1;
				// o.color = 1;
				// float3 p = v.position.xyz;
				// p = rndrot(p, hash41(floor(_Time.y))*2-1);
				// o.color.a *= step(0.5, frac(p.y * 10. + floor(_Time.y*10.)*.1));

				// o.color = v.color;
				if(_Explode>0)
				{ v.position.xyz+= (hash33(v.position.xyz)*2-1)*_Explode; }
				// if(_hideCave>0 && world.y<_hideCaveHeight)
				// { v.position.xyz+= hash33(v.position.xyz)*_hideCave; }
				o.position = UnityObjectToClipPos(v.position);
				o.uv = v.uv;
				return o;
			}

			float4 frag(varying o) : COLOR
			{
				float d = length(o.uv);
				d = smoothstep(.0,-.5,d-.5);
				return o.color * d;// * clamp(1-d,0,1) / max(0.01, d);
			}

			ENDCG
		}
	}
}
