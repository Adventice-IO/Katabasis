
#define ss(a,b,t) smoothstep(a,b,t)

float gyroid (float3 seed) { return dot(sin(seed),cos(seed.yzx)); }
float fbm (float3 seed) {
    float result = 0.;
    float a = .5;
    for (int i = 0; i < 3; ++i) {
        seed += result*.5;
        result += gyroid(seed/a)*a;
        a /= 2.;
    }
    return result;
}

float saturation(float4 col, float rw, float bw)
{
    return col.g - (col.r*rw+col.b*bw);
}

// ervwin94
// https://www.shadertoy.com/view/MtBGWR
float4 key_green(float4 color, float4 reference_color, float red_weight, float blue_weight)
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

float4 over(float4 bg, float4 fg)
{
    return fg + bg * (1.0-fg.a);
}

void rotation (in out float2 p, float a)
{
  float c=cos(a),s=sin(a);
  p = mul(float2x2(c,-s,s,c), p);
}

// blackle https://suricrasia.online/demoscene/functions/
float3 erot(float3 p, float3 ax, float ro) {
  return lerp(dot(ax, p)*ax, p, cos(ro)) + cross(ax,p)*sin(ro);
}

// blackle https://suricrasia.online/demoscene/functions/
float3 rndrot(float3 p, float4 rnd) {
  return erot(p, normalize(tan(rnd.xyz)), rnd.w*acos(-1));
}

// Inigo Quilez (https://www.shadertoy.com/view/4sfGzS)
float noise(sampler2D map, float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
	f = f*f*(3.0-2.0*f);
	float2 uv = (i.xy+float2(37.0,17.0)*i.z) + f.xy;
	float2 rg = tex2Dlod( map, float4((uv+0.5)/256.0, 0.0, 0.0)).yx;
	return lerp( rg.x, rg.y, f.z );
}

// Dave Hoskins (https://www.shadertoy.com/view/4djSRW)
float hash11(float p)
{
    p = frac(p * .1031);
    p *= p + 19.19;
    p *= p + p;
    return frac(p);
}
float hash12(float2 p)
{
    float3 p3  = frac(float3(p.xyx) * .1031);
    p3 += dot(p3, p3.yzx + 19.19);
    return frac((p3.x + p3.y) * p3.z);
}
float hash13(float3 p3)
{
    p3  = frac(p3 * .1031);
    p3 += dot(p3, p3.yzx + 19.19);
    return frac((p3.x + p3.y) * p3.z);
}
float2 hash21(float p)
{
    float3 p3 = frac(float3(p,p,p) * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx + 19.19);
    return frac((p3.xx+p3.yz)*p3.zy);
}
float2 hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx+19.19);
    return frac((p3.xx+p3.yz)*p3.zy);
}
float2 hash23(float3 p3)
{
    p3 = frac(p3 * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx+19.19);
    return frac((p3.xx+p3.yz)*p3.zy);
}
float3 hash31(float p)
{
    float3 p3 = frac(float3(p,p,p) * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yzx+19.19);
    return frac((p3.xxy+p3.yzz)*p3.zyx); 
}
float3 hash32(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yxz+19.19);
    return frac((p3.xxy+p3.yzz)*p3.zyx);
}
float3 hash33(float3 p3)
{
    p3 = frac(p3 * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yxz+19.19);
    return frac((p3.xxy + p3.yxx)*p3.zyx);
}
float4 hash41(float p)
{
	float4 p4 = frac(p * float4(.1031, .1030, .0973, .1099));
    p4 += dot(p4, p4.wzxy+33.33);
    return frac((p4.xxyz+p4.yzzw)*p4.zywx);
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

// Sam Hocevar
float3 hsv2rgb(float3 c)
{
  float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
  float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
  return c.z * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}


// Keijiro Takahashi
// Kinect camera metadata
static float2 _ImageDimensions = float2(512, 424);
static float2 _FocalLength = float2(366.557, 366.557);
static float2 _PrincipalPoint = float2(254.503, 205.052);
static float _NearClip = 0;
static float _FarClip = 6.5535;

// Keijiro Takahashi
// Object space position from depth sample
float3 DepthToPosition(float2 coord, float depth)
{
    coord = coord * _ImageDimensions - _PrincipalPoint;
    float z = lerp(_NearClip, _FarClip, depth);
    return float3(coord * z / _FocalLength, z);
}