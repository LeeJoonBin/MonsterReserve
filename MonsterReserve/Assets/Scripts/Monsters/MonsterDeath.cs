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
    private bool _hasDied;

    private void Awake() => CacheComponents();
    private void OnEnable() => Bind();
    private void OnDisable() => _skeletonWarrior.OnDeath -= HandleDeath;

    private void Bind()
    {
        _skeletonWarrior.OnDeath += HandleDeath;
        if (_hasDied) Revive();
    }

    private void Revive()
    {
        _hasDied = false;
        _navMeshAgent.isStopped = false;
        _behaviorGraphAgent.Restart();
    }

    private void HandleDeath()
    {
        _hasDied = true;
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
