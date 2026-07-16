using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Lets a ShadowCaster2D cast from a <see cref="DestructibleSurface"/>: add the caster to the
/// same GameObject and pick the destructible under Casting Source (a fresh caster picks it
/// automatically). Shadow geometry is built from the surface's chunk colliders -- the exact
/// silhouette of the surviving art -- and follows along as carves re-mesh them.
///
/// The built-in Collider2D source cannot do this because it reads a single collider, while the
/// surface spreads its collision over one PolygonCollider2D per chunk.
///
/// The chunk colliders only exist in play mode, so the scene view shows no shadow preview.
/// </summary>
[System.Serializable]
public class ShadowShape2DProvider_Destructible : ShadowShape2DProvider
{
    // Runtime-only change tracking; deliberately reset by Enabled/OnPersistantDataCreated.
    [System.NonSerialized] int lastVersion = -1;

    static readonly List<Vector2> pathPoints = new List<Vector2>(4);

    /// Outrank the built-in providers so a fresh ShadowCaster2D on a destructible defaults to it.
    public override int Priority() => 10;

    public override bool IsShapeSource(Component sourceComponent) => sourceComponent is DestructibleSurface;

    public override void OnPersistantDataCreated(Component sourceComponent, ShadowShape2D persistantShadowShape)
        => lastVersion = -1;

    public override void Enabled(Component sourceComponent, ShadowShape2D persistantShadowShape)
        => lastVersion = -1;

    public override void OnBeforeRender(Component sourceComponent, Bounds worldCullingBounds, ShadowShape2D persistantShadowShape)
    {
        var surface = (DestructibleSurface)sourceComponent;
        PolygonCollider2D[] colliders = surface.ChunkColliders;
        if (colliders == null) return;   // shape not built yet
        if (surface.ColliderShapeVersion == lastVersion) return;
        lastVersion = surface.ColliderShapeVersion;

        // Every path is a closed loop: one line segment per vertex.
        int vertexCount = 0;
        for (int i = 0; i < colliders.Length; i++)
            vertexCount += colliders[i].GetTotalPointCount();

        if (vertexCount == 0)
        {
            // Everything carved away; hand over empty geometry so the last shadow disappears too.
            persistantShadowShape.SetShape(new NativeArray<Vector3>(), new NativeArray<int>(),
                ShadowShape2D.OutlineTopology.Lines, ShadowShape2D.WindingOrder.CounterClockwise);
            return;
        }

        var vertices = new NativeArray<Vector3>(vertexCount, Allocator.Temp);
        var indices = new NativeArray<int>(2 * vertexCount, Allocator.Temp);

        int v = 0, n = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            var col = colliders[i];
            for (int p = 0; p < col.pathCount; p++)
            {
                col.GetPath(p, pathPoints);
                int start = v;
                for (int k = 0; k < pathPoints.Count; k++)
                {
                    vertices[v] = pathPoints[k];
                    indices[n++] = v;
                    indices[n++] = k == pathPoints.Count - 1 ? start : v + 1;
                    v++;
                }
            }
        }

        // The greedy mesher winds its quads counter-clockwise. Interior geometry is skipped for
        // the same reason the built-in providers skip it when a Renderer is present: self-shadows
        // use the renderer's own silhouette.
        persistantShadowShape.SetShape(vertices, indices,
            ShadowShape2D.OutlineTopology.Lines, ShadowShape2D.WindingOrder.CounterClockwise);

        vertices.Dispose();
        indices.Dispose();
    }
}
