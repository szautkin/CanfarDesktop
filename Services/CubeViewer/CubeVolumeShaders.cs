namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// HLSL Shader Model 5.0 source for the cube volume ray-marcher, compiled at
/// runtime via D3DCompiler. This is a faithful port of <c>Cube.metal</c> from
/// the macOS app: a fullscreen-triangle pass that reconstructs the world ray
/// from the inverse view-projection, marches a half-float Texture3D front-to-back
/// with jittered starts and early ray termination, applies the shared
/// stretch + opacity transfer function + colormap, and supports a MIP mode.
/// </summary>
internal static class CubeVolumeShaders
{
    /// <summary>
    /// Vertex shader: emits a single fullscreen triangle from SV_VertexID and
    /// passes through clip-space NDC. No vertex buffer required.
    /// </summary>
    public const string VertexEntry = "VSMain";

    /// <summary>Fragment/pixel shader entry point: the volume ray-march.</summary>
    public const string PixelEntry = "PSMain";

    /// <summary>
    /// Combined HLSL source. The constant buffer layout matches
    /// <c>CubeUniforms</c> on the C# side byte-for-byte (matrices uploaded
    /// pre-transposed so <c>mul(vector, matrix)</c> reproduces the Metal
    /// column-vector math).
    /// </summary>
    public const string Source = """
// Read constant-buffer matrices in row-major order so they match the in-memory
// layout of System.Numerics.Matrix4x4 (M{row}{col}). With row_major packing we
// can use mul(matrix, vector) — i.e. matrix * column-vector — exactly like the
// Metal source, with NO CPU-side transpose needed.
#pragma pack_matrix(row_major)

cbuffer CubeUniforms : register(b0)
{
    float4x4 invViewProj;   // inverse(proj * view), column-vector math (mul(M, v))
    float4x4 inverseModel;  // inverse(model)
    float2   window;        // normalized lo, hi
    float    steps;
    float    density;
    float    jitter;
    int      stretch;
    int      mip;
    float    pad0;
};

Texture3D<float> dataTex  : register(t0);
Texture2D<float4> cmapTex : register(t1);
Texture2D<float>  tfTex   : register(t2);
SamplerState samp         : register(s0);

struct VSOut
{
    float4 position : SV_Position;
    float2 ndc      : TEXCOORD0;
};

// Fullscreen triangle: (0,0),(2,0),(0,2) in UV -> covers the screen.
VSOut VSMain(uint vid : SV_VertexID)
{
    VSOut o;
    float2 uv  = float2((vid << 1) & 2, vid & 2);
    float2 ndc = uv * 2.0 - 1.0;
    // D3D11 and Metal share the same clip-space convention (y-up NDC, depth
    // z in [0,1]), so this is a 1:1 port of the Metal vertex_cube: position and
    // the ray-reconstruction NDC must agree, so both use the same un-negated ndc.
    o.position = float4(ndc, 0.0, 1.0);
    o.ndc = ndc;
    return o;
}

// HLSL has no asinh intrinsic (unlike Metal/GLSL): asinh(x) = ln(x + sqrt(x²+1)).
float asinh_(float x)
{
    return log(x + sqrt(x * x + 1.0));
}

// Stretch index order matches ImageStretcher.StretchMode (Linear, Log, Sqrt,
// Squared, Asinh), so the volume applies the identical stretch as the 2D slice.
float applyStretch(float x, int mode)
{
    x = saturate(x);
    if (mode == 1) return log10(1.0 + 9.0 * x);              // Log
    if (mode == 2) return sqrt(x);                            // Sqrt
    if (mode == 3) return x * x;                              // Squared
    if (mode == 4) return asinh_(10.0 * x) / asinh_(10.0);    // Asinh
    return x;                                                 // Linear
}

float2 hitBox(float3 orig, float3 dir)
{
    float3 invDir = 1.0 / dir;
    float3 t0 = (float3(-0.5, -0.5, -0.5) - orig) * invDir;
    float3 t1 = (float3( 0.5,  0.5,  0.5) - orig) * invDir;
    float3 tmin = min(t0, t1);
    float3 tmax = max(t0, t1);
    return float2(max(max(tmin.x, tmin.y), tmin.z),
                  min(min(tmax.x, tmax.y), tmax.z));
}

float hashf(float2 p)
{
    return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
}

float4 PSMain(VSOut input) : SV_Target
{
    // Reconstruct the world-space ray from screen NDC, then map into unit-box
    // (texture) space via the inverse model matrix. mul(M, v) == M * v (Metal).
    float4 nearH = mul(invViewProj, float4(input.ndc, 0.0, 1.0));
    float4 farH  = mul(invViewProj, float4(input.ndc, 1.0, 1.0));
    float3 nearW = nearH.xyz / nearH.w;
    float3 farW  = farH.xyz / farH.w;
    float3 ro = mul(inverseModel, float4(nearW, 1.0)).xyz;
    float3 rd = normalize(mul(inverseModel, float4(farW - nearW, 0.0)).xyz);

    float2 bounds = hitBox(ro, rd);
    bounds.x = max(bounds.x, 0.0);
    if (bounds.x >= bounds.y)
        discard;

    float dt = 1.7320508 / steps;            // unit-cube diagonal / steps
    float t  = bounds.x + dt * hashf(input.position.xy + jitter);

    float3 acc = float3(0.0, 0.0, 0.0);
    float alpha = 0.0;
    float mipVal = 0.0;

    [loop]
    for (int i = 0; i < 2048; i++)
    {
        if (t > bounds.y || alpha > 0.98) break;
        if ((float)i >= steps * 1.7320508) break;
        float3 p = ro + rd * t + 0.5;
        float r = dataTex.SampleLevel(samp, p, 0);
        if (r > 0.0)
        {
            float v = (r - window.x) / max(window.y - window.x, 1.0e-6);
            float s = applyStretch(v, stretch);
            if (mip == 1)
            {
                mipVal = max(mipVal, s);
            }
            else
            {
                float a = saturate(tfTex.SampleLevel(samp, float2(s, 0.5), 0) * density * dt * 60.0);
                float3 c = cmapTex.SampleLevel(samp, float2(s, 0.5), 0).rgb;
                acc += (1.0 - alpha) * a * c;
                alpha += (1.0 - alpha) * a;
            }
        }
        t += dt;
    }

    if (mip == 1)
    {
        if (mipVal <= 0.003) discard;
        float a = smoothstep(0.0, 0.25, mipVal);
        return float4(cmapTex.SampleLevel(samp, float2(mipVal, 0.5), 0).rgb * a, a); // premultiplied
    }
    if (alpha <= 0.003) discard;
    return float4(acc, alpha); // premultiplied
}
""";
}
