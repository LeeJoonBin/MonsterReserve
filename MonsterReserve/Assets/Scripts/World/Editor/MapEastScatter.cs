using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 메뉴 25 : 동쪽 구역(C2, 데모 산맥을 옮긴 구릉진 혼합 숲)에 서식지 위치 표시, 랜드마크 3곳(버려진 수레길, 숲속 작은 사당, 쓰러진 나무 지대),
// 나무, 바위를 놓는다 (이슈 #15).
// 놓는 규칙 (사용자 승인 임시값)
// - 구분 : 새 노이즈(주기 ZoneNoisePeriod)를 C2 표본(서식지 밖)의 분위수로 나눠 숲 / 드문드문 / 빈터 = 45 / 35 / 20%. 서식지 반경 안은 빈터다.
// - 나무 1ha당 숲 60 ~ 100 / 드문드문 5 ~ 15 / 빈터 0. 칸 중심 지면이 MountainTopHeight(85m) 이상이면 구분과 상관없이 산 위 밀도 (나무 5 ~ 15, 바위 5 ~ 10).
//   바위는 그 밖 1 ~ 3. 경사 MapTreeScatter.MaxSlope(35°) 이상에는 나무, 바위를 두지 않는다.
// - 고사목 DeadTreeRatio(10%)는 모든 곳, 거대목 GiantTreeRatio(3%)는 숲에서만 (산 위는 숲이 아니다).
// - 나무 크기 0.6 ~ 1.5배. u(0 ~ 1 무작위)^지수로 뽑아, 숲 가장자리 띠(숲 노이즈가 숲 경계값 ~ 숲 안쪽 경계값)에서 안쪽으로 갈수록
//   지수를 EdgeScaleExponent → CoreScaleExponent로 바꿔 가장자리는 작은 나무, 안쪽은 큰 나무가 많게 한다. 드문드문, 산 위는 가장자리 지수를 쓴다.
// - 주력 침엽수 종류 비중 : 숲은 키 큰 종류(ForestMainWeights), 드문드문·산 위는 작은 종류(SmallMainWeights) 위주.
// - 서식지 : East/Habitat을 MapEastShaper.HabitatCenter의 지면 높이에 두고, 반경 MapEastShaper.HabitatRadius 안은 비운다.
// - 랜드마크 둘레 LandmarkClearance(8m)는 나무, 바위를 비운다. 사당, 쓰러진 나무 지대는 C2 경계에서 (LandmarkEdgeMargin + 바닥 반지름) 안쪽,
//   서로 LandmarkMinSpacing, 수레길 선에서 LandmarkRoadSpacing 이상 떨어진 곳에 둔다.
// 버려진 수레길 : 서쪽 경계(x 250, 거점 쪽) 열 중 z가 거점에서 RoadStartHalfRange 안인 칸에서 시작해, 동쪽으로 RoadEastReach 들어간 열까지
//   RoadGridSpacing 격자 위 비용이 가장 작은 길을 다익스트라로 찾는다. 1m당 비용 = 1 + 경사(제곱) + 바탕 높이 위로 오른 높이라
//   경사가 낮고 낮은 골짜기를 따라간다. 경사 RoadMaxSlope 넘는 칸은 지나지 않는다. 무작위가 없어 같은 지형이면 같은 길이고, 메뉴 26이 GetCartRoadPath로 같은 선을 칠한다.
//   길 위·옆에 부서진 수레, 떨어진 바퀴 2, 장작 무리, 길가 작은 돌을 길 시작에서 잰 거리와 길 옆 거리로 놓는다 (RoadPieces).
// 숲속 작은 사당 : 중심과 둘레 ShrineForestRingRadius 8곳이 모두 숲인 곳 중 (1) 묻히는 조각 (2) 바닥 아래 지면 높이 차가 가장 작은 곳.
//   기둥 받침 2 (하나에는 부러진 기둥을 올림), 선돌 1, 계단 벽돌 4. 둘레 max(ShrineClearRadius, 바닥 반지름 + 8m) 안은 나무를 비운다.
// 쓰러진 나무 지대 : 숲 가장자리 띠, 경사 FallenMaxSlope 이하, FallenUphillDistance 둘레에 FallenUphillRise 이상 높은 땅이 있는 곳(산 비탈 아래) 중
//   40 × 40m 지대 안 지면 높이 차가 가장 작은 곳 (상한 FallenMaxAreaRange, 넘으면 경고). 고사목(Conifer_08/09) 8 ~ 12그루를 비탈 위 → 아래 방향 ±10°로 눕힌다.
//   눕힌 고사목 맞춤은 메뉴 22 다리 맞춤을 따른다 : 줄기(축에서 FallenCoreRadius 안 정점)가 땅에 얹히는 높이를 정점마다 재고,
//   굴림 FallenRollStep 간격 × 기울기(지면 기울기 ± FallenPitchRange) 중 줄기가 가장 덜 뜨고 가지가 가장 덜 묻히는 것을 고른다.
//   그루마다 지대 안 FallenCandidateStep 격자 밑동 후보를 줄기 선 아래 지면이 덜 울퉁불퉁한 순으로 FallenFitCandidates곳 맞춰 보고,
//   뜸 FallenMaxTrunkGap, 묻힘 FallenMaxBranchBury 기준을 맞추는 첫 자리에 놓는다. 못 맞춘 그루는 빼고 더 뽑으며, 8그루를 못 채우면 경고한다.
//   원본 prefab의 CapsuleCollider(LOD마다 하나)가 나무와 함께 눕으므로 충돌체를 따로 붙이지 않는다. 몇몇 나무 밑동 뒤에 그루터기를 둔다.
// 다시 실행하면 East 아래 Trees, Rocks, Landmarks, Habitat의 자식을 지우고 같은 seed로 다시 만든 뒤 씬을 저장한다.
// 실행 순서 : ... → 23 → 24 → 25 → 26 → 10. 24(동쪽 지형)를 다시 실행하면 25, 26도 순서대로 다시 실행하고, 25를 다시 실행하면 26도 다시 실행한다.
// 지면·바닥 식생(메뉴 26)은 GetEastZone, GetCartRoadPath로 같은 구분과 길 선을 쓴다.
public static class MapEastScatter
{
    private enum TreeKind
    {
        Main = 0,
        Dead = 1,
        Giant = 2
    }

    // 선분(점이면 From == To) 둘레 Radius 안에는 나무, 바위를 두지 않는다
    private struct Blocker
    {
        public Vector2 From;
        public Vector2 To;
        public float Radius;
    }

    private class InstanceStats
    {
        public int ColliderCount;
        public int DisallowCount;
    }

    // GPU Resident Drawer 제외 판정에 함께 쓰는 값
    private class RenderSetup
    {
        public System.Type DisallowType;
        public Dictionary<Shader, bool> GpuDrivenSupport = new Dictionary<Shader, bool>();
    }

    // 한 번 실행하는 동안 함께 쓰는 값과 결과 개수 묶음. 구분 배열은 CategoryLabels 순서 (FieldZone 뒤에 산 위)
    private class ScatterContext
    {
        public Terrain Terrain;
        public System.Random Random;
        public List<Blocker> Blockers;
        // TreeKind 순서
        public GameObject[][] TreePrefabs;
        public GameObject[] RockPrefabs;
        public Transform TreeParent;
        public Transform RockParent;
        // 주력 나무 prefab마다 LOD0 정점 높이 (m, 크기 1)
        public float[] MainTreeHeights;
        public int[] CellCounts = new int[CategoryLabels.Length];
        // 산 위를 따로 떼지 않은 노이즈 구분만의 칸 수 (FieldZone 순서). 승인 비율과 견준다
        public int[] ZoneCellCounts = new int[3];
        public int[] TreeCounts = new int[CategoryLabels.Length];
        public int[] RockCounts = new int[CategoryLabels.Length];
        public int[] TreeKindCounts = new int[3];
        public int[,] MainTreeCounts = new int[CategoryLabels.Length, MapTreeScatter.MainTreePaths.Length];
        public int[] ScaleBinCounts = new int[ScaleBinEdges.Length - 1];
        // 숲 나무 크기 합과 수 [가장자리 띠, 안쪽]
        public float[] ForestScaleSums = new float[2];
        public int[] ForestScaleCounts = new int[2];
        public int HabitatCellCount;
        public int ColliderCount;
        public int HabitatSkipCount;
        public int LandmarkSkipCount;
        public int SlopeSkipCount;
    }

    // 랜드마크 조각 하나의 배치도 (묶음 중심, 묶음 회전 기준)
    private struct PieceSpec
    {
        // MapTreeScatter.PrefabFolder 아래 경로 (확장자 없음)
        public string Path;
        public Vector2 Offset;
        public float Yaw;
        public float Scale;
        // true면 고정 seed로 조금 기울이고 크기를 흔든다 (기둥, 선돌)
        public bool IsWobbly;
        // true면 땅이 아니라 바로 앞 조각의 윗면에 올리고 자기 높이 × StackSinkRatio만큼 파묻는다
        public bool IsStacked;
        public float StackSinkRatio;
    }

    // 수레길 조각 하나. 길 시작에서 길을 따라 잰 거리, 길 왼쪽(+, 동쪽으로 갈 때 북쪽)으로 벗어난 거리, 길 방향 기준 회전
    private struct RoadPieceSpec
    {
        public string Path;
        public float Distance;
        public float Side;
        public float Yaw;
        // 눕히는 회전 (x = 앞뒤, y = 좌우, 도). 기운 수레, 누운 바퀴에만 쓴다
        public Vector2 Tilt;
        public float Scale;
    }

    private class PlacedPiece
    {
        public PieceSpec Spec;
        public Transform Transform;
        // 묶음 중심 기준 렌더러 범위 바닥의 가운데와 둘레 8곳 (XZ)
        public Vector2[] Points;
        // 메시 정점 기준 높이 (m)
        public float Height;
    }

    // 원점에서 조립한 묶음. 자리를 찾은 뒤 통째로 옮긴다
    private class LandmarkBuild
    {
        public Transform Group;
        public List<PlacedPiece> Pieces = new List<PlacedPiece>();
        // 중심에서 가장 먼 렌더러 범위 가장자리까지의 수평 거리
        public float Radius;
        public InstanceStats Stats = new InstanceStats();
    }

    private struct SiteFit
    {
        public Vector2 Position;
        // 땅에 맞추면 많이 묻히는 조각 수
        public int BrokenCount;
        public float GroundRange;
    }

    // 쓰러진 나무 지대 자리
    private struct FallenSite
    {
        public Vector2 Center;
        // 나무가 쓰러진 방향 (둘레의 가장 높은 땅에서 중심 쪽, 단위 벡터)
        public Vector2 Direction;
        public float GroundRange;
        public float Rise;
    }

    // 고사목 prefab의 LOD0 정점 (루트 기준, 크기 1)
    private class TrunkModel
    {
        public GameObject Prefab;
        public Vector3[] Vertices;
        // 줄기 축에서 FallenCoreRadius 안의 정점 (줄기), 그 밖은 가지
        public bool[] IsCore;
        // 밑동(피벗)에서 꼭대기까지 길이 (m, 크기 1)
        public float Length;
    }

    // 눕힐 고사목 하나 (밑동 위치, 쓰러진 방향)
    private struct FallenTrunk
    {
        public TrunkModel Model;
        public Vector2 Base;
        public Vector2 Direction;
        public float Scale;
        public float Length;
    }

    private struct FallenFit
    {
        public Quaternion Rotation;
        public float PivotHeight;
        public float Pitch;
        // 줄기 밑면이 땅에서 가장 많이 뜬 높이, 가지가 땅속으로 가장 깊이 들어간 깊이 (m)
        public float TrunkGap;
        public float BranchBury;
        public float Score;
    }

    // 쓰러진 나무 지대 하나를 만드는 동안 함께 쓰는 값과 결과
    private class FallenBuild
    {
        public FallenSite Site;
        public Transform Group;
        public GroundPatch Ground;
        public int Target;
        public List<FallenTrunk> Trunks = new List<FallenTrunk>();
        public List<FallenFit> Fits = new List<FallenFit>();
        // 후보 자리를 모두 맞춰 봐도 기준(뜸, 묻힘)을 못 맞춰 뺀 그루 수
        public int RejectedCount;
        public int StumpCount;
        public InstanceStats Stats = new InstanceStats();
    }

    // 나무 한 그루를 놓기 전에 뽑은 값
    private struct TreeDraw
    {
        public TreeKind Kind;
        public int PrefabIndex;
        public Vector2 Position;
        public float Scale;
        public float Yaw;
    }

    // 쓰러진 나무 지대 둘레 지면 높이를 미리 읽어 둔 격자. 굴림·기울기 후보마다 정점 아래 지면을 빨리 읽기 위함이다
    private class GroundPatch
    {
        public Vector2 Origin;
        public float Spacing;
        public int Count;
        public float[,] Heights;

        // 쌍선형 보간 높이 (m). 범위 밖은 가장자리 값
        public float Sample(Vector2 position)
        {
            float gridX = Mathf.Clamp((position.x - Origin.x) / Spacing, 0f, Count - 1);
            float gridZ = Mathf.Clamp((position.y - Origin.y) / Spacing, 0f, Count - 1);
            int x0 = Mathf.Min((int)gridX, Count - 2);
            int z0 = Mathf.Min((int)gridZ, Count - 2);
            float south = Mathf.Lerp(Heights[z0, x0], Heights[z0, x0 + 1], gridX - x0);
            float north = Mathf.Lerp(Heights[z0 + 1, x0], Heights[z0 + 1, x0 + 1], gridX - x0);
            return Mathf.Lerp(south, north, gridZ - z0);
        }
    }

    // 수레길 찾기 격자. 번호 = 줄 × Columns + 열, 0열이 C2 서쪽 경계
    private class RoadGrid
    {
        public Vector2 Origin;
        public int Columns;
        public int Rows;
        public float[] Heights;
        public float[] Slopes;
        public float[] Costs;
        public int[] Previous;
        public bool[] IsDone;

        public Vector2 GetPosition(int index)
        {
            return Origin + new Vector2(index % Columns, index / Columns) * RoadGridSpacing;
        }
    }

    private class LandmarkContext
    {
        public Terrain Terrain;
        public RenderSetup Render;
        public Transform Parent;
        // 기둥·선돌 흔들림, 쓰러진 나무 수·자리·크기, 그루터기
        public System.Random Random;
        public List<Blocker> Blockers = new List<Blocker>();
        // 사당, 쓰러진 나무 지대 중심 (서로 거리 검사)
        public List<Vector2> Centers = new List<Vector2>();
        public List<Vector2> RoadPath;
        // 놓은 랜드마크 prefab 인스턴스 전부와 그중 눕힌 고사목
        public List<Transform> Pieces = new List<Transform>();
        public List<Transform> FallenTrees = new List<Transform>();
        // 땅에 맞춘 뒤 뜨거나 많이 묻힌 조각 수 (수레길, 사당, 그루터기)
        public int BrokenCount;
        // 기준을 넘게 뜬 줄기, 묻힌 가지가 있는 고사목 수
        public int FallenFloatCount;
        public int FallenBuryCount;
        public InstanceStats Stats = new InstanceStats();
        public List<string> Notes = new List<string>();
    }

    private const string RegionName = "East";
    private const string TreeParentName = "Trees";
    private const string RockParentName = "Rocks";
    private const string LandmarkParentName = "Landmarks";
    private const string HabitatName = "Habitat";
    private const string RoadLabel = "버려진 수레길";
    private const string ShrineLabel = "숲속 작은 사당";
    private const string FallenLabel = "쓰러진 나무 지대";
    // 쓰지 않는 prefab. 로그에서 0개인지 센다
    private const string WallRuinName = "Prefab_WallRuin";

    // 거점 기준 "동" 방위 범위 (도). 로그에서 서식지 방위가 이 안인지 본다
    private const float EastAzimuthMin = 67.5f;
    private const float EastAzimuthMax = 112.5f;

    // 앞 메뉴들의 seed(20261109 ~ 20261155)를 피한다 (이슈 #15 브리핑의 시작값)
    private const int ZoneSeed = 20261158;
    private const int ScatterSeed = 20261159;
    private const int LandmarkSeed = 20261160;

    private static readonly Rect EastBounds = MapGrid.GetRangeBounds(MapEastShaper.EastCell, MapEastShaper.EastCell);

    // ---------- 구분 (빈터 / 드문드문 / 숲) ----------
    // 숲 덩어리가 C2(1000 × 500m) 안에 여러 개 생기도록 중앙(250m)과 남동쪽 작은 무리(120m) 사이 주기를 쓴다 (임시값)
    private const float ZoneNoisePeriod = 200f;
    private const float OpenAreaRatio = 0.20f;
    private const float ForestAreaRatio = 0.45f;
    // 숲 중 노이즈가 높은 쪽 이 비율(C2 넓이 기준)을 숲 안쪽으로 본다. 숲 경계값 ~ 안쪽 경계값 사이(45 - 25 = 20%)가 가장자리 띠 (임시값)
    private const float ForestCoreAreaRatio = 0.25f;
    // 분위수를 구할 때 C2를 표본 조사하는 간격 (m)
    private const float ZoneSampleSpacing = 5f;
    // Perlin 입력이 음수가 되지 않도록 노이즈 위치를 양수 쪽으로 옮기는 범위
    private const float NoiseOffsetMin = 100f;
    private const float NoiseOffsetMax = 1000f;
    // 아래 두 값은 선언 순서대로 초기화되므로 EastBounds 뒤, ZoneThresholds 앞에 둔다
    private static readonly Vector2 ZoneNoiseOffset = CreateZoneNoiseOffset();
    // [드문드문 시작, 숲 시작, 숲 안쪽 시작]. 지형과 상관없는 노이즈라 한 번만 구한다
    private static readonly float[] ZoneThresholds = CalculateZoneThresholds();

    // ---------- 나무, 바위 ----------
    // 밀도 구분 : FieldZone (빈터, 드문드문, 숲) 뒤에 산 위
    private const int MountainCategory = 3;
    private static readonly string[] CategoryLabels = { "빈터", "드문드문", "숲", "산 위" };
    // 칸 중심 지면이 이 높이 이상이면 산 위 밀도를 쓴다 (m, 사용자 승인 임시값). 메뉴 26도 같은 높이를 쓸 수 있게 공개한다
    public const float MountainTopHeight = 85f;

    // 흩뿌리기 단위 칸 (10m × 10m)과 1ha 넓이 (MapTreeScatter와 같은 단위)
    private const float ScatterCellSize = 10f;
    private const float HectareArea = 10000f;

    // 밀도 (1ha당, CategoryLabels 순서, 사용자 승인 임시값)
    private static readonly float[] TreeDensityMin = { 0f, 5f, 60f, 5f };
    private static readonly float[] TreeDensityMax = { 0f, 15f, 100f, 15f };
    private static readonly float[] RockDensityMin = { 1f, 1f, 1f, 5f };
    private static readonly float[] RockDensityMax = { 3f, 3f, 3f, 10f };

    // 나무 종류 비율 (사용자 승인 임시값). 고사목은 모든 곳, 거대목은 숲에서만
    private const float DeadTreeRatio = 0.10f;
    private const float GiantTreeRatio = 0.03f;

    // 나무 크기 (사용자 승인 임시값). u^지수 : 지수 2면 평균 약 0.9배(작은 쪽), 0.5면 평균 약 1.2배(큰 쪽) (임시값)
    private const float TreeScaleMin = 0.6f;
    private const float TreeScaleMax = 1.5f;
    private const float EdgeScaleExponent = 2f;
    private const float CoreScaleExponent = 0.5f;
    // 로그의 크기 분포 구간 경계 (마지막 구간은 위 끝 포함)
    private static readonly float[] ScaleBinEdges = { 0.6f, 0.8f, 1.0f, 1.2f, 1.5f };

    // 주력 침엽수 종류 비중 (MapTreeScatter.MainTreePaths 순서 : 01, 02, 03, 04, 05, 10, 11, 임시값).
    // 원본 CapsuleCollider 높이 기준 16.1 / 19.5 / 20.4(메뉴 3이 붙이는 충돌체) / 18.6 / 10.4 / 19.5 / 25.7m.
    // 숲은 키 큰 03, 11을 3, 중간 02, 04, 10을 2, 작은 01, 05를 1. 드문드문·산 위는 작은 01, 05를 3, 나머지 1. 실측 높이는 로그로 확인한다
    private static readonly float[] ForestMainWeights = { 1f, 2f, 3f, 2f, 1f, 2f, 3f };
    private static readonly float[] SmallMainWeights = { 3f, 1f, 1f, 1f, 3f, 1f, 1f };

    // ---------- 랜드마크 공통 ----------
    private const float LandmarkEdgeMargin = 50f;
    private const float LandmarkClearance = 8f;
    // 사당과 쓰러진 나무 지대 중심 사이, 두 중심과 수레길 선 사이 최소 거리 (m, 서쪽 랜드마크 간격과 같은 값 / 임시값)
    private const float LandmarkMinSpacing = 150f;
    private const float LandmarkRoadSpacing = 80f;
    private const float LandmarkSearchStep = 10f;
    // 사당 중심 경사 상한 (서쪽 랜드마크와 같은 값)
    private const float LandmarkMaxSlope = 25f;
    // 밑면을 바닥 둘레의 가장 낮은 땅보다 (높이 × 이 값)만큼 아래에 맞춘다 (서쪽 신전 터, 야영지와 같은 값)
    private const float StructureSinkRatio = 0.05f;
    // 밑면에서 바닥 둘레의 가장 높은 땅까지가 높이 × 이 값을 넘으면 묻힌 것으로 센다
    private const float PieceMaxBuryRatio = 0.5f;
    // 기둥, 선돌의 기울기와 크기 흔들림 (서쪽 랜드마크와 같은 값)
    private const float PieceTiltMax = 4f;
    private const float PieceScaleJitter = 0.1f;

    // ---------- 버려진 수레길 ----------
    // 길 찾기 격자 간격, 동쪽으로 들어갈 거리(길이 약 200m가 되게), 목표 열 너머로 더 보는 폭 (m, 임시값)
    private const float RoadGridSpacing = 5f;
    private const float RoadEastReach = 180f;
    private const float RoadSearchMargin = 60f;
    // 시작 칸 : 서쪽 경계 열 중 z가 거점(0)에서 이 거리 안 (m, 임시값)
    private const float RoadStartHalfRange = 60f;
    // 이 경사를 넘는 칸은 지나지 않는다 (도, 임시값)
    private const float RoadMaxSlope = 15f;
    // 1m당 비용 = 1 + RoadSlopeWeight × (경사 / RoadMaxSlope)² + RoadHeightWeight × 바탕(30m) 위로 오른 높이 (임시값)
    private const float RoadSlopeWeight = 4f;
    private const float RoadHeightWeight = 0.1f;
    // 격자 길의 45° 꺾임을 고르는 이웃 수와 반복 횟수, 고른 뒤 다시 나누는 간격 (m)
    private const int RoadSmoothRadius = 3;
    private const int RoadSmoothPasses = 2;
    private const float RoadSampleStep = 2f;
    // 길 폭의 반 (MapLayout.md 길 폭 4m). 길 중심에서 (이 값 + LandmarkClearance) 안은 나무, 바위를 비운다
    private const float RoadHalfWidth = 2f;

    private const string CartPath = "Objects/Prefab_Cart";
    private const string CartWheelPath = "Objects/Prefab_CartWheel";
    private const string Firewood01Path = "Objects/Prefab_Firewood_01";
    private const string Firewood02Path = "Objects/Prefab_Firewood_02";
    private const string Firewood03Path = "Objects/Prefab_Firewood_03";
    private const string SmallRock01Path = "Stones/Prefab_SmallRock_01";
    private const string SmallRock02Path = "Stones/Prefab_SmallRock_02";

    // 수레길 배치도 (임시값). 장작만 1.3 ~ 1.5배, 수레·바퀴는 원래 크기 (서쪽 야영지와 같은 규칙). 길가 돌은 0.6 ~ 0.9배.
    // 길 가운데쯤 바퀴 하나가 빠져 기운 수레, 수레 옆과 길 건너에 누운 바퀴, 수레에서 쏟아진 장작과 길가에 버려진 장작 두 무리, 길 가장자리 작은 돌 6개
    private static readonly RoadPieceSpec[] RoadPieces =
    {
        new RoadPieceSpec { Path = CartPath, Distance = 118f, Side = 3.5f, Yaw = 20f, Tilt = new Vector2(0f, 12f), Scale = 1f },
        new RoadPieceSpec { Path = CartWheelPath, Distance = 122f, Side = 6f, Yaw = 70f, Tilt = new Vector2(0f, 82f), Scale = 1f },
        new RoadPieceSpec { Path = CartWheelPath, Distance = 131f, Side = -3.5f, Yaw = 150f, Tilt = new Vector2(0f, 78f), Scale = 1f },
        new RoadPieceSpec { Path = Firewood01Path, Distance = 112f, Side = 5.5f, Yaw = 30f, Scale = 1.4f },
        new RoadPieceSpec { Path = Firewood02Path, Distance = 114.5f, Side = 6f, Yaw = 100f, Scale = 1.3f },
        new RoadPieceSpec { Path = Firewood03Path, Distance = 125f, Side = 1.8f, Yaw = 200f, Scale = 1.5f },
        new RoadPieceSpec { Path = Firewood01Path, Distance = 58f, Side = -4.5f, Yaw = 10f, Scale = 1.3f },
        new RoadPieceSpec { Path = Firewood02Path, Distance = 59.5f, Side = -5.2f, Yaw = 80f, Scale = 1.5f },
        new RoadPieceSpec { Path = Firewood03Path, Distance = 60.5f, Side = -3.8f, Yaw = 160f, Scale = 1.4f },
        new RoadPieceSpec { Path = Firewood01Path, Distance = 172f, Side = 4.5f, Yaw = 250f, Scale = 1.5f },
        new RoadPieceSpec { Path = Firewood03Path, Distance = 173.5f, Side = 5.5f, Yaw = 300f, Scale = 1.3f },
        new RoadPieceSpec { Path = SmallRock01Path, Distance = 18f, Side = 2.6f, Yaw = 0f, Scale = 0.7f },
        new RoadPieceSpec { Path = SmallRock02Path, Distance = 42f, Side = -2.8f, Yaw = 60f, Scale = 0.9f },
        new RoadPieceSpec { Path = SmallRock01Path, Distance = 83f, Side = 2.9f, Yaw = 120f, Scale = 0.6f },
        new RoadPieceSpec { Path = SmallRock02Path, Distance = 101f, Side = -2.7f, Yaw = 200f, Scale = 0.8f },
        new RoadPieceSpec { Path = SmallRock01Path, Distance = 148f, Side = 2.8f, Yaw = 260f, Scale = 0.7f },
        new RoadPieceSpec { Path = SmallRock02Path, Distance = 188f, Side = -2.9f, Yaw = 330f, Scale = 0.9f }
    };

    // ---------- 숲속 작은 사당 ----------
    // 나무를 비우는 빈터 반지름 (m, 사용자 승인 약 10m). 바닥 반지름 + LandmarkClearance가 더 크면 그 값을 쓴다
    private const float ShrineClearRadius = 10f;
    // 중심과 이 반지름 8곳이 모두 숲이어야 한다 (숲 안쪽의 빈터가 되게, m, 임시값)
    private const float ShrineForestRingRadius = 20f;
    private const int ShrineForestRingCount = 8;
    private const float ShrineGroupYaw = 200f;

    // 사당 배치도 (임시값, 원래 크기). 앞쪽 두 받침 중 서쪽 받침에 부러진 기둥이 남았고, 뒤쪽에 선돌, 앞마당에 계단 벽돌이 흩어져 있다.
    // 서쪽 신전 터(19조각, 바닥 반지름 15m)보다 훨씬 작은 8조각
    private static readonly PieceSpec[] ShrinePieces =
    {
        new PieceSpec { Path = "Ruins/Prefab_ PillarBase_Square", Offset = new Vector2(-2.5f, 1.5f), Yaw = 10f, Scale = 1f },
        new PieceSpec { Path = "Ruins/Prefab_ PillarBroken", Offset = new Vector2(-2.5f, 1.5f), Yaw = 35f, Scale = 1f, IsWobbly = true, IsStacked = true, StackSinkRatio = 0.05f },
        new PieceSpec { Path = "Ruins/Prefab_ PillarBase_Round", Offset = new Vector2(2.5f, 1.5f), Yaw = 40f, Scale = 1f },
        new PieceSpec { Path = "StandingStones/Prefab_RockFormation_04", Offset = new Vector2(0f, 4.5f), Yaw = 180f, Scale = 1f, IsWobbly = true },
        new PieceSpec { Path = "Ruins/Prefab_Stairs_Brick_01", Offset = new Vector2(-1.2f, -1.5f), Yaw = 20f, Scale = 1f },
        new PieceSpec { Path = "Ruins/Prefab_Stairs_Brick_02", Offset = new Vector2(1.5f, -2.2f), Yaw = 70f, Scale = 1f },
        new PieceSpec { Path = "Ruins/Prefab_Stairs_Brick_03", Offset = new Vector2(-3.5f, -2.5f), Yaw = 140f, Scale = 1f },
        new PieceSpec { Path = "Ruins/Prefab_Stairs_Brick_01", Offset = new Vector2(3.8f, -0.8f), Yaw = 250f, Scale = 1f }
    };

    // ---------- 쓰러진 나무 지대 ----------
    // 지대 한 변, 고사목 수, 쓰러진 방향 흔들림 (사용자 승인 임시값)
    private const float FallenAreaSize = 40f;
    private const int FallenCountMin = 8;
    private const int FallenCountMax = 12;
    private const float FallenYawJitter = 10f;
    // 눕힌 고사목 크기 (임시값). 길이 19.5m × 0.7 ~ 1.2 = 13.7 ~ 23.4m로 40m 안에 들게 한다 (나무 크기 0.6 ~ 1.5배 안)
    private const float FallenScaleMin = 0.7f;
    private const float FallenScaleMax = 1.2f;
    // 줄기끼리 최소 간격 (m, 임시값)
    private const float FallenMinTrunkGap = 1.5f;
    // 지대(40 × 40m, 쓰러진 방향 기준) 안 지면 높이 차 상한과 재는 표본 수 (한 변, 임시값). 상한 안인 자리가 없으면 높이 차가 가장 작은 곳에 두고 경고한다
    private const float FallenMaxAreaRange = 1.5f;
    private const int FallenAreaSampleCount = 9;
    // 그루마다 밑동 후보 격자 간격 (m), 맞춰 볼 후보 수 (줄기 선 아래 지면이 덜 울퉁불퉁한 순), 기준을 못 맞춘 그루 대신 더 뽑는 수 (임시값)
    private const float FallenCandidateStep = 2f;
    private const int FallenFitCandidates = 5;
    private const int FallenSpareDraws = 6;
    // 후보 울퉁불퉁함 : 줄기 선과 양옆 FallenProfileSideOffset 선을 따라 FallenProfileStep마다 지면이 밑동~끝 직선에서 벗어난 범위 (m)
    private const float FallenProfileStep = 1f;
    private const float FallenProfileSideOffset = 1.5f;
    // 자리 조건 : 지대를 덮는 5 × 5 표본점 경사 상한, 비탈 아래인지 볼 둘레 거리와 표본 수, 둘레 최고 지면이 중심보다 높아야 하는 양 (임시값)
    private const float FallenMaxSlope = 15f;
    private const int FallenSiteSampleCount = 5;
    private const float FallenUphillDistance = 80f;
    private const int FallenUphillSampleCount = 16;
    private const float FallenUphillRise = 15f;
    // 줄기 축에서 이 반지름(크기 1 기준, m) 안의 정점을 줄기로 본다 (메뉴 22 다리와 같은 값)
    private const float FallenCoreRadius = 0.7f;
    // 굴림, 기울기 찾기 (도). 기울기는 밑동과 끝 지면을 이은 기울기 ± FallenPitchRange
    private const float FallenRollStep = 15f;
    private const float FallenPitchRange = 4f;
    private const float FallenPitchStep = 0.5f;
    // 줄기를 따라 뜬 높이를 재는 간격, 줄기를 가장 높은 땅보다 묻는 깊이 (m, 메뉴 22 다리와 같은 값)
    private const float FallenBinStep = 0.5f;
    private const float FallenSinkDepth = 0.05f;
    // 뜸, 묻힘으로 세는 기준 (m, 사용자 승인 임시값). 가지가 줄기 둘레로 몇 m 뻗은 침엽수라 줄기와 가지를 함께 땅에 붙일 수 없어,
    // 가지에 받쳐 줄기가 떠 있는 실제 쓰러진 나무 모습을 허용하도록 0.3 / 0.5m에서 완화했다
    private const float FallenMaxTrunkGap = 1.0f;
    private const float FallenMaxBranchBury = 1.2f;
    // 지면 격자 간격과 지대 둘레로 더 읽는 폭 (가지가 지대 밖으로 나가는 만큼, m)
    private const float FallenGroundSpacing = 0.5f;
    private const float FallenGroundMargin = 6f;
    // 비움 판정에 쓰는 줄기 반폭 (m, 메뉴 22 다리와 같은 값)
    private const float FallenTrunkHalfWidth = 1f;
    // 그루터기 : 앞쪽 나무 몇 그루의 밑동 뒤 StumpOffset에 둔다. 다른 줄기와 StumpMinTrunkDistance 안이면 뺀다 (임시값)
    private const string StumpPath = "Objects/Prefab_Stump";
    private const int StumpCountMin = 3;
    private const int StumpCountMax = 5;
    private const float StumpOffset = 1.2f;
    private const float StumpMinTrunkDistance = 1f;

    // ---------- 공통 ----------
    // 렌더러 범위 둘레 8곳은 AABB보다 FootprintInset만큼 안쪽을 본다 (서쪽, 남쪽, 남동쪽 배치와 같은 값)
    private const float FootprintInset = 0.7f;
    private static readonly Vector2[] RingOffsets =
    {
        new Vector2(-1f, -1f), new Vector2(0f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 0f),
        new Vector2(1f, 0f), new Vector2(-1f, 1f), new Vector2(0f, 1f), new Vector2(1f, 1f)
    };
    private const float MaxBuryGap = 3f;
    // 뜸 검수 (계산 오차만 허용)
    private const float HeightTolerance = 0.01f;

    // GPU Resident Drawer로 그리면 몸체가 안 보이는 것으로 확인된 셰이더 (서쪽, 남쪽, 남동쪽 배치와 같은 목록)
    private static readonly string[] GpuDrivenBrokenShaderNames = { "Polyart/Dreamscape CliffAuto", "M_Water_Lake_Amp" };
    private const string DisallowGpuDrivenTypeName = "UnityEngine.Rendering.DisallowGPUDrivenRendering, Unity.RenderPipelines.GPUDriven.Runtime";
    // 셰이더를 찾지 못하거나 컴파일에 실패하면 Unity가 대신 쓰는 분홍 셰이더
    private const string ErrorShaderName = "Hidden/InternalErrorShader";

    // 거점 기준 8방위. 0도 = 북(+Z), 시계 방향
    private static readonly string[] DirectionLabels = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };

    // 같은 위치는 항상 같은 구분을 돌려준다. 서식지 반경 안은 빈터다 (메뉴 26도 같은 구분을 쓴다). 산 위(85m 이상)는 부르는 쪽에서 따로 본다
    public static MapTreeScatter.FieldZone GetEastZone(Vector2 position)
    {
        if (IsInHabitat(position)) return MapTreeScatter.FieldZone.Open;

        float noise = SampleZoneNoise(position);
        if (noise >= ZoneThresholds[1]) return MapTreeScatter.FieldZone.Forest;
        if (noise >= ZoneThresholds[0]) return MapTreeScatter.FieldZone.Sparse;
        return MapTreeScatter.FieldZone.Open;
    }

    // 버려진 수레길 길 선 (월드 XZ, 서쪽 끝 → 동쪽, RoadSampleStep 간격). 무작위 없이 지형 높이와 경사만으로 찾아 같은 지형이면 같은 선이다.
    // 메뉴 26이 길을 칠할 때 같은 선을 쓴다. 조건에 맞는 길이 없으면 null
    public static List<Vector2> GetCartRoadPath(Terrain terrain)
    {
        List<Vector2> cells = FindRoadCells(terrain);
        if (cells == null) return null;
        return Resample(SmoothPath(cells), RoadSampleStep);
    }

    [MenuItem("Tools/Map/25. 동쪽 나무·바위·랜드마크·서식지 배치")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        Scene scene = terrain.gameObject.scene;
        if (scene != SceneManager.GetActiveScene())
        {
            Debug.LogError("[MapEastScatter] Game 씬을 활성 씬으로 두고 실행하세요.");
            return;
        }

        GameObject eastRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == RegionName);
        if (eastRoot == null)
        {
            Debug.LogError("씬 루트에 East가 없습니다. 먼저 'Tools/Map/1. 지형과 구역 부모 생성'을 실행하세요.");
            return;
        }

        System.Type disallowType = System.Type.GetType(DisallowGpuDrivenTypeName);
        if (disallowType == null)
        {
            Debug.LogError($"{DisallowGpuDrivenTypeName} 타입을 찾지 못했습니다. URP 패키지 버전이 바뀌었는지 확인하세요.");
            return;
        }

        Transform treeParent = ResetParent(eastRoot.transform, TreeParentName);
        Transform rockParent = ResetParent(eastRoot.transform, RockParentName);
        Transform landmarkParent = ResetParent(eastRoot.transform, LandmarkParentName);
        Transform habitat = ResetParent(eastRoot.transform, HabitatName);
        habitat.position = ToWorld(MapEastShaper.HabitatCenter, GetGroundHeight(terrain, MapEastShaper.HabitatCenter));

        var landmarks = new LandmarkContext
        {
            Terrain = terrain,
            Render = new RenderSetup { DisallowType = disallowType },
            Parent = landmarkParent,
            Random = new System.Random(LandmarkSeed)
        };
        PlaceCartRoad(landmarks);
        PlaceShrine(landmarks);
        PlaceFallenTrees(landmarks);

        ScatterContext scatter = ScatterAll(terrain, landmarks.Blockers, treeParent, rockParent);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        LogZones(scatter);
        LogTreeSizes(scatter);
        LogLandmarks(landmarks);
        LogPlacementCheck(terrain, scatter, landmarks);
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

    // ---------- 구분 ----------

    // 다른 구역과 무늬가 겹치지 않도록 새 seed로 노이즈 위치를 정한다
    private static Vector2 CreateZoneNoiseOffset()
    {
        var random = new System.Random(ZoneSeed);
        return new Vector2(RandomRange(random, NoiseOffsetMin, NoiseOffsetMax), RandomRange(random, NoiseOffsetMin, NoiseOffsetMax));
    }

    private static float SampleZoneNoise(Vector2 position)
    {
        return Mathf.PerlinNoise(position.x / ZoneNoisePeriod + ZoneNoiseOffset.x, position.y / ZoneNoisePeriod + ZoneNoiseOffset.y);
    }

    // C2(서식지 밖)를 표본 조사해 빈터 20%, 숲 45%, 숲 안쪽 25%가 되는 노이즈 경계값
    private static float[] CalculateZoneThresholds()
    {
        int columns = Mathf.RoundToInt(EastBounds.width / ZoneSampleSpacing);
        int rows = Mathf.RoundToInt(EastBounds.height / ZoneSampleSpacing);
        var samples = new List<float>(columns * rows);
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                var position = new Vector2(EastBounds.xMin + (x + 0.5f) * ZoneSampleSpacing, EastBounds.yMin + (z + 0.5f) * ZoneSampleSpacing);
                if (!IsInHabitat(position)) samples.Add(SampleZoneNoise(position));
            }
        }
        samples.Sort();

        int count = samples.Count;
        return new[]
        {
            samples[Mathf.RoundToInt(count * OpenAreaRatio)],
            samples[count - Mathf.RoundToInt(count * ForestAreaRatio)],
            samples[count - Mathf.RoundToInt(count * ForestCoreAreaRatio)]
        };
    }

    // 숲 경계값에서 0, 숲 안쪽 경계값 이상에서 1. 숲이 아니면 0
    private static float GetForestDepth(Vector2 position)
    {
        return Mathf.InverseLerp(ZoneThresholds[1], ZoneThresholds[2], SampleZoneNoise(position));
    }

    private static bool IsInHabitat(Vector2 position)
    {
        return Vector2.Distance(position, MapEastShaper.HabitatCenter) < MapEastShaper.HabitatRadius;
    }

    // 칸 중심 지면이 85m 이상이면 산 위, 아니면 노이즈 구분
    private static int GetCategory(Terrain terrain, Vector2 position)
    {
        if (GetGroundHeight(terrain, position) >= MountainTopHeight) return MountainCategory;
        return (int)GetEastZone(position);
    }

    // ---------- 버려진 수레길 ----------

    // 다익스트라 : 비용이 가장 작은 칸부터 확정하며, 처음 확정된 목표 열(RoadEastReach) 칸에서 거슬러 올라간 칸 목록.
    // 칸 수가 약 4천이라 가장 작은 칸을 매번 훑어 찾는다
    private static List<Vector2> FindRoadCells(Terrain terrain)
    {
        RoadGrid grid = CreateRoadGrid(terrain);
        int targetColumn = Mathf.RoundToInt(RoadEastReach / RoadGridSpacing);
        while (true)
        {
            int current = FindCheapestOpen(grid);
            if (current < 0) return null;

            grid.IsDone[current] = true;
            if (current % grid.Columns >= targetColumn) return TraceRoad(grid, current);
            RelaxNeighbors(grid, current);
        }
    }

    // C2 서쪽 경계부터 동쪽으로 (RoadEastReach + RoadSearchMargin), 남북은 경계에서 LandmarkEdgeMargin 안쪽
    private static RoadGrid CreateRoadGrid(Terrain terrain)
    {
        var grid = new RoadGrid
        {
            Origin = new Vector2(EastBounds.xMin, EastBounds.yMin + LandmarkEdgeMargin),
            Columns = Mathf.RoundToInt((RoadEastReach + RoadSearchMargin) / RoadGridSpacing) + 1,
            Rows = Mathf.RoundToInt((EastBounds.height - 2f * LandmarkEdgeMargin) / RoadGridSpacing) + 1
        };
        int count = grid.Columns * grid.Rows;
        grid.Heights = new float[count];
        grid.Slopes = new float[count];
        grid.Costs = new float[count];
        grid.Previous = new int[count];
        grid.IsDone = new bool[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 position = grid.GetPosition(i);
            grid.Heights[i] = GetGroundHeight(terrain, position);
            grid.Slopes[i] = GetSlope(terrain, position);
            grid.Costs[i] = float.PositiveInfinity;
            grid.Previous[i] = -1;
        }

        // 거점 쪽에서 시작한다 : 서쪽 경계 열 중 z가 거점(0)에서 RoadStartHalfRange 안인 칸
        for (int row = 0; row < grid.Rows; row++)
        {
            int index = row * grid.Columns;
            if (Mathf.Abs(grid.GetPosition(index).y) <= RoadStartHalfRange && grid.Slopes[index] <= RoadMaxSlope) grid.Costs[index] = 0f;
        }
        return grid;
    }

    // 확정하지 않은 칸 중 비용이 가장 작은 칸. 닿을 수 있는 칸이 없으면 -1
    private static int FindCheapestOpen(RoadGrid grid)
    {
        int cheapest = -1;
        float bestCost = float.PositiveInfinity;
        for (int i = 0; i < grid.Costs.Length; i++)
        {
            if (grid.IsDone[i] || grid.Costs[i] >= bestCost) continue;
            bestCost = grid.Costs[i];
            cheapest = i;
        }
        return cheapest;
    }

    private static void RelaxNeighbors(RoadGrid grid, int current)
    {
        int column = current % grid.Columns;
        int row = current / grid.Columns;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = column + dx;
                int nz = row + dz;
                if ((dx == 0 && dz == 0) || nx < 0 || nz < 0 || nx >= grid.Columns || nz >= grid.Rows) continue;

                int next = nz * grid.Columns + nx;
                if (grid.IsDone[next] || grid.Slopes[next] > RoadMaxSlope) continue;

                float length = new Vector2(dx, dz).magnitude * RoadGridSpacing;
                float cost = grid.Costs[current] + length * GetRoadStepWeight(grid, current, next);
                if (cost >= grid.Costs[next]) continue;
                grid.Costs[next] = cost;
                grid.Previous[next] = current;
            }
        }
    }

    // 1m당 비용 : 평지 1에 경사(제곱)와 바탕 높이 위로 오른 높이만큼 더한다. 경사가 낮고 낮은 골짜기를 따라가게 한다
    private static float GetRoadStepWeight(RoadGrid grid, int from, int to)
    {
        float slope = (grid.Slopes[from] + grid.Slopes[to]) / 2f / RoadMaxSlope;
        float rise = Mathf.Max(0f, (grid.Heights[from] + grid.Heights[to]) / 2f - MapTerrainSetup.BaseHeight);
        return 1f + RoadSlopeWeight * slope * slope + RoadHeightWeight * rise;
    }

    private static List<Vector2> TraceRoad(RoadGrid grid, int end)
    {
        var path = new List<Vector2>();
        for (int index = end; index >= 0; index = grid.Previous[index])
            path.Add(grid.GetPosition(index));
        path.Reverse();
        return path;
    }

    // 격자 칸을 이은 선의 45° 꺾임을 앞뒤 RoadSmoothRadius개 평균으로 고른다. 양 끝은 그대로 둔다
    private static List<Vector2> SmoothPath(List<Vector2> path)
    {
        List<Vector2> current = path;
        for (int pass = 0; pass < RoadSmoothPasses; pass++)
        {
            var next = new List<Vector2>(current.Count);
            for (int i = 0; i < current.Count; i++)
            {
                int reach = Mathf.Min(RoadSmoothRadius, Mathf.Min(i, current.Count - 1 - i));
                Vector2 sum = Vector2.zero;
                for (int j = i - reach; j <= i + reach; j++)
                    sum += current[j];
                next.Add(sum / (2 * reach + 1));
            }
            current = next;
        }
        return current;
    }

    // 선을 따라 step 간격으로 다시 나눈다. 마지막 점은 그대로 넣는다
    private static List<Vector2> Resample(List<Vector2> path, float step)
    {
        var result = new List<Vector2> { path[0] };
        // 마지막으로 넣은 점에서 지금 선분 끝까지 거리
        float carried = 0f;
        for (int i = 1; i < path.Count; i++)
        {
            Vector2 from = path[i - 1];
            Vector2 to = path[i];
            float segment = Vector2.Distance(from, to);
            float distance = step - carried;
            while (distance <= segment)
            {
                result.Add(Vector2.Lerp(from, to, distance / segment));
                distance += step;
            }
            carried = segment - (distance - step);
        }
        if (result[result.Count - 1] != path[path.Count - 1]) result.Add(path[path.Count - 1]);
        return result;
    }

    private static float GetPathLength(List<Vector2> path)
    {
        float length = 0f;
        for (int i = 1; i < path.Count; i++)
            length += Vector2.Distance(path[i - 1], path[i]);
        return length;
    }

    // 길 시작에서 distance만큼 간 점과 그 자리의 길 방향 (단위 벡터)
    private static Vector2 GetPathPoint(List<Vector2> path, float distance, out Vector2 tangent)
    {
        for (int i = 1; i < path.Count; i++)
        {
            float segment = Vector2.Distance(path[i - 1], path[i]);
            tangent = (path[i] - path[i - 1]).normalized;
            if (distance <= segment || i == path.Count - 1) return Vector2.Lerp(path[i - 1], path[i], Mathf.Clamp01(distance / segment));
            distance -= segment;
        }
        tangent = Vector2.right;
        return path[0];
    }

    private static float GetPathDistance(List<Vector2> path, Vector2 position)
    {
        float distance = float.PositiveInfinity;
        for (int i = 1; i < path.Count; i++)
            distance = Mathf.Min(distance, GetSegmentDistance(position, path[i - 1], path[i]));
        return distance;
    }

    // 길 선을 찾아 길 위·옆에 조각을 놓고, 길 중심에서 (길 폭의 반 + LandmarkClearance) 안을 비운다
    private static void PlaceCartRoad(LandmarkContext context)
    {
        List<Vector2> path = GetCartRoadPath(context.Terrain);
        if (path == null)
        {
            Debug.LogWarning($"[MapEastScatter] '{RoadLabel}' 길을 찾지 못했습니다. 경사 {RoadMaxSlope}도 이하 칸으로 이어지는 길이 없습니다. 길 찾기 상수를 확인하세요.");
            return;
        }

        GameObject[] prefabs = LoadPrefabs(RoadPieces.Select(piece => piece.Path), RoadLabel);
        if (prefabs == null) return;

        context.RoadPath = path;
        float length = GetPathLength(path);
        Vector2 middle = GetPathPoint(path, length / 2f, out _);
        Transform group = CreateChild(context.Parent, RoadLabel);
        group.position = ToWorld(middle, GetGroundHeight(context.Terrain, middle));

        var stats = new InstanceStats();
        int brokenCount = 0;
        for (int i = 0; i < RoadPieces.Length; i++)
        {
            GameObject instance = PlaceRoadPiece(context, group, prefabs[i], RoadPieces[i]);
            FinishInstance(context.Render, instance, prefabs[i].name, stats);
            if (GroundInstance(context.Terrain, instance, StructureSinkRatio)) brokenCount++;
            context.Pieces.Add(instance.transform);
        }
        for (int i = 1; i < path.Count; i++)
            context.Blockers.Add(new Blocker { From = path[i - 1], To = path[i], Radius = RoadHalfWidth + LandmarkClearance });

        context.BrokenCount += brokenCount;
        MergeStats(context.Stats, stats);
        LogRoadNote(context, group, brokenCount, stats);
    }

    // 길 방향을 기준으로 놓는다. 배치도 거리가 길보다 길면 길 끝에 둔다
    private static GameObject PlaceRoadPiece(LandmarkContext context, Transform group, GameObject prefab, RoadPieceSpec piece)
    {
        List<Vector2> path = context.RoadPath;
        Vector2 point = GetPathPoint(path, Mathf.Min(piece.Distance, GetPathLength(path)), out Vector2 tangent);
        var left = new Vector2(-tangent.y, tangent.x);
        Vector2 position = point + left * piece.Side;

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
        instance.transform.SetPositionAndRotation(
            ToWorld(position, GetGroundHeight(context.Terrain, position)),
            Quaternion.Euler(piece.Tilt.x, GetAzimuth(tangent) + piece.Yaw, piece.Tilt.y));
        instance.transform.localScale = Vector3.one * piece.Scale;
        return instance;
    }

    private static void LogRoadNote(LandmarkContext context, Transform group, int brokenCount, InstanceStats stats)
    {
        List<Vector2> path = context.RoadPath;
        float length = GetPathLength(path);
        float[] heights = path.Select(point => GetGroundHeight(context.Terrain, point)).ToArray();
        float maxSlope = path.Max(point => GetSlope(context.Terrain, point));
        Vector2 start = path[0];
        Vector2 end = path[path.Count - 1];
        context.Notes.Add($"{RoadLabel} : 시작 ({start.x:F0}, {start.y:F0}) → 끝 ({end.x:F0}, {end.y:F0}), 길이 {length:F1}m (목표 약 200m), " +
                          $"경사 최대 {maxSlope:F1}도 (찾을 때 칸 기준 {RoadMaxSlope}도 이하, 고른 뒤 다시 잼), 높이 {heights.Min():F1} ~ {heights.Max():F1}m, " +
                          $"비움 폭 길 중심에서 {RoadHalfWidth + LandmarkClearance}m, 조각 {RoadPieces.Length}개 (뜨거나 묻힘 {brokenCount}), " +
                          $"Disallow 붙임 {stats.DisallowCount}, 충돌체 추가 {stats.ColliderCount}\n  prefab : {DescribePrefabs(group)}");
    }

    // ---------- 숲속 작은 사당 ----------

    private static void PlaceShrine(LandmarkContext context)
    {
        LandmarkBuild build = BuildGroup(context, ShrineLabel, ShrinePieces, ShrineGroupYaw);
        if (build == null) return;

        SiteFit? fit = FindShrineSite(context, build);
        if (fit == null)
        {
            Debug.LogWarning($"[MapEastScatter] '{ShrineLabel}' 자리를 찾지 못했습니다 (바닥 반지름 {build.Radius:F1}m). 숲 조건, 간격 조건을 확인하세요.");
            Object.DestroyImmediate(build.Group.gameObject);
            return;
        }

        int brokenCount = PlaceGroupAt(context, build, fit.Value.Position);
        float clearRadius = Mathf.Max(ShrineClearRadius, build.Radius + LandmarkClearance);
        context.Blockers.Add(new Blocker { From = fit.Value.Position, To = fit.Value.Position, Radius = clearRadius });
        context.Centers.Add(fit.Value.Position);
        context.BrokenCount += brokenCount;
        MergeStats(context.Stats, build.Stats);

        Vector2 center = fit.Value.Position;
        context.Notes.Add($"{ShrineLabel} : 중심 ({center.x:F0}, {center.y:F0}) 지면 {GetGroundHeight(context.Terrain, center):F1}m, " +
                          $"바닥 반지름 {build.Radius:F1}m, 나무 비움 반지름 {clearRadius:F1}m (사용자 승인 약 {ShrineClearRadius}m, 바닥 반지름 + {LandmarkClearance}m 중 큰 값), " +
                          $"숲 안쪽 정도 {GetForestDepth(center):F2} (0 = 숲 경계, 1 = 숲 안쪽), 경사 {GetSlope(context.Terrain, center):F1}도, " +
                          $"바닥 아래 지면 높이 차 {fit.Value.GroundRange:F2}m, 조각 {build.Pieces.Count}개 (뜨거나 묻힘 {brokenCount}), " +
                          $"Disallow 붙임 {build.Stats.DisallowCount}, 충돌체 추가 {build.Stats.ColliderCount}\n  prefab : {DescribePrefabs(build.Group)}");
    }

    // 배치도대로 월드 원점에 조립한다. 묶음 중심이 원점이라 조각의 월드 XZ가 곧 중심 기준 위치가 된다
    private static LandmarkBuild BuildGroup(LandmarkContext context, string label, PieceSpec[] pieces, float groupYaw)
    {
        GameObject[] prefabs = LoadPrefabs(pieces.Select(piece => piece.Path), label);
        if (prefabs == null) return null;

        var build = new LandmarkBuild { Group = new GameObject(label).transform };
        build.Group.SetParent(context.Parent, false);
        build.Group.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, groupYaw, 0f));
        for (int i = 0; i < pieces.Length; i++)
        {
            GameObject instance = CreatePiece(context.Random, pieces[i], prefabs[i], build.Group);
            FinishInstance(context.Render, instance, prefabs[i].name, build.Stats);

            Bounds bounds = GetRendererBounds(instance);
            (float bottom, float top) = GetMeshHeightRange(instance);
            build.Pieces.Add(new PlacedPiece { Spec = pieces[i], Transform = instance.transform, Points = GetFootprintPoints(bounds), Height = top - bottom });
            build.Radius = Mathf.Max(build.Radius, ToXZ(bounds.center).magnitude + Mathf.Max(bounds.extents.x, bounds.extents.z));
        }
        return build;
    }

    // prefab 연결을 유지한 채 놓는다. 흔들림 값은 조각 순서대로 같은 seed에서 뽑아 다시 실행해도 같다 (서쪽 메뉴 12와 같은 방식)
    private static GameObject CreatePiece(System.Random random, PieceSpec piece, GameObject prefab, Transform group)
    {
        Quaternion rotation = Quaternion.Euler(0f, piece.Yaw, 0f);
        float scale = piece.Scale;
        if (piece.IsWobbly)
        {
            float tilt = RandomRange(random, 0f, PieceTiltMax);
            Vector3 tiltAxis = Quaternion.Euler(0f, RandomRange(random, 0f, 360f), 0f) * Vector3.right;
            rotation = Quaternion.AngleAxis(tilt, tiltAxis) * rotation;
            scale *= RandomRange(random, 1f - PieceScaleJitter, 1f + PieceScaleJitter);
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
        instance.transform.localPosition = new Vector3(piece.Offset.x, 0f, piece.Offset.y);
        instance.transform.localRotation = rotation;
        instance.transform.localScale = Vector3.one * scale;
        return instance;
    }

    // C2를 LandmarkSearchStep 격자로 돌며 조건을 맞추는 자리 중 (1) 묻히는 조각 (2) 바닥 아래 지면 높이 차가 가장 작은 곳. 같으면 남서쪽에서 먼저 만난 자리
    private static SiteFit? FindShrineSite(LandmarkContext context, LandmarkBuild build)
    {
        float inset = LandmarkEdgeMargin + build.Radius;
        SiteFit? best = null;
        for (float z = EastBounds.yMin + inset; z <= EastBounds.yMax - inset; z += LandmarkSearchStep)
        {
            for (float x = EastBounds.xMin + inset; x <= EastBounds.xMax - inset; x += LandmarkSearchStep)
            {
                var position = new Vector2(x, z);
                if (!CanPlaceShrine(context, build.Radius, position)) continue;

                SiteFit fit = MeasureGroup(context.Terrain, build, position);
                if (best == null || IsBetterFit(fit, best.Value)) best = fit;
            }
        }
        return best;
    }

    // 중심과 둘레 8곳이 숲, 산 위가 아님, 경사 LandmarkMaxSlope 이하, 서식지·다른 랜드마크·수레길과 떨어짐
    private static bool CanPlaceShrine(LandmarkContext context, float radius, Vector2 position)
    {
        if (GetEastZone(position) != MapTreeScatter.FieldZone.Forest) return false;
        if (!IsLandmarkSpacingValid(context, position, radius)) return false;
        if (GetSlope(context.Terrain, position) > LandmarkMaxSlope) return false;
        if (GetGroundHeight(context.Terrain, position) >= MountainTopHeight) return false;
        return GetCirclePoints(position, ShrineForestRingRadius, ShrineForestRingCount)
            .All(point => GetEastZone(point) == MapTreeScatter.FieldZone.Forest);
    }

    // 바닥이 서식지 반경 밖, 다른 랜드마크 중심과 LandmarkMinSpacing 이상, 수레길 선과 LandmarkRoadSpacing 이상
    private static bool IsLandmarkSpacingValid(LandmarkContext context, Vector2 position, float radius)
    {
        if (Vector2.Distance(position, MapEastShaper.HabitatCenter) < MapEastShaper.HabitatRadius + radius) return false;
        if (context.Centers.Any(center => Vector2.Distance(center, position) < LandmarkMinSpacing)) return false;
        return context.RoadPath == null || GetPathDistance(context.RoadPath, position) >= LandmarkRoadSpacing;
    }

    // 묶음 중심을 position에 옮겼을 때 조각마다 바닥 아래 지면을 재서, 땅에 맞추면 많이 묻히는 조각 수를 센다. 올린 조각은 지면을 재지 않는다
    private static SiteFit MeasureGroup(Terrain terrain, LandmarkBuild build, Vector2 position)
    {
        float groundMin = float.MaxValue;
        float groundMax = float.MinValue;
        int brokenCount = 0;
        foreach (PlacedPiece piece in build.Pieces)
        {
            if (piece.Spec.IsStacked) continue;

            float[] grounds = piece.Points.Select(point => GetGroundHeight(terrain, point + position)).ToArray();
            float lowest = grounds.Min();
            float highest = grounds.Max();
            groundMin = Mathf.Min(groundMin, lowest);
            groundMax = Mathf.Max(groundMax, highest);
            if (IsBuried(lowest, highest, piece.Height, StructureSinkRatio)) brokenCount++;
        }
        return new SiteFit { Position = position, BrokenCount = brokenCount, GroundRange = groundMax - groundMin };
    }

    private static bool IsBetterFit(SiteFit fit, SiteFit best)
    {
        if (fit.BrokenCount != best.BrokenCount) return fit.BrokenCount < best.BrokenCount;
        return fit.GroundRange < best.GroundRange;
    }

    // 조립한 묶음을 옮긴 뒤 조각마다 높이를 맞춘다. 묶음 GameObject는 중심의 지면 높이에 둔다. 뜨거나 많이 묻힌 조각 수를 돌려준다
    private static int PlaceGroupAt(LandmarkContext context, LandmarkBuild build, Vector2 position)
    {
        build.Group.position = ToWorld(position, GetGroundHeight(context.Terrain, position));
        int brokenCount = 0;
        for (int i = 0; i < build.Pieces.Count; i++)
        {
            PlacedPiece piece = build.Pieces[i];
            if (piece.Spec.IsStacked) StackOnPiece(piece, build.Pieces[i - 1]);
            else if (GroundInstance(context.Terrain, piece.Transform.gameObject, StructureSinkRatio)) brokenCount++;
            context.Pieces.Add(piece.Transform);
        }
        return brokenCount;
    }

    // 앞 조각의 정점 윗면에 정점 밑면을 맞추고 (자기 높이 × StackSinkRatio)만큼 파묻는다 (서쪽 메뉴 12와 같은 방식)
    private static void StackOnPiece(PlacedPiece piece, PlacedPiece below)
    {
        float belowTop = GetMeshHeightRange(below.Transform.gameObject).Top;
        (float bottom, float top) = GetMeshHeightRange(piece.Transform.gameObject);
        piece.Transform.position += Vector3.up * (belowTop - (top - bottom) * piece.Spec.StackSinkRatio - bottom);
    }

    // ---------- 쓰러진 나무 지대 ----------

    private static void PlaceFallenTrees(LandmarkContext context)
    {
        FallenSite? found = FindFallenSite(context);
        if (found == null)
        {
            Debug.LogWarning($"[MapEastScatter] '{FallenLabel}' 자리를 찾지 못했습니다. 숲 가장자리, 경사 {FallenMaxSlope}도, 비탈 아래 조건을 확인하세요.");
            return;
        }

        FallenSite site = found.Value;
        TrunkModel[] models = MapTreeScatter.DeadTreePaths.Select(LoadTrunkModel).ToArray();
        float halfDiagonal = FallenAreaSize / 2f * Mathf.Sqrt(2f);
        var build = new FallenBuild
        {
            Site = site,
            Group = CreateChild(context.Parent, FallenLabel),
            Ground = CreateGroundPatch(context.Terrain, site.Center, halfDiagonal + FallenGroundMargin),
            Target = context.Random.Next(FallenCountMin, FallenCountMax + 1)
        };
        build.Group.position = ToWorld(site.Center, GetGroundHeight(context.Terrain, site.Center));
        PlaceFallenTreesInArea(context, build, models);
        if (build.Trunks.Count < FallenCountMin)
            Debug.LogWarning($"[MapEastScatter] '{FallenLabel}' 고사목을 {build.Trunks.Count}그루만 놓았습니다 (최소 {FallenCountMin}). 뜸·묻힘 기준을 맞추는 자리가 모자랍니다.");

        build.StumpCount = PlaceStumps(context, build);
        context.Centers.Add(site.Center);
        MergeStats(context.Stats, build.Stats);
        LogFallenNote(context, build);
    }

    // 숲 가장자리 띠, 산 위가 아님, 지대를 덮는 5 × 5 표본점 경사 FallenMaxSlope 이하, 둘레에 FallenUphillRise 이상 높은 땅(산 비탈 아래)인 곳 중
    // 지대(쓰러진 방향으로 돌린 40 × 40m) 안 지면 높이 차가 가장 작은 곳. 높이 차 상한(FallenMaxAreaRange) 안인 곳이 있으면 그중 하나가 골라지고,
    // 없으면 높이 차가 가장 작은 곳에 두고 경고한다
    private static FallenSite? FindFallenSite(LandmarkContext context)
    {
        float halfDiagonal = FallenAreaSize / 2f * Mathf.Sqrt(2f);
        float inset = LandmarkEdgeMargin + halfDiagonal;
        FallenSite? best = null;
        for (float z = EastBounds.yMin + inset; z <= EastBounds.yMax - inset; z += LandmarkSearchStep)
        {
            for (float x = EastBounds.xMin + inset; x <= EastBounds.xMax - inset; x += LandmarkSearchStep)
            {
                var position = new Vector2(x, z);
                if (GetEastZone(position) != MapTreeScatter.FieldZone.Forest || GetForestDepth(position) >= 1f) continue;
                if (!IsLandmarkSpacingValid(context, position, halfDiagonal)) continue;
                if (GetGroundHeight(context.Terrain, position) >= MountainTopHeight) continue;
                if (!IsGentleArea(context.Terrain, position, halfDiagonal)) continue;

                FallenSite? site = MeasureUphill(context.Terrain, position);
                if (site == null) continue;
                FallenSite measured = site.Value;
                measured.GroundRange = MeasureAreaRange(context.Terrain, measured);
                if (best == null || measured.GroundRange < best.Value.GroundRange) best = measured;
            }
        }

        if (best != null && best.Value.GroundRange > FallenMaxAreaRange)
            Debug.LogWarning($"[MapEastScatter] '{FallenLabel}' : 지대 안 지면 높이 차 {FallenMaxAreaRange}m 이하인 자리가 없어 가장 작은 곳 ({best.Value.GroundRange:F2}m)에 둡니다.");
        return best;
    }

    // 중심 둘레 한 변 2 × halfSize 정사각형 5 × 5 표본점이 모두 경사 FallenMaxSlope 이하인지 (지대를 어느 방향으로 돌려도 덮는다)
    private static bool IsGentleArea(Terrain terrain, Vector2 center, float halfSize)
    {
        float step = 2f * halfSize / (FallenSiteSampleCount - 1);
        for (int z = 0; z < FallenSiteSampleCount; z++)
        {
            for (int x = 0; x < FallenSiteSampleCount; x++)
            {
                Vector2 point = center + new Vector2(x * step - halfSize, z * step - halfSize);
                if (GetSlope(terrain, point) > FallenMaxSlope) return false;
            }
        }
        return true;
    }

    // 쓰러진 방향으로 돌린 40 × 40m 지대 안 FallenAreaSampleCount² 표본점의 지면 높이 차 (m)
    private static float MeasureAreaRange(Terrain terrain, FallenSite site)
    {
        var across = new Vector2(-site.Direction.y, site.Direction.x);
        float half = FallenAreaSize / 2f;
        float step = FallenAreaSize / (FallenAreaSampleCount - 1);
        float low = float.MaxValue;
        float high = float.MinValue;
        for (int i = 0; i < FallenAreaSampleCount; i++)
        {
            for (int j = 0; j < FallenAreaSampleCount; j++)
            {
                Vector2 point = site.Center + site.Direction * (i * step - half) + across * (j * step - half);
                float height = GetGroundHeight(terrain, point);
                low = Mathf.Min(low, height);
                high = Mathf.Max(high, height);
            }
        }
        return high - low;
    }

    // 둘레 FallenUphillDistance 표본 중 가장 높은 땅이 중심보다 FallenUphillRise 이상 높으면, 그곳에서 중심 쪽을 쓰러진 방향으로 삼는다 (비탈 위 → 아래)
    private static FallenSite? MeasureUphill(Terrain terrain, Vector2 center)
    {
        float centerHeight = GetGroundHeight(terrain, center);
        Vector2 highest = center;
        float highestHeight = float.MinValue;
        foreach (Vector2 point in GetCirclePoints(center, FallenUphillDistance, FallenUphillSampleCount))
        {
            float height = GetGroundHeight(terrain, point);
            if (height <= highestHeight) continue;
            highestHeight = height;
            highest = point;
        }

        float rise = highestHeight - centerHeight;
        if (rise < FallenUphillRise) return null;
        return new FallenSite { Center = center, Direction = (center - highest).normalized, Rise = rise };
    }

    // prefab을 잠깐 원점에 놓고 LOD0 정점을 루트 기준으로 읽은 뒤 지운다 (메뉴 22와 같은 방식)
    private static TrunkModel LoadTrunkModel(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + path + ".prefab");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;
        Vector3[] vertices = GetLod0Vertices(instance).ToArray();
        Object.DestroyImmediate(instance);

        bool[] isCore = vertices.Select(vertex => new Vector2(vertex.x, vertex.z).magnitude <= FallenCoreRadius).ToArray();
        return new TrunkModel { Prefab = prefab, Vertices = vertices, IsCore = isCore, Length = vertices.Max(vertex => vertex.y) };
    }

    private static GroundPatch CreateGroundPatch(Terrain terrain, Vector2 center, float halfSize)
    {
        int count = Mathf.CeilToInt(2f * halfSize / FallenGroundSpacing) + 1;
        var patch = new GroundPatch
        {
            Origin = center - Vector2.one * halfSize,
            Spacing = FallenGroundSpacing,
            Count = count,
            Heights = new float[count, count]
        };
        for (int z = 0; z < count; z++)
        {
            for (int x = 0; x < count; x++)
                patch.Heights[z, x] = GetGroundHeight(terrain, patch.Origin + new Vector2(x, z) * FallenGroundSpacing);
        }
        return patch;
    }

    // 그루마다 모델·크기·방향을 뽑고 지대 안에서 기준(뜸 FallenMaxTrunkGap, 묻힘 FallenMaxBranchBury)을 맞추는 자리를 찾아 놓는다.
    // 못 맞춘 그루는 빼고 FallenSpareDraws번까지 더 뽑아 목표 수를 채운다. 무작위는 랜드마크 seed만 써서 나무·바위 배치(ScatterSeed)와 섞이지 않는다
    private static void PlaceFallenTreesInArea(LandmarkContext context, FallenBuild build, TrunkModel[] models)
    {
        int maxDraws = build.Target + FallenSpareDraws;
        for (int draw = 0; draw < maxDraws && build.Trunks.Count < build.Target; draw++)
        {
            FallenTrunk shape = DrawTrunk(context.Random, models, build.Site);
            if (TryFitTrunk(build, shape, out FallenTrunk trunk, out FallenFit fit)) PlaceFallenTree(context, build, trunk, fit);
            else build.RejectedCount++;
        }
    }

    // 모델, 크기, 방향(쓰러진 방향 ± FallenYawJitter)만 뽑는다. 밑동 자리는 TryFitTrunk가 찾는다
    private static FallenTrunk DrawTrunk(System.Random random, TrunkModel[] models, FallenSite site)
    {
        TrunkModel model = models[random.Next(models.Length)];
        float scale = RandomRange(random, FallenScaleMin, FallenScaleMax);
        Vector2 direction = Rotate(site.Direction, RandomRange(random, -FallenYawJitter, FallenYawJitter));
        return new FallenTrunk { Model = model, Base = site.Center, Direction = direction, Scale = scale, Length = model.Length * scale };
    }

    // 후보 자리 중 줄기 선 아래 지면이 덜 울퉁불퉁한 FallenFitCandidates곳을 차례로 맞춰 보고, 기준을 맞추는 첫 자리를 고른다
    private static bool TryFitTrunk(FallenBuild build, FallenTrunk shape, out FallenTrunk trunk, out FallenFit fit)
    {
        foreach (FallenTrunk candidate in FindTrunkCandidates(build, shape).Take(FallenFitCandidates))
        {
            FallenFit candidateFit = FitFallenTree(build.Ground, candidate);
            if (candidateFit.TrunkGap > FallenMaxTrunkGap || candidateFit.BranchBury > FallenMaxBranchBury) continue;

            trunk = candidate;
            fit = candidateFit;
            return true;
        }
        trunk = shape;
        fit = default;
        return false;
    }

    // 지대 안 FallenCandidateStep 격자의 밑동 자리 중 줄기 전체가 지대 안이고 놓은 줄기와 FallenMinTrunkGap 이상 떨어진 곳을 울퉁불퉁한 정도가 작은 순으로
    // (같으면 격자 순서, OrderBy는 순서를 지킨다)
    private static List<FallenTrunk> FindTrunkCandidates(FallenBuild build, FallenTrunk shape)
    {
        FallenSite site = build.Site;
        var across = new Vector2(-site.Direction.y, site.Direction.x);
        float half = FallenAreaSize / 2f;
        var candidates = new List<FallenTrunk>();
        var roughness = new List<float>();
        for (float along = -half; along <= half; along += FallenCandidateStep)
        {
            for (float side = -half; side <= half; side += FallenCandidateStep)
            {
                FallenTrunk trunk = shape;
                trunk.Base = site.Center + site.Direction * along + across * side;
                if (!IsInsideFallenArea(site, GetTip(trunk))) continue;
                if (build.Trunks.Any(other => GetSegmentGap(trunk.Base, GetTip(trunk), other.Base, GetTip(other)) < FallenMinTrunkGap)) continue;

                candidates.Add(trunk);
                roughness.Add(MeasureRoughness(build.Ground, trunk));
            }
        }
        return Enumerable.Range(0, candidates.Count).OrderBy(index => roughness[index]).Select(index => candidates[index]).ToList();
    }

    // 줄기 선과 양옆 선을 따라 지면이 (선마다 밑동~끝 지면을 이은 직선)에서 벗어난 값의 범위 (m). 작을수록 줄기가 덜 뜨고 가지가 덜 묻힌다
    private static float MeasureRoughness(GroundPatch ground, FallenTrunk trunk)
    {
        var across = new Vector2(-trunk.Direction.y, trunk.Direction.x);
        float low = float.MaxValue;
        float high = float.MinValue;
        int steps = Mathf.CeilToInt(trunk.Length / FallenProfileStep);
        for (int line = -1; line <= 1; line++)
        {
            Vector2 start = trunk.Base + across * (line * FallenProfileSideOffset);
            float startGround = ground.Sample(start);
            float endGround = ground.Sample(start + trunk.Direction * trunk.Length);
            for (int step = 0; step <= steps; step++)
            {
                float t = (float)step / steps;
                float deviation = ground.Sample(start + trunk.Direction * (trunk.Length * t)) - Mathf.Lerp(startGround, endGround, t);
                low = Mathf.Min(low, deviation);
                high = Mathf.Max(high, deviation);
            }
        }
        return high - low;
    }

    private static Vector2 GetTip(FallenTrunk trunk)
    {
        return trunk.Base + trunk.Direction * trunk.Length;
    }

    private static bool IsInsideFallenArea(FallenSite site, Vector2 point)
    {
        Vector2 local = point - site.Center;
        var across = new Vector2(-site.Direction.y, site.Direction.x);
        float half = FallenAreaSize / 2f;
        return Mathf.Abs(Vector2.Dot(local, site.Direction)) <= half && Mathf.Abs(Vector2.Dot(local, across)) <= half;
    }

    // 찾은 굴림·기울기로 고사목을 눕히고, 줄기를 따라 나무·바위를 비운다
    private static void PlaceFallenTree(LandmarkContext context, FallenBuild build, FallenTrunk trunk, FallenFit fit)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(trunk.Model.Prefab, build.Group);
        instance.transform.SetPositionAndRotation(ToWorld(trunk.Base, fit.PivotHeight), fit.Rotation);
        instance.transform.localScale = Vector3.one * trunk.Scale;
        FinishInstance(context.Render, instance, trunk.Model.Prefab.name, build.Stats);

        context.Pieces.Add(instance.transform);
        context.FallenTrees.Add(instance.transform);
        if (fit.TrunkGap > FallenMaxTrunkGap) context.FallenFloatCount++;
        if (fit.BranchBury > FallenMaxBranchBury) context.FallenBuryCount++;
        context.Blockers.Add(new Blocker { From = trunk.Base, To = GetTip(trunk), Radius = FallenTrunkHalfWidth + LandmarkClearance });
        build.Trunks.Add(trunk);
        build.Fits.Add(fit);
    }

    // 굴림 FallenRollStep 간격 × 기울기(밑동과 끝 지면을 이은 기울기 ± FallenPitchRange) 중 (줄기가 뜬 높이, 가지가 묻힌 깊이) 중 큰 값이 가장 작은 것
    private static FallenFit FitFallenTree(GroundPatch ground, FallenTrunk trunk)
    {
        float baseGround = ground.Sample(trunk.Base);
        float tipGround = ground.Sample(GetTip(trunk));
        // 양수 기울기는 끝이 아래로 내려간다 (CreateTrunkRotation)
        float pitchCenter = Mathf.Atan2(baseGround - tipGround, trunk.Length) * Mathf.Rad2Deg;
        int pitchSteps = Mathf.RoundToInt(FallenPitchRange / FallenPitchStep);
        var best = new FallenFit { Score = float.MaxValue };
        for (float roll = 0f; roll < 360f; roll += FallenRollStep)
        {
            for (int step = -pitchSteps; step <= pitchSteps; step++)
            {
                FallenFit fit = MeasureFallenTree(ground, trunk, pitchCenter + step * FallenPitchStep, roll);
                if (fit.Score < best.Score) best = fit;
            }
        }
        return best;
    }

    // 피벗 높이는 모든 줄기 정점이 (아래 지면 - FallenSinkDepth) 이상이 되는 가장 낮은 높이다 (줄기가 가장 높은 땅에 얹힘).
    // 줄기는 FallenBinStep 칸마다 가장 낮은 정점과 지면의 틈을 재서 가장 큰 틈을, 가지는 지면 아래로 가장 깊이 들어간 깊이를 잰다 (정점 기준)
    private static FallenFit MeasureFallenTree(GroundPatch ground, FallenTrunk trunk, float pitch, float roll)
    {
        TrunkModel model = trunk.Model;
        Quaternion rotation = CreateTrunkRotation(ToWorld(trunk.Direction, 0f), pitch, roll);
        int count = model.Vertices.Length;
        var heights = new float[count];
        var grounds = new float[count];
        var alongs = new float[count];
        float pivot = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            Vector3 offset = rotation * (model.Vertices[i] * trunk.Scale);
            var flat = new Vector2(offset.x, offset.z);
            heights[i] = offset.y;
            grounds[i] = ground.Sample(trunk.Base + flat);
            alongs[i] = Vector2.Dot(flat, trunk.Direction);
            if (model.IsCore[i]) pivot = Mathf.Max(pivot, grounds[i] - offset.y);
        }
        pivot -= FallenSinkDepth;

        // 밑동 정점이 피벗보다 조금 뒤에 있을 수 있어 한 칸 앞에서 시작한다
        int binCount = Mathf.CeilToInt(trunk.Length / FallenBinStep) + 2;
        var coreGaps = Enumerable.Repeat(float.PositiveInfinity, binCount).ToArray();
        float branchBury = 0f;
        for (int i = 0; i < count; i++)
        {
            float gap = pivot + heights[i] - grounds[i];
            if (!model.IsCore[i])
            {
                branchBury = Mathf.Max(branchBury, -gap);
                continue;
            }
            int bin = Mathf.Clamp(Mathf.FloorToInt(alongs[i] / FallenBinStep) + 1, 0, binCount - 1);
            coreGaps[bin] = Mathf.Min(coreGaps[bin], gap);
        }
        float trunkGap = coreGaps.Where(gap => !float.IsPositiveInfinity(gap)).Max();

        return new FallenFit
        {
            Rotation = rotation,
            PivotHeight = pivot,
            Pitch = pitch,
            TrunkGap = trunkGap,
            BranchBury = branchBury,
            Score = Mathf.Max(trunkGap, branchBury)
        };
    }

    // 나무 local Y(줄기)를 수평 방향 trunkDirection으로 눕히고, 줄기 축으로 roll만큼 굴린 뒤, 수평 옆 축으로 pitch만큼 기울인다 (메뉴 22와 같은 방식)
    private static Quaternion CreateTrunkRotation(Vector3 trunkDirection, float pitch, float roll)
    {
        Vector3 side = Vector3.Cross(Vector3.up, trunkDirection);
        return Quaternion.AngleAxis(pitch, side) * Quaternion.AngleAxis(roll, trunkDirection) * Quaternion.LookRotation(Vector3.up, trunkDirection);
    }

    // 앞쪽 나무 몇 그루의 밑동 뒤(쓰러지기 전 서 있던 자리)에 그루터기를 둔다. 다른 줄기와 겹치면 뺀다. 놓은 수를 돌려준다
    private static int PlaceStumps(LandmarkContext context, FallenBuild build)
    {
        GameObject[] prefabs = LoadPrefabs(new[] { StumpPath }, FallenLabel);
        if (prefabs == null) return 0;

        List<FallenTrunk> trunks = build.Trunks;
        int count = Mathf.Min(context.Random.Next(StumpCountMin, StumpCountMax + 1), trunks.Count);
        int placed = 0;
        for (int i = 0; i < count; i++)
        {
            Vector2 position = trunks[i].Base - trunks[i].Direction * StumpOffset;
            float yaw = RandomRange(context.Random, 0f, 360f);
            if (trunks.Any(other => GetSegmentDistance(position, other.Base, GetTip(other)) < StumpMinTrunkDistance)) continue;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[0], build.Group);
            instance.transform.SetPositionAndRotation(ToWorld(position, GetGroundHeight(context.Terrain, position)), Quaternion.Euler(0f, yaw, 0f));
            instance.transform.localScale = Vector3.one;
            FinishInstance(context.Render, instance, prefabs[0].name, build.Stats);
            if (GroundInstance(context.Terrain, instance, StructureSinkRatio)) context.BrokenCount++;

            context.Pieces.Add(instance.transform);
            context.Blockers.Add(new Blocker { From = position, To = position, Radius = FallenTrunkHalfWidth + LandmarkClearance });
            placed++;
        }
        return placed;
    }

    private static void LogFallenNote(LandmarkContext context, FallenBuild build)
    {
        FallenSite site = build.Site;
        List<FallenTrunk> trunks = build.Trunks;
        List<FallenFit> fits = build.Fits;
        float radius = trunks.Count == 0
            ? 0f
            : trunks.Max(trunk => Mathf.Max(Vector2.Distance(site.Center, trunk.Base) + StumpOffset, Vector2.Distance(site.Center, GetTip(trunk)))) + FallenTrunkHalfWidth;
        float yawSpread = trunks.Count == 0 ? 0f : trunks.Max(trunk => Vector2.Angle(trunk.Direction, site.Direction));
        int colliderCount = context.FallenTrees.Sum(tree => tree.GetComponentsInChildren<Collider>(true).Length);
        string fitText = fits.Count == 0
            ? "-"
            : $"줄기 뜬 높이 최대 {fits.Max(fit => fit.TrunkGap):F2}m (기준 {FallenMaxTrunkGap}m), 가지 묻힌 깊이 최대 {fits.Max(fit => fit.BranchBury):F2}m (기준 {FallenMaxBranchBury}m), " +
              $"기울기 {fits.Min(fit => fit.Pitch):F1} ~ {fits.Max(fit => fit.Pitch):F1}도";
        context.Notes.Add($"{FallenLabel} : 중심 ({site.Center.x:F0}, {site.Center.y:F0}) 지면 {GetGroundHeight(context.Terrain, site.Center):F1}m, " +
                          $"쓰러진 방향 {GetAzimuth(site.Direction):F0}도 (그루마다 최대 {yawSpread:F1}도 벗어남, 기준 ±{FallenYawJitter}도), " +
                          $"바닥 반지름 {radius:F1}m, 숲 안쪽 정도 {GetForestDepth(site.Center):F2}, 둘레 {FallenUphillDistance}m 안 가장 높은 땅 +{site.Rise:F1}m, " +
                          $"지대 안 지면 높이 차 {site.GroundRange:F2}m (상한 {FallenMaxAreaRange}m)\n  고사목 {trunks.Count}/{build.Target}그루 " +
                          $"(허용 {FallenCountMin} ~ {FallenCountMax}, 기준을 못 맞춰 뺀 그루 {build.RejectedCount}, 그루마다 후보 {FallenFitCandidates}곳 맞춰 봄), 크기 " +
                          $"{(trunks.Count == 0 ? "-" : $"{trunks.Min(trunk => trunk.Scale):F2} ~ {trunks.Max(trunk => trunk.Scale):F2}배")}, {fitText}, " +
                          $"뜬 줄기 {context.FallenFloatCount}, 묻힌 가지 {context.FallenBuryCount}, 그루터기 {build.StumpCount}개, " +
                          $"원본 충돌체 함께 눕힘 {colliderCount}개, 충돌체 추가 {build.Stats.ColliderCount}, Disallow 붙임 {build.Stats.DisallowCount}, " +
                          $"40 × 40m 기준 밀도 {(trunks.Count + build.StumpCount) / (FallenAreaSize * FallenAreaSize):F4}개/m²");
    }

    // ---------- 나무, 바위 ----------

    private static ScatterContext ScatterAll(Terrain terrain, List<Blocker> blockers, Transform treeParent, Transform rockParent)
    {
        var context = new ScatterContext
        {
            Terrain = terrain,
            Random = new System.Random(ScatterSeed),
            Blockers = blockers,
            TreeParent = treeParent,
            RockParent = rockParent,
            TreePrefabs = new[]
            {
                LoadScatterPrefabs(MapTreeScatter.MainTreePaths),
                LoadScatterPrefabs(MapTreeScatter.DeadTreePaths),
                LoadScatterPrefabs(MapTreeScatter.GiantTreePaths)
            },
            RockPrefabs = LoadScatterPrefabs(MapTreeScatter.RockPaths)
        };
        context.MainTreeHeights = context.TreePrefabs[(int)TreeKind.Main].Select(MeasurePrefabHeight).ToArray();

        int columns = Mathf.RoundToInt(EastBounds.width / ScatterCellSize);
        int rows = Mathf.RoundToInt(EastBounds.height / ScatterCellSize);
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                var cellMin = new Vector2(EastBounds.xMin + x * ScatterCellSize, EastBounds.yMin + z * ScatterCellSize);
                Vector2 cellCenter = cellMin + Vector2.one * (ScatterCellSize / 2f);
                // 칸 중심이 서식지 안이면 칸 전체를 비운다 (밀도를 놓을 수 있는 칸 넓이로 대조하기 위함)
                if (IsInHabitat(cellCenter))
                {
                    context.HabitatCellCount++;
                    continue;
                }

                int category = GetCategory(terrain, cellCenter);
                context.CellCounts[category]++;
                context.ZoneCellCounts[(int)GetEastZone(cellCenter)]++;
                ScatterTrees(context, cellMin, category);
                ScatterRocks(context, cellMin, category);
            }
        }
        return context;
    }

    private static GameObject[] LoadScatterPrefabs(string[] paths)
    {
        return paths.Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + path + ".prefab")).ToArray();
    }

    // prefab을 잠깐 원점에 놓고 LOD0 정점 높이를 잰 뒤 지운다 (종류 비중의 근거를 로그로 확인하기 위함)
    private static float MeasurePrefabHeight(GameObject prefab)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        (float bottom, float top) = GetMeshHeightRange(instance);
        Object.DestroyImmediate(instance);
        return top - bottom;
    }

    private static void ScatterTrees(ScatterContext context, Vector2 cellMin, int category)
    {
        int count = RollCount(context.Random, TreeDensityMin[category], TreeDensityMax[category]);
        for (int i = 0; i < count; i++)
            TryAddTree(context, cellMin, category);
    }

    private static void ScatterRocks(ScatterContext context, Vector2 cellMin, int category)
    {
        int count = RollCount(context.Random, RockDensityMin[category], RockDensityMax[category]);
        for (int i = 0; i < count; i++)
        {
            if (TryAddRock(context, cellMin)) context.RockCounts[category]++;
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

    // 무작위 값은 조건과 상관없이 같은 순서로 뽑아 결과가 항상 같게 한다
    private static void TryAddTree(ScatterContext context, Vector2 cellMin, int category)
    {
        TreeDraw draw = DrawTree(context, cellMin, category);
        if (!CanScatter(context, draw.Position)) return;

        // prefab 연결을 유지한 채 놓는다. Static 플래그는 켜지 않는다 (GPU Resident Drawer로 그림)
        GameObject prefab = context.TreePrefabs[(int)draw.Kind][draw.PrefabIndex];
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, context.TreeParent);
        instance.transform.SetPositionAndRotation(ToWorld(draw.Position, GetGroundHeight(context.Terrain, draw.Position)), Quaternion.Euler(0f, draw.Yaw, 0f));
        instance.transform.localScale = Vector3.one * draw.Scale;
        if (MapTreeScatter.AddMissingCollider(instance, prefab.name)) context.ColliderCount++;
        RecordTree(context, category, draw);
    }

    private static TreeDraw DrawTree(ScatterContext context, Vector2 cellMin, int category)
    {
        System.Random random = context.Random;
        TreeKind kind = PickTreeKind(random, category);
        int prefabIndex = kind == TreeKind.Main ? PickMainTree(random, category) : random.Next(context.TreePrefabs[(int)kind].Length);
        var position = new Vector2(
            cellMin.x + RandomRange(random, 0f, ScatterCellSize),
            cellMin.y + RandomRange(random, 0f, ScatterCellSize));
        float scale = DrawTreeScale(random, category, position);
        return new TreeDraw { Kind = kind, PrefabIndex = prefabIndex, Position = position, Scale = scale, Yaw = RandomRange(random, 0f, 360f) };
    }

    // 고사목은 모든 곳, 거대목은 숲에서만 나온다
    private static TreeKind PickTreeKind(System.Random random, int category)
    {
        double roll = random.NextDouble();
        if (category == (int)MapTreeScatter.FieldZone.Forest)
        {
            if (roll < GiantTreeRatio) return TreeKind.Giant;
            roll -= GiantTreeRatio;
        }
        return roll < DeadTreeRatio ? TreeKind.Dead : TreeKind.Main;
    }

    // 숲은 키 큰 종류, 드문드문·산 위는 작은 종류 위주로 비중을 두고 뽑는다
    private static int PickMainTree(System.Random random, int category)
    {
        float[] weights = category == (int)MapTreeScatter.FieldZone.Forest ? ForestMainWeights : SmallMainWeights;
        double roll = random.NextDouble() * weights.Sum();
        for (int i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];
            if (roll < 0d) return i;
        }
        return weights.Length - 1;
    }

    // 숲은 가장자리 띠에서 안쪽으로 갈수록 지수를 줄여 큰 나무 쪽으로 치우치게 한다. 숲이 아니면 가장자리 지수 (작은 쪽)
    private static float DrawTreeScale(System.Random random, int category, Vector2 position)
    {
        float u = (float)random.NextDouble();
        float exponent = category == (int)MapTreeScatter.FieldZone.Forest
            ? Mathf.Lerp(EdgeScaleExponent, CoreScaleExponent, GetForestDepth(position))
            : EdgeScaleExponent;
        return TreeScaleMin + (TreeScaleMax - TreeScaleMin) * Mathf.Pow(u, exponent);
    }

    private static void RecordTree(ScatterContext context, int category, TreeDraw draw)
    {
        context.TreeCounts[category]++;
        context.TreeKindCounts[(int)draw.Kind]++;
        if (draw.Kind == TreeKind.Main) context.MainTreeCounts[category, draw.PrefabIndex]++;

        int bin = 0;
        while (bin < context.ScaleBinCounts.Length - 1 && draw.Scale >= ScaleBinEdges[bin + 1]) bin++;
        context.ScaleBinCounts[bin]++;

        if (category != (int)MapTreeScatter.FieldZone.Forest) return;
        int band = GetForestDepth(draw.Position) < 1f ? 0 : 1;
        context.ForestScaleSums[band] += draw.Scale;
        context.ForestScaleCounts[band]++;
    }

    // 바위 : 놓아 본 뒤 피벗을 렌더러 범위 바닥 표본점 중 가장 낮은 땅에 맞춰 비탈 아래쪽이 뜨지 않게 한다. 무작위 값은 조건과 상관없이 같은 순서로 뽑는다
    private static bool TryAddRock(ScatterContext context, Vector2 cellMin)
    {
        System.Random random = context.Random;
        var position = new Vector2(
            cellMin.x + RandomRange(random, 0f, ScatterCellSize),
            cellMin.y + RandomRange(random, 0f, ScatterCellSize));
        GameObject prefab = context.RockPrefabs[random.Next(context.RockPrefabs.Length)];
        float scale = RandomRange(random, MapTreeScatter.RockScaleMin, MapTreeScatter.RockScaleMax);
        float yaw = RandomRange(random, 0f, 360f);
        if (!CanScatter(context, position)) return false;

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, context.RockParent);
        instance.transform.SetPositionAndRotation(ToWorld(position, GetGroundHeight(context.Terrain, position)), Quaternion.Euler(0f, yaw, 0f));
        instance.transform.localScale = Vector3.one * scale;
        float lowest = GetFootprintPoints(GetRendererBounds(instance)).Append(position).Min(point => GetGroundHeight(context.Terrain, point));
        instance.transform.position = ToWorld(position, lowest);
        if (MapTreeScatter.AddMissingCollider(instance, prefab.name)) context.ColliderCount++;
        return true;
    }

    // 서식지 반경, 랜드마크 둘레, 경사 35° 이상에는 놓지 않는다. 건너뛴 이유를 센다
    private static bool CanScatter(ScatterContext context, Vector2 position)
    {
        if (IsInHabitat(position))
        {
            context.HabitatSkipCount++;
            return false;
        }
        if (IsBlocked(context.Blockers, position))
        {
            context.LandmarkSkipCount++;
            return false;
        }
        if (GetSlope(context.Terrain, position) >= MapTreeScatter.MaxSlope)
        {
            context.SlopeSkipCount++;
            return false;
        }
        return true;
    }

    private static bool IsBlocked(List<Blocker> blockers, Vector2 position)
    {
        return blockers.Any(blocker => GetSegmentDistance(position, blocker.From, blocker.To) < blocker.Radius);
    }

    // ---------- 높이 맞춤, 렌더링 ----------

    // 밑면을 바닥 둘레의 가장 낮은 땅보다 (높이 × sinkRatio)만큼 아래에 맞춘다. 비탈 아래쪽이 뜨지 않고 위쪽은 땅에 묻힌다.
    // 둘레 표본 위치(XZ)는 렌더러 범위로, 밑면과 높이는 메시 정점으로 잰다 (Prefab_Cart처럼 메시 범위가 실제 모양보다 아래로 잡힌 prefab이 있어서).
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

    // 밑면을 (가장 낮은 땅 - 높이 × sinkRatio)에 맞췄을 때, 가장 높은 땅이 밑면에서 높이 × PieceMaxBuryRatio를 넘으면 많이 묻힌 것이다
    private static bool IsBuried(float lowest, float highest, float height, float sinkRatio)
    {
        return highest - (lowest - height * sinkRatio) > height * PieceMaxBuryRatio;
    }

    // 실제 메시 정점의 가장 낮은, 높은 월드 Y
    private static (float Bottom, float Top) GetMeshHeightRange(GameObject instance)
    {
        float bottom = float.MaxValue;
        float top = float.MinValue;
        foreach (Vector3 vertex in GetLod0Vertices(instance))
        {
            bottom = Mathf.Min(bottom, vertex.y);
            top = Mathf.Max(top, vertex.y);
        }
        return (bottom, top);
    }

    // LOD0 렌더러 메시 정점의 월드 위치. LODGroup이 있으면 LOD1 이하는 같은 자리의 낮은 단계 메시라 뺀다
    private static IEnumerable<Vector3> GetLod0Vertices(GameObject instance)
    {
        var lowerLodRenderers = new HashSet<Renderer>(instance.GetComponentsInChildren<LODGroup>()
            .SelectMany(group => group.GetLODs().Skip(1))
            .SelectMany(lod => lod.renderers));

        foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>())
        {
            if (lowerLodRenderers.Contains(renderer)) continue;

            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;

            Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
                yield return toWorld.MultiplyPoint3x4(vertex);
        }
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

    // 경로마다 prefab을 읽는다. 하나라도 없으면 오류를 남기고 null (배치도 경로 오타를 바로 알리기 위함)
    private static GameObject[] LoadPrefabs(IEnumerable<string> paths, string label)
    {
        string[] pathList = paths.ToArray();
        GameObject[] prefabs = LoadScatterPrefabs(pathList);
        string[] missing = pathList.Where((path, index) => prefabs[index] == null).Distinct().ToArray();
        if (missing.Length == 0) return prefabs;

        Debug.LogError($"[MapEastScatter] {string.Join(", ", missing)} prefab을 찾지 못해 '{label}'을 놓지 못했습니다. 배치도의 경로를 확인하세요.");
        return null;
    }

    // 충돌체 추가, Static 끔, GPU Resident Drawer 제외 (원본 prefab은 고치지 않는다)
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
            if (renderer.sharedMaterials.All(material => material == null || SupportsGpuDriven(render, material.shader))) continue;
            if (renderer.GetComponent(render.DisallowType) != null) continue;

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

    private static void MergeStats(InstanceStats into, InstanceStats from)
    {
        into.ColliderCount += from.ColliderCount;
        into.DisallowCount += from.DisallowCount;
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

    // 원 둘레 count곳 (북쪽부터 시계 방향)
    private static IEnumerable<Vector2> GetCirclePoints(Vector2 center, float radius, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float radian = i * 2f * Mathf.PI / count;
            yield return center + new Vector2(Mathf.Sin(radian), Mathf.Cos(radian)) * radius;
        }
    }

    private static float GetSegmentDistance(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 segment = to - from;
        float lengthSquared = segment.sqrMagnitude;
        float t = lengthSquared > 0f ? Mathf.Clamp01(Vector2.Dot(point - from, segment) / lengthSquared) : 0f;
        return Vector2.Distance(point, from + segment * t);
    }

    // 두 선분 사이 가장 짧은 거리. 엇갈려 지나면 0
    private static float GetSegmentGap(Vector2 fromA, Vector2 toA, Vector2 fromB, Vector2 toB)
    {
        if (AreSegmentsCrossing(fromA, toA, fromB, toB)) return 0f;
        return Mathf.Min(
            Mathf.Min(GetSegmentDistance(fromA, fromB, toB), GetSegmentDistance(toA, fromB, toB)),
            Mathf.Min(GetSegmentDistance(fromB, fromA, toA), GetSegmentDistance(toB, fromA, toA)));
    }

    private static bool AreSegmentsCrossing(Vector2 fromA, Vector2 toA, Vector2 fromB, Vector2 toB)
    {
        float sideB1 = Cross(toA - fromA, fromB - fromA);
        float sideB2 = Cross(toA - fromA, toB - fromA);
        float sideA1 = Cross(toB - fromB, fromA - fromB);
        float sideA2 = Cross(toB - fromB, toA - fromB);
        return sideB1 * sideB2 < 0f && sideA1 * sideA2 < 0f;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    // degrees만큼 돌린다 (위에서 볼 때 방위각이 커지는 쪽)
    private static Vector2 Rotate(Vector2 vector, float degrees)
    {
        float radian = -degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radian);
        float sin = Mathf.Sin(radian);
        return new Vector2(vector.x * cos - vector.y * sin, vector.x * sin + vector.y * cos);
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
    // 아래 로그는 검수(구분 비율, 밀도가 승인 값 안인지, 크기 분포, 종류 비중, 랜드마크 위치와 크기, 수레길, 고사목 맞춤, 서식지 비움, 뜸·묻힘, C2 밖, 성능 측정용 배치 수)에 쓰는 값이라 남긴다

    private static void LogZones(ScatterContext context)
    {
        float cellArea = ScatterCellSize * ScatterCellSize / HectareArea;
        float zoneCells = context.ZoneCellCounts.Sum();
        float[] approved = { OpenAreaRatio, 1f - OpenAreaRatio - ForestAreaRatio, ForestAreaRatio };
        string zoneRatios = string.Join(", ", Enumerable.Range(0, 3)
            .Select(zone => $"{CategoryLabels[zone]} {context.ZoneCellCounts[zone] / zoneCells:P1} (승인 {approved[zone]:P0})"));

        var lines = new List<string>();
        for (int category = 0; category < CategoryLabels.Length; category++)
        {
            float area = context.CellCounts[category] * cellArea;
            lines.Add($"{CategoryLabels[category]} 넓이 {area:F1}ha : 나무 {context.TreeCounts[category]}그루 " +
                      $"{FormatDensity(context.TreeCounts[category], area, TreeDensityMin[category], TreeDensityMax[category])}, " +
                      $"바위 {context.RockCounts[category]}개 {FormatDensity(context.RockCounts[category], area, RockDensityMin[category], RockDensityMax[category])}");
        }
        lines.Add($"비운 칸 : 서식지 {context.HabitatCellCount * cellArea:F1}ha");

        int[] kinds = context.TreeKindCounts;
        int treeTotal = kinds.Sum();
        float total = Mathf.Max(1f, treeTotal);
        Debug.Log($"[MapEastScatter] 동쪽(C2) 구분 비율 (노이즈 주기 {ZoneNoisePeriod}m, 서식지 밖 칸, 산 위 포함) : {zoneRatios}. " +
                  $"산 위({MountainTopHeight}m 이상)는 구분과 상관없이 산 위 밀도를 쓴다. 구분별 넓이와 밀도 :\n" + string.Join("\n", lines));
        Debug.Log($"[MapEastScatter] 나무 {treeTotal}그루 (주력 {kinds[(int)TreeKind.Main]}, 고사목 {kinds[(int)TreeKind.Dead]} ({kinds[(int)TreeKind.Dead] / total:P1}, 목표 {DeadTreeRatio:P0}), " +
                  $"거대목 {kinds[(int)TreeKind.Giant]} ({kinds[(int)TreeKind.Giant] / total:P1}, 숲 안 목표 {GiantTreeRatio:P0})), 바위 {context.RockCounts.Sum()}개 " +
                  $"(산 위 {context.RockCounts[MountainCategory]}, 그 밖 {context.RockCounts.Sum() - context.RockCounts[MountainCategory]}), 충돌체 추가 {context.ColliderCount} / " +
                  $"건너뜀 서식지 {context.HabitatSkipCount}, 랜드마크 둘레 {context.LandmarkSkipCount}, 경사 {MapTreeScatter.MaxSlope}도 이상 {context.SlopeSkipCount}");
    }

    private static string FormatDensity(int count, float area, float min, float max)
    {
        float density = area > 0f ? count / area : 0f;
        bool isInRange = density >= min && density <= max;
        return $"(1ha당 {density:F1}, 표 {min:F0} ~ {max:F0} {(isInRange ? "안" : "밖")})";
    }

    private static void LogTreeSizes(ScatterContext context)
    {
        string bins = string.Join(", ", Enumerable.Range(0, context.ScaleBinCounts.Length)
            .Select(bin => $"{ScaleBinEdges[bin]:F1} ~ {ScaleBinEdges[bin + 1]:F1}배 {context.ScaleBinCounts[bin]}"));
        string edgeMean = context.ForestScaleCounts[0] > 0 ? $"{context.ForestScaleSums[0] / context.ForestScaleCounts[0]:F2}배 ({context.ForestScaleCounts[0]}그루)" : "-";
        string coreMean = context.ForestScaleCounts[1] > 0 ? $"{context.ForestScaleSums[1] / context.ForestScaleCounts[1]:F2}배 ({context.ForestScaleCounts[1]}그루)" : "-";

        GameObject[] mains = context.TreePrefabs[(int)TreeKind.Main];
        var species = new List<string>();
        for (int category = 1; category < CategoryLabels.Length; category++)
        {
            int[] counts = Enumerable.Range(0, mains.Length).Select(index => context.MainTreeCounts[category, index]).ToArray();
            float sum = Mathf.Max(1f, counts.Sum());
            species.Add($"{CategoryLabels[category]} : " + string.Join(", ", Enumerable.Range(0, mains.Length)
                .Select(index => $"{mains[index].name.Replace("Prefab_", "")} {counts[index] / sum:P0}")));
        }
        string heights = string.Join(", ", Enumerable.Range(0, mains.Length).Select(index => $"{mains[index].name.Replace("Prefab_", "")} {context.MainTreeHeights[index]:F1}m"));

        Debug.Log($"[MapEastScatter] 나무 크기 분포 (전체, 허용 {TreeScaleMin} ~ {TreeScaleMax}배) : {bins}. " +
                  $"숲 가장자리 띠 평균 {edgeMean}, 숲 안쪽 평균 {coreMean} (지수 {EdgeScaleExponent} → {CoreScaleExponent})");
        Debug.Log($"[MapEastScatter] 주력 침엽수 종류 비중 (실측 LOD0 높이 : {heights}) :\n" + string.Join("\n", species));
    }

    private static string DescribePrefabs(Transform group)
    {
        return string.Join(", ", group.Cast<Transform>()
            .GroupBy(piece => piece.name)
            .OrderByDescending(names => names.Count())
            .Select(names => $"{names.Key} × {names.Count()}"));
    }

    private static void LogLandmarks(LandmarkContext context)
    {
        var spacing = new List<string>();
        for (int i = 0; i < context.Centers.Count; i++)
        {
            for (int j = i + 1; j < context.Centers.Count; j++)
                spacing.Add($"중심 사이 {Vector2.Distance(context.Centers[i], context.Centers[j]):F0}m (기준 {LandmarkMinSpacing}m 이상)");
            if (context.RoadPath != null)
                spacing.Add($"({context.Centers[i].x:F0}, {context.Centers[i].y:F0})와 수레길 선 {GetPathDistance(context.RoadPath, context.Centers[i]):F0}m (기준 {LandmarkRoadSpacing}m 이상)");
        }
        Debug.Log($"[MapEastScatter] 랜드마크 {context.Notes.Count}/3곳, {string.Join(", ", spacing)}, 경계 여유 {LandmarkEdgeMargin}m + 바닥 반지름, " +
                  $"둘레 비움 {LandmarkClearance}m :\n" + string.Join("\n", context.Notes));
    }

    // 놓인 오브젝트를 다시 읽어 뜸, 묻힘, 서식지 반경 안, C2 밖, 경사 35° 이상, 분홍, WallRuin, Disallow 수를 센다
    private static void LogPlacementCheck(Terrain terrain, ScatterContext scatter, LandmarkContext landmarks)
    {
        List<Transform> trees = scatter.TreeParent.Cast<Transform>().ToList();
        List<Transform> rocks = scatter.RockParent.Cast<Transform>().ToList();
        List<Transform> groups = landmarks.Parent.Cast<Transform>().ToList();
        List<Transform> all = trees.Concat(rocks).Concat(landmarks.Pieces).ToList();

        int floatingCount = 0;
        int buriedCount = 0;
        foreach (Transform item in trees.Concat(rocks))
        {
            float gap = item.position.y - GetGroundHeight(terrain, ToXZ(item.position));
            if (gap > HeightTolerance) floatingCount++;
            if (gap < -MaxBuryGap) buriedCount++;
        }
        int steepCount = trees.Concat(rocks).Count(item => GetSlope(terrain, ToXZ(item.position)) >= MapTreeScatter.MaxSlope);
        int inHabitatCount = all.Count(item => IsInHabitat(ToXZ(item.position)));
        int outsideCount = all.Concat(groups).Select(item => ToXZ(item.position)).Append(MapEastShaper.HabitatCenter)
            .Count(position => !EastBounds.Contains(position));
        int wallRuinCount = all.Count(item => item.name == WallRuinName);

        Transform[] parents = { scatter.TreeParent, scatter.RockParent, landmarks.Parent };
        int pinkCount = parents.SelectMany(parent => parent.GetComponentsInChildren<MeshRenderer>(true))
            .Count(renderer => renderer.sharedMaterials.Any(IsBrokenMaterial));
        int disallowCount = parents.Sum(parent => parent.GetComponentsInChildren(landmarks.Render.DisallowType, true).Length);

        Vector2 habitatCenter = MapEastShaper.HabitatCenter;
        float azimuth = GetAzimuth(habitatCenter);
        string direction = DirectionLabels[Mathf.RoundToInt(azimuth / 45f) % DirectionLabels.Length];
        bool isEast = azimuth >= EastAzimuthMin && azimuth <= EastAzimuthMax;
        Debug.Log($"[MapEastScatter] 서식지 ({habitatCenter.x:F0}, {GetGroundHeight(terrain, habitatCenter):F1}, {habitatCenter.y:F0}), " +
                  $"거점 기준 방위각 {azimuth:F1}도 ({direction}, {EastAzimuthMin} ~ {EastAzimuthMax}도 {(isEast ? "안" : "밖")}), " +
                  $"반경 {MapEastShaper.HabitatRadius}m 안 오브젝트 {inHabitatCount}개 (0이어야 한다)");
        Debug.Log($"[MapEastScatter] 뜨거나 묻힌 오브젝트 : 나무·바위 뜸 {floatingCount}, 묻힘 {buriedCount} / 랜드마크 조각(수레길, 사당, 그루터기) {landmarks.BrokenCount} / " +
                  $"눕힌 고사목 뜬 줄기 {landmarks.FallenFloatCount}, 묻힌 가지 {landmarks.FallenBuryCount}. 나무·바위 경사 {MapTreeScatter.MaxSlope}도 이상 {steepCount}, " +
                  $"분홍 렌더러 {pinkCount}, {WallRuinName} 사용 {wallRuinCount}개, C2 밖 {outsideCount}개 (모두 0이어야 한다). " +
                  $"Disallow 붙은 렌더러 {disallowCount}개 (랜드마크 붙임 {landmarks.Stats.DisallowCount}), " +
                  $"충돌체 추가 나무·바위 {scatter.ColliderCount} / 랜드마크 {landmarks.Stats.ColliderCount}");

        // 묶음 GameObject, 서식지 표시(1)도 씬 GameObject라 함께 센다
        int total = all.Count + groups.Count + 1;
        Debug.Log($"[MapEastScatter] East 배치 GameObject 총 {total}개 (나무 {trees.Count}, 바위 {rocks.Count}, 랜드마크 조각 {landmarks.Pieces.Count} " +
                  $"(그중 눕힌 고사목 {landmarks.FallenTrees.Count}), 묶음 {groups.Count}, 서식지 표시 1)");
    }
}
