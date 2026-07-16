using UnityEngine;
[RequireComponent(typeof(Rigidbody2D), typeof(HingeJoint2D))]
public class Hammer : MonoBehaviour
{
    [SerializeField] BoxCollider2D hammerHead;

    Player player;
    public Rigidbody2D rb { get; private set; }
    public HingeJoint2D joint { get; private set; }

    [Header("Blow")]
    [SerializeField] float minImpulse = 0.5f;
    [SerializeField] float maxImpulse = 6f;
    [SerializeField] float minDepth = 0.02f;
    [SerializeField] float maxDepth = 0.5f;

    [Header("Recoil")]
    [Tooltip("How much of the swing's speed comes back off a hard hit. 0 = dead stop, 1 = full rebound.")]
    [SerializeField, Range(0f, 1f)] float bounciness = 0.35f;

    /// While true, Player leaves the hammer's velocity alone so the bounce off a hit is visible
    /// instead of being overwritten by the cursor chase on the very next step.
    public float lastBounceTime { get; private set; } = -9999.0f;
    // The swing speed going into this physics step, captured before the solver reacts to any
    // contact. By callback time rb.angularVelocity is already post-impact, so the bounce has to
    // reverse this pre-impact value instead.
    float impactOmega;

    private void Awake()
    {
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<Player>();
        rb = GetComponent<Rigidbody2D>();
        joint = GetComponent<HingeJoint2D>();
    }

    void FixedUpdate()
    {
        impactOmega = rb.angularVelocity;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        float impulse = 0f;
        for (int i = 0; i < collision.contactCount; i++) impulse += collision.GetContact(i).normalImpulse;
        if (impulse < minImpulse) return;

        // Any hard hit kicks the head back the way it came, destructible or not.
        Bounce();

        var target = collision.collider.GetComponent<DestructibleSurface>();
        if (target == null) return;
        if (hammerHead == null) return;

        ContactPoint2D hit = collision.GetContact(0);
        // The contact normal points out of the struck surface toward us, so the blow went the other way.
        Vector2 strike = -hit.normal;

        float force = Mathf.InverseLerp(minImpulse, maxImpulse, impulse);
        target.CarveHammerBlow(hit.point, strike, FaceWidth(strike), Mathf.Lerp(minDepth, maxDepth, force));
    }

    /// <summary>
    /// Reverse the swing about the hinge, damped by bounciness, and hold the aim servo off for
    /// recoilTime so the rebound is actually seen. Written as the full pivot-consistent velocity
    /// state (spin plus matching COM velocity), same as the servo in Player, or the hinge solver
    /// would turn leftover momentum back into forward rotation and eat the bounce.
    /// </summary>
    void Bounce()
    {
        lastBounceTime = Time.time;

        float omega = -impactOmega * bounciness;                        // deg/s, back the way it came
        Vector2 pivot = transform.TransformPoint(joint.anchor);
        Vector2 r = rb.worldCenterOfMass - pivot;
        Vector2 pivotVelocity = joint.connectedBody != null ? joint.connectedBody.linearVelocity : Vector2.zero;

        rb.angularVelocity = omega;
        rb.linearVelocity = pivotVelocity + omega * Mathf.Deg2Rad * new Vector2(-r.y, r.x);
    }
    float FaceWidth(Vector2 strike)
    {
        Vector2 across = new Vector2(-strike.y, strike.x);
        Vector3 scale = hammerHead.transform.lossyScale;
        Vector2 ax = (Vector2)hammerHead.transform.right * (hammerHead.size.x * scale.x);
        Vector2 ay = (Vector2)hammerHead.transform.up * (hammerHead.size.y * scale.y);
        return Mathf.Abs(Vector2.Dot(ax, across)) + Mathf.Abs(Vector2.Dot(ay, across));
    }
}
