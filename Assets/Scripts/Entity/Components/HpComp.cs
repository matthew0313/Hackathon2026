using UnityEngine;

public abstract class HpComp : MonoBehaviour, IDamagable
{
    [field: SerializeField] public float maxHp { get; private set; } = 100.0f;
    public DamageReceivedInfo GetDamage(DamageInfo damage)
    {
        throw new System.NotImplementedException();
    }
}