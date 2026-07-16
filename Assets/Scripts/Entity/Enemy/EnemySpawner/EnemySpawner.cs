using UnityEngine;

public class EnemySpawner : Enemy
{
    [SerializeField] DestructibleSprite sprite;
    [SerializeField] float deadPercentage = 30.0f;
    [SerializeField] float spawnRateMax = 30.0f, spawnRateMin = 10.0f;
    [SerializeField] float distanceMax = 50.0f, distanceMin = 5.0f;
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
        }
        class Dead : State<EnemySpawner>
        {
            public Dead(EnemySpawner origin, Layer<EnemySpawner> parent) : base(origin, parent) { }
        }
    }
}