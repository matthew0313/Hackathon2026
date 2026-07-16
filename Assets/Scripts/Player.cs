using UnityEngine;

/// <summary>
/// WASD-driven player that carries the hammer.
///
/// The hammer hangs off a hinge at the player, so the player never positions it -- it only drives its
/// rotation, and does that by servoing the body's angular velocity toward the aimed angle rather than
/// writing the rotation. Writing it would teleport the head straight through whatever it was meant to
/// hit; driving velocity leaves the head in the solver, so it collides on the way round and contacts
/// report the real impulses Hammer reads as blow force. The speed is proportional to how far the head
/// still has to travel, so flinging the cursor across a wall whips it round and lands hard, while
/// easing it round taps -- the swing is still what does the damage.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Player : MonoBehaviour
{
    [SerializeField] float moveSpeed = 4f;
    [SerializeField] Hammer hammer;
    [Tooltip("How hard the head chases the cursor. Its reciprocal is roughly the seconds it takes to " +
             "close most of the gap, so 10 = about a tenth of a second when nothing is in the way.")]
    [SerializeField] float followSpeed = 10.0f;
    [SerializeField] float hammerMoveTime = 0.3f;
    Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }
    private void Update()
    {
        UpdateHammer();
    }
    void FixedUpdate()
    {
        Move();
    }
    private void Move()
    {
        rb.linearVelocity = InputManager.Instance.MoveInput() * moveSpeed;
    }
    void UpdateHammer()
    {
        Vector2 targetDir = InputManager.Instance.AimInput(transform.position);
        Vector2 dir = hammer.transform.up;
        float targetAngle = Mathf.Atan2(targetDir.y, targetDir.x) * Mathf.Rad2Deg;
        float angleDiff = Mathf.DeltaAngle(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, targetAngle);
        hammer.rb.angularVelocity = angleDiff;
    }
}
