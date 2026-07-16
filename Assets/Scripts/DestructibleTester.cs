using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hold left mouse button to carve holes in any DestructibleSprite under the cursor.
/// Throwaway harness for eyeballing the effect -- not meant to ship.
/// </summary>
public class DestructibleTester : MonoBehaviour
{
    [SerializeField] Camera cam;
    [SerializeField] float radius = 0.25f;
    [Tooltip("Seconds between carves while the button is held. 0 = every frame.")]
    [SerializeField] float interval = 0.05f;

    readonly Collider2D[] hits = new Collider2D[16];
    ContactFilter2D filter;
    float nextCarveTime;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        filter = ContactFilter2D.noFilter;
        filter.useTriggers = true;
    }

    void Update()
    {
        if (cam == null || Mouse.current == null) return;
        if (!Mouse.current.leftButton.isPressed) return;
        if (Time.time < nextCarveTime) return;
        nextCarveTime = Time.time + interval;

        Vector3 p = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        p.z = 0f;

        int count = Physics2D.OverlapCircle(p, radius, filter, hits);
        for (int i = 0; i < count; i++)
        {
            var d = hits[i].GetComponent<DestructibleSprite>();
            if (d != null) d.Carve(p, radius);
        }
    }
}
