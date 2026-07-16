using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// A Tilemap that can have holes carved out of it by blunt impacts. See
/// <see cref="DestructibleSurface"/> for how damage, collision, cleanup and scheduling work;
/// this class only derives the solid shape from the painted tiles.
///
/// The shape is rasterised once at startup, cell by cell, from each tile sprite's alpha, so
/// tile textures need Read/Write Enabled. The TilemapRenderer's material must use the
/// Custom/Destructible Tilemap shader, which samples the damage mask by local position -- a
/// tilemap draws from many atlas rects at once, so there is no single sprite rect to remap
/// UVs from the way the sprite shader does.
///
/// Owns collision: it builds its own chunked PolygonCollider2Ds from the mask, and disables
/// any TilemapCollider2D on the object so the two never fight.
///
/// Assumptions, all of the common square-tilemap setup: a rectangular grid, each tile's sprite
/// stretched over its whole cell, and no per-tile transforms (flips/rotations are ignored when
/// rasterising). Tiles painted or erased at runtime are not picked up -- the shape is derived
/// once at startup.
/// </summary>
[RequireComponent(typeof(Tilemap), typeof(TilemapRenderer))]
public class DestructibleTilemap : DestructibleSurface
{
    [Header("Resolution")]
    [Tooltip("Mask texels per cell edge. Higher = finer damage and collision, at more memory and " +
             "carve cost. Match the tile art's pixels per cell for pixel-perfect damage.")]
    [SerializeField, Range(4, 64)] int texelsPerCell = 16;

    /// One tile sprite's solidity at cell resolution, plus its colour sums for the debris tint.
    /// Cached per sprite because a tilemap repeats the same few sprites hundreds of times.
    class Stamp
    {
        public bool[] solid;
        public double r, g, b;
        public int count;
    }

    protected override bool BuildShape()
    {
        var tilemap = GetComponent<Tilemap>();
        tilemap.CompressBounds();
        BoundsInt cells = tilemap.cellBounds;
        if (cells.size.x <= 0 || cells.size.y <= 0)
        {
            Debug.LogError($"{name}: DestructibleTilemap has no tiles to build a shape from.", this);
            return false;
        }

        var tilemapCollider = GetComponent<TilemapCollider2D>();
        if (tilemapCollider != null && tilemapCollider.enabled)
        {
            Debug.LogWarning(
                $"{name}: disabling the TilemapCollider2D -- DestructibleTilemap builds its own " +
                "colliders from the damage mask, and two collision sources would fight.", this);
            tilemapCollider.enabled = false;
        }

        Vector2 cellSize = tilemap.layoutGrid.cellSize;
        Vector2 origin = tilemap.CellToLocal(cells.min);
        InitMask(
            cells.size.x * texelsPerCell, cells.size.y * texelsPerCell,
            origin, cellSize / texelsPerCell);

        var stamps = new Dictionary<Sprite, Stamp>();
        double tr = 0, tg = 0, tb = 0;
        int opaqueCount = 0;
        var cell = new Vector3Int(0, 0, cells.zMin);

        for (int cy = 0; cy < cells.size.y; cy++)
        {
            cell.y = cells.yMin + cy;
            for (int cx = 0; cx < cells.size.x; cx++)
            {
                cell.x = cells.xMin + cx;
                Sprite sprite = tilemap.GetSprite(cell);
                if (sprite == null) continue;

                if (!stamps.TryGetValue(sprite, out Stamp stamp))
                {
                    stamp = RasteriseTile(sprite);
                    if (stamp == null) return false;   // unreadable texture; already logged
                    stamps.Add(sprite, stamp);
                }

                int x0 = cx * texelsPerCell;
                int y0 = cy * texelsPerCell;
                for (int ty = 0; ty < texelsPerCell; ty++)
                    for (int tx = 0; tx < texelsPerCell; tx++)
                        if (stamp.solid[ty * texelsPerCell + tx])
                            MarkSolid(x0 + tx, y0 + ty);

                tr += stamp.r; tg += stamp.g; tb += stamp.b;
                opaqueCount += stamp.count;
            }
        }

        if (opaqueCount == 0)
        {
            Debug.LogError($"{name}: no tile pixel reached the alpha threshold; nothing to destroy.", this);
            return false;
        }

        if (tintDebrisFromSprite)
            debrisTint = new Color((float)(tr / opaqueCount), (float)(tg / opaqueCount), (float)(tb / opaqueCount), 1f);

        // The tilemap shader samples the mask by local position, so the rect is simply the
        // local-space span of the cell bounds the mask grid covers.
        var maskRect = new Vector4(
            origin.x, origin.y,
            cells.size.x * cellSize.x, cells.size.y * cellSize.y);

        FinishShape(GetComponent<TilemapRenderer>(), maskRect);
        return true;
    }

    /// Point-samples one tile sprite's texture rect down to a cell's worth of mask texels.
    /// Returns null (after logging) if the texture is not readable.
    Stamp RasteriseTile(Sprite sprite)
    {
        Texture2D tex = sprite.texture;
        Rect texRect = sprite.textureRect;

        int rx = Mathf.Clamp(Mathf.FloorToInt(texRect.x), 0, tex.width - 1);
        int ry = Mathf.Clamp(Mathf.FloorToInt(texRect.y), 0, tex.height - 1);
        int rw = Mathf.Clamp(Mathf.CeilToInt(texRect.width), 1, tex.width - rx);
        int rh = Mathf.Clamp(Mathf.CeilToInt(texRect.height), 1, tex.height - ry);

        Color[] src;
        try
        {
            src = tex.GetPixels(rx, ry, rw, rh);
        }
        catch (UnityException)
        {
            Debug.LogError(
                $"{name}: tile texture '{tex.name}' is not readable. Enable Read/Write in its import " +
                "settings so the destructible shape can be derived from the tiles' alpha.", this);
            return null;
        }

        var stamp = new Stamp { solid = new bool[texelsPerCell * texelsPerCell] };
        for (int y = 0; y < texelsPerCell; y++)
        {
            int sy = y * rh / texelsPerCell;
            for (int x = 0; x < texelsPerCell; x++)
            {
                Color c = src[sy * rw + x * rw / texelsPerCell];
                if (c.a < alphaThreshold) continue;

                stamp.solid[y * texelsPerCell + x] = true;
                stamp.r += c.r; stamp.g += c.g; stamp.b += c.b;
                stamp.count++;
            }
        }
        return stamp;
    }
}
