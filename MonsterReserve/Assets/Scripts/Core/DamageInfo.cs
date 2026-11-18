using UnityEngine;

public struct DamageInfo
{
    public float Damage;
    public Vector3 HitPoint;
    public Vector3 StartPosition;

    public DamageInfo(float damage, Vector3 hitPoint, Vector3 startPosition)
    {
        Damage = damage;
        HitPoint = hitPoint;
        StartPosition = startPosition;
    }
}
