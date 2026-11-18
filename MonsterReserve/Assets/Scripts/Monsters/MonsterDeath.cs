using UnityEngine;
using UnityEngine.AI;
using Unity.Behavior;

public class MonsterDeath : MonoBehaviour
{
    private const string DieTrigger = "Die";

    private SkeletonWarrior _skeletonWarrior;
    private BehaviorGraphAgent _behaviorGraphAgent;
    private NavMeshAgent _navMeshAgent;
    private Animator _animator;

    private void Awake() => CacheComponents();
    private void OnEnable() => _skeletonWarrior.OnDeath += HandleDeath;
    private void OnDisable() => _skeletonWarrior.OnDeath -= HandleDeath;

    private void HandleDeath()
    {
        _behaviorGraphAgent.End();

        _navMeshAgent.ResetPath();
        _navMeshAgent.isStopped = true;

        _animator.SetTrigger(DieTrigger);
    }

    private void CacheComponents()
    {
        _skeletonWarrior = GetComponent<SkeletonWarrior>();
        _behaviorGraphAgent = GetComponent<BehaviorGraphAgent>();
        _navMeshAgent = GetComponent<NavMeshAgent>();
        _animator = GetComponentInChildren<Animator>();
    }
}
