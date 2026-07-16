using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base for surfaces that can have holes carved out of them by blunt impacts.
///
/// Damage lives in a per-instance R8 mask texture that the surface's shader multiplies into
/// alpha; the source art is never touched. Collision is greedy-meshed from the same texel grid
/// into axis-aligned rectangles, so the collider matches the visible art exactly rather than
/// approximating it.
///
/// Carves are applied to the CPU mask immediately but the GPU upload and the collider
/// rebuild are deferred to <see cref="DestructionScheduler"/>, which batches them to one
/// texture upload per frame and spends a fixed time budget on collider work.
///
/// Every carve is followed by a local cleanup pass: barely-attached jags along the fresh edge
/// erode away and small fully-detached fragments break off, so repeated blows leave chipped
/// silhouettes rather than pixel noise. See <see cref="CleanupCarvedRegion"/>.
///
/// Subclasses implement <see cref="BuildShape"/>: derive the solid texels from their art, and
/// bind the mask to their renderer via <see cref="InitMask"/>, <see cref="MarkSolid"/> and
/// <see cref="FinishShape"/>. See DestructibleSprite and DestructibleTilemap.
/// </summary>
[DisallowMultipleComponent]
public abstract class DestructibleSurface : MonoBehaviour
{
    [Header("State")]
    [Tooltip("Can this surface currently be carved? While false, blows still collide but remove nothing.")]
    public bool canBreak = true;

    [Tooltip("Whether the surface's colliders start on. Toggle at runtime via collisionEnabled.")]
    [SerializeField] bool collision = true;

    [Header("Collision")]
    [Tooltip("Mask texels per chunk edge. Each chunk owns one PolygonCollider2D and re-meshes independently. " +
             "Smaller = cheaper individual rebuilds, but more colliders on the object.")]
    [SerializeField, Range(8, 128)] int chunkTexels = 64;

    [Header("Shape")]
    [Tooltip("Art alpha at or above this counts as solid when deriving the collision shape.")]
    [SerializeField, Range(0f, 1f)] protected float alphaThreshold = 0.5f;

    [Header("Hammer blows")]
    [Tooltip("How ragged a hammer crater is. 0 = a clean lens; higher makes the bite reach much " +
             "deeper in some directions than others, giving it fingers instead of a smooth edge.")]
    [SerializeField, Range(0f, 0.6f)] float impactRoughness = 0.34f;

    [Tooltip("Chips knocked loose beside a hammer crater's mouth, detached from it. 0 = none.")]
    [SerializeField, Range(0, 6)] int spallChips = 3;

    [Tooltip("Thin radial cracks fracturing outward from a hammer crater's rim. 0 = none.")]
    [SerializeField, Range(0, 5)] int fractureCracks = 3;

    [Header("Cleanup")]
    [Tooltip("Erosion passes run along a fresh carve edge: solid texels left with almost no neighbours " +
             "are shaved off, so destroyed sides read as chipped material rather than pixel noise. 0 = off.")]
    [SerializeField, Range(0, 3)] int edgeSmoothing = 2;

    [Tooltip("A carve can strand slivers of material with no connection to the rest of the object. " +
             "Loose fragments up to this many texels break off automatically (and leave as debris). 0 = keep them.")]
    [SerializeField, Range(0, 128)] int maxLooseFragment = 24;

    [Header("Debris")]
    [SerializeField] ParticleSystem debris;
    [Tooltip("Debris particles emitted per square world unit of material removed.")]
    [SerializeField] float debrisPerUnitArea = 60f;
    [SerializeField] int maxDebrisPerHit = 24;
    [Tooltip("Tint debris with the average colour of the art instead of the particle system's own colour.")]
    [SerializeField] protected bool tintDebrisFromSprite = true;

    // Hairline gap between adjacent collider paths. See RebuildChunk.
    const float PathInset = 1e-4f;

    // How far a hammer crater's rim chips back past the contact point, as a fraction of its depth.
    const float RimChip = 0.18f;

    static readonly int MaskId = Shader.PropertyToID("_DamageMask");
    static readonly int MaskRectId = Shader.PropertyToID("_MaskRect");
    static readonly int MaskTexelSizeId = Shader.PropertyToID("_MaskTexelSize");

    MaterialPropertyBlock mpb;
    Texture2D maskTex;

    // Mask space. 255 = intact, 0 = carved away.
    byte[] mask;
    // Does this texel currently collide? True only while it was opaque in the source art AND is
    // still intact. Collision is meshed straight off this, at texel resolution, so the collider
    // neither over- nor under-approximates the art: anything visible is collidable and nothing
    // else is. That exactness is what keeps every last pixel reachable by a physics-driven hit
    // without hanging phantom collision off the silhouette.
    bool[] solid;
    int maskW, maskH;

    // Chunks. Each owns one PolygonCollider2D over a chunkTexels-square block of the mask.
    int chunksX, chunksY;
    int[] chunkSolid;     // count of intact opaque texels per chunk; 0 = collider can go dark
    PolygonCollider2D[] chunkColliders;
    bool[] chunkDirty;
    readonly Queue<int> dirtyChunks = new Queue<int>();

    bool maskDirty;
    uint blowSeed;
    bool[] visited;
    readonly List<RectInt> rects = new List<RectInt>();
    readonly List<Vector2> pathBuffer = new List<Vector2>(4);

    // Post-carve cleanup scratch. cleanupSeen is region-local and regrown as regions demand.
    readonly List<int> erodeList = new List<int>();
    readonly List<int> floodStack = new List<int>();
    readonly List<int> fragment = new List<int>();
    bool[] cleanupSeen;

    Vector2 boundsMin;
    Vector2 texelToLocal;   // local units per mask texel
    protected Color debrisTint = Color.white;
    float worldTexelArea;

    // Solid texels at build time vs now, for intactPercentage. Purely visual carving (already
    // transparent art) never touches these, matching what the player perceives as "material".
    int initialSolid;
    int remainingSolid;

    /// <summary>
    /// How much of the surface's original material is still standing, 0..100.
    /// 100 = untouched, 0 = every last solid texel carved away.
    /// </summary>
    public float intactPercentage => initialSolid == 0 ? 0f : 100f * remainingSolid / initialSolid;

    /// <summary>
    /// Turns the surface's collision on and off as one. Carved-empty chunks stay dark when
    /// re-enabling; everything else keeps meshing in the background either way, so toggling
    /// is O(chunks) with no rebuild cost.
    /// </summary>
    public bool collisionEnabled
    {
        get => collision;
        set
        {
            if (collision == value) return;
            collision = value;
            if (chunkColliders == null) return;   // before Awake; BuildColliders will apply it
            for (int i = 0; i < chunkColliders.Length; i++)
                chunkColliders[i].enabled = value && chunkSolid[i] > 0;
        }
    }

    internal bool Queued { get; set; }
    internal bool HasPendingColliderWork => dirtyChunks.Count > 0;

    // For ShadowShape2DProvider_Destructible: the chunk colliders are the exact silhouette of the
    // surviving art, so 2D shadows are built straight from their paths. The version bumps whenever
    // any chunk re-meshes, so shadow geometry only rebuilds when a carve actually moved collision.
    internal int ColliderShapeVersion { get; private set; }
    internal PolygonCollider2D[] ChunkColliders => chunkColliders;

    void Awake()
    {
        if (!BuildShape())
        {
            enabled = false;
            return;
        }
        BuildColliders();
    }

    /// <summary>
    /// Derive the solid shape from the surface's art and bind the mask to its renderer:
    /// call <see cref="InitMask"/>, mark opaque texels with <see cref="MarkSolid"/>, then
    /// <see cref="FinishShape"/>. Return false (after logging why) to disable the component.
    /// </summary>
    protected abstract bool BuildShape();

    /// <summary>
    /// Allocates the mask grid, fully intact and with no solid texels yet.
    /// </summary>
    /// <param name="w">Mask width in texels.</param>
    /// <param name="h">Mask height in texels.</param>
    /// <param name="boundsMin">Local-space position of mask texel (0,0)'s corner.</param>
    /// <param name="texelToLocal">Local units per mask texel.</param>
    protected void InitMask(int w, int h, Vector2 boundsMin, Vector2 texelToLocal)
    {
        maskW = w;
        maskH = h;
        this.boundsMin = boundsMin;
        this.texelToLocal = texelToLocal;

        mask = new byte[maskW * maskH];
        for (int i = 0; i < mask.Length; i++) mask[i] = 255;
        solid = new bool[maskW * maskH];

        chunksX = Mathf.CeilToInt((float)maskW / chunkTexels);
        chunksY = Mathf.CeilToInt((float)maskH / chunkTexels);
        chunkSolid = new int[chunksX * chunksY];
        chunkDirty = new bool[chunksX * chunksY];
        visited = new bool[chunkTexels * chunkTexels];

        Vector3 scale = transform.lossyScale;
        worldTexelArea = Mathf.Abs(texelToLocal.x * scale.x) * Mathf.Abs(texelToLocal.y * scale.y);
    }

    /// Marks one texel as opaque-and-collidable during <see cref="BuildShape"/>.
    protected void MarkSolid(int x, int y)
    {
        int i = y * maskW + x;
        if (solid[i]) return;
        solid[i] = true;
        chunkSolid[ChunkOf(x, y)]++;
        initialSolid++;
        remainingSolid++;
    }

    /// <summary>
    /// Creates the mask texture and binds it to the renderer via a MaterialPropertyBlock.
    /// maskRect is handed to the shader untouched; what space it describes is a contract
    /// between the subclass and its shader (atlas UVs for sprites, local space for tilemaps).
    /// </summary>
    protected void FinishShape(Renderer renderer, Vector4 maskRect)
    {
        // The mask is bound per-renderer, so a wrong material fails silently: colliders carve
        // fine while the art never changes. Catch that here instead.
        Material sharedMat = renderer.sharedMaterial;
        if (sharedMat == null || !sharedMat.HasProperty(MaskId))
            Debug.LogError(
                $"{name}: the renderer's material ('{(sharedMat == null ? "none" : sharedMat.name)}') has no " +
                "_DamageMask property, so carves will not show. Assign a material using one of the " +
                "Custom/Destructible shaders.", this);
        // linear: this is a mask, not colour -- it must not go through sRGB conversion.
        // Bilinear: the mask data itself stays binary; the filtering is what antialiases carved
        // edges, ramping alpha across one texel instead of stair-stepping texel by texel.
        maskTex = new Texture2D(maskW, maskH, TextureFormat.R8, false, true)
        {
            name = $"{name}_DamageMask",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        maskTex.SetPixelData(mask, 0);
        maskTex.Apply(false, false);

        mpb = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(mpb);
        mpb.SetTexture(MaskId, maskTex);
        mpb.SetVector(MaskRectId, maskRect);
        mpb.SetVector(MaskTexelSizeId, new Vector4(1f / maskW, 1f / maskH, maskW, maskH));
        renderer.SetPropertyBlock(mpb);
    }

    int ChunkOf(int x, int y) => (y / chunkTexels) * chunksX + (x / chunkTexels);

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
        if (!enabled || !canBreak || mask == null) return;

        Vector2 c = WorldToTexel(worldPos);
        float rt = ToTexels(worldRadius);
        if (rt <= 0f) return;

        var tally = CarveTally.Empty;
        CarveDisc(c.x, c.y, rt, ref tally);
        FinishCarve(worldPos, ref tally);
    }

    /// <summary>
    /// Carve the crater left by a hammer blow.
    ///
    /// Not a stamp of the head's outline -- that reads as machined. A blunt impact fractures the
    /// material around it, so this cuts a lens as wide as the striking face at the surface and
    /// tapering to its deepest point, with a chipped irregular rim, thin radial cracks running out
    /// from it, and loose spall flecks beside the mouth. No two blows cut the same silhouette.
    /// </summary>
    /// <param name="worldContact">Where the head met the surface, in world space.</param>
    /// <param name="worldStrikeDir">Direction the head was travelling. Need not be normalised.</param>
    /// <param name="worldFaceWidth">Width of the head's striking face, in world units -- how wide the
    /// crater's mouth is.</param>
    /// <param name="worldDepth">How far the blow bites in along the strike, in world units. Drive this
    /// from the impact force.</param>
    public void CarveHammerBlow(Vector3 worldContact, Vector2 worldStrikeDir, float worldFaceWidth, float worldDepth)
    {
        if (!enabled || !canBreak || mask == null) return;
        if (worldFaceWidth <= 0f || worldDepth <= 0f) return;

        Vector2 dir = transform.InverseTransformDirection(worldStrikeDir);
        if (dir.sqrMagnitude < 1e-8f) return;
        dir.Normalize();

        Vector2 c = WorldToTexel(worldContact);
        float hw = ToTexels(worldFaceWidth * 0.5f);   // half-width across the striking face
        float hd = ToTexels(worldDepth);              // bite along the strike
        if (hw <= 0f || hd <= 0f) return;

        uint seed = blowSeed++;
        var tally = CarveTally.Empty;

        CarveCrater(c, dir, hw, hd, seed, ref tally);
        CarveCracks(c, dir, hw, hd, seed, ref tally);
        CarveSpall(c, dir, hw, hd, seed, ref tally);

        FinishCarve(worldContact, ref tally);
    }

    /// <summary>
    /// The main bite of a hammer blow: an ellipse spanning the striking face at the surface and
    /// tapering along the strike, its rim pushed in and out with angle so the edge is ragged
    /// rather than machined.
    /// </summary>
    void CarveCrater(Vector2 c, Vector2 dir, float hw, float hd, uint seed, ref CarveTally tally)
    {
        // Phases for this blow's rim, so no two craters are cut to the same silhouette.
        float p0 = Hash01(seed * 747796405u + 1u) * Mathf.PI * 2f;
        float p1 = Hash01(seed * 2891336453u + 2u) * Mathf.PI * 2f;
        float p2 = Hash01(seed * 3266489917u + 3u) * Mathf.PI * 2f;

        // Exact AABB of the ellipse (semi-axis hd along dir, hw across), padded for the roughest rim.
        float pad = 1f + impactRoughness;
        float ex = Mathf.Sqrt(hd * hd * dir.x * dir.x + hw * hw * dir.y * dir.y) * pad;
        float ey = Mathf.Sqrt(hd * hd * dir.y * dir.y + hw * hw * dir.x * dir.x) * pad;

        int x0 = Mathf.Max(0, Mathf.FloorToInt(c.x - ex));
        int x1 = Mathf.Min(maskW - 1, Mathf.CeilToInt(c.x + ex));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(c.y - ey));
        int y1 = Mathf.Min(maskH - 1, Mathf.CeilToInt(c.y + ey));
        if (x0 > x1 || y0 > y1) return;

        float outerSq = pad * pad;
        float backLimit = -RimChip * hd;   // a little material chips off behind the contact too

        for (int y = y0; y <= y1; y++)
        {
            float dy = y + 0.5f - c.y;
            int row = y * maskW;
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - c.x;

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

                RemoveTexel(row + x, x, y, ref tally);
            }
        }
    }

    /// <summary>
    /// Thin radial cracks running out from the crater rim. A blunt hit on brittle material
    /// fractures well past the bite itself, and these hairlines are what tie the crater into the
    /// surrounding material instead of leaving it a clean punched hole.
    /// </summary>
    void CarveCracks(Vector2 c, Vector2 dir, float hw, float hd, uint seed, ref CarveTally tally)
    {
        for (int k = 0; k < fractureCracks; k++)
        {
            uint cs = seed * 5099u + (uint)k * 7919u;

            // Radiate into the forward hemisphere, biased along the strike.
            float ang = (Hash01(cs + 1u) * 2f - 1f) * (80f * Mathf.Deg2Rad);
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            var rdir = new Vector2(ca * dir.x - sa * dir.y, ca * dir.y + sa * dir.x);
            var perp = new Vector2(-rdir.y, rdir.x);

            // Start just inside the crater's rim so the crack always connects to it.
            float rim = 1f / Mathf.Sqrt(ca * ca / (hd * hd) + sa * sa / (hw * hw));
            Vector2 p = c + rdir * (rim * 0.8f);

            float len = Mathf.Lerp(0.9f, 2f, Hash01(cs + 2u)) * hd;
            // Root thickness scales with the face, but never below a texel or the crack vanishes.
            float rootR = Mathf.Max(0.75f, Mathf.Lerp(0.10f, 0.16f, Hash01(cs + 3u)) * hw);

            // Walk outward in overlapping discs that taper to a point, wandering sideways each
            // step so the line reads as a fracture, not a drilled row of holes.
            int steps = Mathf.Clamp(Mathf.CeilToInt(len / rootR), 4, 24);
            for (int i = 0; i < steps; i++)
            {
                float t = i / (steps - 1f);
                CarveDisc(p.x, p.y, Mathf.Lerp(rootR, rootR * 0.25f, t), ref tally);
                float wander = (Hash01(cs + 40u + (uint)i) - 0.5f) * rootR;
                p += rdir * (len / steps) + perp * wander;
            }
        }
    }

    /// <summary>
    /// Chips knocked loose around the crater's mouth, detached from it. Spall flies off the
    /// surface beside the impact -- not ahead of it -- so the flecks sit just clear of the mouth
    /// on either side, hugging the surface line.
    /// </summary>
    void CarveSpall(Vector2 c, Vector2 dir, float hw, float hd, uint seed, ref CarveTally tally)
    {
        for (int k = 0; k < spallChips; k++)
        {
            uint cs = seed * 9781u + (uint)k * 6151u;
            float side = (k & 1) == 0 ? 1f : -1f;   // alternate sides so the chips never all bunch up

            float u = Mathf.Lerp(-0.05f, 0.4f, Hash01(cs + 11u)) * hd;         // shallow: near the surface
            float v = side * Mathf.Lerp(1.15f, 1.8f, Hash01(cs + 22u)) * hw;   // just clear of the mouth
            float rad = Mathf.Lerp(0.15f, 0.30f, Hash01(cs + 33u)) * hw;

            CarveDisc(c.x + u * dir.x - v * dir.y, c.y + u * dir.y + v * dir.x, rad, ref tally);
        }
    }

    /// Carves a filled circle in texel space. The primitive under Carve, the cracks and the
    /// spall chips; no scheduling of its own.
    void CarveDisc(float cx, float cy, float r, ref CarveTally tally)
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
                RemoveTexel(row + x, x, y, ref tally);
            }
        }
    }

    /// Cheap integer hash -> 0..1. Only used to vary hammer craters between blows.
    static float Hash01(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352du;
        x ^= x >> 15; x *= 0x846ca68bu;
        x ^= x >> 16;
        return (x & 0xFFFFFF) / (float)0xFFFFFF;
    }

    float WorldScale => Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));

    /// World position -> mask texel coordinates.
    Vector2 WorldToTexel(Vector3 worldPos)
    {
        Vector2 local = transform.InverseTransformPoint(worldPos);
        return new Vector2(
            (local.x - boundsMin.x) / texelToLocal.x,
            (local.y - boundsMin.y) / texelToLocal.y);
    }

    /// World-space distance -> mask texels. InverseTransformPoint bakes the scale out of
    /// positions, so distances must shed it too or radii come out wrong on scaled objects.
    float ToTexels(float worldDist) => (worldDist / WorldScale) / texelToLocal.x;

    /// Running result of one carve call: whether the mask changed at all (GPU upload needed),
    /// how many collidable texels went (drives how much debris to emit), and the texel AABB of
    /// those removals (bounds the post-carve cleanup).
    struct CarveTally
    {
        public bool changed;
        public int removed;
        public int x0, y0, x1, y1;

        public static CarveTally Empty => new CarveTally
            { x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue };

        public void Grow(int x, int y)
        {
            if (x < x0) x0 = x;
            if (x > x1) x1 = x;
            if (y < y0) y0 = y;
            if (y > y1) y1 = y;
        }
    }

    /// Clears one texel and keeps the chunk's occupancy count in step.
    void RemoveTexel(int i, int x, int y, ref CarveTally tally)
    {
        if (mask[i] == 0) return;
        mask[i] = 0;
        tally.changed = true;

        // mask was intact, so solid[i] still reflects whether the source art was opaque here.
        // A transparent texel is a visual-only change: the mask uploads, but collision cannot move.
        if (!solid[i]) return;

        solid[i] = false;
        remainingSolid--;
        tally.removed++;
        tally.Grow(x, y);
        int chunk = ChunkOf(x, y);
        chunkSolid[chunk]--;
        MarkChunkDirty(chunk);
    }

    void FinishCarve(Vector3 worldPos, ref CarveTally tally)
    {
        if (!tally.changed) return;

        if (tally.removed > 0) CleanupCarvedRegion(ref tally);

        maskDirty = true;
        DestructionScheduler.Enqueue(this);

        if (tally.removed > 0) EmitDebris(worldPos, tally.removed);
    }

    /// <summary>
    /// Tidies up around a fresh carve: erode away the barely-attached jags along the new edge,
    /// then break off any small piece the carve left floating with no connection to the rest of
    /// the object. Both run only inside the carve's own AABB plus padding, so the undamaged
    /// silhouette is never touched, and both remove through <see cref="RemoveTexel"/> so the
    /// debris and collider bookkeeping stay in step.
    /// </summary>
    void CleanupCarvedRegion(ref CarveTally tally)
    {
        int pad = edgeSmoothing + 1;
        int rx0 = Mathf.Max(0, tally.x0 - pad);
        int ry0 = Mathf.Max(0, tally.y0 - pad);
        int rx1 = Mathf.Min(maskW - 1, tally.x1 + pad);
        int ry1 = Mathf.Min(maskH - 1, tally.y1 + pad);

        for (int pass = 0; pass < edgeSmoothing; pass++)
            if (!ErodeJags(rx0, ry0, rx1, ry1, ref tally))
                break;

        if (maxLooseFragment > 0) BreakOffFragments(rx0, ry0, rx1, ry1, ref tally);
    }

    /// One erosion pass over the region: shave off solid texels with three or fewer solid
    /// neighbours of eight. That kills lone specks, one-texel-wide slivers and staircase corners,
    /// while flat and diagonal faces (five neighbours) are left alone. Marks first and removes
    /// after, so the result carries no sweep-direction bias. Returns whether anything eroded.
    bool ErodeJags(int rx0, int ry0, int rx1, int ry1, ref CarveTally tally)
    {
        erodeList.Clear();
        for (int y = ry0; y <= ry1; y++)
        {
            int row = y * maskW;
            for (int x = rx0; x <= rx1; x++)
                if (solid[row + x] && SolidNeighbours(x, y) <= 3)
                    erodeList.Add(row + x);
        }

        for (int k = 0; k < erodeList.Count; k++)
        {
            int i = erodeList[k];
            RemoveTexel(i, i % maskW, i / maskW, ref tally);
        }
        return erodeList.Count > 0;
    }

    /// How many of a texel's eight neighbours are solid. Off-texture counts as empty.
    int SolidNeighbours(int x, int y)
    {
        int n = 0;
        for (int dy = -1; dy <= 1; dy++)
        {
            int ny = y + dy;
            if (ny < 0 || ny >= maskH) continue;
            int row = ny * maskW;
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx;
                if (nx >= 0 && nx < maskW && solid[row + nx]) n++;
            }
        }
        return n;
    }

    /// <summary>
    /// Flood-fill the region and break off every solid piece that is fully detached and no bigger
    /// than <see cref="maxLooseFragment"/>. Connectivity is 4-way to match the collider: pieces
    /// touching only diagonally share a single corner between two collider paths, so they are
    /// already physically separate. A piece that reaches the region border may continue beyond
    /// what was searched, so it is assumed attached and kept.
    /// </summary>
    void BreakOffFragments(int rx0, int ry0, int rx1, int ry1, ref CarveTally tally)
    {
        int rw = rx1 - rx0 + 1;
        int rh = ry1 - ry0 + 1;
        if (cleanupSeen == null || cleanupSeen.Length < rw * rh) cleanupSeen = new bool[rw * rh];
        else System.Array.Clear(cleanupSeen, 0, rw * rh);

        void Visit(int nx, int ny)
        {
            if (nx < rx0 || nx > rx1 || ny < ry0 || ny > ry1) return;
            int l = (ny - ry0) * rw + (nx - rx0);
            if (cleanupSeen[l] || !solid[ny * maskW + nx]) return;
            cleanupSeen[l] = true;
            floodStack.Add(ny * maskW + nx);
        }

        for (int y = ry0; y <= ry1; y++)
        {
            for (int x = rx0; x <= rx1; x++)
            {
                if (cleanupSeen[(y - ry0) * rw + (x - rx0)] || !solid[y * maskW + x]) continue;

                fragment.Clear();
                floodStack.Clear();
                cleanupSeen[(y - ry0) * rw + (x - rx0)] = true;
                floodStack.Add(y * maskW + x);
                bool attached = false;

                while (floodStack.Count > 0)
                {
                    int i = floodStack[floodStack.Count - 1];
                    floodStack.RemoveAt(floodStack.Count - 1);
                    fragment.Add(i);

                    int cx = i % maskW, cy = i / maskW;
                    if (cx == rx0 || cx == rx1 || cy == ry0 || cy == ry1) attached = true;

                    Visit(cx - 1, cy);
                    Visit(cx + 1, cy);
                    Visit(cx, cy - 1);
                    Visit(cx, cy + 1);
                }

                if (attached || fragment.Count > maxLooseFragment) continue;
                for (int k = 0; k < fragment.Count; k++)
                {
                    int i = fragment[k];
                    RemoveTexel(i, i % maskW, i / maskW, ref tally);
                }
            }
        }
    }

    void MarkChunkDirty(int chunk)
    {
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

    /// <summary>
    /// Greedy-mesh one chunk's solid texels into maximal rectangles and hand them to that
    /// chunk's collider as quad paths. Texels are boolean and axis-aligned, so this is both
    /// cheaper and far less fiddly than contour tracing plus simplification -- and because it
    /// runs at texel resolution, the result is the art's exact silhouette.
    /// </summary>
    void RebuildChunk(int chunk)
    {
        ColliderShapeVersion++;
        var col = chunkColliders[chunk];
        if (chunkSolid[chunk] == 0)
        {
            col.pathCount = 0;
            col.enabled = false;
            return;
        }

        int chx = chunk % chunksX;
        int chy = chunk / chunksX;
        int tx0 = chx * chunkTexels;
        int ty0 = chy * chunkTexels;
        int w = Mathf.Min(tx0 + chunkTexels, maskW) - tx0;
        int h = Mathf.Min(ty0 + chunkTexels, maskH) - ty0;

        System.Array.Clear(visited, 0, visited.Length);
        rects.Clear();

        for (int y = 0; y < h; y++)
        {
            int row = (ty0 + y) * maskW + tx0;
            for (int x = 0; x < w; x++)
            {
                if (visited[y * chunkTexels + x] || !solid[row + x]) continue;

                int rw = 1;
                while (x + rw < w && !visited[y * chunkTexels + x + rw] && solid[row + x + rw])
                    rw++;

                int rh = 1;
                while (y + rh < h)
                {
                    int next = (ty0 + y + rh) * maskW + tx0;
                    bool rowFits = true;
                    for (int k = 0; k < rw; k++)
                    {
                        if (!visited[(y + rh) * chunkTexels + x + k] && solid[next + x + k]) continue;
                        rowFits = false;
                        break;
                    }
                    if (!rowFits) break;
                    rh++;
                }

                for (int yy = 0; yy < rh; yy++)
                    for (int xx = 0; xx < rw; xx++)
                        visited[(y + yy) * chunkTexels + x + xx] = true;

                rects.Add(new RectInt(tx0 + x, ty0 + y, rw, rh));
            }
        }

        col.pathCount = rects.Count;
        for (int i = 0; i < rects.Count; i++)
        {
            RectInt r = rects[i];
            Vector2 min = TexelCornerToLocal(r.xMin, r.yMin);
            Vector2 max = TexelCornerToLocal(r.xMax, r.yMax);

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
        col.enabled = collision;   // chunkSolid > 0, so the mesh above always yields at least one rect
    }

    Vector2 TexelCornerToLocal(int tx, int ty)
        => new Vector2(boundsMin.x + tx * texelToLocal.x, boundsMin.y + ty * texelToLocal.y);
}
