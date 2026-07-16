#ifndef DESTRUCTIBLE_MASK_INCLUDED
#define DESTRUCTIBLE_MASK_INCLUDED

// Shared damage-mask sampling for the Destructible Sprite passes.
//
// Expects _MaskRect, _MaskTexelSize, _RimColor and _RimTexels to be declared already -- they live in
// each pass's UnityPerMaterial CBUFFER, which must keep an identical layout across every pass for the
// SRP Batcher, so they cannot be declared here.
//
// _DamageMask, _MaskRect and _MaskTexelSize are supplied per-renderer by DestructibleSprite through a
// MaterialPropertyBlock, so all instances share one material.

TEXTURE2D(_DamageMask);
SAMPLER(sampler_DamageMask);

// Multiplies the damage mask into c.a and tints surviving edges next to carved texels.
// uv is the sprite's texture UV; the mask covers only this sprite's sub-rect of that texture.
half4 ApplyDamage(half4 c, float2 uv)
{
    float2 maskUV = (uv - _MaskRect.xy) / _MaskRect.zw;
    half m = SAMPLE_TEXTURE2D(_DamageMask, sampler_DamageMask, maskUV).r;
    c.a *= m;

    // The mask is binary and point-filtered, so there is no gradient to smoothstep across. Detect the
    // edge with taps instead: "this texel survived but a neighbour did not". Clamp wrapping means taps
    // off the sprite read as intact, so the outer silhouette never gets a rim.
    if (_RimTexels > 0.0h && m > 0.5h)
    {
        float2 o = _MaskTexelSize.xy * _RimTexels;
        half n =   SAMPLE_TEXTURE2D(_DamageMask, sampler_DamageMask, maskUV + float2( o.x, 0)).r;
        n = min(n, SAMPLE_TEXTURE2D(_DamageMask, sampler_DamageMask, maskUV + float2(-o.x, 0)).r);
        n = min(n, SAMPLE_TEXTURE2D(_DamageMask, sampler_DamageMask, maskUV + float2(0,  o.y)).r);
        n = min(n, SAMPLE_TEXTURE2D(_DamageMask, sampler_DamageMask, maskUV + float2(0, -o.y)).r);
        c.rgb = lerp(c.rgb, _RimColor.rgb, (1.0h - n) * _RimColor.a);
    }

    return c;
}

#endif // DESTRUCTIBLE_MASK_INCLUDED
