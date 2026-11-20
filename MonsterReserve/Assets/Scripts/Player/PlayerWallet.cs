using UnityEngine;

public class PlayerWallet : MonoBehaviour
{
   public int Money { get; set; }

   public void AddMoney(int money)
   {
      Money += money;
      Debug.Log(Money);
   }

   public void SpendMoney(int money)
   {
      if (Money < money) return;
      Money -= money;
      Debug.Log(money);
   }
}
