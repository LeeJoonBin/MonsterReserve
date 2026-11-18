using System;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

// 위협(플레이어)의 반대 방향으로 도망갈 지점을 구해 FleePosition에 쓴다.
// 이동은 하지 않는다 (다음 Navigate To Location 노드가 이동한다).
// 충분히 멀거나 갈 수 있는 지점이 없으면 Failure를 돌려줘 다음 가지(대기)로 넘긴다.
[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "Compute Flee Position",
    story: "Compute [FleePosition] away from [Target] for [Self]",
    category: "Action/Navigation",
    id: "df2fb883efba4d76a71111a19c179766")]
public partial class ComputeFleePositionAction : Action
{
    [SerializeReference] public BlackboardVariable<Vector3> FleePosition;
    [SerializeReference] public BlackboardVariable<GameObject> Target;
    [SerializeReference] public BlackboardVariable<GameObject> Self;
    [SerializeReference] public BlackboardVariable<Vector3> LastSeenPosition; // Target이 없을 때 위협 위치
    [SerializeReference] public BlackboardVariable<float> FleeDistance;       // 한 번에 도망가는 거리 (변경되는 값)
    [SerializeReference] public BlackboardVariable<float> SafeDistance;       // 이보다 멀면 도망가지 않는다 (변경되는 값)

    protected override Status OnStart()
    {
        Vector3 selfPosition = Self.Value.transform.position;
        Vector3 threatPosition = Target.Value != null ? Target.Value.transform.position : LastSeenPosition.Value;

        if (Vector3.Distance(selfPosition, threatPosition) >= SafeDistance.Value) return Status.Failure;

        // 위협에서 나를 향하는 방향 (높이는 무시)
        Vector3 awayDirection = selfPosition - threatPosition;
        awayDirection.y = 0f;
        Vector3 candidate = selfPosition + awayDirection.normalized * FleeDistance.Value;

        // 갈 수 없는 곳(벽, 물 등)이면 가까운 NavMesh 위 지점으로 맞춘다
        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, FleeDistance.Value, NavMesh.AllAreas)) return Status.Failure;

        FleePosition.Value = hit.position;
        return Status.Success;
    }
}
