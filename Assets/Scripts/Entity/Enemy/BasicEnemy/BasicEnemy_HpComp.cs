using MEC;
using System.Collections.Generic;
using UnityEngine;

namespace BasicEnemy
{
    public class BasicEnemy_HpComp : HpComp
    {
        BasicEnemy origin;
        public override void Initialize()
        {
            base.Initialize();
            origin = GetComponent<BasicEnemy>();
        }
        protected override void OnDeath()
        {
            base.OnDeath();
            origin.topLayer.ChangeState("Dead");
        }
    }
}
