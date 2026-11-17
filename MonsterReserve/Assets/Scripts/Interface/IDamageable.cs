// 맞을 수 있는 것 (플레이어, 몬스터). 때리는 쪽은 상대가 누구인지 몰라도 이 약속으로 피해를 준다
public interface IDamageable
{
    public void TakeDamage(DamageInfo damageInfo);
}
