using System.Collections.Generic;
using NavMeshPlus.Components;
using NavMeshPlus.Extensions;
using UnityEngine;
using UnityEngine.AI;
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
///
/// Navigation: NavMeshPlus bakes this tilemap's cells as per-tile sources, which know nothing
/// about the damage mask. So when every solid texel of a cell has been carved away, the tile
/// itself is removed (invisible anyway -- the mask hides it) and the surface's NavMeshData is
/// updated asynchronously in place. Updating in place is what makes it partial: the builder
/// diffs sources against what each navmesh tile was last built from and re-voxelises only the
/// tiles whose sources changed -- the ones the broken tiles vanished from.
/// </summary>
[RequireComponent(typeof(Tilemap), typeof(TilemapRenderer))]
public class DestructibleTilemap : DestructibleSurface
{
    [Header("Resolution")]
    [Tooltip("Mask texels per cell edge. Higher = finer damage and collision, at more memory and " +
             "carve cost. Match the tile art's pixels per cell for pixel-perfect damage.")]
    [SerializeField, Range(4, 64)] int texelsPerCell = 16;

    [Header("NavMesh")]
    [Tooltip("Surface to patch when fully-broken tiles open new paths. Empty = first active surface.")]
    [SerializeField] NavMeshSurface navMeshSurface;
    [Tooltip("Seconds after the last tile breaks before the navmesh recalculates, " +
             "so a burst of blows costs one update instead of one per hit.")]
    [SerializeField] float navMeshDebounce = 0.3f;

    Tilemap tilemap;
    Vector3Int cellsMin;
    int cellsX;
    int[] cellSolid;   // remaining solid texels per cell; reaching 0 retires the tile
    readonly List<Vector3Int> brokenCells = new List<Vector3Int>();

    bool navDirty;
    float navDirtyTime;
    AsyncOperation navMeshOp;

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
        tilemap = GetComponent<Tilemap>();
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

        cellsMin = cells.min;
        cellsX = cells.size.x;
        cellSolid = new int[cells.size.x * cells.size.y];

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

                cellSolid[cy * cellsX + cx] = stamp.count;
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

    /// Tracks each cell's surviving material; the moment a cell's last solid texel goes, the
    /// tile is queued for retirement. A cell can only hit zero once, so no dedup is needed.
    protected override void OnSolidTexelCarved(int x, int y)
    {
        int cx = x / texelsPerCell;
        int cy = y / texelsPerCell;
        if (--cellSolid[cy * cellsX + cx] > 0) return;
        brokenCells.Add(new Vector3Int(cellsMin.x + cx, cellsMin.y + cy, cellsMin.z));
    }

    void LateUpdate()
    {
        if (brokenCells.Count > 0)
        {
            // Retiring the tile changes nothing visibly (its texels are all mask-hidden) or
            // physically (collision is ours, not the TilemapCollider2D's) -- it only stops the
            // cell being collected as a navmesh source on the next update.
            for (int i = 0; i < brokenCells.Count; i++)
                tilemap.SetTile(brokenCells[i], null);
            brokenCells.Clear();
            navDirty = true;
            navDirtyTime = Time.time;
        }

        if (!navDirty || Time.time - navDirtyTime < navMeshDebounce) return;
        if (navMeshOp != null && !navMeshOp.isDone) return;   // update again once the running one lands

        NavMeshSurface surface = navMeshSurface;
        if (surface == null && NavMeshSurface.activeSurfaces.Count > 0) surface = NavMeshSurface.activeSurfaces[0];
        if (surface == null || surface.navMeshData == null) { navDirty = false; return; }

        navDirty = false;
        navMeshOp = UpdateNavMeshIncremental(surface);
    }

    /// <summary>
    /// Update the surface's existing NavMeshData in place, rebuilding only the navmesh tiles
    /// whose sources changed since they were last built.
    ///
    /// Mirrors the runtime source collection of NavMeshSurface.UpdateNavMesh (which is where the
    /// CollectSources2d extension rasterises tilemaps and sprites). The bounds handed to
    /// UpdateNavMeshDataAsync must span the whole world: they are the navmesh's coverage volume,
    /// and anything outside them is dropped from the data, not preserved. The partial behaviour
    /// comes from updating the existing data instead -- the builder diffs the sources per navmesh
    /// tile and skips every tile whose inputs are unchanged, so only the region around removed
    /// tiles pays for voxelisation.
    /// </summary>
    AsyncOperation UpdateNavMeshIncremental(NavMeshSurface surface)
    {
        using var state = new NavMeshBuilderState();
        var sources = new List<NavMeshBuildSource>();
        var markups = new List<NavMeshBuildMarkup>();

        foreach (var m in NavMeshModifier.activeModifiers)
        {
            if ((surface.layerMask & (1 << m.gameObject.layer)) == 0) continue;
            if (!m.AffectsAgentType(surface.agentTypeID)) continue;
            markups.Add(new NavMeshBuildMarkup
            {
                root = m.transform,
                overrideArea = m.overrideArea,
                area = m.area,
                ignoreFromBuild = m.ignoreFromBuild,
            });
        }

        NavMeshBuilder.CollectSources(null, surface.layerMask, surface.useGeometry, surface.defaultArea, markups, sources);
        for (int i = 0; i < surface.NavMeshExtensions.Count; i++)
            surface.NavMeshExtensions[i].CollectSources(surface, sources, state);

        if (surface.ignoreNavMeshAgent)
            sources.RemoveAll(x => x.component != null && x.component.gameObject.GetComponent<NavMeshAgent>() != null);
        if (surface.ignoreNavMeshObstacle)
            sources.RemoveAll(x => x.component != null && x.component.gameObject.GetComponent<NavMeshObstacle>() != null);

        foreach (var m in NavMeshModifierVolume.activeModifiers)
        {
            if ((surface.layerMask & (1 << m.gameObject.layer)) == 0) continue;
            if (!m.AffectsAgentType(surface.agentTypeID)) continue;
            Vector3 scale = m.transform.lossyScale;
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.ModifierBox,
                transform = Matrix4x4.TRS(m.transform.TransformPoint(m.center), m.transform.rotation, Vector3.one),
                size = new Vector3(m.size.x * Mathf.Abs(scale.x), m.size.y * Mathf.Abs(scale.y), m.size.z * Mathf.Abs(scale.z)),
                area = m.area,
            });
        }

        state.worldBounds = surface.CalculateWorldBounds(sources);
        for (int i = 0; i < surface.NavMeshExtensions.Count; i++)
            surface.NavMeshExtensions[i].PostCollectSources(surface, sources, state);

        return NavMeshBuilder.UpdateNavMeshDataAsync(surface.navMeshData, surface.GetBuildSettings(), sources, state.worldBounds);
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
