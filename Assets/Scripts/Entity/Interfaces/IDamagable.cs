using UnityEngine;

public interface IDamagable
{
    public DamageReceivedInfo GetDamage(DamageInfo damage);
}
public struct DamageInfo
{
    public float amount;
    public object damageSource;
    public Vector2 hitPoint;
}
public struct DamageReceivedInfo
{
    public DamageInfo damage;
    public bool ignored, fatal;
    public float amountReceived;
}