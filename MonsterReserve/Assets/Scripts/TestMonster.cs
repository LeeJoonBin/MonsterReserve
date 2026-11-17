using UnityEngine;

public class TestMonster : MonoBehaviour, IDamageable
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void TakeDamage(DamageInfo damageInfo)
    {
        Debug.Log(damageInfo.Damage);
    }
}
