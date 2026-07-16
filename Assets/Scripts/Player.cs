using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class Player : Entity
{
    Camera mainCam;
    [SerializeField] float moveSpeed = 4f;
    [SerializeField] Hammer hammer;
    [Tooltip("How hard the head chases the cursor. Its reciprocal is roughly the seconds it takes to " +
             "close most of the gap, so 10 = about a tenth of a second when nothing is in the way.")]
    [SerializeField] float followSpeed = 10.0f;
    //[field:SerializeField] public float maxStamina { get; private set; } = 100.0f;
    //[field:SerializeField] public float staminaLowerThreshold { get; private set; } = 30.0f;
    //[field:SerializeField] public float stamina { get; private set; }
    //[SerializeField] float staminaDepleteRate = 0.1f, staminaRegen = 10.0f;
    [SerializeField] float recoilTime = 0.3f;


    Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        mainCam = Camera.main;
        //stamina = maxStamina;
    }
    private void Update()
    {
        //stamina = Mathf.Min(maxStamina, stamina + staminaRegen * Time.deltaTime);
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
        float targetAngle = Mathf.Atan2(targetDir.y, targetDir.x) * Mathf.Rad2Deg;
        float angleDiff = Mathf.DeltaAngle(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, targetAngle);

        // A fresh blow's bounce-back owns the body for a moment (see Hammer.Bounce); chasing the
        // cursor again immediately would overwrite the recoil before it is ever seen.
        while(Time.time - hammer.lastBounceTime < recoilTime)
        {
            Mouse.current.WarpCursorPosition(mainCam.WorldToScreenPoint(hammer.transform.position + hammer.transform.up * 5.0f));
            return;
        }

        float omega = angleDiff * followSpeed;
        //if (stamina < staminaLowerThreshold) omega *= Mathf.Lerp(1.0f, 0.0f, (staminaLowerThreshold - stamina) / staminaLowerThreshold);
        Vector2 pivot = hammer.transform.TransformPoint(hammer.joint.anchor);
        Vector2 r = hammer.rb.worldCenterOfMass - pivot;
        hammer.rb.angularVelocity = omega;
        hammer.rb.linearVelocity = rb.linearVelocity + omega * Mathf.Deg2Rad * new Vector2(-r.y, r.x);
        //stamina -= Mathf.Abs(omega) * staminaDepleteRate * Time.fixedDeltaTime;
    }
}
