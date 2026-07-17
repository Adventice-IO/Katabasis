Shader "Point Cloud/Optimized_Masked_VR_Size"
{
    Properties
    {
        _PointSize("Point Size (pixels)", Range(0.1, 64)) = 2
        _Alpha("Alpha", Range(0,1)) = 1
        _MaxDistance("Max Distance", float) = 50
        _DistFade("Distance Fade", float) = 10
        _Reveal("Reveal", Range(0,1)) = 0
        _BoxMin("Box Min", Vector) = (0,0,0,0)
        _BoxMax("Box Max", Vector) = (1,1,1,0)
        _BoxFeather("Box Feather", float) = 1
        _ColorKey("Purple Key Color", Color) = (0, 1, 0, 1)

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

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #pragma target 4.5
            #pragma require geometry
            #pragma require structuredbuffer

            #include "UnityCG.cginc"
            #include "Common.cginc"
            #include "Color.cginc"

            struct attribute
            {
                float4 position : POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct vertex_to_geometry
            {
                float4 position : SV_POSITION;
                float4 color : COLOR;
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

            struct OrientedMaskBox
            {
                float4x4 worldToLocal;
                float3 extents;
                float alpha;
                float4 settings;
            };

            float _PointSize;
            float _Alpha;
            float _MaxDistance;
            float _DistFade;
            float _Reveal;
            float3 _BoxMin;
            float3 _BoxMax;
            float _BoxFeather;
            float _KatabasisSpectatorPass;
            float _KatabasisSpectatorPointMode;
            float _KatabasisSpectatorPointSize;
            float _KatabasisSpectatorPointAlpha;

            int _EnableFocalMode;
            float _Focal;
            float _Focalwidth;
            int _EnableBlackAndWhite;
            float _BlackAndWhiteThreshold;

            StructuredBuffer<OrientedMaskBox> _MaskBoxes;
            int _MaskCount;
            float4 _ColorKey;

            vertex_to_geometry vert(attribute v)
            {
                vertex_to_geometry o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(vertex_to_geometry, o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float effectiveAlpha = lerp(
                    _Alpha,
                    _KatabasisSpectatorPointAlpha,
                    saturate(_KatabasisSpectatorPass));
                float globalAlpha = effectiveAlpha * _Reveal;
                if (globalAlpha <= 0.0)
                {
                    return o;
                }

                float3 worldPos = mul(unity_ObjectToWorld, v.position).xyz;
                float d = distance(_WorldSpaceCameraPos, worldPos);

                float focalFade = 1.0;
                if (_EnableFocalMode > 0)
                {
                    float focalWidth = max(_Focalwidth, 0.0001);
                    float distToFocal = d - _Focal;
                    focalFade = saturate((focalWidth - abs(distToFocal)) / focalWidth);
                    focalFade = smoothstep(0.2, 0.8, focalFade);
                }

                if (d > _MaxDistance)
                {
                    return o;
                }

                float closeFade = 1.0;
                closeFade *= smoothstep(0.0, 0.5, d - 1.25);
                closeFade *= lerp(0.1, 1.0, smoothstep(10.0, 0.0, d - 10.0));

                float relDist = saturate((_MaxDistance - d) / max(_DistFade, 0.0001));
                float distFade = smoothstep(0, 1, relDist);

                float maskAlpha = 1.0;
                [loop]
                for (int j = 0; j < _MaskCount; j++)
                {
                    OrientedMaskBox mask = _MaskBoxes[j];
                    float3 localPos = mul(mask.worldToLocal, float4(worldPos, 1.0)).xyz;
                    float3 localD = abs(localPos);
                    float3 distToEdge = mask.extents - localD;
                    float boxSDF = min(distToEdge.x, min(distToEdge.y, distToEdge.z));

                    float feather = max(0.001, mask.settings.x);
                    float soloSignal = mask.settings.y;
                    float featherFactor = saturate(boxSDF / feather);
                    maskAlpha *= lerp(1.0, mask.alpha, featherFactor);

                    if (soloSignal > 0.5)
                    {
                        float3 camLocalPos = mul(mask.worldToLocal, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                        float3 camD = abs(camLocalPos);
                        float3 camDistToEdge = mask.extents - camD;
                        float camBoxSDF = min(camDistToEdge.x, min(camDistToEdge.y, camDistToEdge.z));
                        if (boxSDF <= 0)
                        {
                            maskAlpha *= 1.0 - saturate(camBoxSDF / feather);
                        }
                    }

                    if (maskAlpha <= 0.001)
                    {
                        break;
                    }
                }

                float3 boxCenter = (_BoxMin + _BoxMax) * 0.5;
                float3 boxSize = abs(_BoxMax - _BoxMin);
                float3 localPoint = worldPos - boxCenter;
                float3 halfSize = boxSize * 0.5;
                float3 boundsDistToEdge = halfSize - abs(localPoint);
                float minDist = min(boundsDistToEdge.x, boundsDistToEdge.z);
                float boxFeatherAlpha = saturate(minDist / max(0.001, _BoxFeather * length(boxSize)));

                float visibility = globalAlpha * distFade * closeFade * maskAlpha * boxFeatherAlpha * focalFade;
                if (visibility <= 0.001)
                {
                    return o;
                }

                o.position = UnityObjectToClipPos(v.position);

                float4 color = get_color(v.color, _ColorKey);
                float brightness = dot(color.rgb, float3(0.299, 0.587, 0.114));
                float brightnessFactor = smoothstep(0.0, 0.1, brightness);
                float3 brightened = lerp(color.rgb * 10, color.rgb, brightnessFactor);
                float3 gray = float3(brightness, brightness, brightness);
                brightened = lerp(gray, brightened, lerp(0.1, 1.0, brightnessFactor));
                o.color = float4(brightened, color.a) * visibility;

                if (_EnableBlackAndWhite > 0)
                {
                    float finalBrightness = dot(o.color.rgb, float3(0.299, 0.587, 0.114));
                    o.color = finalBrightness < _BlackAndWhiteThreshold
                        ? float4(0, 0, 0, 1)
                        : float4(1, 1, 1, 1);
                }

                return o;
            }

            varying CreateVertex(vertex_to_geometry input, float2 uv)
            {
                varying o;
                UNITY_INITIALIZE_OUTPUT(varying, o);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float spectatorPointSize = lerp(
                    1.0,
                    _KatabasisSpectatorPointSize,
                    saturate(_KatabasisSpectatorPointMode));
                float effectivePointSize = lerp(
                    _PointSize,
                    spectatorPointSize,
                    saturate(_KatabasisSpectatorPass));
                float2 screenSize = max(_ScreenParams.xy, float2(1.0, 1.0));
                float2 clipOffset = uv * (effectivePointSize / screenSize);
                o.position = input.position;
                o.position.xy += clipOffset * o.position.w;
                o.color = input.color;
                o.uv = uv;
                return o;
            }

            [maxvertexcount(4)]
            void geom(point vertex_to_geometry input[1], inout TriangleStream<varying> outputStream)
            {
                UNITY_SETUP_INSTANCE_ID(input[0]);
                outputStream.Append(CreateVertex(input[0], float2(-1.0,  1.0)));
                outputStream.Append(CreateVertex(input[0], float2( 1.0,  1.0)));
                outputStream.Append(CreateVertex(input[0], float2(-1.0, -1.0)));
                outputStream.Append(CreateVertex(input[0], float2( 1.0, -1.0)));
                outputStream.RestartStrip();
            }

            fixed4 frag(varying i) : SV_Target
            {
                if (dot(i.uv, i.uv) > 1.0)
                {
                    discard;
                }
                return i.color;
            }
            ENDCG
        }
    }
}
