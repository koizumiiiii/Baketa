// ResizeShader.hlsl - GPU Bilinear Resize Shader for Issue #193
// Vertex Shader + Pixel Shader for texture downscaling

// Texture and Sampler
Texture2D<float4> sourceTexture : register(t0);
SamplerState bilinearSampler : register(s0);

// Vertex Shader Input/Output
struct VSInput
{
    float2 Position : POSITION;
    float2 TexCoord : TEXCOORD0;
};

struct PSInput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

// Vertex Shader - Full-screen quad
PSInput VSMain(VSInput input)
{
    PSInput output;
    output.Position = float4(input.Position, 0.0f, 1.0f);
    output.TexCoord = input.TexCoord;
    return output;
}

// Pixel Shader - Bilinear sampling (hardware accelerated)
float4 PSMain(PSInput input) : SV_TARGET
{
    return sourceTexture.Sample(bilinearSampler, input.TexCoord);
}
