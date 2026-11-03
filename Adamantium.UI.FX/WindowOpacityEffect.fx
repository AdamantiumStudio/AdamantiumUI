// A window's own opacity, where the desktop does not lay the window down with it: everything drawn so far, color and
// alpha alike, times it - blended as the destination times the source alpha, so the frame stays premultiplied.

float WindowAlpha;

// One oversized triangle over the window.
[shader("vertex")]
float4 WindowOpacityVS(uint id : SV_VertexID) : SV_Position
{
    float2 uv = float2((id << 1) & 2, id & 2);
    return float4(uv * 2.0 - 1.0, 0.0, 1.0);
}

[shader("fragment")]
float4 WindowOpacityPS(float4 position : SV_Position) : SV_Target
{
    return float4(0.0, 0.0, 0.0, WindowAlpha);
}

technique WindowOpacity
{
    pass Draw
    {
        VertexShader = WindowOpacityVS;
        PixelShader = WindowOpacityPS;
    }
}
