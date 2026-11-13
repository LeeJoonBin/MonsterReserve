using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 메뉴 15 : 남서쪽 구역(A3, 초원)에 서식지 위치 표시, 랜드마크 2곳, 나무, 바위·소품을 놓는다 (이슈 #9).
// 1) 서식지 : SouthWest/Habitat를 HabitatCenter의 지면 높이에 둔다. 위치 표시만 하고, 반경 HabitatRadius 안은 풀밭으로 비운다.
// 2) 랜드마크 : 언덕 꼭대기 룬 돌 무리, 바위 노두를 Dreamscape prefab으로 직접 구성한다 (서쪽 메뉴 12와 같은 조립, 높이 맞춤 방식).
//    룬 돌 무리는 언덕 위에서 가장 높은 지면 지점 둘레에서 묻히는 돌이 없는 자리와 방향을 고르고, 바위 노두는 아래 "랜드마크 위치 규칙"으로 자리를 찾는다.
// 3) 나무, 바위 : 새 노이즈로 풀밭 / 드문드문 / 나무 무리를 70 / 20 / 10%로 나누고, 나무 무리가 작은 숲 조각으로 뭉치게 한다.
//    바위는 언덕 위와 평지의 밀도를 따로 둔다 (평지의 풀밭, 드문드문에 돌을 더 둔다).
//    서식지 반경과 랜드마크 둘레는 비운다. A3에는 강, 등산로, 물이 없어 물·길 피하기는 하지 않는다.
// 다시 실행하면 SouthWest 아래 Trees, Rocks, Landmarks, Habitat의 자식을 지우고 같은 seed로 다시 만든 뒤 씬을 저장한다.
// 실행 순서 : ... → 14 → 15 → 16 → 10. 14(남서쪽 지형)를 다시 실행하면 지형 높이가 바뀌므로 15도 다시 실행하고,
// 15를 다시 실행하면 16도 다시 실행한다.
public static class MapSouthWestScatter
{
    private enum TreeKind
    {
        Main = 0,
        Dead = 1,
        Giant = 2
    }

    // 먼저 놓은 랜드마크가 차지한 자리 (월드 XZ)
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

    // 한 번 실행하는 동안 함께 쓰는 값과 결과 개수 묶음. 개수 배열의 순서는 FieldZone (풀밭, 드문드문, 나무 무리)
    private class ScatterContext
    {
        public Terrain Terrain;
        public System.Random Random;
        public InstanceGroup[] TreeGroups;
        public InstanceGroup RockGroup;
        public List<Blocker> Landmarks;
        public int[] ZoneCellCounts = new int[3];
        public int[] TreeCounts = new int[3];
        public int[] RockCounts = new int[3];
        public int[] TreeKindCounts = new int[3];
        // [지대][FieldZone]. 지대는 FlatTerrain, HillTerrain
        public int[][] TerrainCellCounts = { new int[3], new int[3] };
        public int[][] TerrainRockCounts = { new int[3], new int[3] };
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
        // 묶음 중심 기준 XZ (m)
        public Vector2 Offset;
        public float Yaw;
        public float Scale;
        // true면 고정 seed로 조금 기울이고 크기를 흔든다 (룬 돌)
        public bool IsWobbly;
    }

    private class LandmarkSpec
    {
        public string Label;
        // 배치도 전체를 돌리는 각도 (도). 언덕 꼭대기 묶음은 자리 맞춤에서 다시 고른다
        public float GroupYaw;
        // 조각 밑면을 바닥 둘레의 가장 낮은 땅보다 (조각 높이 × 이 값)만큼 아래에 맞춘다
        public float SinkRatio;
        // 기준 바닥 반지름. 로그에서 실제 바닥 반지름과 견주는 용도
        public float ApproxRadius;
        // true면 자리를 찾지 않고 언덕 꼭대기에 둔다
        public bool IsHilltop;
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

    // 원점에서 조립한 랜드마크 하나. 자리를 찾은 뒤 통째로 옮긴다
    private class LandmarkBuild
    {
        public Transform Group;
        public List<PlacedPiece> Pieces = new List<PlacedPiece>();
        public int InstanceCount;
        // 중심에서 가장 먼 렌더러 범위 가장자리까지의 수평 거리
        public float Radius;
        public int ColliderCount;
        public int DisallowCount;
        // 분홍(셰이더 오류, 머티리얼 없음) 렌더러 수 + 렌더러가 없는 조각 수
        public int BrokenMaterialCount;
    }

    private struct LandmarkFit
    {
        public Vector2 Position;
        // 땅에 맞추면 많이 묻히는 조각 수
        public int BrokenCount;
        // 묶음 바닥 아래 지면의 가장 높은 곳 - 가장 낮은 곳
        public float GroundRange;
        // 룬 돌 무리처럼 회전도 함께 고른 경우의 묶음 회전 (도)
        public float GroupYaw;
    }

    private class LandmarkContext
    {
        public Terrain Terrain;
        public System.Type DisallowType;
        public Transform Parent;
        public System.Random Random;
        public Vector2 Hilltop;
        public List<Blocker> Placed = new List<Blocker>();
        public Dictionary<Shader, bool> GpuDrivenSupport = new Dictionary<Shader, bool>();
        public int BrokenCount;
        public List<string> Notes = new List<string>();
    }

    private const string SouthWestCell = "A3";
    private const string SouthWestRegionName = "SouthWest";
    private const string TreeParentName = "Trees";
    private const string RockParentName = "Rocks";
    private const string LandmarkParentName = "Landmarks";
    private const string HabitatName = "Habitat";

    // 서식지 (사용자 승인 임시값). 거점(원점) 기준 남서쪽. 반경 안은 풀밭으로 두고 나무, 바위, 랜드마크를 놓지 않는다
    public static readonly Vector2 HabitatCenter = new Vector2(-750f, -750f);
    public const float HabitatRadius = 80f;

    // 언덕 꼭대기를 찾을 때 지면 높이를 재는 간격 (m). 언덕 범위는 MapSouthWestShaper의 언덕 판정을 따른다
    private const float HilltopSearchStep = 2f;
    // 비틀린 언덕 윤곽이 언덕 중심에서 HillRadius보다 멀리 나갈 수 있어, 꼭대기를 찾는 사각형은 이 배율만큼 넓게 잡고 언덕 판정으로 거른다
    private const float HilltopSearchRangeRatio = 2f;

    // 룬 돌 무리 자리 맞춤 : 꼭대기에서 이 반지름 안의 자리와 묶음 회전 각도를 돌며 (1) 묻히는 조각 (2) 바닥 아래 지면 높이 차가 가장 작은 것을 고른다.
    // 꼭대기가 좁은 능선이면 정해진 방향, 정확한 꼭대기 한 점에서는 비탈에 걸친 룬 돌이 묻혀서다
    private const float HilltopFitRadius = 10f;
    private const float HilltopFitStep = 1f;
    private const float HilltopYawStep = 15f;

    // 앞 메뉴들의 seed(20261109 ~ 20261130)와 겹치지 않게 둔다
    private const int ZoneSeed = 20261131;
    private const int ScatterSeed = 20261132;
    private const int LandmarkSeed = 20261133;

    // 구분 마스크 (사용자 승인 비율). 노이즈 하위 70% = 풀밭, 다음 20% = 드문드문, 상위 10% = 나무 무리.
    // 주기를 짧게 잡아 상위 10%가 큰 숲이 아니라 여러 개의 작은 숲 조각으로 흩어지게 한다
    private const float ZoneNoisePeriod = 120f;
    private const float OpenAreaRatio = 0.70f;
    private const float ForestAreaRatio = 0.10f;
    // 분위수를 구할 때 A3를 표본 조사하는 간격 (m)
    private const float ZoneSampleSpacing = 5f;
    // Perlin 입력이 음수가 되지 않도록 노이즈 위치를 양수 쪽으로 옮기는 범위
    private const float NoiseOffsetMin = 100f;
    private const float NoiseOffsetMax = 1000f;
    private static readonly Vector2 ZoneNoiseOffset = CreateZoneNoiseOffset();

    // 흩뿌리기 단위 칸 (10m × 10m)과 1ha 넓이 (MapTreeScatter와 같은 단위)
    private const float ScatterCellSize = 10f;
    private const float HectareArea = 10000f;

    // 밀도 표 (1ha당 최소, 최대, 사용자 승인 임시값). 순서는 FieldZone (풀밭, 드문드문, 나무 무리)
    private static readonly float[] TreeDensityMin = { 0f, 2f, 60f };
    private static readonly float[] TreeDensityMax = { 0f, 5f, 100f };
    // 바위는 [지대][FieldZone]. 지대 0 = 평지, 1 = 언덕 위 (MapSouthWestShaper 언덕 가중치 > 0).
    // 언덕을 빼고 평지의 풀밭, 드문드문에만 돌을 늘린다 (사용자 결정). 나무 무리는 두 지대 모두 같다
    private const int FlatTerrain = 0;
    private const int HillTerrain = 1;
    private static readonly float[][] RockDensityMin = { new[] { 3f, 3f, 5f }, new[] { 1f, 1f, 5f } };
    private static readonly float[][] RockDensityMax = { new[] { 5f, 5f, 10f }, new[] { 2f, 2f, 10f } };
    private static readonly string[] TerrainLabels = { "평지", "언덕 위" };
    private static readonly string[] ZoneLabels = { "풀밭", "드문드문", "나무 무리" };

    // 바위 피벗을 이 반지름(× 배율) 안의 가장 낮은 땅에 맞춘다 (서쪽 배치와 같은 값)
    private const float RockFootprintRadius = 1f;

    // ---------- 랜드마크 위치 규칙 (바위 노두, 서쪽 메뉴 12와 같은 규칙) ----------
    // A3 안을 LandmarkSearchStep 간격 격자로 돌며 아래 조건을 모두 맞추는 자리 중,
    // (1) 땅에 맞추면 많이 묻히는 조각이 가장 적고 (2) 바닥 아래 지면 높이 차가 가장 작은 곳에 놓는다. 같으면 남서쪽에서 먼저 만난 자리.
    // - 묶음 바닥 전체가 A3 경계에서 LandmarkEdgeMargin 이상 안쪽
    // - 묶음 바닥 전체가 서식지 반경 밖
    // - 먼저 놓은 랜드마크(룬 돌 무리)와 중심 거리 LandmarkMinSpacing 이상
    // - 중심 경사 LandmarkMaxSlope 이하
    private const float LandmarkEdgeMargin = 50f;
    private const float LandmarkMinSpacing = 150f;
    private const float LandmarkMaxSlope = 25f;
    private const float LandmarkSearchStep = 10f;
    // 랜드마크 바닥 둘레에 나무, 바위를 두지 않아 멀리서도 보이는 빈터가 되게 한다
    private const float LandmarkClearance = 8f;

    // ---------- 랜드마크 배치도 (임시값) ----------
    // 좌표는 묶음 중심 기준 (x = 동, z = 북, 묶음 회전 전). 조각 크기는 prefab 원래 크기 기준 배율이다.
    private const float PieceTiltMax = 4f;
    private const float PieceScaleJitter = 0.1f;
    // 조각 밑면에서 바닥 둘레의 가장 높은 땅까지가 조각 높이 × 이 값을 넘으면 묻힌 것으로 센다
    private const float PieceMaxBuryRatio = 0.5f;
    // 묻는 깊이 비율. 룬 돌은 서쪽 선돌, 바위 노두는 서쪽 큰 바위 무리와 같은 값
    private const float StructureSinkRatio = 0.05f;
    private const float BigRockSinkRatio = 0.25f;

    // 언덕 꼭대기 룬 돌 무리 : 꼭대기 둘레 반지름 약 8m 안에 룬 돌 4개를 거리와 방향을 고르지 않게 흩어 둔다
    private static readonly PieceSpec[] HilltopStonePieces =
    {
        new PieceSpec { Path = "StandingStones/Prefab_RockFormation_02", Offset = new Vector2(0.5f, 7.5f), Yaw = 190f, Scale = 1f, IsWobbly = true },
        new PieceSpec { Path = "StandingStones/Prefab_RockFormation_05", Offset = new Vector2(6f, -1.5f), Yaw = 260f, Scale = 1f, IsWobbly = true },
        new PieceSpec { Path = "StandingStones/Prefab_RockFormation_11", Offset = new Vector2(-2.5f, -5f), Yaw = 20f, Scale = 1f, IsWobbly = true },
        new PieceSpec { Path = "StandingStones/Prefab_RockFormation_12", Offset = new Vector2(-7f, 2f), Yaw = 95f, Scale = 1f, IsWobbly = true }
    };

    // 바위 노두 : 가운데 절벽 조각 하나로 솟은 바위 언덕을 만들고, 큰 바위 6개 (1.5 ~ 3배)를 둘레에, 작은 돌 3개를 바깥에 둔다
    private static readonly PieceSpec[] OutcropPieces =
    {
        new PieceSpec { Path = "Stones/Prefab_Cliff_02", Offset = new Vector2(0f, 0f), Yaw = 0f, Scale = 0.5f },
        new PieceSpec { Path = "Stones/Prefab_Rock_13", Offset = new Vector2(5f, -1f), Yaw = 40f, Scale = 3f },
        new PieceSpec { Path = "Stones/Prefab_Rock_10", Offset = new Vector2(-4f, 4f), Yaw = 200f, Scale = 2.5f },
        new PieceSpec { Path = "Stones/Prefab_Rock_07", Offset = new Vector2(2f, 6f), Yaw = 110f, Scale = 2.2f },
        new PieceSpec { Path = "Stones/Prefab_Rock_05", Offset = new Vector2(-5f, -4f), Yaw = 300f, Scale = 2f },
        new PieceSpec { Path = "Stones/Prefab_Rock_03", Offset = new Vector2(8f, 4f), Yaw = 150f, Scale = 1.8f },
        new PieceSpec { Path = "Stones/Prefab_Rock_01", Offset = new Vector2(1f, -7f), Yaw = 250f, Scale = 1.5f },
        new PieceSpec { Path = "Stones/Prefab_SmallRock_01", Offset = new Vector2(11f, -4f), Yaw = 30f, Scale = 1f },
        new PieceSpec { Path = "Stones/Prefab_SmallRock_02", Offset = new Vector2(-10f, 0f), Yaw = 160f, Scale = 1.2f },
        new PieceSpec { Path = "Stones/Prefab_SmallRock_01", Offset = new Vector2(-3f, 10f), Yaw = 280f, Scale = 0.9f }
    };

    // 놓는 순서대로. 바위 노두가 룬 돌 무리와의 거리 조건을 보므로 룬 돌 무리를 먼저 놓는다
    private static readonly LandmarkSpec[] LandmarkSpecs =
    {
        new LandmarkSpec
        {
            Label = "HilltopRuneStones", GroupYaw = 0f, SinkRatio = StructureSinkRatio, ApproxRadius = 10f,
            IsHilltop = true, Pieces = HilltopStonePieces
        },
        // 바닥 반지름은 첫 실행에서 잰 23.6m를 사용자가 그대로 승인했다 (구성안은 약 15m)
        new LandmarkSpec { Label = "RockOutcrop", GroupYaw = 30f, SinkRatio = BigRockSinkRatio, ApproxRadius = 23.6f, Pieces = OutcropPieces }
    };

    // 렌더러 범위 둘레 8곳은 AABB보다 FootprintInset만큼 안쪽을 본다 (서쪽 배치와 같은 값)
    private const float FootprintInset = 0.7f;
    private static readonly Vector2[] RingOffsets =
    {
        new Vector2(-1f, -1f), new Vector2(0f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 0f),
        new Vector2(1f, 0f), new Vector2(-1f, 1f), new Vector2(0f, 1f), new Vector2(1f, 1f)
    };
    private const float MaxBuryGap = 3f;
    // 뜸 검수 (계산 오차만 허용) : 나무·바위는 피벗이 피벗 아래 지면보다, 랜드마크 조각은 정점 밑면이 바닥 둘레의 가장 낮은 땅보다 이만큼 넘게 높으면 뜬 것으로 센다
    private const float HeightTolerance = 0.01f;

    // GPU Resident Drawer로 그리면 몸체가 안 보이는 것으로 확인된 셰이더 (서쪽 배치와 같은 목록)
    private static readonly string[] GpuDrivenBrokenShaderNames = { "Polyart/Dreamscape CliffAuto", "M_Water_Lake_Amp" };
    private const string DisallowGpuDrivenTypeName = "UnityEngine.Rendering.DisallowGPUDrivenRendering, Unity.RenderPipelines.GPUDriven.Runtime";
    // 셰이더를 찾지 못하거나 컴파일에 실패하면 Unity가 대신 쓰는 분홍 셰이더
    private const string ErrorShaderName = "Hidden/InternalErrorShader";

    // 거점 기준 8방위. 0도 = 북(+Z), 시계 방향
    private static readonly string[] DirectionLabels = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };

    // 노이즈 값의 구분 경계 [드문드문 시작, 나무 무리 시작]. A3를 표본 조사해 면적 비율이 맞도록 한 번만 계산한다.
    // 위의 HabitatCenter, ZoneNoiseOffset을 쓰므로 그 뒤에 둔다 (정적 필드는 적힌 순서대로 초기화된다)
    private static readonly float[] ZoneThresholds = CalculateZoneThresholds();

    // 같은 위치는 항상 같은 구분을 돌려준다 (지면·바닥 식생 메뉴 16이 같은 구분을 쓰기 위함). 서식지 반경 안은 풀밭이다
    public static MapTreeScatter.FieldZone GetSouthWestZone(Vector2 worldXZ)
    {
        if (IsInHabitat(worldXZ)) return MapTreeScatter.FieldZone.Open;

        float noise = SampleZoneNoise(worldXZ);
        if (noise >= ZoneThresholds[1]) return MapTreeScatter.FieldZone.Forest;
        if (noise >= ZoneThresholds[0]) return MapTreeScatter.FieldZone.Sparse;
        return MapTreeScatter.FieldZone.Open;
    }

    [MenuItem("Tools/Map/15. 남서쪽 나무·바위·랜드마크·서식지 배치")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        Scene scene = terrain.gameObject.scene;
        if (scene != SceneManager.GetActiveScene())
        {
            Debug.LogError("[MapSouthWestScatter] Game 씬을 활성 씬으로 두고 실행하세요.");
            return;
        }

        GameObject southWestRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == SouthWestRegionName);
        if (southWestRoot == null)
        {
            Debug.LogError("씬 루트에 SouthWest가 없습니다. 먼저 'Tools/Map/1. 지형과 구역 부모 생성'을 실행하세요.");
            return;
        }

        System.Type disallowType = System.Type.GetType(DisallowGpuDrivenTypeName);
        if (disallowType == null)
        {
            Debug.LogError($"{DisallowGpuDrivenTypeName} 타입을 찾지 못했습니다. URP 패키지 버전이 바뀌었는지 확인하세요.");
            return;
        }

        Transform treeParent = ResetParent(southWestRoot.transform, TreeParentName);
        Transform rockParent = ResetParent(southWestRoot.transform, RockParentName);
        Transform landmarkParent = ResetParent(southWestRoot.transform, LandmarkParentName);
        Transform habitat = ResetParent(southWestRoot.transform, HabitatName);
        habitat.position = ToWorld(HabitatCenter, GetGroundHeight(terrain, HabitatCenter));

        var landmarkContext = new LandmarkContext
        {
            Terrain = terrain,
            DisallowType = disallowType,
            Parent = landmarkParent,
            Random = new System.Random(LandmarkSeed),
            Hilltop = FindHilltop(terrain)
        };
        foreach (LandmarkSpec spec in LandmarkSpecs)
            TryPlaceLandmark(landmarkContext, spec);
        ScatterContext scatterContext = ScatterTreesAndRocks(terrain, treeParent, rockParent, landmarkContext.Placed);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        LogZones(scatterContext);
        LogLandmarks(landmarkContext);
        LogPlacementCheck(terrain, new[] { treeParent, rockParent }, landmarkParent, landmarkContext.BrokenCount);
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

    // ---------- 구분 (풀밭 / 드문드문 / 나무 무리) ----------

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
    // 서식지 반경은 노이즈와 상관없이 풀밭이므로, 반경 밖 표본만으로 경계를 정하고 풀밭 몫에서 서식지 넓이를 뺀다.
    // 그래야 서식지를 합친 A3 전체 비율이 70 / 20 / 10%에 맞는다
    private static float[] CalculateZoneThresholds()
    {
        Rect southWest = MapGrid.GetRangeBounds(SouthWestCell, SouthWestCell);
        int columns = Mathf.RoundToInt(southWest.width / ZoneSampleSpacing);
        int rows = Mathf.RoundToInt(southWest.height / ZoneSampleSpacing);
        var outside = new List<float>(columns * rows);
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                var position = new Vector2(southWest.xMin + (x + 0.5f) * ZoneSampleSpacing, southWest.yMin + (z + 0.5f) * ZoneSampleSpacing);
                if (!IsInHabitat(position)) outside.Add(SampleZoneNoise(position));
            }
        }
        outside.Sort();

        int total = columns * rows;
        int habitatCount = total - outside.Count;
        int openCount = Mathf.Max(0, Mathf.RoundToInt(total * OpenAreaRatio) - habitatCount);
        int forestCount = Mathf.RoundToInt(total * ForestAreaRatio);
        return new[] { outside[openCount], outside[outside.Count - forestCount] };
    }

    private static bool IsInHabitat(Vector2 position)
    {
        return Vector2.Distance(position, HabitatCenter) < HabitatRadius;
    }

    // ---------- 랜드마크 ----------

    // 언덕 위(지형과 같은 비틀린 윤곽 안)를 격자로 돌며 지면이 가장 높은 곳. 비틀기 때문에 꼭대기가 언덕 중심에서 벗어날 수 있어 직접 찾는다.
    // 찾는 사각형은 A3 밖으로 나가지 않게 자른다
    private static Vector2 FindHilltop(Terrain terrain)
    {
        Rect southWest = MapGrid.GetRangeBounds(SouthWestCell, SouthWestCell);
        float range = MapSouthWestShaper.HillRadius * HilltopSearchRangeRatio;
        Vector2 center = MapSouthWestShaper.HillCenter;
        Vector2 best = center;
        float bestHeight = float.MinValue;
        for (float z = Mathf.Max(southWest.yMin, center.y - range); z <= Mathf.Min(southWest.yMax, center.y + range); z += HilltopSearchStep)
        {
            for (float x = Mathf.Max(southWest.xMin, center.x - range); x <= Mathf.Min(southWest.xMax, center.x + range); x += HilltopSearchStep)
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

    // 꼭대기 둘레 HilltopFitRadius 안의 자리와 묶음 회전 각도 중 가장 잘 맞는 것을 고르고, 묶음을 그 각도로 돌려 둔다
    private static LandmarkFit FindHilltopFit(LandmarkContext context, LandmarkBuild build, float sinkRatio)
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

                    LandmarkFit fit = MeasureLandmark(context.Terrain, build, sinkRatio, context.Hilltop + offset);
                    fit.GroupYaw = yaw;
                    if (best == null || IsBetterFit(fit, best.Value)) best = fit;
                }
            }
        }
        RotateLandmark(build, best.Value.GroupYaw);
        return best.Value;
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

    private static void TryPlaceLandmark(LandmarkContext context, LandmarkSpec spec)
    {
        LandmarkBuild build = BuildLandmark(context, spec);
        if (build == null) return;

        LandmarkFit? fit = spec.IsHilltop
            ? FindHilltopFit(context, build, spec.SinkRatio)
            : FindLandmarkSpot(context, build, spec.SinkRatio);
        if (fit == null)
        {
            Debug.LogWarning($"[MapSouthWestScatter] '{spec.Label}' 자리를 찾지 못했습니다 (바닥 반지름 {build.Radius:F1}m). 위치 규칙의 조건을 확인하세요.");
            Object.DestroyImmediate(build.Group.gameObject);
            return;
        }

        PlaceLandmark(context, build, spec, fit.Value);
    }

    // 배치도대로 월드 원점에 조립한다. 묶음 중심이 원점이라 조각의 월드 XZ가 곧 중심 기준 위치가 되어, 자리 찾기에서 그대로 쓴다
    private static LandmarkBuild BuildLandmark(LandmarkContext context, LandmarkSpec spec)
    {
        var build = new LandmarkBuild { Group = new GameObject(spec.Label).transform };
        build.Group.SetParent(context.Parent, false);
        build.Group.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, spec.GroupYaw, 0f));

        foreach (PieceSpec piece in spec.Pieces)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + piece.Path + ".prefab");
            if (prefab == null)
            {
                Debug.LogError($"[MapSouthWestScatter] {piece.Path} prefab을 찾지 못해 '{spec.Label}'을 놓지 못했습니다. 배치도의 경로를 확인하세요.");
                Object.DestroyImmediate(build.Group.gameObject);
                return null;
            }

            GameObject instance = CreatePiece(context, piece, prefab, build.Group);
            build.InstanceCount++;
            FinishInstance(context, build, instance, prefab.name);

            // 렌더러가 없는 조각은 크기와 높이를 잴 수 없어 깨진 조각으로만 세고 높이 맞춤에서 뺀다
            if (instance.GetComponentsInChildren<Renderer>().Length == 0)
            {
                build.BrokenMaterialCount++;
                continue;
            }

            Bounds bounds = GetRendererBounds(instance);
            (float bottom, float top) = GetMeshHeightRange(instance);
            build.Pieces.Add(new PlacedPiece { Transform = instance.transform, Points = GetFootprintPoints(bounds), Height = top - bottom });
            build.Radius = Mathf.Max(build.Radius, ToXZ(bounds.center).magnitude + Mathf.Max(bounds.extents.x, bounds.extents.z));
        }
        return build;
    }

    // prefab 연결을 유지한 채 놓는다. 흔들림 값은 조각 순서대로 같은 seed에서 뽑아 다시 실행해도 같다
    private static GameObject CreatePiece(LandmarkContext context, PieceSpec piece, GameObject prefab, Transform group)
    {
        Quaternion rotation = Quaternion.Euler(0f, piece.Yaw, 0f);
        float scale = piece.Scale;
        if (piece.IsWobbly)
        {
            float tilt = RandomRange(context.Random, 0f, PieceTiltMax);
            Vector3 tiltAxis = Quaternion.Euler(0f, RandomRange(context.Random, 0f, 360f), 0f) * Vector3.right;
            rotation = Quaternion.AngleAxis(tilt, tiltAxis) * rotation;
            scale *= RandomRange(context.Random, 1f - PieceScaleJitter, 1f + PieceScaleJitter);
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
        instance.transform.localPosition = new Vector3(piece.Offset.x, 0f, piece.Offset.y);
        instance.transform.localRotation = rotation;
        instance.transform.localScale = Vector3.one * scale;
        return instance;
    }

    private static LandmarkFit? FindLandmarkSpot(LandmarkContext context, LandmarkBuild build, float sinkRatio)
    {
        Rect southWest = MapGrid.GetRangeBounds(SouthWestCell, SouthWestCell);
        float inset = LandmarkEdgeMargin + build.Radius;
        LandmarkFit? best = null;
        for (float z = southWest.yMin + inset; z <= southWest.yMax - inset; z += LandmarkSearchStep)
        {
            for (float x = southWest.xMin + inset; x <= southWest.xMax - inset; x += LandmarkSearchStep)
            {
                var position = new Vector2(x, z);
                if (!CanPlaceLandmark(context, build.Radius, position)) continue;

                LandmarkFit fit = MeasureLandmark(context.Terrain, build, sinkRatio, position);
                if (best == null || IsBetterFit(fit, best.Value)) best = fit;
            }
        }
        return best;
    }

    private static bool CanPlaceLandmark(LandmarkContext context, float radius, Vector2 position)
    {
        if (Vector2.Distance(position, HabitatCenter) < HabitatRadius + radius) return false;
        if (context.Placed.Any(other => Vector2.Distance(other.Center, position) < LandmarkMinSpacing)) return false;
        return GetSlope(context.Terrain, position) <= LandmarkMaxSlope;
    }

    // 묶음 중심을 position에 옮겼을 때 조각마다 바닥 아래 지면을 재서, 땅에 맞추면 많이 묻히는 조각 수를 센다 (PlaceLandmark와 같은 계산)
    private static LandmarkFit MeasureLandmark(Terrain terrain, LandmarkBuild build, float sinkRatio, Vector2 position)
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
            if (IsBuried(lowest, highest, piece.Height, sinkRatio)) brokenCount++;
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

        int brokenCount = build.Pieces.Count(piece => GroundPiece(context.Terrain, piece, spec.SinkRatio));
        context.Placed.Add(new Blocker { Center = fit.Position, Radius = build.Radius + LandmarkClearance });
        context.BrokenCount += brokenCount;

        string prefabNames = string.Join(", ", build.Group.Cast<Transform>()
            .GroupBy(piece => piece.name)
            .OrderByDescending(names => names.Count())
            .Select(names => $"{names.Key} × {names.Count()}"));
        context.Notes.Add($"{spec.Label} : 중심 ({fit.Position.x:F1}, {fit.Position.y:F1}) 지면 {centerGround:F1}m, " +
                          $"묶음 회전 {build.Group.eulerAngles.y:F0}도, 바닥 반지름 {build.Radius:F1}m (기준 약 {spec.ApproxRadius:F1}m), 구분 {ZoneLabels[(int)GetSouthWestZone(fit.Position)]}, " +
                          $"바닥 아래 지면 높이 차 {fit.GroundRange:F1}m, 경사 {GetSlope(context.Terrain, fit.Position):F1}도, " +
                          $"조각 {build.InstanceCount}개 (뜨거나 묻힘 {brokenCount}, 깨진 머티리얼·렌더러 없음 {build.BrokenMaterialCount}), " +
                          $"Disallow 붙임 {build.DisallowCount}, 충돌체 추가 {build.ColliderCount}" +
                          $"\n  prefab : {prefabNames}");
    }

    // 밑면을 바닥 둘레의 가장 낮은 땅보다 (높이 × sinkRatio)만큼 아래에 맞춘다. 비탈 아래쪽이 뜨지 않고 위쪽은 땅에 묻힌다.
    // 둘레 표본 위치(XZ)는 렌더러 범위로, 밑면과 높이는 메시 정점으로 잰다 (메시 범위가 실제 모양보다 아래로 잡힌 prefab이 있어서).
    // 옮긴 뒤 정점 밑면을 다시 재서 가장 낮은 땅보다 높으면 뜬 것으로 본다. 뜨거나 많이 묻혔으면 true
    private static bool GroundPiece(Terrain terrain, PlacedPiece piece, float sinkRatio)
    {
        GameObject instance = piece.Transform.gameObject;
        float[] grounds = GetFootprintPoints(GetRendererBounds(instance)).Select(point => GetGroundHeight(terrain, point)).ToArray();
        float lowest = grounds.Min();
        (float bottom, float top) = GetMeshHeightRange(instance);
        float height = top - bottom;
        piece.Transform.position += Vector3.up * (lowest - height * sinkRatio - bottom);

        bool isFloating = GetMeshHeightRange(instance).Bottom > lowest + HeightTolerance;
        return isFloating || IsBuried(lowest, grounds.Max(), height, sinkRatio);
    }

    // 실제 메시 정점의 가장 낮은, 높은 월드 Y. LODGroup이 있으면 LOD0 렌더러만 본다 (LOD1 이하는 같은 자리의 낮은 단계 메시라 겹친다)
    private static (float Bottom, float Top) GetMeshHeightRange(GameObject instance)
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
                float y = toWorld.MultiplyPoint3x4(vertex).y;
                bottom = Mathf.Min(bottom, y);
                top = Mathf.Max(top, y);
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
    private static void FinishInstance(LandmarkContext context, LandmarkBuild build, GameObject instance, string prefabName)
    {
        if (MapTreeScatter.AddMissingCollider(instance, prefabName)) build.ColliderCount++;

        // Static Batching과 섞지 않는다 (MapLayout.md 성능 기준)
        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetStaticEditorFlags(child.gameObject) != 0) GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
        }

        foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.sharedMaterials.Any(IsBrokenMaterial)) build.BrokenMaterialCount++;
            if (renderer.sharedMaterials.All(material => material == null || SupportsGpuDriven(context, material.shader))) continue;
            if (renderer.GetComponent(context.DisallowType) != null) continue;

            // 절벽 조각(CliffAuto 셰이더)이 여기에 걸린다
            renderer.gameObject.AddComponent(context.DisallowType);
            build.DisallowCount++;
        }
    }

    // 분홍으로 보이는 머티리얼 (머티리얼 없음, 셰이더 없음, 오류 셰이더, 이 환경에서 지원하지 않는 셰이더)
    private static bool IsBrokenMaterial(Material material)
    {
        if (material == null || material.shader == null) return true;
        return material.shader.name == ErrorShaderName || !material.shader.isSupported;
    }

    private static bool SupportsGpuDriven(LandmarkContext context, Shader shader)
    {
        if (context.GpuDrivenSupport.TryGetValue(shader, out bool isSupported)) return isSupported;

        isSupported = !GpuDrivenBrokenShaderNames.Contains(shader.name);
        context.GpuDrivenSupport[shader] = isSupported;
        return isSupported;
    }

    // ---------- 나무, 바위 ----------

    private static ScatterContext ScatterTreesAndRocks(Terrain terrain, Transform treeParent, Transform rockParent, List<Blocker> landmarks)
    {
        var context = new ScatterContext
        {
            Terrain = terrain,
            Random = new System.Random(ScatterSeed),
            Landmarks = landmarks,
            TreeGroups = new[]
            {
                CreateGroup(MapTreeScatter.MainTreePaths, treeParent, MapTreeScatter.TreeScaleMin, MapTreeScatter.TreeScaleMax, false),
                CreateGroup(MapTreeScatter.DeadTreePaths, treeParent, MapTreeScatter.TreeScaleMin, MapTreeScatter.TreeScaleMax, false),
                CreateGroup(MapTreeScatter.GiantTreePaths, treeParent, MapTreeScatter.TreeScaleMin, MapTreeScatter.TreeScaleMax, false)
            },
            RockGroup = CreateGroup(MapTreeScatter.RockPaths, rockParent, MapTreeScatter.RockScaleMin, MapTreeScatter.RockScaleMax, true)
        };

        Rect southWest = MapGrid.GetRangeBounds(SouthWestCell, SouthWestCell);
        int columns = Mathf.RoundToInt(southWest.width / ScatterCellSize);
        int rows = Mathf.RoundToInt(southWest.height / ScatterCellSize);
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                var cellMin = new Vector2(southWest.xMin + x * ScatterCellSize, southWest.yMin + z * ScatterCellSize);
                Vector2 cellCenter = cellMin + Vector2.one * (ScatterCellSize / 2f);
                MapTreeScatter.FieldZone zone = GetSouthWestZone(cellCenter);
                int terrainKind = IsOnHill(cellCenter) ? HillTerrain : FlatTerrain;
                context.ZoneCellCounts[(int)zone]++;
                context.TerrainCellCounts[terrainKind][(int)zone]++;
                ScatterTrees(context, cellMin, zone);
                ScatterRocks(context, cellMin, zone, terrainKind);
            }
        }
        return context;
    }

    private static InstanceGroup CreateGroup(string[] paths, Transform parent, float scaleMin, float scaleMax, bool isRock)
    {
        GameObject[] prefabs = paths
            .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + path + ".prefab"))
            .ToArray();
        return new InstanceGroup { Prefabs = prefabs, Parent = parent, ScaleMin = scaleMin, ScaleMax = scaleMax, IsRock = isRock };
    }

    private static void ScatterTrees(ScatterContext context, Vector2 cellMin, MapTreeScatter.FieldZone zone)
    {
        int count = RollCount(context.Random, TreeDensityMin[(int)zone], TreeDensityMax[(int)zone]);
        for (int i = 0; i < count; i++)
        {
            TreeKind kind = PickTreeKind(context.Random, zone);
            if (!TryAddInstance(context, cellMin, context.TreeGroups[(int)kind])) continue;

            context.TreeKindCounts[(int)kind]++;
            context.TreeCounts[(int)zone]++;
        }
    }

    private static void ScatterRocks(ScatterContext context, Vector2 cellMin, MapTreeScatter.FieldZone zone, int terrainKind)
    {
        int count = RollCount(context.Random, RockDensityMin[terrainKind][(int)zone], RockDensityMax[terrainKind][(int)zone]);
        for (int i = 0; i < count; i++)
        {
            if (!TryAddInstance(context, cellMin, context.RockGroup)) continue;

            context.RockCounts[(int)zone]++;
            context.TerrainRockCounts[terrainKind][(int)zone]++;
        }
    }

    // 지형(메뉴 14)과 같은 비틀린 윤곽 기준. A3 안쪽은 경계 감쇠값이 0보다 커서 언덕 모양 가중치만 보면 된다
    private static bool IsOnHill(Vector2 position)
    {
        return MapSouthWestShaper.CalculateHillWeight(position) > 0f;
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

    // 고사목은 모든 구분, 거대목은 나무 무리에서만 나온다 (서쪽과 같은 규칙)
    private static TreeKind PickTreeKind(System.Random random, MapTreeScatter.FieldZone zone)
    {
        double roll = random.NextDouble();
        if (zone == MapTreeScatter.FieldZone.Forest)
        {
            if (roll < MapTreeScatter.GiantTreeRatio) return TreeKind.Giant;
            roll -= MapTreeScatter.GiantTreeRatio;
        }
        return roll < MapTreeScatter.DeadTreeRatio ? TreeKind.Dead : TreeKind.Main;
    }

    // 서식지 반경, 랜드마크 둘레, 급경사에는 놓지 않는다. 무작위 값은 조건과 상관없이 같은 순서로 뽑아 결과가 항상 같게 한다
    private static bool TryAddInstance(ScatterContext context, Vector2 cellMin, InstanceGroup group)
    {
        System.Random random = context.Random;
        var position = new Vector2(
            cellMin.x + RandomRange(random, 0f, ScatterCellSize),
            cellMin.y + RandomRange(random, 0f, ScatterCellSize));
        GameObject prefab = group.Prefabs[random.Next(group.Prefabs.Length)];
        float scale = RandomRange(random, group.ScaleMin, group.ScaleMax);
        float yaw = RandomRange(random, 0f, 360f);

        if (IsInHabitat(position))
        {
            context.HabitatSkipCount++;
            return false;
        }
        if (context.Landmarks.Any(blocker => Vector2.Distance(blocker.Center, position) < blocker.Radius))
        {
            context.LandmarkSkipCount++;
            return false;
        }
        if (GetSlope(context.Terrain, position) > MapTreeScatter.MaxSlope)
        {
            context.SlopeSkipCount++;
            return false;
        }

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
    // 아래 로그는 검수(비율, 밀도가 승인 값 안인지, 랜드마크 위치, 서식지 비움, 뜸·묻힘, A3 밖 배치, 성능 측정용 배치 수)에 쓰는 값이라 남긴다

    private static void LogZones(ScatterContext context)
    {
        float totalCells = context.ZoneCellCounts.Sum();
        var lines = new List<string>();
        for (int zone = 0; zone < ZoneLabels.Length; zone++)
        {
            float area = context.ZoneCellCounts[zone] * ScatterCellSize * ScatterCellSize / HectareArea;
            lines.Add($"{ZoneLabels[zone]} {area:F1}ha ({context.ZoneCellCounts[zone] / totalCells:P1}) : " +
                      $"나무 {context.TreeCounts[zone]}그루 {FormatDensity(context.TreeCounts[zone], area, TreeDensityMin[zone], TreeDensityMax[zone])}, " +
                      $"바위·소품 {context.RockCounts[zone]}개 " +
                      $"{FormatDensity(context.RockCounts[zone], area, RockDensityMin[HillTerrain][zone], RockDensityMax[FlatTerrain][zone])}");
        }

        // 바위 밀도는 지대마다 표가 달라 지대별로 따로 대조한다
        for (int terrainKind = 0; terrainKind < TerrainLabels.Length; terrainKind++)
        {
            var parts = new List<string>();
            for (int zone = 0; zone < ZoneLabels.Length; zone++)
            {
                float area = context.TerrainCellCounts[terrainKind][zone] * ScatterCellSize * ScatterCellSize / HectareArea;
                int rockCount = context.TerrainRockCounts[terrainKind][zone];
                parts.Add($"{ZoneLabels[zone]} {area:F1}ha 바위·소품 {rockCount}개 " +
                          $"{FormatDensity(rockCount, area, RockDensityMin[terrainKind][zone], RockDensityMax[terrainKind][zone])}");
            }
            lines.Add($"[{TerrainLabels[terrainKind]}] " + string.Join(", ", parts));
        }

        int[] kinds = context.TreeKindCounts;
        Debug.Log($"[MapSouthWestScatter] 남서쪽(A3) 구분별 면적과 밀도 (승인 비율 풀밭 {OpenAreaRatio:P0} / 드문드문 {1f - ForestAreaRatio - OpenAreaRatio:P0} / 나무 무리 {ForestAreaRatio:P0}) :\n" +
                  string.Join("\n", lines));
        Debug.Log($"[MapSouthWestScatter] 나무 {kinds.Sum()}그루 (주력 {kinds[(int)TreeKind.Main]}, 고사목 {kinds[(int)TreeKind.Dead]} ({kinds[(int)TreeKind.Dead] / Mathf.Max(1f, kinds.Sum()):P1}), " +
                  $"거대목 {kinds[(int)TreeKind.Giant]}), 바위·소품 {context.RockCounts.Sum()}개, 충돌체 추가 {context.ColliderCount}개 / " +
                  $"건너뜀 서식지 {context.HabitatSkipCount}, 랜드마크 둘레 {context.LandmarkSkipCount}, 경사 {context.SlopeSkipCount}");
    }

    private static string FormatDensity(int count, float area, float min, float max)
    {
        float density = area > 0f ? count / area : 0f;
        bool isInRange = density >= min && density <= max;
        return $"(1ha당 {density:F1}, 표 {min:F0} ~ {max:F0} {(isInRange ? "안" : "밖")})";
    }

    private static void LogLandmarks(LandmarkContext context)
    {
        Debug.Log($"[MapSouthWestScatter] 랜드마크 {context.Notes.Count}/{LandmarkSpecs.Length}곳 :\n" + string.Join("\n", context.Notes));
    }

    // 놓인 오브젝트를 다시 읽어 서식지 반경 안 개수(0이어야 함), A3 밖 개수(0이어야 함), 나무·바위의 뜸, 묻힘을 센다.
    // 랜드마크의 뜸, 묻힘은 놓을 때 잰 값을 더한다
    private static void LogPlacementCheck(Terrain terrain, Transform[] scatterParents, Transform landmarkParent, int landmarkBrokenCount)
    {
        List<Transform> scattered = scatterParents.SelectMany(parent => parent.Cast<Transform>()).ToList();
        List<Transform> landmarkGroups = landmarkParent.Cast<Transform>().ToList();
        List<Transform> landmarkItems = landmarkGroups.SelectMany(group => group.Cast<Transform>()).ToList();

        int inHabitatCount = scattered.Concat(landmarkItems).Count(child => IsInHabitat(ToXZ(child.position)));
        // 서식지 표시는 HabitatCenter에 두므로 그 위치로 함께 센다
        Rect southWest = MapGrid.GetRangeBounds(SouthWestCell, SouthWestCell);
        int outsideCount = scattered.Concat(landmarkItems).Concat(landmarkGroups).Select(child => ToXZ(child.position)).Append(HabitatCenter)
            .Count(position => !southWest.Contains(position));

        int floatingCount = 0;
        int buriedCount = 0;
        foreach (Transform child in scattered)
        {
            float gap = child.position.y - GetGroundHeight(terrain, ToXZ(child.position));
            if (gap > HeightTolerance) floatingCount++;
            if (gap < -MaxBuryGap) buriedCount++;
        }

        float azimuth = GetAzimuth(HabitatCenter);
        string direction = DirectionLabels[Mathf.RoundToInt(azimuth / 45f) % DirectionLabels.Length];
        Debug.Log($"[MapSouthWestScatter] 서식지 ({HabitatCenter.x:F0}, {GetGroundHeight(terrain, HabitatCenter):F1}, {HabitatCenter.y:F0}), " +
                  $"거점 기준 방위각 {azimuth:F1}도 ({direction}), 반경 {HabitatRadius}m 안 오브젝트 {inHabitatCount}개 (0이어야 한다)");
        Debug.Log($"[MapSouthWestScatter] 뜨거나 묻힌 오브젝트 : 나무·바위 뜸 {floatingCount}, 묻힘 {buriedCount} / 랜드마크 {landmarkBrokenCount}. " +
                  $"A3 밖에 놓인 오브젝트 {outsideCount}개 (0이어야 한다)");

        // 랜드마크 묶음 GameObject와 서식지 표시(1)도 씬 GameObject라 함께 센다
        int total = scattered.Count + landmarkItems.Count + landmarkGroups.Count + 1;
        Debug.Log($"[MapSouthWestScatter] SouthWest 배치 GameObject 총 {total}개 (나무·바위 {scattered.Count}, 랜드마크 오브젝트 {landmarkItems.Count}, " +
                  $"랜드마크 묶음 {landmarkGroups.Count}, 서식지 표시 1)");
    }
}
