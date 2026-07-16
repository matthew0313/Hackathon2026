using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody2D))]
public class Player : Entity
{
    Camera mainCam;
    [SerializeField] float moveSpeed = 4f;
    [SerializeField] Hammer hammer;
    [SerializeField] Transform rotator;
    [Tooltip("How hard the head chases the cursor. Its reciprocal is roughly the seconds it takes to " +
             "close most of the gap, so 10 = about a tenth of a second when nothing is in the way.")]
    [SerializeField] float followSpeed = 10.0f;
    //[field:SerializeField] public float maxStamina { get; private set; } = 100.0f;
    //[field:SerializeField] public float staminaLowerThreshold { get; private set; } = 30.0f;
    //[field:SerializeField] public float stamina { get; private set; }
    //[SerializeField] float staminaDepleteRate = 0.1f, staminaRegen = 10.0f;
    [SerializeField] float recoilTime = 0.3f;
    [field:SerializeField] public int maxHp { get; private set; } = 3;
    public int hp = 3;
    public event Action onHpChange;
    public event Action<int> onDamage;
    public Rigidbody2D rb { get; private set; }

    bool dead = false;
    public void GetDamage(int damage)
    {
        if (dead) return;
        hp = Mathf.Max(0, hp - damage);
        onDamage?.Invoke(damage);
        onHpChange?.Invoke();
        if(hp <= 0)
        {
            rb.simulated = false;
            dead = true;
            enabled = false;
            GameManager.Instance.GameOver();
        }
    }
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        mainCam = Camera.main;
        hp = maxHp;
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
        rotator.right = InputManager.Instance.AimInput(transform.position);
    }
    void UpdateHammer()
    {
        Vector2 targetDir = InputManager.Instance.AimInput(transform.position);
        Vector2 dir = hammer.transform.up;
        float targetAngle = Mathf.Atan2(targetDir.y, targetDir.x) * Mathf.Rad2Deg;
        float angleDiff = Mathf.DeltaAngle(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, targetAngle);


        float omega = angleDiff * followSpeed;
        //if (stamina < staminaLowerThreshold) omega *= Mathf.Lerp(1.0f, 0.0f, (staminaLowerThreshold - stamina) / staminaLowerThreshold);
        Vector2 pivot = hammer.transform.TransformPoint(hammer.joint.anchor);
        Vector2 r = hammer.rb.worldCenterOfMass - pivot;
        hammer.rb.angularVelocity = omega;
        hammer.rb.linearVelocity = rb.linearVelocity + omega * Mathf.Deg2Rad * new Vector2(-r.y, r.x);
        //stamina -= Mathf.Abs(omega) * staminaDepleteRate * Time.fixedDeltaTime;
    }
}
