#ifndef TREE_GENERATOR_LEAF_WIND_INCLUDED
#define TREE_GENERATOR_LEAF_WIND_INCLUDED

// Shared wind offset for TreeGenerator leaf shaders.
// uv.y: 0 at stem, 1 at tip (triangle leaf / billboard quad).

float3 TreeGeneratorApplyLeafWindWS(
    float3 positionWS,
    float2 uv,
    float leafWindEnabled,
    float strength,
    float frequency,
    float turbulence,
    float phaseScale,
    float maskExponent,
    float3 windDirection,
    float time)
{
    if (leafWindEnabled < 0.5)
        return positionWS;

    float expMask = max(maskExponent, 0.05);
    float mask = pow(saturate(uv.y), expMask);

    float3 wdir = windDirection;
    float len = length(wdir);
    wdir = len > 1e-5 ? wdir / len : float3(1, 0, 0);

    float ph = dot(positionWS, float3(0.731, 1.127, 0.653)) * phaseScale + time * frequency;
    float gust = sin(ph) * cos(ph * 0.77 + time * frequency * 1.315);
    gust += sin(dot(positionWS.xz, float2(1.713, 2.317)) * phaseScale + time * (frequency * 2.07)) * turbulence * 0.45;
    gust *= (1.0 + turbulence * 0.35 * sin(time * frequency * 3.1 + positionWS.y * phaseScale * 2.0));

    return positionWS + wdir * (gust * strength * mask);
}

#endif
