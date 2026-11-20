using UnityEngine;

public class PlayerWallet : MonoBehaviour
{
   public int Money { get; private set; }

   // 의뢰 완료되면 받는 돈 받는 함수
   public void AddMoney(int money)
   {
      Money += money;
      EventBus<MoneyChangedEvent>.Raise(new MoneyChangedEvent(Money));
   }
   // 돈 소비 함수
   public bool TrySpend(int money)
   {
      if (Money < money) return false;
      Money -= money;
      EventBus<MoneyChangedEvent>.Raise(new MoneyChangedEvent(Money));
      return true;
   }
}
