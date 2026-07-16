using UnityEngine;
using UnityEngine.Windows;

/// <summary>
/// WASD-driven player that carries the hammer.
///
/// The hammer is pivoted at the player, so the player never positions it -- it only drives its
/// rotation. That is done with torque rather than by snapping it to the aimed angle: the head has to
/// be wound up and swung, so it trails the cursor, overshoots, and arrives carrying real momentum.
/// That momentum is exactly what Hammer reads as impact force, so the swing is what does the damage.
/// Flick the cursor across a wall and the head whips round into it; ease it round and it taps.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Player : MonoBehaviour
{
    [SerializeField] float moveSpeed = 4f;
    [SerializeField] Hammer hammer;
    [SerializeField] float swingTorque = 20.0f;

    Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void FixedUpdate()
    {
        Move();
        UpdateHammer();
    }
    private void Move()
    {
        rb.linearVelocity = InputManager.Instance.MoveInput() * moveSpeed;
    }
    void UpdateHammer()
    {
        Vector2 targetDir = InputManager.Instance.AimInput(transform.position);
        Vector2 dir = hammer.transform.up;
        float angle = Mathf.DeltaAngle(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, Mathf.Atan2(targetDir.y, targetDir.x) * Mathf.Rad2Deg);
        hammer.rb.angularVelocity = angle * swingTorque;
        Debug.Log(angle);
    }
}
