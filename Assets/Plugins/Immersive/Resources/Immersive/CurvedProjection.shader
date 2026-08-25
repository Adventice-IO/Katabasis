Shader "Immersive/CurvedProjection"
{
    Properties
    {
        _PositiveX ("Positive X", 2D) = "black" {}
        _NegativeX ("Negative X", 2D) = "black" {}
        _PositiveY ("Positive Y", 2D) = "black" {}
        _NegativeY ("Negative Y", 2D) = "black" {}
        _PositiveZ ("Positive Z", 2D) = "black" {}
        _NegativeZ ("Negative Z", 2D) = "black" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_PositiveX);
            SAMPLER(sampler_PositiveX);
            TEXTURE2D(_NegativeX);
            SAMPLER(sampler_NegativeX);
            TEXTURE2D(_PositiveY);
            SAMPLER(sampler_PositiveY);
            TEXTURE2D(_NegativeY);
            SAMPLER(sampler_NegativeY);
            TEXTURE2D(_PositiveZ);
            SAMPLER(sampler_PositiveZ);
            TEXTURE2D(_NegativeZ);
            SAMPLER(sampler_NegativeZ);

            float4x4 _PositiveXVP;
            float4x4 _NegativeXVP;
            float4x4 _PositiveYVP;
            float4x4 _NegativeYVP;
            float4x4 _PositiveZVP;
            float4x4 _NegativeZVP;
            float4x4 _ShapeToWorld;
            float4x4 _WorldToCaptureAxes;

            int _SetupShape;
            int _UseFocusedCapture;
            int _DomeUnwrapMode;
            float4 _OutputUvBounds;
            float _CylinderRadius;
            float _CylinderBaseHeight;
            float _CylinderPanelHeight;
            float _CylinderAngleRadians;
            float _DomeSphereRadius;
            float _DomeCenterHeight;
            float _DomeMaximumPolar;
            float3 _EyeShape;

            static const float Pi = 3.14159265358979323846;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float3 GetCylinderPoint(float2 uv)
            {
                float angle = (uv.x - .5) * _CylinderAngleRadians;
                return float3(
                    _CylinderRadius * sin(angle),
                    _CylinderBaseHeight + uv.y * _CylinderPanelHeight,
                    _CylinderRadius * cos(angle));
            }

            bool TryGetDomePoint(float2 uv, out float3 surfacePosition)
            {
                surfacePosition = 0.0;
                float longitude;
                float polar;

                if (_DomeUnwrapMode == 2)
                {
                    longitude = (uv.x - .5) * 2.0 * Pi;
                    polar = (1.0 - uv.y) * _DomeMaximumPolar;
                }
                else
                {
                    float2 disc = (uv - .5) * 2.0;
                    float radial = length(disc);
                    if (radial > 1.0)
                    {
                        return false;
                    }

                    longitude = atan2(disc.x, disc.y);
                    if (_DomeUnwrapMode == 1)
                    {
                        polar = 2.0 * asin(
                            saturate(radial * sin(_DomeMaximumPolar * .5)));
                    }
                    else
                    {
                        polar = radial * _DomeMaximumPolar;
                    }
                }

                float ringRadius = _DomeSphereRadius * sin(polar);
                float halfPolarSin = sin(polar * .5);
                surfacePosition = float3(
                    ringRadius * sin(longitude),
                    _DomeCenterHeight
                        - 2.0 * _DomeSphereRadius
                        * halfPolarSin * halfPolarSin,
                    ringRadius * cos(longitude));
                return true;
            }

            float2 ProjectUv(float4x4 viewProjection, float4 worldPoint)
            {
                float4 clip = mul(viewProjection, worldPoint);
                float2 captureNdc = clip.xy / clip.w;

                // GL.GetGPUProjectionMatrix(..., true) bakes the render-texture
                // Y flip into clip space on top-origin graphics APIs. Undo that
                // clip-space convention when converting back to sampling UVs.
                #if UNITY_UV_STARTS_AT_TOP
                    captureNdc.y = -captureNdc.y;
                #endif

                return saturate(captureNdc * .5 + .5);
            }

            half4 SampleCapture(float3 directionShape, float4 worldPoint)
            {
                float3 absoluteDirection = abs(directionShape);
                float2 captureUv = 0.0;

                if (absoluteDirection.x >= absoluteDirection.y
                    && absoluteDirection.x >= absoluteDirection.z)
                {
                    if (directionShape.x >= 0.0)
                    {
                        captureUv = ProjectUv(_PositiveXVP, worldPoint);
                        return SAMPLE_TEXTURE2D(
                            _PositiveX,
                            sampler_PositiveX,
                            captureUv);
                    }

                    captureUv = ProjectUv(_NegativeXVP, worldPoint);
                    return SAMPLE_TEXTURE2D(
                        _NegativeX,
                        sampler_NegativeX,
                        captureUv);
                }

                if (absoluteDirection.y >= absoluteDirection.z)
                {
                    if (directionShape.y >= 0.0)
                    {
                        captureUv = ProjectUv(_PositiveYVP, worldPoint);
                        return SAMPLE_TEXTURE2D(
                            _PositiveY,
                            sampler_PositiveY,
                            captureUv);
                    }

                    captureUv = ProjectUv(_NegativeYVP, worldPoint);
                    return SAMPLE_TEXTURE2D(
                        _NegativeY,
                        sampler_NegativeY,
                        captureUv);
                }

                if (directionShape.z >= 0.0)
                {
                    captureUv = ProjectUv(_PositiveZVP, worldPoint);
                    return SAMPLE_TEXTURE2D(
                        _PositiveZ,
                        sampler_PositiveZ,
                        captureUv);
                }

                captureUv = ProjectUv(_NegativeZVP, worldPoint);
                return SAMPLE_TEXTURE2D(
                    _NegativeZ,
                    sampler_NegativeZ,
                    captureUv);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 outputUv = lerp(
                    _OutputUvBounds.xy,
                    _OutputUvBounds.zw,
                    input.uv);
                float3 surfacePoint;
                if (_SetupShape == 1)
                {
                    surfacePoint = GetCylinderPoint(outputUv);
                }
                else if (!TryGetDomePoint(outputUv, surfacePoint))
                {
                    return half4(0.0, 0.0, 0.0, 1.0);
                }

                float3 directionShape = normalize(surfacePoint - _EyeShape);
                float3 distantPointShape = _EyeShape + directionShape * 100.0;
                float4 worldPoint = mul(
                    _ShapeToWorld,
                    float4(distantPointShape, 1.0));
                float3 worldDirection = normalize(
                    mul((float3x3)_ShapeToWorld, directionShape));
                float3 captureDirection = normalize(
                    mul((float3x3)_WorldToCaptureAxes, worldDirection));

                if (_UseFocusedCapture != 0)
                {
                    float2 captureUv = ProjectUv(_PositiveXVP, worldPoint);
                    return SAMPLE_TEXTURE2D(
                        _PositiveX,
                        sampler_PositiveX,
                        captureUv);
                }

                return SampleCapture(captureDirection, worldPoint);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
