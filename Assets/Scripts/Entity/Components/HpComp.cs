using System;
using UnityEngine;

public abstract class HpComp : MonoBehaviour, IDamagable
{
    [SerializeField] protected float m_maxHp = 100.0f;
    [SerializeField] protected float m_hp = 100.0f;
    public float maxHp
    {
        get => m_maxHp;
        protected set {  m_maxHp = value; onHpChange?.Invoke(); }
    }
    public float hp
    {
        get => m_hp;
        protected set { m_hp = value; onHpChange?.Invoke(); }
    }

    public event Action onHpChange, onDeath;
    public event Action<DamageReceivedInfo> onDamageReceive;
    public DamageReceivedInfo GetDamage(DamageInfo damage)
    {
        var tmp = ProcessDamage(damage);
        onDamageReceive?.Invoke(tmp);
        return tmp;
    }
    public bool dead { get; protected set; } = false;
    protected virtual DamageReceivedInfo ProcessDamage(DamageInfo damage)
    {
        if (dead) return new() { damage = damage, ignored = true };
        DamageReceivedInfo info = new() { damage = damage };
        float prevHp = hp;
        hp = Mathf.Max(0, hp - damage.amount);
        info.amountReceived = prevHp - hp;
        if(hp <= 0.01f)
        {
            info.fatal = true;
            dead = true;
            OnDeath();
        }
        return info;
    }
    protected virtual void OnDeath()
    {
        onDeath?.Invoke();
    }
}