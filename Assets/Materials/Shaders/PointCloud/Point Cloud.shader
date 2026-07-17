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
        _ColorKey ("Purple Key Color", Color) = (0, 1, 0, 1)

         _Focal("Focal", float) = 30
         _Focalwidth("Focal Width", float) = 20
         _Threshold("Threshold", float) = 0.5
         _EnableFocalMode("Enable Focal Mode", float) = 1
        _EnableBlackAndWhite("Enable Black and White", float) = 0
        _BlackAndWhiteThreshold("Black and White Threshold", float) = 0.5
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Blend One One
        ZWrite Off
        Cull Off
        // Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            // Explicitly force the compiler to validate structured buffers for the target API
            #pragma require structuredbuffer 

            #include "UnityCG.cginc"
			#include "Common.cginc"
			#include "Color.cginc"

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
            float _KatabasisSpectatorPass;
            float _KatabasisSpectatorPointAlpha;

            // KataDraw parameters
            int _EnableFocalMode;
            float _Focal;
            float _Focalwidth;
            float _Threshold;
            
            int _EnableBlackAndWhite;
            float _BlackAndWhiteThreshold;

            // Buffers
            StructuredBuffer<OrientedMaskBox> _MaskBoxes;
            int _MaskCount;
            float4 _ColorKey;

        

            varying vert(attribute v)
            {
                varying o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                
 
                // 1. Quick Global Exit
                float effectiveAlpha = lerp(
                    _Alpha,
                    _KatabasisSpectatorPointAlpha,
                    saturate(_KatabasisSpectatorPass));
                float globalAlpha = effectiveAlpha * _Reveal;
                if (globalAlpha <= 0.0) {
                    o.position = float4(0,0,0,0);
                    o.color = float4(0,0,0,0);
                    return o;
                }

                // 2. Space Transformations
                float3 worldPos = mul(unity_ObjectToWorld, v.position).xyz;
                float d = distance(_WorldSpaceCameraPos, worldPos);
                // float cylinder = distance(_WorldSpaceCameraPos.xz, worldPos.xz);
                // cylinder = min(cylinder - 1.0, -(abs(_WorldSpaceCameraPos.y-worldPos.y) - 0.5));
                
                float focalFade = 1.0;
                //cutting out dots that are outside the "focal"
                if(_EnableFocalMode > 0){
					// Calculate the distance from the camera to the world position
					float distToFocal = d - _Focal;

					// Calculate the focal fading using the saturate function
					focalFade = saturate((_Focalwidth - abs(distToFocal)) / _Focalwidth);
                    focalFade = smoothstep(0.2, 0.8, focalFade);

				}


                // 3. Distance Cull/Fade
                if (d > _MaxDistance) {
                    o.position = float4(0,0,0,0);
                    o.color = float4(0,0,0,0);
                    return o;
                }

                float closeFade = 1.0;
                closeFade *= smoothstep(0.0, 0.5, d-1.25);
                closeFade *= lerp(0.1, 1.0, smoothstep(10.0, 0.0, d-10.0));


                float relDist = saturate((_MaxDistance - d) / max(_DistFade, 0.0001));
                float distFade = smoothstep(0,1, relDist);

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
                float visibility = globalAlpha * distFade * closeFade * maskAlpha * boxFeatherAlpha * focalFade;

                // Move heavy clip space math to AFTER visibility check
                // This saves massive overhead on culled points
                if (visibility <= 0.001) {
                    o.position = float4(0,0,0,0);
                    o.color = float4(0,0,0,0);
                    return o;
                }

                //v.position.xyz += (hash33(v.position.xyz)*2-1) * smoothstep(0.0, 2.0, d-10.0) * 0.05;

                o.position = UnityObjectToClipPos(v.position);
                o.uv = v.uv;

                float4 color = get_color(v.color, _ColorKey);
    
                //color.rgb *= lerp(1.0, 10.0, smoothstep(3.0, 0.0, d-1.0));

                // Color pass: compensate dark points
                float brightness = dot(color.rgb, float3(0.299, 0.587, 0.114));
                float darknessThreshold = 0.1;
                float brightnessFactor = smoothstep(0.0, darknessThreshold, brightness);

                float3 brightened = lerp(color.rgb * 10, color.rgb, brightnessFactor);
                float3 gray = float3(brightness, brightness, brightness);
                float saturationFactor = lerp(0.1, 1.0, brightnessFactor);
                brightened = lerp(gray, brightened, saturationFactor);

                // Fixed: Apply the brightened color that was calculated but never used
                o.color = float4(brightened, color.a) * visibility;
                // o.color = v.color;
                // o.color = 1 * distFade;;
                
                if(_EnableBlackAndWhite > 0){
                    float brightness = dot(o.color.rgb, float3(0.299, 0.587, 0.114));
                    // brightness *= o.color.a;
                    if (brightness < _BlackAndWhiteThreshold) {
                        o.color = float4(0, 0, 0, 1.0); // Black
                    } else {
                        o.color = float4(1, 1, 1, 1.0); // White
                    }
                }

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
