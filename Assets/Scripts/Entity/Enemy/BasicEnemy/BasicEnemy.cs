using MEC;
using System.Collections.Generic;
using UnityEngine;

namespace BasicEnemy
{
    [RequireComponent(typeof(Rigidbody2D), typeof(BasicEnemy_HpComp))]
    public class BasicEnemy : Entity
    {
        [SerializeField] Animator anim;

        [SerializeField] float detectRange = 10.0f;
        [SerializeField] float detectTime = 1.0f, unDetectTime = 3.0f;
        [SerializeField] LayerMask detectionBlockMask;

        [SerializeField] float chaseDist = 1.5f;
        [SerializeField] float backstepDist = 1.0f;

        [SerializeField] float rotateRate = 10.0f;
        [SerializeField] float chaseSpeed = 5.0f, backstepSpeed = 3.0f;
        [SerializeField] float attackDamage = 10.0f;
        [SerializeField] float attackRange = 2.0f;
        [SerializeField] float attackWindup = 0.5f, attackEndlag = 0.5f;
        [SerializeField] SquareHitbox2D attackHitbox;

        [SerializeField] string FSMPath;

        Rigidbody2D rb;
        BasicEnemy_HpComp hpComp;
        Player player;
        internal TopLayer topLayer;
        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            hpComp = GetComponent<BasicEnemy_HpComp>();
            hpComp.Initialize();
            player = GameObject.FindGameObjectWithTag("Player").GetComponent<Player>();
            topLayer = new(this);
            topLayer.onFSMChange += () => { FSMPath = topLayer.GetFSMPath(); };
            topLayer.OnStateEnter();
        }
        private void Update()
        {
            topLayer.OnStateUpdate();
            CheckDetection();
        }
        private void FixedUpdate()
        {
            topLayer.OnStateFixedUpdate();
        }
        private void OnDestroy()
        {
            topLayer.OnStateExit();
        }
        bool CheckDetection()
        {
            Vector2 dir = player.transform.position - transform.position;
            return !Physics2D.Raycast(transform.position, dir.normalized, dir.magnitude, detectionBlockMask);
        }
        internal class TopLayer : TopLayer<BasicEnemy>
        {
            public TopLayer(BasicEnemy origin) : base(origin)
            {
                defaultState = new Idle(origin, this);
                AddState("Idle", defaultState);
                AddState("Detected", new Detected(origin, this));
                AddState("Dead", new Dead(origin, this));
            }
            class Idle : State<BasicEnemy>
            {
                public Idle(BasicEnemy origin, Layer<BasicEnemy> parent) : base(origin, parent) { }
                float counter = 0.0f;
                public override void OnStateUpdate()
                {
                    base.OnStateUpdate();
                    if (origin.CheckDetection()) counter += Time.deltaTime;
                    else counter = 0.0f;

                    if (counter > origin.detectTime) parentLayer.ChangeState("Detected");
                }
            }
            class Detected : Layer<BasicEnemy>
            {
                public Detected(BasicEnemy origin, Layer<BasicEnemy> parent) : base(origin, parent)
                {
                    defaultState = new Chasing(origin, this);
                    AddState("Chasing", defaultState);
                    AddState("Attacking", new Attacking(origin, this));
                }
                float counter = 0.0f;
                public override void OnStateUpdate()
                {
                    base.OnStateUpdate();
                    if (!origin.CheckDetection()) counter += Time.deltaTime;
                    else counter = 0.0f;

                    if (counter > origin.unDetectTime) parentLayer.ChangeState("Idle");
                }
                class Chasing : State<BasicEnemy>
                {
                    public Chasing(BasicEnemy origin, Layer<BasicEnemy> parent) : base(origin, parent) { }
                    public override void OnStateFixedUpdate()
                    {
                        base.OnStateFixedUpdate();
                        Vector2 dir = origin.player.transform.position - origin.transform.position;
                        origin.rb.linearVelocity = dir.magnitude > origin.chaseDist ? 
                            dir.normalized * origin.chaseSpeed : dir.magnitude < origin.backstepDist ? 
                            dir.normalized * -origin.backstepSpeed : Vector2.zero;
                    }
                    public override void OnStateUpdate()
                    {
                        base.OnStateUpdate();
                        Vector2 dir = origin.player.transform.position - origin.transform.position; dir.Normalize();
                        origin.transform.rotation = Quaternion.Slerp(origin.transform.rotation, Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg), origin.rotateRate * Time.deltaTime);
                        if (Vector2.Distance(origin.transform.position, origin.player.transform.position) < origin.attackRange)
                        {
                            parentLayer.ChangeState("Attacking");
                        }
                    }
                    public override void OnStateExit()
                    {
                        base.OnStateExit();
                        origin.rb.linearVelocity = Vector2.zero;
                    }
                }
                class Attacking : State<BasicEnemy>
                {
                    public Attacking(BasicEnemy origin, Layer<BasicEnemy> parent) : base(origin, parent) { }
                    readonly List<GameObject> hitList = new();
                    CoroutineHandle attack;
                    public override void OnStateEnter()
                    {
                        base.OnStateEnter();
                        attack = Timing.RunCoroutine(Attack());
                    }
                    IEnumerator<float> Attack()
                    {
                        yield return Timing.WaitForSeconds(origin.attackWindup);
                        hitList.Clear();
                        foreach (var i in origin.attackHitbox.Scan())
                        {
                            GameObject hitObject = i.transform.gameObject;
                            if (hitList.Contains(hitObject)) continue; hitList.Add(hitObject);

                            if (hitObject.TryGetComponent(out IDamagable damagable))
                            {
                                damagable.GetDamage(new DamageInfo()
                                {
                                    amount = origin.attackDamage,
                                    damageSource = origin
                                });
                            }
                        }
                        yield return Timing.WaitForSeconds(origin.attackEndlag);
                        parentLayer.ChangeState("Chasing");
                    }
                    public override void OnStateExit()
                    {
                        base.OnStateExit();
                        Timing.KillCoroutines(attack);
                    }
                }
            }
            class Dead : State<BasicEnemy>
            {
                public Dead(BasicEnemy origin, Layer<BasicEnemy> parent) : base(origin, parent) { }
                public override void OnStateEnter()
                {
                    base.OnStateEnter();
                }
                public override void OnStateFixedUpdate()
                {
                    base.OnStateFixedUpdate();
                    origin.rb.linearVelocity = Vector2.zero;
                }
            }
        }
    }
}