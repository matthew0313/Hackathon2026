using UnityEngine;
[RequireComponent(typeof(Rigidbody2D))]
public class Hammer : MonoBehaviour
{
    [SerializeField] BoxCollider2D hammerHead;

    public Rigidbody2D rb { get; private set; }

    [Header("Blow")]
    [SerializeField] float minImpulse = 0.5f;
    [SerializeField] float maxImpulse = 6f;
    [SerializeField] float minDepth = 0.02f;
    [SerializeField] float maxDepth = 0.09f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        var target = collision.collider.GetComponent<DestructibleSprite>();
        if (target == null) return;
        if (hammerHead == null) return;

        // Sum every contact: a face landing flat-on reports several, and the solver splits the
        // impulse between them. Reading only the first would under-read the hardest blows.
        float impulse = 0f;
        for (int i = 0; i < collision.contactCount; i++) impulse += collision.GetContact(i).normalImpulse;
        if (impulse < minImpulse) return;

        ContactPoint2D hit = collision.GetContact(0);
        // The contact normal points out of the struck surface toward us, so the blow went the other way.
        Vector2 strike = -hit.normal;

        float force = Mathf.InverseLerp(minImpulse, maxImpulse, impulse);
        target.CarveHammerBlow(hit.point, strike, FaceWidth(strike), Mathf.Lerp(minDepth, maxDepth, force));
        Debug.Log("EE");
    }

    /// <summary>
    /// The head's silhouette width as seen along the strike -- its actual contact patch, whichever way
    /// the head happens to be turned. Saves having to declare which of the box's axes is "the face",
    /// and means a corner-on hit correctly bites narrower than a flat-on one.
    /// </summary>
    float FaceWidth(Vector2 strike)
    {
        Vector2 across = new Vector2(-strike.y, strike.x);
        Vector3 scale = hammerHead.transform.lossyScale;
        Vector2 ax = (Vector2)hammerHead.transform.right * (hammerHead.size.x * scale.x);
        Vector2 ay = (Vector2)hammerHead.transform.up * (hammerHead.size.y * scale.y);
        return Mathf.Abs(Vector2.Dot(ax, across)) + Mathf.Abs(Vector2.Dot(ay, across));
    }
}
