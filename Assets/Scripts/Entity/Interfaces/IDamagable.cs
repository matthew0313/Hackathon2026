using UnityEngine;

public interface IDamagable
{
    public DamageReceivedInfo GetDamage(DamageInfo damage);
}
public struct DamageInfo
{
    public float amount;
}
public struct DamageReceivedInfo
{
    public DamageInfo damage;
    public bool ignored;
    public float amountReceived;
}