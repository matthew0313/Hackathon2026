using UnityEngine;

/// <summary>
/// A sprite that can have holes carved out of it by blunt impacts. See
/// <see cref="DestructibleSurface"/> for how damage, collision, cleanup and scheduling work;
/// this class only derives the solid shape from the sprite's alpha and hands the damage mask
/// to a material using the Custom/Destructible Sprite shader.
///
/// Requires Read/Write Enabled on the source sprite's texture (needed once, at startup,
/// to derive the object's solid shape from its alpha).
///
/// Note: SpriteRenderer flipX/flipY flip the rendering but not the collider, so damage would
/// stop lining up with the visual. Use a negative transform scale instead -- that goes through
/// the transform, which both the shader and the carve maths already respect.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class DestructibleSprite : DestructibleSurface
{
    [Header("Resolution")]
    [Tooltip("Sprite pixels per mask texel. 1 = pixel perfect. 2 = half res, a quarter of the memory and carve cost.")]
    [SerializeField, Range(1, 8)] int maskDownscale = 1;

    protected override bool BuildShape()
    {
        var sr = GetComponent<SpriteRenderer>();
        if (sr.sprite == null)
        {
            Debug.LogError($"{name}: DestructibleSprite needs a sprite assigned.", this);
            return false;
        }

        if (sr.flipX || sr.flipY)
        {
            Debug.LogWarning(
                $"{name}: SpriteRenderer flipX/flipY flips the sprite's rendering but not its collider, " +
                "so carved holes will not line up with the visual. Use a negative transform scale instead.", this);
        }

        Sprite sprite = sr.sprite;

        // textureRect is the sprite's real footprint in its texture. For Tight sprite meshes (Unity's
        // default) it is trimmed inward from sprite.rect by textureRectOffset, whereas sprite.bounds
        // always spans the full untrimmed rect -- so the local-space mapping below must be derived from
        // that offset, not from bounds.min, or the visual and the collider drift apart.
        Rect texRect = sprite.textureRect;
        Texture2D srcTex = sprite.texture;

        int rx = Mathf.Clamp(Mathf.FloorToInt(texRect.x), 0, srcTex.width - 1);
        int ry = Mathf.Clamp(Mathf.FloorToInt(texRect.y), 0, srcTex.height - 1);
        int srcW = Mathf.Clamp(Mathf.CeilToInt(texRect.width), 1, srcTex.width - rx);
        int srcH = Mathf.Clamp(Mathf.CeilToInt(texRect.height), 1, srcTex.height - ry);

        Color[] src;
        try
        {
            src = srcTex.GetPixels(rx, ry, srcW, srcH);
        }
        catch (UnityException)
        {
            Debug.LogError(
                $"{name}: '{srcTex.name}' is not readable. Enable Read/Write in its import settings " +
                "so the destructible shape can be derived from the sprite's alpha.", this);
            return false;
        }

        int maskW = Mathf.Max(1, Mathf.CeilToInt((float)srcW / maskDownscale));
        int maskH = Mathf.Max(1, Mathf.CeilToInt((float)srcH / maskDownscale));

        // Local-space corner of mask texel (0,0). bounds.min is the untrimmed rect's corner, so shift by
        // the trim offset (plus the sub-pixel correction from flooring texRect down to whole pixels).
        float ppu = sprite.pixelsPerUnit;
        Vector2 pixelOffset = sprite.textureRectOffset + new Vector2(rx - texRect.x, ry - texRect.y);
        Vector2 boundsMin = (Vector2)sprite.bounds.min + pixelOffset / ppu;
        InitMask(maskW, maskH, boundsMin, new Vector2(maskDownscale / ppu, maskDownscale / ppu));

        // Point-sample the source alpha down into mask space.
        double tr = 0, tg = 0, tb = 0;
        int opaqueCount = 0;
        for (int y = 0; y < maskH; y++)
        {
            int sy = Mathf.Min(srcH - 1, y * maskDownscale);
            for (int x = 0; x < maskW; x++)
            {
                int sx = Mathf.Min(srcW - 1, x * maskDownscale);
                Color c = src[sy * srcW + sx];
                if (c.a < alphaThreshold) continue;

                MarkSolid(x, y);
                tr += c.r; tg += c.g; tb += c.b;
                opaqueCount++;
            }
        }

        if (tintDebrisFromSprite && opaqueCount > 0)
            debrisTint = new Color((float)(tr / opaqueCount), (float)(tg / opaqueCount), (float)(tb / opaqueCount), 1f);

        // The shader receives sprite-atlas UVs, so hand it the exact texture region the mask grid covers
        // -- maskW * maskDownscale pixels starting at (rx, ry) -- so its remap matches the carve maths.
        float texW = srcTex.width;
        float texH = srcTex.height;
        var maskRect = new Vector4(
            rx / texW, ry / texH,
            (maskW * maskDownscale) / texW, (maskH * maskDownscale) / texH);

        FinishShape(sr, maskRect);
        return true;
    }
}
