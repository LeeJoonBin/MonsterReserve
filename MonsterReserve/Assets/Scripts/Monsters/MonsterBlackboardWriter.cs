using UnityEngine;
using Unity.Behavior;

public class MonsterBlackboardWriter : MonoBehaviour
{
     private const string ShotPositionKey = "ShotPosition";
     private const string HasShotPositionKey = "HasShotPosition";
     private const string TargetKey = "Target";
     private const string IsTargetInSightKey = "IsTargetInSight";
     private const string LastSeenPositionKey = "LastSeenPosition";
     private const string HasLastSeenPositionKey = "HasLastSeenPosition";
     private const string IsFleeingKey = "IsFleeing";

     [SerializeField] private float _stealth; // 은신 확률
     private BehaviorGraphAgent _behaviorGraphAgent;
     private SkeletonWarrior _skeletonWarrior;
     private MonsterDetector _monsterDetector;

     private void Awake() => CacheComponents();
     private void OnEnable() => BindAction();
     private void OnDisable() => UnBindAction();

     private void Update()
     {
          _behaviorGraphAgent.SetVariableValue(IsTargetInSightKey, _monsterDetector.IsTargetInSight);
          if (_monsterDetector.TargetTransform != null)
               _behaviorGraphAgent.SetVariableValue(TargetKey, _monsterDetector.TargetTransform.gameObject);

          if (_monsterDetector.IsTargetInSight)
          {
               _behaviorGraphAgent.SetVariableValue(LastSeenPositionKey, _monsterDetector.LastSeenPosition);
               _behaviorGraphAgent.SetVariableValue(HasLastSeenPositionKey, true);
          }
     }
     private void BindAction()
     {
          _skeletonWarrior.OnHit += HandleHit;
          ResetBlackboard();
     }

     private void ResetBlackboard()
     {
          _behaviorGraphAgent.SetVariableValue(IsTargetInSightKey, false);
          _behaviorGraphAgent.SetVariableValue(HasLastSeenPositionKey, false);
          _behaviorGraphAgent.SetVariableValue(HasShotPositionKey, false);
          _behaviorGraphAgent.SetVariableValue(IsFleeingKey, false);
     }

     private void UnBindAction()
     {
          _skeletonWarrior.OnHit -= HandleHit;
     }
     private void HandleHit(DamageInfo damageInfo)
     {
          if (_skeletonWarrior.HealthRatio <= 0.3f) _behaviorGraphAgent.SetVariableValue(IsFleeingKey, true);
          if(_monsterDetector.IsTargetInSight) return;
          
          if (damageInfo.Attacker != null && Random.value < Mathf.Max(1f - _stealth, 0.5f))
          {
               _behaviorGraphAgent.SetVariableValue(LastSeenPositionKey, damageInfo.Attacker.position);
               _behaviorGraphAgent.SetVariableValue(HasLastSeenPositionKey, true);
               return;
          }
          _behaviorGraphAgent.SetVariableValue(ShotPositionKey, damageInfo.StartPosition);
          _behaviorGraphAgent.SetVariableValue(HasShotPositionKey, true);
     }
     
     private void CacheComponents()
     {
          _behaviorGraphAgent = GetComponent<BehaviorGraphAgent>();
          _skeletonWarrior = GetComponent<SkeletonWarrior>();
          _monsterDetector = GetComponentInChildren<MonsterDetector>();
     }
}
