using MEC;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace BasicEnemy
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class BasicEnemy : Entity
    {
        [SerializeField] SpriteRenderer spriteRenderer;
        [SerializeField] DestructibleSprite sprite;
        [SerializeField] NavMeshAgent agent;
        [SerializeField] float deadPrecentage = 30.0f;

        [SerializeField] float detectRange = 10.0f;
        [SerializeField] float detectTime = 1.0f, unDetectTime = 3.0f;
        [SerializeField] LayerMask detectionBlockMask;

        [SerializeField] float roamingSpeed = 3.0f;
        [SerializeField] float roamingCooldown = 5.0f;
        [SerializeField] float roamingRadius = 5.0f;
        [SerializeField] float roamingArriveDist = 0.2f;

        [SerializeField] float chaseDist = 1.5f;
        [SerializeField] float backstepDist = 1.0f;

        [SerializeField] float rotateRate = 10.0f;
        [SerializeField] float chaseSpeed = 5.0f, backstepSpeed = 3.0f;
        [SerializeField] float attackCooldown = 3.0f;
        [SerializeField] float attackDamage = 10.0f;
        [SerializeField] float attackRange = 2.0f;
        [SerializeField] float attackWindup = 0.5f, attackEndlag = 0.5f;
        [SerializeField] SquareHitbox2D attackHitbox;
        [SerializeField] PooledEffect attackEffect;

        [SerializeField] float deadFadeTime = 2.0f;

        Rigidbody2D rb;
        Player player;
        internal TopLayer topLayer;
        readonly int fullHp = Animator.StringToHash("FullHp");
        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            agent.updateUpAxis = false;
            player = GameObject.FindGameObjectWithTag("Player").GetComponent<Player>();
            topLayer = new(this);
            topLayer.OnStateEnter();
        }
        bool dead = false;
        private void Update()
        {
            topLayer.OnStateUpdate();
            CheckDetection();
            if(!dead && sprite.intactPercentage < deadPrecentage)
            {
                dead = true; topLayer.ChangeState("Dead");
            }
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
        float lastAttackTime = -9999.0f;
        internal class TopLayer : TopLayer<BasicEnemy>
        {
            public TopLayer(BasicEnemy origin) : base(origin)
            {
                defaultState = new Idle(origin, this);
                AddState("Idle", defaultState);
                AddState("Detected", new Detected(origin, this));
                AddState("Dead", new Dead(origin, this));
            }
            class Idle : Layer<BasicEnemy>
            {
                public Idle(BasicEnemy origin, Layer<BasicEnemy> parent) : base(origin, parent)
                {
                    defaultState = new Waiting(origin, this);
                    AddState("Waiting", defaultState);
                    AddState("Roaming", new Roaming(origin, this));
                }
                float counter = 0.0f;
                public override void OnStateEnter()
                {
                    base.OnStateEnter();
                    origin.agent.enabled = true;
                    origin.rb.linearVelocity = Vector2.zero;
                    origin.sprite.onHammerBlow += OnHammerBlow;
                }

                private void OnHammerBlow()
                {
                    parentLayer.ChangeState("Detected");
                }

                public override void OnStateUpdate()
                {
                    base.OnStateUpdate();
                    if (origin.CheckDetection()) counter += Time.deltaTime;
                    else counter = 0.0f;

                    if (counter > origin.detectTime) parentLayer.ChangeState("Detected");
                }
                public override void OnStateExit()
                {
                    base.OnStateExit();
                    origin.sprite.onHammerBlow -= OnHammerBlow;
                }
                class Waiting : State<BasicEnemy>
                {
                    public Waiting(BasicEnemy origin, Layer<BasicEnemy> parent) : base(origin, parent) { }
                    float counter = 0.0f;
                    public override void OnStateEnter()
                    {
                        base.OnStateEnter();
                        counter = 0.0f;
                    }
                    public override void OnStateUpdate()
                    {
                        base.OnStateUpdate();
                        counter += Time.deltaTime;
                        if (counter > origin.roamingCooldown) parentLayer.ChangeState("Roaming");
                    }
                }
                class Roaming : State<BasicEnemy>
                {
                    public Roaming(BasicEnemy origin, Layer<BasicEnemy> parent) : base(origin, parent) { }
                    public override void OnStateEnter()
                    {
                        base.OnStateEnter();
                        origin.agent.isStopped = false;
                        origin.agent.stoppingDistance = origin.roamingArriveDist;
                        origin.agent.speed = origin.roamingSpeed;
                        if (!origin.agent.isOnNavMesh)
                        {
                            parentLayer.ChangeState("Waiting"); return;
                        }
                        for (int i = 0; i < 20; i++)
                        {
                            Vector2 candidate = origin.rb.position + UnityEngine.Random.insideUnitCircle * origin.roamingRadius;
                            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, origin.roamingRadius * 0.5f, NavMesh.AllAreas)) continue;
                            origin.agent.SetDestination(candidate);
                            return;
                        }
                        parentLayer.ChangeState("Waiting");
                    }
                    public override void OnStateUpdate()
                    {
                        base.OnStateUpdate();
                        if (origin.agent.remainingDistance <= origin.roamingArriveDist + 0.01f) parentLayer.ChangeState("Waiting");
                    }
                    public override void OnStateExit()
                    {
                        base.OnStateExit();
                        origin.agent.isStopped = true;
                    }
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
                public override void OnStateEnter()
                {
                    base.OnStateEnter();
                    origin.agent.enabled = false;
                }
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
                    public override void OnStateUpdate()
                    {
                        base.OnStateUpdate();
                        
                        if(origin.agent.isOnNavMesh) origin.agent.SetDestination(origin.player.transform.position);
                        Vector2 dir = origin.player.transform.position - origin.transform.position;
                        if(dir.magnitude > origin.chaseDist)
                        {
                            origin.agent.isStopped = false;
                            origin.agent.SetDestination(origin.player.transform.position);
                        }
                        else
                        {
                            origin.agent.isStopped = true;
                            if(dir.magnitude < origin.backstepDist)
                            {
                                origin.agent.Move(dir.normalized * -origin.backstepSpeed * Time.deltaTime);
                            }
                        }
                        origin.transform.rotation = Quaternion.Slerp(origin.transform.rotation, Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg), origin.rotateRate * Time.deltaTime);
                        if (Time.time - origin.lastAttackTime > origin.attackCooldown && Vector2.Distance(origin.transform.position, origin.player.transform.position) < origin.attackRange)
                        {
                            parentLayer.ChangeState("Attacking");
                        }
                    }
                    public override void OnStateExit()
                    {
                        base.OnStateExit();
                        origin.agent.isStopped = true;
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
                        origin.lastAttackTime = Time.time;
                        Vector2 dir = origin.player.transform.position - origin.transform.position;
                        origin.transform.right = dir.normalized;
                        attack = Timing.RunCoroutine(Attack());
                    }
                    IEnumerator<float> Attack()
                    {
                        yield return Timing.WaitForSeconds(origin.attackWindup);
                        origin.attackEffect.Get(origin.attackHitbox.transform.position, origin.attackHitbox.transform.rotation);
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
                float counter = 0.0f;
                public override void OnStateEnter()
                {
                    base.OnStateEnter();
                    counter = 0.0f;
                    origin.sprite.canBreak = false;
                    origin.sprite.collisionEnabled = false;
                    origin.rb.simulated = false;
                }
                public override void OnStateUpdate()
                {
                    base.OnStateUpdate();
                    counter = Mathf.Min(origin.deadFadeTime, counter + Time.deltaTime);
                    Color tmp = origin.spriteRenderer.color;
                    tmp.a = 1.0f - counter / origin.deadFadeTime;
                    origin.spriteRenderer.color = tmp;
                    if(counter >= origin.deadFadeTime)
                    {
                        Destroy(origin.gameObject);
                    }
                }
            }
        }
    }
}