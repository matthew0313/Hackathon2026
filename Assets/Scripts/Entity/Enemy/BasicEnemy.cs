using UnityEngine;

public class BasicEnemy : Entity
{
    Player player;
    private void Awake()
    {
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<Player>();
    }
    class TopLayer : TopLayer<BasicEnemy>
    {
        public TopLayer(BasicEnemy origin) : base(origin)
        {

        }
        class Idle : State<BasicEnemy>
        {
            public Idle(BasicEnemy origin, Layer<BasicEnemy> parent) : base(origin, parent) { }
        }
        class Detected : Layer<BasicEnemy>
        {
            public Detected(BasicEnemy origin, Layer<BasicEnemy> parent) : base(origin, parent)
            {

            }
        }
    }
}