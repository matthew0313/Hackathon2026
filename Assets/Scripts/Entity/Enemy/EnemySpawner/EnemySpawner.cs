using UnityEngine;
using System.Collections.Generic;
using BasicEnemy;
public class EnemySpawner : Enemy
{
    [SerializeField] DestructibleSprite sprite;
    [SerializeField] float deadPercentage = 30.0f;
    [SerializeField] float spawnRateMax = 30.0f, spawnRateMin = 10.0f;
    [SerializeField] float distanceMax = 50.0f, distanceMin = 5.0f;
    [SerializeField] Transform spawnPoint;
    [SerializeField] BasicEnemy.BasicEnemy enemyPrefab;
    [SerializeField] int maxEnemies = 10;
    Player player;
    TopLayer topLayer;
    private void Awake()
    {
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<Player>();
        topLayer = new(this);
        topLayer.OnStateEnter();
    }
    bool dead = false;
    private void Update()
    {
        topLayer.OnStateUpdate();
        if(!dead && sprite.intactPercentage < deadPercentage)
        {
            dead = true; topLayer.ChangeState("Dead");
        }
    }
    private void OnDestroy()
    {
        topLayer.OnStateExit();
    }
    readonly List<BasicEnemy.BasicEnemy> enemies = new();
    class TopLayer : TopLayer<EnemySpawner>
    {
        public TopLayer(EnemySpawner origin) : base(origin)
        {
            defaultState = new Idle(origin, this);
            AddState("Idle", defaultState);
            AddState("Dead", new Dead(origin, this));
        }
        class Idle : State<EnemySpawner>
        {
            public Idle(EnemySpawner origin, Layer<EnemySpawner> parent) : base(origin, parent) { }
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
                origin.enemies.RemoveAll(item => item is null);
                if (origin.enemies.Count >= origin.maxEnemies) return;
                float dist = Vector2.Distance(origin.transform.position, origin.player.transform.position);
                if(counter > Mathf.Lerp(origin.spawnRateMin, origin.spawnRateMax, Mathf.InverseLerp(origin.distanceMin, origin.distanceMax, dist)))
                {
                    counter = 0.0f;
                    var tmp = Instantiate(origin.enemyPrefab, origin.spawnPoint.position, Quaternion.identity);
                    origin.enemies.Add(tmp);
                }
            }
        }
        class Dead : State<EnemySpawner>
        {
            public Dead(EnemySpawner origin, Layer<EnemySpawner> parent) : base(origin, parent) { }
        }
    }
}