//----------------------------------------------
//        BCG Building Generator
//  Shared interior-mapping math (Built-in + URP SubShaders both include this).
//----------------------------------------------
#ifndef BCG_INTERIOR_MAPPING_INCLUDED
#define BCG_INTERIOR_MAPPING_INCLUDED

float BCG_Hash21(float2 p) {

    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);

}

//  UV-space interior mapping against the pre-projected 4x2 room atlas.
//  uv       : facade strip-atlas UV (8 cells / U tile, 0.125 V per window band)
//  viewTS   : tangent-space camera->fragment direction (unnormalized ok)
//  roomDepth: back-wall image fraction; MUST match the bake (0.5 for the shipped atlas)
//  curtainFraction : fraction of windows that read as blinds-drawn (openness ~0)
//  litHash  : stable per-cell 0..1 hash for night-glow picks
//  openness : stable per-cell interior clarity - 0.05 curtained, else 0.35..1.0
float3 BCG_SampleInterior(sampler2D roomAtlas, float2 uv, float3 viewTS, float roomDepth,
                          float curtainFraction, out float litHash, out float openness) {

    float2 cellF = float2(uv.x * 8.0, uv.y / 0.125);
    float2 cellId = floor(cellF);
    float2 cellUV = frac(cellF);
    litHash = BCG_Hash21(cellId + 17.31);

    //  Per-window openness: some windows are blinds-drawn dark glass, the rest vary in clarity.
    //  Distinct hash offset from the room-pick (0), tint (3.7) and lit (17.31) draws.
    float openHash = BCG_Hash21(cellId + 9.13);
    openness = (openHash < curtainFraction)
        ? 0.05
        : lerp(0.35, 1.0, saturate((openHash - curtainFraction) / max(1.0 - curtainFraction, 1e-4)));

    float roomPick = floor(BCG_Hash21(cellId) * 8.0);
    float2 tile = float2(fmod(roomPick, 4.0), floor(roomPick / 4.0));

    //  Classic single-texture parallax room (farFrac = roomDepth):
    float depthScale = 1.0 / (1.0 - roomDepth) - 1.0;
    float3 pos = float3(cellUV * 2.0 - 1.0, -1.0);
    float3 dir = normalize(viewTS);
    dir.z *= -depthScale;

    float3 id = 1.0 / dir;
    float3 k = abs(id) - pos * id;
    float kMin = min(min(k.x, k.y), k.z);
    pos += kMin * dir;

    float interp = pos.z * 0.5 + 0.5;
    float realZ = saturate(interp) / depthScale + 1.0;
    interp = 1.0 - (1.0 / realZ);
    interp *= depthScale + 1.0;

    float2 roomUV = pos.xy * lerp(1.0, roomDepth, interp);
    roomUV = roomUV * 0.5 + 0.5;

    float2 atlasUV = (tile + roomUV) * float2(0.25, 0.5);

    //  Per-cell tint variation breaks up the 8-room repetition (spec: hash seeds room, tint, lit).
    float tint = lerp(0.78, 1.15, BCG_Hash21(cellId + 3.7));
    return tex2D(roomAtlas, atlasUV).rgb * tint;

}

#endif
