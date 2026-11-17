using UnityEngine;
using Unity.Behavior;

public class MonsterBlackboardWriter : MonoBehaviour
{
     private const string ShotPositionKey = "ShotPosition";
     private const string HasShotPositionKey = "HasShotPosition";
     
     private BehaviorGraphAgent _behaviorGraphAgent;
     private SkeletonWarrior _skeletonWarrior;

     private void Awake() => CacheComponents();
     private void OnEnable() => _skeletonWarrior.OnHit += HandleHit;
     private void OnDisable() => _skeletonWarrior.OnHit -= HandleHit;

     private void HandleHit(DamageInfo damageInfo)
     {
          _behaviorGraphAgent.SetVariableValue(ShotPositionKey, damageInfo.StartPosition);
          _behaviorGraphAgent.SetVariableValue(HasShotPositionKey, true);
     }
     private void CacheComponents()
     {
          _behaviorGraphAgent = GetComponent<BehaviorGraphAgent>();
          _skeletonWarrior = GetComponent<SkeletonWarrior>();
     }
}
