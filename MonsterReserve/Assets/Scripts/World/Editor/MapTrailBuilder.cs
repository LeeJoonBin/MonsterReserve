using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 메뉴 7-1 : 거점에서 북쪽 산(데모 이식 지형, 메뉴 5-1)의 봉우리까지 오르는 등산로 두 갈래를 만든다.
// 서쪽 길 : 거점 가장자리 → 강 서쪽을 따라 북쪽 → 호수 서쪽 물가(룬 돌 자리) → 서쪽 봉우리(룬 돌 자리).
// 동쪽 길 : 거점 가장자리 → 북동쪽 여울에서 강을 한 번 건넘 → 호수 동쪽 물가(룬 돌 자리) → 동쪽 봉우리(룬 돌 자리).
// 0) 경유점 : 지형을 읽어 정한다 (규칙은 아래 상수 주석). 호숫가는 물 밖 완만한 곳 중 호수 정서(정동)에 가까운 곳,
//    봉우리는 호수 가운데의 서쪽(동쪽)에서 가장 높은 꼭대기, 정상 경유점은 그 둘레에서 길로 닿을 수 있는 가장 높은 완만한 곳이다.
//    호숫가는 점수 순 후보 여러 곳을 두고, 앞 구간을 되짚지 않고 봉우리까지 길을 낼 수 있는 첫 후보를 쓴다 (막다른 물가를 건너뛴다).
//    정한 경유점은 경로와 함께 TrailDataPath에 저장한다 (GetWaypoints : 룬 돌 배치가 쓴다).
// 1) 경로 : 높이맵 위 A* 탐색. 길 방향 경사가 PlanMaxSlope를 넘는 이동은 하지 않고, 경사가 클수록, 방향을 크게 꺾을수록 비용을 늘린다.
//    옆 비탈 제한보다 가파른 땅, 호숫가 보호 띠, 이미 정한 경로(같은 길의 앞 구간, 다른 길) 둘레는 비용을 늘려 피한다.
//    같은 길의 앞 구간 바로 옆(RetraceBlockDistance 안)은 지나지 않는다 (왔던 길을 되짚어 겹치지 않게).
//    물(호수, 강 둘레)과 거점 비움 반경 안(시작점 제외)은 지나지 않는다. 여울은 강을 직각으로 건너는 곧은 구간이다.
// 2) 길 높이 : 두 길을 함께 맞춘다. 길 방향 경사 제한과 함께, 가까이 지나는 다른 구간(지그재그 다리, 다른 길)과의 높이 차를
//    "사이 땅 폭 × 옆 비탈 제한" 이하로 맞춰, 두 길 바닥 사이를 옆 비탈 제한 안의 비탈로 이을 수 있게 한다. 그 안에서 땅에서 가장 적게 벗어나는 높이를 쓴다.
// 3) 지형 : 길 폭 안은 길 높이로 평평하게, 양옆은 옆 비탈 제한 이하의 비탈로 원래 땅에 잇는다 (여러 길 바닥과 범위 밖 땅을 동시에 만족하는 위, 아래 한계).
//    고치는 범위는 점마다 비탈이 땅과 만나는 곳까지 넓히고 (상한 MaxCarveHalfWidth), 상한 안에서 만나지 못하는 점(땅이 제한보다 가파른 곳)은
//    그 점의 옆 비탈을 범위 끝 땅에 닿을 만큼 가파르게 한다 (로그로 위치를 남긴다). 원래 땅이 제한보다 가파른 곳은 원래 기울기까지 허용해 길에서 먼 땅은 그대로 남는다.
//    호수 물턱과 강(물, 강둑) 둘레는 바꾸지 않는다 (여울은 강바닥을 그대로 건넌다).
// 4) 경로 저장 : 나무, 바위 배치와 지면 칠 메뉴가 깎기 전 지형으로 찾은 같은 경로를 쓰도록 경로 점을 TrailDataPath에 저장한다 (GetTrails).
//
// 지형을 읽어 경로를 찾으므로, 이 메뉴만 다시 실행하면 이전 길 자국 위에서 경로를 다시 찾아 결과가 달라진다.
// 다시 실행할 때는 메뉴 1-1 → 2 → 5-1 → 6 → 7 → 7-1 순서로 실행한다 (같은 지형이면 탐색이 결정적이라 같은 결과가 나온다).
public static class MapTrailBuilder
{
    // 길 폭의 반 (MapLayout.md "임시값" 길 폭 4m)
    public const float HalfWidth = 2f;

    // 룬 돌 자리(경유점) 이름. GetWaypoints가 돌려주는 사전의 키다
    public const string WestLakeWaypointName = "서쪽 호숫가";
    public const string WestSummitWaypointName = "서쪽 봉우리";
    public const string EastLakeWaypointName = "동쪽 호숫가";
    public const string EastSummitWaypointName = "동쪽 봉우리";

    private const string TrailDataPath = "Assets/Settings/World/TrailPaths.json";
    private const string WestTrailName = "서쪽 길";
    private const string EastTrailName = "동쪽 길";
    // 호수 수면은 메뉴 6이 North/Lake 아래에 놓는다
    private const string LakeRegionName = "North";
    private const string LakeParentName = "Lake";

    // 경로 탐색 범위 : 북쪽 이식 창(MapDemoTransplant.GetGameWindowRect) 전체와, 거점 남쪽 이 거리까지 (여울 탐색 범위도 이 안에 든다)
    private const float SearchSouthPadding = 100f;
    // 탐색 칸 간격 (높이맵 칸 수, 약 2.4m). 이동 중간 칸에서도 경사를 재므로 짝수여야 한다
    private const int NodeStep = 2;
    // 탐색 이동 방향 : 3칸 안의 32방향. 방향 사이 각이 최대 약 18도라 가파른 비탈에서도 경사 제한 안의 비스듬한 방향을 고를 수 있다
    private static readonly Vector2Int[] SearchMoves =
    {
        new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(0, -1),
        new Vector2Int(1, 1), new Vector2Int(-1, 1), new Vector2Int(-1, -1), new Vector2Int(1, -1),
        new Vector2Int(2, 1), new Vector2Int(1, 2), new Vector2Int(-1, 2), new Vector2Int(-2, 1),
        new Vector2Int(-2, -1), new Vector2Int(-1, -2), new Vector2Int(1, -2), new Vector2Int(2, -1),
        new Vector2Int(3, 1), new Vector2Int(1, 3), new Vector2Int(-1, 3), new Vector2Int(-3, 1),
        new Vector2Int(-3, -1), new Vector2Int(-1, -3), new Vector2Int(1, -3), new Vector2Int(3, -1),
        new Vector2Int(3, 2), new Vector2Int(2, 3), new Vector2Int(-2, 3), new Vector2Int(-3, 2),
        new Vector2Int(-3, -2), new Vector2Int(-2, -3), new Vector2Int(2, -3), new Vector2Int(3, -2)
    };
    // 이동 방향의 단위 벡터 (꺾는 각도 계산용)
    private static readonly Vector2[] MoveDirections = SearchMoves.Select(move => ((Vector2)move).normalized).ToArray();

    // 길 방향 경사 (사용자 결정 : 최대 25도). 탐색과 길 높이는 PlanMaxSlope로 계획해, 높이맵 격자로 다시 잴 때 25도를 넘지 않게 여유를 둔다 (임시값)
    private const float TrailMaxSlope = 25f;
    private const float PlanMaxSlope = 22f;
    private static readonly float PlanMaxGrade = Mathf.Tan(PlanMaxSlope * Mathf.Deg2Rad);
    // 땅을 바꾸지 않는 곳(호수 물턱, 강 둘레)과 시작, 끝 점은 땅 높이에 고정한다. 이 점들 사이는 PinMaxSlope까지 허용한다 (임시값, 검사값보다 낮게)
    private const float PinMaxSlope = 24f;
    private static readonly float PinMaxGrade = Mathf.Tan(PinMaxSlope * Mathf.Deg2Rad);
    // 이동 비용 = 거리 × (1 + 이 값 × (경사 / 최대 경사)²). 클수록 완만하게 돌아가는 길을 고른다 (임시값)
    private const float GradeCostWeight = 1f;
    // 땅 경사가 (옆 비탈 제한 - SteepMarginSlope)를 넘는 칸은 넘는 1도마다 비용 배율을 SteepCostPerDegree씩 늘린다 (임시값).
    // 그런 비탈을 가로지르면 옆 비탈이 원래 땅과 만나지 못해 고치는 범위가 넓어지거나 옆 비탈이 제한을 넘는다
    private const float SteepMarginSlope = 7f;
    private const float SteepCostPerDegree = 0.5f;
    // 호숫가 보호 띠(물에서 LakeProtectDistance 안) 칸의 비용 배율 (임시값). 그 안의 길은 땅 높이에 고정되어 고르게 다듬을 수 없다
    private const float ProtectedCostFactor = 3f;
    // 방향을 꺾는 비용 (m). 되돌아 꺾을 때(180도) 이 값, 덜 꺾으면 (1 - cos 각도) / 2 비율 (임시값).
    // 같은 경사의 지그재그는 꺾는 횟수와 상관없이 길이가 같아, 꺾는 비용이 없으면 다리가 짧고 촘촘한 지그재그가 나온다
    private const float HairpinCost = 40f;
    // 이미 정한 경로(같은 길의 앞 구간, 다른 길)에서 이 거리 안의 칸은 비용 배율 OverlapCostFactor (임시값, 기본 고치는 범위 × 2).
    // 구간의 시작, 끝 경유점에서 이 거리 안은 앞 구간과 이어지는 곳이라 늘리지 않는다
    private const float OverlapKeepDistance = CarveHalfWidth * 2f;
    private const float OverlapCostFactor = 6f;
    // 이미 정한 경로를 탐색 칸에 찍을 때 건너뛰는 경로 점 수 (경로 점 1m 간격, 탐색 칸 약 2.4m)
    private const int OverlapStampStride = 2;
    // 되짚기 금지 : 구간 탐색은 같은 길의 앞 구간 점에서 RetraceBlockDistance 안의 칸을 지나지 않는다 (임시값).
    // 검사 기준(길 폭 + SelfApproachGap = 6m)에, 경로 다듬기로 점이 옮겨지는 여유를 더한 값이다.
    // 바로 앞 구간의 끝 RetraceKeepArc는 이번 구간이 경유점에서 나가는 곳이라 막지 않는다. RetraceBlockDistance보다 길어야 시작 칸이 막히지 않는다
    private const float RetraceBlockDistance = 10f;
    private const float RetraceKeepArc = 12f;

    // 강 : 탐색은 강 중심선에서 이 거리 안을 지나지 않는다 (여울 제외, 임시값).
    // 지형은 RiverProtectDistance 안(물 6m + 강둑 2m + 여유 2m)을 바꾸지 않는다
    private const float RiverKeepDistance = 30f;
    private const float RiverProtectDistance = 10f;
    // 호수 : 탐색은 수면 메시에서 LakeAvoidDistance 안을 지나지 않는다 (임시값).
    // 지형은 LakeProtectDistance 안(물가 둘레 땅)을 바꾸지 않는다 (임시값)
    private const float LakeAvoidDistance = 6f;
    private const float LakeProtectDistance = 30f;
    // 수면 메시 끝 정점 찾기 : 4방향 이웃 칸 중 하나라도 메시 정점이 아니면 물가(또는 섬 가장자리) 정점이다
    private static readonly Vector2Int[] EdgeNeighbors = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

    // 호숫가 경유점 : 물에서 LakeProtectDistance ~ ShoreMaxDistance 떨어진 칸 중 호수 가운데에서 본 방향이 정서(정동) ± ShoreSectorHalfAngle 안이고
    // 경사가 ShoreFlatSlope 이하인 곳. 점수 = 물에서 거리 + 정서(정동)에서 벗어난 각도 × ShoreAngleCost가 가장 작은 곳 (임시값).
    // 완만한 곳이 없으면 가장 완만한 곳을 쓴다. 물턱 밖이라 룬 돌과 길 바닥을 놓을 수 있다
    private const float ShoreMaxDistance = 60f;
    private const float ShoreSectorHalfAngle = 45f;
    private const float ShoreFlatSlope = 15f;
    private const float ShoreAngleCost = 1f;
    // 호숫가 후보 : 위 순서(점수, 없으면 경사)로 서로 ShoreCandidateSpacing 이상 떨어진 곳을 ShoreCandidateCount곳까지 둔다 (임시값).
    // 1번 후보가 위 규칙으로 고른 곳이다. 그 후보로 길을 낼 수 없으면(되짚지 않고 봉우리로 갈 수 없는 막다른 물가) 다음 후보를 쓴다
    private const float ShoreCandidateSpacing = 40f;
    private const int ShoreCandidateCount = 8;
    // 봉우리 : 이식 창을 PeakEdgeMargin만큼 줄인 범위, 호수 가운데의 서쪽(동쪽) 칸 중 둘레 PeakIsolationRadius 사각형 안에서 가장 높은 칸(꼭대기) 가운데 가장 높은 곳 (임시값)
    private const float PeakEdgeMargin = 40f;
    private const float PeakIsolationRadius = 150f;
    // 정상 경유점 : 꼭대기에서 SummitSearchRadius 안, 길로 닿을 수 있는 칸 중 경사 SummitFlatSlope 이하에서 가장 높은 곳.
    // 없으면 경사와 상관없이 가장 높은 곳 (임시값)
    private const float SummitSearchRadius = 50f;
    private const float SummitFlatSlope = 20f;

    // 여울 : 거점에서 이 거리 범위, 북동쪽(x > 0, z > 0)의 강 중심선 점 중 강을 직각으로 건너는 곧은 선의 최대 경사가 가장 작은 곳 (임시값)
    private const float FordSearchMin = 140f;
    private const float FordSearchMax = 320f;
    // 여울 후보로 보는 강 경로 점 간격 (강 경로 점 간격 2m × 4 = 8m)
    private const int FordCandidateStep = 4;
    // 여울 곧은 구간의 반 길이. 탐색이 강에 다가가지 않는 거리보다 조금 길게 두어 양 끝이 탐색 가능한 칸이 되게 한다
    private const float FordHalfLength = RiverKeepDistance + 2f;
    private const int NoFordLeg = -1;
    // 동쪽 길 경유점 순서 : 거점 가장자리, 여울 거점 쪽 끝, 여울 건너편 끝, 호숫가, 봉우리. 1번 구간이 여울이다
    private const int EastFordLeg = 1;

    // 경로 점 간격과 다듬기 (임시값). 탐색 칸을 이은 꺾은선을 이동 평균으로 다듬어 칸 모양 들쭉날쭉을 없앤다
    private const float PointSpacing = 1f;
    private const float PathSmoothRadius = 4f;
    private const int PathSmoothPasses = 2;
    // 길 높이를 정하기 전에 땅 높이를 길 방향으로 고르게 하는 범위
    private const float ProfileSmoothRadius = 3f;

    // 길 높이의 근접 제약 : 서로 ProximityRange보다 가까운 두 점(다른 길의 점, 또는 같은 길에서 경로 거리가 ProximityMinArc보다 멀고 서로 마주 보는 점)은
    // 높이 차를 (사이 땅 폭 × 옆 비탈 제한) 이하로 맞춘다. 사이 땅 폭 = 거리 - 길 폭 - BedGapMargin(높이맵 대각 칸 크기 이상, 임시값)
    private const float ProximityRange = OverlapKeepDistance;
    private const float ProximityMinArc = 8f;
    // 두 점을 잇는 방향과 각 점의 진행 방향 사이 각이 이 값 이상이면 마주 보는 점(나란한 다리)으로 본다. 작으면 같은 길 바닥의 앞뒤 점이다
    private const float FacingMinAngle = 45f;
    private static readonly float FacingMaxCos = Mathf.Cos(FacingMinAngle * Mathf.Deg2Rad);
    private const float BedGapMargin = 2f;
    private const float NoConstraint = -1f;

    // 고치는 범위 : 기본 CarveHalfWidth. 옆 비탈이 땅과 만나지 못하는 점은 (만나는 거리 + CarveMargin)까지 넓히되 MaxCarveHalfWidth를 넘지 않는다 (임시값, 강과 같은 방식)
    private const float CarveHalfWidth = 25f;
    private const float MaxCarveHalfWidth = 60f;
    // 범위 끝 단차를 재는 띠(EdgeCheckWidth)보다 넓어야 띠 안에 고친 칸이 없다
    private const float CarveMargin = 5f;
    private const float WidthScanStep = 1f;
    // 굽은 곳에서 점 사이가 덜 고쳐지지 않도록, 앞뒤 이 거리 안 점 중 가장 넓은 범위와 가장 가파른 옆 비탈을 쓴다
    private const float WidthSmoothDistance = 10f;
    // 땅이 옆 비탈 범위 밖이라고 볼 최소 차이 (부동소수 오차는 무시)
    private const float SectionTolerance = 0.05f;
    private static readonly float[] Sides = { -1f, 1f };
    // 옆 비탈 제한. 기슭은 사용자 결정 기슭 최대 경사 35도, 그 위(정상부 바위 지대 포함)는 UpperSideMaxSlope (임시값).
    // 기슭 높이 상한은 MapDemoTransplant, MapNorthScatter의 기슭 높이 상한(160m)과 같은 값이다
    private const float FoothillMaxHeight = 160f;
    private const float FoothillLimitMargin = 10f;
    private const float FoothillSideMaxSlope = 35f;
    private const float UpperSideMaxSlope = 50f;
    // 이웃 칸끼리 높이 차를 제한해도 높이맵 경사(가운데 차분)는 대각선 방향 때문에 최대 이 배율만큼 커진다 (MapNorthShaper와 같은 계산)
    private static readonly float StencilSlopeFactor = Mathf.Sqrt(4f - 2f * Mathf.Sqrt(2f));
    // 땅이 제한보다 가파른 점 : 범위 끝 띠(MaxCarveHalfWidth - CarveMargin ~ MaxCarveHalfWidth)의 땅에 닿는 기울기 × SteepCutSlack을 옆 비탈로 쓴다.
    // MaxSteepCutSlope를 넘으면 그 값에서 자르고 로그로 센다 (그 점은 범위 끝에 단차가 남을 수 있다, 임시값)
    private const float SteepCutSlack = 1.1f;
    private const float MaxSteepCutSlope = 70f;
    private static readonly float MaxSteepCutTan = Mathf.Tan(MaxSteepCutSlope * Mathf.Deg2Rad) / StencilSlopeFactor;
    // 옆 비탈 계산 반복 : 순방향 + 역방향 패스 한 번에 바뀐 높이가 이 값 이하면 끝낸다
    private const float RelaxTolerance = 0.0001f;
    private const int MaxRelaxPasses = 200;
    // 순방향 패스에서 이미 지나온 이웃 (x, z). 역방향 패스는 부호를 뒤집어 반대쪽 이웃을 본다
    private static readonly Vector2Int[] RelaxNeighbors =
    {
        new Vector2Int(-1, 0), new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1)
    };

    // 검사 : 길 방향 경사는 앞뒤 이 거리의 높이 차로 잰다. 고치는 범위 끝 단차는 끝에서 이 폭 안을 본다
    private const float GradeCheckHalfSpan = 2f;
    private const float EdgeCheckWidth = 3f;
    private const float SlopeCheckTolerance = 0.01f;
    private const float ChangeThreshold = 0.01f;
    // 길을 따라 SelfApproachArc보다 멀리 떨어진 두 점이 (길 폭 + SelfApproachGap)보다 가까우면 지그재그가 겹친 곳으로 센다
    private const float SelfApproachArc = 20f;
    private const float SelfApproachGap = 2f;
    // 두 길 사이 거리는 두 길이 함께 시작하는 거점 둘레를 빼고도 잰다
    private const float BaseAreaRadius = MapTreeScatter.BaseClearRadius + OverlapKeepDistance;
    // 로그 대표 위치 : 서로 RepresentativeSpacing보다 떨어진 곳 중 값이 큰 RepresentativeCount곳
    private const int RepresentativeCount = 5;
    private const float RepresentativeSpacing = 50f;

    // 탐색, 지형 고치기가 함께 쓰는 높이맵 범위
    private class TerrainGrid
    {
        public RectInt Range;
        // [0, 0] 꼭짓점의 월드 XZ
        public Vector2 Origin;
        public float Spacing;
        // 읽은 값 그대로. 바뀌지 않은 칸은 이 값을 그대로 다시 써서 오차가 생기지 않게 한다
        public float[,] Normalized;
        // 월드 높이 [z, x]
        public float[,] Original;
        public float[,] Heights;
        public DistanceField RiverDistances;
        public DistanceField LakeDistances;
    }

    // 칸마다 물까지의 거리. Cap보다 먼 칸은 Cap으로 남는다
    private class DistanceField
    {
        public float[,] Values;
        public float Cap;
    }

    private class SearchGrid
    {
        public int CountX;
        public int CountZ;
        public bool[] IsBlocked;
        public float[] CostFactors;
        // 이미 정한 경로에서 OverlapKeepDistance 안인 칸
        public bool[] IsNearPlanned;
    }

    private class PlanContext
    {
        public TerrainGrid Grid;
        public SearchGrid Search;
    }

    private class SearchState
    {
        public float[] Costs;
        public int[] Previous;
        // 지금까지 가장 싼 경로가 이 칸에 들어온 이동 번호 (시작 칸은 -1). 꺾는 비용을 잴 때 쓴다
        public int[] IncomingMoves;
        public bool[] IsClosed;
        public NodeHeap Open = new NodeHeap();
        public int Goal;
        public Vector2 GoalPosition;
        // 구간의 시작, 끝 경유점 (이미 정한 경로 둘레 비용을 늘리지 않는 곳)
        public Vector2 LegStart;
        public Vector2 LegEnd;
        // 같은 길의 앞 구간 바로 옆이라 지나지 않는 칸 (CreateRetraceBlock)
        public bool[] IsRetraceBlocked;
    }

    // 경유점을 고를 때 함께 쓰는 값
    private class WaypointSearch
    {
        public PlanContext Context;
        // 거점 둘레와 여울 건너편에서 길 경사 제한 안으로 닿을 수 있는 탐색 칸
        public bool[] IsReachable;
        // 수면 메시 정점의 평균 (호수 물의 가운데). 서쪽, 동쪽을 가르는 기준이다
        public Vector2 LakeCenter;
        // 탐색 칸마다 둘레 PeakIsolationRadius 사각형 안의 최고 높이
        public float[] AreaMax;
    }

    private struct Ford
    {
        public Vector2 Center;
        public Vector2 BaseSideBank;
        public Vector2 FarSideBank;
        public float MaxSlope;
    }

    private class Trail
    {
        public string Name;
        public List<Vector2> Waypoints;
        // 이 길이 지나는 룬 돌 자리 (이름 → 월드 XZ)
        public Dictionary<string, Vector2> RuneWaypoints;
        public List<Vector2> Points;
        // 시작점에서 각 점까지의 경로 길이
        public float[] Distances;
        // 각 점의 길 높이 (월드)
        public float[] Profile;
        public bool[] IsPinned;
        public int PinConflictCount;
        // 여울 구간 점 번호 범위 (없으면 -1)
        public int FordStart = -1;
        public int FordEnd = -1;
        // 점마다 고치는 범위 (중심선에서 거리)
        public float[] CarveWidths;
        // 땅이 제한보다 가파른 점에 쓰는 옆 비탈 (이웃 칸 사이 허용 기울기 tan, 그런 점이 아니면 0)
        public float[] SteepLimits;
        public Representatives SteepPoints;
        public int CappedSteepCount;
    }

    // 두 길의 점을 이어 붙인 길 높이 제약 그래프. 경로 앞뒤 점은 길 방향 경사로, 근접 점은 Allowances로 높이 차를 제한한다
    private class ProfileGraph
    {
        public int Count;
        // 점마다 속한 길의 시작에서 잰 경로 거리
        public float[] Distances;
        // 앞 점과 같은 길로 이어지는지 (길의 첫 점이면 false)
        public bool[] IsLinkedToPrevious;
        public List<int>[] Neighbors;
        public List<float>[] Allowances;
        public int PairCount;
    }

    private struct ProfilePoint
    {
        public Vector2 Position;
        public Vector2 Forward;
        public float Ground;
        public int TrailIndex;
    }

    // 여러 출발점 Dijkstra 진행 상태
    private class ProfileSolve
    {
        public float[] Values;
        public bool[] IsDone;
        public NodeHeap Open = new NodeHeap();
    }

    // 길 중심선 한 점에서 한쪽 옆으로 땅을 읽는 단면
    private struct SectionScan
    {
        public Vector2 Point;
        public Vector2 Side;
        public float Bed;
    }

    // 지형을 고칠 수 있는 범위(점마다 길 중심선에서 고치는 범위 안)의 칸 정보
    private class CarveField
    {
        public float[,] TrailDistances;
        // 가장 가까운 중심선 위치의 길 높이, 고치는 범위, 가파른 곳 옆 비탈
        public float[,] BedHeights;
        public float[,] CarveWidths;
        public float[,] SteepLimits;
        public bool[,] IsInZone;
        public bool[,] IsProtected;
        public bool[,] IsBed;
        // 이웃 칸과의 허용 기울기(tan)
        public float[,] Limits;
        // 길 바닥, 고정 칸, 범위 밖 땅에서 옆 비탈 제한으로 오르내리는 위, 아래 한계
        public float[,] Ceiling;
        public float[,] Floor;
        // 고치는 범위 칸을 모두 담는 꼭짓점 범위 (xMax, yMax 포함)
        public RectInt Bounds;
    }

    private struct RelaxJob
    {
        public CarveField Field;
        public float[,] Values;
        public float[,] Original;
        public float Spacing;
        public bool IsCeiling;
    }

    private class CarveStats
    {
        public float MaxCut;
        public Vector2 MaxCutPosition;
        public float MaxFill;
        public Vector2 MaxFillPosition;
        public float MaxEdgeChange;
        public Vector2 MaxEdgeChangePosition;
        public int ChangedCount;
        // 옆 비탈 제한을 지킬 수 없는 칸 (값 = 위, 아래 한계가 엇갈린 높이)
        public Representatives BedConflicts = new Representatives();
        public Representatives SideConflicts = new Representatives();
    }

    // 로그용 대표 위치 : 서로 RepresentativeSpacing보다 떨어진 곳 중 값이 큰 곳을 RepresentativeCount개까지 모은다. Count는 전체 개수다
    private class Representatives
    {
        public int Count;
        private readonly List<Vector2> _positions = new List<Vector2>();
        private readonly List<float> _values = new List<float>();

        public void Add(Vector2 position, float value)
        {
            Count++;
            int near = _positions.FindIndex(existing => Vector2.Distance(existing, position) < RepresentativeSpacing);
            if (near >= 0)
            {
                if (value > _values[near]) Replace(near, position, value);
                return;
            }
            if (_positions.Count < RepresentativeCount)
            {
                _positions.Add(position);
                _values.Add(value);
                return;
            }

            int smallest = _values.IndexOf(_values.Min());
            if (value > _values[smallest]) Replace(smallest, position, value);
        }

        public string ToText(string unit)
        {
            if (_positions.Count == 0) return "없음";
            return string.Join(", ", Enumerable.Range(0, _positions.Count)
                .OrderByDescending(i => _values[i])
                .Select(i => $"({_positions[i].x:F0}, {_positions[i].y:F0}) {_values[i]:F1}{unit}"));
        }

        private void Replace(int index, Vector2 position, float value)
        {
            _positions[index] = position;
            _values[index] = value;
        }
    }

    // 경로 저장 형식 (JsonUtility)
    [System.Serializable]
    private class TrailData
    {
        public List<TrailRecord> Trails = new List<TrailRecord>();
        public List<WaypointRecord> Waypoints = new List<WaypointRecord>();
    }

    [System.Serializable]
    private class TrailRecord
    {
        public string Name;
        public List<Vector2> Points;
    }

    [System.Serializable]
    private class WaypointRecord
    {
        public string Name;
        public Vector2 Position;
    }

    // 열린 목록. 우선순위가 가장 작은 번호를 먼저 꺼내는 이진 힙 (A*, 길 높이 Dijkstra가 함께 쓴다)
    private class NodeHeap
    {
        private readonly List<float> _priorities = new List<float>();
        private readonly List<int> _nodes = new List<int>();

        public int Count => _nodes.Count;

        public void Push(int node, float priority)
        {
            _priorities.Add(priority);
            _nodes.Add(node);
            int index = _nodes.Count - 1;
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (_priorities[parent] <= _priorities[index]) break;

                Swap(index, parent);
                index = parent;
            }
        }

        public int Pop()
        {
            int top = _nodes[0];
            int last = _nodes.Count - 1;
            Swap(0, last);
            _priorities.RemoveAt(last);
            _nodes.RemoveAt(last);

            int index = 0;
            while (true)
            {
                int smallest = index;
                int left = index * 2 + 1;
                int right = left + 1;
                if (left < _nodes.Count && _priorities[left] < _priorities[smallest]) smallest = left;
                if (right < _nodes.Count && _priorities[right] < _priorities[smallest]) smallest = right;
                if (smallest == index) return top;

                Swap(index, smallest);
                index = smallest;
            }
        }

        private void Swap(int a, int b)
        {
            (_priorities[a], _priorities[b]) = (_priorities[b], _priorities[a]);
            (_nodes[a], _nodes[b]) = (_nodes[b], _nodes[a]);
        }
    }

    // 나무, 바위 배치와 지면 칠이 길을 피할 때 쓴다. 메뉴 7-1이 저장한 경로를 읽는다 (없으면 null).
    // 경로는 한 번 읽어 두고 IsNearTrail에 넘긴다
    public static List<List<Vector2>> GetTrails()
    {
        TrailData data = LoadTrailData();
        return data?.Trails.Select(trail => trail.Points).ToList();
    }

    // 룬 돌 배치가 쓴다. 메뉴 7-1이 지형을 읽어 정한 경유점 (이름 → 월드 XZ, 이름은 *WaypointName 상수). 저장 파일이 없으면 null.
    // 동쪽 봉우리를 찾지 못한 지형에서는 동쪽 봉우리가 서쪽 봉우리와 같은 자리다 (로그로 알린다)
    public static Dictionary<string, Vector2> GetWaypoints()
    {
        TrailData data = LoadTrailData();
        return data?.Waypoints.ToDictionary(record => record.Name, record => record.Position);
    }

    // 길 중심선에서 수평 거리가 (길 폭의 반 + margin) 안이면 true
    public static bool IsNearTrail(List<List<Vector2>> trails, Vector3 world, float margin)
    {
        var position = new Vector2(world.x, world.z);
        return trails.Any(points => GetDistanceToPath(points, position) <= HalfWidth + margin);
    }

    [MenuItem("Tools/Map/7-1. 등산로 만들기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        List<Vector2> lakePoints = ReadLakePoints(terrain);
        if (lakePoints == null) return;

        TerrainGrid grid = ReadGrid(terrain);
        List<Vector2> river = MapRiverBuilder.GetPath(terrain);
        MeasureWaterDistances(grid, river, lakePoints);
        if (!TryFindFord(grid, river, out Ford ford))
        {
            Debug.LogError("[MapTrailBuilder] 여울 자리를 찾지 못했습니다. FordSearchMin, FordSearchMax를 확인하세요. 지형을 바꾸지 않았습니다.");
            return;
        }

        var context = new PlanContext { Grid = grid, Search = CreateSearchGrid(grid) };
        if (!TrySelectWaypoints(context, ford, lakePoints, out Dictionary<string, Vector2> waypoints, out List<Vector2> westShores, out List<Vector2> eastShores)) return;

        Trail[] trails = PlanTrails(context, ford, waypoints, westShores, eastShores);
        if (trails == null) return;

        FitProfiles(grid, trails);
        foreach (Trail trail in trails)
            CalculateCarveWidths(grid, trail);
        CarveField field = CreateCarveField(grid, trails);
        CarveStats stats = ApplyCarve(grid, field);
        WriteHeights(terrain, grid);
        SaveTrails(trails, waypoints);

        // 검수(경사, 여울, 룬 돌 자리, 길 간격, 지형 변경, 물 침범)에 쓰는 값이라 로그를 남긴다
        LogFord(ford);
        foreach (Trail trail in trails)
            LogTrail(grid, trail);
        LogTrailSpacing(trails[0], trails[1]);
        LogCarve(grid, field, stats);
    }

    // ---------- 준비 ----------

    // 메뉴 6이 놓은 호수 수면 메시 정점 (월드 XZ). 수면 메시 끝은 물가보다 한 칸 밖이다
    private static List<Vector2> ReadLakePoints(Terrain terrain)
    {
        GameObject region = terrain.gameObject.scene.GetRootGameObjects().FirstOrDefault(root => root.name == LakeRegionName);
        Transform lake = region != null ? region.transform.Find(LakeParentName) : null;
        MeshFilter filter = lake != null ? lake.GetComponentInChildren<MeshFilter>() : null;
        if (filter == null)
        {
            Debug.LogError("North/Lake 아래에 호수 수면이 없습니다. 먼저 'Tools/Map/6. 호수 만들기'를 실행하세요.");
            return null;
        }

        return filter.sharedMesh.vertices
            .Select(vertex => filter.transform.TransformPoint(vertex))
            .Select(point => new Vector2(point.x, point.z))
            .ToList();
    }

    private static TerrainGrid ReadGrid(Terrain terrain)
    {
        TerrainData data = terrain.terrainData;
        int divisions = data.heightmapResolution - 1;
        Vector3 position = terrain.transform.position;
        Rect worldRect = GetSearchRect(new Rect(position.x, position.z, data.size.x, data.size.z));
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, worldRect, divisions);
        float spacing = data.size.x / divisions;

        // 높이맵은 꼭짓점 기준이라 끝 줄까지 포함한다
        int depth = range.height + 1;
        int width = range.width + 1;
        float[,] normalized = data.GetHeights(range.x, range.y, width, depth);
        var original = new float[depth, width];
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
                original[z, x] = normalized[z, x] * data.size.y + position.y;
        }

        return new TerrainGrid
        {
            Range = range,
            Origin = new Vector2(position.x + range.x * spacing, position.z + range.y * spacing),
            Spacing = spacing,
            Normalized = normalized,
            Original = original,
            Heights = (float[,])original.Clone(),
            RiverDistances = new DistanceField { Values = CreateFilled(depth, width, RiverKeepDistance), Cap = RiverKeepDistance },
            LakeDistances = new DistanceField { Values = CreateFilled(depth, width, ShoreMaxDistance), Cap = ShoreMaxDistance }
        };
    }

    // 경유점을 지형에서 고르므로 범위는 고정이다 : 이식 창 전체와 거점 남쪽 SearchSouthPadding까지를 Terrain 안으로 자른다
    private static Rect GetSearchRect(Rect terrainRect)
    {
        Rect window = MapDemoTransplant.GetGameWindowRect();
        return Rect.MinMaxRect(
            Mathf.Max(window.xMin, terrainRect.xMin), Mathf.Max(Mathf.Min(window.yMin, -SearchSouthPadding), terrainRect.yMin),
            Mathf.Min(window.xMax, terrainRect.xMax), Mathf.Min(window.yMax, terrainRect.yMax));
    }

    // 수면 메시 정점은 높이맵 꼭짓점과 같은 자리라, 정점 칸은 물(거리 0)로 두고 물 밖까지의 거리는 메시 끝 정점에서만 잰다
    // (호숫가 경유점을 찾느라 거리 상한이 넓어져 모든 정점을 찍으면 오래 걸린다)
    private static void MeasureWaterDistances(TerrainGrid grid, List<Vector2> river, List<Vector2> lakePoints)
    {
        for (int i = 0; i < river.Count - 1; i++)
            StampDistance(grid, grid.RiverDistances, river[i], river[i + 1]);

        var lakeCells = new HashSet<Vector2Int>(lakePoints.Select(point => GetNearestCell(grid, point)));
        foreach (Vector2Int cell in lakeCells)
            grid.LakeDistances.Values[cell.y, cell.x] = 0f;
        foreach (Vector2 point in lakePoints)
        {
            if (IsLakeEdge(lakeCells, GetNearestCell(grid, point))) StampDistance(grid, grid.LakeDistances, point, point);
        }
    }

    private static bool IsLakeEdge(HashSet<Vector2Int> lakeCells, Vector2Int cell)
    {
        return EdgeNeighbors.Any(offset => !lakeCells.Contains(cell + offset));
    }

    // 선분 from-to에서 각 칸까지의 거리로 field를 줄인다. Cap보다 먼 칸은 보지 않는다
    private static void StampDistance(TerrainGrid grid, DistanceField field, Vector2 from, Vector2 to)
    {
        RectInt cells = GetCellRange(grid, from, to, field.Cap);
        for (int z = cells.yMin; z <= cells.yMax; z++)
        {
            for (int x = cells.xMin; x <= cells.xMax; x++)
            {
                float distance = GetSegmentDistance(from, to, GetCellWorld(grid, x, z));
                if (distance < field.Values[z, x]) field.Values[z, x] = distance;
            }
        }
    }

    // ---------- 여울 ----------

    // 후보마다 강을 직각으로 건너는 곧은 선(거점 쪽 끝 → 건너편 끝)의 최대 경사를 재고, 가장 작은 곳을 고른다.
    // 같으면 먼저 본 곳(상류)을 쓴다
    private static bool TryFindFord(TerrainGrid grid, List<Vector2> river, out Ford ford)
    {
        ford = new Ford { MaxSlope = float.MaxValue };
        for (int i = 1; i < river.Count - 1; i += FordCandidateStep)
        {
            Vector2 center = river[i];
            float baseDistance = center.magnitude;
            if (center.x <= 0f || center.y <= 0f || baseDistance < FordSearchMin || baseDistance > FordSearchMax) continue;

            Vector2 normal = GetRight(river, i);
            // 거점 쪽을 향하게 뒤집는다
            if (Vector2.Dot(normal, -center) < 0f) normal = -normal;
            Vector2 baseSide = center + normal * FordHalfLength;
            Vector2 farSide = center - normal * FordHalfLength;
            float slope = GetLineMaxSlope(grid, baseSide, farSide);
            if (slope >= ford.MaxSlope) continue;

            ford = new Ford { Center = center, BaseSideBank = baseSide, FarSideBank = farSide, MaxSlope = slope };
        }
        return ford.MaxSlope < float.MaxValue;
    }

    // 곧은 선을 PointSpacing 간격으로 나눠 이웃 점 사이 경사의 최대값 (도)
    private static float GetLineMaxSlope(TerrainGrid grid, Vector2 from, Vector2 to)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / PointSpacing));
        float run = Vector2.Distance(from, to) / steps;
        float previous = SampleHeight(grid, grid.Original, from);
        float maxSlope = 0f;
        for (int i = 1; i <= steps; i++)
        {
            float height = SampleHeight(grid, grid.Original, Vector2.Lerp(from, to, (float)i / steps));
            maxSlope = Mathf.Max(maxSlope, Mathf.Atan(Mathf.Abs(height - previous) / run) * Mathf.Rad2Deg);
            previous = height;
        }
        return maxSlope;
    }

    // ---------- 경유점 ----------

    // 호숫가 후보 둘(서쪽, 동쪽), 봉우리 둘을 지형에서 고른다. 서쪽과 동쪽은 호수 물의 가운데 x로 가른다.
    // 호숫가는 후보 목록만 돌려주고, 실제 자리는 PlanTrails가 길을 낼 수 있는 후보로 정해 waypoints에 넣는다.
    // 두 길은 서로 다른 봉우리로 간다 : 두 길의 마지막 오르막이 같은 비탈을 나눠 쓰지 않게 하고, 높은 전망 지점을 둘 만든다.
    // 동쪽 꼭대기가 없거나 닿을 수 없으면 동쪽 길도 서쪽 봉우리로 간다 (경고)
    private static bool TrySelectWaypoints(PlanContext context, Ford ford, List<Vector2> lakePoints, out Dictionary<string, Vector2> waypoints,
        out List<Vector2> westShores, out List<Vector2> eastShores)
    {
        waypoints = new Dictionary<string, Vector2>();
        eastShores = null;
        var search = new WaypointSearch
        {
            Context = context,
            IsReachable = FindReachable(context, ford),
            LakeCenter = lakePoints.Aggregate(Vector2.zero, (sum, point) => sum + point) / lakePoints.Count,
            AreaMax = GetAreaMax(context, PeakIsolationRadius)
        };
        Debug.Log($"[MapTrailBuilder] 경유점 기준 : 호수 물 가운데(수면 메시 정점 평균) ({search.LakeCenter.x:F0}, {search.LakeCenter.y:F0}), " +
                  $"길 경사 {PlanMaxSlope}도 이하로 닿을 수 있는 탐색 칸 {search.IsReachable.Count(isReachable => isReachable)}개 / {search.IsReachable.Length}개");

        if (!TryFindShores(search, Vector2.left, WestLakeWaypointName, out westShores)) return false;
        if (!TryFindShores(search, Vector2.right, EastLakeWaypointName, out eastShores)) return false;
        if (!TryFindPeak(search, false, out int westPeak) || !TryFindSummit(search, westPeak, WestSummitWaypointName, out Vector2 westSummit))
        {
            Debug.LogError("[MapTrailBuilder] 서쪽 봉우리 또는 길로 닿을 수 있는 정상 자리를 찾지 못했습니다. PeakEdgeMargin, SummitSearchRadius를 확인하세요. 지형을 바꾸지 않았습니다.");
            return false;
        }

        Vector2 eastSummit = westSummit;
        if (TryFindPeak(search, true, out int eastPeak) && TryFindSummit(search, eastPeak, EastSummitWaypointName, out Vector2 eastSpot)) eastSummit = eastSpot;
        else Debug.LogWarning("[MapTrailBuilder] 동쪽 봉우리(또는 길로 닿을 수 있는 정상 자리)를 찾지 못해 동쪽 길도 서쪽 봉우리로 갑니다.");

        waypoints[WestSummitWaypointName] = westSummit;
        waypoints[EastSummitWaypointName] = eastSummit;
        return true;
    }

    // 거점 비움 반경 바로 밖과 여울 건너편 끝에서 시작해, 길 경사 제한 안의 이동으로 닿는 탐색 칸 (A*와 같은 이동 규칙)
    private static bool[] FindReachable(PlanContext context, Ford ford)
    {
        SearchGrid search = context.Search;
        var isReached = new bool[search.CountX * search.CountZ];
        var queue = new Queue<int>();
        float seedBand = GetNodeSpacing(context) * 2f;
        for (int node = 0; node < isReached.Length; node++)
        {
            Vector2 world = GetNodeWorld(context, node);
            bool isSeed = world.magnitude < MapTreeScatter.BaseClearRadius + seedBand || Vector2.Distance(world, ford.FarSideBank) < seedBand;
            if (!isSeed || search.IsBlocked[node]) continue;

            isReached[node] = true;
            queue.Enqueue(node);
        }

        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            var current = new Vector2Int(node % search.CountX, node / search.CountX);
            foreach (Vector2Int move in SearchMoves)
            {
                Vector2Int nextCell = current + move;
                if (!IsInsideNodes(search, nextCell)) continue;

                int next = nextCell.y * search.CountX + nextCell.x;
                if (isReached[next] || search.IsBlocked[next] || float.IsPositiveInfinity(GetMoveCost(context.Grid, current, move))) continue;

                isReached[next] = true;
                queue.Enqueue(next);
            }
        }
        return isReached;
    }

    // 규칙은 ShoreMaxDistance, ShoreCandidateCount 주석. direction은 호수 가운데에서 본 정서(Vector2.left) 또는 정동(Vector2.right).
    // 순서가 같으면 먼저 본 칸이 앞이다 (OrderBy는 같은 값의 순서를 바꾸지 않는다)
    private static bool TryFindShores(WaypointSearch search, Vector2 direction, string name, out List<Vector2> shores)
    {
        PlanContext context = search.Context;
        var flatScores = new List<KeyValuePair<int, float>>();
        var slopes = new List<KeyValuePair<int, float>>();
        for (int node = 0; node < search.IsReachable.Length; node++)
        {
            if (!search.IsReachable[node]) continue;

            float lakeDistance = GetNodeLakeDistance(context, node);
            if (lakeDistance < LakeProtectDistance || lakeDistance >= ShoreMaxDistance) continue;

            float angle = Vector2.Angle(GetNodeWorld(context, node) - search.LakeCenter, direction);
            if (angle > ShoreSectorHalfAngle) continue;

            float slope = GetNodeSlope(context, node);
            if (slope <= ShoreFlatSlope) flatScores.Add(new KeyValuePair<int, float>(node, lakeDistance + angle * ShoreAngleCost));
            slopes.Add(new KeyValuePair<int, float>(node, slope));
        }

        bool isFlat = flatScores.Count > 0;
        IEnumerable<int> ordered = (isFlat ? flatScores : slopes).OrderBy(pair => pair.Value).Select(pair => pair.Key);
        var chosen = new List<int>();
        foreach (int node in ordered)
        {
            if (chosen.Count >= ShoreCandidateCount) break;

            Vector2 world = GetNodeWorld(context, node);
            if (chosen.Any(other => Vector2.Distance(GetNodeWorld(context, other), world) < ShoreCandidateSpacing)) continue;
            chosen.Add(node);
        }

        shores = chosen.Select(node => GetNodeWorld(context, node)).ToList();
        if (chosen.Count == 0)
        {
            Debug.LogError($"[MapTrailBuilder] {name} 자리를 찾지 못했습니다 (물에서 {LakeProtectDistance} ~ {ShoreMaxDistance}m, 방향 ± {ShoreSectorHalfAngle}도, 길로 닿을 수 있는 곳). 지형을 바꾸지 않았습니다.");
            return false;
        }

        for (int i = 0; i < chosen.Count; i++)
        {
            Vector2 shore = shores[i];
            Debug.Log($"[MapTrailBuilder] {name} {i + 1}번 후보 ({shore.x:F0}, {shore.y:F0}), 높이 {GetNodeHeight(context, chosen[i]):F1}m, 물에서 {GetNodeLakeDistance(context, chosen[i]):F1}m, " +
                      $"호수 가운데에서 본 방향이 정{(direction.x < 0f ? "서" : "동")}에서 {Vector2.Angle(shore - search.LakeCenter, direction):F0}도, 경사 {GetNodeSlope(context, chosen[i]):F1}도 " +
                      (isFlat ? $"(경사 {ShoreFlatSlope}도 이하, 점수 순)" : $"(경사 {ShoreFlatSlope}도 이하인 곳이 없어 완만한 순)"));
        }
        return true;
    }

    // 규칙은 PeakIsolationRadius 주석. isEast면 호수 가운데 x 이상, 아니면 미만에서 찾는다
    private static bool TryFindPeak(WaypointSearch search, bool isEast, out int peak)
    {
        PlanContext context = search.Context;
        Rect window = MapDemoTransplant.GetGameWindowRect();
        Rect area = Rect.MinMaxRect(window.xMin + PeakEdgeMargin, window.yMin + PeakEdgeMargin, window.xMax - PeakEdgeMargin, window.yMax - PeakEdgeMargin);
        peak = -1;
        float best = float.MinValue;
        for (int node = 0; node < search.AreaMax.Length; node++)
        {
            Vector2 world = GetNodeWorld(context, node);
            if (!area.Contains(world) || (world.x >= search.LakeCenter.x) != isEast) continue;

            float height = GetNodeHeight(context, node);
            if (height < search.AreaMax[node] || height <= best) continue;

            best = height;
            peak = node;
        }
        return peak >= 0;
    }

    // 규칙은 SummitSearchRadius 주석
    private static bool TryFindSummit(WaypointSearch search, int peak, string name, out Vector2 summit)
    {
        PlanContext context = search.Context;
        int reach = Mathf.CeilToInt(SummitSearchRadius / GetNodeSpacing(context));
        var center = new Vector2Int(peak % context.Search.CountX, peak / context.Search.CountX);
        Vector2 peakWorld = GetNodeWorld(context, peak);
        int flatBest = -1;
        int highest = -1;
        for (int dz = -reach; dz <= reach; dz++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                Vector2Int cell = center + new Vector2Int(dx, dz);
                if (!IsInsideNodes(context.Search, cell)) continue;

                int node = cell.y * context.Search.CountX + cell.x;
                if (!search.IsReachable[node] || Vector2.Distance(GetNodeWorld(context, node), peakWorld) > SummitSearchRadius) continue;

                float height = GetNodeHeight(context, node);
                if (highest < 0 || height > GetNodeHeight(context, highest)) highest = node;
                if (GetNodeSlope(context, node) > SummitFlatSlope) continue;
                if (flatBest < 0 || height > GetNodeHeight(context, flatBest)) flatBest = node;
            }
        }

        int chosen = flatBest >= 0 ? flatBest : highest;
        summit = chosen >= 0 ? GetNodeWorld(context, chosen) : Vector2.zero;
        Debug.Log($"[MapTrailBuilder] {name} 꼭대기 ({peakWorld.x:F0}, {peakWorld.y:F0}) {GetNodeHeight(context, peak):F1}m (둘레 {PeakIsolationRadius}m 사각형 안 최고, 창 끝 {PeakEdgeMargin}m 제외) → " +
                  (chosen < 0 ? $"반경 {SummitSearchRadius}m 안에 길로 닿을 수 있는 칸이 없음" :
                      $"정상 경유점 ({summit.x:F0}, {summit.y:F0}) {GetNodeHeight(context, chosen):F1}m, 꼭대기에서 {Vector2.Distance(summit, peakWorld):F1}m, 경사 {GetNodeSlope(context, chosen):F1}도 " +
                      (flatBest >= 0 ? $"(경사 {SummitFlatSlope}도 이하 중 최고)" : $"(경사 {SummitFlatSlope}도 이하인 곳이 없어 가장 높은 곳)")));
        return chosen >= 0;
    }

    // 탐색 칸마다 둘레 radius 사각형 안의 최고 높이 (가로, 세로로 나눠 구한다)
    private static float[] GetAreaMax(PlanContext context, float radius)
    {
        SearchGrid search = context.Search;
        int reach = Mathf.CeilToInt(radius / GetNodeSpacing(context));
        var heights = new float[search.CountX * search.CountZ];
        for (int node = 0; node < heights.Length; node++)
            heights[node] = GetNodeHeight(context, node);

        return FilterMax(FilterMax(heights, search, reach, true), search, reach, false);
    }

    private static float[] FilterMax(float[] values, SearchGrid search, int reach, bool isAlongX)
    {
        var result = new float[values.Length];
        for (int z = 0; z < search.CountZ; z++)
        {
            for (int x = 0; x < search.CountX; x++)
            {
                float max = float.MinValue;
                for (int offset = -reach; offset <= reach; offset++)
                {
                    var sample = new Vector2Int(isAlongX ? x + offset : x, isAlongX ? z : z + offset);
                    if (IsInsideNodes(search, sample)) max = Mathf.Max(max, values[sample.y * search.CountX + sample.x]);
                }
                result[z * search.CountX + x] = max;
            }
        }
        return result;
    }

    // ---------- 경로 탐색 ----------

    // 서쪽 길을 먼저 정하고, 동쪽 길은 서쪽 길 둘레를 피해 정한다. 호숫가 자리는 길을 낼 수 있는 후보로 정해 waypoints에 넣는다.
    // 하나라도 못 찾으면 null (지형을 바꾸지 않는다)
    private static Trail[] PlanTrails(PlanContext context, Ford ford, Dictionary<string, Vector2> waypoints, List<Vector2> westShores, List<Vector2> eastShores)
    {
        Vector2 westSummit = waypoints[WestSummitWaypointName];
        Trail west = BuildShoreTrail(context, WestTrailName, WestLakeWaypointName, westShores, waypoints,
            shore => new List<Vector2> { GetBaseEdge(shore), shore, westSummit }, NoFordLeg);
        if (west == null) return null;

        Vector2 eastSummit = waypoints[EastSummitWaypointName];
        Trail east = BuildShoreTrail(context, EastTrailName, EastLakeWaypointName, eastShores, waypoints,
            shore => new List<Vector2> { GetBaseEdge(ford.BaseSideBank), ford.BaseSideBank, ford.FarSideBank, shore, eastSummit }, EastFordLeg);
        if (east == null) return null;

        west.RuneWaypoints = new Dictionary<string, Vector2>
        {
            { WestLakeWaypointName, waypoints[WestLakeWaypointName] }, { WestSummitWaypointName, waypoints[WestSummitWaypointName] }
        };
        east.RuneWaypoints = new Dictionary<string, Vector2>
        {
            { EastLakeWaypointName, waypoints[EastLakeWaypointName] }, { EastSummitWaypointName, waypoints[EastSummitWaypointName] }
        };
        return new[] { west, east };
    }

    // 호숫가 후보를 순서대로 넣어 길을 찾고, 처음 찾은 길과 그 호숫가 자리(waypoints[shoreName])를 쓴다.
    // 실패한 후보가 찍은 "이미 정한 경로" 표시는 되돌린다. 모든 후보가 실패하면 null
    private static Trail BuildShoreTrail(PlanContext context, string name, string shoreName, List<Vector2> shores,
        Dictionary<string, Vector2> waypoints, System.Func<Vector2, List<Vector2>> getWaypoints, int fordLeg)
    {
        for (int i = 0; i < shores.Count; i++)
        {
            bool[] plannedBefore = (bool[])context.Search.IsNearPlanned.Clone();
            Trail trail = BuildTrail(context, name, getWaypoints(shores[i]), fordLeg);
            if (trail != null)
            {
                waypoints[shoreName] = shores[i];
                Debug.Log($"[MapTrailBuilder] {name} : {shoreName} {i + 1}번 후보 ({shores[i].x:F0}, {shores[i].y:F0})를 씁니다.");
                return trail;
            }
            context.Search.IsNearPlanned = plannedBefore;
        }

        Debug.LogError($"[MapTrailBuilder] {name} : {shoreName} 후보 {shores.Count}곳 모두 길을 내지 못했습니다 (경사 {PlanMaxSlope}도 이하, 앞 구간에서 {RetraceBlockDistance}m 밖). " +
                       "ShoreCandidateCount, ShoreCandidateSpacing, RetraceBlockDistance를 확인하세요. 지형을 바꾸지 않았습니다.");
        return null;
    }

    // 거점 비움 반경 위, 첫 경유점 방향의 점
    private static Vector2 GetBaseEdge(Vector2 target)
    {
        return target.normalized * MapTreeScatter.BaseClearRadius;
    }

    // 탐색 칸마다 지나갈 수 있는지와 비용 배율을 미리 구한다
    private static SearchGrid CreateSearchGrid(TerrainGrid grid)
    {
        int width = grid.Original.GetLength(1);
        int depth = grid.Original.GetLength(0);
        var search = new SearchGrid { CountX = (width - 1) / NodeStep + 1, CountZ = (depth - 1) / NodeStep + 1 };
        int count = search.CountX * search.CountZ;
        search.IsBlocked = new bool[count];
        search.CostFactors = new float[count];
        search.IsNearPlanned = new bool[count];
        for (int nodeZ = 0; nodeZ < search.CountZ; nodeZ++)
        {
            for (int nodeX = 0; nodeX < search.CountX; nodeX++)
            {
                int x = nodeX * NodeStep;
                int z = nodeZ * NodeStep;
                int node = nodeZ * search.CountX + nodeX;
                search.IsBlocked[node] = GetCellWorld(grid, x, z).magnitude < MapTreeScatter.BaseClearRadius
                                         || grid.RiverDistances.Values[z, x] < RiverKeepDistance
                                         || grid.LakeDistances.Values[z, x] < LakeAvoidDistance;
                float slope = GetCellSlope(grid.Original, z, x, grid.Spacing);
                float excess = Mathf.Max(0f, slope - (GetSideMaxSlope(grid.Original[z, x]) - SteepMarginSlope));
                float protectedFactor = grid.LakeDistances.Values[z, x] < LakeProtectDistance ? ProtectedCostFactor : 1f;
                search.CostFactors[node] = (1f + SteepCostPerDegree * excess) * protectedFactor;
            }
        }
        return search;
    }

    // 여울 구간은 곧은 선, 나머지 구간은 A* 경로를 다듬어 잇는다. 구간을 정할 때마다 그 둘레를 다음 탐색이 피하도록 찍는다.
    // 같은 길의 앞 구간 바로 옆은 지나지 않는다 (CreateRetraceBlock). 경로를 못 찾으면 null (다음 호숫가 후보로 넘어가므로 경고로 남긴다)
    private static Trail BuildTrail(PlanContext context, string name, List<Vector2> waypoints, int fordLeg)
    {
        var trail = new Trail { Name = name, Waypoints = waypoints, Points = new List<Vector2> { waypoints[0] } };
        var legs = new List<List<Vector2>>();
        for (int leg = 0; leg < waypoints.Count - 1; leg++)
        {
            List<Vector2> points = PlanLeg(context, waypoints[leg], waypoints[leg + 1], leg == fordLeg, CreateRetraceBlock(context, legs));
            if (points == null)
            {
                Debug.LogWarning($"[MapTrailBuilder] {name} {leg + 1}번 구간 ({waypoints[leg].x:F0}, {waypoints[leg].y:F0}) → " +
                                 $"({waypoints[leg + 1].x:F0}, {waypoints[leg + 1].y:F0}) 경로를 찾지 못했습니다 (경사 {PlanMaxSlope}도 이하, 같은 길의 앞 구간에서 {RetraceBlockDistance}m 밖).");
                return null;
            }

            if (leg == fordLeg) trail.FordStart = trail.Points.Count - 1;
            trail.Points.AddRange(points.Skip(1));
            if (leg == fordLeg) trail.FordEnd = trail.Points.Count - 1;
            StampPlanned(context, points);
            legs.Add(points);
        }
        trail.Distances = GetDistances(trail.Points);
        return trail;
    }

    private static List<Vector2> PlanLeg(PlanContext context, Vector2 from, Vector2 to, bool isFord, bool[] isRetraceBlocked)
    {
        if (isFord) return Resample(new List<Vector2> { from, to }, PointSpacing);

        List<Vector2> nodes = FindPath(context, from, to, isRetraceBlocked);
        return nodes == null ? null : SmoothLeg(nodes);
    }

    // 규칙은 RetraceBlockDistance 주석. legs는 같은 길에서 이미 정한 구간들 (마지막이 바로 앞 구간)
    private static bool[] CreateRetraceBlock(PlanContext context, List<List<Vector2>> legs)
    {
        SearchGrid search = context.Search;
        var isBlocked = new bool[search.CountX * search.CountZ];
        for (int leg = 0; leg < legs.Count; leg++)
        {
            List<Vector2> points = legs[leg];
            float[] distances = GetDistances(points);
            float blockUntil = leg == legs.Count - 1 ? distances[distances.Length - 1] - RetraceKeepArc : float.MaxValue;
            for (int i = 0; i < points.Count && distances[i] <= blockUntil; i++)
                StampRetrace(context, isBlocked, points[i]);
        }
        return isBlocked;
    }

    // point에서 RetraceBlockDistance 안인 탐색 칸을 표시한다 (칸 위치에서 실제 거리로 잰다)
    private static void StampRetrace(PlanContext context, bool[] isBlocked, Vector2 point)
    {
        SearchGrid search = context.Search;
        float nodeSpacing = GetNodeSpacing(context);
        // 가장 가까운 칸이 점에서 칸 반 대각선만큼 벗어날 수 있어 한 칸 더 본다
        int reach = Mathf.CeilToInt(RetraceBlockDistance / nodeSpacing) + 1;
        int center = GetNearestNode(context, point);
        var centerCell = new Vector2Int(center % search.CountX, center / search.CountX);
        for (int dz = -reach; dz <= reach; dz++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                Vector2Int cell = centerCell + new Vector2Int(dx, dz);
                if (!IsInsideNodes(search, cell)) continue;

                int node = cell.y * search.CountX + cell.x;
                if (Vector2.Distance(GetNodeWorld(context, node), point) < RetraceBlockDistance) isBlocked[node] = true;
            }
        }
    }

    // 정한 구간 점에서 OverlapKeepDistance 안의 탐색 칸을 표시한다
    private static void StampPlanned(PlanContext context, List<Vector2> points)
    {
        SearchGrid search = context.Search;
        float nodeSpacing = GetNodeSpacing(context);
        int reach = Mathf.CeilToInt(OverlapKeepDistance / nodeSpacing);
        float reachSquared = OverlapKeepDistance * OverlapKeepDistance / (nodeSpacing * nodeSpacing);
        for (int i = 0; i < points.Count; i += OverlapStampStride)
        {
            int center = GetNearestNode(context, points[i]);
            var centerCell = new Vector2Int(center % search.CountX, center / search.CountX);
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    Vector2Int cell = centerCell + new Vector2Int(dx, dz);
                    if (dx * dx + dz * dz > reachSquared || !IsInsideNodes(search, cell)) continue;
                    search.IsNearPlanned[cell.y * search.CountX + cell.x] = true;
                }
            }
        }
    }

    private static List<Vector2> FindPath(PlanContext context, Vector2 from, Vector2 to, bool[] isRetraceBlocked)
    {
        int start = GetNearestNode(context, from);
        SearchState state = CreateSearchState(context, GetNearestNode(context, to));
        state.LegStart = from;
        state.LegEnd = to;
        state.IsRetraceBlocked = isRetraceBlocked;
        state.Costs[start] = 0f;
        state.Open.Push(start, Vector2.Distance(GetNodeWorld(context, start), state.GoalPosition));
        while (state.Open.Count > 0)
        {
            int node = state.Open.Pop();
            if (state.IsClosed[node]) continue;
            if (node == state.Goal) return BuildNodePath(context, state, from, to);

            state.IsClosed[node] = true;
            ExpandNode(context, state, node);
        }
        return null;
    }

    private static SearchState CreateSearchState(PlanContext context, int goal)
    {
        int count = context.Search.CountX * context.Search.CountZ;
        var state = new SearchState
        {
            Costs = new float[count],
            Previous = new int[count],
            IncomingMoves = new int[count],
            IsClosed = new bool[count],
            Goal = goal,
            GoalPosition = GetNodeWorld(context, goal)
        };
        for (int i = 0; i < count; i++)
        {
            state.Costs[i] = float.PositiveInfinity;
            state.Previous[i] = -1;
            state.IncomingMoves[i] = -1;
        }
        return state;
    }

    // 막힌 칸(물, 거점 비움 반경, 같은 길의 앞 구간 바로 옆)은 목표 칸일 때만 들어간다. 시작 칸은 막혀 있어도 거기서 나갈 수 있다.
    // 꺾는 비용은 지금까지 가장 싼 경로의 들어온 방향으로 잰다 (칸마다 방향을 따로 두지 않아 메모리를 아낀다. 최적은 아니지만 촘촘한 지그재그를 줄이기에 충분하다)
    private static void ExpandNode(PlanContext context, SearchState state, int node)
    {
        SearchGrid search = context.Search;
        var current = new Vector2Int(node % search.CountX, node / search.CountX);
        for (int move = 0; move < SearchMoves.Length; move++)
        {
            Vector2Int nextCell = current + SearchMoves[move];
            if (!IsInsideNodes(search, nextCell)) continue;

            int next = nextCell.y * search.CountX + nextCell.x;
            if (state.IsClosed[next] || ((search.IsBlocked[next] || state.IsRetraceBlocked[next]) && next != state.Goal)) continue;

            float moveCost = GetMoveCost(context.Grid, current, SearchMoves[move]);
            if (float.IsPositiveInfinity(moveCost)) continue;

            float cost = state.Costs[node] + moveCost * search.CostFactors[next] * GetOverlapFactor(context, state, next)
                         + GetTurnCost(state.IncomingMoves[node], move);
            if (cost >= state.Costs[next]) continue;

            state.Costs[next] = cost;
            state.Previous[next] = node;
            state.IncomingMoves[next] = move;
            state.Open.Push(next, cost + Vector2.Distance(GetNodeWorld(context, next), state.GoalPosition));
        }
    }

    // 이동 앞뒤 반쪽의 경사 중 큰 값이 PlanMaxSlope를 넘으면 무한대. 아니면 거리 × 경사 비용
    private static float GetMoveCost(TerrainGrid grid, Vector2Int node, Vector2Int move)
    {
        Vector2Int from = node * NodeStep;
        Vector2Int middle = from + move * (NodeStep / 2);
        Vector2Int to = from + move * NodeStep;
        float halfLength = move.magnitude * NodeStep * grid.Spacing / 2f;
        float firstRise = Mathf.Abs(grid.Original[middle.y, middle.x] - grid.Original[from.y, from.x]);
        float secondRise = Mathf.Abs(grid.Original[to.y, to.x] - grid.Original[middle.y, middle.x]);
        float grade = Mathf.Max(firstRise, secondRise) / halfLength;
        if (grade > PlanMaxGrade) return float.PositiveInfinity;

        float ratio = grade / PlanMaxGrade;
        return halfLength * 2f * (1f + GradeCostWeight * ratio * ratio);
    }

    private static float GetTurnCost(int previousMove, int move)
    {
        if (previousMove < 0) return 0f;

        float cosine = Vector2.Dot(MoveDirections[previousMove], MoveDirections[move]);
        return HairpinCost * (1f - cosine) * 0.5f;
    }

    // 이미 정한 경로 둘레면 OverlapCostFactor. 단, 이 구간의 시작, 끝 경유점 둘레는 앞 구간과 이어지는 곳이라 1
    private static float GetOverlapFactor(PlanContext context, SearchState state, int node)
    {
        if (!context.Search.IsNearPlanned[node]) return 1f;

        Vector2 world = GetNodeWorld(context, node);
        bool isNearLegEnd = Vector2.Distance(world, state.LegStart) < OverlapKeepDistance || Vector2.Distance(world, state.LegEnd) < OverlapKeepDistance;
        return isNearLegEnd ? 1f : OverlapCostFactor;
    }

    // 목표에서 거꾸로 따라가 칸 위치를 모은다. 양 끝은 칸 위치 대신 실제 경유점으로 바꾼다
    private static List<Vector2> BuildNodePath(PlanContext context, SearchState state, Vector2 from, Vector2 to)
    {
        var nodes = new List<Vector2>();
        for (int node = state.Goal; node != -1; node = state.Previous[node])
            nodes.Add(GetNodeWorld(context, node));
        nodes.Reverse();
        nodes[0] = from;
        nodes[nodes.Count - 1] = to;
        return nodes;
    }

    private static int GetNearestNode(PlanContext context, Vector2 world)
    {
        Vector2 cell = (world - context.Grid.Origin) / GetNodeSpacing(context);
        int x = Mathf.Clamp(Mathf.RoundToInt(cell.x), 0, context.Search.CountX - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt(cell.y), 0, context.Search.CountZ - 1);
        return z * context.Search.CountX + x;
    }

    private static bool IsInsideNodes(SearchGrid search, Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < search.CountX && cell.y >= 0 && cell.y < search.CountZ;
    }

    private static float GetNodeSpacing(PlanContext context)
    {
        return context.Grid.Spacing * NodeStep;
    }

    // 탐색 칸이 놓인 높이맵 꼭짓점 (x, z)
    private static Vector2Int GetNodeCell(PlanContext context, int node)
    {
        return new Vector2Int((node % context.Search.CountX) * NodeStep, (node / context.Search.CountX) * NodeStep);
    }

    private static Vector2 GetNodeWorld(PlanContext context, int node)
    {
        Vector2Int cell = GetNodeCell(context, node);
        return GetCellWorld(context.Grid, cell.x, cell.y);
    }

    private static float GetNodeHeight(PlanContext context, int node)
    {
        Vector2Int cell = GetNodeCell(context, node);
        return context.Grid.Original[cell.y, cell.x];
    }

    private static float GetNodeSlope(PlanContext context, int node)
    {
        Vector2Int cell = GetNodeCell(context, node);
        return GetCellSlope(context.Grid.Original, cell.y, cell.x, context.Grid.Spacing);
    }

    private static float GetNodeLakeDistance(PlanContext context, int node)
    {
        Vector2Int cell = GetNodeCell(context, node);
        return context.Grid.LakeDistances.Values[cell.y, cell.x];
    }

    // ---------- 경로 다듬기 ----------

    // 칸을 이은 꺾은선을 촘촘히 나누고 이동 평균으로 다듬는다. 양 끝(경유점)은 움직이지 않는다
    private static List<Vector2> SmoothLeg(List<Vector2> nodes)
    {
        List<Vector2> points = Resample(nodes, PointSpacing);
        int radius = Mathf.RoundToInt(PathSmoothRadius / PointSpacing);
        for (int pass = 0; pass < PathSmoothPasses; pass++)
            points = SmoothPoints(points, radius);
        return Resample(points, PointSpacing);
    }

    // 끝으로 갈수록 창을 좁혀 양 끝 점이 그대로 남게 한다
    private static List<Vector2> SmoothPoints(List<Vector2> points, int radius)
    {
        var smoothed = new List<Vector2>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            int half = Mathf.Min(radius, i, points.Count - 1 - i);
            Vector2 sum = Vector2.zero;
            for (int j = i - half; j <= i + half; j++)
                sum += points[j];
            smoothed.Add(sum / (2 * half + 1));
        }
        return smoothed;
    }

    // 꺾은선을 같은 간격의 점으로 다시 나눈다. 마지막 점은 그대로 남긴다 (MapRiverBuilder와 같은 방법)
    private static List<Vector2> Resample(List<Vector2> curve, float spacing)
    {
        var points = new List<Vector2> { curve[0] };
        // 마지막으로 찍은 점 뒤로 지나온 거리
        float carried = 0f;
        for (int i = 1; i < curve.Count; i++)
        {
            float length = Vector2.Distance(curve[i - 1], curve[i]);
            float walked = 0f;
            while (carried + length - walked >= spacing)
            {
                walked += spacing - carried;
                points.Add(Vector2.Lerp(curve[i - 1], curve[i], walked / length));
                carried = 0f;
            }
            carried += length - walked;
        }

        // 남은 거리가 짧으면 마지막 점을 끝점으로 옮겨 아주 짧은 구간이 생기지 않게 한다
        if (carried > spacing * 0.5f) points.Add(curve[curve.Count - 1]);
        else points[points.Count - 1] = curve[curve.Count - 1];
        return points;
    }

    // ---------- 길 높이 ----------

    // 두 길을 하나의 제약 그래프로 함께 맞춘다 (다른 길과 가까이 지나는 곳에도 높이 차 제한이 걸리기 때문).
    // 목표 높이 아래에서 가장 높은 "제약을 지키는 높이"와 위에서 가장 낮은 것의 가운데를 쓰고 (목표에서 가장 적게 벗어난다),
    // 그 값을 고정점에서 PinMaxGrade로 오르내리는 범위 안으로 자르면 고정점에서는 땅 높이와 같아진다.
    // 제약(두 점 높이 차 ≤ 허용값)을 지키는 높이끼리의 가운데값, 최대, 최소는 다시 제약을 지키므로 결과도 제약을 지킨다
    private static void FitProfiles(TerrainGrid grid, Trail[] trails)
    {
        ProfileGraph graph = CreateProfileGraph(grid, trails);
        var targets = new float[graph.Count];
        var isPinned = new bool[graph.Count];
        int offset = 0;
        foreach (Trail trail in trails)
        {
            float[] trailTargets = GetTargets(grid, trail);
            System.Array.Copy(trailTargets, 0, targets, offset, trailTargets.Length);
            System.Array.Copy(trail.IsPinned, 0, isPinned, offset, trailTargets.Length);
            offset += trailTargets.Length;
        }

        float[] below = SolveCeiling(graph, targets, PlanMaxGrade);
        float[] above = SolveFloor(graph, targets, PlanMaxGrade);
        float[] pinCeiling = SolveCeiling(graph, GetPinValues(targets, isPinned, float.PositiveInfinity), PinMaxGrade);
        float[] pinFloor = SolveFloor(graph, GetPinValues(targets, isPinned, float.NegativeInfinity), PinMaxGrade);

        var profile = new float[graph.Count];
        var isConflict = new bool[graph.Count];
        for (int i = 0; i < graph.Count; i++)
        {
            // 고정점끼리 제약보다 가파르게 놓인 곳은 가운데를 쓴다 (로그로 확인)
            isConflict[i] = pinFloor[i] > pinCeiling[i];
            float middle = (below[i] + above[i]) * 0.5f;
            profile[i] = isConflict[i] ? (pinFloor[i] + pinCeiling[i]) * 0.5f : Mathf.Clamp(middle, pinFloor[i], pinCeiling[i]);
        }

        offset = 0;
        foreach (Trail trail in trails)
        {
            int count = trail.Points.Count;
            trail.Profile = new float[count];
            System.Array.Copy(profile, offset, trail.Profile, 0, count);
            trail.PinConflictCount = isConflict.Skip(offset).Take(count).Count(flag => flag);
            offset += count;
        }
        Debug.Log($"[MapTrailBuilder] 길 높이 근접 제약 {graph.PairCount}쌍 (다른 길, 같은 길의 마주 보는 다리 중 {ProximityRange}m 안)");
    }

    // 땅을 바꾸지 않는 곳과 양 끝은 중심선 땅 높이에 고정하고, 나머지는 길 폭 단면 평균 높이(깎는 양과 메우는 양이 비슷해진다)를 목표로 한다
    private static float[] GetTargets(TerrainGrid grid, Trail trail)
    {
        int count = trail.Points.Count;
        trail.IsPinned = new bool[count];
        var targets = new float[count];
        for (int i = 0; i < count; i++)
        {
            trail.IsPinned[i] = i == 0 || i == count - 1 || IsProtectedAt(grid, trail.Points[i]);
            targets[i] = trail.IsPinned[i]
                ? SampleHeight(grid, grid.Original, trail.Points[i])
                : GetCrossAverage(grid, trail.Points, i);
        }
        return SmoothValues(targets, trail.IsPinned, Mathf.RoundToInt(ProfileSmoothRadius / PointSpacing));
    }

    private static float GetCrossAverage(TerrainGrid grid, List<Vector2> points, int index)
    {
        Vector2 right = GetRight(points, index) * HalfWidth;
        return (SampleHeight(grid, grid.Original, points[index] - right)
                + SampleHeight(grid, grid.Original, points[index])
                + SampleHeight(grid, grid.Original, points[index] + right)) / 3f;
    }

    private static float[] SmoothValues(float[] values, bool[] isPinned, int radius)
    {
        var smoothed = (float[])values.Clone();
        for (int i = 0; i < values.Length; i++)
        {
            if (isPinned[i]) continue;

            int half = Mathf.Min(radius, i, values.Length - 1 - i);
            float sum = 0f;
            for (int j = i - half; j <= i + half; j++)
                sum += values[j];
            smoothed[i] = sum / (2 * half + 1);
        }
        return smoothed;
    }

    private static float[] GetPinValues(float[] targets, bool[] isPinned, float freeValue)
    {
        var values = new float[targets.Length];
        for (int i = 0; i < targets.Length; i++)
            values[i] = isPinned[i] ? targets[i] : freeValue;
        return values;
    }

    private static ProfileGraph CreateProfileGraph(TerrainGrid grid, Trail[] trails)
    {
        int count = trails.Sum(trail => trail.Points.Count);
        var graph = new ProfileGraph
        {
            Count = count,
            Distances = new float[count],
            IsLinkedToPrevious = new bool[count],
            Neighbors = new List<int>[count],
            Allowances = new List<float>[count]
        };
        var points = new ProfilePoint[count];
        int index = 0;
        for (int trailIndex = 0; trailIndex < trails.Length; trailIndex++)
        {
            Trail trail = trails[trailIndex];
            for (int i = 0; i < trail.Points.Count; i++, index++)
            {
                graph.Distances[index] = trail.Distances[i];
                graph.IsLinkedToPrevious[index] = i > 0;
                graph.Neighbors[index] = new List<int>();
                graph.Allowances[index] = new List<float>();
                points[index] = new ProfilePoint
                {
                    Position = trail.Points[i],
                    Forward = GetForward(trail.Points, i),
                    Ground = SampleHeight(grid, grid.Original, trail.Points[i]),
                    TrailIndex = trailIndex
                };
            }
        }
        AddProximityPairs(graph, points);
        return graph;
    }

    // ProximityRange 크기 칸으로 점을 나눠, 이웃 칸끼리만 거리를 잰다
    private static void AddProximityPairs(ProfileGraph graph, ProfilePoint[] points)
    {
        var buckets = new Dictionary<Vector2Int, List<int>>();
        for (int i = 0; i < points.Length; i++)
        {
            Vector2Int key = GetBucket(points[i].Position);
            if (!buckets.TryGetValue(key, out List<int> members))
            {
                members = new List<int>();
                buckets[key] = members;
            }
            members.Add(i);
        }

        for (int i = 0; i < points.Length; i++)
        {
            Vector2Int key = GetBucket(points[i].Position);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (buckets.TryGetValue(key + new Vector2Int(dx, dz), out List<int> members)) AddBucketPairs(graph, points, i, members);
                }
            }
        }
    }

    private static Vector2Int GetBucket(Vector2 position)
    {
        return new Vector2Int(Mathf.FloorToInt(position.x / ProximityRange), Mathf.FloorToInt(position.y / ProximityRange));
    }

    private static void AddBucketPairs(ProfileGraph graph, ProfilePoint[] points, int index, List<int> members)
    {
        foreach (int other in members)
        {
            if (other <= index) continue;

            float allowance = GetProximityAllowance(graph, points, index, other);
            if (allowance < 0f) continue;

            graph.Neighbors[index].Add(other);
            graph.Allowances[index].Add(allowance);
            graph.Neighbors[other].Add(index);
            graph.Allowances[other].Add(allowance);
            graph.PairCount++;
        }
    }

    // 두 점의 높이 차 허용값. 제약이 필요 없으면 NoConstraint.
    // 같은 길의 점은 경로 거리가 ProximityMinArc보다 멀고 서로 마주 볼 때(지그재그 다리)만, 그리고 경로를 따라 이어진 경사 제한보다 좁을 때만 건다
    private static float GetProximityAllowance(ProfileGraph graph, ProfilePoint[] points, int first, int second)
    {
        ProfilePoint a = points[first];
        ProfilePoint b = points[second];
        Vector2 offset = b.Position - a.Position;
        float distance = offset.magnitude;
        if (distance >= ProximityRange) return NoConstraint;

        float limit = GetSideLimitTan(Mathf.Min(a.Ground, b.Ground));
        float allowance = limit * Mathf.Max(0f, distance - HalfWidth * 2f - BedGapMargin);
        if (a.TrailIndex != b.TrailIndex) return allowance;

        float arc = Mathf.Abs(graph.Distances[second] - graph.Distances[first]);
        if (arc <= ProximityMinArc || allowance >= PinMaxGrade * arc) return NoConstraint;
        // 경로가 자기 자신과 겹친 곳은 방향을 볼 수 없지만 두 바닥 높이가 같아야 한다
        if (distance <= Mathf.Epsilon) return allowance;

        Vector2 across = offset / distance;
        bool isFacing = Mathf.Abs(Vector2.Dot(a.Forward, across)) <= FacingMaxCos && Mathf.Abs(Vector2.Dot(b.Forward, across)) <= FacingMaxCos;
        return isFacing ? allowance : NoConstraint;
    }

    // values 이하에서 가장 높은, 모든 제약(경로 앞뒤 점 : chainGrade × 경로 거리, 근접 점 : 허용값)을 지키는 높이.
    // 낮은 점부터 확정하며 이웃으로 퍼뜨린다 (여러 출발점 Dijkstra). 무한대인 점은 이웃에서 내려온다
    private static float[] SolveCeiling(ProfileGraph graph, float[] values, float chainGrade)
    {
        var solve = new ProfileSolve { Values = (float[])values.Clone(), IsDone = new bool[graph.Count] };
        for (int i = 0; i < graph.Count; i++)
        {
            if (!float.IsPositiveInfinity(solve.Values[i])) solve.Open.Push(i, solve.Values[i]);
        }

        while (solve.Open.Count > 0)
        {
            int node = solve.Open.Pop();
            if (solve.IsDone[node]) continue;

            solve.IsDone[node] = true;
            float value = solve.Values[node];
            if (graph.IsLinkedToPrevious[node])
                Lower(solve, node - 1, value + chainGrade * (graph.Distances[node] - graph.Distances[node - 1]));
            if (node + 1 < graph.Count && graph.IsLinkedToPrevious[node + 1])
                Lower(solve, node + 1, value + chainGrade * (graph.Distances[node + 1] - graph.Distances[node]));
            for (int k = 0; k < graph.Neighbors[node].Count; k++)
                Lower(solve, graph.Neighbors[node][k], value + graph.Allowances[node][k]);
        }
        return solve.Values;
    }

    private static void Lower(ProfileSolve solve, int node, float value)
    {
        if (solve.IsDone[node] || value >= solve.Values[node]) return;

        solve.Values[node] = value;
        solve.Open.Push(node, value);
    }

    // values 이상에서 가장 낮은, 모든 제약을 지키는 높이 (부호를 뒤집어 SolveCeiling을 쓴다)
    private static float[] SolveFloor(ProfileGraph graph, float[] values, float chainGrade)
    {
        float[] ceiling = SolveCeiling(graph, values.Select(value => -value).ToArray(), chainGrade);
        return ceiling.Select(value => -value).ToArray();
    }

    // ---------- 고치는 범위 ----------

    // 점마다 고치는 범위와 가파른 곳 옆 비탈을 구하고, 앞뒤 WidthSmoothDistance 안의 최대값으로 넓힌다
    private static void CalculateCarveWidths(TerrainGrid grid, Trail trail)
    {
        int count = trail.Points.Count;
        var widths = new float[count];
        var steepLimits = new float[count];
        trail.SteepPoints = new Representatives();
        trail.CappedSteepCount = 0;
        for (int i = 0; i < count; i++)
        {
            float steep = MeasureSection(grid, trail, i, out widths[i]);
            if (steep <= 0f) continue;

            if (steep > MaxSteepCutTan)
            {
                steep = MaxSteepCutTan;
                trail.CappedSteepCount++;
            }
            steepLimits[i] = steep;
            trail.SteepPoints.Add(trail.Points[i], Mathf.Atan(steep * StencilSlopeFactor) * Mathf.Rad2Deg);
        }
        trail.CarveWidths = SpreadMax(widths, trail.Distances);
        trail.SteepLimits = SpreadMax(steepLimits, trail.Distances);
    }

    // 중심선 양옆 땅을 MaxCarveHalfWidth에서 안쪽으로 읽어, 옆 비탈 범위(길 높이 ± 제한 기울기 × 길 바닥 끝에서의 거리) 밖인 가장 먼 거리를 찾는다.
    // 범위 = 그 거리 + CarveMargin을 [CarveHalfWidth, MaxCarveHalfWidth]로 자른 값.
    // 상한 안에서 비탈이 땅과 만나지 못하면 범위 끝 띠의 땅에 닿는 옆 비탈 기울기를 돌려준다 (아니면 0)
    private static float MeasureSection(TerrainGrid grid, Trail trail, int index, out float width)
    {
        Vector2 right = GetRight(trail.Points, index);
        float bed = trail.Profile[index];
        float limit = GetSideLimitTan(Mathf.Min(SampleHeight(grid, grid.Original, trail.Points[index]), bed));
        float farthest = 0f;
        foreach (float side in Sides)
            farthest = Mathf.Max(farthest, FindFarthestOutside(grid, new SectionScan { Point = trail.Points[index], Side = right * side, Bed = bed }, limit));

        width = Mathf.Clamp(farthest + CarveMargin, CarveHalfWidth, MaxCarveHalfWidth);
        if (farthest + CarveMargin <= MaxCarveHalfWidth) return 0f;

        float bandGrade = 0f;
        foreach (float side in Sides)
            bandGrade = Mathf.Max(bandGrade, GetOuterBandGrade(grid, new SectionScan { Point = trail.Points[index], Side = right * side, Bed = bed }));
        return Mathf.Max(limit, bandGrade * SteepCutSlack);
    }

    // 바꾸지 않는 칸(물턱, 강 둘레)은 넓혀도 고칠 수 없으므로 보지 않는다
    private static float FindFarthestOutside(TerrainGrid grid, SectionScan scan, float limit)
    {
        for (float distance = MaxCarveHalfWidth; distance > HalfWidth; distance -= WidthScanStep)
        {
            Vector2 world = scan.Point + scan.Side * distance;
            if (IsProtectedAt(grid, world)) continue;

            float ground = SampleHeight(grid, grid.Original, world);
            if (Mathf.Abs(ground - scan.Bed) > limit * (distance - HalfWidth) + SectionTolerance) return distance;
        }
        return 0f;
    }

    // 범위 끝 띠의 땅이 모두 옆 비탈 범위 안에 들려면 필요한 기울기
    private static float GetOuterBandGrade(TerrainGrid grid, SectionScan scan)
    {
        float grade = 0f;
        for (float distance = MaxCarveHalfWidth; distance >= MaxCarveHalfWidth - CarveMargin; distance -= WidthScanStep)
        {
            Vector2 world = scan.Point + scan.Side * distance;
            if (IsProtectedAt(grid, world)) continue;

            float ground = SampleHeight(grid, grid.Original, world);
            grade = Mathf.Max(grade, Mathf.Abs(ground - scan.Bed) / (distance - HalfWidth));
        }
        return grade;
    }

    private static float[] SpreadMax(float[] values, float[] distances)
    {
        var spread = new float[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            float max = values[i];
            for (int j = i - 1; j >= 0 && distances[i] - distances[j] <= WidthSmoothDistance; j--)
                max = Mathf.Max(max, values[j]);
            for (int j = i + 1; j < values.Length && distances[j] - distances[i] <= WidthSmoothDistance; j++)
                max = Mathf.Max(max, values[j]);
            spread[i] = max;
        }
        return spread;
    }

    // ---------- 지형 ----------

    private static CarveField CreateCarveField(TerrainGrid grid, Trail[] trails)
    {
        int depth = grid.Original.GetLength(0);
        int width = grid.Original.GetLength(1);
        var field = new CarveField
        {
            TrailDistances = CreateFilled(depth, width, MaxCarveHalfWidth),
            BedHeights = new float[depth, width],
            CarveWidths = new float[depth, width],
            SteepLimits = new float[depth, width],
            IsInZone = new bool[depth, width],
            IsProtected = new bool[depth, width],
            IsBed = new bool[depth, width],
            Limits = new float[depth, width],
            Ceiling = new float[depth, width],
            Floor = new float[depth, width]
        };
        foreach (Trail trail in trails)
        {
            for (int i = 0; i < trail.Points.Count - 1; i++)
                StampTrailSegment(grid, field, trail, i);
        }

        var min = new Vector2Int(width, depth);
        var max = new Vector2Int(-1, -1);
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!InitCarveCell(grid, field, z, x)) continue;

                min = Vector2Int.Min(min, new Vector2Int(x, z));
                max = Vector2Int.Max(max, new Vector2Int(x, z));
            }
        }
        field.Bounds = new RectInt(min.x, min.y, max.x - min.x, max.y - min.y);

        RelaxField(new RelaxJob { Field = field, Values = field.Ceiling, Original = grid.Original, Spacing = grid.Spacing, IsCeiling = true }, "위 한계");
        RelaxField(new RelaxJob { Field = field, Values = field.Floor, Original = grid.Original, Spacing = grid.Spacing, IsCeiling = false }, "아래 한계");
        return field;
    }

    // 두 길 중 가장 가까운 중심선 위치까지의 거리와 그곳의 길 높이, 고치는 범위, 가파른 곳 옆 비탈을 구한다
    private static void StampTrailSegment(TerrainGrid grid, CarveField field, Trail trail, int index)
    {
        Vector2 from = trail.Points[index];
        Vector2 to = trail.Points[index + 1];
        float reach = Mathf.Max(trail.CarveWidths[index], trail.CarveWidths[index + 1]);
        float steepLimit = Mathf.Max(trail.SteepLimits[index], trail.SteepLimits[index + 1]);
        RectInt cells = GetCellRange(grid, from, to, reach);
        for (int z = cells.yMin; z <= cells.yMax; z++)
        {
            for (int x = cells.xMin; x <= cells.xMax; x++)
            {
                Vector2 world = GetCellWorld(grid, x, z);
                float t = GetProjection(from, to, world);
                float distance = Vector2.Distance(world, Vector2.Lerp(from, to, t));
                if (distance >= field.TrailDistances[z, x]) continue;

                field.TrailDistances[z, x] = distance;
                field.BedHeights[z, x] = Mathf.Lerp(trail.Profile[index], trail.Profile[index + 1], t);
                field.CarveWidths[z, x] = reach;
                field.SteepLimits[z, x] = steepLimit;
            }
        }
    }

    // 길 바닥(바꾸는 곳)과 고정 칸(물턱, 강 둘레)은 그 높이에서, 나머지는 위 한계 무한대, 아래 한계 -무한대에서 시작한다. 고치는 범위 안이면 true
    private static bool InitCarveCell(TerrainGrid grid, CarveField field, int z, int x)
    {
        float distance = field.TrailDistances[z, x];
        field.IsInZone[z, x] = distance < field.CarveWidths[z, x];
        if (!field.IsInZone[z, x]) return false;

        float original = grid.Original[z, x];
        bool isProtected = IsProtectedCell(grid, z, x);
        bool isBed = !isProtected && distance <= HalfWidth;
        field.IsProtected[z, x] = isProtected;
        field.IsBed[z, x] = isBed;

        float seed = isProtected ? original : field.BedHeights[z, x];
        field.Ceiling[z, x] = isProtected || isBed ? seed : float.PositiveInfinity;
        field.Floor[z, x] = isProtected || isBed ? seed : float.NegativeInfinity;
        field.Limits[z, x] = Mathf.Max(GetSideLimitTan(Mathf.Min(original, field.BedHeights[z, x])), field.SteepLimits[z, x]);
        return true;
    }

    // 이웃 사이 허용 높이 차로 위, 아래 한계를 퍼뜨린다. 고치는 범위 밖 이웃은 바뀌지 않으므로 원래 높이에 고정된 칸으로 본다 (범위 끝에서 단차가 생기지 않게)
    private static void RelaxField(RelaxJob job, string label)
    {
        for (int pass = 1; pass <= MaxRelaxPasses; pass++)
        {
            float change = Mathf.Max(RelaxPass(job, true), RelaxPass(job, false));
            if (change <= RelaxTolerance) return;
        }
        Debug.LogWarning($"[MapTrailBuilder] 옆 비탈 계산({label})이 {MaxRelaxPasses}회 안에 끝나지 않았다. 옆 비탈 경사가 제한을 넘을 수 있다");
    }

    // 한 방향 패스에서 가장 크게 바뀐 높이를 돌려준다. 고치는 범위를 담는 꼭짓점 범위만 돈다
    private static float RelaxPass(RelaxJob job, bool isForward)
    {
        RectInt bounds = job.Field.Bounds;
        int sign = isForward ? 1 : -1;
        float maxChange = 0f;
        for (int i = 0; i <= bounds.height; i++)
        {
            int z = isForward ? bounds.yMin + i : bounds.yMax - i;
            for (int j = 0; j <= bounds.width; j++)
            {
                int x = isForward ? bounds.xMin + j : bounds.xMax - j;
                if (!job.Field.IsInZone[z, x]) continue;

                float value = RelaxCell(job, new Vector2Int(x, z), sign);
                if (value == job.Values[z, x]) continue;

                // 무한대에서 처음 내려온 칸은 바뀐 양을 셀 수 없으므로 한 번 더 돌게 한다
                float change = float.IsInfinity(job.Values[z, x]) ? float.MaxValue : Mathf.Abs(value - job.Values[z, x]);
                maxChange = Mathf.Max(maxChange, change);
                job.Values[z, x] = value;
            }
        }
        return maxChange;
    }

    // 허용 높이 차 = max(두 칸 중 작은 제한 기울기 × 거리, 원래 땅의 높이 차). 원래 땅이 제한보다 가파른 곳은 원래 기울기까지 허용해,
    // 길이 닿지 않는 땅은 원래 높이 그대로 위, 아래 한계 사이에 남는다
    private static float RelaxCell(RelaxJob job, Vector2Int cell, int sign)
    {
        CarveField field = job.Field;
        int depth = job.Values.GetLength(0);
        int width = job.Values.GetLength(1);
        float value = job.Values[cell.y, cell.x];
        foreach (Vector2Int offset in RelaxNeighbors)
        {
            int x = cell.x + offset.x * sign;
            int z = cell.y + offset.y * sign;
            if (x < 0 || x >= width || z < 0 || z >= depth) continue;

            bool isInZone = field.IsInZone[z, x];
            float neighbor = isInZone ? job.Values[z, x] : job.Original[z, x];
            float limit = isInZone ? Mathf.Min(field.Limits[cell.y, cell.x], field.Limits[z, x]) : field.Limits[cell.y, cell.x];
            float distance = offset.x != 0 && offset.y != 0 ? job.Spacing * Mathf.Sqrt(2f) : job.Spacing;
            float allowed = Mathf.Max(limit * distance, Mathf.Abs(job.Original[cell.y, cell.x] - job.Original[z, x]));
            value = PullToward(value, neighbor, allowed, job.IsCeiling);
        }
        return value;
    }

    private static float PullToward(float value, float neighbor, float allowed, bool isCeiling)
    {
        return isCeiling ? Mathf.Min(value, neighbor + allowed) : Mathf.Max(value, neighbor - allowed);
    }

    // 길 바닥은 길 높이로, 옆은 원래 땅을 [아래 한계, 위 한계] 안으로 자른다 (원래 땅이 두 비탈 사이에 있으면 그대로 남는다).
    // 고정 칸(물턱, 강 둘레)은 바꾸지 않는다
    private static CarveStats ApplyCarve(TerrainGrid grid, CarveField field)
    {
        var stats = new CarveStats();
        RectInt bounds = field.Bounds;
        for (int z = bounds.yMin; z <= bounds.yMax; z++)
        {
            for (int x = bounds.xMin; x <= bounds.xMax; x++)
            {
                if (!field.IsInZone[z, x] || field.IsProtected[z, x]) continue;

                float height = GetCarvedHeight(grid, field, new Vector2Int(x, z), stats);
                grid.Heights[z, x] = height;
                bool isEdge = field.TrailDistances[z, x] >= field.CarveWidths[z, x] - EdgeCheckWidth;
                RecordChange(stats, GetCellWorld(grid, x, z), height - grid.Original[z, x], isEdge);
            }
        }
        return stats;
    }

    private static float GetCarvedHeight(TerrainGrid grid, CarveField field, Vector2Int cell, CarveStats stats)
    {
        float ceiling = field.Ceiling[cell.y, cell.x];
        float floor = field.Floor[cell.y, cell.x];
        Vector2 world = GetCellWorld(grid, cell.x, cell.y);
        if (field.IsBed[cell.y, cell.x])
        {
            float bed = field.BedHeights[cell.y, cell.x];
            float excess = Mathf.Max(bed - ceiling, floor - bed);
            if (excess > ChangeThreshold) stats.BedConflicts.Add(world, excess);
            return bed;
        }

        if (floor <= ceiling) return Mathf.Clamp(grid.Original[cell.y, cell.x], floor, ceiling);

        // 양쪽 길 바닥(또는 고정 칸, 범위 밖 땅) 사이가 옆 비탈 제한으로 이을 수 없을 만큼 높이 차가 큰 곳 (로그로 확인)
        stats.SideConflicts.Add(world, floor - ceiling);
        return (floor + ceiling) * 0.5f;
    }

    private static void RecordChange(CarveStats stats, Vector2 world, float change, bool isEdge)
    {
        if (Mathf.Abs(change) > ChangeThreshold) stats.ChangedCount++;
        if (-change > stats.MaxCut)
        {
            stats.MaxCut = -change;
            stats.MaxCutPosition = world;
        }
        if (change > stats.MaxFill)
        {
            stats.MaxFill = change;
            stats.MaxFillPosition = world;
        }
        if (isEdge && Mathf.Abs(change) > stats.MaxEdgeChange)
        {
            stats.MaxEdgeChange = Mathf.Abs(change);
            stats.MaxEdgeChangePosition = world;
        }
    }

    // 바뀐 칸만 다시 정규화하고, 나머지는 읽은 값 그대로 쓴다
    private static void WriteHeights(Terrain terrain, TerrainGrid grid)
    {
        TerrainData data = terrain.terrainData;
        float baseY = terrain.transform.position.y;
        for (int z = 0; z < grid.Heights.GetLength(0); z++)
        {
            for (int x = 0; x < grid.Heights.GetLength(1); x++)
            {
                if (grid.Heights[z, x] == grid.Original[z, x]) continue;
                grid.Normalized[z, x] = (grid.Heights[z, x] - baseY) / data.size.y;
            }
        }
        data.SetHeights(grid.Range.x, grid.Range.y, grid.Normalized);
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
    }

    // ---------- 경로 저장 ----------

    private static void SaveTrails(Trail[] trails, Dictionary<string, Vector2> waypoints)
    {
        var data = new TrailData();
        foreach (Trail trail in trails)
            data.Trails.Add(new TrailRecord { Name = trail.Name, Points = trail.Points });
        foreach (KeyValuePair<string, Vector2> waypoint in waypoints)
            data.Waypoints.Add(new WaypointRecord { Name = waypoint.Key, Position = waypoint.Value });

        File.WriteAllText(TrailDataPath, JsonUtility.ToJson(data));
        AssetDatabase.ImportAsset(TrailDataPath);
    }

    private static TrailData LoadTrailData()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(TrailDataPath);
        if (asset == null)
        {
            Debug.LogError($"{TrailDataPath}가 없습니다. 먼저 'Tools/Map/7-1. 등산로 만들기'를 실행하세요.");
            return null;
        }
        return JsonUtility.FromJson<TrailData>(asset.text);
    }

    // ---------- 계산 ----------

    // 호수 물턱 또는 강(물, 강둑) 둘레라 지형을 바꾸지 않는 칸
    private static bool IsProtectedCell(TerrainGrid grid, int z, int x)
    {
        return grid.LakeDistances.Values[z, x] < LakeProtectDistance || grid.RiverDistances.Values[z, x] < RiverProtectDistance;
    }

    private static bool IsProtectedAt(TerrainGrid grid, Vector2 world)
    {
        Vector2Int cell = GetNearestCell(grid, world);
        return IsProtectedCell(grid, cell.y, cell.x);
    }

    private static float GetSideMaxSlope(float height)
    {
        return height < FoothillMaxHeight + FoothillLimitMargin ? FoothillSideMaxSlope : UpperSideMaxSlope;
    }

    // 이웃 칸 사이 허용 기울기(tan). 대각선 때문에 높이맵 경사가 커지는 만큼 낮춘다
    private static float GetSideLimitTan(float height)
    {
        return Mathf.Tan(GetSideMaxSlope(height) * Mathf.Deg2Rad) / StencilSlopeFactor;
    }

    private static Vector2 GetCellWorld(TerrainGrid grid, int x, int z)
    {
        return grid.Origin + new Vector2(x, z) * grid.Spacing;
    }

    private static Vector2Int GetNearestCell(TerrainGrid grid, Vector2 world)
    {
        Vector2 cell = (world - grid.Origin) / grid.Spacing;
        int x = Mathf.Clamp(Mathf.RoundToInt(cell.x), 0, grid.Original.GetLength(1) - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt(cell.y), 0, grid.Original.GetLength(0) - 1);
        return new Vector2Int(x, z);
    }

    // 구간 from-to에서 reach 안에 들 수 있는 칸 범위 (xMax, yMax 포함)
    private static RectInt GetCellRange(TerrainGrid grid, Vector2 from, Vector2 to, float reach)
    {
        int lastX = grid.Original.GetLength(1) - 1;
        int lastZ = grid.Original.GetLength(0) - 1;
        Vector2 min = (Vector2.Min(from, to) - Vector2.one * reach - grid.Origin) / grid.Spacing;
        Vector2 max = (Vector2.Max(from, to) + Vector2.one * reach - grid.Origin) / grid.Spacing;
        int xMin = Mathf.Clamp(Mathf.FloorToInt(min.x), 0, lastX);
        int zMin = Mathf.Clamp(Mathf.FloorToInt(min.y), 0, lastZ);
        int xMax = Mathf.Clamp(Mathf.CeilToInt(max.x), 0, lastX);
        int zMax = Mathf.Clamp(Mathf.CeilToInt(max.y), 0, lastZ);
        return new RectInt(xMin, zMin, xMax - xMin, zMax - zMin);
    }

    // 높이맵 꼭짓점 사이를 겹선형으로 읽는다
    private static float SampleHeight(TerrainGrid grid, float[,] heights, Vector2 world)
    {
        Vector2 cell = (world - grid.Origin) / grid.Spacing;
        int x = Mathf.Clamp(Mathf.FloorToInt(cell.x), 0, heights.GetLength(1) - 2);
        int z = Mathf.Clamp(Mathf.FloorToInt(cell.y), 0, heights.GetLength(0) - 2);
        float tx = Mathf.Clamp01(cell.x - x);
        float tz = Mathf.Clamp01(cell.y - z);
        float south = Mathf.Lerp(heights[z, x], heights[z, x + 1], tx);
        float north = Mathf.Lerp(heights[z + 1, x], heights[z + 1, x + 1], tx);
        return Mathf.Lerp(south, north, tz);
    }

    // 가운데 차분 경사 (도). 범위 끝 칸은 안쪽 칸과의 차로 잰다
    private static float GetCellSlope(float[,] heights, int z, int x, float spacing)
    {
        int left = Mathf.Max(x - 1, 0);
        int right = Mathf.Min(x + 1, heights.GetLength(1) - 1);
        int south = Mathf.Max(z - 1, 0);
        int north = Mathf.Min(z + 1, heights.GetLength(0) - 1);
        float gradientX = (heights[z, right] - heights[z, left]) / ((right - left) * spacing);
        float gradientZ = (heights[north, x] - heights[south, x]) / ((north - south) * spacing);
        return Mathf.Atan(Mathf.Sqrt(gradientX * gradientX + gradientZ * gradientZ)) * Mathf.Rad2Deg;
    }

    private static float[,] CreateFilled(int depth, int width, float value)
    {
        var filled = new float[depth, width];
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
                filled[z, x] = value;
        }
        return filled;
    }

    private static float[] GetDistances(List<Vector2> points)
    {
        var distances = new float[points.Count];
        for (int i = 1; i < points.Count; i++)
            distances[i] = distances[i - 1] + Vector2.Distance(points[i - 1], points[i]);
        return distances;
    }

    // 경로 진행 방향 (단위 벡터)
    private static Vector2 GetForward(List<Vector2> points, int index)
    {
        Vector2 previous = points[Mathf.Max(index - 1, 0)];
        Vector2 next = points[Mathf.Min(index + 1, points.Count - 1)];
        return (next - previous).normalized;
    }

    // 경로 진행 방향의 오른쪽 (단위 벡터)
    private static Vector2 GetRight(List<Vector2> points, int index)
    {
        Vector2 forward = GetForward(points, index);
        return new Vector2(forward.y, -forward.x);
    }

    private static float GetDistanceToPath(List<Vector2> points, Vector2 position)
    {
        float minDistance = float.MaxValue;
        for (int i = 0; i < points.Count - 1; i++)
            minDistance = Mathf.Min(minDistance, GetSegmentDistance(points[i], points[i + 1], position));
        return minDistance;
    }

    private static float GetSegmentDistance(Vector2 from, Vector2 to, Vector2 position)
    {
        return Vector2.Distance(position, Vector2.Lerp(from, to, GetProjection(from, to, position)));
    }

    // 선분 a-b 위에서 position에 가장 가까운 곳의 비율 (0 = a, 1 = b). 길이 0인 선분(호수 정점)은 0
    private static float GetProjection(Vector2 a, Vector2 b, Vector2 position)
    {
        Vector2 segment = b - a;
        if (segment.sqrMagnitude <= 0f) return 0f;
        return Mathf.Clamp01(Vector2.Dot(position - a, segment) / segment.sqrMagnitude);
    }

    // ---------- 로그 ----------

    private static void LogFord(Ford ford)
    {
        Debug.Log($"[MapTrailBuilder] 여울 강 중심 ({ford.Center.x:F0}, {ford.Center.y:F0}), 거점 쪽 끝 ({ford.BaseSideBank.x:F0}, {ford.BaseSideBank.y:F0}) → " +
                  $"건너편 끝 ({ford.FarSideBank.x:F0}, {ford.FarSideBank.y:F0}), 깎기 전 곧은 선 최대 경사 {ford.MaxSlope:F1}도");
        if (ford.MaxSlope > TrailMaxSlope) Debug.LogWarning($"[MapTrailBuilder] 여울 곧은 선의 경사가 {TrailMaxSlope}도를 넘습니다. 강둑 구간은 지형을 바꾸지 않으므로 그대로 남습니다.");
    }

    private static void LogTrail(TerrainGrid grid, Trail trail)
    {
        string waypointText = string.Join(" → ", trail.Waypoints.Select(point => $"({point.x:F0}, {point.y:F0})"));
        float length = trail.Distances[trail.Distances.Length - 1];
        float startHeight = SampleHeight(grid, grid.Heights, trail.Points[0]);
        float endHeight = SampleHeight(grid, grid.Heights, trail.Points[trail.Points.Count - 1]);
        Debug.Log($"[MapTrailBuilder] {trail.Name} 경유점 : {waypointText}");
        Debug.Log($"[MapTrailBuilder] {trail.Name} 경로 점 {trail.Points.Count}개 (간격 {PointSpacing}m), 길이 {length:F0}m, " +
                  $"시작 높이 {startHeight:F1}m, 끝 높이 {endHeight:F1}m");
        LogGrade(grid, trail);
        LogTrailWater(grid, trail);
        LogWaypointDistances(trail);
        LogSelfSpacing(trail);
        LogCarveWidths(trail);
    }

    // 계획한 길 높이의 경사와, 고친 지형을 중심선에서 앞뒤 GradeCheckHalfSpan 거리로 다시 잰 경사
    private static void LogGrade(TerrainGrid grid, Trail trail)
    {
        float planned = 0f;
        for (int i = 1; i < trail.Profile.Length; i++)
        {
            float run = trail.Distances[i] - trail.Distances[i - 1];
            planned = Mathf.Max(planned, Mathf.Atan(Mathf.Abs(trail.Profile[i] - trail.Profile[i - 1]) / run) * Mathf.Rad2Deg);
        }

        int span = Mathf.Max(1, Mathf.RoundToInt(GradeCheckHalfSpan / PointSpacing));
        float measured = 0f;
        Vector2 measuredPosition = Vector2.zero;
        var overPoints = new Representatives();
        for (int i = span; i < trail.Points.Count - span; i++)
        {
            float rise = Mathf.Abs(SampleHeight(grid, grid.Heights, trail.Points[i + span]) - SampleHeight(grid, grid.Heights, trail.Points[i - span]));
            float slope = Mathf.Atan(rise / (trail.Distances[i + span] - trail.Distances[i - span])) * Mathf.Rad2Deg;
            if (slope > TrailMaxSlope + SlopeCheckTolerance) overPoints.Add(trail.Points[i], slope);
            if (slope <= measured) continue;

            measured = slope;
            measuredPosition = trail.Points[i];
        }
        Debug.Log($"[MapTrailBuilder] {trail.Name} 길 방향 경사 : 계획 최대 {planned:F1}도 (계획 {PlanMaxSlope}도, 고정점 사이 {PinMaxSlope}도), " +
                  $"고친 지형 최대 {measured:F1}도 ({measuredPosition.x:F0}, {measuredPosition.y:F0}), {TrailMaxSlope}도 초과 점 {overPoints.Count}개 (0이어야 함, 대표 : {overPoints.ToText("도")}). " +
                  $"고정점끼리 경사 제한 충돌 {trail.PinConflictCount}개");
    }

    // 여울을 뺀 경로 점이 강 물 위, 호수 수면 메시 위에 있는지 보고, 여울에서 물을 건너는 길이를 잰다
    private static void LogTrailWater(TerrainGrid grid, Trail trail)
    {
        int riverCount = 0;
        int lakeCount = 0;
        int fordWaterCount = 0;
        float minRiverDistance = float.MaxValue;
        for (int i = 0; i < trail.Points.Count; i++)
        {
            Vector2Int cell = GetNearestCell(grid, trail.Points[i]);
            float riverDistance = grid.RiverDistances.Values[cell.y, cell.x];
            bool isOverRiver = riverDistance < MapRiverBuilder.HalfWidth;
            if (i >= trail.FordStart && i <= trail.FordEnd)
            {
                if (isOverRiver) fordWaterCount++;
                continue;
            }

            minRiverDistance = Mathf.Min(minRiverDistance, riverDistance);
            if (isOverRiver) riverCount++;
            if (grid.LakeDistances.Values[cell.y, cell.x] < grid.Spacing) lakeCount++;
        }

        string riverText = minRiverDistance >= RiverKeepDistance ? $"{RiverKeepDistance}m 이상" : $"{minRiverDistance:F1}m";
        Debug.Log($"[MapTrailBuilder] {trail.Name} 물 침범 : 강 물 위(여울 제외) {riverCount}점, 호수 수면 메시 위 {lakeCount}점 (둘 다 0이어야 함). " +
                  $"여울 밖 강 중심선까지 최소 {riverText}");
        if (trail.FordStart >= 0)
        {
            Vector2 start = trail.Points[trail.FordStart];
            Debug.Log($"[MapTrailBuilder] {trail.Name} 여울 구간 ({start.x:F0}, {start.y:F0})부터 {trail.FordEnd - trail.FordStart}점, " +
                      $"그중 강 물 안 {fordWaterCount}점 (약 {fordWaterCount * PointSpacing:F0}m, 강바닥을 그대로 건넘)");
        }
    }

    private static void LogWaypointDistances(Trail trail)
    {
        string text = string.Join(", ", trail.RuneWaypoints.Select(waypoint =>
            $"{waypoint.Key} ({waypoint.Value.x:F0}, {waypoint.Value.y:F0}) {GetDistanceToPath(trail.Points, waypoint.Value):F1}m"));
        Debug.Log($"[MapTrailBuilder] {trail.Name} 룬 돌 자리(경유점)까지 최소 거리 : {text}");
    }

    // 같은 길에서 경로 거리 SelfApproachArc보다 떨어진 두 점(지그재그 다리) 사이의 최소 거리와, (길 폭 + SelfApproachGap)보다 가까운 점 수
    private static void LogSelfSpacing(Trail trail)
    {
        float limit = HalfWidth * 2f + SelfApproachGap;
        float minDistance = float.MaxValue;
        Vector2 minPosition = Vector2.zero;
        int closeCount = 0;
        for (int i = 0; i < trail.Points.Count; i++)
        {
            bool isClose = false;
            for (int j = i + 1; j < trail.Points.Count; j++)
            {
                if (trail.Distances[j] - trail.Distances[i] <= SelfApproachArc) continue;

                float distance = Vector2.Distance(trail.Points[i], trail.Points[j]);
                if (distance < limit) isClose = true;
                if (distance >= minDistance) continue;

                minDistance = distance;
                minPosition = trail.Points[i];
            }
            if (isClose) closeCount++;
        }
        string minText = minDistance < float.MaxValue ? $"{minDistance:F1}m ({minPosition.x:F0}, {minPosition.y:F0})" : "해당 없음";
        Debug.Log($"[MapTrailBuilder] {trail.Name} 다리 사이(경로 거리 {SelfApproachArc}m 초과) 최소 거리 {minText}, {limit}m보다 가까운 점 {closeCount}개");
    }

    private static void LogCarveWidths(Trail trail)
    {
        int widest = 0;
        for (int i = 1; i < trail.CarveWidths.Length; i++)
        {
            if (trail.CarveWidths[i] > trail.CarveWidths[widest]) widest = i;
        }
        int widenedCount = trail.CarveWidths.Count(width => width > CarveHalfWidth);
        Debug.Log($"[MapTrailBuilder] {trail.Name} 고치는 범위 : 기본 {CarveHalfWidth}m, 넓힌 점 {widenedCount}개 / {trail.CarveWidths.Length}개, " +
                  $"최대 {trail.CarveWidths[widest]:F1}m ({trail.Points[widest].x:F0}, {trail.Points[widest].y:F0}). " +
                  $"상한 {MaxCarveHalfWidth}m 안에서 땅과 만나지 못해 옆 비탈을 가파르게 한 점 {trail.SteepPoints.Count}개 " +
                  $"(그중 {MaxSteepCutSlope}도에서 자른 점 {trail.CappedSteepCount}개, 대표 위치와 옆 비탈 각도 : {trail.SteepPoints.ToText("도")})");
    }

    // 두 길 점 사이 최소 거리. 거점 둘레(BaseAreaRadius 안)는 두 길이 함께 시작하는 곳이라 빼고도 잰다
    private static void LogTrailSpacing(Trail first, Trail second)
    {
        float minDistance = float.MaxValue;
        Vector2 minPosition = Vector2.zero;
        float outsideMin = float.MaxValue;
        Vector2 outsidePosition = Vector2.zero;
        foreach (Vector2 a in first.Points)
        {
            foreach (Vector2 b in second.Points)
            {
                float distance = Vector2.Distance(a, b);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    minPosition = a;
                }
                if (a.magnitude < BaseAreaRadius || b.magnitude < BaseAreaRadius || distance >= outsideMin) continue;

                outsideMin = distance;
                outsidePosition = a;
            }
        }
        string outsideText = outsideMin < float.MaxValue ? $"{outsideMin:F1}m ({outsidePosition.x:F0}, {outsidePosition.y:F0})" : "해당 없음";
        Debug.Log($"[MapTrailBuilder] 두 길 사이 최소 거리 {minDistance:F1}m ({minPosition.x:F0}, {minPosition.y:F0}), " +
                  $"거점 둘레 {BaseAreaRadius}m 밖 {outsideText} (탐색이 피하는 거리 {OverlapKeepDistance}m)");
    }

    private static void LogCarve(TerrainGrid grid, CarveField field, CarveStats stats)
    {
        Debug.Log($"[MapTrailBuilder] 지형 바뀐 칸 {stats.ChangedCount}개, 최대 깎음 -{stats.MaxCut:F2}m ({stats.MaxCutPosition.x:F0}, {stats.MaxCutPosition.y:F0}), " +
                  $"최대 메움 +{stats.MaxFill:F2}m ({stats.MaxFillPosition.x:F0}, {stats.MaxFillPosition.y:F0})");
        Debug.Log($"[MapTrailBuilder] 고치는 범위 끝(점마다 범위 끝에서 {EdgeCheckWidth}m 안) 최대 변화 {stats.MaxEdgeChange:F2}m " +
                  $"({stats.MaxEdgeChangePosition.x:F0}, {stats.MaxEdgeChangePosition.y:F0}). 0에 가까워야 범위 끝에 단차가 없다");
        Debug.Log($"[MapTrailBuilder] 옆 비탈 제한을 지킬 수 없는 칸 : 길 바닥 {stats.BedConflicts.Count}개 (대표 : {stats.BedConflicts.ToText("m")}), " +
                  $"옆 비탈 {stats.SideConflicts.Count}개 (대표 : {stats.SideConflicts.ToText("m")}). 값은 위, 아래 한계가 엇갈린 높이 (0개여야 함)");
        LogSideSlopes(grid, field);
        LogProtected(grid, field);
    }

    // 고치는 범위 안(길 바닥 제외)의 경사. 기슭(FoothillMaxHeight 미만)은 길 때문에 35도를 넘게 된 칸을 센다
    private static void LogSideSlopes(TerrainGrid grid, CarveField field)
    {
        float maxSlope = 0f;
        Vector2 maxPosition = Vector2.zero;
        float maxFoothillSlope = 0f;
        var steeperCells = new Representatives();
        for (int z = 1; z < grid.Heights.GetLength(0) - 1; z++)
        {
            for (int x = 1; x < grid.Heights.GetLength(1) - 1; x++)
            {
                if (!field.IsInZone[z, x] || field.IsBed[z, x]) continue;

                float slope = GetCellSlope(grid.Heights, z, x, grid.Spacing);
                if (slope > maxSlope)
                {
                    maxSlope = slope;
                    maxPosition = GetCellWorld(grid, x, z);
                }
                if (grid.Heights[z, x] >= FoothillMaxHeight) continue;

                maxFoothillSlope = Mathf.Max(maxFoothillSlope, slope);
                bool isSteeper = slope > GetCellSlope(grid.Original, z, x, grid.Spacing) + SlopeCheckTolerance;
                if (slope > FoothillSideMaxSlope + SlopeCheckTolerance && isSteeper) steeperCells.Add(GetCellWorld(grid, x, z), slope);
            }
        }
        Debug.Log($"[MapTrailBuilder] 길 옆(고치는 범위 안, 길 바닥 제외) 최대 경사 {maxSlope:F1}도 ({maxPosition.x:F0}, {maxPosition.y:F0}). " +
                  $"기슭(높이 {FoothillMaxHeight}m 미만) 최대 {maxFoothillSlope:F2}도, 길 때문에 {FoothillSideMaxSlope}도를 넘게 된 칸 {steeperCells.Count}개 " +
                  $"(대표 : {steeperCells.ToText("도")}). 옆 비탈을 가파르게 한 점(위 고치는 범위 로그) 둘레에서 나올 수 있다");
    }

    // 호수 물턱, 강 둘레(고정 칸)에서 바뀐 칸 수. ApplyCarve가 고정 칸을 건너뛰므로 0이 나와야 한다
    private static void LogProtected(TerrainGrid grid, CarveField field)
    {
        int changedCount = 0;
        for (int z = 0; z < grid.Heights.GetLength(0); z++)
        {
            for (int x = 0; x < grid.Heights.GetLength(1); x++)
            {
                if (IsProtectedCell(grid, z, x) && grid.Heights[z, x] != grid.Original[z, x]) changedCount++;
            }
        }
        int protectedInZone = 0;
        foreach (bool isProtected in field.IsProtected)
        {
            if (isProtected) protectedInZone++;
        }
        Debug.Log($"[MapTrailBuilder] 호수 물턱(수면 메시에서 {LakeProtectDistance}m), 강 둘레(중심선에서 {RiverProtectDistance}m) 중 길 범위에 든 칸 {protectedInZone}개, " +
                  $"그중 바뀐 칸 {changedCount}개 (0이어야 함). 경로, 경유점 저장 : {TrailDataPath}");
    }
}
