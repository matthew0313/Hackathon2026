using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

/// <summary>
/// Batches the expensive half of destruction across every <see cref="DestructibleSurface"/>
/// in the scene.
///
/// Mask uploads are cheap and happen for every dirty object each frame, so the visual
/// always tracks the hit immediately. Collider rebuilds are metered against a frame budget
/// and serviced round-robin, so twenty objects hit in the same frame spread their re-mesh
/// cost over the next few frames instead of spiking. Collision lagging the visual by a
/// frame or two is not perceptible in play.
///
/// Creates itself on demand; there is no need to place one in the scene.
/// </summary>
[DefaultExecutionOrder(1000)]
public class DestructionScheduler : MonoBehaviour
{
    [Tooltip("Milliseconds per frame to spend rebuilding colliders across all destructibles.")]
    [SerializeField] float frameBudgetMs = 2f;

    static DestructionScheduler instance;
    static bool quitting;

    readonly List<DestructibleSurface> pending = new List<DestructibleSurface>();
    readonly Stopwatch stopwatch = new Stopwatch();
    int cursor;

    // Statics survive play sessions when Enter Play Mode Options disables domain reload.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        quitting = false;
    }

    public static void Enqueue(DestructibleSurface d)
    {
        if (d == null || d.Queued || quitting) return;

        if (instance == null)
        {
            var go = new GameObject(nameof(DestructionScheduler)) { hideFlags = HideFlags.DontSave };
            instance = go.AddComponent<DestructionScheduler>();
        }

        d.Queued = true;
        instance.pending.Add(d);
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    void OnApplicationQuit() => quitting = true;

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void LateUpdate()
    {
        if (pending.Count == 0) return;

        // One texture upload per dirty object, so the visual always lands the frame it was carved.
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            if (pending[i] == null) pending.RemoveAt(i);
            else pending[i].FlushMask();
        }

        stopwatch.Restart();
        while (pending.Count > 0 && stopwatch.Elapsed.TotalMilliseconds < frameBudgetMs)
        {
            if (cursor >= pending.Count) cursor = 0;
            var d = pending[cursor];

            // Every iteration either removes an entry or rebuilds one chunk, so this terminates.
            if (d == null || !d.HasPendingColliderWork)
            {
                if (d != null) d.Queued = false;
                pending.RemoveAt(cursor);
                continue;
            }

            d.RebuildNextChunk();

            if (!d.HasPendingColliderWork)
            {
                d.Queued = false;
                pending.RemoveAt(cursor);
            }
            else
            {
                cursor++;
            }
        }
    }
}
