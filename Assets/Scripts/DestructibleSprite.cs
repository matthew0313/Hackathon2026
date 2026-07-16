using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A sprite that can have holes carved out of it by blunt impacts.
///
/// Damage lives in a per-instance R8 mask texture that the shader multiplies into the
/// sprite's alpha; the source sprite asset is never touched. Collision is driven by a
/// separate, much coarser occupancy grid that is greedy-meshed into rectangles, so the
/// collider rebuild cost is decoupled from the visual resolution.
///
/// Carves are applied to the CPU mask immediately but the GPU upload and the collider
/// rebuild are deferred to <see cref="DestructionScheduler"/>, which batches them to one
/// texture upload per frame and spends a fixed time budget on collider work.
///
/// Requires Read/Write Enabled on the source sprite's texture (needed once, at startup,
/// to derive the object's solid shape from its alpha).
///
/// Note: SpriteRenderer flipX/flipY flip the rendering but not the collider, so damage would
/// stop lining up with the visual. Use a negative transform scale instead -- that goes through
/// the transform, which both the shader and the carve maths already respect.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[DisallowMultipleComponent]
public class DestructibleSprite : MonoBehaviour
{
    [Header("Resolution")]
    [Tooltip("Sprite pixels per mask texel. 1 = pixel perfect. 2 = half res, a quarter of the memory and carve cost.")]
    [SerializeField, Range(1, 8)] int maskDownscale = 1;

    [Tooltip("Mask texels per collision cell. Higher = blockier collision, much cheaper rebuilds.")]
    [SerializeField, Range(1, 16)] int cellSize = 4;

    [Tooltip("Collision cells per chunk edge. Each chunk owns one PolygonCollider2D and re-meshes independently.")]
    [SerializeField, Range(4, 32)] int chunkCells = 16;

    [Header("Shape")]
    [Tooltip("Sprite alpha at or above this counts as solid when deriving the collision shape.")]
    [SerializeField, Range(0f, 1f)] float alphaThreshold = 0.5f;

    [Header("Hammer blows")]
    [Tooltip("How ragged a hammer crater is. 0 = a clean lens; higher makes the bite reach much " +
             "deeper in some directions than others, giving it fingers instead of a smooth edge.")]
    [SerializeField, Range(0f, 0.6f)] float impactRoughness = 0.34f;

    [Tooltip("Chips knocked loose to the sides of a hammer crater, detached from it. 0 = none.")]
    [SerializeField, Range(0, 6)] int spallChips = 3;

    [Header("Debris")]
    [SerializeField] ParticleSystem debris;
    [Tooltip("Debris particles emitted per square world unit of material removed.")]
    [SerializeField] float debrisPerUnitArea = 60f;
    [SerializeField] int maxDebrisPerHit = 24;
    [Tooltip("Tint debris with the average colour of the sprite instead of the particle system's own colour.")]
    [SerializeField] bool tintDebrisFromSprite = true;

    // Hairline gap between adjacent collider paths. See RebuildChunk.
    const float PathInset = 1e-4f;

    // How far a hammer crater's rim chips back past the contact point, as a fraction of its depth.
    const float RimChip = 0.18f;

    static readonly int MaskId = Shader.PropertyToID("_DamageMask");
    static readonly int MaskRectId = Shader.PropertyToID("_MaskRect");
    static readonly int MaskTexelSizeId = Shader.PropertyToID("_MaskTexelSize");

    SpriteRenderer sr;
    MaterialPropertyBlock mpb;
    Texture2D maskTex;

    // Mask space. 255 = intact, 0 = carved away.
    byte[] mask;
    // Immutable: was this texel opaque in the source art? Only opaque texels contribute to collision.
    bool[] opaque;
    int maskW, maskH;

    // Collision grid. A cell collides while it holds at least one intact opaque texel, so collision
    // over-approximates the art by up to cellSize-1 texels rather than under-approximating it. That
    // keeps the invariant "anything visible is collidable", which is what makes every last pixel
    // reachable by a physics-driven hit. Shrink cellSize to tighten the fit.
    int[] cellSolid;      // count of intact opaque texels per cell
    int gridW, gridH;

    // Chunks.
    int chunksX, chunksY;
    PolygonCollider2D[] chunkColliders;
    bool[] chunkDirty;
    readonly Queue<int> dirtyChunks = new Queue<int>();

    bool maskDirty;
    uint blowSeed;
    bool[] visited;
    readonly List<RectInt> rects = new List<RectInt>();
    readonly List<Vector2> pathBuffer = new List<Vector2>(4);

    Vector2 boundsMin;
    Vector2 texelToLocal;   // local units per mask texel
    Color debrisTint = Color.white;
    float worldTexelArea;

    internal bool Queued { get; set; }
    internal bool HasPendingColliderWork => dirtyChunks.Count > 0;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr.sprite == null)
        {
            Debug.LogError($"{name}: DestructibleSprite needs a sprite assigned.", this);
            enabled = false;
            return;
        }

        if (sr.flipX || sr.flipY)
        {
            Debug.LogWarning(
                $"{name}: SpriteRenderer flipX/flipY flips the sprite's rendering but not its collider, " +
                "so carved holes will not line up with the visual. Use a negative transform scale instead.", this);
        }

        if (!BuildFromSprite(sr.sprite))
        {
            enabled = false;
            return;
        }

        BuildColliders();
    }

    bool BuildFromSprite(Sprite sprite)
    {
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

        maskW = Mathf.Max(1, Mathf.CeilToInt((float)srcW / maskDownscale));
        maskH = Mathf.Max(1, Mathf.CeilToInt((float)srcH / maskDownscale));

        mask = new byte[maskW * maskH];
        opaque = new bool[maskW * maskH];

        gridW = Mathf.CeilToInt((float)maskW / cellSize);
        gridH = Mathf.CeilToInt((float)maskH / cellSize);
        cellSolid = new int[gridW * gridH];

        // Point-sample the source alpha down into mask space and seed the occupancy counts.
        double tr = 0, tg = 0, tb = 0;
        int opaqueCount = 0;
        for (int y = 0; y < maskH; y++)
        {
            int sy = Mathf.Min(srcH - 1, y * maskDownscale);
            for (int x = 0; x < maskW; x++)
            {
                int sx = Mathf.Min(srcW - 1, x * maskDownscale);
                Color c = src[sy * srcW + sx];
                int i = y * maskW + x;

                mask[i] = 255;
                if (c.a < alphaThreshold) continue;

                opaque[i] = true;
                cellSolid[(y / cellSize) * gridW + (x / cellSize)]++;
                tr += c.r; tg += c.g; tb += c.b;
                opaqueCount++;
            }
        }

        if (tintDebrisFromSprite && opaqueCount > 0)
            debrisTint = new Color((float)(tr / opaqueCount), (float)(tg / opaqueCount), (float)(tb / opaqueCount), 1f);

        // linear: this is a mask, not colour -- it must not go through sRGB conversion.
        maskTex = new Texture2D(maskW, maskH, TextureFormat.R8, false, true)
        {
            name = $"{name}_DamageMask",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        maskTex.SetPixelData(mask, 0);
        maskTex.Apply(false, false);

        // The shader receives sprite-atlas UVs, so hand it the exact texture region the mask grid covers
        // -- maskW * maskDownscale pixels starting at (rx, ry) -- so its remap matches the carve maths.
        float texW = srcTex.width;
        float texH = srcTex.height;
        var maskRect = new Vector4(
            rx / texW, ry / texH,
            (maskW * maskDownscale) / texW, (maskH * maskDownscale) / texH);

        mpb = new MaterialPropertyBlock();
        sr.GetPropertyBlock(mpb);
        mpb.SetTexture(MaskId, maskTex);
        mpb.SetVector(MaskRectId, maskRect);
        mpb.SetVector(MaskTexelSizeId, new Vector4(1f / maskW, 1f / maskH, maskW, maskH));
        sr.SetPropertyBlock(mpb);

        // Local-space corner of mask texel (0,0). bounds.min is the untrimmed rect's corner, so shift by
        // the trim offset (plus the sub-pixel correction from flooring texRect down to whole pixels).
        float ppu = sprite.pixelsPerUnit;
        Vector2 pixelOffset = sprite.textureRectOffset + new Vector2(rx - texRect.x, ry - texRect.y);
        boundsMin = (Vector2)sprite.bounds.min + pixelOffset / ppu;
        texelToLocal = new Vector2(maskDownscale / ppu, maskDownscale / ppu);

        Vector3 scale = transform.lossyScale;
        worldTexelArea = Mathf.Abs(texelToLocal.x * scale.x) * Mathf.Abs(texelToLocal.y * scale.y);

        chunksX = Mathf.CeilToInt((float)gridW / chunkCells);
        chunksY = Mathf.CeilToInt((float)gridH / chunkCells);
        chunkDirty = new bool[chunksX * chunksY];
        visited = new bool[chunkCells * chunkCells];
        return true;
    }

    void BuildColliders()
    {
        chunkColliders = new PolygonCollider2D[chunksX * chunksY];
        for (int i = 0; i < chunkColliders.Length; i++)
        {
            var col = gameObject.AddComponent<PolygonCollider2D>();
            col.pathCount = 0;
            chunkColliders[i] = col;
            RebuildChunk(i);
        }
    }

    void OnDestroy()
    {
        if (maskTex != null) Destroy(maskTex);
    }

    /// <summary>
    /// Carve a circular hole -- shots, explosions, anything with no particular shape to it.
    /// Safe to call many times per frame; the GPU upload and collider rebuild are batched by
    /// the scheduler. See <see cref="CarveHammerBlow"/> for hammer strikes.
    /// </summary>
    public void Carve(Vector3 worldPos, float worldRadius)
    {
        if (!enabled || mask == null) return;

        Vector2 local = transform.InverseTransformPoint(worldPos);
        float cx = (local.x - boundsMin.x) / texelToLocal.x;
        float cy = (local.y - boundsMin.y) / texelToLocal.y;

        // Scale is baked out by InverseTransformPoint, so undo it on the radius too.
        float rt = (worldRadius / WorldScale) / texelToLocal.x;
        if (rt <= 0f) return;

        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rt));
        int x1 = Mathf.Min(maskW - 1, Mathf.CeilToInt(cx + rt));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - rt));
        int y1 = Mathf.Min(maskH - 1, Mathf.CeilToInt(cy + rt));
        if (x0 > x1 || y0 > y1) return;

        float r2 = rt * rt;
        int removed = 0;
        bool changed = false;

        for (int y = y0; y <= y1; y++)
        {
            float dy = y + 0.5f - cy;
            float dy2 = dy * dy;
            int row = y * maskW;
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx;
                if (dx * dx + dy2 > r2) continue;
                changed |= RemoveTexel(row + x, x, y, ref removed);
            }
        }

        FinishCarve(worldPos, changed, removed);
    }

    /// <summary>
    /// Carve the crater left by a hammer blow.
    ///
    /// Not a stamp of the head's outline -- that reads as machined. A blunt impact spalls the
    /// material around it, so this cuts a lens as wide as the striking face at the surface, tapering
    /// away at its deepest point, with a chipped, irregular rim that varies from blow to blow.
    /// </summary>
    /// <param name="worldContact">Where the head met the surface, in world space.</param>
    /// <param name="worldStrikeDir">Direction the head was travelling. Need not be normalised.</param>
    /// <param name="worldFaceWidth">Width of the head's striking face, in world units -- how wide the
    /// crater's mouth is.</param>
    /// <param name="worldDepth">How far the blow bites in along the strike, in world units. Drive this
    /// from the impact force.</param>
    public void CarveHammerBlow(Vector3 worldContact, Vector2 worldStrikeDir, float worldFaceWidth, float worldDepth)
    {
        if (!enabled || mask == null) return;
        if (worldFaceWidth <= 0f || worldDepth <= 0f) return;

        Vector2 dir = transform.InverseTransformDirection(worldStrikeDir);
        if (dir.sqrMagnitude < 1e-8f) return;
        dir.Normalize();

        Vector2 local = transform.InverseTransformPoint(worldContact);
        float cx = (local.x - boundsMin.x) / texelToLocal.x;
        float cy = (local.y - boundsMin.y) / texelToLocal.y;

        float scale = WorldScale;
        float hw = (worldFaceWidth * 0.5f / scale) / texelToLocal.x;   // half-width across the face
        float hd = (worldDepth / scale) / texelToLocal.x;              // bite along the strike
        if (hw <= 0f || hd <= 0f) return;

        // Phases for this blow's rim, so no two craters are cut to the same silhouette.
        uint s = blowSeed++;
        float p0 = Hash01(s * 747796405u + 1u) * Mathf.PI * 2f;
        float p1 = Hash01(s * 2891336453u + 2u) * Mathf.PI * 2f;
        float p2 = Hash01(s * 3266489917u + 3u) * Mathf.PI * 2f;

        // Exact AABB of the ellipse (semi-axis hd along dir, hw across), padded for the roughest rim.
        float pad = 1f + impactRoughness;
        float ex = Mathf.Sqrt(hd * hd * dir.x * dir.x + hw * hw * dir.y * dir.y) * pad;
        float ey = Mathf.Sqrt(hd * hd * dir.y * dir.y + hw * hw * dir.x * dir.x) * pad;

        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - ex));
        int x1 = Mathf.Min(maskW - 1, Mathf.CeilToInt(cx + ex));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ey));
        int y1 = Mathf.Min(maskH - 1, Mathf.CeilToInt(cy + ey));
        if (x0 > x1 || y0 > y1) return;

        float outerSq = pad * pad;
        float backLimit = -RimChip * hd;   // a little material chips off behind the contact too
        int removed = 0;
        bool changed = false;

        for (int y = y0; y <= y1; y++)
        {
            float dy = y + 0.5f - cy;
            int row = y * maskW;
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx;

                // Into the blow's frame: u runs along the strike, v across the face.
                float u = dx * dir.x + dy * dir.y;
                if (u < backLimit) continue;          // never eat material behind the surface
                float v = -dx * dir.y + dy * dir.x;

                float un = u / hd, vn = v / hw;
                float r2 = un * un + vn * vn;
                if (r2 > outerSq) continue;           // outside even the roughest rim; skip the atan2

                // Ragged rim: push the ellipse's edge in and out with angle.
                float ang = Mathf.Atan2(vn, un);
                float n = 0.55f * Mathf.Sin(3f * ang + p0)
                        + 0.30f * Mathf.Sin(7f * ang + p1)
                        + 0.15f * Mathf.Sin(13f * ang + p2);
                float limit = 1f + impactRoughness * n;
                if (r2 > limit * limit) continue;

                changed |= RemoveTexel(row + x, x, y, ref removed);
            }
        }

        // Spall: chips knocked loose off to the sides of the blow, detached from the crater itself.
        // A blunt hit on brittle material shatters it rather than punching a clean bite, and these
        // separate flecks are most of what sells that.
        for (int k = 0; k < spallChips; k++)
        {
            uint cs = s * 9781u + (uint)k * 6151u;
            float ang = (Hash01(cs + 11u) * 2f - 1f) * (60f * Mathf.Deg2Rad);   // off the strike axis
            float dist = Mathf.Lerp(0.95f, 1.6f, Hash01(cs + 22u)) * hd;
            float rad = Mathf.Lerp(0.10f, 0.22f, Hash01(cs + 33u)) * hw * 2f;

            float u = Mathf.Cos(ang) * dist;
            float v = Mathf.Sin(ang) * dist;
            CarveDisc(cx + u * dir.x - v * dir.y, cy + u * dir.y + v * dir.x, rad, ref removed, ref changed);
        }

        FinishCarve(worldContact, changed, removed);
    }

    /// Carves a filled circle in texel space. Shared by the spall chips; no scheduling of its own.
    void CarveDisc(float cx, float cy, float r, ref int removed, ref bool changed)
    {
        if (r <= 0f) return;

        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r));
        int x1 = Mathf.Min(maskW - 1, Mathf.CeilToInt(cx + r));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r));
        int y1 = Mathf.Min(maskH - 1, Mathf.CeilToInt(cy + r));
        if (x0 > x1 || y0 > y1) return;

        float r2 = r * r;
        for (int y = y0; y <= y1; y++)
        {
            float dy = y + 0.5f - cy;
            float dy2 = dy * dy;
            int row = y * maskW;
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx;
                if (dx * dx + dy2 > r2) continue;
                changed |= RemoveTexel(row + x, x, y, ref removed);
            }
        }
    }

    /// Cheap integer hash -> 0..1. Only used to vary crater rims between blows.
    static float Hash01(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352du;
        x ^= x >> 15; x *= 0x846ca68bu;
        x ^= x >> 16;
        return (x & 0xFFFFFF) / (float)0xFFFFFF;
    }

    float WorldScale => Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));

    /// Clears one texel and keeps the cell's occupancy count in step. Returns whether anything changed.
    bool RemoveTexel(int i, int x, int y, ref int removed)
    {
        if (mask[i] == 0) return false;
        mask[i] = 0;
        if (!opaque[i]) return true;

        removed++;
        int c = (y / cellSize) * gridW + (x / cellSize);
        // Emptying the cell is the only thing that changes the collision shape.
        if (--cellSolid[c] == 0) MarkChunkDirty(c);
        return true;
    }

    void FinishCarve(Vector3 worldPos, bool changed, int removed)
    {
        if (!changed) return;

        maskDirty = true;
        DestructionScheduler.Enqueue(this);

        if (removed > 0) EmitDebris(worldPos, removed);
    }

    void MarkChunkDirty(int cellIndex)
    {
        int chunk = ((cellIndex / gridW) / chunkCells) * chunksX + ((cellIndex % gridW) / chunkCells);
        if (chunkDirty[chunk]) return;
        chunkDirty[chunk] = true;
        dirtyChunks.Enqueue(chunk);
    }

    void EmitDebris(Vector3 worldPos, int texelsRemoved)
    {
        if (debris == null) return;

        int count = Mathf.Clamp(
            Mathf.RoundToInt(texelsRemoved * worldTexelArea * debrisPerUnitArea), 1, maxDebrisPerHit);

        var ep = new ParticleSystem.EmitParams
        {
            position = debris.main.simulationSpace == ParticleSystemSimulationSpace.World
                ? worldPos
                : debris.transform.InverseTransformPoint(worldPos),
            applyShapeToPosition = true,
        };
        if (tintDebrisFromSprite) ep.startColor = debrisTint;

        debris.Emit(ep, count);
    }

    internal void FlushMask()
    {
        if (!maskDirty) return;
        maskDirty = false;
        maskTex.SetPixelData(mask, 0);
        maskTex.Apply(false, false);
    }

    internal void RebuildNextChunk()
    {
        if (dirtyChunks.Count == 0) return;
        int chunk = dirtyChunks.Dequeue();
        chunkDirty[chunk] = false;
        RebuildChunk(chunk);
    }

    bool CellSolid(int gx, int gy) => cellSolid[gy * gridW + gx] > 0;

    /// <summary>
    /// Greedy-mesh one chunk's solid cells into maximal rectangles and hand them to that
    /// chunk's collider as quad paths. Cells are boolean and axis-aligned, so this is both
    /// cheaper and far less fiddly than contour tracing plus simplification.
    /// </summary>
    void RebuildChunk(int chunk)
    {
        int chx = chunk % chunksX;
        int chy = chunk / chunksX;
        int cx0 = chx * chunkCells;
        int cy0 = chy * chunkCells;
        int w = Mathf.Min(cx0 + chunkCells, gridW) - cx0;
        int h = Mathf.Min(cy0 + chunkCells, gridH) - cy0;

        System.Array.Clear(visited, 0, visited.Length);
        rects.Clear();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (visited[y * chunkCells + x] || !CellSolid(cx0 + x, cy0 + y)) continue;

                int rw = 1;
                while (x + rw < w && !visited[y * chunkCells + x + rw] && CellSolid(cx0 + x + rw, cy0 + y))
                    rw++;

                int rh = 1;
                while (y + rh < h)
                {
                    bool rowFits = true;
                    for (int k = 0; k < rw; k++)
                    {
                        if (!visited[(y + rh) * chunkCells + x + k] && CellSolid(cx0 + x + k, cy0 + y + rh)) continue;
                        rowFits = false;
                        break;
                    }
                    if (!rowFits) break;
                    rh++;
                }

                for (int yy = 0; yy < rh; yy++)
                    for (int xx = 0; xx < rw; xx++)
                        visited[(y + yy) * chunkCells + x + xx] = true;

                rects.Add(new RectInt(cx0 + x, cy0 + y, rw, rh));
            }
        }

        var col = chunkColliders[chunk];
        col.pathCount = rects.Count;
        for (int i = 0; i < rects.Count; i++)
        {
            RectInt r = rects[i];
            Vector2 min = CellCornerToLocal(r.xMin, r.yMin);
            Vector2 max = CellCornerToLocal(r.xMax, r.yMax);

            // Unity's PolygonCollider2D triangulation is not robust when two paths on the same
            // collider share a collinear edge: it can silently eat a sliver off one of them, leaving
            // visible pixels with no collision on them -- which then strand, because a physics-driven
            // hit can no longer reach them. Whether it happens depends on the exact float bits, so it
            // shows up as the odd stray pixel rather than anything reproducible. A hairline inset stops
            // paths from ever touching. It is a fraction of one texel and far below the physics contact
            // offset, so nothing can slip through the seams.
            float inset = Mathf.Min(PathInset, Mathf.Min(max.x - min.x, max.y - min.y) * 0.05f);
            min.x += inset; min.y += inset;
            max.x -= inset; max.y -= inset;

            pathBuffer.Clear();
            pathBuffer.Add(min);
            pathBuffer.Add(new Vector2(max.x, min.y));
            pathBuffer.Add(max);
            pathBuffer.Add(new Vector2(min.x, max.y));
            col.SetPath(i, pathBuffer);
        }
        col.enabled = rects.Count > 0;
    }

    Vector2 CellCornerToLocal(int gx, int gy)
    {
        // The last cell on each axis may be partial, so clamp to the real mask extent.
        int tx = Mathf.Min(gx * cellSize, maskW);
        int ty = Mathf.Min(gy * cellSize, maskH);
        return new Vector2(boundsMin.x + tx * texelToLocal.x, boundsMin.y + ty * texelToLocal.y);
    }
}
