using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 메뉴 22 : 남동쪽 구역(C3, 물이 퍼진 낮은 습지와 바위 언덕)에 서식지 위치 표시, 랜드마크(버려진 어부 야영지, 쓰러진 고사목 다리 2곳),
// 언덕 비탈 절벽 조각, 나무, 바위, 물가 소품을 놓는다 (이슈 #13).
// 물 판정은 연못 목록 없이 지형에서 직접 잰다 : 지형 높이 < MapSouthEastShaper.WaterLevel(29m)이면 물이다.
// 놓는 곳 규칙 (사용자 승인 임시값)
// - 지면 PlaceMinHeight(29.3m) 미만에는 아무것도 놓지 않는다. 나무는 피벗과 둘레(TreeFootprintRadius) 4곳,
//   바위·소품·랜드마크 조각·절벽 조각은 렌더러 범위 바닥 표본점(가운데와 둘레 8곳)이 모두 이 높이 이상이어야 한다 (물가로 넘어가지 않게).
//   예외 : 고사목 다리(물길을 가로지름)와 야영지에서 물가에 반쯤 걸친 배 1척. 로그에서 따로 센다.
// - 구분 : 새 노이즈를 분위수로 나눠 풀습지 / 드문드문 / 작은 무리 = 80 / 15 / 5%. 분위수 표본은 놓을 수 있는 땅(29.3m 이상, 서식지 밖)만 쓰고,
//   언덕 위도 표본에 넣는다 (언덕 위 나무도 같은 구분을 따른다. 바위만 언덕 밀도를 쓴다).
// - 나무 1ha당 풀습지 0 / 드문드문 1 ~ 3 / 작은 무리 30 ~ 60, 고사목 DeadTreeRatio(20%), 거대목은 작은 무리에서만 MapTreeScatter.GiantTreeRatio.
// - 바위 1ha당 습지 1 ~ 3, 언덕 위(MapSouthEastShaper.CalculateHillWeight > 0) 5 ~ 10. 바위는 MapTreeScatter.RockPaths 중 Stones만 쓴다.
// - 물가 소품(그루터기, 속 빈 통나무) : 물가 띠(지면 29.3m 이상이면서 물에서 ShoreDistance 안) 넓이 기준 1ha당 1 ~ 2.
// - 절벽 조각 : 언덕 비탈에 3 ~ 5개. 꼭대기 HilltopClearRadius 안에는 두지 않고, 조각 윗면이 꼭대기 지면보다 높으면 버린다 (꼭대기 시야).
//   CliffAuto 셰이더는 GPU Resident Drawer로 그려지지 않으므로 DisallowGPUDrivenRendering을 붙인다 (서쪽 메뉴 12와 같은 처리).
// - 서식지 : SouthEast/Habitat를 MapSouthEastShaper.HabitatCenter의 지면 높이에 두고, 반경 HabitatRadius 안은 비운다.
// - 랜드마크 둘레 LandmarkClearance(8m)는 나무, 바위, 소품을 비운다. 랜드마크는 C3 경계에서 (LandmarkEdgeMargin + 바닥 반지름) 안쪽에 둔다.
// 버려진 어부 야영지 : 가장 큰 물 덩어리 CampPondCount개의 물가 칸마다 물 쪽 방향을 재고, 야영지 +z를 그 방향으로 돌려 물가에서 뭍 쪽으로
//   CampShoreOffsets만큼 들어간 자리를 놓아 본다. 뭍 쪽 조각이 모두 29.3m 이상이고 중심 경사 CampMaxSlope 이하인 자리 중
//   (1) 묻히는 조각 (2) 바닥 아래 지면 높이 차가 가장 작은 곳에 놓는다. 물가에 걸친 배는 야영지 중심에서 물 쪽으로 가다 처음 물을 만난 곳에 긴 축을 물 쪽으로 둔다.
// 쓰러진 고사목 다리 : 물 지도에서 물길 가운데 줄(뭍까지 거리가 이웃보다 작지 않은 물 칸)마다 여러 방향으로 물 폭을 재어 가장 좁은 방향을 건너는 방향으로 삼는다.
//   물 폭 BridgeMinWaterWidth ~ BridgeMaxWaterWidth(6 ~ 16m)이고 물길 방향으로는 물 폭 이상 물이 이어지는 곳(만의 끝, 작은 웅덩이 제외)을 후보로,
//   양쪽 BridgeBankHeight(29.1m, 다리에만 쓰는 걸침 땅 기준) 땅 사이가 짧은 곳부터 고사목을 눕혀 맞춰 본다.
//   줄기 밑면이 걸침 구간(BridgeBankHeight 선 바깥)의 땅에 얹히고, 물 위에서는 수면 위 BridgeMinWaterClearance ~ BridgeMaxWaterClearance가 되는 기울기를 찾는다.
//   걸어서 건널 수 있게 줄기 윗면에 맞춘 평평한 BoxCollider를 자식 오브젝트로 붙인다 (원본 prefab의 CapsuleCollider는 그대로 함께 눕는다).
// 다시 실행하면 SouthEast 아래 Trees, Rocks, Landmarks, Habitat의 자식을 지우고 같은 seed로 다시 만든 뒤 씬을 저장한다. SouthEast/Ponds(메뉴 21)는 건드리지 않는다.
// 실행 순서 : ... → 19 → 20 → 21 → 22 → 23 → 10. 20(남동쪽 지형)을 다시 실행하면 21, 22, 23도 순서대로 다시 실행하고, 22를 다시 실행하면 23도 다시 실행한다.
// 지면·바닥 식생(메뉴 23)은 CalculateZoneThresholds, GetSouthEastZone으로 같은 구분을 쓴다.
public static class MapSouthEastScatter
{
    private enum TreeKind
    {
        Main = 0,
        Dead = 1,
        Giant = 2
    }

    // 다리 맞춤이 실패한 이유 (BridgeFailureLabels와 같은 순서)
    private enum BridgeFailure
    {
        None = 0,
        Bearing = 1,
        WetBearing = 2,
        WaterClearance = 3,
        EndGap = 4
    }

    // 선분(점이면 From == To) 둘레 Radius 안에는 나무, 바위, 소품을 두지 않는다
    private struct Blocker
    {
        public Vector2 From;
        public Vector2 To;
        public float Radius;
    }

    // 같은 크기 범위로 같은 부모 아래 놓는 prefab 묶음
    private class InstanceGroup
    {
        public GameObject[] Prefabs;
        public Transform Parent;
        public float ScaleMin;
        public float ScaleMax;
    }

    private class InstanceStats
    {
        public int ColliderCount;
        public int DisallowCount;
        // 분홍(셰이더 오류, 머티리얼 없음) 렌더러 수
        public int BrokenMaterialCount;
    }

    // GPU Resident Drawer 제외 판정에 함께 쓰는 값
    private class RenderSetup
    {
        public System.Type DisallowType;
        public Dictionary<Shader, bool> GpuDrivenSupport = new Dictionary<Shader, bool>();
    }

    // C3를 WaterGridSpacing 칸으로 나눈 물 지도. 배열은 [z, x]이고 값은 칸 가운데에서 잰다
    private class WaterMap
    {
        public Vector2 Origin;
        public float Spacing;
        public int Columns;
        public int Rows;
        public float[,] Heights;
        public bool[,] IsWater;
        // 물 덩어리 번호 (물이 아니면 -1)와 덩어리마다 넓이 (m²)
        public int[,] Component;
        public List<float> ComponentAreas;
        // 가장 가까운 물 칸, 물이 아닌 칸까지 거리 (m)
        public float[,] WaterDistance;
        public float[,] LandDistance;

        public Vector2 GetCellCenter(int x, int z)
        {
            return Origin + new Vector2(x + 0.5f, z + 0.5f) * Spacing;
        }
    }

    // 한 번 실행하는 동안 함께 쓰는 값과 결과 개수 묶음. 구분 배열은 FieldZone 순서, 지대 배열은 TerrainLabels 순서
    private class ScatterContext
    {
        public Terrain Terrain;
        public WaterMap Water;
        public System.Random Random;
        // 물가 소품은 따로 뽑아, 소품 규칙을 바꿔도 나무·바위 배치가 바뀌지 않게 한다
        public System.Random PropRandom;
        public RenderSetup Render;
        public List<Blocker> Blockers;
        public float[] Thresholds;
        public InstanceGroup[] TreeGroups;
        public InstanceGroup RockGroup;
        public InstanceGroup PropGroup;
        public int[] ZoneCellCounts = new int[ZoneLabels.Length];
        public int[] TreeCounts = new int[ZoneLabels.Length];
        public int[] TreeKindCounts = new int[3];
        public int[] TerrainCellCounts = new int[TerrainLabels.Length];
        public int[] RockCounts = new int[TerrainLabels.Length];
        // 칸 중심이 서식지 안, 29.3m 미만(그중 물)이라 아무것도 놓지 않은 칸
        public int HabitatCellCount;
        public int LowCellCount;
        public int WaterCellCount;
        public float ShoreArea;
        public int ShorePropTarget;
        public int ShorePropCount;
        public int ShorePropFailedDraws;
        public int ColliderCount;
        public int HabitatSkipCount;
        public int LandmarkSkipCount;
        public int SlopeSkipCount;
        // 바닥 둘레 중 29.3m 미만이 있어 건너뛴 수
        public int LowGroundSkipCount;
    }

    // 야영지 조각 하나의 배치도 (묶음 중심 기준, +z = 물 쪽)
    private struct PieceSpec
    {
        // MapTreeScatter.PrefabFolder 아래 경로 (확장자 없음)
        public string Path;
        public Vector2 Offset;
        public float Yaw;
        public float Scale;
    }

    private class PlacedPiece
    {
        public Transform Transform;
        // 메시 정점 기준 높이 (m)
        public float Height;
    }

    // 원점에서 조립한 묶음. 자리를 찾은 뒤 통째로 옮긴다
    private class LandmarkBuild
    {
        public Transform Group;
        public List<PlacedPiece> Pieces = new List<PlacedPiece>();
        public InstanceStats Stats = new InstanceStats();
    }

    // 묶음 회전 단계마다 조각별 바닥 표본점 (묶음 중심 기준 XZ)과 바닥 반지름. 자리 후보마다 렌더러 범위를 다시 재지 않으려고 미리 구한다
    private class CampFootprints
    {
        // [회전 단계][조각][표본점]
        public Vector2[][][] Points;
        public float[] Radius;
        // 조각별 메시 정점 기준 높이 (회전과 상관없다)
        public float[] Heights;
    }

    private struct CampFit
    {
        public Vector2 Center;
        // 묶음 +z가 향하는 물 쪽 방향 (단위 벡터)
        public Vector2 Direction;
        public int YawIndex;
        public int BrokenCount;
        public float GroundRange;
        public float Slope;
        // 중심에서 물 쪽으로 가다 처음 물(29m 미만)을 만난 곳
        public Vector2 ShorePoint;
    }

    // 물길을 건너는 자리 하나
    private struct Crossing
    {
        // 물길 가운데
        public Vector2 Center;
        // 건너는 방향 (단위 벡터, 물 폭이 가장 좁은 방향)
        public Vector2 Direction;
        public float WaterWidth;
        // 가운데에서 -Direction, +Direction 쪽 BridgeBankHeight 땅까지 거리와 그 합
        public float BankMinus;
        public float BankPlus;
        public float BankWidth;
    }

    // 다리로 쓰는 고사목 prefab의 LOD0 정점 (루트 기준, 크기 1)
    private class TrunkModel
    {
        public GameObject Prefab;
        public Vector3[] Vertices;
        // 줄기 축에서 BridgeCoreRadius 안의 정점 (줄기), 그 밖은 가지
        public bool[] IsCore;
        // 밑동(피벗)에서 꼭대기까지 길이 (m, 크기 1)
        public float Length;
        // 가지가 아래로 가장 적게 처지는 굴림 각도 (도)
        public float Roll;
    }

    private struct BridgeFit
    {
        public float Scale;
        public float Length;
        // 한쪽 걸침 길이 (BridgeBankHeight 선 바깥, m)
        public float Bearing;
        public float Pitch;
        public Vector3 Pivot;
        public Quaternion Rotation;
        // 걸침 구간에서 줄기 밑면과 땅 사이 가장 작은 틈 (밑동 쪽, 끝 쪽, m. 음수는 묻힘)
        public float GapBase;
        public float GapTip;
        // 물 위 구간에서 줄기 밑면의 수면 위 높이 (m)
        public float WaterClearMin;
        public float WaterClearMax;
        // 물 위 구간 줄기 윗면의 평균 높이 (걷는 면, m)
        public float WalkTop;
        // 가지 가장 낮은 곳과 그 아래 땅·수면 중 높은 쪽의 차 (m, 음수면 가지가 땅이나 물에 들어감)
        public float BranchClearMin;
    }

    private class LandmarkContext
    {
        public Terrain Terrain;
        public RenderSetup Render;
        public Transform Parent;
        public Transform RockParent;
        // 절벽 조각의 수, 종류, 크기, 회전
        public System.Random Random;
        public WaterMap Water;
        public Vector2 Hilltop;
        public float HilltopHeight;
        public List<Blocker> Blockers = new List<Blocker>();
        // 야영지, 다리 중심 (서로 거리 검사)
        public List<Vector2> Centers = new List<Vector2>();
        public List<Transform> CampPieces = new List<Transform>();
        public Transform ShoreBoat;
        public List<Transform> BridgeTrees = new List<Transform>();
        public HashSet<Transform> Cliffs = new HashSet<Transform>();
        // 땅에 맞춘 뒤 뜨거나 많이 묻힌 야영지 조각, 절벽 조각 수
        public int CampBrokenCount;
        public int CliffBrokenCount;
        public int CliffTarget;
        public int CrossingCount;
        public int[] BridgeFailures = new int[BridgeFailureLabels.Length];
        // 물길 후보마다 물 폭, 땅 사이, 결과 (다리가 모자랄 때 원인을 보려고 남긴다)
        public List<string> CrossingNotes = new List<string>();
        public InstanceStats Stats = new InstanceStats();
        public List<string> Notes = new List<string>();
    }

    private const string RegionName = "SouthEast";
    private const string TreeParentName = "Trees";
    private const string RockParentName = "Rocks";
    private const string LandmarkParentName = "Landmarks";
    private const string HabitatName = "Habitat";
    private const string CampLabel = "AbandonedFisherCamp";
    private const string BridgeLabel = "DeadwoodBridge";
    private const string BridgeColliderName = "BridgeWalkCollider";
    // 쓰지 않는 prefab. 로그에서 0개인지 센다
    private const string WallRuinName = "Prefab_WallRuin";

    // 이 높이 미만 땅에는 아무것도 놓지 않는다 (m, 사용자 승인 임시값). 다리와 물가에 걸친 배만 예외
    private const float PlaceMinHeight = 29.3f;

    // 거점 기준 "남동" 방위 범위 (도). 로그에서 서식지 방위가 이 안인지 본다
    private const float SouthEastAzimuthMin = 112.5f;
    private const float SouthEastAzimuthMax = 157.5f;

    // 앞 메뉴들의 seed(20261109 ~ 20261148)와 메뉴 20이 더 쓸 수 있는 값(20261149, 20261150)을 피한다
    private const int ZoneSeed = 20261151;
    private const int ScatterSeed = 20261152;
    private const int PropSeed = 20261153;
    private const int LandmarkSeed = 20261154;

    // ---------- 구분 (풀습지 / 드문드문 / 작은 무리) ----------
    // 작은 무리가 습지 곳곳의 작은 숲 조각이 되도록 남서쪽 나무 무리와 같은 주기를 쓴다 (임시값)
    private const float ZoneNoisePeriod = 120f;
    private const float MarshAreaRatio = 0.80f;
    private const float GroveAreaRatio = 0.05f;
    // 분위수를 구할 때 C3를 표본 조사하는 간격 (m)
    private const float ZoneSampleSpacing = 5f;
    // Perlin 입력이 음수가 되지 않도록 노이즈 위치를 양수 쪽으로 옮기는 범위
    private const float NoiseOffsetMin = 100f;
    private const float NoiseOffsetMax = 1000f;
    private static readonly Vector2 ZoneNoiseOffset = CreateZoneNoiseOffset();
    private static readonly string[] ZoneLabels = { "풀습지", "드문드문", "작은 무리" };

    // 흩뿌리기 단위 칸 (10m × 10m)과 1ha 넓이 (MapTreeScatter와 같은 단위)
    private const float ScatterCellSize = 10f;
    private const float HectareArea = 10000f;

    // 나무 밀도 (1ha당, FieldZone 순서, 사용자 승인 임시값)
    private static readonly float[] TreeDensityMin = { 0f, 1f, 30f };
    private static readonly float[] TreeDensityMax = { 0f, 3f, 60f };
    // 이 구역만 고사목을 늘린다 (사용자 승인 임시값, 다른 구역은 MapTreeScatter.DeadTreeRatio)
    private const float DeadTreeRatio = 0.2f;
    // 나무 피벗 둘레 이 반지름 안 4곳과 피벗이 모두 29.3m 이상이어야 한다 (줄기와 뿌리 자리)
    private const float TreeFootprintRadius = 1f;

    // 바위 밀도 (1ha당, TerrainLabels 순서, 사용자 승인 임시값)
    private const int MarshTerrain = 0;
    private const int HillTerrain = 1;
    private static readonly string[] TerrainLabels = { "습지", "언덕 위" };
    private static readonly float[] RockDensityMin = { 1f, 5f };
    private static readonly float[] RockDensityMax = { 3f, 10f };
    // 그루터기, 속 빈 통나무는 물가 소품으로 따로 놓으므로 바위는 Stones만 쓴다
    private static readonly string[] RockPaths = MapTreeScatter.RockPaths.Where(path => path.StartsWith("Stones/")).ToArray();

    // 물가 소품 (사용자 승인 임시값). 물가 띠 넓이 × 목표 밀도만큼 정해 두고, 놓지 못하면 띠의 다른 칸을 다시 뽑는다
    private const float ShoreDistance = 15f;
    private const float ShorePropDensityMin = 1f;
    private const float ShorePropDensityMax = 2f;
    private const float ShorePropTargetDensity = 1.5f;
    private const int ShorePropMaxDrawsPerPiece = 10;
    private static readonly string[] ShorePropPaths = { "Objects/Prefab_Stump", "Objects/Prefab_HollowTrunk" };

    // 물 지도 칸 크기 (m). 물가 띠, 야영지 물가 후보, 다리 물길 후보를 찾는 데 쓴다
    private const float WaterGridSpacing = 2f;

    // ---------- 랜드마크 공통 ----------
    private const float LandmarkEdgeMargin = 50f;
    private const float LandmarkClearance = 8f;
    // 야영지와 다리 중심 사이 최소 거리 (다른 구역 랜드마크와 같은 값)
    private const float LandmarkMinSpacing = 100f;
    // 밑면을 바닥 둘레의 가장 낮은 땅보다 (높이 × 이 값)만큼 아래에 맞춘다 (서쪽 야영지와 같은 값)
    private const float StructureSinkRatio = 0.05f;
    // 밑면에서 바닥 둘레의 가장 높은 땅까지가 높이 × 이 값을 넘으면 묻힌 것으로 센다
    private const float PieceMaxBuryRatio = 0.5f;

    // ---------- 버려진 어부 야영지 ----------
    // 물가 후보는 넓이가 가장 큰 물 덩어리 이 개수의 물가 칸이다 (임시값)
    private const int CampPondCount = 3;
    // 물가 칸에서 뭍 쪽으로 야영지 중심을 들여 놓아 보는 거리 (m)
    private static readonly float[] CampShoreOffsets = { 8f, 11f, 14f };
    private const float CampMaxSlope = 8f;
    // 묶음 회전 단계 (도). 물 쪽 방향을 이 간격으로 반올림해 조각 바닥 표본점을 미리 구해 둔다
    private const float CampYawStep = 15f;
    // 물가 칸의 물 쪽 방향을 잴 때 둘러보는 반지름 (m)
    private const float CampNormalRadius = 6f;
    // 야영지 중심에서 물 쪽으로 물을 찾는 최대 거리와 간격 (m)
    private const float CampShoreMaxDistance = 25f;
    private const float ShoreMarchStep = 0.25f;

    private const string BoatPath = "Objects/Prefab_Boat";
    private const string PaddlePath = "Objects/Prefab_BoatPaddle";
    private const string HollowTrunkPath = "Objects/Prefab_HollowTrunk";
    private const string Firewood01Path = "Objects/Prefab_Firewood_01";
    private const string Firewood02Path = "Objects/Prefab_Firewood_02";
    private const string Firewood03Path = "Objects/Prefab_Firewood_03";
    // 불 자리 : 작은 돌을 원으로 (서쪽 야영지와 같은 돌, 크기)
    private const int FirePitStoneCount = 8;
    private const float FirePitRadius = 1.1f;
    private const float FirePitStoneScale = 0.35f;
    private static readonly string[] FirePitPaths = { "Stones/Prefab_SmallRock_01", "Stones/Prefab_SmallRock_02" };

    // 야영지 배치도 (임시값, 묶음 중심 기준, +z = 물 쪽). 가운데 불 자리와 앉는 통나무 3개, 뒤쪽(뭍 쪽) 양옆에 장작 2무리,
    // 물 쪽 동편에 끌어 올린 배와 노, 물 쪽 서편에 노 하나. 통나무와 장작만 1.3 ~ 1.5배, 배·노·돌은 원래 크기 (서쪽 야영지와 같은 규칙).
    // 물가에 반쯤 걸친 배는 자리마다 물가 위치가 달라 배치도에 넣지 않고 PlaceShoreBoat가 따로 놓는다
    private static readonly PieceSpec[] CampPieces = new[]
        {
            Piece(HollowTrunkPath, -3.7f, 0.8f, 80f, 1.4f),
            Piece(HollowTrunkPath, 3.5f, 1.5f, 110f, 1.4f),
            Piece(HollowTrunkPath, 0.3f, -3.8f, 0f, 1.3f),
            Piece(Firewood01Path, -9f, -6f, 10f, 1.4f),
            Piece(Firewood02Path, -7.6f, -4.8f, 75f, 1.3f),
            Piece(Firewood03Path, -8.5f, -3.8f, 150f, 1.5f),
            Piece(Firewood01Path, 8f, -8f, 200f, 1.3f),
            Piece(Firewood02Path, 9.4f, -6.6f, 260f, 1.5f),
            Piece(Firewood03Path, 10.2f, -8.4f, 320f, 1.4f),
            Piece(BoatPath, 8f, 7f, 25f, 1f),
            Piece(PaddlePath, 10.8f, 5f, 70f, 1f),
            Piece(PaddlePath, -6f, 7f, 160f, 1f)
        }
        .Concat(CreateRing(FirePitPaths, FirePitStoneCount, FirePitRadius, FirePitStoneScale))
        .ToArray();

    // ---------- 쓰러진 고사목 다리 ----------
    private const int BridgeCount = 2;
    // 물 폭 (29m 미만 구간, 사용자 승인 임시값)
    private const float BridgeMinWaterWidth = 6f;
    // 상한 12m에서 늘림 (다리 0곳이라 사용자 승인 조건 완화 임시값)
    private const float BridgeMaxWaterWidth = 16f;
    // 물 폭을 재는 방향 수 (0 ~ 180도를 나눔)와 재는 간격 (m)
    private const int BridgeDirectionCount = 12;
    private const float BridgeMarchStep = 0.5f;
    // 다리 끝(걸침)을 놓는 땅 높이 (m). 다리에만 쓰고, 나무·바위·소품·야영지는 PlaceMinHeight(29.3m) 그대로다 (사용자 승인 조건 완화 임시값)
    private const float BridgeBankHeight = 29.1f;
    // 수면 가장자리 너머로 BridgeBankHeight 땅을 찾는 최대 거리 (m)
    private const float BridgeMaxBankSearch = 10f;
    // 물길 방향(건너는 방향과 수직)으로 양쪽 모두 물 폭 × 이 값 안에 뭍이 없어야 물길로 본다
    private const float BridgeChannelRatio = 1f;
    // 양쪽 걸침 목표 길이와 최소 길이 (BridgeBankHeight 선 바깥, m, 임시값)
    private const float BridgeTargetBearing = 3f;
    private const float BridgeMinBearing = 1.5f;
    // 다리 나무 크기 범위. 하한은 다른 나무와 같고, 상한만 다리 나무에 한해 1.5배로 늘림 (사용자 승인 조건 완화 임시값)
    private const float BridgeScaleMin = MapTreeScatter.TreeScaleMin;
    private const float BridgeScaleMax = 1.5f;
    // 줄기 축에서 이 반지름(크기 1 기준, m) 안의 정점을 줄기로 본다. CapsuleCollider 반지름(0.35m)의 2배 정도
    private const float BridgeCoreRadius = 0.7f;
    private const float BridgeRollStep = 15f;
    // 줄기 기울기 (도). 걷는 면이 너무 기울지 않게 작게 둔다
    private const float BridgeMaxPitch = 4f;
    private const float BridgePitchStep = 0.5f;
    // 줄기를 따라 땅과 줄기 밑면을 견주는 간격 (m)
    private const float BridgeProfileStep = 0.5f;
    // 줄기 밑면을 가장 높은 땅보다 이만큼 묻는다 (m)
    private const float BridgeSinkDepth = 0.05f;
    // 양쪽 걸침 구간 모두 줄기 밑면과 땅 사이 틈이 이 값 이하여야 한다 (뜨지 않게, m)
    private const float BridgeMaxEndGap = 0.15f;
    // 물 위 구간 줄기 밑면의 수면 위 높이 범위 (m, 임시값)
    private const float BridgeMinWaterClearance = 0.05f;
    private const float BridgeMaxWaterClearance = 1f;
    // 두 다리 중심 사이 최소 거리 (m, 임시값)
    private const float BridgeMinSpacing = 200f;
    // 걷는 면 상자 너비, 두께 (m, 임시값)
    private const float BridgeWalkWidth = 0.8f;
    private const float BridgeWalkThickness = 0.5f;
    // 다리 둘레 비움 판정에 쓰는 줄기 반폭 (m)
    private const float BridgeHalfWidth = 1f;
    private static readonly string[] BridgeFailureLabels = { "없음", "나무가 짧아 걸침 부족", "걸침 구간에 29.3m 미만 땅", "수면 위 높이 범위 밖", "걸침 끝이 뜸" };

    // ---------- 언덕, 절벽 조각 ----------
    // 언덕 꼭대기를 찾을 때 지면 높이를 재는 간격 (m)과 찾는 사각형 배율 (남쪽 메뉴 18과 같은 값)
    private const float HilltopSearchStep = 2f;
    private const float HilltopSearchRangeRatio = 2f;
    // 꼭대기 이 반지름 안에는 절벽 조각을 두지 않는다 (m, 임시값)
    private const float HilltopClearRadius = 30f;
    private const int CliffCountMin = 3;
    private const int CliffCountMax = 5;
    private static readonly string[] CliffPaths =
    {
        "Stones/Prefab_Cliff_01", "Stones/Prefab_Cliff_02", "Stones/Prefab_Cliff_03", "Stones/Prefab_Cliff_04", "Stones/Prefab_Cliff_05"
    };
    // 크기 (임시값). 서쪽 큰 바위 무리 0.6배, 남서쪽 바위 노두 0.5배를 참고했다
    private const float CliffScaleMin = 0.4f;
    private const float CliffScaleMax = 0.6f;
    // 비탈 위쪽이 땅에 묻혀 바위가 땅에서 솟은 것처럼 보이게 조금 더 묻는다 (임시값)
    private const float CliffSinkRatio = 0.15f;
    // 후보 칸 간격, 비탈로 보는 최소 경사, 조각 사이 최소 거리, 둘레 비움 폭 (임시값)
    private const float CliffSearchStep = 4f;
    private const float CliffMinSlope = 10f;
    private const float CliffMinSpacing = 25f;
    private const float CliffClearance = 2f;

    // ---------- 공통 ----------
    // 렌더러 범위 둘레 8곳은 AABB보다 FootprintInset만큼 안쪽을 본다 (서쪽, 남쪽 배치와 같은 값)
    private const float FootprintInset = 0.7f;
    private static readonly Vector2[] RingOffsets =
    {
        new Vector2(-1f, -1f), new Vector2(0f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 0f),
        new Vector2(1f, 0f), new Vector2(-1f, 1f), new Vector2(0f, 1f), new Vector2(1f, 1f)
    };
    private const float MaxBuryGap = 3f;
    // 뜸 검수 (계산 오차만 허용)
    private const float HeightTolerance = 0.01f;

    // GPU Resident Drawer로 그리면 몸체가 안 보이는 것으로 확인된 셰이더 (서쪽, 남쪽 배치와 같은 목록)
    private static readonly string[] GpuDrivenBrokenShaderNames = { "Polyart/Dreamscape CliffAuto", "M_Water_Lake_Amp" };
    private const string DisallowGpuDrivenTypeName = "UnityEngine.Rendering.DisallowGPUDrivenRendering, Unity.RenderPipelines.GPUDriven.Runtime";
    // 셰이더를 찾지 못하거나 컴파일에 실패하면 Unity가 대신 쓰는 분홍 셰이더
    private const string ErrorShaderName = "Hidden/InternalErrorShader";

    // 거점 기준 8방위. 0도 = 북(+Z), 시계 방향
    private static readonly string[] DirectionLabels = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };

    // 노이즈 값의 구분 경계 [드문드문 시작, 작은 무리 시작]. 놓을 수 있는 땅이 지형 높이로 정해지므로 지형을 읽어 구한다.
    // 메뉴 20을 다시 실행하면 경계가 바뀌므로 저장해 두지 않고 실행할 때마다 구한다 (메뉴 23도 같은 메서드를 부른다)
    public static float[] CalculateZoneThresholds(Terrain terrain)
    {
        Rect southEast = GetSouthEastRect();
        int columns = Mathf.RoundToInt(southEast.width / ZoneSampleSpacing);
        int rows = Mathf.RoundToInt(southEast.height / ZoneSampleSpacing);
        var samples = new List<float>(columns * rows);
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                var position = new Vector2(southEast.xMin + (x + 0.5f) * ZoneSampleSpacing, southEast.yMin + (z + 0.5f) * ZoneSampleSpacing);
                if (IsInHabitat(position) || GetGroundHeight(terrain, position) < PlaceMinHeight) continue;
                samples.Add(SampleZoneNoise(position));
            }
        }
        samples.Sort();

        int marshCount = Mathf.RoundToInt(samples.Count * MarshAreaRatio);
        int groveCount = Mathf.RoundToInt(samples.Count * GroveAreaRatio);
        return new[] { samples[marshCount], samples[samples.Count - groveCount] };
    }

    // 같은 위치는 항상 같은 구분을 돌려준다. 서식지 반경 안은 풀습지다. 물과 29.3m 미만 땅은 부르는 쪽에서 따로 거른다
    public static MapTreeScatter.FieldZone GetSouthEastZone(Vector2 position, float[] thresholds)
    {
        if (IsInHabitat(position)) return MapTreeScatter.FieldZone.Open;

        float noise = SampleZoneNoise(position);
        if (noise >= thresholds[1]) return MapTreeScatter.FieldZone.Forest;
        if (noise >= thresholds[0]) return MapTreeScatter.FieldZone.Sparse;
        return MapTreeScatter.FieldZone.Open;
    }

    [MenuItem("Tools/Map/22. 남동쪽 나무·바위·랜드마크·서식지 배치")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        Scene scene = terrain.gameObject.scene;
        if (scene != SceneManager.GetActiveScene())
        {
            Debug.LogError("[MapSouthEastScatter] Game 씬을 활성 씬으로 두고 실행하세요.");
            return;
        }

        GameObject southEastRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == RegionName);
        if (southEastRoot == null)
        {
            Debug.LogError("씬 루트에 SouthEast가 없습니다. 먼저 'Tools/Map/1. 지형과 구역 부모 생성'을 실행하세요.");
            return;
        }

        System.Type disallowType = System.Type.GetType(DisallowGpuDrivenTypeName);
        if (disallowType == null)
        {
            Debug.LogError($"{DisallowGpuDrivenTypeName} 타입을 찾지 못했습니다. URP 패키지 버전이 바뀌었는지 확인하세요.");
            return;
        }

        // SouthEast/Ponds(메뉴 21)는 지우지 않는다
        Transform treeParent = ResetParent(southEastRoot.transform, TreeParentName);
        Transform rockParent = ResetParent(southEastRoot.transform, RockParentName);
        Transform landmarkParent = ResetParent(southEastRoot.transform, LandmarkParentName);
        Transform habitat = ResetParent(southEastRoot.transform, HabitatName);
        habitat.position = ToWorld(MapSouthEastShaper.HabitatCenter, GetGroundHeight(terrain, MapSouthEastShaper.HabitatCenter));

        var render = new RenderSetup { DisallowType = disallowType };
        Vector2 hilltop = FindHilltop(terrain);
        var landmarks = new LandmarkContext
        {
            Terrain = terrain,
            Render = render,
            Parent = landmarkParent,
            RockParent = rockParent,
            Random = new System.Random(LandmarkSeed),
            Water = CreateWaterMap(terrain),
            Hilltop = hilltop,
            HilltopHeight = GetGroundHeight(terrain, hilltop)
        };
        PlaceCamp(landmarks);
        PlaceBridges(landmarks);
        PlaceCliffs(landmarks);

        ScatterContext scatter = ScatterAll(landmarks, treeParent, rockParent);
        ScatterShoreProps(scatter);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        LogZones(scatter);
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

    private static bool IsInHabitat(Vector2 position)
    {
        return Vector2.Distance(position, MapSouthEastShaper.HabitatCenter) < MapSouthEastShaper.HabitatRadius;
    }

    // 지형과 같은 비틀린 언덕 윤곽 안인지
    private static bool IsOnHill(Vector2 position)
    {
        return MapSouthEastShaper.CalculateHillWeight(position) > 0f;
    }

    // ---------- 물 지도 ----------

    // C3를 칸으로 나눠 칸마다 지면 높이, 물(29m 미만)인지, 물 덩어리, 물·뭍까지 거리를 구한다
    private static WaterMap CreateWaterMap(Terrain terrain)
    {
        Rect southEast = GetSouthEastRect();
        int columns = Mathf.RoundToInt(southEast.width / WaterGridSpacing);
        int rows = Mathf.RoundToInt(southEast.height / WaterGridSpacing);
        var map = new WaterMap
        {
            Origin = southEast.min,
            Spacing = WaterGridSpacing,
            Columns = columns,
            Rows = rows,
            Heights = new float[rows, columns],
            IsWater = new bool[rows, columns]
        };
        var isLand = new bool[rows, columns];
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                float height = GetGroundHeight(terrain, map.GetCellCenter(x, z));
                map.Heights[z, x] = height;
                map.IsWater[z, x] = height < MapSouthEastShaper.WaterLevel;
                isLand[z, x] = !map.IsWater[z, x];
            }
        }
        map.WaterDistance = CalculateDistance(map.IsWater, WaterGridSpacing);
        map.LandDistance = CalculateDistance(isLand, WaterGridSpacing);
        LabelComponents(map);
        return map;
    }

    // isSource 칸까지의 대략 거리 (m). 앞뒤로 두 번 훑는 chamfer 방식 (가로세로 spacing, 대각선 spacing × √2)
    private static float[,] CalculateDistance(bool[,] isSource, float spacing)
    {
        int rows = isSource.GetLength(0);
        int columns = isSource.GetLength(1);
        float diagonal = spacing * Mathf.Sqrt(2f);
        var distance = new float[rows, columns];
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
                distance[z, x] = isSource[z, x] ? 0f : float.PositiveInfinity;
        }

        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                float value = distance[z, x];
                if (x > 0) value = Mathf.Min(value, distance[z, x - 1] + spacing);
                if (z > 0)
                {
                    value = Mathf.Min(value, distance[z - 1, x] + spacing);
                    if (x > 0) value = Mathf.Min(value, distance[z - 1, x - 1] + diagonal);
                    if (x < columns - 1) value = Mathf.Min(value, distance[z - 1, x + 1] + diagonal);
                }
                distance[z, x] = value;
            }
        }

        for (int z = rows - 1; z >= 0; z--)
        {
            for (int x = columns - 1; x >= 0; x--)
            {
                float value = distance[z, x];
                if (x < columns - 1) value = Mathf.Min(value, distance[z, x + 1] + spacing);
                if (z < rows - 1)
                {
                    value = Mathf.Min(value, distance[z + 1, x] + spacing);
                    if (x < columns - 1) value = Mathf.Min(value, distance[z + 1, x + 1] + diagonal);
                    if (x > 0) value = Mathf.Min(value, distance[z + 1, x - 1] + diagonal);
                }
                distance[z, x] = value;
            }
        }
        return distance;
    }

    // 8방향으로 이어진 물 칸에 같은 덩어리 번호를 붙이고 덩어리마다 넓이를 잰다 (메뉴 20, 21과 같은 이웃 규칙)
    private static void LabelComponents(WaterMap map)
    {
        map.Component = new int[map.Rows, map.Columns];
        map.ComponentAreas = new List<float>();
        for (int z = 0; z < map.Rows; z++)
        {
            for (int x = 0; x < map.Columns; x++)
                map.Component[z, x] = -1;
        }

        float cellArea = map.Spacing * map.Spacing;
        for (int z = 0; z < map.Rows; z++)
        {
            for (int x = 0; x < map.Columns; x++)
            {
                if (!map.IsWater[z, x] || map.Component[z, x] >= 0) continue;

                int id = map.ComponentAreas.Count;
                int count = 0;
                var queue = new Queue<Vector2Int>();
                map.Component[z, x] = id;
                queue.Enqueue(new Vector2Int(x, z));
                while (queue.Count > 0)
                {
                    Vector2Int cell = queue.Dequeue();
                    count++;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cell.x + dx;
                            int nz = cell.y + dz;
                            if (nx < 0 || nz < 0 || nx >= map.Columns || nz >= map.Rows) continue;
                            if (!map.IsWater[nz, nx] || map.Component[nz, nx] >= 0) continue;

                            map.Component[nz, nx] = id;
                            queue.Enqueue(new Vector2Int(nx, nz));
                        }
                    }
                }
                map.ComponentAreas.Add(count * cellArea);
            }
        }
    }

    // origin에서 direction으로 step씩 가며 지면이 height 이상인 첫 거리 (m). maxDistance 안에 없으면 무한대
    private static float MarchToHeight(Terrain terrain, Vector2 origin, Vector2 direction, float height, float maxDistance, float step)
    {
        for (float distance = step; distance <= maxDistance; distance += step)
        {
            if (GetGroundHeight(terrain, origin + direction * distance) >= height) return distance;
        }
        return float.PositiveInfinity;
    }

    // ---------- 언덕 ----------

    // 언덕 위(지형과 같은 비틀린 윤곽 안)를 격자로 돌며 지면이 가장 높은 곳. 비틀기 때문에 꼭대기가 언덕 중심에서 벗어날 수 있어 직접 찾는다
    private static Vector2 FindHilltop(Terrain terrain)
    {
        Rect southEast = GetSouthEastRect();
        float range = MapSouthEastShaper.HillRadius * HilltopSearchRangeRatio;
        Vector2 center = MapSouthEastShaper.HillCenter;
        Vector2 best = center;
        float bestHeight = float.MinValue;
        for (float z = Mathf.Max(southEast.yMin, center.y - range); z <= Mathf.Min(southEast.yMax, center.y + range); z += HilltopSearchStep)
        {
            for (float x = Mathf.Max(southEast.xMin, center.x - range); x <= Mathf.Min(southEast.xMax, center.x + range); x += HilltopSearchStep)
            {
                var position = new Vector2(x, z);
                if (!IsOnHill(position)) continue;

                float height = GetGroundHeight(terrain, position);
                if (height <= bestHeight) continue;
                bestHeight = height;
                best = position;
            }
        }
        return best;
    }

    // ---------- 버려진 어부 야영지 ----------

    private static PieceSpec Piece(string path, float x, float z, float yaw, float scale)
    {
        return new PieceSpec { Path = path, Offset = new Vector2(x, z), Yaw = yaw, Scale = scale };
    }

    // 묶음 중심을 원 중심으로, 원 둘레에 count개를 같은 간격(북쪽부터 시계 방향)으로 놓고 가운데를 보게 돌린다 (서쪽 메뉴 12와 같은 방식)
    private static IEnumerable<PieceSpec> CreateRing(string[] paths, int count, float radius, float scale)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = i * 360f / count;
            float radian = angle * Mathf.Deg2Rad;
            yield return Piece(paths[i % paths.Length], Mathf.Sin(radian) * radius, Mathf.Cos(radian) * radius, angle + 180f, scale);
        }
    }

    private static void PlaceCamp(LandmarkContext context)
    {
        LandmarkBuild build = BuildGroup(context, CampLabel, CampPieces);
        if (build == null) return;

        CampFootprints footprints = MeasureCampFootprints(build);
        CampFit? fit = FindCampFit(context, footprints);
        if (fit == null)
        {
            Debug.LogWarning($"[MapSouthEastScatter] '{CampLabel}' 자리를 찾지 못했습니다. 물가 후보 규칙(물 덩어리 수, 들여 놓는 거리, 경사)을 확인하세요.");
            Object.DestroyImmediate(build.Group.gameObject);
            return;
        }

        PlaceCampAt(context, build, footprints, fit.Value);
    }

    // 배치도대로 월드 원점에 조립한다. 묶음 중심이 원점이라 조각의 월드 XZ가 곧 중심 기준 위치가 된다
    private static LandmarkBuild BuildGroup(LandmarkContext context, string label, PieceSpec[] pieces)
    {
        var build = new LandmarkBuild { Group = new GameObject(label).transform };
        build.Group.SetParent(context.Parent, false);
        build.Group.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        foreach (PieceSpec piece in pieces)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + piece.Path + ".prefab");
            if (prefab == null)
            {
                Debug.LogError($"[MapSouthEastScatter] {piece.Path} prefab을 찾지 못해 '{label}'을 놓지 못했습니다. 배치도의 경로를 확인하세요.");
                Object.DestroyImmediate(build.Group.gameObject);
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, build.Group);
            instance.transform.localPosition = new Vector3(piece.Offset.x, 0f, piece.Offset.y);
            instance.transform.localRotation = Quaternion.Euler(0f, piece.Yaw, 0f);
            instance.transform.localScale = Vector3.one * piece.Scale;
            FinishInstance(context.Render, instance, prefab.name, build.Stats);

            (float bottom, float top) = GetMeshHeightRange(instance);
            build.Pieces.Add(new PlacedPiece { Transform = instance.transform, Height = top - bottom });
        }
        return build;
    }

    // 묶음을 CampYawStep마다 돌려 조각별 바닥 표본점과 바닥 반지름을 잰 뒤 회전을 되돌린다
    private static CampFootprints MeasureCampFootprints(LandmarkBuild build)
    {
        int yawCount = Mathf.RoundToInt(360f / CampYawStep);
        var footprints = new CampFootprints
        {
            Points = new Vector2[yawCount][][],
            Radius = new float[yawCount],
            Heights = build.Pieces.Select(piece => piece.Height).ToArray()
        };
        for (int i = 0; i < yawCount; i++)
        {
            build.Group.rotation = Quaternion.Euler(0f, i * CampYawStep, 0f);
            footprints.Points[i] = new Vector2[build.Pieces.Count][];
            for (int piece = 0; piece < build.Pieces.Count; piece++)
            {
                Bounds bounds = GetRendererBounds(build.Pieces[piece].Transform.gameObject);
                footprints.Points[i][piece] = GetFootprintPoints(bounds);
                footprints.Radius[i] = Mathf.Max(footprints.Radius[i], ToXZ(bounds.center).magnitude + Mathf.Max(bounds.extents.x, bounds.extents.z));
            }
        }
        build.Group.rotation = Quaternion.identity;
        return footprints;
    }

    // 가장 큰 물 덩어리 CampPondCount개의 물가 칸마다 물 쪽 방향으로 묶음을 돌리고, 뭍 쪽으로 CampShoreOffsets만큼 들여 놓아 본다
    private static CampFit? FindCampFit(LandmarkContext context, CampFootprints footprints)
    {
        WaterMap map = context.Water;
        var ponds = new HashSet<int>(Enumerable.Range(0, map.ComponentAreas.Count)
            .OrderByDescending(id => map.ComponentAreas[id])
            .Take(CampPondCount));
        int yawCount = footprints.Points.Length;

        CampFit? best = null;
        for (int z = 0; z < map.Rows; z++)
        {
            for (int x = 0; x < map.Columns; x++)
            {
                if (!IsShoreCell(map, x, z, ponds)) continue;

                // 물 쪽 방향을 회전 단계로 반올림하고, 그 회전에서 묶음 +z가 실제로 향하는 방향을 쓴다
                Vector2 toWater = GetWaterDirection(map, x, z);
                float yaw = Mathf.Atan2(toWater.x, toWater.y) * Mathf.Rad2Deg;
                int yawIndex = (Mathf.RoundToInt(yaw / CampYawStep) % yawCount + yawCount) % yawCount;
                float radian = yawIndex * CampYawStep * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Sin(radian), Mathf.Cos(radian));

                Vector2 shore = map.GetCellCenter(x, z);
                foreach (float offset in CampShoreOffsets)
                {
                    CampFit? fit = MeasureCamp(context, footprints, yawIndex, shore - direction * offset, direction);
                    if (fit != null && (best == null || IsBetterCampFit(fit.Value, best.Value))) best = fit;
                }
            }
        }
        return best;
    }

    // 물이 아니고, 8방향 이웃에 고른 물 덩어리의 물 칸이 있는 칸
    private static bool IsShoreCell(WaterMap map, int x, int z, HashSet<int> ponds)
    {
        if (map.IsWater[z, x]) return false;

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx;
                int nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= map.Columns || nz >= map.Rows) continue;
                if (map.IsWater[nz, nx] && ponds.Contains(map.Component[nz, nx])) return true;
            }
        }
        return false;
    }

    // 반지름 CampNormalRadius 안 물 칸 방향의 평균 (단위 벡터)
    private static Vector2 GetWaterDirection(WaterMap map, int x, int z)
    {
        int reach = Mathf.CeilToInt(CampNormalRadius / map.Spacing);
        Vector2 sum = Vector2.zero;
        for (int dz = -reach; dz <= reach; dz++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                int nx = x + dx;
                int nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= map.Columns || nz >= map.Rows || !map.IsWater[nz, nx]) continue;

                Vector2 offset = new Vector2(dx, dz) * map.Spacing;
                if (offset.magnitude <= CampNormalRadius) sum += offset.normalized;
            }
        }
        return sum.normalized;
    }

    // 자리 조건을 못 맞추면 null. 조각 바닥 표본점에 29.3m 미만 땅이 하나라도 있으면 바로 버린다 (대부분 후보가 여기서 걸러진다)
    private static CampFit? MeasureCamp(LandmarkContext context, CampFootprints footprints, int yawIndex, Vector2 center, Vector2 direction)
    {
        float radius = footprints.Radius[yawIndex];
        if (!IsInsideSouthEast(center, LandmarkEdgeMargin + radius)) return null;
        if (Vector2.Distance(center, MapSouthEastShaper.HabitatCenter) < MapSouthEastShaper.HabitatRadius + radius) return null;

        float slope = GetSlope(context.Terrain, center);
        if (slope > CampMaxSlope) return null;

        float groundMin = float.MaxValue;
        float groundMax = float.MinValue;
        int brokenCount = 0;
        Vector2[][] pieces = footprints.Points[yawIndex];
        float[] heights = footprints.Heights;
        for (int piece = 0; piece < pieces.Length; piece++)
        {
            float lowest = float.MaxValue;
            float highest = float.MinValue;
            foreach (Vector2 point in pieces[piece])
            {
                float ground = GetGroundHeight(context.Terrain, point + center);
                if (ground < PlaceMinHeight) return null;
                lowest = Mathf.Min(lowest, ground);
                highest = Mathf.Max(highest, ground);
            }
            groundMin = Mathf.Min(groundMin, lowest);
            groundMax = Mathf.Max(groundMax, highest);
            if (IsBuried(lowest, highest, heights[piece], StructureSinkRatio)) brokenCount++;
        }

        float shoreDistance = MarchBelowWater(context.Terrain, center, direction);
        if (float.IsPositiveInfinity(shoreDistance)) return null;

        return new CampFit
        {
            Center = center,
            Direction = direction,
            YawIndex = yawIndex,
            BrokenCount = brokenCount,
            GroundRange = groundMax - groundMin,
            Slope = slope,
            ShorePoint = center + direction * shoreDistance
        };
    }

    // origin에서 direction으로 가며 처음 물(29m 미만)을 만난 거리. CampShoreMaxDistance 안에 없으면 무한대
    private static float MarchBelowWater(Terrain terrain, Vector2 origin, Vector2 direction)
    {
        for (float distance = ShoreMarchStep; distance <= CampShoreMaxDistance; distance += ShoreMarchStep)
        {
            if (GetGroundHeight(terrain, origin + direction * distance) < MapSouthEastShaper.WaterLevel) return distance;
        }
        return float.PositiveInfinity;
    }

    private static bool IsBetterCampFit(CampFit fit, CampFit best)
    {
        if (fit.BrokenCount != best.BrokenCount) return fit.BrokenCount < best.BrokenCount;
        return fit.GroundRange < best.GroundRange;
    }

    // 조립한 묶음을 찾은 자리로 옮겨 조각마다 높이를 맞추고, 물가에 걸친 배를 놓는다. 묶음 GameObject는 중심의 지면 높이에 둔다
    private static void PlaceCampAt(LandmarkContext context, LandmarkBuild build, CampFootprints footprints, CampFit fit)
    {
        float centerGround = GetGroundHeight(context.Terrain, fit.Center);
        build.Group.SetPositionAndRotation(ToWorld(fit.Center, centerGround), Quaternion.Euler(0f, fit.YawIndex * CampYawStep, 0f));
        int brokenCount = build.Pieces.Count(piece => GroundInstance(context.Terrain, piece.Transform.gameObject, StructureSinkRatio));
        context.CampPieces.AddRange(build.Pieces.Select(piece => piece.Transform));

        string boatNote = PlaceShoreBoat(context, build, fit);
        float radius = footprints.Radius[fit.YawIndex];
        if (context.ShoreBoat != null)
        {
            Bounds boatBounds = GetRendererBounds(context.ShoreBoat.gameObject);
            radius = Mathf.Max(radius, Vector2.Distance(fit.Center, ToXZ(boatBounds.center)) + Mathf.Max(boatBounds.extents.x, boatBounds.extents.z));
        }

        context.Blockers.Add(new Blocker { From = fit.Center, To = fit.Center, Radius = radius + LandmarkClearance });
        context.Centers.Add(fit.Center);
        context.CampBrokenCount += brokenCount;
        MergeStats(context.Stats, build.Stats);

        int pond = FindNearestComponent(context.Water, fit.ShorePoint);
        string pondText = pond >= 0 ? $"{context.Water.ComponentAreas[pond]:F0}m²" : "-";
        context.Notes.Add($"{CampLabel} : 중심 ({fit.Center.x:F1}, {fit.Center.y:F1}) 지면 {centerGround:F2}m, 물 쪽 방향 {fit.YawIndex * CampYawStep:F0}도, " +
                          $"바닥 반지름 {radius:F1}m (물가 배 포함), 경사 {fit.Slope:F1}도 (기준 {CampMaxSlope}도 이하), 바닥 아래 지면 높이 차 {fit.GroundRange:F2}m, " +
                          $"중심에서 물가까지 {Vector2.Distance(fit.Center, fit.ShorePoint):F1}m, 물 덩어리 넓이 {pondText}, " +
                          $"조각 {build.Pieces.Count + (context.ShoreBoat != null ? 1 : 0)}개 (뜨거나 묻힘 {brokenCount}), " +
                          $"Disallow 붙임 {build.Stats.DisallowCount}, 충돌체 추가 {build.Stats.ColliderCount}\n" +
                          $"  {boatNote}\n  prefab : {DescribePrefabs(build.Group)}");
    }

    // 물가에 반쯤 걸친 배 : 중심을 처음 물을 만난 곳에 두고 긴 축을 물 쪽으로 돌린다. 밑면은 바닥 표본점 중 물이 아닌 땅의 가장 낮은 곳에 맞춘다
    // (물 쪽 바닥은 수면 아래라 그 높이에 맞추면 배가 잠긴다). 결과 설명을 돌려준다
    private static string PlaceShoreBoat(LandmarkContext context, LandmarkBuild build, CampFit fit)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + BoatPath + ".prefab");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, build.Group);
        instance.transform.SetPositionAndRotation(ToWorld(fit.ShorePoint, MapSouthEastShaper.WaterLevel), Quaternion.identity);
        instance.transform.localScale = Vector3.one;

        // 회전 없이 놓았을 때 범위가 x로 더 길면 긴 축이 x이므로 90도 더 돌려 긴 축을 물 쪽으로 맞춘다
        Bounds upright = GetRendererBounds(instance);
        float yaw = Mathf.Atan2(fit.Direction.x, fit.Direction.y) * Mathf.Rad2Deg + (upright.extents.x > upright.extents.z ? 90f : 0f);
        instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        FinishInstance(context.Render, instance, prefab.name, build.Stats);

        float[] grounds = GetFootprintPoints(GetRendererBounds(instance)).Select(point => GetGroundHeight(context.Terrain, point)).ToArray();
        float[] dryGrounds = grounds.Where(ground => ground >= MapSouthEastShaper.WaterLevel).DefaultIfEmpty(MapSouthEastShaper.WaterLevel).ToArray();
        (float bottom, float top) = GetMeshHeightRange(instance);
        float height = top - bottom;
        float lowestDry = dryGrounds.Min();
        instance.transform.position += Vector3.up * (lowestDry - height * StructureSinkRatio - bottom);

        context.ShoreBoat = instance.transform;
        context.CampPieces.Add(instance.transform);
        int wetCount = grounds.Count(ground => ground < MapSouthEastShaper.WaterLevel);
        bool isBuried = IsBuried(lowestDry, dryGrounds.Max(), height, StructureSinkRatio);
        return $"물가에 걸친 배 : ({fit.ShorePoint.x:F1}, {fit.ShorePoint.y:F1}), 바닥 표본점 {grounds.Length}곳 중 물 위 {wetCount}곳 (예외), " +
               $"밑면 수면 대비 {GetMeshHeightRange(instance).Bottom - MapSouthEastShaper.WaterLevel:+0.00;-0.00}m, 뭍 쪽 묻힘 {(isBuried ? "있음" : "없음")}";
    }

    // 지점에서 가장 가까운 물 덩어리 번호 (물 지도 칸 기준, 반경 3칸 안. 없으면 -1)
    private static int FindNearestComponent(WaterMap map, Vector2 position)
    {
        Vector2 local = (position - map.Origin) / map.Spacing;
        int cx = Mathf.FloorToInt(local.x);
        int cz = Mathf.FloorToInt(local.y);
        for (int reach = 0; reach <= 3; reach++)
        {
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = cx + dx;
                    int z = cz + dz;
                    if (x < 0 || z < 0 || x >= map.Columns || z >= map.Rows) continue;
                    if (map.Component[z, x] >= 0) return map.Component[z, x];
                }
            }
        }
        return -1;
    }

    // ---------- 쓰러진 고사목 다리 ----------

    // BridgeBankHeight 땅 사이가 짧은 물길부터 맞춰 보고, 먼저 놓은 다리와 BridgeMinSpacing, 야영지와 LandmarkMinSpacing 이상 떨어진 곳에 BridgeCount개까지 놓는다
    private static void PlaceBridges(LandmarkContext context)
    {
        List<Crossing> crossings = FindCrossings(context);
        context.CrossingCount = crossings.Count;
        TrunkModel[] models = MapTreeScatter.DeadTreePaths.Select(LoadTrunkModel).ToArray();

        var placed = new List<Vector2>();
        foreach (Crossing crossing in crossings.OrderBy(candidate => candidate.BankWidth))
        {
            string result;
            if (placed.Count >= BridgeCount) result = "다리를 다 놓아 맞추지 않음";
            else if (placed.Any(center => Vector2.Distance(center, crossing.Center) < BridgeMinSpacing)) result = $"놓은 다리와 {BridgeMinSpacing}m 안";
            else if (context.Centers.Any(center => Vector2.Distance(center, crossing.Center) < LandmarkMinSpacing)) result = $"야영지와 {LandmarkMinSpacing}m 안";
            else
            {
                TrunkModel model = models[placed.Count % models.Length];
                BridgeFit? fit = FitBridge(context.Terrain, model, crossing, out BridgeFailure failure);
                if (fit == null)
                {
                    context.BridgeFailures[(int)failure]++;
                    result = $"맞춤 실패 ({BridgeFailureLabels[(int)failure]}, {model.Prefab.name} 길이 {model.Length:F1}m × 최대 {BridgeScaleMax}배)";
                }
                else
                {
                    PlaceBridge(context, model, crossing, fit.Value, placed.Count);
                    placed.Add(crossing.Center);
                    result = $"다리 {placed.Count} 놓음";
                }
            }
            context.CrossingNotes.Add($"  ({crossing.Center.x:F1}, {crossing.Center.y:F1}) 물 폭 {crossing.WaterWidth:F1}m, " +
                                      $"{BridgeBankHeight}m 땅 사이 {crossing.BankWidth:F1}m : {result}");
        }

        if (placed.Count < BridgeCount)
            Debug.LogWarning($"[MapSouthEastScatter] 고사목 다리를 {placed.Count}/{BridgeCount}곳만 놓았습니다. 물길 후보 {crossings.Count}곳, 맞춤 실패 : {DescribeBridgeFailures(context.BridgeFailures)}");
        context.Centers.AddRange(placed);
    }

    // 물길 가운데 줄(뭍까지 거리가 8방향 이웃보다 작지 않은 물 칸) 중 물 폭이 맞을 수 있는 칸마다 건너는 자리를 잰다
    private static List<Crossing> FindCrossings(LandmarkContext context)
    {
        WaterMap map = context.Water;
        float minHalf = BridgeMinWaterWidth / 2f - map.Spacing;
        float maxHalf = BridgeMaxWaterWidth / 2f + map.Spacing;
        var crossings = new List<Crossing>();
        for (int z = 1; z < map.Rows - 1; z++)
        {
            for (int x = 1; x < map.Columns - 1; x++)
            {
                if (!map.IsWater[z, x]) continue;

                float landDistance = map.LandDistance[z, x];
                if (landDistance < minHalf || landDistance > maxHalf || !IsRidgeCell(map, x, z)) continue;

                Vector2 center = map.GetCellCenter(x, z);
                if (!IsInsideSouthEast(center, LandmarkEdgeMargin)) continue;

                Crossing? crossing = MeasureCrossing(context.Terrain, center);
                if (crossing != null) crossings.Add(crossing.Value);
            }
        }
        return crossings;
    }

    private static bool IsRidgeCell(WaterMap map, int x, int z)
    {
        float value = map.LandDistance[z, x];
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (map.LandDistance[z + dz, x + dx] > value) return false;
            }
        }
        return true;
    }

    // 0 ~ 180도를 BridgeDirectionCount개로 나눠 물 폭(양쪽 29m 땅 사이)을 재고 가장 좁은 방향을 건너는 방향으로 삼는다.
    // 물 폭이 BridgeMinWaterWidth ~ BridgeMaxWaterWidth이고, 물길 방향 양쪽으로 물 폭 × BridgeChannelRatio 안에 뭍이 없어야 한다.
    // 가운데를 물길 가운데로 옮긴 뒤 양쪽 BridgeBankHeight 땅까지 거리를 잰다
    private static Crossing? MeasureCrossing(Terrain terrain, Vector2 center)
    {
        float waterLevel = MapSouthEastShaper.WaterLevel;
        float bestWidth = float.PositiveInfinity;
        Vector2 bestDirection = Vector2.zero;
        float bestShift = 0f;
        for (int i = 0; i < BridgeDirectionCount; i++)
        {
            float radian = i * Mathf.PI / BridgeDirectionCount;
            var direction = new Vector2(Mathf.Sin(radian), Mathf.Cos(radian));
            float plus = MarchToHeight(terrain, center, direction, waterLevel, BridgeMaxWaterWidth, BridgeMarchStep);
            if (plus >= bestWidth) continue;
            float minus = MarchToHeight(terrain, center, -direction, waterLevel, BridgeMaxWaterWidth, BridgeMarchStep);
            if (plus + minus >= bestWidth) continue;

            bestWidth = plus + minus;
            bestDirection = direction;
            bestShift = (plus - minus) / 2f;
        }
        if (bestWidth < BridgeMinWaterWidth || bestWidth > BridgeMaxWaterWidth) return null;

        Vector2 middle = center + bestDirection * bestShift;
        var along = new Vector2(-bestDirection.y, bestDirection.x);
        float channelReach = bestWidth * BridgeChannelRatio;
        if (MarchToHeight(terrain, middle, along, waterLevel, channelReach, BridgeMarchStep) <= channelReach) return null;
        if (MarchToHeight(terrain, middle, -along, waterLevel, channelReach, BridgeMarchStep) <= channelReach) return null;

        float bankReach = bestWidth / 2f + BridgeMaxBankSearch;
        float bankPlus = MarchToHeight(terrain, middle, bestDirection, BridgeBankHeight, bankReach, BridgeMarchStep);
        float bankMinus = MarchToHeight(terrain, middle, -bestDirection, BridgeBankHeight, bankReach, BridgeMarchStep);
        if (float.IsPositiveInfinity(bankPlus) || float.IsPositiveInfinity(bankMinus)) return null;

        return new Crossing
        {
            Center = middle,
            Direction = bestDirection,
            WaterWidth = bestWidth,
            BankMinus = bankMinus,
            BankPlus = bankPlus,
            BankWidth = bankMinus + bankPlus
        };
    }

    // prefab을 잠깐 원점에 놓고 LOD0 정점을 루트 기준으로 읽은 뒤 지운다
    private static TrunkModel LoadTrunkModel(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + path + ".prefab");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;
        Vector3[] vertices = GetLod0Vertices(instance).ToArray();
        Object.DestroyImmediate(instance);

        bool[] isCore = vertices.Select(vertex => new Vector2(vertex.x, vertex.z).magnitude <= BridgeCoreRadius).ToArray();
        var model = new TrunkModel { Prefab = prefab, Vertices = vertices, IsCore = isCore, Length = vertices.Max(vertex => vertex.y) };
        model.Roll = ChooseRoll(model);
        return model;
    }

    // 줄기 축을 돌리는 각도 중 가지가 줄기 아래로 가장 적게 처지는 것. 수평으로 눕히면 가지 높이는 눕힌 방향과 상관없어 한 번만 구한다
    private static float ChooseRoll(TrunkModel model)
    {
        float bestRoll = 0f;
        float bestDrop = float.MaxValue;
        for (float roll = 0f; roll < 360f; roll += BridgeRollStep)
        {
            Quaternion rotation = CreateTrunkRotation(Vector3.forward, 0f, roll);
            float drop = 0f;
            for (int i = 0; i < model.Vertices.Length; i++)
            {
                if (!model.IsCore[i]) drop = Mathf.Max(drop, -(rotation * model.Vertices[i]).y);
            }
            if (drop >= bestDrop) continue;
            bestDrop = drop;
            bestRoll = roll;
        }
        return bestRoll;
    }

    // 나무 local Y(줄기)를 수평 방향 trunkDirection으로 눕히고, 줄기 축으로 roll만큼 굴린 뒤, 수평 옆 축으로 pitch만큼 기울인다
    private static Quaternion CreateTrunkRotation(Vector3 trunkDirection, float pitch, float roll)
    {
        Vector3 side = Vector3.Cross(Vector3.up, trunkDirection);
        return Quaternion.AngleAxis(pitch, side) * Quaternion.AngleAxis(roll, trunkDirection) * Quaternion.LookRotation(Vector3.up, trunkDirection);
    }

    // 나무 길이가 (BridgeBankHeight 땅 사이 + 양쪽 BridgeTargetBearing)이 되게 크기를 고르고 허용 범위로 자른다. 밑동을 -Direction 쪽 물가에 두고
    // 기울기를 돌려 보며 양쪽 걸침 끝 틈이 가장 작은 것을 고른다
    private static BridgeFit? FitBridge(Terrain terrain, TrunkModel model, Crossing crossing, out BridgeFailure failure)
    {
        float scale = Mathf.Clamp((crossing.BankWidth + 2f * BridgeTargetBearing) / model.Length, BridgeScaleMin, BridgeScaleMax);
        float length = model.Length * scale;
        float bearing = (length - crossing.BankWidth) / 2f;
        failure = BridgeFailure.Bearing;
        if (bearing < BridgeMinBearing) return null;

        Vector2 basePosition = crossing.Center - crossing.Direction * (crossing.BankMinus + bearing);
        BridgeFit? best = null;
        int pitchSteps = Mathf.RoundToInt(BridgeMaxPitch / BridgePitchStep);
        for (int step = -pitchSteps; step <= pitchSteps; step++)
        {
            BridgeFit? fit = MeasureBridge(terrain, model, crossing, scale, step * BridgePitchStep, basePosition, bearing, out BridgeFailure stepFailure);
            if (fit == null)
            {
                failure = stepFailure;
                continue;
            }
            if (best == null || Mathf.Max(fit.Value.GapBase, fit.Value.GapTip) < Mathf.Max(best.Value.GapBase, best.Value.GapTip)) best = fit;
        }
        if (best != null) failure = BridgeFailure.None;
        return best;
    }

    // 줄기를 따라 BridgeProfileStep 칸마다 줄기 밑면·윗면과 가지 밑면(피벗 기준)을 정점으로 재고, 칸 아래 지면과 견준다.
    // 피벗 높이는 모든 칸에서 줄기 밑면이 (지면 - BridgeSinkDepth) 이상이 되는 가장 낮은 높이다 (가장 높은 땅에 얹힘)
    private static BridgeFit? MeasureBridge(Terrain terrain, TrunkModel model, Crossing crossing, float scale, float pitch,
        Vector2 basePosition, float bearing, out BridgeFailure failure)
    {
        Vector3 direction = ToWorld(crossing.Direction, 0f);
        Quaternion rotation = CreateTrunkRotation(direction, pitch, model.Roll);
        // 밑동 정점이 피벗보다 조금 뒤에 있을 수 있어 한 칸 앞에서 시작한다
        float start = -BridgeProfileStep;
        int binCount = Mathf.CeilToInt((model.Length * scale - start) / BridgeProfileStep) + 1;
        var coreBottom = Enumerable.Repeat(float.PositiveInfinity, binCount).ToArray();
        var coreTop = Enumerable.Repeat(float.NegativeInfinity, binCount).ToArray();
        var branchBottom = Enumerable.Repeat(float.PositiveInfinity, binCount).ToArray();
        for (int i = 0; i < model.Vertices.Length; i++)
        {
            Vector3 offset = rotation * (model.Vertices[i] * scale);
            int bin = Mathf.FloorToInt((Vector3.Dot(offset, direction) - start) / BridgeProfileStep);
            if (bin < 0 || bin >= binCount) continue;

            if (model.IsCore[i])
            {
                coreBottom[bin] = Mathf.Min(coreBottom[bin], offset.y);
                coreTop[bin] = Mathf.Max(coreTop[bin], offset.y);
            }
            else
            {
                branchBottom[bin] = Mathf.Min(branchBottom[bin], offset.y);
            }
        }

        var grounds = new float[binCount];
        float pivotHeight = float.NegativeInfinity;
        for (int bin = 0; bin < binCount; bin++)
        {
            grounds[bin] = GetGroundHeight(terrain, basePosition + crossing.Direction * GetBinCenter(start, bin));
            if (!float.IsPositiveInfinity(coreBottom[bin])) pivotHeight = Mathf.Max(pivotHeight, grounds[bin] - BridgeSinkDepth - coreBottom[bin]);
        }

        float bankStart = bearing;
        float bankEnd = bearing + crossing.BankWidth;
        float waterLevel = MapSouthEastShaper.WaterLevel;
        var fit = new BridgeFit
        {
            Scale = scale,
            Length = model.Length * scale,
            Bearing = bearing,
            Pitch = pitch,
            Pivot = ToWorld(basePosition, pivotHeight),
            Rotation = rotation,
            GapBase = float.PositiveInfinity,
            GapTip = float.PositiveInfinity,
            WaterClearMin = float.PositiveInfinity,
            WaterClearMax = float.NegativeInfinity,
            BranchClearMin = float.PositiveInfinity
        };
        float walkTopSum = 0f;
        int waterBinCount = 0;
        failure = BridgeFailure.WetBearing;
        for (int bin = 0; bin < binCount; bin++)
        {
            float along = GetBinCenter(start, bin);
            float surface = Mathf.Max(grounds[bin], waterLevel);
            if (!float.IsPositiveInfinity(branchBottom[bin])) fit.BranchClearMin = Mathf.Min(fit.BranchClearMin, pivotHeight + branchBottom[bin] - surface);
            if (float.IsPositiveInfinity(coreBottom[bin])) continue;

            float bottom = pivotHeight + coreBottom[bin];
            bool isBearing = along < bankStart || along > bankEnd;
            if (isBearing && grounds[bin] < BridgeBankHeight) return null;
            if (along < bankStart) fit.GapBase = Mathf.Min(fit.GapBase, bottom - grounds[bin]);
            if (along > bankEnd) fit.GapTip = Mathf.Min(fit.GapTip, bottom - grounds[bin]);
            if (grounds[bin] >= waterLevel) continue;

            fit.WaterClearMin = Mathf.Min(fit.WaterClearMin, bottom - waterLevel);
            fit.WaterClearMax = Mathf.Max(fit.WaterClearMax, bottom - waterLevel);
            walkTopSum += pivotHeight + coreTop[bin];
            waterBinCount++;
        }

        failure = BridgeFailure.Bearing;
        if (float.IsPositiveInfinity(fit.GapBase) || float.IsPositiveInfinity(fit.GapTip) || waterBinCount == 0) return null;
        failure = BridgeFailure.WaterClearance;
        if (fit.WaterClearMin < BridgeMinWaterClearance || fit.WaterClearMax > BridgeMaxWaterClearance) return null;
        failure = BridgeFailure.EndGap;
        if (Mathf.Max(fit.GapBase, fit.GapTip) > BridgeMaxEndGap) return null;

        failure = BridgeFailure.None;
        fit.WalkTop = walkTopSum / waterBinCount;
        return fit;
    }

    private static float GetBinCenter(float start, int bin)
    {
        return start + (bin + 0.5f) * BridgeProfileStep;
    }

    // 다리 묶음은 물길 가운데(걷는 면 높이)에 두고, 그 아래에 눕힌 고사목과 걷는 면 충돌체를 둔다
    private static void PlaceBridge(LandmarkContext context, TrunkModel model, Crossing crossing, BridgeFit fit, int index)
    {
        string label = $"{BridgeLabel} {index + 1}";
        Transform group = CreateChild(context.Parent, label);
        group.position = ToWorld(crossing.Center, fit.WalkTop);

        var tree = (GameObject)PrefabUtility.InstantiatePrefab(model.Prefab, group);
        tree.transform.SetPositionAndRotation(fit.Pivot, fit.Rotation);
        tree.transform.localScale = Vector3.one * fit.Scale;
        var stats = new InstanceStats();
        FinishInstance(context.Render, tree, model.Prefab.name, stats);
        AddWalkCollider(tree.transform, crossing, fit);
        stats.ColliderCount++;
        MergeStats(context.Stats, stats);
        context.BridgeTrees.Add(tree.transform);

        Vector2 basePosition = ToXZ(fit.Pivot);
        context.Blockers.Add(new Blocker
        {
            From = basePosition,
            To = basePosition + crossing.Direction * fit.Length,
            Radius = BridgeHalfWidth + LandmarkClearance
        });

        float waterLevel = MapSouthEastShaper.WaterLevel;
        context.Notes.Add($"{label} : 중심 ({crossing.Center.x:F1}, {crossing.Center.y:F1}), 건너는 방향 {GetAzimuth(crossing.Direction):F0}도, " +
                          $"바닥 반지름 {fit.Length / 2f:F1}m, 물 폭 {crossing.WaterWidth:F1}m (기준 {BridgeMinWaterWidth} ~ {BridgeMaxWaterWidth}m), " +
                          $"{BridgeBankHeight}m 땅 사이 {crossing.BankWidth:F1}m, 걸침 길이 양쪽 {fit.Bearing:F1}m (최소 {BridgeMinBearing}m)\n" +
                          $"  {model.Prefab.name} 크기 {fit.Scale:F2}배 (허용 {BridgeScaleMin} ~ {BridgeScaleMax}배), 길이 {fit.Length:F1}m, 기울기 {fit.Pitch:+0.0;-0.0;0.0}도, 굴림 {model.Roll:F0}도, " +
                          $"줄기 밑면 수면 위 {fit.WaterClearMin:F2} ~ {fit.WaterClearMax:F2}m (기준 {BridgeMinWaterClearance} ~ {BridgeMaxWaterClearance}m), " +
                          $"걸침 끝 틈 밑동 쪽 {fit.GapBase:F2}m / 끝 쪽 {fit.GapTip:F2}m (기준 {BridgeMaxEndGap}m 이하, 음수는 묻힘), " +
                          $"걷는 면 수면 위 {fit.WalkTop - waterLevel:F2}m, 가지 가장 낮은 곳 (땅·수면 대비) {fit.BranchClearMin:+0.00;-0.00}m, " +
                          $"충돌체 추가 {stats.ColliderCount} (걷는 면 상자), Disallow 붙임 {stats.DisallowCount}");
    }

    // 줄기 방향과 기울기는 따르되 굴림은 빼서 윗면이 수평인 상자를 줄기 전체 길이로 붙인다. 윗면은 물 위 구간 줄기 윗면 평균 높이(WalkTop)를 지난다.
    // 나무 루트의 회전에는 굴림이 들어 있어 루트에 상자를 붙이면 상자도 굴러가므로 자식 오브젝트에 붙인다 (원본 prefab은 고치지 않는다)
    private static void AddWalkCollider(Transform tree, Crossing crossing, BridgeFit fit)
    {
        Transform walk = new GameObject(BridgeColliderName).transform;
        walk.SetParent(tree, false);
        Quaternion flat = CreateTrunkRotation(ToWorld(crossing.Direction, 0f), fit.Pitch, 0f);
        Vector3 axis = flat * Vector3.up;
        Vector3 up = flat * Vector3.forward;

        // 윗면 기준점 (물길 가운데, 걷는 면 높이)에서 상자 두께 절반만큼 내리고, 줄기 축을 따라 나무 길이 가운데로 옮긴다
        Vector3 topPoint = ToWorld(crossing.Center, fit.WalkTop) - up * (BridgeWalkThickness / 2f);
        Vector3 middle = fit.Pivot + axis * (fit.Length / 2f);
        walk.SetPositionAndRotation(topPoint + axis * Vector3.Dot(middle - topPoint, axis), flat);

        // 부모 크기(fit.Scale)를 물려받으므로 나눠서 월드 크기를 맞춘다
        var box = walk.gameObject.AddComponent<BoxCollider>();
        box.size = new Vector3(BridgeWalkWidth, fit.Length, BridgeWalkThickness) / fit.Scale;
    }

    // ---------- 절벽 조각 ----------

    // 꼭대기 둘레를 조각 수만큼의 같은 각도 구역으로 나눠 구역마다 하나씩 놓는다. 구역 안 비탈 후보 중 꼭대기 시야를 가리지 않고
    // 다른 조각과 CliffMinSpacing 이상인 자리에서 (1) 묻힘 (2) 바닥 아래 지면 높이 차가 가장 작은 곳을 고른다
    private static void PlaceCliffs(LandmarkContext context)
    {
        System.Random random = context.Random;
        int count = random.Next(CliffCountMin, CliffCountMax + 1);
        float startAngle = RandomRange(random, 0f, 360f);
        float sectorWidth = 360f / count;
        List<Vector2> candidates = FindCliffCandidates(context);
        context.CliffTarget = count;

        var placed = new List<Vector2>();
        for (int i = 0; i < count; i++)
        {
            string path = CliffPaths[random.Next(CliffPaths.Length)];
            float scale = RandomRange(random, CliffScaleMin, CliffScaleMax);
            float yaw = RandomRange(random, 0f, 360f);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + path + ".prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, context.RockParent);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
            instance.transform.localScale = Vector3.one * scale;

            Bounds bounds = GetRendererBounds(instance);
            Vector2[] points = GetFootprintPoints(bounds);
            (float bottom, float top) = GetMeshHeightRange(instance);
            Vector2? best = FindCliffSpot(context, candidates, placed, points, top - bottom, startAngle + i * sectorWidth, sectorWidth);
            if (best == null)
            {
                Object.DestroyImmediate(instance);
                continue;
            }

            instance.transform.position = ToWorld(best.Value, 0f);
            var stats = new InstanceStats();
            FinishInstance(context.Render, instance, prefab.name, stats);
            if (GroundInstance(context.Terrain, instance, CliffSinkRatio)) context.CliffBrokenCount++;
            MergeStats(context.Stats, stats);

            placed.Add(best.Value);
            context.Cliffs.Add(instance.transform);
            float radius = ToXZ(bounds.center).magnitude + Mathf.Max(bounds.extents.x, bounds.extents.z);
            context.Blockers.Add(new Blocker { From = best.Value, To = best.Value, Radius = radius + CliffClearance });
        }
    }

    // 언덕 위 CliffSearchStep 격자 중 꼭대기 HilltopClearRadius 밖, 경사 CliffMinSlope ~ MapTreeScatter.MaxSlope, 서식지 밖, C3 경계 안쪽인 곳
    private static List<Vector2> FindCliffCandidates(LandmarkContext context)
    {
        Rect southEast = GetSouthEastRect();
        float range = MapSouthEastShaper.HillRadius * HilltopSearchRangeRatio;
        Vector2 center = MapSouthEastShaper.HillCenter;
        var candidates = new List<Vector2>();
        for (float z = Mathf.Max(southEast.yMin, center.y - range); z <= Mathf.Min(southEast.yMax, center.y + range); z += CliffSearchStep)
        {
            for (float x = Mathf.Max(southEast.xMin, center.x - range); x <= Mathf.Min(southEast.xMax, center.x + range); x += CliffSearchStep)
            {
                var position = new Vector2(x, z);
                if (!IsOnHill(position) || IsInHabitat(position) || !IsInsideSouthEast(position, LandmarkEdgeMargin)) continue;
                if (Vector2.Distance(position, context.Hilltop) < HilltopClearRadius) continue;

                float slope = GetSlope(context.Terrain, position);
                if (slope >= CliffMinSlope && slope <= MapTreeScatter.MaxSlope) candidates.Add(position);
            }
        }
        return candidates;
    }

    // 꼭대기에서 본 방위가 [sectorStart, sectorStart + sectorWidth)인 후보 중 가장 잘 맞는 자리.
    // 땅에 맞춘 뒤 조각 윗면이 꼭대기 지면보다 높으면 꼭대기에서 내려다보는 시야를 가리므로 버린다
    private static Vector2? FindCliffSpot(LandmarkContext context, List<Vector2> candidates, List<Vector2> placed, Vector2[] points, float height,
        float sectorStart, float sectorWidth)
    {
        Vector2? best = null;
        bool isBestBuried = true;
        float bestRange = float.MaxValue;
        foreach (Vector2 candidate in candidates)
        {
            if (Mathf.Repeat(GetAzimuth(candidate - context.Hilltop) - sectorStart, 360f) >= sectorWidth) continue;
            if (placed.Any(other => Vector2.Distance(other, candidate) < CliffMinSpacing)) continue;

            float[] grounds = points.Select(point => GetGroundHeight(context.Terrain, point + candidate)).ToArray();
            float lowest = grounds.Min();
            float highest = grounds.Max();
            if (lowest < PlaceMinHeight) continue;
            if (lowest + height * (1f - CliffSinkRatio) > context.HilltopHeight) continue;

            bool isBuried = IsBuried(lowest, highest, height, CliffSinkRatio);
            float range = highest - lowest;
            bool isBetter = best == null || (isBuried != isBestBuried ? !isBuried : range < bestRange);
            if (!isBetter) continue;
            best = candidate;
            isBestBuried = isBuried;
            bestRange = range;
        }
        return best;
    }

    // ---------- 나무, 바위, 물가 소품 ----------

    private static ScatterContext ScatterAll(LandmarkContext landmarks, Transform treeParent, Transform rockParent)
    {
        Terrain terrain = landmarks.Terrain;
        var context = new ScatterContext
        {
            Terrain = terrain,
            Water = landmarks.Water,
            Random = new System.Random(ScatterSeed),
            PropRandom = new System.Random(PropSeed),
            Render = landmarks.Render,
            Blockers = landmarks.Blockers,
            Thresholds = CalculateZoneThresholds(terrain),
            TreeGroups = new[]
            {
                CreateGroup(MapTreeScatter.MainTreePaths, treeParent, MapTreeScatter.TreeScaleMin, MapTreeScatter.TreeScaleMax),
                CreateGroup(MapTreeScatter.DeadTreePaths, treeParent, MapTreeScatter.TreeScaleMin, MapTreeScatter.TreeScaleMax),
                CreateGroup(MapTreeScatter.GiantTreePaths, treeParent, MapTreeScatter.TreeScaleMin, MapTreeScatter.TreeScaleMax)
            },
            RockGroup = CreateGroup(RockPaths, rockParent, MapTreeScatter.RockScaleMin, MapTreeScatter.RockScaleMax),
            PropGroup = CreateGroup(ShorePropPaths, rockParent, MapTreeScatter.RockScaleMin, MapTreeScatter.RockScaleMax)
        };

        Rect southEast = GetSouthEastRect();
        int columns = Mathf.RoundToInt(southEast.width / ScatterCellSize);
        int rows = Mathf.RoundToInt(southEast.height / ScatterCellSize);
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                var cellMin = new Vector2(southEast.xMin + x * ScatterCellSize, southEast.yMin + z * ScatterCellSize);
                Vector2 cellCenter = cellMin + Vector2.one * (ScatterCellSize / 2f);
                // 칸 중심이 서식지 안이거나 29.3m 미만이면 칸 전체를 비운다 (밀도를 놓을 수 있는 칸 넓이로 대조하기 위함)
                if (IsInHabitat(cellCenter))
                {
                    context.HabitatCellCount++;
                    continue;
                }
                float ground = GetGroundHeight(terrain, cellCenter);
                if (ground < PlaceMinHeight)
                {
                    context.LowCellCount++;
                    if (ground < MapSouthEastShaper.WaterLevel) context.WaterCellCount++;
                    continue;
                }

                int zone = (int)GetSouthEastZone(cellCenter, context.Thresholds);
                int terrainKind = IsOnHill(cellCenter) ? HillTerrain : MarshTerrain;
                context.ZoneCellCounts[zone]++;
                context.TerrainCellCounts[terrainKind]++;
                ScatterTrees(context, cellMin, zone);
                ScatterRocks(context, cellMin, terrainKind);
            }
        }
        return context;
    }

    private static InstanceGroup CreateGroup(string[] paths, Transform parent, float scaleMin, float scaleMax)
    {
        GameObject[] prefabs = paths
            .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + path + ".prefab"))
            .ToArray();
        return new InstanceGroup { Prefabs = prefabs, Parent = parent, ScaleMin = scaleMin, ScaleMax = scaleMax };
    }

    private static void ScatterTrees(ScatterContext context, Vector2 cellMin, int zone)
    {
        int count = RollCount(context.Random, TreeDensityMin[zone], TreeDensityMax[zone]);
        for (int i = 0; i < count; i++)
        {
            TreeKind kind = PickTreeKind(context.Random, zone);
            if (!TryAddTree(context, cellMin, context.TreeGroups[(int)kind])) continue;

            context.TreeKindCounts[(int)kind]++;
            context.TreeCounts[zone]++;
        }
    }

    private static void ScatterRocks(ScatterContext context, Vector2 cellMin, int terrainKind)
    {
        int count = RollCount(context.Random, RockDensityMin[terrainKind], RockDensityMax[terrainKind]);
        for (int i = 0; i < count; i++)
        {
            if (TryAddGroundItem(context, context.Random, cellMin, ScatterCellSize, context.RockGroup)) context.RockCounts[terrainKind]++;
        }
    }

    // 물가 띠(물 지도 칸 중 지면 29.3m 이상, 물에서 ShoreDistance 안, 서식지 밖) 넓이 × 목표 밀도만큼, 띠 칸을 무작위로 뽑아 놓는다
    private static void ScatterShoreProps(ScatterContext context)
    {
        WaterMap map = context.Water;
        var cells = new List<Vector2>();
        for (int z = 0; z < map.Rows; z++)
        {
            for (int x = 0; x < map.Columns; x++)
            {
                if (map.Heights[z, x] < PlaceMinHeight || map.WaterDistance[z, x] > ShoreDistance) continue;
                if (IsInHabitat(map.GetCellCenter(x, z))) continue;
                cells.Add(map.Origin + new Vector2(x, z) * map.Spacing);
            }
        }

        context.ShoreArea = cells.Count * map.Spacing * map.Spacing / HectareArea;
        context.ShorePropTarget = Mathf.RoundToInt(context.ShoreArea * ShorePropTargetDensity);
        for (int draw = 0; draw < context.ShorePropTarget * ShorePropMaxDrawsPerPiece && context.ShorePropCount < context.ShorePropTarget; draw++)
        {
            Vector2 cellMin = cells[context.PropRandom.Next(cells.Count)];
            if (TryAddGroundItem(context, context.PropRandom, cellMin, map.Spacing, context.PropGroup)) context.ShorePropCount++;
            else context.ShorePropFailedDraws++;
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

    // 고사목은 모든 구분, 거대목은 작은 무리에서만 나온다
    private static TreeKind PickTreeKind(System.Random random, int zone)
    {
        double roll = random.NextDouble();
        if (zone == (int)MapTreeScatter.FieldZone.Forest)
        {
            if (roll < MapTreeScatter.GiantTreeRatio) return TreeKind.Giant;
            roll -= MapTreeScatter.GiantTreeRatio;
        }
        return roll < DeadTreeRatio ? TreeKind.Dead : TreeKind.Main;
    }

    // 서식지 반경, 랜드마크·절벽 조각 둘레, 급경사에는 놓지 않는다. 건너뛴 이유를 센다
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
        if (GetSlope(context.Terrain, position) > MapTreeScatter.MaxSlope)
        {
            context.SlopeSkipCount++;
            return false;
        }
        return true;
    }

    // 무작위 값은 조건과 상관없이 같은 순서로 뽑아 결과가 항상 같게 한다
    private static bool TryAddTree(ScatterContext context, Vector2 cellMin, InstanceGroup group)
    {
        System.Random random = context.Random;
        var position = new Vector2(
            cellMin.x + RandomRange(random, 0f, ScatterCellSize),
            cellMin.y + RandomRange(random, 0f, ScatterCellSize));
        GameObject prefab = group.Prefabs[random.Next(group.Prefabs.Length)];
        float scale = RandomRange(random, group.ScaleMin, group.ScaleMax);
        float yaw = RandomRange(random, 0f, 360f);
        if (!CanScatter(context, position)) return false;
        if (!IsDryRing(context.Terrain, position, TreeFootprintRadius))
        {
            context.LowGroundSkipCount++;
            return false;
        }

        // prefab 연결을 유지한 채 놓는다. Static 플래그는 켜지 않는다 (GPU Resident Drawer로 그림)
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.Parent);
        instance.transform.SetPositionAndRotation(ToWorld(position, GetGroundHeight(context.Terrain, position)), Quaternion.Euler(0f, yaw, 0f));
        instance.transform.localScale = Vector3.one * scale;
        if (MapTreeScatter.AddMissingCollider(instance, prefab.name)) context.ColliderCount++;
        return true;
    }

    // 바위, 물가 소품 : 놓아 본 뒤 렌더러 범위 바닥 표본점이 모두 29.3m 이상인지 보고, 피벗을 그중 가장 낮은 땅에 맞춰 비탈 아래쪽이 뜨지 않게 한다.
    // 무작위 값은 조건과 상관없이 같은 순서로 뽑는다
    private static bool TryAddGroundItem(ScatterContext context, System.Random random, Vector2 areaMin, float areaSize, InstanceGroup group)
    {
        var position = new Vector2(
            areaMin.x + RandomRange(random, 0f, areaSize),
            areaMin.y + RandomRange(random, 0f, areaSize));
        GameObject prefab = group.Prefabs[random.Next(group.Prefabs.Length)];
        float scale = RandomRange(random, group.ScaleMin, group.ScaleMax);
        float yaw = RandomRange(random, 0f, 360f);
        if (!CanScatter(context, position)) return false;

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.Parent);
        instance.transform.SetPositionAndRotation(ToWorld(position, GetGroundHeight(context.Terrain, position)), Quaternion.Euler(0f, yaw, 0f));
        instance.transform.localScale = Vector3.one * scale;
        float lowest = GetFootprintPoints(GetRendererBounds(instance)).Append(position).Min(point => GetGroundHeight(context.Terrain, point));
        if (lowest < PlaceMinHeight)
        {
            Object.DestroyImmediate(instance);
            context.LowGroundSkipCount++;
            return false;
        }

        instance.transform.position = ToWorld(position, lowest);
        if (MapTreeScatter.AddMissingCollider(instance, prefab.name)) context.ColliderCount++;
        return true;
    }

    // 가운데와 동서남북 radius 4곳이 모두 29.3m 이상인지
    private static bool IsDryRing(Terrain terrain, Vector2 center, float radius)
    {
        return GetRingPoints(center, radius).Append(center).All(point => GetGroundHeight(terrain, point) >= PlaceMinHeight);
    }

    private static bool IsBlocked(List<Blocker> blockers, Vector2 position)
    {
        return blockers.Any(blocker => GetSegmentDistance(position, blocker.From, blocker.To) < blocker.Radius);
    }

    private static float GetSegmentDistance(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 segment = to - from;
        float lengthSquared = segment.sqrMagnitude;
        float t = lengthSquared > 0f ? Mathf.Clamp01(Vector2.Dot(point - from, segment) / lengthSquared) : 0f;
        return Vector2.Distance(point, from + segment * t);
    }

    // ---------- 높이 맞춤, 렌더링 ----------

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

            // 절벽 조각(CliffAuto 셰이더)이 여기에 걸린다
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
        into.BrokenMaterialCount += from.BrokenMaterialCount;
    }

    // ---------- 지형, 계산 ----------

    private static Rect GetSouthEastRect()
    {
        return MapGrid.GetRangeBounds(MapSouthEastShaper.SouthEastCell, MapSouthEastShaper.SouthEastCell);
    }

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

    // C3 경계에서 inset 이상 안쪽인지
    private static bool IsInsideSouthEast(Vector2 position, float inset)
    {
        Rect southEast = GetSouthEastRect();
        return position.x >= southEast.xMin + inset && position.x <= southEast.xMax - inset
            && position.y >= southEast.yMin + inset && position.y <= southEast.yMax - inset;
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
    // 아래 로그는 검수(비율, 밀도가 승인 값 안인지, 물·물가, 서식지 비움, 랜드마크 위치와 크기, 다리 높이, 뜸·묻힘, C3 밖, 성능 측정용 배치 수)에 쓰는 값이라 남긴다

    private static void LogZones(ScatterContext context)
    {
        float cellArea = ScatterCellSize * ScatterCellSize / HectareArea;
        float placeableCells = context.ZoneCellCounts.Sum();
        var lines = new List<string>();
        for (int zone = 0; zone < ZoneLabels.Length; zone++)
        {
            float area = context.ZoneCellCounts[zone] * cellArea;
            lines.Add($"{ZoneLabels[zone]} 비율 {context.ZoneCellCounts[zone] / placeableCells:P1}, 넓이 {area:F1}ha : " +
                      $"나무 {context.TreeCounts[zone]}그루 {FormatDensity(context.TreeCounts[zone], area, TreeDensityMin[zone], TreeDensityMax[zone])}");
        }
        for (int terrainKind = 0; terrainKind < TerrainLabels.Length; terrainKind++)
        {
            float area = context.TerrainCellCounts[terrainKind] * cellArea;
            lines.Add($"{TerrainLabels[terrainKind]} 넓이 {area:F1}ha : 바위 {context.RockCounts[terrainKind]}개 " +
                      FormatDensity(context.RockCounts[terrainKind], area, RockDensityMin[terrainKind], RockDensityMax[terrainKind]));
        }
        lines.Add($"물가 띠 (29.3m 이상, 물에서 {ShoreDistance}m 안) {context.ShoreArea:F1}ha : 그루터기·속 빈 통나무 {context.ShorePropCount}/{context.ShorePropTarget}개 " +
                  $"{FormatDensity(context.ShorePropCount, context.ShoreArea, ShorePropDensityMin, ShorePropDensityMax)} (띠 넓이 기준, 못 놓은 뽑기 {context.ShorePropFailedDraws})");
        lines.Add($"비운 칸 : 서식지 {context.HabitatCellCount * cellArea:F1}ha, 중심 {PlaceMinHeight}m 미만 {context.LowCellCount * cellArea:F1}ha " +
                  $"(그중 물 {MapSouthEastShaper.WaterLevel}m 미만 {context.WaterCellCount * cellArea:F1}ha)");

        int[] kinds = context.TreeKindCounts;
        int treeTotal = kinds.Sum();
        Debug.Log($"[MapSouthEastScatter] 남동쪽(C3) 구분별 넓이와 밀도 (승인 비율 풀습지 {MarshAreaRatio:P0} / 드문드문 {1f - MarshAreaRatio - GroveAreaRatio:P0} / " +
                  $"작은 무리 {GroveAreaRatio:P0}, 놓을 수 있는 땅 기준, 노이즈 주기 {ZoneNoisePeriod}m) :\n" + string.Join("\n", lines));
        Debug.Log($"[MapSouthEastScatter] 나무 {treeTotal}그루 (주력 {kinds[(int)TreeKind.Main]}, 고사목 {kinds[(int)TreeKind.Dead]} " +
                  $"({kinds[(int)TreeKind.Dead] / Mathf.Max(1f, treeTotal):P1}, 목표 {DeadTreeRatio:P0}), 거대목 {kinds[(int)TreeKind.Giant]}), " +
                  $"바위 {context.RockCounts.Sum()}개, 물가 소품 {context.ShorePropCount}개, 충돌체 추가 {context.ColliderCount} / " +
                  $"건너뜀 서식지 {context.HabitatSkipCount}, 랜드마크·절벽 둘레 {context.LandmarkSkipCount}, 경사 {context.SlopeSkipCount}, " +
                  $"바닥 둘레 {PlaceMinHeight}m 미만 {context.LowGroundSkipCount}");
    }

    private static string FormatDensity(int count, float area, float min, float max)
    {
        float density = area > 0f ? count / area : 0f;
        bool isInRange = density >= min && density <= max;
        return $"(1ha당 {density:F1}, 표 {min:F0} ~ {max:F0} {(isInRange ? "안" : "밖")})";
    }

    private static void LogLandmarks(LandmarkContext context)
    {
        float minSpacing = float.MaxValue;
        for (int i = 0; i < context.Centers.Count; i++)
        {
            for (int j = i + 1; j < context.Centers.Count; j++)
                minSpacing = Mathf.Min(minSpacing, Vector2.Distance(context.Centers[i], context.Centers[j]));
        }
        string spacingText = context.Centers.Count > 1 ? $"{minSpacing:F1}m" : "-";
        Debug.Log($"[MapSouthEastScatter] 랜드마크 {context.Centers.Count}/{1 + BridgeCount}곳 (야영지 1, 다리 {context.BridgeTrees.Count}/{BridgeCount}), " +
                  $"중심 사이 최소 거리 {spacingText} (기준 {LandmarkMinSpacing}m 이상, 다리끼리 {BridgeMinSpacing}m 이상), " +
                  $"물길 후보 {context.CrossingCount}곳, 맞춤 실패 : {DescribeBridgeFailures(context.BridgeFailures)} :\n" + string.Join("\n", context.Notes) +
                  $"\n물길 후보 (땅 사이가 짧은 순) :\n" + string.Join("\n", context.CrossingNotes));

        string cliffs = string.Join(", ", context.Cliffs.Select(cliff =>
        {
            Vector2 position = ToXZ(cliff.position);
            float top = GetMeshHeightRange(cliff.gameObject).Top;
            return $"{cliff.name} ({position.x:F0}, {position.y:F0}) 꼭대기에서 {Vector2.Distance(position, context.Hilltop):F0}m, " +
                   $"경사 {GetSlope(context.Terrain, position):F0}도, 윗면 꼭대기 지면 대비 {top - context.HilltopHeight:+0.0;-0.0}m";
        }));
        Debug.Log($"[MapSouthEastScatter] 언덕 꼭대기 ({context.Hilltop.x:F1}, {context.Hilltop.y:F1}) {context.HilltopHeight:F1}m. " +
                  $"절벽 조각 {context.Cliffs.Count}/{context.CliffTarget}개 (허용 {CliffCountMin} ~ {CliffCountMax}, 꼭대기 {HilltopClearRadius}m 밖, " +
                  $"뜨거나 묻힘 {context.CliffBrokenCount}) : {cliffs}");
    }

    private static string DescribeBridgeFailures(int[] counts)
    {
        IEnumerable<string> parts = Enumerable.Range(1, counts.Length - 1)
            .Where(reason => counts[reason] > 0)
            .Select(reason => $"{BridgeFailureLabels[reason]} {counts[reason]}");
        string text = string.Join(", ", parts);
        return text.Length > 0 ? text : "없음";
    }

    private static string DescribePrefabs(Transform group)
    {
        return string.Join(", ", group.Cast<Transform>()
            .GroupBy(piece => piece.name)
            .OrderByDescending(names => names.Count())
            .Select(names => $"{names.Key} × {names.Count()}"));
    }

    // 놓인 오브젝트를 다시 읽어 29.3m 미만 땅 위, 서식지 반경 안, C3 밖 개수(모두 0이어야 함)와 뜸, 묻힘, 분홍, WallRuin, Disallow 수를 센다.
    // 물가에 걸친 배와 다리는 예외로 따로 센다
    private static void LogPlacementCheck(Terrain terrain, ScatterContext scatter, LandmarkContext landmarks)
    {
        List<Transform> trees = scatter.TreeGroups[0].Parent.Cast<Transform>().ToList();
        List<Transform> rocks = scatter.RockGroup.Parent.Cast<Transform>().ToList();
        List<Transform> groups = landmarks.Parent.Cast<Transform>().ToList();
        List<Transform> all = trees.Concat(rocks).Concat(landmarks.CampPieces).Concat(landmarks.BridgeTrees).ToList();

        // 나무는 피벗 둘레, 그 밖은 렌더러 범위 바닥 표본점 중 29.3m 미만이 있는 수
        int lowTreeCount = trees.Count(tree => !IsDryRing(terrain, ToXZ(tree.position), TreeFootprintRadius));
        int lowOtherCount = rocks.Concat(landmarks.CampPieces)
            .Where(item => item != landmarks.ShoreBoat)
            .Count(item => GetFootprintPoints(GetRendererBounds(item.gameObject)).Any(point => GetGroundHeight(terrain, point) < PlaceMinHeight));
        int shoreBoatWetCount = landmarks.ShoreBoat == null
            ? 0
            : GetFootprintPoints(GetRendererBounds(landmarks.ShoreBoat.gameObject)).Count(point => GetGroundHeight(terrain, point) < MapSouthEastShaper.WaterLevel);

        int inHabitatCount = all.Count(item => IsInHabitat(ToXZ(item.position)));
        Rect southEast = GetSouthEastRect();
        int outsideCount = all.Concat(groups).Select(item => ToXZ(item.position)).Append(MapSouthEastShaper.HabitatCenter)
            .Count(position => !southEast.Contains(position));
        int wallRuinCount = all.Count(item => item.name == WallRuinName);

        int floatingCount = 0;
        int buriedCount = 0;
        foreach (Transform item in trees.Concat(rocks))
        {
            if (landmarks.Cliffs.Contains(item)) continue;

            float gap = item.position.y - GetGroundHeight(terrain, ToXZ(item.position));
            if (gap > HeightTolerance) floatingCount++;
            if (gap < -MaxBuryGap) buriedCount++;
        }

        Transform[] parents = { scatter.TreeGroups[0].Parent, scatter.RockGroup.Parent, landmarks.Parent };
        int pinkCount = parents.SelectMany(parent => parent.GetComponentsInChildren<MeshRenderer>(true))
            .Count(renderer => renderer.sharedMaterials.Any(IsBrokenMaterial));
        int disallowCount = parents.Sum(parent => parent.GetComponentsInChildren(landmarks.Render.DisallowType, true).Length);
        int hilltopTreeCount = trees.Count(tree => Vector2.Distance(ToXZ(tree.position), landmarks.Hilltop) < HilltopClearRadius);

        Vector2 habitatCenter = MapSouthEastShaper.HabitatCenter;
        float azimuth = GetAzimuth(habitatCenter);
        string direction = DirectionLabels[Mathf.RoundToInt(azimuth / 45f) % DirectionLabels.Length];
        bool isSouthEast = azimuth >= SouthEastAzimuthMin && azimuth <= SouthEastAzimuthMax;
        Debug.Log($"[MapSouthEastScatter] 서식지 ({habitatCenter.x:F0}, {GetGroundHeight(terrain, habitatCenter):F1}, {habitatCenter.y:F0}), " +
                  $"거점 기준 방위각 {azimuth:F1}도 ({direction}, {SouthEastAzimuthMin} ~ {SouthEastAzimuthMax}도 {(isSouthEast ? "안" : "밖")}), " +
                  $"반경 {MapSouthEastShaper.HabitatRadius}m 안 오브젝트 {inHabitatCount}개 (0이어야 한다)");
        Debug.Log($"[MapSouthEastScatter] {PlaceMinHeight}m 미만 땅에 걸친 오브젝트 : 나무 {lowTreeCount}, 바위·소품·절벽·야영지 조각 {lowOtherCount} (모두 0이어야 한다) / " +
                  $"예외 다리 {landmarks.BridgeTrees.Count}개, 물가에 걸친 배 {(landmarks.ShoreBoat != null ? 1 : 0)}개 (바닥 표본점 중 물 위 {shoreBoatWetCount}곳)");
        Debug.Log($"[MapSouthEastScatter] 뜨거나 묻힌 오브젝트 : 나무·바위·소품 뜸 {floatingCount}, 묻힘 {buriedCount} / 야영지 조각 {landmarks.CampBrokenCount} / " +
                  $"절벽 조각 {landmarks.CliffBrokenCount}. 분홍 렌더러 {pinkCount}, {WallRuinName} 사용 {wallRuinCount}개, C3 밖 {outsideCount}개 (모두 0이어야 한다). " +
                  $"Disallow 붙은 렌더러 {disallowCount}개 (랜드마크·절벽 붙임 {landmarks.Stats.DisallowCount}), " +
                  $"충돌체 추가 나무·바위·소품 {scatter.ColliderCount} / 랜드마크·절벽 {landmarks.Stats.ColliderCount}. " +
                  $"언덕 꼭대기 {HilltopClearRadius}m 안 나무 {hilltopTreeCount}그루 (시야 확인용)");

        // 묶음 GameObject, 다리 걷는 면 충돌체, 서식지 표시(1)도 씬 GameObject라 함께 센다
        int total = all.Count + groups.Count + landmarks.BridgeTrees.Count + 1;
        Debug.Log($"[MapSouthEastScatter] SouthEast 배치 GameObject 총 {total}개 (나무 {trees.Count}, 바위·물가 소품·절벽 {rocks.Count}, " +
                  $"야영지 조각 {landmarks.CampPieces.Count}, 다리 나무 {landmarks.BridgeTrees.Count}, 묶음 {groups.Count}, 걷는 면 {landmarks.BridgeTrees.Count}, 서식지 표시 1). " +
                  "SouthEast/Ponds는 세지 않았다");
    }
}
