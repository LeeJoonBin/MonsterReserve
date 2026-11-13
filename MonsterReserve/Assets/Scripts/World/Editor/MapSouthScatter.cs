using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 메뉴 18 : 남쪽 구역(B3, 강가 유적 도시)에 서식지 위치 표시, 유적 랜드마크 3곳, 큰 유적 도시, 나무, 바위, 흩어진 유적 조각을 놓는다 (이슈 #11).
// 1) 서식지 : South/Habitat를 HabitatCenter의 지면 높이에 둔다. 위치 표시만 하고, 반경 HabitatRadius 안은 빈터로 비운다.
// 2) 랜드마크 : 언덕 위 감시탑 터, 무너진 다리 터, 강가 선착장 터를 Dreamscape prefab으로 직접 구성한다 (남서쪽 메뉴 15와 같은 조립, 높이 맞춤 방식).
//    감시탑 터는 언덕 꼭대기 둘레에서 묻히는 조각이 없는 자리와 방향을, 다리 터는 강이 곧은 곳을, 선착장 터는 강 한쪽 물가를 찾는다.
// 3) 유적 도시 : CityRect(B3 경계 안쪽 CityEdgeMargin, z CityZMin ~ CityZMax) 안, 강 보호 구역과 서식지를 뺀 곳.
//    강 양쪽에 엇갈린 시가지 2개(강 서쪽은 북쪽 범위의 강가 평지에 구역 4곳, 강 동쪽은 남쪽 범위에 구역 2곳)를 두고, 구역(구역마다 조금씩 돌린 방향)을 남북으로 틈 없이 붙인다.
//    시가지 안 이웃 구역은 z로 정확히 한 구역 폭(DistrictGridSize × LotSpacing)만큼, x로 DistrictMaxShiftX 안에서 어긋난다.
//    시가지 자리는 구역들의 쓸 수 있는 칸 합이 가장 많은 곳을 고르고, 구역마다 3 × 3 거리 격자를 두어 칸마다 건물 터(작은 집, 큰 집, 탑 터, 기둥 회랑)를 놓는다.
//    도시 가운데에 가까운 두 구역에는 광장을 하나씩 둔다. 무너진 칸과 놓지 못한 칸은 빈 칸으로 두고 흩어진 조각을 모아 둔다.
//    도시 사각형 둘레 네 변에 담장 줄 자리를 두되, 줄마다 일정 확률로 생략하고, 남는 줄도 이어진 일부 조각만 남기며 위치와 방향을 흔들어 끊어진 담장으로 보이게 한다.
//    줄 자리가 가파르면 도시 안쪽으로 옮겨 조각 바닥이 모두 완만한 첫 자리에 놓는다.
// 4) 나무, 바위, 유적 조각 : 도시 밖은 새 노이즈로 빈터 / 드문드문 / 나무 무리를 나눈 규칙 그대로, 도시 안은 나무·바위를 줄이고 유적 조각을 늘린다.
//    유적 조각은 구분별 목표 개수를 정해 놓고, 놓기 전에 묻힘을 미리 재서 지면 기울기에 맞춰 눕혀도 묻히는 자리는 다른 위치·회전·칸으로 다시 시도한다.
//    강 보호 구역(MapSouthShaper.RiverProtectDistance), 서식지 반경, 랜드마크 둘레, 도시 구조물 둘레는 비운다. 다리 받침과 선착장 터만 강 보호 구역 안에 둔다 (물 위는 비움).
// 유적 조각은 모두 원래 크기의 RuinScale배(± PieceScaleJitter)이고, 배치도 간격도 그 크기에 맞춘 값이다. 배와 노는 원래 크기다.
// Prefab_WallRuin은 LODGroup 경고를 내므로 쓰지 않는다 (MapLayout.md "배치할 때 확인할 것").
// 다시 실행하면 South 아래 Trees, Rocks, Landmarks, Habitat의 자식을 지우고 같은 seed로 다시 만든 뒤 씬을 저장한다. South/River는 건드리지 않는다.
// 실행 순서 : ... → 17 → 18 → 19 → 10. 17(남쪽 지형)을 다시 실행하면 지형 높이가 바뀌므로 18도 다시 실행하고,
// 18을 다시 실행하면 19도 다시 실행한다. 지면·바닥 식생(메뉴 19)은 GetSouthZone, IsInCityArea로 같은 구분을 쓴다.
public static class MapSouthScatter
{
    private enum TreeKind
    {
        Main = 0,
        Dead = 1,
        Giant = 2
    }

    // 랜드마크 자리를 정하는 방식
    private enum LandmarkPlacement
    {
        Hilltop = 0,
        RiverBridge = 1,
        RiverBank = 2
    }

    // 시가지 자리 후보의 쓸 수 있는 칸 합이 같을 때 고르는 규칙
    private enum SettlementTieBreak
    {
        // 맨 위 구역이 더 북쪽인 자리
        North = 0,
        // 쓸 수 있는 칸의 바닥 높이 차 합이 더 작은 자리
        Flatter = 1
    }

    // 도시 칸에 놓는 건물 터 형태 (LotKindLabels와 같은 순서)
    private enum LotKind
    {
        SmallHouse = 0,
        LargeHouse = 1,
        Tower = 2,
        Colonnade = 3
    }

    // 도시 구조물(건물 터, 광장, 담장 조각)을 놓지 못한 이유 (SiteFailureLabels와 같은 순서)
    private enum SiteFailure
    {
        None = 0,
        Outside = 1,
        Overlap = 2,
        River = 3,
        Habitat = 4,
        Slope = 5,
        Broken = 6
    }

    // 흩뿌리기를 건너뛴 이유
    private enum SkipReason
    {
        None = 0,
        Habitat = 1,
        River = 2,
        Landmark = 3,
        Slope = 4
    }

    // 먼저 놓은 랜드마크, 도시 구조물이 차지한 자리 (월드 XZ)
    private struct Blocker
    {
        public Vector2 Center;
        public float Radius;
    }

    // 같은 크기 범위로 같은 부모 아래 놓는 prefab 묶음
    private class InstanceGroup
    {
        public GameObject[] Prefabs;
        public Transform Parent;
        public float ScaleMin;
        public float ScaleMax;
        // 바위는 바닥 둘레의 가장 낮은 땅에 맞춰 비탈 아래쪽이 뜨지 않게 한다
        public bool IsRock;
    }

    // 유적 조각에 한 처리 개수
    private class InstanceStats
    {
        public int ColliderCount;
        public int DisallowCount;
        // 분홍(셰이더 오류, 머티리얼 없음) 렌더러 수 + 렌더러가 없는 조각 수
        public int BrokenMaterialCount;
    }

    // GPU Resident Drawer 제외 판정에 함께 쓰는 값
    private class RenderSetup
    {
        public System.Type DisallowType;
        public Dictionary<Shader, bool> GpuDrivenSupport = new Dictionary<Shader, bool>();
    }

    // 흩어진 유적 조각 종류별 결과. 묻힘 원인(어떤 조각이 어떤 경사에서)을 로그로 확인하는 용도
    private class DebrisPrefabStats
    {
        public int PlacedCount;
        // 바닥 기울이기를 해도 묻혀서 버린 시도 수
        public int RejectedCount;
        // 놓은 조각 중, 바닥을 기울이지 않고 세웠다면 묻힘으로 판정됐을 수와 그 자리의 가장 큰 경사
        public int UprightBuriedCount;
        public float UprightBuriedMaxSlope;
        // 조각 자기 위쪽 축 기준 두께 (m, 크기 흔들림 포함)
        public float ThicknessMin = float.MaxValue;
        public float ThicknessMax;
    }

    // 유적 조각 하나를 지금 회전으로 땅에 맞췄을 때의 값. 거리는 조각 위쪽 축(바닥 평면의 법선) 방향으로 잰다
    private struct DebrisFit
    {
        // 월드 위쪽으로 옮길 거리 (m)
        public float Lift;
        public float Thickness;
        public bool IsBuried;
    }

    // 한 번 실행하는 동안 함께 쓰는 값과 결과 개수 묶음. 묶음 배열의 순서는 CategoryLabels (빈터, 드문드문, 나무 무리, 도시)
    private class ScatterContext
    {
        public Terrain Terrain;
        public System.Random Random;
        // 유적 조각은 따로 뽑아, 조각 규칙을 바꿔도 나무·바위 배치가 바뀌지 않게 한다
        public System.Random DebrisRandom;
        public InstanceGroup[] TreeGroups;
        public InstanceGroup RockGroup;
        public GameObject[] DebrisPrefabs;
        public RenderSetup Render;
        // 랜드마크 둘레와 도시 구조물 둘레. 나무, 바위, 유적 조각을 두지 않는다
        public List<Blocker> Blockers;
        // 무너진 칸, 놓지 못한 칸의 중심. 터 대신 흩어진 조각을 모아 둔다
        public List<Vector2> EmptyLots;
        public int[] CellCounts = new int[CategoryCount];
        // 칸 중심이 강 보호 구역이나 서식지 안이라 아무것도 놓지 않은 칸 (면적 비율에서는 빈터로 친다)
        public int ClearedCellCount;
        public int[] TreeCounts = new int[CategoryCount];
        public int[] RockCounts = new int[CategoryCount];
        public int[] DebrisCounts = new int[CategoryCount];
        public int[] TreeKindCounts = new int[3];
        // 묶음별 배치 칸의 왼쪽 아래 꼭짓점. 유적 조각을 묶음별 목표 개수만큼 이 칸들에서 뽑아 놓는다
        public List<Vector2>[] Cells = Enumerable.Range(0, CategoryCount).Select(_ => new List<Vector2>()).ToArray();
        public int[] DebrisTargets = new int[CategoryCount];
        public int EmptyLotDebrisCount;
        // Rocks 아래 유적 조각. 뜸 검사를 피벗이 아니라 메시 정점으로 하므로 따로 기억한다
        public HashSet<Transform> Debris = new HashSet<Transform>();
        public InstanceStats DebrisStats = new InstanceStats();
        public Dictionary<string, DebrisPrefabStats> DebrisResults = new Dictionary<string, DebrisPrefabStats>();
        // 놓은 뒤 다시 잰 값으로 뜨거나 묻힌 유적 조각 수 (0이어야 한다)
        public int DebrisBrokenCount;
        // 유적 조각 시도 중 서식지, 강 보호 구역, 랜드마크·구조물 둘레, 급경사에 걸린 수와 끝내 놓지 못한 뽑기 수
        public int DebrisBlockedCount;
        public int DebrisFailedDrawCount;
        public int ColliderCount;
        public int HabitatSkipCount;
        public int RiverSkipCount;
        public int LandmarkSkipCount;
        public int SlopeSkipCount;
    }

    // 랜드마크·도시 조각 하나의 배치도 (묶음 중심, 묶음 회전 기준). 크기는 유적이면 RuinScale배(± 흔들림), 소품이면 원래 크기
    private struct PieceSpec
    {
        // MapTreeScatter.PrefabFolder 아래 경로 (확장자 없음)
        public string Path;
        // 묶음 중심 기준 XZ (m, 1.5배 크기에 맞춘 값)
        public Vector2 Offset;
        public float Yaw;
        // true면 고정 seed로 조금 기울인다 (기둥)
        public bool IsWobbly;
        // true면 유적이 아닌 소품 (배, 노). 크기를 키우거나 흔들지 않는다
        public bool IsProp;
    }

    private class LandmarkSpec
    {
        public string Label;
        // 구성안의 대략 바닥 반지름. 로그에서 실제 바닥 반지름과 견주는 용도
        public float ApproxRadius;
        public LandmarkPlacement Placement;
        public PieceSpec[] Pieces;
    }

    // 놓은 조각 하나
    private class PlacedPiece
    {
        public Transform Transform;
        // 묶음 중심 기준 렌더러 범위 바닥의 가운데와 둘레 8곳 (XZ). 자리 찾기에서 지면 높이를 잴 곳
        public Vector2[] Points;
        public float Height;
    }

    // 원점에서 조립한 묶음 하나 (랜드마크, 건물 터, 광장, 담장 줄). 자리를 찾은 뒤 통째로 옮긴다
    private class LandmarkBuild
    {
        public Transform Group;
        public List<PlacedPiece> Pieces = new List<PlacedPiece>();
        public int InstanceCount;
        // 중심에서 가장 먼 렌더러 범위 가장자리까지의 수평 거리
        public float Radius;
        public InstanceStats Stats = new InstanceStats();
    }

    private struct LandmarkFit
    {
        public Vector2 Position;
        // 땅에 맞추면 많이 묻히는 조각 수
        public int BrokenCount;
        // 묶음 바닥 아래 지면의 가장 높은 곳 - 가장 낮은 곳
        public float GroundRange;
        // 회전도 함께 고른 경우의 묶음 회전 (도)
        public float GroupYaw;
        // 다리 터, 선착장 터 : 자리 앞뒤 강 방향이 꺾인 각도 (도). 작을수록 곧다
        public float Bend;
    }

    // 도시 구역 하나. 3 × 3 격자 칸 중심과, 자리를 고를 때 잰 칸마다의 쓸 수 있는지 여부
    private class District
    {
        public int Index;
        public Vector2 Center;
        public float Yaw;
        public List<Vector2> Lots;
        public bool[] IsLotValid;
        public int ValidCount;
        // 칸마다 칸 가운데와 둘레 8곳의 지면 높이 차 (최고 - 최저, m). 쓸 수 없는 칸은 재지 않아 0이다
        public float[] LotGroundRanges;
        public Transform Group;
        // 광장을 놓은 칸 번호 (없으면 -1)
        public int PlazaLot = -1;
    }

    // 시가지 하나 (강 한쪽에 남북으로 붙인 구역 묶음)
    private class SettlementSpec
    {
        public string Label;
        public bool IsWest;
        // 시가지에 속한 모든 구역 중심이 이 z 범위 안이어야 한다 (CityRect와 겹치는 부분만 쓴다)
        public float ZMin;
        public float ZMax;
        // 북 → 남 구역 순서의 묶음 회전 (도). 길이가 시가지의 구역 수다
        public float[] Yaws;
        // 칸 수 합이 같을 때 고르는 규칙
        public SettlementTieBreak TieBreak;
    }

    // 시가지 자리를 찾는 동안 함께 쓰는 값. 구역 중심은 x = CityRect.xMin + 열 × DistrictSearchStep, 맨 위 구역 z = zMax - 행 × DistrictSearchStep
    private class SettlementSearch
    {
        public LandmarkContext Context;
        // 먼저 놓은 다른 시가지의 구역
        public List<District> Placed;
        public SettlementSpec Spec;
        public int FirstIndex;
        public int Columns;
        public int ShiftSteps;
        // 구역 순서마다 (열, 맨 위 구역 행) → 잰 구역 (강 반대쪽이면 null). 같은 자리를 여러 조합에서 다시 재지 않게 둔다
        public Dictionary<(int, int), District>[] Measured;
        // 지금 살펴보는 조합과, 같은 시가지 위 구역과의 겹침까지 뺀 칸
        public District[] Chain;
        public bool[][] ChainValid;
        public District[] Best;
        public bool[][] BestValid;
        public int BestCount = -1;
        public float BestTop;
        // 고른 조합의 쓸 수 있는 칸 바닥 높이 차 합 (m, TieBreak가 Flatter일 때만 잰다)
        public float BestGroundRange;
    }

    // 담장 변 하나 (From → To)
    private struct WallEdge
    {
        public Vector2 From;
        public Vector2 To;
    }

    // 담장 줄 하나. WallRunPieces 중 FirstPiece번부터 PieceCount개만 놓는다
    private struct WallRun
    {
        public Vector2 Center;
        public float Yaw;
        public int FirstPiece;
        public int PieceCount;
        // 변과 수직인 도시 안쪽 방향 (단위 벡터)과 변 번호 (WallSideLabels 순서)
        public Vector2 Inward;
        public int Side;
    }

    // 도시 결과 (로그용)
    private class CityStats
    {
        public int[] BuildingCounts = new int[LotKindLabels.Length];
        public int PlazaCount;
        // 구역 자리를 고를 때 조건(도시 범위, 강, 서식지, 랜드마크, 경사)을 못 맞춘 칸
        public int SearchInvalidLots;
        public int CollapsedLots;
        // 건물 터를 놓지 못한 이유별 칸 수 (SiteFailure 순서)
        public int[] LotFailures = new int[SiteFailureLabels.Length];
        public int[] PlazaFailures = new int[SiteFailureLabels.Length];
        public int WallRunCount;
        // 확률로 생략한 줄 수
        public int WallRunSkippedCount;
        public int WallRunDroppedCount;
        // 안쪽으로 WallInwardMax까지 옮겨도 경사 때문에 자리가 없어 놓지 않은 줄 수
        public int WallRunNoSiteCount;
        // 변별 놓은 줄 수 (WallSideLabels 순서)와 놓은 줄의 안쪽으로 옮긴 거리
        public int[] WallRunsBySide = new int[WallSideLabels.Length];
        public float WallShiftSum;
        public float WallShiftMin = float.MaxValue;
        public float WallShiftMax;
        public float WallLength;
        public float WallLengthMin = float.MaxValue;
        public float WallLengthMax;
        // 담장 줄에서 뺀 조각 수 (SiteFailure 순서)
        public int[] WallRemoved = new int[SiteFailureLabels.Length];
        public int PieceCount;
        public int BrokenCount;
        public InstanceStats Stats = new InstanceStats();
        public List<string> DistrictNotes = new List<string>();
    }

    private class LandmarkContext
    {
        public Terrain Terrain;
        public RenderSetup Render;
        public Transform Parent;
        // 랜드마크·도시 조각의 크기, 기울기 흔들림
        public System.Random Random;
        // 도시 칸의 형태, 무너짐, 담장 줄 방향 흔들림
        public System.Random CityRandom;
        public Vector2 Hilltop;
        // 랜드마크 3곳 (바닥 반지름 + LandmarkClearance)
        public List<Blocker> Placed = new List<Blocker>();
        // 도시 구조물 (건물 터, 광장은 바닥 반지름, 담장은 조각마다 + CityClearance)
        public List<Blocker> CityBlockers = new List<Blocker>();
        public List<Vector2> EmptyLots = new List<Vector2>();
        // 놓은 랜드마크·도시 조각 전부 (검수 로그에서 다시 읽는다)
        public List<Transform> AllPieces = new List<Transform>();
        // 다리 받침과 선착장 터는 강 보호 구역 검사에서 따로 센다
        public Transform BridgeGroup;
        public Transform PierGroup;
        public int BrokenCount;
        public int PieceCount;
        public InstanceStats Stats = new InstanceStats();
        public List<string> Notes = new List<string>();
        public CityStats City = new CityStats();
    }

    private const string SouthCell = "B3";
    private const string SouthRegionName = "South";
    private const string TreeParentName = "Trees";
    private const string RockParentName = "Rocks";
    private const string LandmarkParentName = "Landmarks";
    private const string HabitatName = "Habitat";
    private const string CityParentName = "City";
    private const string WallParentName = "Walls";
    // 쓰지 않는 prefab. 로그에서 0개인지 센다
    private const string WallRuinName = "Prefab_WallRuin";

    // 서식지 (사용자 승인 임시값). 거점(원점) 기준 남쪽. 반경 안은 빈터로 두고 나무, 바위, 유적을 놓지 않는다
    public static readonly Vector2 HabitatCenter = new Vector2(-120f, -850f);
    public const float HabitatRadius = 80f;
    // 거점 기준 "남" 방위 범위 (도). 로그에서 서식지 방위가 이 안인지 본다
    private const float SouthAzimuthMin = 157.5f;
    private const float SouthAzimuthMax = 202.5f;

    // 강 경로 중 B3를 이만큼 넓힌 범위 안 구간만 거리 계산에 쓴다 (계산량 줄이기)
    private const float RiverPathMargin = 100f;

    // 언덕 꼭대기를 찾을 때 지면 높이를 재는 간격 (m). 언덕 범위는 MapSouthShaper의 언덕 판정을 따른다
    private const float HilltopSearchStep = 2f;
    // 비틀린 언덕 윤곽이 언덕 중심에서 HillRadius보다 멀리 나갈 수 있어, 꼭대기를 찾는 사각형은 이 배율만큼 넓게 잡고 언덕 판정으로 거른다
    private const float HilltopSearchRangeRatio = 2f;
    // 감시탑 터 자리 맞춤 : 꼭대기에서 이 반지름 안의 자리와 묶음 회전 각도를 돌며 (1) 묻히는 조각 (2) 바닥 아래 지면 높이 차가 가장 작은 것을 고른다
    private const float HilltopFitRadius = 10f;
    private const float HilltopFitStep = 1f;
    private const float HilltopYawStep = 15f;

    // 다리 터 : 강 중심선 위 점마다 앞뒤 BridgeStraightDistance 지점을 이어 강 방향과 꺾인 각도를 잰다.
    // 다리 받침 조각의 바닥 표본점이 모두 강 중심에서 BridgeMinRiverDistance ~ BridgeMaxRiverDistance 안인 자리 중
    // (1) 묻히는 조각 (2) 꺾인 각도 (3) 바닥 아래 지면 높이 차가 가장 작은 곳에 놓는다. 묶음의 x축이 강을 가로지른다
    private const float BridgeStraightDistance = 20f;
    private const float BridgeMinRiverDistance = 8f;
    private const float BridgeMaxRiverDistance = 18f;

    // 선착장 터 : 강 중심선 위 점마다 양쪽 물가에서 강 중심으로부터 PierBankOffset 떨어진 곳을 묶음 중심으로 놓아 본다.
    // 묶음의 -x가 강 쪽, z가 강 방향이다. 조각 바닥 표본점이 모두 강 중심에서 PierMinRiverDistance ~ PierMaxRiverDistance 안인 자리 중
    // 다리 터와 같은 순서((1) 묻힘 (2) 꺾인 각도 (3) 지면 높이 차)로 고른다
    private const float PierBankOffset = 14f;
    private const float PierMinRiverDistance = 8f;
    private const float PierMaxRiverDistance = 20f;

    // 앞 메뉴들의 seed(20261109 ~ 20261141)와 겹치지 않게 둔다
    private const int ZoneSeed = 20261142;
    private const int ScatterSeed = 20261143;
    private const int LandmarkSeed = 20261144;
    private const int DebrisSeed = 20261145;
    // 메뉴 19의 seed(20261146) 다음 값
    private const int CitySeed = 20261147;

    // 구분 마스크 (사용자 승인 비율). 노이즈 하위 40% = 빈터, 다음 40% = 드문드문, 상위 20% = 나무 무리.
    // 경계는 도시와 상관없이 B3 전체로 정해, 도시 밖 무늬가 도시를 만들기 전과 같게 둔다 (사용자 승인)
    private const float ZoneNoisePeriod = 150f;
    private const float OpenAreaRatio = 0.40f;
    private const float ForestAreaRatio = 0.20f;
    // 분위수를 구할 때 B3를 표본 조사하는 간격 (m)
    private const float ZoneSampleSpacing = 5f;
    // Perlin 입력이 음수가 되지 않도록 노이즈 위치를 양수 쪽으로 옮기는 범위
    private const float NoiseOffsetMin = 100f;
    private const float NoiseOffsetMax = 1000f;
    private static readonly Vector2 ZoneNoiseOffset = CreateZoneNoiseOffset();

    // 흩뿌리기 단위 칸 (10m × 10m)과 1ha 넓이 (MapTreeScatter와 같은 단위)
    private const float ScatterCellSize = 10f;
    private const float HectareArea = 10000f;

    // 밀도 묶음 : 0 ~ 2는 MapTreeScatter.FieldZone (도시 밖), 3은 도시 안
    private const int CategoryCount = 4;
    private const int CityCategory = 3;
    private static readonly string[] CategoryLabels = { "빈터", "드문드문", "나무 무리", "도시" };
    // 밀도 표 (1ha당 최소, 최대, 사용자 승인 임시값). 도시 안은 나무 0 ~ 3, 바위 1 ~ 2로 줄인다
    private static readonly float[] TreeDensityMin = { 0f, 5f, 60f, 0f };
    private static readonly float[] TreeDensityMax = { 0f, 10f, 100f, 3f };
    private static readonly float[] RockDensityMin = { 2f, 2f, 5f, 1f };
    private static readonly float[] RockDensityMax = { 4f, 4f, 10f, 2f };
    // 흩어진 유적 조각 : 도시 밖 3 ~ 5, 도시 안 8 ~ 10 (사용자 승인). 칸마다 개수를 굴리면 넓이가 작은 묶음에서 표 밖으로 벗어나
    // 묶음별로 표 가운데 값(목표 밀도) × 넓이만큼을 정해 두고, 놓지 못한 것은 같은 묶음의 다른 칸에서 다시 뽑아 채운다
    private static readonly float[] DebrisDensityMin = { 3f, 3f, 3f, 8f };
    private static readonly float[] DebrisDensityMax = { 5f, 5f, 5f, 10f };
    private static readonly float[] DebrisTargetDensity = { 4f, 4f, 4f, 9f };
    // 목표 1개당 칸을 뽑는 최대 횟수. 넘으면 목표보다 적게 놓고 로그에 남긴다
    private const int DebrisMaxDrawsPerPiece = 10;
    // 뽑은 칸 하나에서 위치, 회전을 바꿔 다시 시도하는 횟수
    private const int DebrisPlaceAttempts = 4;
    // 바닥 넓은 조각(계단 벽돌, 기둥 받침)이 비탈에서 한쪽이 묻히지 않도록 지면 기울기에 맞춰 눕히는 최대 각도 (임시값).
    // 지면이 이보다 가파르면 이 각도까지만 눕히고 나머지는 묻힘 판정에 맡긴다
    private const float DebrisMaxAlignTilt = 20f;
    // 빈 칸 하나에 모아 두는 흩어진 조각 수와, 그 조각을 뿌리는 정사각형 한 변 (m)
    private const int EmptyLotDebrisCount = 4;
    private const float EmptyLotSpread = 16f;

    // ---------- 유적 조각 prefab ----------
    private const string WallLeftPath = "Ruins/Prefab_ Wall_Left";
    private const string WallRightPath = "Ruins/Prefab_ Wall_Right";
    private const string WallMidPath = "Ruins/Prefab_Wall_Mid";
    private const string DoorFramePath = "Ruins/Prefab_ DoorFrame_WallBase";
    private const string DoorArchPath = "Ruins/Prefab_DoorArch_Broken";
    private const string StairsPath = "Ruins/Prefab_Stairs";
    private const string PillarFullPath = "Ruins/Prefab_PillarFull";
    private const string PillarHalfFullPath = "Ruins/Prefab_PillarHalf_Full";
    private const string PillarBrokenPath = "Ruins/Prefab_ PillarBroken";
    private const string PillarHalfBroken01Path = "Ruins/Prefab_ PillarHalf_Broken_01";
    private const string PillarHalfBroken02Path = "Ruins/Prefab_ PillarHalf_Broken_02";
    private const string BaseRoundPath = "Ruins/Prefab_ PillarBase_Round";
    private const string BaseSquarePath = "Ruins/Prefab_ PillarBase_Square";
    private const string BaseSquareSmallPath = "Ruins/Prefab_ PillarBase_SquareSmall";
    private const string Brick01Path = "Ruins/Prefab_Stairs_Brick_01";
    private const string Brick02Path = "Ruins/Prefab_Stairs_Brick_02";
    private const string Brick03Path = "Ruins/Prefab_Stairs_Brick_03";
    private const string BoatPath = "Objects/Prefab_Boat";
    private const string PaddlePath = "Objects/Prefab_BoatPaddle";
    private static readonly string[] BrickPaths = { Brick01Path, Brick02Path, Brick03Path };
    private static readonly string[] BasePaths = { BaseRoundPath, BaseSquarePath, BaseSquareSmallPath };
    private static readonly string[] BrokenPillarPaths = { PillarBrokenPath, PillarHalfBroken01Path, PillarHalfBroken02Path };
    // 큰 집 안쪽 기둥 : 반쪽 기둥과 부러진 기둥을 섞는다
    private static readonly string[] InnerPillarPaths = { PillarHalfFullPath, PillarBrokenPath, PillarHalfBroken01Path, PillarHalfBroken02Path };

    // 흩어진 유적 조각 : 부서진 기둥, 기둥 받침, 계단 벽돌 조각
    private static readonly string[] DebrisPaths =
    {
        PillarBrokenPath, PillarHalfBroken01Path, PillarHalfBroken02Path,
        BaseRoundPath, BaseSquarePath, BaseSquareSmallPath,
        Brick01Path, Brick02Path, Brick03Path
    };

    // 바위 피벗을 이 반지름(× 배율) 안의 가장 낮은 땅에 맞춘다 (남서쪽 배치와 같은 값)
    private const float RockFootprintRadius = 1f;

    // ---------- 랜드마크 규칙 ----------
    // 경계 안쪽 거리와 랜드마크 사이 거리 (사용자 승인 값)
    private const float LandmarkEdgeMargin = 50f;
    private const float LandmarkMinSpacing = 100f;
    // 랜드마크 바닥 둘레에 나무, 바위, 유적 조각을 두지 않아 멀리서도 보이는 빈터가 되게 한다
    private const float LandmarkClearance = 8f;

    // ---------- 조각 크기 ----------
    // 모든 유적 조각을 원래 크기의 RuinScale배로 키우고 ± PieceScaleJitter만큼 흔든다 (사용자 승인). 배치도 간격은 이 크기에 맞춘 값이다
    private const float RuinScale = 1.5f;
    private const float PieceScaleJitter = 0.1f;
    private const float PieceTiltMax = 4f;
    // 조각 밑면에서 바닥 둘레의 가장 높은 땅까지가 조각 높이 × 이 값을 넘으면 묻힌 것으로 센다
    private const float PieceMaxBuryRatio = 0.5f;
    // 묻는 깊이 비율. 서쪽 신전 터, 남서쪽 룬 돌과 같은 값 (모든 유적 조각에 같은 값)
    private const float StructureSinkRatio = 0.05f;

    // ---------- 유적 도시 (사용자 승인 임시값) ----------
    private const float CityZMin = -1150f;
    private const float CityZMax = -300f;
    private const float CityEdgeMargin = 30f;
    // 강 양쪽에 엇갈린 시가지 2개 (사용자 승인 임시값). 강 서쪽 시가지가 구역 1 ~ 4, 강 동쪽 시가지가 구역 5 ~ 6 (각각 북 → 남).
    // 구역마다 방향을 조금씩 다르게 돌려 한 도시 안에서도 거리 방향이 달라 보이게 하고, 같은 시가지의 위·아래 구역은 돌린 방향이 서로 반대가 되게 나눴다.
    // 강 서쪽 시가지의 구역 중심 z 범위는 메뉴 17이 만든 강가 평지 z 범위(MapSouthShaper.FlatZMin ~ FlatZMax)와 같은 값을 써서 평지 위에 놓이게 한다.
    // 칸은 구역 중심에서 z로 약 ±(LotSpacing + LotCheckRadius)까지 나가므로 맨 위·맨 아래 구역의 바깥 줄 칸은 평지 경계 경사 띠에 걸칠 수 있다
    private static readonly SettlementSpec[] SettlementSpecs =
    {
        new SettlementSpec
        {
            Label = "강 서쪽 시가지", IsWest = true, ZMin = MapSouthShaper.FlatZMin, ZMax = MapSouthShaper.FlatZMax,
            Yaws = new[] { 4f, -3f, 6f, -2f }, TieBreak = SettlementTieBreak.North
        },
        new SettlementSpec
        {
            Label = "강 동쪽 시가지", IsWest = false, ZMin = -1150f, ZMax = -580f,
            Yaws = new[] { -5f, 3f }, TieBreak = SettlementTieBreak.Flatter
        }
    };
    // 구역 격자 (DistrictGridSize × DistrictGridSize칸, 칸 중심 간격 LotSpacing). 가장 큰 구조물(광장, 반지름 약 15m)과 길을 함께 담는 간격
    private const int DistrictGridSize = 3;
    private const float LotSpacing = 30f;
    // 시가지 안 이웃 구역 중심의 z 간격 (구역 한 폭이라 틈 없이 붙는다)과 x 차이 상한 (임시값)
    private const float DistrictSpacingZ = DistrictGridSize * LotSpacing;
    private const float DistrictMaxShiftX = 30f;
    // 구역 자리를 고를 때 칸 하나가 차지한다고 보는 반지름
    private const float LotCheckRadius = 15f;
    private const float DistrictSearchStep = 10f;
    // 광장 수. 도시 가운데에 가까운 구역부터 하나씩 둔다
    private const int PlazaCount = 2;
    // 칸이 무너져 터 대신 흩어진 조각만 남을 확률, 형태별 비중 (LotKind 순서)
    private const float CollapseRatio = 0.15f;
    private static readonly float[] LotKindWeights = { 0.35f, 0.25f, 0.2f, 0.2f };
    private static readonly string[] LotKindLabels = { "SmallHouse", "LargeHouse", "TowerRuin", "Colonnade" };
    private static readonly string[] SiteFailureLabels = { "없음", "도시 범위 밖", "다른 구조물과 겹침", "강 보호 구역", "서식지", "경사", "뜸·묻힘" };
    // 건물 터 바닥 경사 상한 (도). 조각마다 바닥 가운데에서 잰다
    private const float BuildingMaxSlope = 20f;
    // 도시 구조물 둘레에 나무, 바위, 유적 조각을 두지 않는 폭 (m)
    private const float CityClearance = 3f;
    // 담장 : 도시 사각형 안쪽 WallEdgeInset의 네 변을 따라 줄 자리를 중심 간격 WallRunSpacing으로 둔다 (줄 자리 길이 약 WallRunLength).
    // 줄마다 WallRunSkipRatio 확률로 생략하고, 남는 줄은 WallRunPieces 중 이어진 WallRunKeepMinRatio ~ WallRunKeepMaxRatio만 남긴다 (무작위 시작점).
    // 줄 중심은 변과 수직 방향으로 ± WallOffsetJitter, 방향은 변 방향 ± WallYawJitter만큼 흔든다 (사용자 승인 임시값).
    // 흔들린 줄 자리에서 도시 안쪽(변과 수직)으로 WallInwardStep씩 최대 WallInwardMax까지(0m 포함) 옮겨 보며, 줄 조각의 바닥 표본점 경사가
    // 모두 WallMaxSlope 이하인 첫 자리에 놓는다. 끝까지 없으면 그 줄은 놓지 않는다 (사용자 승인 임시값).
    // 강, 서식지, 다른 구조물에 걸리거나 묻히는 조각은 빼서 끊긴 곳이 된다
    private const float WallEdgeInset = 5f;
    private const float WallInwardStep = 5f;
    private const float WallInwardMax = 60f;
    private const float WallMaxSlope = 12f;
    // 담장 변 이름 (CreateWallRuns의 변 순서)
    private static readonly string[] WallSideLabels = { "남", "동", "서", "북" };
    private const float WallRunLength = 50f;
    private const float WallRunSpacing = 80f;
    private const float WallRunSkipRatio = 0.35f;
    private const float WallRunKeepMinRatio = 0.4f;
    private const float WallRunKeepMaxRatio = 1f;
    private const float WallOffsetJitter = 3f;
    private const float WallYawJitter = 8f;
    // 남은 조각이 이보다 적은 줄은 지운다
    private const int WallRunMinPieces = 5;

    // 도시 사각형 (B3 경계 안쪽 CityEdgeMargin, z CityZMin ~ CityZMax)
    private static readonly Rect CityRect = CreateCityRect();

    // ---------- 랜드마크 배치도 (임시값, 1.5배 크기 기준 좌표) ----------
    // 좌표는 묶음 중심 기준 (x = 동, z = 북, 묶음 회전 전)

    // 언덕 위 감시탑 터 : 반지름 7.5m 원에 기둥 받침과 부러진 기둥 7개 (남쪽 한 자리는 비움), 비운 남쪽 자리에 부서진 아치(입구), 안쪽에 계단
    private static readonly PieceSpec[] WatchtowerPieces =
    {
        Piece(BaseRoundPath, 0f, 7.5f, 0f),
        Piece(PillarBrokenPath, 5.3f, 5.3f, 45f, true),
        Piece(BaseSquarePath, 7.5f, 0f, 90f),
        Piece(PillarHalfBroken01Path, 5.3f, -5.3f, 135f, true),
        Piece(BaseRoundPath, -5.3f, -5.3f, 225f),
        Piece(PillarHalfBroken02Path, -7.5f, 0f, 270f, true),
        Piece(BaseSquareSmallPath, -5.3f, 5.3f, 315f),
        Piece(DoorArchPath, 0f, -7.5f, 0f),
        Piece(StairsPath, 0f, 1.5f, 0f)
    };

    // 무너진 다리 터 : x = 강을 가로지르는 방향, z = 강 방향. 양쪽 물가에 마주 보는 받침 (각 5조각).
    // 강 방향 간격만 1.5배로 넓히고, 강을 가로지르는 방향은 받침이 강 중심 8 ~ 18m 띠 안에 들도록 약 10.5 ~ 15.5m에 둔다 (사용자 결정).
    // 벽은 강과 나란히(묶음 회전 90도) 세워 받침 앞면처럼 보이게 한다. 강 위(8m 안)에는 아무것도 두지 않는다
    private static readonly PieceSpec[] BridgePieces =
    {
        Piece(BaseSquarePath, 10.5f, 3f, 0f),
        Piece(BaseSquarePath, 10.5f, -3f, 0f),
        Piece(WallMidPath, 13f, 0f, 90f),
        Piece(Brick01Path, 15.5f, 2.25f, 30f),
        Piece(Brick02Path, 15.5f, -3f, 200f),
        Piece(BaseRoundPath, -10.5f, 3f, 0f),
        Piece(BaseSquarePath, -10.5f, -3f, 90f),
        Piece(WallMidPath, -13f, 0.75f, 270f),
        Piece(Brick03Path, -15.5f, 1.5f, 80f),
        Piece(Brick01Path, -15f, -3.75f, 140f)
    };

    // 강가 선착장 터 : 묶음 중심은 강 중심에서 PierBankOffset(14m) 떨어진 물가, -x가 강 쪽 (x = -6이면 강 중심에서 약 8m).
    // 강 방향(z) 간격만 1.5배로 넓히고, x는 조각이 강 중심 8 ~ 20m 띠 안에 들도록 -4 ~ 3.5m에 둔다 (사용자 결정).
    // 물가 쪽에 말뚝 자리 같은 기둥 받침 둘과 바닥 벽돌로 나루터 흔적을 남기고, 뭍 쪽에 배를 강과 비스듬히(60도) 끌어 올려 두고 노를 옆에 둔다.
    // 배와 노는 유적이 아닌 소품이라 원래 크기다 (사용자 결정)
    private static readonly PieceSpec[] PierPieces =
    {
        Piece(BaseSquarePath, -4f, 3.75f, 0f),
        Piece(BaseSquarePath, -4f, -3.75f, 90f),
        Piece(Brick01Path, -3f, 0.75f, 10f),
        Piece(Brick02Path, -1f, -1.5f, 95f),
        Piece(Brick03Path, -0.5f, 2.25f, 180f),
        Piece(BaseSquareSmallPath, -2f, 6.75f, 30f),
        Piece(PillarHalfBroken02Path, 1f, 6f, 200f, true),
        Prop(BoatPath, 1.5f, -4.5f, 60f),
        Prop(PaddlePath, 3.5f, 0.75f, 20f),
        Piece(Brick01Path, 3f, 5.25f, 250f)
    };

    // 놓는 순서대로. 자리가 정해진 감시탑 터를 먼저, 강가 자리를 찾는 다리 터, 선착장 터를 그다음에 놓는다. 도시는 그 뒤에 만든다
    private static readonly LandmarkSpec[] LandmarkSpecs =
    {
        new LandmarkSpec { Label = "HillWatchtowerRuin", ApproxRadius = 12f, Placement = LandmarkPlacement.Hilltop, Pieces = WatchtowerPieces },
        new LandmarkSpec { Label = "BridgeRuin", ApproxRadius = 18f, Placement = LandmarkPlacement.RiverBridge, Pieces = BridgePieces },
        new LandmarkSpec { Label = "PierRuin", ApproxRadius = 12f, Placement = LandmarkPlacement.RiverBank, Pieces = PierPieces }
    };

    // ---------- 도시 배치도 (임시값, 1.5배 크기 기준 좌표, 입구가 -z) ----------

    // 탑 터 : 반지름 TowerRadius 원 위 TowerSlotCount자리 중 이어진 0 ~ TowerMissingMax자리가 무너져 빠진다
    private const int TowerSlotCount = 8;
    private const float TowerRadius = 7.5f;
    private const int TowerMissingMax = 2;
    // 기둥 회랑 : 두 줄(x = ±ColonnadeRowOffset), 줄마다 ColonnadeSlotsPerRow자리(ColonnadeSpacing 간격).
    // 자리마다 누적 확률로 빠짐(~ 0.2) / 받침만(~ 0.5) / 부러진 기둥(~ 0.85) / 온전한 기둥(나머지)
    private const float ColonnadeRowOffset = 4.5f;
    private const float ColonnadeSpacing = 4.5f;
    private const int ColonnadeSlotsPerRow = 5;
    private const double ColonnadeMissingRatio = 0.2;
    private const double ColonnadeBaseRatio = 0.5;
    private const double ColonnadeBrokenRatio = 0.85;

    // 광장 : 남북 넓은 계단, 4 × 4 바닥 벽돌(4.5m 간격), 네 모서리 기둥
    private const float PlazaStairsOffset = 12.5f;
    private const float PlazaBrickSpacing = 4.5f;
    private const int PlazaBrickGrid = 4;
    private const float PlazaCornerOffset = 10f;
    private static readonly string[] PlazaCornerPaths = { PillarHalfFullPath, PillarBrokenPath, BaseRoundPath, PillarFullPath };

    // 담장 줄 하나 : x축으로 약 50m. 받침 → 벽 셋(왼·가운데·오른, 1.5배 폭 7.5m 간격) → 부러진 기둥 → 무너진 틈(낮은 벽돌) →
    // 받침 → 벽 하나 → 반쪽 기둥 → 무너진 틈 → 끝 받침. 무너져 돌아간 벽 하나를 줄 옆에 둔다
    private static readonly PieceSpec[] WallRunPieces =
    {
        Piece(BaseSquarePath, -25f, 0f, 0f),
        Piece(WallLeftPath, -21.25f, 0f, 0f),
        Piece(WallMidPath, -13.75f, 0f, 0f),
        Piece(WallRightPath, -6.25f, 0f, 0f),
        Piece(PillarBrokenPath, -1.5f, 0f, 40f, true),
        Piece(Brick01Path, 2.5f, 0.75f, 15f),
        Piece(Brick02Path, 5f, -0.75f, 100f),
        Piece(BaseRoundPath, 9f, 0f, 0f),
        Piece(WallMidPath, 12.75f, 0f, 0f),
        Piece(WallMidPath, 12f, 5.5f, 80f),
        Piece(PillarHalfBroken01Path, 17f, 0f, 220f, true),
        Piece(Brick03Path, 20.5f, 0.75f, 60f),
        Piece(BaseSquarePath, 24f, 0f, 0f)
    };

    // 렌더러 범위 둘레 8곳은 AABB보다 FootprintInset만큼 안쪽을 본다 (서쪽 배치와 같은 값)
    private const float FootprintInset = 0.7f;
    private static readonly Vector2[] RingOffsets =
    {
        new Vector2(-1f, -1f), new Vector2(0f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 0f),
        new Vector2(1f, 0f), new Vector2(-1f, 1f), new Vector2(0f, 1f), new Vector2(1f, 1f)
    };
    private const float MaxBuryGap = 3f;
    // 뜸 검수 (계산 오차만 허용) : 나무·바위는 피벗이 피벗 아래 지면보다, 유적 조각은 정점 밑면이 바닥 둘레의 가장 낮은 땅보다 이만큼 넘게 높으면 뜬 것으로 센다
    private const float HeightTolerance = 0.01f;

    // GPU Resident Drawer로 그리면 몸체가 안 보이는 것으로 확인된 셰이더 (서쪽 배치와 같은 목록)
    private static readonly string[] GpuDrivenBrokenShaderNames = { "Polyart/Dreamscape CliffAuto", "M_Water_Lake_Amp" };
    private const string DisallowGpuDrivenTypeName = "UnityEngine.Rendering.DisallowGPUDrivenRendering, Unity.RenderPipelines.GPUDriven.Runtime";
    // 셰이더를 찾지 못하거나 컴파일에 실패하면 Unity가 대신 쓰는 분홍 셰이더
    private const string ErrorShaderName = "Hidden/InternalErrorShader";

    // 거점 기준 8방위. 0도 = 북(+Z), 시계 방향
    private static readonly string[] DirectionLabels = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };

    // B3 둘레의 강 중심선. 경로는 MapRiverBuilder의 상수로만 정해져 스크립트를 다시 컴파일하기 전까지 바뀌지 않으므로 처음 쓸 때 한 번만 구한다
    private static List<Vector2> _riverPath;
    // 노이즈 값의 구분 경계 [드문드문 시작, 나무 무리 시작]. 강 경로가 있어야 계산할 수 있어 처음 쓸 때 한 번만 구한다
    private static float[] _zoneThresholds;

    // 같은 위치는 항상 같은 구분을 돌려준다 (지면·바닥 식생 메뉴 19가 같은 구분을 쓰기 위함).
    // 강 보호 구역, 서식지 반경, 도시 사각형 안은 빈터다 (도시 안 나무는 도시 밀도 표를 따로 쓴다)
    public static MapTreeScatter.FieldZone GetSouthZone(Vector2 worldXZ)
    {
        if (IsCleared(worldXZ) || CityRect.Contains(worldXZ)) return MapTreeScatter.FieldZone.Open;

        float[] thresholds = GetZoneThresholds();
        float noise = SampleZoneNoise(worldXZ);
        if (noise >= thresholds[1]) return MapTreeScatter.FieldZone.Forest;
        if (noise >= thresholds[0]) return MapTreeScatter.FieldZone.Sparse;
        return MapTreeScatter.FieldZone.Open;
    }

    // 도시 안(도시 사각형 안, 강 보호 구역과 서식지 밖)인지. 메뉴 19의 지면 비율 로그도 같은 판정을 쓴다
    public static bool IsInCityArea(Vector2 worldXZ)
    {
        return CityRect.Contains(worldXZ) && !IsCleared(worldXZ);
    }

    [MenuItem("Tools/Map/18. 남쪽 나무·바위·유적·서식지 배치")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        Scene scene = terrain.gameObject.scene;
        if (scene != SceneManager.GetActiveScene())
        {
            Debug.LogError("[MapSouthScatter] Game 씬을 활성 씬으로 두고 실행하세요.");
            return;
        }

        GameObject southRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == SouthRegionName);
        if (southRoot == null)
        {
            Debug.LogError("씬 루트에 South가 없습니다. 먼저 'Tools/Map/1. 지형과 구역 부모 생성'을 실행하세요.");
            return;
        }

        System.Type disallowType = System.Type.GetType(DisallowGpuDrivenTypeName);
        if (disallowType == null)
        {
            Debug.LogError($"{DisallowGpuDrivenTypeName} 타입을 찾지 못했습니다. URP 패키지 버전이 바뀌었는지 확인하세요.");
            return;
        }

        // South/River(메뉴 7)는 지우지 않는다
        Transform treeParent = ResetParent(southRoot.transform, TreeParentName);
        Transform rockParent = ResetParent(southRoot.transform, RockParentName);
        Transform landmarkParent = ResetParent(southRoot.transform, LandmarkParentName);
        Transform habitat = ResetParent(southRoot.transform, HabitatName);
        habitat.position = ToWorld(HabitatCenter, GetGroundHeight(terrain, HabitatCenter));

        var render = new RenderSetup { DisallowType = disallowType };
        var landmarkContext = new LandmarkContext
        {
            Terrain = terrain,
            Render = render,
            Parent = landmarkParent,
            Random = new System.Random(LandmarkSeed),
            CityRandom = new System.Random(CitySeed),
            Hilltop = FindHilltop(terrain)
        };
        foreach (LandmarkSpec spec in LandmarkSpecs)
            TryPlaceLandmark(landmarkContext, spec);
        List<District> districts = BuildCity(landmarkContext);

        List<Blocker> blockers = landmarkContext.Placed.Concat(landmarkContext.CityBlockers).ToList();
        ScatterContext scatterContext = ScatterAll(terrain, new[] { treeParent, rockParent }, render, blockers);
        scatterContext.EmptyLots = landmarkContext.EmptyLots;
        ScatterDebris(scatterContext);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        LogZones(scatterContext);
        LogLandmarks(landmarkContext);
        LogCity(landmarkContext, districts, scatterContext);
        LogPlacementCheck(terrain, scatterContext, landmarkContext);
    }

    // 부모가 없으면 만들고, 있으면 자식을 모두 지운다
    private static Transform ResetParent(Transform root, string name)
    {
        Transform parent = root.Find(name);
        if (parent == null)
        {
            parent = new GameObject(name).transform;
            parent.SetParent(root, false);
        }
        for (int i = parent.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(parent.GetChild(i).gameObject);
        return parent;
    }

    private static Transform CreateChild(Transform parent, string name)
    {
        Transform child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    // ---------- 강 ----------

    // 메뉴 7과 같은 강 경로(MapRiverBuilder.GetPath) 중 B3를 RiverPathMargin만큼 넓힌 범위에 든 연속 구간.
    // 강은 B2에서 들어와 맵 남쪽 끝으로 나가므로 이 범위 안 구간은 하나로 이어진다
    private static List<Vector2> GetRiverPath()
    {
        if (_riverPath != null) return _riverPath;

        List<Vector2> path = MapRiverBuilder.GetPath(MapTerrainSetup.FindSceneTerrain());
        Rect south = MapGrid.GetRangeBounds(SouthCell, SouthCell);
        Rect area = Rect.MinMaxRect(south.xMin - RiverPathMargin, south.yMin - RiverPathMargin,
            south.xMax + RiverPathMargin, south.yMax + RiverPathMargin);
        int first = path.FindIndex(point => area.Contains(point));
        int last = path.FindLastIndex(point => area.Contains(point));
        _riverPath = path.GetRange(first, last - first + 1);
        return _riverPath;
    }

    private static float GetRiverDistance(Vector2 position)
    {
        return MapSouthShaper.GetDistanceToRiver(GetRiverPath(), position);
    }

    private static bool IsInRiverProtect(Vector2 position)
    {
        return GetRiverDistance(position) < MapSouthShaper.RiverProtectDistance;
    }

    // 강 중심선은 B3 안에서 북에서 남으로 z가 줄어들기만 하므로, z가 가장 가까운 강 점의 x와 견줘 서쪽인지 본다
    private static bool IsWestOfRiver(Vector2 position)
    {
        List<Vector2> river = GetRiverPath();
        Vector2 nearest = river[0];
        foreach (Vector2 point in river)
        {
            if (Mathf.Abs(point.y - position.y) < Mathf.Abs(nearest.y - position.y)) nearest = point;
        }
        return position.x < nearest.x;
    }

    // ---------- 구분 (빈터 / 드문드문 / 나무 무리) ----------

    // 다른 구역과 무늬가 겹치지 않도록 새 seed로 노이즈 위치를 정한다
    private static Vector2 CreateZoneNoiseOffset()
    {
        var random = new System.Random(ZoneSeed);
        return new Vector2(RandomRange(random, NoiseOffsetMin, NoiseOffsetMax), RandomRange(random, NoiseOffsetMin, NoiseOffsetMax));
    }

    private static float SampleZoneNoise(Vector2 worldXZ)
    {
        return Mathf.PerlinNoise(worldXZ.x / ZoneNoisePeriod + ZoneNoiseOffset.x, worldXZ.y / ZoneNoisePeriod + ZoneNoiseOffset.y);
    }

    // Perlin 노이즈는 값이 가운데에 몰려 있어 고정 임계값으로는 비율을 맞추기 어려워 분위수로 경계를 정한다.
    // 강 보호 구역과 서식지 반경은 노이즈와 상관없이 빈터이므로, 그 밖의 표본만으로 경계를 정하고 빈터 몫에서 그 넓이를 뺀다.
    // 도시를 만들기 전과 같은 경계를 쓰려고 도시 사각형은 빼지 않는다
    private static float[] GetZoneThresholds()
    {
        if (_zoneThresholds != null) return _zoneThresholds;

        Rect south = MapGrid.GetRangeBounds(SouthCell, SouthCell);
        int columns = Mathf.RoundToInt(south.width / ZoneSampleSpacing);
        int rows = Mathf.RoundToInt(south.height / ZoneSampleSpacing);
        var outside = new List<float>(columns * rows);
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                var position = new Vector2(south.xMin + (x + 0.5f) * ZoneSampleSpacing, south.yMin + (z + 0.5f) * ZoneSampleSpacing);
                if (!IsCleared(position)) outside.Add(SampleZoneNoise(position));
            }
        }
        outside.Sort();

        int total = columns * rows;
        int clearedCount = total - outside.Count;
        int openCount = Mathf.Max(0, Mathf.RoundToInt(total * OpenAreaRatio) - clearedCount);
        int forestCount = Mathf.RoundToInt(total * ForestAreaRatio);
        _zoneThresholds = new[] { outside[openCount], outside[outside.Count - forestCount] };
        return _zoneThresholds;
    }

    private static bool IsInHabitat(Vector2 position)
    {
        return Vector2.Distance(position, HabitatCenter) < HabitatRadius;
    }

    // 노이즈와 상관없이 빈터로 비우는 곳 (서식지 반경, 강 보호 구역)
    private static bool IsCleared(Vector2 position)
    {
        return IsInHabitat(position) || IsInRiverProtect(position);
    }

    private static Rect CreateCityRect()
    {
        Rect south = MapGrid.GetRangeBounds(SouthCell, SouthCell);
        return Rect.MinMaxRect(
            south.xMin + CityEdgeMargin, Mathf.Max(CityZMin, south.yMin + CityEdgeMargin),
            south.xMax - CityEdgeMargin, Mathf.Min(CityZMax, south.yMax - CityEdgeMargin));
    }

    // ---------- 배치도 조각 ----------

    private static PieceSpec Piece(string path, float x, float z, float yaw, bool isWobbly = false)
    {
        return new PieceSpec { Path = path, Offset = new Vector2(x, z), Yaw = yaw, IsWobbly = isWobbly };
    }

    private static PieceSpec Prop(string path, float x, float z, float yaw)
    {
        return new PieceSpec { Path = path, Offset = new Vector2(x, z), Yaw = yaw, IsProp = true };
    }

    // ---------- 랜드마크 ----------

    // 언덕 위(지형과 같은 비틀린 윤곽 안)를 격자로 돌며 지면이 가장 높은 곳. 비틀기 때문에 꼭대기가 언덕 중심에서 벗어날 수 있어 직접 찾는다.
    // 찾는 사각형은 B3 밖으로 나가지 않게 자른다
    private static Vector2 FindHilltop(Terrain terrain)
    {
        Rect south = MapGrid.GetRangeBounds(SouthCell, SouthCell);
        float range = MapSouthShaper.HillRadius * HilltopSearchRangeRatio;
        Vector2 center = MapSouthShaper.HillCenter;
        Vector2 best = center;
        float bestHeight = float.MinValue;
        for (float z = Mathf.Max(south.yMin, center.y - range); z <= Mathf.Min(south.yMax, center.y + range); z += HilltopSearchStep)
        {
            for (float x = Mathf.Max(south.xMin, center.x - range); x <= Mathf.Min(south.xMax, center.x + range); x += HilltopSearchStep)
            {
                var position = new Vector2(x, z);
                if (MapSouthShaper.CalculateHillWeight(position) <= 0f) continue;

                float height = GetGroundHeight(terrain, position);
                if (height <= bestHeight) continue;
                bestHeight = height;
                best = position;
            }
        }
        return best;
    }

    private static void TryPlaceLandmark(LandmarkContext context, LandmarkSpec spec)
    {
        LandmarkBuild build = BuildGroup(context, spec.Label, spec.Pieces, context.Parent);
        if (build == null) return;

        LandmarkFit? fit = FindFit(context, build, spec);
        if (fit == null)
        {
            Debug.LogWarning($"[MapSouthScatter] '{spec.Label}' 자리를 찾지 못했습니다 (바닥 반지름 {build.Radius:F1}m). 위치 규칙의 조건을 확인하세요.");
            Object.DestroyImmediate(build.Group.gameObject);
            return;
        }

        PlaceLandmark(context, build, spec, fit.Value);
        if (spec.Placement == LandmarkPlacement.RiverBridge) context.BridgeGroup = build.Group;
        if (spec.Placement == LandmarkPlacement.RiverBank) context.PierGroup = build.Group;
    }

    private static LandmarkFit? FindFit(LandmarkContext context, LandmarkBuild build, LandmarkSpec spec)
    {
        switch (spec.Placement)
        {
            case LandmarkPlacement.Hilltop:
                return FindHilltopFit(context, build);
            case LandmarkPlacement.RiverBridge:
                return FindBridgeFit(context, build);
            default:
                return FindBankFit(context, build);
        }
    }

    // 배치도대로 월드 원점에 조립한다. 묶음 중심이 원점이라 조각의 월드 XZ가 곧 중심 기준 위치가 되어, 자리 찾기에서 그대로 쓴다
    private static LandmarkBuild BuildGroup(LandmarkContext context, string label, PieceSpec[] pieces, Transform parent)
    {
        var build = new LandmarkBuild { Group = new GameObject(label).transform };
        build.Group.SetParent(parent, false);
        build.Group.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        foreach (PieceSpec piece in pieces)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + piece.Path + ".prefab");
            if (prefab == null)
            {
                Debug.LogError($"[MapSouthScatter] {piece.Path} prefab을 찾지 못해 '{label}'을 놓지 못했습니다. 배치도의 경로를 확인하세요.");
                Object.DestroyImmediate(build.Group.gameObject);
                return null;
            }

            GameObject instance = CreatePiece(context.Random, piece, prefab, build.Group);
            build.InstanceCount++;
            FinishInstance(context.Render, instance, prefab.name, build.Stats);

            // 렌더러가 없는 조각은 크기와 높이를 잴 수 없어 깨진 조각으로만 세고 높이 맞춤에서 뺀다
            if (instance.GetComponentsInChildren<Renderer>().Length == 0)
            {
                build.Stats.BrokenMaterialCount++;
                continue;
            }

            Bounds bounds = GetRendererBounds(instance);
            (float bottom, float top) = GetMeshHeightRange(instance);
            build.Pieces.Add(new PlacedPiece { Transform = instance.transform, Points = GetFootprintPoints(bounds), Height = top - bottom });
            build.Radius = Mathf.Max(build.Radius, ToXZ(bounds.center).magnitude + Mathf.Max(bounds.extents.x, bounds.extents.z));
        }
        return build;
    }

    // prefab 연결을 유지한 채 놓는다. 유적 조각은 RuinScale배 ± 흔들림, 기둥은 기울기도 흔든다.
    // 흔들림 값은 조각 순서대로 같은 seed에서 뽑아 다시 실행해도 같다
    private static GameObject CreatePiece(System.Random random, PieceSpec piece, GameObject prefab, Transform group)
    {
        Quaternion rotation = Quaternion.Euler(0f, piece.Yaw, 0f);
        float scale = 1f;
        if (!piece.IsProp) scale = RuinScale * RandomRange(random, 1f - PieceScaleJitter, 1f + PieceScaleJitter);
        if (piece.IsWobbly) rotation = CreateTilt(random) * rotation;

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
        instance.transform.localPosition = new Vector3(piece.Offset.x, 0f, piece.Offset.y);
        instance.transform.localRotation = rotation;
        instance.transform.localScale = Vector3.one * scale;
        return instance;
    }

    // 0 ~ PieceTiltMax도 기울기. 기울이는 방향도 무작위
    private static Quaternion CreateTilt(System.Random random)
    {
        float tilt = RandomRange(random, 0f, PieceTiltMax);
        Vector3 tiltAxis = Quaternion.Euler(0f, RandomRange(random, 0f, 360f), 0f) * Vector3.right;
        return Quaternion.AngleAxis(tilt, tiltAxis);
    }

    // 꼭대기 둘레 HilltopFitRadius 안의 자리와 묶음 회전 각도 중 가장 잘 맞는 것을 고르고, 묶음을 그 각도로 돌려 둔다
    private static LandmarkFit FindHilltopFit(LandmarkContext context, LandmarkBuild build)
    {
        LandmarkFit? best = null;
        for (float yaw = 0f; yaw < 360f; yaw += HilltopYawStep)
        {
            RotateLandmark(build, yaw);
            for (float z = -HilltopFitRadius; z <= HilltopFitRadius; z += HilltopFitStep)
            {
                for (float x = -HilltopFitRadius; x <= HilltopFitRadius; x += HilltopFitStep)
                {
                    var offset = new Vector2(x, z);
                    if (offset.magnitude > HilltopFitRadius) continue;

                    LandmarkFit fit = MeasureLandmark(context.Terrain, build, context.Hilltop + offset);
                    fit.GroupYaw = yaw;
                    if (best == null || IsBetterFit(fit, best.Value)) best = fit;
                }
            }
        }
        RotateLandmark(build, best.Value.GroupYaw);
        return best.Value;
    }

    // 강 중심선 위 점마다 강 방향으로 묶음을 돌려 받침이 양쪽 물가(강 중심 8 ~ 18m)에 드는 자리를 찾는다
    private static LandmarkFit? FindBridgeFit(LandmarkContext context, LandmarkBuild build)
    {
        List<Vector2> river = GetRiverPath();
        LandmarkFit? best = null;
        for (int i = 0; i < river.Count; i++)
        {
            Vector2 center = river[i];
            if (!CanPlaceBridge(context, build.Radius, center)) continue;

            int before = FindIndexAtDistance(river, i, -1, BridgeStraightDistance);
            int after = FindIndexAtDistance(river, i, 1, BridgeStraightDistance);
            if (before < 0 || after < 0) continue;

            // 묶음의 +z가 하류 방향을 향하도록 돌린다 (x축이 강을 가로지른다)
            Vector2 direction = river[after] - river[before];
            float yaw = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
            RotateLandmark(build, yaw);
            if (!IsWithinRiverDistance(build, center, BridgeMinRiverDistance, BridgeMaxRiverDistance)) continue;

            LandmarkFit fit = MeasureLandmark(context.Terrain, build, center);
            fit.GroupYaw = yaw;
            fit.Bend = Vector2.Angle(center - river[before], river[after] - center);
            if (best == null || IsBetterBridgeFit(fit, best.Value)) best = fit;
        }

        if (best != null) RotateLandmark(build, best.Value.GroupYaw);
        return best;
    }

    // 강 중심선 위 점마다 양쪽 물가(강 중심에서 PierBankOffset)에 묶음을 놓아 보고, 조각이 모두 강 중심 8 ~ 20m 안에 드는 자리를 찾는다.
    // 묶음의 -x가 강 쪽을 향하도록 돌린다 (+x = 강에서 멀어지는 방향, z = 강 방향)
    private static LandmarkFit? FindBankFit(LandmarkContext context, LandmarkBuild build)
    {
        List<Vector2> river = GetRiverPath();
        LandmarkFit? best = null;
        for (int i = 0; i < river.Count; i++)
        {
            int before = FindIndexAtDistance(river, i, -1, BridgeStraightDistance);
            int after = FindIndexAtDistance(river, i, 1, BridgeStraightDistance);
            if (before < 0 || after < 0) continue;

            Vector2 direction = (river[after] - river[before]).normalized;
            // 하류를 바라볼 때 오른쪽, 왼쪽 물가
            foreach (Vector2 away in new[] { new Vector2(direction.y, -direction.x), new Vector2(-direction.y, direction.x) })
            {
                Vector2 center = river[i] + away * PierBankOffset;
                if (!CanPlaceBridge(context, build.Radius, center)) continue;

                // Y축 회전 yaw는 묶음 +x를 월드 (cos yaw, -sin yaw)로 보낸다. 이 방향이 away가 되게 한다
                float yaw = Mathf.Atan2(-away.y, away.x) * Mathf.Rad2Deg;
                RotateLandmark(build, yaw);
                if (!IsWithinRiverDistance(build, center, PierMinRiverDistance, PierMaxRiverDistance)) continue;

                LandmarkFit fit = MeasureLandmark(context.Terrain, build, center);
                fit.GroupYaw = yaw;
                fit.Bend = Vector2.Angle(river[i] - river[before], river[after] - river[i]);
                if (best == null || IsBetterBridgeFit(fit, best.Value)) best = fit;
            }
        }

        if (best != null) RotateLandmark(build, best.Value.GroupYaw);
        return best;
    }

    // start에서 step 방향(-1 = 상류, 1 = 하류)으로 경로를 따라 distance 이상 간 첫 점의 번호. 경로가 모자라면 -1
    private static int FindIndexAtDistance(List<Vector2> river, int start, int step, float distance)
    {
        float walked = 0f;
        int index = start;
        while (walked < distance && index + step >= 0 && index + step < river.Count)
        {
            walked += Vector2.Distance(river[index], river[index + step]);
            index += step;
        }
        return walked >= distance ? index : -1;
    }

    // 다리 터, 선착장 터 공통 : 경계 안쪽, 서식지 밖, 먼저 놓은 랜드마크와 LandmarkMinSpacing 이상
    private static bool CanPlaceBridge(LandmarkContext context, float radius, Vector2 position)
    {
        if (!IsInsideSouth(position, LandmarkEdgeMargin + radius)) return false;
        if (Vector2.Distance(position, HabitatCenter) < HabitatRadius + radius) return false;
        return context.Placed.All(other => Vector2.Distance(other.Center, position) >= LandmarkMinSpacing);
    }

    // 조각마다 바닥 표본점(가운데와 둘레 8곳)이 모두 강 중심에서 minDistance ~ maxDistance 안인지 (다리 받침, 선착장 터).
    // 강이 굽은 곳에서는 조각이 물 쪽으로 들어가거나 멀어져 걸러진다
    private static bool IsWithinRiverDistance(LandmarkBuild build, Vector2 center, float minDistance, float maxDistance)
    {
        foreach (PlacedPiece piece in build.Pieces)
        {
            foreach (Vector2 point in piece.Points)
            {
                float distance = GetRiverDistance(point + center);
                if (distance < minDistance || distance > maxDistance) return false;
            }
        }
        return true;
    }

    private static bool IsBetterBridgeFit(LandmarkFit fit, LandmarkFit best)
    {
        if (fit.BrokenCount != best.BrokenCount) return fit.BrokenCount < best.BrokenCount;
        if (!Mathf.Approximately(fit.Bend, best.Bend)) return fit.Bend < best.Bend;
        return fit.GroundRange < best.GroundRange;
    }

    // 원점에 둔 묶음을 돌리고, 자리 찾기에 쓰는 조각별 바닥 표본 위치와 바닥 반지름을 다시 잰다 (높이는 Y축 회전으로 바뀌지 않는다)
    private static void RotateLandmark(LandmarkBuild build, float yaw)
    {
        build.Group.rotation = Quaternion.Euler(0f, yaw, 0f);
        build.Radius = 0f;
        foreach (PlacedPiece piece in build.Pieces)
        {
            Bounds bounds = GetRendererBounds(piece.Transform.gameObject);
            piece.Points = GetFootprintPoints(bounds);
            build.Radius = Mathf.Max(build.Radius, ToXZ(bounds.center).magnitude + Mathf.Max(bounds.extents.x, bounds.extents.z));
        }
    }

    // 묶음 중심을 position에 옮겼을 때 조각마다 바닥 아래 지면을 재서, 땅에 맞추면 많이 묻히는 조각 수를 센다 (GroundInstance와 같은 계산)
    private static LandmarkFit MeasureLandmark(Terrain terrain, LandmarkBuild build, Vector2 position)
    {
        float groundMin = float.MaxValue;
        float groundMax = float.MinValue;
        int brokenCount = 0;
        foreach (PlacedPiece piece in build.Pieces)
        {
            float[] grounds = piece.Points.Select(point => GetGroundHeight(terrain, point + position)).ToArray();
            float lowest = grounds.Min();
            float highest = grounds.Max();
            groundMin = Mathf.Min(groundMin, lowest);
            groundMax = Mathf.Max(groundMax, highest);
            if (IsBuried(lowest, highest, piece.Height, StructureSinkRatio)) brokenCount++;
        }
        return new LandmarkFit { Position = position, BrokenCount = brokenCount, GroundRange = groundMax - groundMin };
    }

    // 밑면을 (가장 낮은 땅 - 높이 × sinkRatio)에 맞췄을 때, 가장 높은 땅이 밑면에서 높이 × PieceMaxBuryRatio를 넘으면 많이 묻힌 것이다
    private static bool IsBuried(float lowest, float highest, float height, float sinkRatio)
    {
        return highest - (lowest - height * sinkRatio) > height * PieceMaxBuryRatio;
    }

    private static bool IsBetterFit(LandmarkFit fit, LandmarkFit best)
    {
        if (fit.BrokenCount != best.BrokenCount) return fit.BrokenCount < best.BrokenCount;
        return fit.GroundRange < best.GroundRange;
    }

    // 조립한 묶음을 찾은 자리로 옮긴 뒤 조각마다 높이를 맞춘다. 묶음 GameObject는 중심의 지면 높이에 둔다
    private static void PlaceLandmark(LandmarkContext context, LandmarkBuild build, LandmarkSpec spec, LandmarkFit fit)
    {
        float centerGround = GetGroundHeight(context.Terrain, fit.Position);
        build.Group.position = ToWorld(fit.Position, centerGround);

        int brokenCount = build.Pieces.Count(piece => GroundInstance(context.Terrain, piece.Transform.gameObject, StructureSinkRatio));
        context.Placed.Add(new Blocker { Center = fit.Position, Radius = build.Radius + LandmarkClearance });
        context.BrokenCount += brokenCount;
        context.PieceCount += build.InstanceCount;
        context.AllPieces.AddRange(build.Group.Cast<Transform>());
        MergeStats(context.Stats, build.Stats);

        // 조각 바닥 표본점의 강 중심 거리 (다리 터는 8 ~ 18m, 선착장 터는 8 ~ 20m, 감시탑 터는 강 보호 거리 이상이어야 한다)
        float[] riverDistances = build.Pieces
            .SelectMany(piece => GetFootprintPoints(GetRendererBounds(piece.Transform.gameObject)))
            .Select(GetRiverDistance).ToArray();
        bool isRiverside = spec.Placement != LandmarkPlacement.Hilltop;
        string bendText = isRiverside ? $", 강 꺾인 각도 {fit.Bend:F1}도 (앞뒤 {BridgeStraightDistance}m)" : "";
        context.Notes.Add($"{spec.Label} : 중심 ({fit.Position.x:F1}, {fit.Position.y:F1}) 지면 {centerGround:F1}m, " +
                          $"묶음 회전 {build.Group.eulerAngles.y:F0}도, 바닥 반지름 {build.Radius:F1}m (기준 약 {spec.ApproxRadius:F1}m), " +
                          $"구분 {CategoryLabels[(int)GetSouthZone(fit.Position)]}, 바닥 아래 지면 높이 차 {fit.GroundRange:F1}m, " +
                          $"경사 {GetSlope(context.Terrain, fit.Position):F1}도{bendText}, " +
                          $"조각 바닥 강 중심 거리 {riverDistances.Min():F1} ~ {riverDistances.Max():F1}m, " +
                          $"조각 {build.InstanceCount}개 (뜨거나 묻힘 {brokenCount}, 깨진 머티리얼·렌더러 없음 {build.Stats.BrokenMaterialCount}), " +
                          $"Disallow 붙임 {build.Stats.DisallowCount}, 충돌체 추가 {build.Stats.ColliderCount}" +
                          $"\n  prefab : {DescribePrefabs(build.Group)}");
    }

    private static string DescribePrefabs(Transform group)
    {
        return string.Join(", ", group.Cast<Transform>()
            .GroupBy(piece => piece.name)
            .OrderByDescending(names => names.Count())
            .Select(names => $"{names.Key} × {names.Count()}"));
    }

    private static void MergeStats(InstanceStats into, InstanceStats from)
    {
        into.ColliderCount += from.ColliderCount;
        into.DisallowCount += from.DisallowCount;
        into.BrokenMaterialCount += from.BrokenMaterialCount;
    }

    // ---------- 유적 도시 ----------

    // 구역 자리 고르기 → 광장 → 건물 터 → 담장 순서로 만든다. 모두 South/Landmarks/City 아래에 둔다
    private static List<District> BuildCity(LandmarkContext context)
    {
        // 묶음 GameObject도 B3 안에 두려고 도시 묶음은 도시 사각형 가운데, 구역 묶음은 구역 중심에 둔다 (담장 묶음은 도시 묶음과 같은 자리)
        Vector2 cityCenter = CityRect.center;
        Transform city = CreateChild(context.Parent, CityParentName);
        city.position = ToWorld(cityCenter, GetGroundHeight(context.Terrain, cityCenter));
        List<District> districts = PlaceDistricts(context);
        var plazaDistricts = new HashSet<District>(districts.OrderBy(district => Vector2.Distance(district.Center, cityCenter)).Take(PlazaCount));

        foreach (District district in districts)
        {
            district.Group = CreateChild(city, $"District {district.Index + 1}");
            district.Group.position = ToWorld(district.Center, GetGroundHeight(context.Terrain, district.Center));
            if (plazaDistricts.Contains(district)) PlacePlaza(context, district);
            for (int lot = 0; lot < district.Lots.Count; lot++)
            {
                if (!district.IsLotValid[lot] || lot == district.PlazaLot) continue;
                PlaceBuildingLot(context, district, lot);
            }
        }

        PlaceWalls(context, CreateChild(city, WallParentName));
        return districts;
    }

    // 시가지마다(강 서쪽 → 강 동쪽 순서) 구역 자리를 정한다. 쓸 수 있는 칸이 없는 구역은 만들지 않는다
    private static List<District> PlaceDistricts(LandmarkContext context)
    {
        var districts = new List<District>();
        int firstIndex = 0;
        foreach (SettlementSpec spec in SettlementSpecs)
        {
            SettlementSearch search = FindSettlement(context, districts, spec, firstIndex);
            firstIndex += spec.Yaws.Length;
            string range = $"구역 중심 z {spec.ZMin:F0} ~ {spec.ZMax:F0}";
            if (search.Best == null)
            {
                context.City.DistrictNotes.Add($"{spec.Label} ({range}) : 강 {(spec.IsWest ? "서" : "동")}쪽에 구역을 모두 둘 자리를 찾지 못해 만들지 않았다");
                continue;
            }

            string groundRange = spec.TieBreak == SettlementTieBreak.Flatter ? $", 칸 바닥 높이 차 합 {search.BestGroundRange:F1}m" : "";
            context.City.DistrictNotes.Add($"{spec.Label} ({range}, 구역 {spec.Yaws.Length}곳) : 쓸 수 있는 칸 합 {search.BestCount}/{search.Best.Sum(district => district.Lots.Count)}{groundRange}");
            for (int level = 0; level < search.Best.Length; level++)
            {
                District district = search.Best[level];
                district.IsLotValid = search.BestValid[level];
                district.ValidCount = district.IsLotValid.Count(isValid => isValid);
                string gap = level == 0
                    ? "시가지 맨 위"
                    : $"위 구역과 z 간격 {search.Best[level - 1].Center.y - district.Center.y:F0}m, x 차이 {district.Center.x - search.Best[level - 1].Center.x:+0;-0;0}m";
                string label = $"  구역 {district.Index + 1} : 중심 ({district.Center.x:F0}, {district.Center.y:F0}), 회전 {district.Yaw:F0}도, " +
                               $"쓸 수 있는 칸 {district.ValidCount}/{district.Lots.Count}, {gap}";
                if (district.ValidCount == 0)
                {
                    context.City.DistrictNotes.Add(label + " (쓸 수 있는 칸이 없어 만들지 않았다)");
                    continue;
                }
                districts.Add(district);
                context.City.SearchInvalidLots += district.Lots.Count - district.ValidCount;
                context.City.DistrictNotes.Add(label);
            }
        }
        return districts;
    }

    // 맨 위 구역 중심을 DistrictSearchStep 격자로 돌고, 아래 구역은 z를 DistrictSpacingZ씩 내리며 x를 위 구역 기준 ± DistrictMaxShiftX 안에서 모두 골라 본다 (전수 탐색).
    // 모든 구역 중심이 강 같은 쪽, CityRect의 x 범위, 시가지 z 범위 안인 조합 중 쓸 수 있는 칸 합이 가장 많은 것을 고른다.
    // 합이 같으면 시가지 규칙대로 더 북쪽(서쪽 시가지) / 쓸 수 있는 칸 바닥 높이 차 합이 더 작은 곳(동쪽 시가지), 그래도 같으면 먼저 찾은 조합을 쓴다.
    // 구역 수가 n이면 맨 위 자리 하나에 조합이 (2 × ShiftSteps + 1)^(n - 1)개다. 구역 하나는 (구역 순서, 열, 행)마다 한 번만 잰다
    private static SettlementSearch FindSettlement(LandmarkContext context, List<District> placed, SettlementSpec spec, int firstIndex)
    {
        int count = spec.Yaws.Length;
        var search = new SettlementSearch
        {
            Context = context,
            Placed = placed,
            Spec = spec,
            FirstIndex = firstIndex,
            Columns = Mathf.FloorToInt(CityRect.width / DistrictSearchStep) + 1,
            ShiftSteps = Mathf.FloorToInt(DistrictMaxShiftX / DistrictSearchStep),
            Measured = Enumerable.Range(0, count).Select(_ => new Dictionary<(int, int), District>()).ToArray(),
            Chain = new District[count],
            ChainValid = new bool[count][]
        };

        float zMin = Mathf.Max(CityRect.yMin, spec.ZMin);
        float zMax = Mathf.Min(CityRect.yMax, spec.ZMax);
        int rows = Mathf.FloorToInt((zMax - zMin - (count - 1) * DistrictSpacingZ) / DistrictSearchStep) + 1;
        for (int row = 0; row < rows; row++)
        {
            float top = zMax - row * DistrictSearchStep;
            for (int column = 0; column < search.Columns; column++)
                SearchDistrict(search, 0, column, row, top, 0);
        }
        return search;
    }

    // level번째 구역을 column 열에 놓아 보고, 다음 구역을 x ± ShiftSteps 열에서 이어 고른다. 마지막 구역이면 조합의 칸 합을 지금까지 가장 좋은 것과 견준다
    private static void SearchDistrict(SettlementSearch search, int level, int column, int row, float top, int total)
    {
        if (column < 0 || column >= search.Columns) return;

        District district = GetMeasuredDistrict(search, level, column, row, top);
        if (district == null) return;

        // 같은 시가지 위 구역의 쓸 수 있는 칸과 LotSpacing보다 가까운 칸은 뺀다 (다른 시가지와의 겹침은 잴 때 이미 뺐다)
        var valid = (bool[])district.IsLotValid.Clone();
        for (int lot = 0; lot < valid.Length; lot++)
        {
            if (valid[lot] && IsNearChainLot(search, level, district.Lots[lot])) valid[lot] = false;
            if (valid[lot]) total++;
        }
        search.Chain[level] = district;
        search.ChainValid[level] = valid;

        if (level < search.Chain.Length - 1)
        {
            for (int shift = -search.ShiftSteps; shift <= search.ShiftSteps; shift++)
                SearchDistrict(search, level + 1, column + shift, row, top, total);
            return;
        }

        if (total < search.BestCount) return;

        bool prefersFlatter = search.Spec.TieBreak == SettlementTieBreak.Flatter;
        float groundRange = prefersFlatter ? GetChainGroundRange(search) : 0f;
        bool isBetter = total > search.BestCount
            || (prefersFlatter ? groundRange < search.BestGroundRange : top > search.BestTop);
        if (!isBetter) return;
        search.BestCount = total;
        search.BestTop = top;
        search.BestGroundRange = groundRange;
        search.Best = (District[])search.Chain.Clone();
        search.BestValid = (bool[][])search.ChainValid.Clone();
    }

    // level번째 구역을 (column, 맨 위 구역 row) 자리에 둔 구역. 중심이 강 반대쪽이면 null. 한 번 잰 자리는 다시 쓴다
    private static District GetMeasuredDistrict(SettlementSearch search, int level, int column, int row, float top)
    {
        if (search.Measured[level].TryGetValue((column, row), out District measured)) return measured;

        var center = new Vector2(CityRect.xMin + column * DistrictSearchStep, top - level * DistrictSpacingZ);
        District district = null;
        if (IsWestOfRiver(center) == search.Spec.IsWest)
            district = MeasureDistrict(search.Context, search.Placed, search.FirstIndex + level, center, search.Spec.Yaws[level]);
        search.Measured[level][(column, row)] = district;
        return district;
    }

    // 지금 살펴보는 조합에서 쓸 수 있는 칸(위 구역과의 겹침까지 뺀 칸)의 바닥 높이 차 합 (m). 작을수록 평평하다
    private static float GetChainGroundRange(SettlementSearch search)
    {
        float sum = 0f;
        for (int level = 0; level < search.Chain.Length; level++)
        {
            District district = search.Chain[level];
            for (int lot = 0; lot < district.Lots.Count; lot++)
            {
                if (search.ChainValid[level][lot]) sum += district.LotGroundRanges[lot];
            }
        }
        return sum;
    }

    private static bool IsNearChainLot(SettlementSearch search, int level, Vector2 lot)
    {
        for (int above = 0; above < level; above++)
        {
            District district = search.Chain[above];
            for (int i = 0; i < district.Lots.Count; i++)
            {
                if (search.ChainValid[above][i] && Vector2.Distance(district.Lots[i], lot) < LotSpacing) return true;
            }
        }
        return false;
    }

    private static District MeasureDistrict(LandmarkContext context, List<District> placed, int index, Vector2 center, float yaw)
    {
        var district = new District { Index = index, Center = center, Yaw = yaw, Lots = GetLotCenters(center, yaw) };
        district.IsLotValid = new bool[district.Lots.Count];
        district.LotGroundRanges = new float[district.Lots.Count];
        for (int lot = 0; lot < district.Lots.Count; lot++)
        {
            if (!IsLotSiteValid(context, placed, district.Lots[lot])) continue;

            district.IsLotValid[lot] = true;
            district.ValidCount++;
            district.LotGroundRanges[lot] = GetLotGroundRange(context.Terrain, district.Lots[lot]);
        }
        return district;
    }

    // 칸 가운데와 둘레 8곳(가운데에서 LotCheckRadius × FootprintInset 떨어진 정사각형 둘레)의 지면 높이 중 최고 - 최저 (m)
    private static float GetLotGroundRange(Terrain terrain, Vector2 lot)
    {
        float center = GetGroundHeight(terrain, lot);
        float min = center;
        float max = center;
        foreach (Vector2 offset in RingOffsets)
        {
            float height = GetGroundHeight(terrain, lot + offset * (LotCheckRadius * FootprintInset));
            min = Mathf.Min(min, height);
            max = Mathf.Max(max, height);
        }
        return max - min;
    }

    // 구역 중심 기준 격자 칸 중심 (구역 회전만큼 돌림). 가운데 칸이 (DistrictGridSize² - 1) / 2번이다
    private static List<Vector2> GetLotCenters(Vector2 center, float yaw)
    {
        var lots = new List<Vector2>();
        int half = DistrictGridSize / 2;
        for (int gz = -half; gz <= half; gz++)
        {
            for (int gx = -half; gx <= half; gx++)
            {
                Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(gx * LotSpacing, 0f, gz * LotSpacing);
                lots.Add(center + ToXZ(offset));
            }
        }
        return lots;
    }

    // 구역 자리를 고를 때의 칸 조건 (칸이 반지름 LotCheckRadius 원을 차지한다고 본다)
    private static bool IsLotSiteValid(LandmarkContext context, List<District> placed, Vector2 lot)
    {
        if (!IsInsideRect(CityRect, lot, LotCheckRadius)) return false;
        if (Vector2.Distance(lot, HabitatCenter) < HabitatRadius + LotCheckRadius) return false;
        if (context.Placed.Any(blocker => Vector2.Distance(blocker.Center, lot) < blocker.Radius + LotCheckRadius)) return false;
        foreach (District district in placed)
        {
            for (int i = 0; i < district.Lots.Count; i++)
            {
                if (district.IsLotValid[i] && Vector2.Distance(district.Lots[i], lot) < LotSpacing) return false;
            }
        }
        if (GetSlope(context.Terrain, lot) > BuildingMaxSlope) return false;
        return GetRiverDistance(lot) >= MapSouthShaper.RiverProtectDistance + LotCheckRadius;
    }

    // 구역 가운데 칸부터 가까운 순으로, 광장이 맞는 첫 칸에 놓는다
    private static void PlacePlaza(LandmarkContext context, District district)
    {
        IEnumerable<int> candidates = Enumerable.Range(0, district.Lots.Count)
            .Where(lot => district.IsLotValid[lot])
            .OrderBy(lot => Vector2.Distance(district.Lots[lot], district.Center));
        foreach (int lot in candidates)
        {
            LandmarkBuild build = BuildGroup(context, $"Plaza {context.City.PlazaCount + 1}", CreatePlazaPieces(), district.Group);
            if (build == null) return;

            LandmarkFit? fit = FitStructure(context, build, district.Lots[lot], district.Yaw, out SiteFailure failure);
            if (fit != null && fit.Value.BrokenCount == 0)
            {
                PlaceCityStructure(context, build, fit.Value);
                district.PlazaLot = lot;
                context.City.PlazaCount++;
                return;
            }

            context.City.PlazaFailures[(int)(fit == null ? failure : SiteFailure.Broken)]++;
            Object.DestroyImmediate(build.Group.gameObject);
        }
    }

    // 칸 하나 : 무너짐 → 형태를 뽑고, 맞는 회전(구역 방향 + 90도 단위)을 찾아 놓는다. 못 놓으면 빈 칸으로 둔다.
    // 형태와 배치도는 무너짐, 실패와 상관없이 같은 순서로 뽑아 다시 실행해도 같다
    private static void PlaceBuildingLot(LandmarkContext context, District district, int lot)
    {
        System.Random random = context.CityRandom;
        Vector2 center = district.Lots[lot];
        bool isCollapsed = random.NextDouble() < CollapseRatio;
        LotKind kind = PickLotKind(random);
        PieceSpec[] pieces = CreateBuildingPieces(random, kind);
        if (isCollapsed)
        {
            context.City.CollapsedLots++;
            context.EmptyLots.Add(center);
            return;
        }

        LandmarkBuild build = BuildGroup(context, $"{LotKindLabels[(int)kind]} {lot + 1}", pieces, district.Group);
        if (build == null) return;

        LandmarkFit? fit = FitStructure(context, build, center, district.Yaw, out SiteFailure failure);
        if (fit == null || fit.Value.BrokenCount > 0)
        {
            context.City.LotFailures[(int)(fit == null ? failure : SiteFailure.Broken)]++;
            Object.DestroyImmediate(build.Group.gameObject);
            context.EmptyLots.Add(center);
            return;
        }

        PlaceCityStructure(context, build, fit.Value);
        context.City.BuildingCounts[(int)kind]++;
    }

    private static LotKind PickLotKind(System.Random random)
    {
        double roll = random.NextDouble() * LotKindWeights.Sum();
        for (int i = 0; i < LotKindWeights.Length - 1; i++)
        {
            roll -= LotKindWeights[i];
            if (roll < 0) return (LotKind)i;
        }
        return (LotKind)(LotKindWeights.Length - 1);
    }

    // 묶음을 구역 방향 + 0, 90, 180, 270도로 돌려 보며, 자리 조건을 맞추는 회전 중 묻힘과 지면 높이 차가 가장 작은 것을 고른다.
    // 맞는 회전이 없으면 null과 마지막으로 걸린 이유
    private static LandmarkFit? FitStructure(LandmarkContext context, LandmarkBuild build, Vector2 center, float baseYaw, out SiteFailure failure)
    {
        LandmarkFit? best = null;
        failure = SiteFailure.None;
        for (int turn = 0; turn < 4; turn++)
        {
            float yaw = baseYaw + turn * 90f;
            RotateLandmark(build, yaw);
            SiteFailure site = CheckStructureSite(context, build, center);
            if (site != SiteFailure.None)
            {
                failure = site;
                continue;
            }

            LandmarkFit fit = MeasureLandmark(context.Terrain, build, center);
            fit.GroupYaw = yaw;
            if (best == null || IsBetterFit(fit, best.Value)) best = fit;
        }

        if (best != null) RotateLandmark(build, best.Value.GroupYaw);
        return best;
    }

    // 건물 터, 광장의 자리 조건 : 도시 사각형 안, 랜드마크·다른 구조물과 겹치지 않음, 조각 바닥이 강 보호 구역과 서식지 밖, 조각 바닥 경사 BuildingMaxSlope 이하
    private static SiteFailure CheckStructureSite(LandmarkContext context, LandmarkBuild build, Vector2 center)
    {
        if (!IsInsideRect(CityRect, center, build.Radius)) return SiteFailure.Outside;
        if (IsOverlapping(context, center, build.Radius)) return SiteFailure.Overlap;

        foreach (PlacedPiece piece in build.Pieces)
        {
            foreach (Vector2 point in piece.Points)
            {
                Vector2 world = point + center;
                if (IsInRiverProtect(world)) return SiteFailure.River;
                if (IsInHabitat(world)) return SiteFailure.Habitat;
            }
            if (GetSlope(context.Terrain, piece.Points[0] + center) > BuildingMaxSlope) return SiteFailure.Slope;
        }
        return SiteFailure.None;
    }

    // 랜드마크(바닥 반지름 + LandmarkClearance)나 먼저 놓은 도시 구조물(+ CityClearance)과 겹치는지
    private static bool IsOverlapping(LandmarkContext context, Vector2 center, float radius)
    {
        return context.Placed.Concat(context.CityBlockers).Any(blocker => Vector2.Distance(blocker.Center, center) < blocker.Radius + radius);
    }

    // 건물 터, 광장을 놓고 조각마다 높이를 맞춘다. 바닥 반지름 + CityClearance를 나무, 바위, 조각이 피할 자리로 남긴다
    private static void PlaceCityStructure(LandmarkContext context, LandmarkBuild build, LandmarkFit fit)
    {
        build.Group.position = ToWorld(fit.Position, GetGroundHeight(context.Terrain, fit.Position));
        context.City.BrokenCount += build.Pieces.Count(piece => GroundInstance(context.Terrain, piece.Transform.gameObject, StructureSinkRatio));
        context.CityBlockers.Add(new Blocker { Center = fit.Position, Radius = build.Radius + CityClearance });
        context.City.PieceCount += build.InstanceCount;
        context.AllPieces.AddRange(build.Group.Cast<Transform>());
        MergeStats(context.City.Stats, build.Stats);
    }

    private static PieceSpec[] CreateBuildingPieces(System.Random random, LotKind kind)
    {
        switch (kind)
        {
            case LotKind.SmallHouse:
                return CreateSmallHousePieces(random);
            case LotKind.LargeHouse:
                return CreateLargeHousePieces(random);
            case LotKind.Tower:
                return CreateTowerPieces(random);
            default:
                return CreateColonnadePieces(random);
        }
    }

    // 작은 집 (약 7.5 × 7.5m) : 뒷벽 Wall_Mid, 옆벽 Wall_Left·Wall_Right, 앞쪽 입구(문틀 또는 부서진 아치), 바닥 벽돌 1 ~ 2
    private static PieceSpec[] CreateSmallHousePieces(System.Random random)
    {
        var pieces = new List<PieceSpec>
        {
            Piece(WallMidPath, 0f, 3.75f, 0f),
            Piece(WallLeftPath, -3.75f, 0f, 90f),
            Piece(WallRightPath, 3.75f, 0f, 90f),
            Piece(random.NextDouble() < 0.5 ? DoorFramePath : DoorArchPath, 0f, -3.75f, 0f),
            Piece(PickPath(random, BrickPaths), 1f, 0.5f, RandomRange(random, 0f, 360f))
        };
        if (random.NextDouble() < 0.5) pieces.Add(Piece(PickPath(random, BrickPaths), -1.5f, -1f, RandomRange(random, 0f, 360f)));
        return pieces.ToArray();
    }

    // 큰 집 (약 15 × 9m) : 뒷벽 둘, 옆벽 둘, 앞벽은 한쪽만 남음, 안쪽 기둥 둘(반쪽·부러진 기둥), 바닥 벽돌 둘
    private static PieceSpec[] CreateLargeHousePieces(System.Random random)
    {
        bool isLeftFront = random.NextDouble() < 0.5;
        return new[]
        {
            Piece(WallLeftPath, -3.75f, 4.5f, 0f),
            Piece(WallRightPath, 3.75f, 4.5f, 0f),
            Piece(WallMidPath, -7.5f, 0f, 90f),
            Piece(WallMidPath, 7.5f, 0f, 90f),
            isLeftFront ? Piece(WallLeftPath, -3.75f, -4.5f, 0f) : Piece(WallRightPath, 3.75f, -4.5f, 0f),
            Piece(PickPath(random, InnerPillarPaths), -3f, 0f, RandomRange(random, 0f, 360f), true),
            Piece(PickPath(random, InnerPillarPaths), 3f, 0f, RandomRange(random, 0f, 360f), true),
            Piece(PickPath(random, BrickPaths), 0f, 1.5f, RandomRange(random, 0f, 360f)),
            Piece(PickPath(random, BrickPaths), -1.5f, -1.5f, RandomRange(random, 0f, 360f))
        };
    }

    // 탑 터 : 반지름 7.5m 원 위 8자리 중 이어진 0 ~ 2자리가 무너져 빠지고, 나머지(6 ~ 8)에 기둥 받침 또는 부러진 기둥
    private static PieceSpec[] CreateTowerPieces(System.Random random)
    {
        int missingCount = random.Next(0, TowerMissingMax + 1);
        int missingStart = random.Next(TowerSlotCount);
        var pieces = new List<PieceSpec>();
        for (int slot = 0; slot < TowerSlotCount; slot++)
        {
            bool isBase = random.NextDouble() < 0.5;
            string path = isBase ? PickPath(random, BasePaths) : PickPath(random, BrokenPillarPaths);
            if ((slot - missingStart + TowerSlotCount) % TowerSlotCount < missingCount) continue;

            float angle = slot * 360f / TowerSlotCount;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, TowerRadius);
            pieces.Add(Piece(path, offset.x, offset.z, angle, !isBase));
        }
        return pieces.ToArray();
    }

    // 기둥 회랑 : x = ±4.5m 두 줄, z -9 ~ 9m에 4.5m 간격 5자리씩. 자리마다 빠짐 20%, 받침만 30%, 부러진 기둥 35%, 온전한 기둥 15%
    private static PieceSpec[] CreateColonnadePieces(System.Random random)
    {
        var pieces = new List<PieceSpec>();
        foreach (float x in new[] { -ColonnadeRowOffset, ColonnadeRowOffset })
        {
            for (int slot = 0; slot < ColonnadeSlotsPerRow; slot++)
            {
                float z = (slot - (ColonnadeSlotsPerRow - 1) / 2f) * ColonnadeSpacing;
                double roll = random.NextDouble();
                float yaw = RandomRange(random, 0f, 360f);
                if (roll < ColonnadeMissingRatio) continue;
                if (roll < ColonnadeBaseRatio) pieces.Add(Piece(PickPath(random, BasePaths), x, z, yaw));
                else if (roll < ColonnadeBrokenRatio) pieces.Add(Piece(PickPath(random, BrokenPillarPaths), x, z, yaw, true));
                else pieces.Add(Piece(PillarFullPath, x, z, yaw, true));
            }
        }
        return pieces.ToArray();
    }

    // 광장 (약 25 × 30m) : 남북 계단, 4 × 4 바닥 벽돌 (종류와 방향을 번갈아), 네 모서리 기둥
    private static PieceSpec[] CreatePlazaPieces()
    {
        var pieces = new List<PieceSpec>
        {
            Piece(StairsPath, 0f, -PlazaStairsOffset, 0f),
            Piece(StairsPath, 0f, PlazaStairsOffset, 180f)
        };
        float start = -(PlazaBrickGrid - 1) / 2f * PlazaBrickSpacing;
        for (int gz = 0; gz < PlazaBrickGrid; gz++)
        {
            for (int gx = 0; gx < PlazaBrickGrid; gx++)
                pieces.Add(Piece(BrickPaths[(gx + gz) % BrickPaths.Length], start + gx * PlazaBrickSpacing, start + gz * PlazaBrickSpacing, 90f * ((gx * 3 + gz) % 4)));
        }

        Vector2[] corners = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };
        for (int i = 0; i < corners.Length; i++)
        {
            string path = PlazaCornerPaths[i];
            pieces.Add(Piece(path, corners[i].x * PlazaCornerOffset, corners[i].y * PlazaCornerOffset, 45f * i, path != BaseRoundPath));
        }
        return pieces.ToArray();
    }

    private static string PickPath(System.Random random, string[] paths)
    {
        return paths[random.Next(paths.Length)];
    }

    // ---------- 도시 담장 ----------

    private static void PlaceWalls(LandmarkContext context, Transform parent)
    {
        List<WallRun> runs = CreateWallRuns(context.CityRandom, context.City);
        for (int i = 0; i < runs.Count; i++)
            PlaceWallRun(context, runs[i], parent, i);
    }

    // 도시 사각형 안쪽 WallEdgeInset의 네 변 (남, 동, 서, 북 순서 = WallSideLabels). 네 변 모두 같은 규칙(줄마다 생략 확률, 남길 조각, 흔들림)을 쓴다.
    // 변마다 From → To 방향의 왼쪽이 도시 안쪽이 되게 잡았다
    private static List<WallRun> CreateWallRuns(System.Random random, CityStats city)
    {
        float xMin = CityRect.xMin + WallEdgeInset;
        float xMax = CityRect.xMax - WallEdgeInset;
        float zMin = CityRect.yMin + WallEdgeInset;
        float zMax = CityRect.yMax - WallEdgeInset;
        WallEdge[] edges =
        {
            new WallEdge { From = new Vector2(xMin, zMin), To = new Vector2(xMax, zMin) },
            new WallEdge { From = new Vector2(xMax, zMin), To = new Vector2(xMax, zMax) },
            new WallEdge { From = new Vector2(xMin, zMax), To = new Vector2(xMin, zMin) },
            new WallEdge { From = new Vector2(xMax, zMax), To = new Vector2(xMin, zMax) }
        };

        var runs = new List<WallRun>();
        for (int side = 0; side < edges.Length; side++)
            AddEdgeRuns(runs, random, edges[side], side, city);
        return runs;
    }

    // 변을 따라 줄 자리를 WallRunSpacing 간격으로 가운데 맞춰 둔다. 줄마다 생략 여부, 남길 조각 수, 시작 조각, 수직 흔들림, 방향 흔들림을
    // 생략 여부와 상관없이 항상 같은 횟수로 뽑아, 한 줄의 생략이 다음 줄 값을 바꾸지 않게 한다
    private static void AddEdgeRuns(List<WallRun> runs, System.Random random, WallEdge edge, int side, CityStats city)
    {
        float length = Vector2.Distance(edge.From, edge.To);
        int count = Mathf.FloorToInt((length - WallRunLength) / WallRunSpacing) + 1;
        Vector2 direction = (edge.To - edge.From).normalized;
        // 변과 수직인 도시 안쪽 방향 (줄 중심을 흔드는 방향, 가파르면 줄을 옮기는 방향)
        var normal = new Vector2(-direction.y, direction.x);
        float start = (length - (count - 1) * WallRunSpacing) / 2f;
        // Y축 회전 yaw는 묶음 +x를 월드 (cos yaw, -sin yaw)로 보낸다. 이 방향이 변 방향이 되게 한다
        float baseYaw = Mathf.Atan2(-direction.y, direction.x) * Mathf.Rad2Deg;
        int totalPieces = WallRunPieces.Length;
        for (int i = 0; i < count; i++)
        {
            bool isSkipped = random.NextDouble() < WallRunSkipRatio;
            float keepRatio = RandomRange(random, WallRunKeepMinRatio, WallRunKeepMaxRatio);
            double startRoll = random.NextDouble();
            float offset = RandomRange(random, -WallOffsetJitter, WallOffsetJitter);
            float jitter = RandomRange(random, -WallYawJitter, WallYawJitter);
            if (isSkipped)
            {
                city.WallRunSkippedCount++;
                continue;
            }

            // 이어진 pieceCount개 조각이 들어갈 수 있는 시작 자리(0 ~ totalPieces - pieceCount) 중 하나를 고른다
            int pieceCount = Mathf.Clamp(Mathf.RoundToInt(totalPieces * keepRatio), 1, totalPieces);
            int firstPiece = Mathf.Min((int)(startRoll * (totalPieces - pieceCount + 1)), totalPieces - pieceCount);
            runs.Add(new WallRun
            {
                Center = edge.From + direction * (start + i * WallRunSpacing) + normal * offset,
                Yaw = baseYaw + jitter,
                FirstPiece = firstPiece,
                PieceCount = pieceCount,
                Inward = normal,
                Side = side
            });
        }
    }

    // 줄에 남길 이어진 조각(배치도의 원래 자리 그대로)을 경사가 완만한 자리(FindWallShift)로 옮긴 뒤 조각마다 검사해,
    // 강 보호 구역·서식지·다른 구조물에 걸리거나 묻히는 조각을 빼서 끊긴 곳으로 남긴다.
    // 남은 조각이 WallRunMinPieces보다 적으면 줄을 지운다. 남은 조각마다 둘레 CityClearance를 피할 자리로 남긴다
    private static void PlaceWallRun(LandmarkContext context, WallRun run, Transform parent, int index)
    {
        PieceSpec[] runPieces = WallRunPieces.Skip(run.FirstPiece).Take(run.PieceCount).ToArray();
        LandmarkBuild build = BuildGroup(context, $"WallRun {index + 1}", runPieces, parent);
        if (build == null) return;

        CityStats city = context.City;
        RotateLandmark(build, run.Yaw);
        float? shift = FindWallShift(context.Terrain, build, run);
        if (shift == null)
        {
            city.WallRunNoSiteCount++;
            Object.DestroyImmediate(build.Group.gameObject);
            return;
        }

        Vector2 center = run.Center + run.Inward * shift.Value;
        build.Group.position = ToWorld(center, GetGroundHeight(context.Terrain, center));
        var kept = new List<Blocker>();
        foreach (PlacedPiece piece in build.Pieces)
        {
            GameObject instance = piece.Transform.gameObject;
            Bounds bounds = GetRendererBounds(instance);
            var blocker = new Blocker { Center = ToXZ(bounds.center), Radius = Mathf.Max(bounds.extents.x, bounds.extents.z) };
            SiteFailure failure = CheckWallPiece(context, blocker, GetFootprintPoints(bounds));
            if (failure == SiteFailure.None && GroundInstance(context.Terrain, instance, StructureSinkRatio)) failure = SiteFailure.Broken;
            if (failure != SiteFailure.None)
            {
                city.WallRemoved[(int)failure]++;
                Object.DestroyImmediate(instance);
                continue;
            }
            kept.Add(blocker);
        }
        MergeStats(city.Stats, build.Stats);

        if (kept.Count < WallRunMinPieces)
        {
            city.WallRunDroppedCount++;
            Object.DestroyImmediate(build.Group.gameObject);
            return;
        }

        foreach (Blocker blocker in kept)
            context.CityBlockers.Add(new Blocker { Center = blocker.Center, Radius = blocker.Radius + CityClearance });

        // 줄 방향으로 남은 조각의 양 끝 사이 거리를 줄 길이로 본다
        Vector2 axis = ToXZ(build.Group.right).normalized;
        Vector2 origin = ToXZ(build.Group.position);
        float[] along = kept.Select(blocker => Vector2.Dot(blocker.Center - origin, axis)).ToArray();
        float runLength = along.Max() - along.Min();
        city.WallRunCount++;
        city.WallRunsBySide[run.Side]++;
        city.WallShiftSum += shift.Value;
        city.WallShiftMin = Mathf.Min(city.WallShiftMin, shift.Value);
        city.WallShiftMax = Mathf.Max(city.WallShiftMax, shift.Value);
        city.WallLength += runLength;
        city.WallLengthMin = Mathf.Min(city.WallLengthMin, runLength);
        city.WallLengthMax = Mathf.Max(city.WallLengthMax, runLength);
        List<Transform> pieces = build.Group.Cast<Transform>().ToList();
        city.PieceCount += pieces.Count;
        context.AllPieces.AddRange(pieces);
    }

    // 흔들린 줄 자리에서 도시 안쪽으로 0, WallInwardStep, ... WallInwardMax m 옮겨 보며, 조각마다 바닥 표본점(가운데와 둘레 8곳) 경사가
    // 모두 WallMaxSlope 이하인 첫 거리. 묶음을 원점에서 줄 방향으로 돌려 둔 배치도 좌표(조각 Points)로 미리 계산한다. 없으면 null
    private static float? FindWallShift(Terrain terrain, LandmarkBuild build, WallRun run)
    {
        for (int step = 0; step * WallInwardStep <= WallInwardMax; step++)
        {
            float shift = step * WallInwardStep;
            Vector2 center = run.Center + run.Inward * shift;
            bool isGentle = build.Pieces.All(piece => piece.Points.All(point => GetSlope(terrain, point + center) <= WallMaxSlope));
            if (isGentle) return shift;
        }
        return null;
    }

    private static SiteFailure CheckWallPiece(LandmarkContext context, Blocker piece, Vector2[] points)
    {
        if (!CityRect.Contains(piece.Center)) return SiteFailure.Outside;
        if (IsOverlapping(context, piece.Center, piece.Radius)) return SiteFailure.Overlap;
        foreach (Vector2 point in points)
        {
            if (IsInRiverProtect(point)) return SiteFailure.River;
            if (IsInHabitat(point)) return SiteFailure.Habitat;
        }
        return SiteFailure.None;
    }

    // ---------- 높이 맞춤, 렌더러 ----------

    // 밑면을 바닥 둘레의 가장 낮은 땅보다 (높이 × sinkRatio)만큼 아래에 맞춘다. 비탈 아래쪽이 뜨지 않고 위쪽은 땅에 묻힌다.
    // 둘레 표본 위치(XZ)는 렌더러 범위로, 밑면과 높이는 메시 정점으로 잰다 (메시 범위가 실제 모양보다 아래로 잡힌 prefab이 있어서).
    // 옮긴 뒤 정점 밑면을 다시 재서 가장 낮은 땅보다 높으면 뜬 것으로 본다. 뜨거나 많이 묻혔으면 true
    private static bool GroundInstance(Terrain terrain, GameObject instance, float sinkRatio)
    {
        float[] grounds = GetFootprintPoints(GetRendererBounds(instance)).Select(point => GetGroundHeight(terrain, point)).ToArray();
        float lowest = grounds.Min();
        (float bottom, float top) = GetMeshHeightRange(instance);
        float height = top - bottom;
        instance.transform.position += Vector3.up * (lowest - height * sinkRatio - bottom);

        bool isFloating = GetMeshHeightRange(instance).Bottom > lowest + HeightTolerance;
        return isFloating || IsBuried(lowest, grounds.Max(), height, sinkRatio);
    }

    // 실제 메시 정점의 가장 낮은, 높은 월드 Y
    private static (float Bottom, float Top) GetMeshHeightRange(GameObject instance)
    {
        return GetMeshRange(instance, Vector3.up);
    }

    // 실제 메시 정점을 axis(단위 벡터) 방향으로 잰 가장 작은, 큰 값. LODGroup이 있으면 LOD0 렌더러만 본다 (LOD1 이하는 같은 자리의 낮은 단계 메시라 겹친다)
    private static (float Bottom, float Top) GetMeshRange(GameObject instance, Vector3 axis)
    {
        var lowerLodRenderers = new HashSet<Renderer>(instance.GetComponentsInChildren<LODGroup>()
            .SelectMany(group => group.GetLODs().Skip(1))
            .SelectMany(lod => lod.renderers));

        float bottom = float.MaxValue;
        float top = float.MinValue;
        foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>())
        {
            if (lowerLodRenderers.Contains(renderer)) continue;

            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;

            Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                float value = Vector3.Dot(toWorld.MultiplyPoint3x4(vertex), axis);
                bottom = Mathf.Min(bottom, value);
                top = Mathf.Max(top, value);
            }
        }
        return (bottom, top);
    }

    private static Bounds GetRendererBounds(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
            bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    // 렌더러 범위 바닥의 가운데와 둘레 8곳 (월드 XZ)
    private static Vector2[] GetFootprintPoints(Bounds bounds)
    {
        Vector2 center = ToXZ(bounds.center);
        var points = new Vector2[RingOffsets.Length + 1];
        points[0] = center;
        for (int i = 0; i < RingOffsets.Length; i++)
        {
            Vector2 offset = RingOffsets[i] * FootprintInset;
            points[i + 1] = center + new Vector2(offset.x * bounds.extents.x, offset.y * bounds.extents.z);
        }
        return points;
    }

    // 충돌체 추가, Static 끔, GPU Resident Drawer 제외, 깨진 머티리얼 세기 (원본 prefab은 고치지 않는다)
    private static void FinishInstance(RenderSetup render, GameObject instance, string prefabName, InstanceStats stats)
    {
        if (MapTreeScatter.AddMissingCollider(instance, prefabName)) stats.ColliderCount++;

        // Static Batching과 섞지 않는다 (MapLayout.md 성능 기준)
        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetStaticEditorFlags(child.gameObject) != 0) GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
        }

        foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.sharedMaterials.Any(IsBrokenMaterial)) stats.BrokenMaterialCount++;
            if (renderer.sharedMaterials.All(material => material == null || SupportsGpuDriven(render, material.shader))) continue;
            if (renderer.GetComponent(render.DisallowType) != null) continue;

            // 절벽 조각(CliffAuto 셰이더)이 여기에 걸린다. 이번 배치도에는 절벽 조각이 없다
            renderer.gameObject.AddComponent(render.DisallowType);
            stats.DisallowCount++;
        }
    }

    // 분홍으로 보이는 머티리얼 (머티리얼 없음, 셰이더 없음, 오류 셰이더, 이 환경에서 지원하지 않는 셰이더)
    private static bool IsBrokenMaterial(Material material)
    {
        if (material == null || material.shader == null) return true;
        return material.shader.name == ErrorShaderName || !material.shader.isSupported;
    }

    private static bool SupportsGpuDriven(RenderSetup render, Shader shader)
    {
        if (render.GpuDrivenSupport.TryGetValue(shader, out bool isSupported)) return isSupported;

        isSupported = !GpuDrivenBrokenShaderNames.Contains(shader.name);
        render.GpuDrivenSupport[shader] = isSupported;
        return isSupported;
    }

    // ---------- 나무, 바위, 유적 조각 ----------

    // parents : [0] = Trees, [1] = Rocks (유적 조각도 Rocks 아래에 둔다)
    private static ScatterContext ScatterAll(Terrain terrain, Transform[] parents, RenderSetup render, List<Blocker> blockers)
    {
        Transform treeParent = parents[0];
        Transform rockParent = parents[1];
        var context = new ScatterContext
        {
            Terrain = terrain,
            Random = new System.Random(ScatterSeed),
            DebrisRandom = new System.Random(DebrisSeed),
            Render = render,
            Blockers = blockers,
            TreeGroups = new[]
            {
                CreateGroup(MapTreeScatter.MainTreePaths, treeParent, MapTreeScatter.TreeScaleMin, MapTreeScatter.TreeScaleMax, false),
                CreateGroup(MapTreeScatter.DeadTreePaths, treeParent, MapTreeScatter.TreeScaleMin, MapTreeScatter.TreeScaleMax, false),
                CreateGroup(MapTreeScatter.GiantTreePaths, treeParent, MapTreeScatter.TreeScaleMin, MapTreeScatter.TreeScaleMax, false)
            },
            RockGroup = CreateGroup(MapTreeScatter.RockPaths, rockParent, MapTreeScatter.RockScaleMin, MapTreeScatter.RockScaleMax, true),
            DebrisPrefabs = LoadPrefabs(DebrisPaths)
        };

        Rect south = MapGrid.GetRangeBounds(SouthCell, SouthCell);
        int columns = Mathf.RoundToInt(south.width / ScatterCellSize);
        int rows = Mathf.RoundToInt(south.height / ScatterCellSize);
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                var cellMin = new Vector2(south.xMin + x * ScatterCellSize, south.yMin + z * ScatterCellSize);
                Vector2 cellCenter = cellMin + Vector2.one * (ScatterCellSize / 2f);
                // 칸 중심이 강 보호 구역이나 서식지 안이면 칸 전체를 비운다 (밀도를 비우지 않은 칸 넓이로 대조하기 위함)
                if (IsCleared(cellCenter))
                {
                    context.ClearedCellCount++;
                    continue;
                }

                int category = IsInCityArea(cellCenter) ? CityCategory : (int)GetSouthZone(cellCenter);
                context.CellCounts[category]++;
                context.Cells[category].Add(cellMin);
                ScatterTrees(context, cellMin, category);
                ScatterRocks(context, cellMin, category);
            }
        }
        return context;
    }

    private static GameObject[] LoadPrefabs(string[] paths)
    {
        return paths
            .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + path + ".prefab"))
            .ToArray();
    }

    private static InstanceGroup CreateGroup(string[] paths, Transform parent, float scaleMin, float scaleMax, bool isRock)
    {
        return new InstanceGroup { Prefabs = LoadPrefabs(paths), Parent = parent, ScaleMin = scaleMin, ScaleMax = scaleMax, IsRock = isRock };
    }

    private static void ScatterTrees(ScatterContext context, Vector2 cellMin, int category)
    {
        int count = RollCount(context.Random, TreeDensityMin[category], TreeDensityMax[category]);
        for (int i = 0; i < count; i++)
        {
            TreeKind kind = PickTreeKind(context.Random, category);
            if (!TryAddInstance(context, cellMin, context.TreeGroups[(int)kind])) continue;

            context.TreeKindCounts[(int)kind]++;
            context.TreeCounts[category]++;
        }
    }

    private static void ScatterRocks(ScatterContext context, Vector2 cellMin, int category)
    {
        int count = RollCount(context.Random, RockDensityMin[category], RockDensityMax[category]);
        for (int i = 0; i < count; i++)
        {
            if (TryAddInstance(context, cellMin, context.RockGroup)) context.RockCounts[category]++;
        }
    }

    // 먼저 도시의 빈 칸마다 흩어진 조각을 EmptyLotDebrisCount개씩 모아 두고(도시 묶음에 센다),
    // 묶음마다 (배치 넓이 × 목표 밀도)개가 될 때까지 그 묶음의 칸을 무작위로 뽑아 놓는다. 놓지 못하면 다른 칸을 다시 뽑는다
    private static void ScatterDebris(ScatterContext context)
    {
        foreach (Vector2 lot in context.EmptyLots)
        {
            for (int i = 0; i < EmptyLotDebrisCount; i++)
            {
                GameObject prefab = context.DebrisPrefabs[context.DebrisRandom.Next(context.DebrisPrefabs.Length)];
                if (!TryAddDebris(context, lot - Vector2.one * (EmptyLotSpread / 2f), EmptyLotSpread, prefab)) continue;

                context.DebrisCounts[CityCategory]++;
                context.EmptyLotDebrisCount++;
            }
        }

        float cellArea = ScatterCellSize * ScatterCellSize / HectareArea;
        for (int category = 0; category < CategoryCount; category++)
        {
            List<Vector2> cells = context.Cells[category];
            int target = Mathf.RoundToInt(cells.Count * cellArea * DebrisTargetDensity[category]);
            context.DebrisTargets[category] = target;
            for (int draw = 0; draw < target * DebrisMaxDrawsPerPiece && context.DebrisCounts[category] < target; draw++)
            {
                Vector2 cellMin = cells[context.DebrisRandom.Next(cells.Count)];
                GameObject prefab = context.DebrisPrefabs[context.DebrisRandom.Next(context.DebrisPrefabs.Length)];
                if (TryAddDebris(context, cellMin, ScatterCellSize, prefab)) context.DebrisCounts[category]++;
                else context.DebrisFailedDrawCount++;
            }
        }
    }

    // 1ha당 밀도를 칸 하나의 개수로 바꾼다. 소수 부분은 확률로 처리해 평균 밀도가 맞게 한다
    private static int RollCount(System.Random random, float densityMin, float densityMax)
    {
        float density = RandomRange(random, densityMin, densityMax);
        float expected = density * ScatterCellSize * ScatterCellSize / HectareArea;
        int count = Mathf.FloorToInt(expected);
        if (random.NextDouble() < expected - count) count++;
        return count;
    }

    // 고사목은 모든 묶음, 거대목은 나무 무리에서만 나온다 (남서쪽과 같은 규칙)
    private static TreeKind PickTreeKind(System.Random random, int category)
    {
        double roll = random.NextDouble();
        if (category == (int)MapTreeScatter.FieldZone.Forest)
        {
            if (roll < MapTreeScatter.GiantTreeRatio) return TreeKind.Giant;
            roll -= MapTreeScatter.GiantTreeRatio;
        }
        return roll < MapTreeScatter.DeadTreeRatio ? TreeKind.Dead : TreeKind.Main;
    }

    // 서식지 반경, 강 보호 구역, 랜드마크·도시 구조물 둘레, 급경사에는 놓지 않는다
    private static SkipReason GetSkipReason(ScatterContext context, Vector2 position)
    {
        if (IsInHabitat(position)) return SkipReason.Habitat;
        if (IsInRiverProtect(position)) return SkipReason.River;
        if (context.Blockers.Any(blocker => Vector2.Distance(blocker.Center, position) < blocker.Radius)) return SkipReason.Landmark;
        if (GetSlope(context.Terrain, position) > MapTreeScatter.MaxSlope) return SkipReason.Slope;
        return SkipReason.None;
    }

    // 나무, 바위용. 건너뛴 이유를 센다 (유적 조각은 다시 시도하므로 따로 센다)
    private static bool CanScatter(ScatterContext context, Vector2 position)
    {
        switch (GetSkipReason(context, position))
        {
            case SkipReason.Habitat:
                context.HabitatSkipCount++;
                return false;
            case SkipReason.River:
                context.RiverSkipCount++;
                return false;
            case SkipReason.Landmark:
                context.LandmarkSkipCount++;
                return false;
            case SkipReason.Slope:
                context.SlopeSkipCount++;
                return false;
            default:
                return true;
        }
    }

    // 무작위 값은 조건과 상관없이 같은 순서로 뽑아 결과가 항상 같게 한다
    private static bool TryAddInstance(ScatterContext context, Vector2 cellMin, InstanceGroup group)
    {
        System.Random random = context.Random;
        var position = new Vector2(
            cellMin.x + RandomRange(random, 0f, ScatterCellSize),
            cellMin.y + RandomRange(random, 0f, ScatterCellSize));
        GameObject prefab = group.Prefabs[random.Next(group.Prefabs.Length)];
        float scale = RandomRange(random, group.ScaleMin, group.ScaleMax);
        float yaw = RandomRange(random, 0f, 360f);
        if (!CanScatter(context, position)) return false;

        float ground = group.IsRock
            ? GetRingPoints(position, RockFootprintRadius * scale).Append(position).Min(point => GetGroundHeight(context.Terrain, point))
            : GetGroundHeight(context.Terrain, position);

        // prefab 연결을 유지한 채 놓는다. Static 플래그는 켜지 않는다 (GPU Resident Drawer로 그림)
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.Parent);
        instance.transform.SetPositionAndRotation(ToWorld(position, ground), Quaternion.Euler(0f, yaw, 0f));
        instance.transform.localScale = Vector3.one * scale;
        if (MapTreeScatter.AddMissingCollider(instance, prefab.name)) context.ColliderCount++;
        return true;
    }

    // 정사각형(areaMin에서 한 변 areaSize) 안에서 위치, 회전, 크기를 DebrisPlaceAttempts번까지 바꿔 가며, 놓기 전에 묻힘을 미리 재서 묻히지 않는 첫 시도에 놓는다.
    // 랜드마크 조각과 같은 크기(RuinScale배 ± 흔들림)와 기울기 흔들림에 지면 기울기 맞춤(최대 DebrisMaxAlignTilt)을 더한다.
    // 무작위 값은 조건과 상관없이 같은 순서로 뽑아 결과가 항상 같다. 모두 실패하면 조각을 지우고 false
    private static bool TryAddDebris(ScatterContext context, Vector2 areaMin, float areaSize, GameObject prefab)
    {
        System.Random random = context.DebrisRandom;
        DebrisPrefabStats stats = GetDebrisPrefabStats(context, prefab.name);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, context.RockGroup.Parent);
        for (int attempt = 0; attempt < DebrisPlaceAttempts; attempt++)
        {
            var position = new Vector2(
                areaMin.x + RandomRange(random, 0f, areaSize),
                areaMin.y + RandomRange(random, 0f, areaSize));
            Quaternion upright = CreateTilt(random) * Quaternion.Euler(0f, RandomRange(random, 0f, 360f), 0f);
            float scale = RuinScale * RandomRange(random, 1f - PieceScaleJitter, 1f + PieceScaleJitter);
            if (GetSkipReason(context, position) != SkipReason.None)
            {
                context.DebrisBlockedCount++;
                continue;
            }

            instance.transform.SetPositionAndRotation(ToWorld(position, GetGroundHeight(context.Terrain, position)), upright);
            instance.transform.localScale = Vector3.one * scale;
            // 원인 확인용 : 세운 채였다면 묻혔을지 잰다
            bool isUprightBuried = MeasureDebrisFit(context.Terrain, instance).IsBuried;

            instance.transform.rotation = CreateGroundAlign(context.Terrain, position) * upright;
            DebrisFit fit = MeasureDebrisFit(context.Terrain, instance);
            if (fit.IsBuried)
            {
                stats.RejectedCount++;
                continue;
            }

            instance.transform.position += Vector3.up * fit.Lift;
            FinishInstance(context.Render, instance, prefab.name, context.DebrisStats);
            context.Debris.Add(instance.transform);
            RecordDebris(stats, fit.Thickness, isUprightBuried, GetSlope(context.Terrain, position));

            // 옮긴 뒤 다시 재서, 가장 낮은 지면이 바닥 평면 위 (두께 × 묻는 비율)에 있고 묻힘 기준 안인지 확인한다
            DebrisFit placed = MeasureDebrisFit(context.Terrain, instance);
            if (placed.IsBuried || Mathf.Abs(placed.Lift) > HeightTolerance) context.DebrisBrokenCount++;
            return true;
        }

        Object.DestroyImmediate(instance);
        return false;
    }

    private static DebrisPrefabStats GetDebrisPrefabStats(ScatterContext context, string prefabName)
    {
        if (context.DebrisResults.TryGetValue(prefabName, out DebrisPrefabStats stats)) return stats;

        stats = new DebrisPrefabStats();
        context.DebrisResults[prefabName] = stats;
        return stats;
    }

    private static void RecordDebris(DebrisPrefabStats stats, float thickness, bool isUprightBuried, float slope)
    {
        stats.PlacedCount++;
        stats.ThicknessMin = Mathf.Min(stats.ThicknessMin, thickness);
        stats.ThicknessMax = Mathf.Max(stats.ThicknessMax, thickness);
        if (!isUprightBuried) return;

        stats.UprightBuriedCount++;
        stats.UprightBuriedMaxSlope = Mathf.Max(stats.UprightBuriedMaxSlope, slope);
    }

    // 조각 위쪽 축을 지면 법선 쪽으로 돌리는 회전. 지면 경사가 DebrisMaxAlignTilt보다 크면 그 각도까지만 돌린다
    private static Quaternion CreateGroundAlign(Terrain terrain, Vector2 position)
    {
        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        Vector3 normal = terrain.terrainData.GetInterpolatedNormal((position.x - origin.x) / size.x, (position.y - origin.z) / size.z);
        Quaternion align = Quaternion.FromToRotation(Vector3.up, normal);
        float angle = Vector3.Angle(Vector3.up, normal);
        return angle <= DebrisMaxAlignTilt ? align : Quaternion.Slerp(Quaternion.identity, align, DebrisMaxAlignTilt / angle);
    }

    // 조각 바닥 평면(조각 위쪽 축에 수직, 메시 정점 밑면을 지남)을 기준으로 바닥 표본점(가운데와 둘레 8곳)의 지면이 얼마나 위에 있는지 잰다.
    // 가장 낮은 지면이 바닥 평면 위 (두께 × StructureSinkRatio)가 되도록 옮길 거리와, 그때 랜드마크와 같은 묻힘 기준(IsBuried)을 넘는지 구한다.
    // 조각을 기울이지 않았으면 GroundInstance와 같은 계산이다
    private static DebrisFit MeasureDebrisFit(Terrain terrain, GameObject instance)
    {
        Vector3 axis = instance.transform.up;
        (float bottom, float top) = GetMeshRange(instance, axis);
        float thickness = top - bottom;
        float[] heights = GetFootprintPoints(GetRendererBounds(instance))
            .Select(point => Vector3.Dot(ToWorld(point, GetGroundHeight(terrain, point)), axis) - bottom)
            .ToArray();
        float lowest = heights.Min();
        float highest = heights.Max();
        return new DebrisFit
        {
            // 월드 위로 d만큼 옮기면 바닥 평면이 축 방향으로 d × axis.y만큼 올라간다
            Lift = (lowest - thickness * StructureSinkRatio) / axis.y,
            Thickness = thickness,
            IsBuried = IsBuried(lowest, highest, thickness, StructureSinkRatio)
        };
    }

    // ---------- 지형, 계산 ----------

    // SampleHeight는 Terrain 기준 높이라 Terrain의 y를 더한다
    private static float GetGroundHeight(Terrain terrain, Vector2 position)
    {
        return terrain.SampleHeight(ToWorld(position, 0f)) + terrain.transform.position.y;
    }

    private static float GetSlope(Terrain terrain, Vector2 position)
    {
        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        return terrain.terrainData.GetSteepness((position.x - origin.x) / size.x, (position.y - origin.z) / size.z);
    }

    // B3 경계에서 inset 이상 안쪽인지
    private static bool IsInsideSouth(Vector2 position, float inset)
    {
        return IsInsideRect(MapGrid.GetRangeBounds(SouthCell, SouthCell), position, inset);
    }

    // rect 경계에서 inset 이상 안쪽인지
    private static bool IsInsideRect(Rect rect, Vector2 position, float inset)
    {
        return position.x >= rect.xMin + inset && position.x <= rect.xMax - inset
            && position.y >= rect.yMin + inset && position.y <= rect.yMax - inset;
    }

    // 동서남북 네 곳
    private static IEnumerable<Vector2> GetRingPoints(Vector2 center, float radius)
    {
        yield return center + Vector2.up * radius;
        yield return center + Vector2.down * radius;
        yield return center + Vector2.left * radius;
        yield return center + Vector2.right * radius;
    }

    // 0도 = 북(+Z), 시계 방향
    private static float GetAzimuth(Vector2 position)
    {
        return (Mathf.Atan2(position.x, position.y) * Mathf.Rad2Deg + 360f) % 360f;
    }

    private static float RandomRange(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    private static Vector2 ToXZ(Vector3 world)
    {
        return new Vector2(world.x, world.z);
    }

    private static Vector3 ToWorld(Vector2 position, float height)
    {
        return new Vector3(position.x, height, position.y);
    }

    // ---------- 로그 ----------
    // 아래 로그는 검수(비율, 밀도가 승인 값 안인지, 강 보호, 랜드마크·도시 위치와 개수, 서식지 비움, 뜸·묻힘, B3 밖 배치, 성능 측정용 배치 수)에 쓰는 값이라 남긴다

    private static void LogZones(ScatterContext context)
    {
        float totalCells = context.CellCounts.Sum() + context.ClearedCellCount;
        float cellArea = ScatterCellSize * ScatterCellSize / HectareArea;
        var lines = new List<string>();
        for (int category = 0; category < CategoryCount; category++)
        {
            // 면적 비율에서는 비운 칸(강 보호 구역, 서식지)을 빈터로 친다. 밀도는 비우지 않은 칸 넓이로 나눈다
            int ratioCells = context.CellCounts[category] + (category == (int)MapTreeScatter.FieldZone.Open ? context.ClearedCellCount : 0);
            float area = context.CellCounts[category] * cellArea;
            lines.Add($"{CategoryLabels[category]} 비율 {ratioCells / totalCells:P1}, 배치 넓이 {area:F1}ha : " +
                      $"나무 {context.TreeCounts[category]}그루 {FormatDensity(context.TreeCounts[category], area, TreeDensityMin[category], TreeDensityMax[category])}, " +
                      $"바위·소품 {context.RockCounts[category]}개 {FormatDensity(context.RockCounts[category], area, RockDensityMin[category], RockDensityMax[category])}, " +
                      $"유적 조각 {context.DebrisCounts[category]}개 {FormatDensity(context.DebrisCounts[category], area, DebrisDensityMin[category], DebrisDensityMax[category])}");
        }

        // 도시 밖 세 묶음을 합친 밀도
        float outsideArea = Enumerable.Range(0, CityCategory).Sum(category => context.CellCounts[category]) * cellArea;
        int outsideTrees = Enumerable.Range(0, CityCategory).Sum(category => context.TreeCounts[category]);
        int outsideRocks = Enumerable.Range(0, CityCategory).Sum(category => context.RockCounts[category]);
        int outsideDebris = Enumerable.Range(0, CityCategory).Sum(category => context.DebrisCounts[category]);
        lines.Add($"도시 밖 합계 {outsideArea:F1}ha : 나무 1ha당 {(outsideArea > 0f ? outsideTrees / outsideArea : 0f):F1}, " +
                  $"바위·소품 1ha당 {(outsideArea > 0f ? outsideRocks / outsideArea : 0f):F1}, " +
                  $"유적 조각 {outsideDebris}개 {FormatDensity(outsideDebris, outsideArea, DebrisDensityMin[0], DebrisDensityMax[0])}");
        lines.Add($"비운 칸 (강 보호 구역, 서식지) {context.ClearedCellCount * cellArea:F1}ha");

        int[] kinds = context.TreeKindCounts;
        int debrisCount = context.DebrisCounts.Sum();
        Debug.Log($"[MapSouthScatter] 남쪽(B3) 묶음별 면적과 밀도 (도시 밖 구분 승인 비율 빈터 {OpenAreaRatio:P0} / 드문드문 {1f - ForestAreaRatio - OpenAreaRatio:P0} / " +
                  $"나무 무리 {ForestAreaRatio:P0}, 경계는 B3 전체 기준) :\n" + string.Join("\n", lines));
        Debug.Log($"[MapSouthScatter] 나무 {kinds.Sum()}그루 (주력 {kinds[(int)TreeKind.Main]}, 고사목 {kinds[(int)TreeKind.Dead]} ({kinds[(int)TreeKind.Dead] / Mathf.Max(1f, kinds.Sum()):P1}), " +
                  $"거대목 {kinds[(int)TreeKind.Giant]}), 바위·소품 {context.RockCounts.Sum()}개, 흩어진 유적 조각 {debrisCount}개, " +
                  $"충돌체 추가 나무·바위 {context.ColliderCount} / 유적 조각 {context.DebrisStats.ColliderCount}, " +
                  $"유적 조각 깨진 머티리얼·렌더러 없음 {context.DebrisStats.BrokenMaterialCount}, Disallow 붙임 {context.DebrisStats.DisallowCount} / " +
                  $"건너뜀 서식지 {context.HabitatSkipCount}, 강 보호 구역 {context.RiverSkipCount}, 랜드마크·구조물 둘레 {context.LandmarkSkipCount}, 경사 {context.SlopeSkipCount}");
        LogDebris(context);
    }

    // 유적 조각 목표와 결과, 종류별 묻힘 원인 (두께, 세운 채였다면 묻혔을 수와 그 경사)
    private static void LogDebris(ScatterContext context)
    {
        string targets = string.Join(", ", Enumerable.Range(0, CategoryCount)
            .Select(category => $"{CategoryLabels[category]} {context.DebrisCounts[category]}/{context.DebrisTargets[category]} (목표 1ha당 {DebrisTargetDensity[category]})"));
        string prefabs = string.Join("\n", context.DebrisResults.OrderBy(pair => pair.Key).Select(pair =>
            $"  {pair.Key} : 놓음 {pair.Value.PlacedCount}, 기울여도 묻혀 버린 시도 {pair.Value.RejectedCount}, " +
            $"두께 {(pair.Value.PlacedCount > 0 ? $"{pair.Value.ThicknessMin:F2} ~ {pair.Value.ThicknessMax:F2}m" : "-")}, " +
            $"세운 채였다면 묻힘 {pair.Value.UprightBuriedCount} (그 자리 최대 경사 {pair.Value.UprightBuriedMaxSlope:F1}도)"));
        Debug.Log($"[MapSouthScatter] 유적 조각 놓음/목표 : {targets}. 도시 빈 칸에 모아 둔 조각 {context.EmptyLotDebrisCount}개 (도시에 포함), " +
                  $"끝내 못 놓은 뽑기 {context.DebrisFailedDrawCount}, 막힌 위치 시도 (서식지·강·랜드마크·구조물·경사) {context.DebrisBlockedCount}, " +
                  $"지면 맞춤 최대 기울기 {DebrisMaxAlignTilt}도, 놓은 뒤 다시 잰 뜸·묻힘 {context.DebrisBrokenCount} (0이어야 한다)\n" + prefabs);
    }

    private static string FormatDensity(int count, float area, float min, float max)
    {
        float density = area > 0f ? count / area : 0f;
        bool isInRange = density >= min && density <= max;
        return $"(1ha당 {density:F1}, 표 {min:F0} ~ {max:F0} {(isInRange ? "안" : "밖")})";
    }

    private static void LogLandmarks(LandmarkContext context)
    {
        // 랜드마크 3곳 중심 사이 가장 가까운 거리 (LandmarkMinSpacing 이상이어야 한다)
        List<Vector2> centers = context.Placed.Select(blocker => blocker.Center).ToList();
        float minSpacing = float.MaxValue;
        for (int i = 0; i < centers.Count; i++)
        {
            for (int j = i + 1; j < centers.Count; j++)
                minSpacing = Mathf.Min(minSpacing, Vector2.Distance(centers[i], centers[j]));
        }
        string spacingText = centers.Count > 1 ? $"{minSpacing:F1}m" : "-";
        Debug.Log($"[MapSouthScatter] 랜드마크 {context.Notes.Count}/{LandmarkSpecs.Length}곳, 중심 사이 최소 거리 {spacingText} (기준 {LandmarkMinSpacing}m 이상) :\n" +
                  string.Join("\n", context.Notes));
    }

    private static void LogCity(LandmarkContext context, List<District> districts, ScatterContext scatter)
    {
        CityStats city = context.City;
        int buildingCount = city.BuildingCounts.Sum();
        string kinds = string.Join(", ", Enumerable.Range(0, LotKindLabels.Length).Select(kind => $"{LotKindLabels[kind]} {city.BuildingCounts[kind]}"));
        Debug.Log($"[MapSouthScatter] 유적 도시 (x {CityRect.xMin:F0} ~ {CityRect.xMax:F0}, z {CityRect.yMin:F0} ~ {CityRect.yMax:F0}) 구역 {districts.Count}/{SettlementSpecs.Sum(spec => spec.Yaws.Length)}곳\n" +
                  string.Join("\n", city.DistrictNotes));
        Debug.Log($"[MapSouthScatter] 건물 터 {buildingCount}채 (목표 40 ~ 50, {(buildingCount >= 40 && buildingCount <= 50 ? "안" : "밖")}) : {kinds} / " +
                  $"광장 {city.PlazaCount}/{PlazaCount} (광장이 맞지 않은 칸 : {DescribeFailures(city.PlazaFailures)}) / " +
                  $"빈 칸 : 구역 자리 조건 미달 {city.SearchInvalidLots}, 무너진 칸 {city.CollapsedLots}, 놓지 못한 칸 {DescribeFailures(city.LotFailures)}");
        float averageLength = city.WallRunCount > 0 ? city.WallLength / city.WallRunCount : 0f;
        string lengthRange = city.WallRunCount > 0 ? $"{city.WallLengthMin:F0} ~ {city.WallLengthMax:F0}m" : "-";
        string sides = string.Join(", ", Enumerable.Range(0, WallSideLabels.Length).Select(side => $"{WallSideLabels[side]} {city.WallRunsBySide[side]}"));
        string shiftRange = city.WallRunCount > 0
            ? $"{city.WallShiftMin:F0} ~ {city.WallShiftMax:F0}m, 평균 {city.WallShiftSum / city.WallRunCount:F1}m"
            : "-";
        Debug.Log($"[MapSouthScatter] 담장 줄 {city.WallRunCount}개 (변별 {sides}) (확률로 생략한 줄 {city.WallRunSkippedCount}, " +
                  $"경사 때문에 자리 없음 {city.WallRunNoSiteCount} (안쪽 {WallInwardMax}m까지, 경사 {WallMaxSlope}도 이하), " +
                  $"남은 조각이 {WallRunMinPieces}개 미만이라 지운 줄 {city.WallRunDroppedCount}), " +
                  $"안쪽으로 옮긴 거리 {shiftRange}, " +
                  $"길이 합 {city.WallLength:F0}m (줄마다 {lengthRange}, 평균 {averageLength:F0}m), 줄에서 뺀 조각 : {DescribeFailures(city.WallRemoved)}");
        Debug.Log($"[MapSouthScatter] 유적 조각 총 {city.PieceCount + context.PieceCount + scatter.Debris.Count}개 : 도시 {city.PieceCount}, 랜드마크 {context.PieceCount}, 흩어진 {scatter.Debris.Count} / " +
                  $"뜨거나 묻힘 도시 {city.BrokenCount}, 랜드마크 {context.BrokenCount}, 흩어진 {scatter.DebrisBrokenCount} (모두 0이어야 한다) / " +
                  $"깨진 머티리얼·렌더러 없음 도시 {city.Stats.BrokenMaterialCount}, 랜드마크 {context.Stats.BrokenMaterialCount}, 흩어진 {scatter.DebrisStats.BrokenMaterialCount} / " +
                  $"Disallow 붙임 {city.Stats.DisallowCount + context.Stats.DisallowCount + scatter.DebrisStats.DisallowCount}, " +
                  $"충돌체 추가 {city.Stats.ColliderCount + context.Stats.ColliderCount + scatter.DebrisStats.ColliderCount}");
    }

    // 이유별 개수 (0인 이유는 뺀다)
    private static string DescribeFailures(int[] counts)
    {
        IEnumerable<string> parts = Enumerable.Range(1, counts.Length - 1)
            .Where(reason => counts[reason] > 0)
            .Select(reason => $"{SiteFailureLabels[reason]} {counts[reason]}");
        string text = string.Join(", ", parts);
        return text.Length > 0 ? text : "없음";
    }

    // 놓인 오브젝트를 다시 읽어 서식지 반경 안, 강 보호 구역 안, 물 위, B3 밖 개수(모두 0이어야 함)와 뜸, 묻힘, WallRuin 사용 수를 센다.
    // 강 거리는 피벗 위치로 잰다. 유적 조각과 랜드마크의 뜸, 묻힘은 놓을 때 잰 값을 더한다
    private static void LogPlacementCheck(Terrain terrain, ScatterContext scatter, LandmarkContext landmarks)
    {
        List<Transform> scattered = scatter.TreeGroups[0].Parent.Cast<Transform>()
            .Concat(scatter.RockGroup.Parent.Cast<Transform>()).ToList();
        List<Transform> pieces = landmarks.AllPieces;
        // 묶음 GameObject (랜드마크, City, 구역, 건물 터, 광장, Walls, 담장 줄). prefab 조각과 그 안쪽은 뺀다
        List<Transform> groups = landmarks.Parent.GetComponentsInChildren<Transform>(true)
            .Where(child => child != landmarks.Parent && !PrefabUtility.IsPartOfPrefabInstance(child)).ToList();
        List<Transform> allItems = scattered.Concat(pieces).ToList();

        int inHabitatCount = allItems.Count(child => IsInHabitat(ToXZ(child.position)));
        // 강 보호 구역 안 오브젝트를 다리 받침, 선착장 터(허용 예외)와 그 밖(0이어야 함)으로 나눠 센다
        List<Transform> nearRiver = allItems.Where(child => IsInRiverProtect(ToXZ(child.position))).ToList();
        int bridgeNearRiverCount = nearRiver.Count(child => child.parent == landmarks.BridgeGroup);
        int pierNearRiverCount = nearRiver.Count(child => child.parent == landmarks.PierGroup);
        int nearRiverCount = nearRiver.Count - bridgeNearRiverCount - pierNearRiverCount;
        int onWaterCount = allItems.Count(child => GetRiverDistance(ToXZ(child.position)) < BridgeMinRiverDistance);
        int wallRuinCount = allItems.Count(child => child.name == WallRuinName);
        // 서식지 표시는 HabitatCenter에 두므로 그 위치로 함께 센다
        Rect south = MapGrid.GetRangeBounds(SouthCell, SouthCell);
        int outsideCount = allItems.Concat(groups).Select(child => ToXZ(child.position)).Append(HabitatCenter)
            .Count(position => !south.Contains(position));

        int floatingCount = 0;
        int buriedCount = 0;
        foreach (Transform child in scattered)
        {
            if (scatter.Debris.Contains(child)) continue;

            float gap = child.position.y - GetGroundHeight(terrain, ToXZ(child.position));
            if (gap > HeightTolerance) floatingCount++;
            if (gap < -MaxBuryGap) buriedCount++;
        }

        float azimuth = GetAzimuth(HabitatCenter);
        string direction = DirectionLabels[Mathf.RoundToInt(azimuth / 45f) % DirectionLabels.Length];
        bool isSouth = azimuth >= SouthAzimuthMin && azimuth <= SouthAzimuthMax;
        Debug.Log($"[MapSouthScatter] 서식지 ({HabitatCenter.x:F0}, {GetGroundHeight(terrain, HabitatCenter):F1}, {HabitatCenter.y:F0}), " +
                  $"거점 기준 방위각 {azimuth:F1}도 ({direction}, {SouthAzimuthMin} ~ {SouthAzimuthMax}도 {(isSouth ? "안" : "밖")}), " +
                  $"반경 {HabitatRadius}m 안 오브젝트 {inHabitatCount}개 (0이어야 한다)");
        Debug.Log($"[MapSouthScatter] 강 중심 {MapSouthShaper.RiverProtectDistance}m 안 오브젝트 : 예외 다리 받침 {bridgeNearRiverCount}개, 선착장 터 {pierNearRiverCount}개 / " +
                  $"그 밖 {nearRiverCount}개 (0이어야 한다). 강 중심 {BridgeMinRiverDistance}m 안 오브젝트 (예외 포함) {onWaterCount}개 (0이어야 한다)");
        Debug.Log($"[MapSouthScatter] 뜨거나 묻힌 오브젝트 : 나무·바위 뜸 {floatingCount}, 묻힘 {buriedCount} / 유적 조각 {scatter.DebrisBrokenCount} / " +
                  $"랜드마크 {landmarks.BrokenCount} / 도시 {landmarks.City.BrokenCount}. {WallRuinName} 사용 {wallRuinCount}개, B3 밖에 놓인 오브젝트 {outsideCount}개 (모두 0이어야 한다)");

        // 묶음 GameObject와 서식지 표시(1)도 씬 GameObject라 함께 센다
        int total = allItems.Count + groups.Count + 1;
        Debug.Log($"[MapSouthScatter] South 배치 GameObject 총 {total}개 (나무·바위·흩어진 유적 조각 {scattered.Count}, 랜드마크·도시 조각 {pieces.Count}, " +
                  $"묶음 {groups.Count}, 서식지 표시 1). South/River는 세지 않았다");
    }
}
