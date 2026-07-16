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
// uv is whatever coordinate _MaskRect is expressed in -- the sprite shader passes texture UVs
// with _MaskRect as the sprite's atlas sub-rect, the tilemap shader passes local position with
// _MaskRect as the mask's local-space footprint. The remap below is the same either way.
half4 ApplyDamage(half4 c, float2 uv)
{
    float2 maskUV = (uv - _MaskRect.xy) / _MaskRect.zw;
    half m = SAMPLE_TEXTURE2D(_DamageMask, sampler_DamageMask, maskUV).r;
    c.a *= m;

    // The mask data is binary; bilinear filtering stretches each carved edge into a one-texel
    // gradient that antialiases the cut (and already faded c.a above). The rim is still found with
    // taps -- "this texel survived but a neighbour did not" -- now easing in over that same
    // gradient rather than switching on per texel. Clamp wrapping means taps off the sprite read
    // as intact, so the outer silhouette never gets a rim.
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
