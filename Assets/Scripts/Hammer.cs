using Unity.Cinemachine;
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
    public float minDepth = 0.02f;
    public float maxDepth = 0.5f;
    [SerializeField] float minDamage = 10.0f, maxDamage = 30.0f;

    [Header("Camera Impulse")]
    [SerializeField] CinemachineImpulseSource impulseSource;
    [SerializeField] float minCamImpulse, maxCamImpulse;

    [Header("Sound")]
    [SerializeField] Sound wallHitSound;
    [SerializeField] Sound enemyHitSound;

    public bool isEnabled = true;

    private void Awake()
    {
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<Player>();
        rb = GetComponent<Rigidbody2D>();
        joint = GetComponent<HingeJoint2D>();
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (!isEnabled) return;

        float impulse = 0f;
        for (int i = 0; i < collision.contactCount; i++) impulse += collision.GetContact(i).normalImpulse;
        if (impulse < minImpulse) return;

        float force = Mathf.InverseLerp(minImpulse, maxImpulse, impulse);
        if (collision.collider.TryGetComponent(out DestructibleSurface target))
        {
            ContactPoint2D hit = collision.GetContact(0);
            // The contact normal points out of the struck surface toward us, so the blow went the other way.
            Vector2 strike = -hit.normal;

            target.CarveHammerBlow(hit.point, strike, FaceWidth(strike), Mathf.Lerp(minDepth, maxDepth, force));
            
            impulseSource.GenerateImpulse(strike * Mathf.Lerp(minCamImpulse, maxCamImpulse, force));
            if (target.CompareTag("Wall")) AudioManager.Instance.PlaySound(wallHitSound);
            if (target.CompareTag("Enemy")) AudioManager.Instance.PlaySound(enemyHitSound);
        }
        if(collision.transform.TryGetComponent(out IDamagable damagable))
        {
            damagable.GetDamage(new()
            {
                amount = Mathf.Lerp(minDamage, maxDamage, force),
                hitPoint = collision.GetContact(0).point
            });
        }
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
