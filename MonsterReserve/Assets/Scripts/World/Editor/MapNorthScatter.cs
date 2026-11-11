using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 메뉴 8 : 북쪽 줄(A1~C1)의 산에 나무, 바위, 큰 바위 무리, 절벽, 룬 돌(랜드마크)을 prefab 인스턴스(GameObject)로 놓는다.
// 나무와 바위는 높이대(기슭, 중턱, 정상 부근)별 밀도로 흩뿌리고, 강(메뉴 7)과 호수(메뉴 6) 위와 그 가장자리는 비운다.
// A1, C1에서 산이 끝난 바깥 평지(바탕 높이 근처)는 그 구역 작업 때 채우므로 비워 둔다.
// 놓는 순서 : 룬 돌 → 절벽 → 큰 바위 → 나무·바위. 먼저 놓은 큰 오브젝트 자리에는 뒤에 놓는 것이 겹치지 않는다.
// 다시 실행하면 NorthWest, North, NorthEast 아래 Trees, Rocks, Cliffs, Landmarks의 자식을 지우고 같은 seed로 다시 만든다.
// 메뉴 5 → 6 → 7 다음에 실행한다 (지형, 호수 수면, 강 경로를 읽는다).
public static class MapNorthScatter
{
    // 지면 칠하기(메뉴 9)도 같은 높이대를 쓴다
    public enum HeightBand
    {
        Foothill = 0,
        Mid = 1,
        Summit = 2
    }

    // 순서는 KindParentNames와 같다
    private enum PlacementKind
    {
        Tree = 0,
        Rock = 1,
        Cliff = 2,
        Landmark = 3
    }

    private enum TreeKind
    {
        Main = 0,
        Dead = 1,
        Giant = 2
    }

    // prefab 하나의 크기 (prefab 루트 기준, 배율 1)
    private class PrefabShape
    {
        public GameObject Prefab;
        public Bounds LocalBounds;
        // 피벗에서 가장 먼 바닥 모서리까지의 수평 거리. 어느 방향으로 돌려도 바닥이 이 원 안에 든다
        public float FootprintRadius;
        // 절벽만 쓴다. 앞면(바위 벽)이 보는 prefab 기준 수평 축 (±x 또는 ±z)
        public Vector3 FaceAxis;
    }

    // 먼저 놓은 큰 오브젝트가 차지한 자리
    private struct Blocker
    {
        public Vector2 Center;
        public float Radius;
    }

    // 큰 오브젝트 종류마다 다른 놓기 규칙
    private struct LargeRule
    {
        public float MaxSlope;
        // 다른 큰 오브젝트와 (두 반지름의 합 × 이 값)보다 가까우면 놓지 않는다. 1보다 작으면 조금 겹쳐 무리 지어 보인다
        public float OverlapRatio;
        // 오브젝트 높이의 이 비율만큼 가장 낮은 땅보다 아래로 박는다
        public float SinkRatio;
    }

    private struct Placement
    {
        public GameObject Prefab;
        public Vector2 Position;
        public float Yaw;
        public float Scale;
        public PlacementKind Kind;
    }

    private struct CliffDraw
    {
        public PrefabShape Shape;
        // 처음 시도하는 배율. 윗면 돌출 조건을 못 맞추면 CliffScaleMax까지 키운다
        public float Scale;
        public float YawJitter;
    }

    // 한 자리에 절벽을 놓았을 때의 높이 계산 결과
    private struct CliffFit
    {
        public Vector2 Position;
        public float Yaw;
        public float Scale;
        public float Slope;
        public float RootHeight;
        // 앞쪽(비탈 아래) 끝 땅의 가장 낮은 곳, 가장 높은 곳 기준으로 밑동이 땅속에 든 깊이
        public float SinkMin;
        public float SinkMax;
        // 윗면 - 바닥 범위 안 가장 높은 땅(비탈 위쪽). 양수면 절벽이 뒤쪽 땅보다 솟아 보인다
        public float Exposure;
        // 바닥 범위 안에 밑면보다 낮은 땅이 있으면 그 부분이 뜬다
        public bool IsFloating;
    }

    // 큰 바위 무리 하나. 중심에서 DistanceMin ~ DistanceMax 거리에 놓는다
    private struct Cluster
    {
        public Vector2 Center;
        public float DistanceMin;
        public float DistanceMax;
    }

    private struct LandmarkSpot
    {
        public string Label;
        public IEnumerable<Vector2> Candidates;
    }

    // 흩뿌리기 칸 하나의 지형 정보 (칸 중심 기준)
    private struct Sample
    {
        public Vector2 Position;
        public float Height;
        public float Slope;
        // 둘레 네 곳보다 얼마나 높은지. 클수록 능선, 봉우리처럼 볼록한 곳
        public float Convexity;
        public HeightBand Band;
        // 이번 작업이 채우는 곳인지 (A1, C1 바깥 평지는 false)
        public bool IsEligible;
    }

    private class WaterMask
    {
        public List<Vector2> RiverPath;
        public Rect RiverBounds;
        // 호수 수면 메시 정점(월드 XZ)을 LakeHashCellSize 칸으로 나눠 담는다
        public Dictionary<Vector2Int, List<Vector2>> LakeVertices = new Dictionary<Vector2Int, List<Vector2>>();
        public Rect LakeBounds;
        // 수면 메시 정점 간격 (= 높이맵 간격). 물 위 검사에서 "메시 위"로 보는 거리
        public float LakeVertexSpacing;
    }

    private class BandStats
    {
        public int[] CellCounts = new int[3];
        // 칸 중심 경사가 나무 기준 경사 이하인 칸
        public int[] TreeableCellCounts = new int[3];
        public int[] TreeCounts = new int[3];
        public int[] RockCounts = new int[3];
    }

    private class ScatterContext
    {
        public Terrain Terrain;
        public System.Type DisallowType;
        public Rect NorthArea;
        public Rect MiddleColumn;
        // [구역, 종류] 부모. 구역 순서는 RegionNames, 종류 순서는 PlacementKind
        public Transform[,] Parents;
        public WaterMask Water;
        public List<Sample> Samples;
        public Dictionary<GameObject, PrefabShape> Shapes = new Dictionary<GameObject, PrefabShape>();
        public List<Blocker> Blockers = new List<Blocker>();
        public List<Blocker> Cliffs = new List<Blocker>();
        public System.Random ScatterRandom;
        public GameObject[][] TreePrefabs;
        public GameObject[] RockPrefabs;
        public int CliffClusterRockCount;
        public int RidgeClusterRockCount;
        public int SlopeSkipCount;
        public int WaterSkipCount;
        public int BlockerSkipCount;
        public int ColliderCount;
        // 경사와 자리는 맞았지만 최대 배율에서도 윗면 돌출, 뜸 조건을 못 맞춘 절벽 후보 수
        public int CliffFitFailCount;
        public List<string> CliffFaceNotes = new List<string>();
        public List<string> CliffNotes = new List<string>();
        public List<string> LandmarkNotes = new List<string>();
    }

    public const string NorthFromCell = "A1";
    public const string NorthToCell = "C1";
    // 정상이 있는 가운데 칸 (북 구역). 이 칸은 산자락 끝까지 모두 채운다
    public const string MiddleCell = "B1";

    // 구역 부모 이름. 순서는 서 → 동 (A1, B1, C1)
    public static readonly string[] RegionNames = { "NorthWest", "North", "NorthEast" };
    private const int WestRegionIndex = 0;
    private const int MiddleRegionIndex = 1;
    private const int EastRegionIndex = 2;
    private static readonly string[] KindParentNames = { "Trees", "Rocks", "Cliffs", "Landmarks" };
    // 호수 수면은 메뉴 6이 North/Lake 아래에 놓는다
    private const string LakeParentName = "Lake";

    // 단계마다 seed를 따로 두어, 한 단계의 값을 바꿔도 다른 단계의 결과가 바뀌지 않게 한다
    private const int ScatterSeed = 20261113;
    private const int CliffSeed = 20261114;
    private const int BigRockSeed = 20261115;
    private const int LandmarkSeed = 20261116;

    // 흩뿌리기 단위 칸과 1ha 넓이 (MapTreeScatter와 같은 단위)
    private const float ScatterCellSize = 10f;
    private const float HectareArea = 10000f;

    // 높이대 경계 (월드 높이, 임시값). 바탕 30m ~ 정상 380m 기준.
    // 기슭 ~ 160m : 아래쪽 약 37%. 호수(수면 약 128m)와 그 둘레가 기슭 숲에 들어가 호숫가가 숲으로 둘러싸인다.
    // 정상 부근 310m ~ : 위쪽 약 20%. 메뉴 5가 가팔라도 되는 정상부 바위 지대로 보는 높이(SummitZoneHeight)와 같게 둔다.
    // 그 사이가 중턱이다 (옆 봉우리 꼭대기 약 250m가 여기 든다)
    private const float FoothillMaxHeight = 160f;
    private const float SummitMinHeight = 310f;
    // 높이대 경계가 등고선을 따라 곧은 줄로 보이지 않게 경계 높이를 노이즈로 ± 이만큼 흔든다
    private const float BandJitter = 15f;
    private const float BandJitterPeriod = 80f;
    private static readonly Vector2 BandNoiseOffset = new Vector2(523.1f, 187.9f);

    // 밀도 표 (1ha당 최소, 최대, 사용자 승인 임시값). 순서는 HeightBand (기슭, 중턱, 정상 부근)
    private static readonly float[] TreeDensityMin = { 30f, 5f, 0f };
    private static readonly float[] TreeDensityMax = { 50f, 15f, 0f };
    private static readonly float[] RockDensityMin = { 1f, 5f, 10f };
    private static readonly float[] RockDensityMax = { 3f, 10f, 20f };
    private static readonly string[] BandLabels = { "기슭", "중턱", "정상 부근" };

    // A1, C1에서 바탕 높이보다 이만큼 높지 않은 곳은 산이 끝난 바깥 평지로 보고 비운다 (임시값).
    // 메뉴 5가 산맥 끝 바깥에 남기는 낮은 언덕(바탕 + 최대 약 17.5m)을 넘는 값이다
    public const float OuterFlatMaxRise = 25f;

    // 바위는 정상부 바위 지대에도 놓도록 나무보다 가파른 곳까지 허용한다 (임시값)
    private const float RockMaxSlope = 50f;
    // 바위 피벗을 이 반지름(× 배율) 안의 가장 낮은 땅에 맞춰 비탈 아래쪽이 뜨지 않게 한다
    private const float RockFootprintRadius = 1f;
    // 산에는 그루터기, 속 빈 통나무(Objects/)를 빼고 돌만 쓴다
    private const string StonePathPrefix = "Stones/";

    // 물 피하기. 강은 MapTreeScatter.RiverClearMargin, 호수는 수면 메시 정점에서 이 거리 안을 비운다 (임시값).
    // 수면 메시 끝은 물가보다 한 칸(약 1.2m) 밖이라, 실제 물가에서는 약 9m가 비워진다
    private const float LakeClearMargin = 8f;
    // 강 경로 범위로 먼저 걸러 IsNearRiver 계산을 줄일 때 쓰는 여유 (강 물 반폭 6m보다 넉넉히)
    private const float RiverBoundsPadding = 10f;
    private const float LakeHashCellSize = 5f;

    // 절벽 (사용자 승인 임시값 : 20개, 크기 1.5 ~ 3배)
    private static readonly string[] CliffPaths =
    {
        "Stones/Prefab_Cliff_01", "Stones/Prefab_Cliff_02", "Stones/Prefab_Cliff_03", "Stones/Prefab_Cliff_04", "Stones/Prefab_Cliff_05"
    };
    private const int LakeWallCliffCount = 4;
    private const int SteepCliffCount = 16;
    private const float CliffScaleMin = 1.5f;
    private const float CliffScaleMax = 3f;
    // 절벽끼리의 최소 거리. 한곳에 몰리지 않고 산 전체에 흩어지게 한다 (임시값)
    private const float CliffMinSpacing = 70f;
    // 절벽을 놓는 비탈 경사 범위 (임시값). 아래는 나무 기준 경사(35도).
    // 위가 너무 가파르면 절벽 깊이만큼 뒤쪽 땅이 높아져(깊이 × tan(경사)) 윗면이 비탈에 묻힌다
    private const float CliffSlopeMin = MapTreeScatter.MaxSlope;
    private const float CliffSlopeMax = 50f;
    // 밑동은 앞쪽(비탈 아래) 끝 땅의 가장 낮은 곳보다 이만큼만 박는다 (임시값)
    private const float CliffBaseSink = 1f;
    // 윗면이 바닥 범위 안 가장 높은 땅(비탈 위쪽)보다 이만큼 이상 솟아야 놓는다 (임시값)
    private const float CliffMinExposure = 4f;
    // 조건을 못 맞추면 배율을 이만큼씩 CliffScaleMax까지 키워 다시 본다
    private const float CliffScaleStep = 0.25f;
    // 바닥 높이를 재는 점 수 (폭 방향, 깊이 방향). 깊이 방향 마지막 줄이 앞쪽 끝이다
    private const int CliffWidthSamples = 5;
    private const int CliffDepthSamples = 3;
    // 폭이 넓은 절벽(약 40m)은 조금만 비틀어도 양 끝 높이 차가 커져 한쪽이 묻히므로 작게 둔다 (임시값)
    private const float CliffYawJitter = 5f;
    // 앞면 찾기 : 법선 y가 이 값보다 작은(60도보다 가파른) 면의 넓이를 얇은 축 + 쪽, - 쪽으로 나눠 많은 쪽을 앞면으로 본다
    private const float CliffSteepNormalY = 0.5f;
    // 두 넓이 비가 이 값보다 작으면 판단하지 않고 CliffFaceSign을 쓴다
    private const float CliffFaceMinRatio = 1.15f;
    // 메시로 앞면을 판단하지 못할 때 쓴다. 얇은 축의 + 방향이면 1, - 방향이면 -1 (미검증)
    private const float CliffFaceSign = 1f;
    // 호수 뒤 벽 : 호수 중심에서 북쪽 ± 이 각도 안을 같은 간격으로 나눠, 방향마다 조건을 맞추는 가장 가파른 곳에 놓는다
    private const float LakeWallArcHalfAngle = 60f;
    private const float LakeWallSearchMin = 45f;
    private const float LakeWallSearchMax = 200f;
    private const float RaySearchStep = 2f;

    // 큰 바위 (데모 씬에서 많이 쓴 큰 바위, 사용자 승인 임시값 : 100 ~ 150개)
    private static readonly string[] BigRockPaths =
    {
        "Stones/Prefab_Rock_12", "Stones/Prefab_Rock_06", "Stones/Prefab_Rock_04", "Stones/Prefab_Rock_09"
    };
    public const float BigRockScaleMin = 1.5f;
    private const float BigRockScaleMax = 3f;
    private const float BigRockMaxSlope = 50f;
    private const float BigRockOverlapRatio = 0.5f;
    private const float BigRockSinkRatio = 0.25f;
    // 절벽 둘레 무리 : 절벽마다 2 ~ 4개, 절벽 바닥 반지름의 0.6 ~ 1.2배 거리 (절벽 밑동을 두르게)
    private const int CliffClusterRockMin = 2;
    private const int CliffClusterRockMax = 4;
    private const float CliffClusterDistanceMin = 0.6f;
    private const float CliffClusterDistanceMax = 1.2f;
    // 능선, 정상부 무리 : 중턱 이상에서 볼록한 곳에 14무리, 무리마다 3 ~ 7개. 절벽 무리와 합쳐 약 130개가 되게 한다
    private const int RidgeClusterCount = 14;
    private const int RidgeClusterRockMin = 3;
    private const int RidgeClusterRockMax = 7;
    private const float RidgeClusterRadius = 25f;
    private const float RidgeClusterSpacing = 80f;
    // 볼록함 = 칸 높이 - 이 거리만큼 떨어진 동서남북 네 곳의 평균 높이
    private const float RidgeProbeDistance = 25f;
    private const float RidgeConvexity = 3f;
    private const int MaxPlaceAttempts = 10;
    private const int BigRockTargetMin = 100;
    private const int BigRockTargetMax = 150;

    // 룬 돌 (랜드마크). 키가 큰 선돌만 쓴다 (08, 12는 낮게 누운 돌이라 멀리서 눈에 띄지 않는다)
    private static readonly string[] LandmarkPaths =
    {
        "StandingStones/Prefab_RockFormation_01", "StandingStones/Prefab_RockFormation_02", "StandingStones/Prefab_RockFormation_03",
        "StandingStones/Prefab_RockFormation_04", "StandingStones/Prefab_RockFormation_05", "StandingStones/Prefab_RockFormation_06",
        "StandingStones/Prefab_RockFormation_07", "StandingStones/Prefab_RockFormation_09", "StandingStones/Prefab_RockFormation_10",
        "StandingStones/Prefab_RockFormation_11"
    };
    private const float LandmarkMaxSlope = 25f;
    private const float LandmarkSinkRatio = 0.05f;
    // 룬 돌 둘레에 나무, 바위를 두지 않아 멀리서도 보이는 빈터가 되게 한다 (임시값)
    private const float LandmarkClearance = 8f;
    private const float SummitLandmarkSearchRadius = 40f;
    private const float PassLandmarkSearchRadius = 30f;
    // 고개를 찾는 범위 (정상 기준 동서 거리). 메뉴 5의 정상과 옆 봉우리(서 -420m, 동 +450m) 사이다
    private const float PassSearchMin = 100f;
    private const float PassSearchMax = 400f;
    // 강은 호수 남쪽으로 나가므로 반대편이 아닌 서쪽 물가에 둔다
    private static readonly Vector2 LakeLandmarkDirection = Vector2.left;
    private const float LakeLandmarkSearchMax = 150f;

    // 절벽은 PlaceGrounded를 쓰지 않고 MeasureCliff로 높이를 정하므로 SinkRatio를 쓰지 않는다
    private static readonly LargeRule CliffRule = new LargeRule { MaxSlope = CliffSlopeMax, OverlapRatio = 1f };
    private static readonly LargeRule BigRockRule = new LargeRule { MaxSlope = BigRockMaxSlope, OverlapRatio = BigRockOverlapRatio, SinkRatio = BigRockSinkRatio };
    private static readonly LargeRule LandmarkRule = new LargeRule { MaxSlope = LandmarkMaxSlope, OverlapRatio = 1f, SinkRatio = LandmarkSinkRatio };

    // 절벽 셰이더는 DOTS 인스턴싱을 지원하지 않아 GPU Resident Drawer에서 빼야 몸체가 보인다 (MapLakeBuilder와 같은 처리)
    private const string DisallowGpuDrivenTypeName = "UnityEngine.Rendering.DisallowGPUDrivenRendering, Unity.RenderPipelines.GPUDriven.Runtime";

    [MenuItem("Tools/Map/8. 북쪽 나무·바위·절벽 배치")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        ScatterContext context = CreateContext(terrain);
        if (context == null) return;

        PlaceLandmarks(context);
        PlaceCliffs(context);
        PlaceBigRocks(context);
        BandStats stats = ScatterTreesAndRocks(context);

        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        LogResult(context, stats);
    }

    // ---------- 준비 ----------

    private static ScatterContext CreateContext(Terrain terrain)
    {
        Transform[] regionRoots = FindRegionRoots(terrain);
        if (regionRoots == null) return null;

        Transform lake = regionRoots[MiddleRegionIndex].Find(LakeParentName);
        MeshFilter lakeFilter = lake != null ? lake.GetComponentInChildren<MeshFilter>() : null;
        if (lakeFilter == null)
        {
            Debug.LogError("North/Lake 아래에 호수 수면이 없습니다. 먼저 'Tools/Map/6. 호수 만들기'를 실행하세요.");
            return null;
        }

        System.Type disallowType = System.Type.GetType(DisallowGpuDrivenTypeName);
        if (disallowType == null)
        {
            Debug.LogError($"{DisallowGpuDrivenTypeName} 타입을 찾지 못했습니다. URP 패키지 버전이 바뀌었는지 확인하세요.");
            return null;
        }

        var context = new ScatterContext
        {
            Terrain = terrain,
            DisallowType = disallowType,
            NorthArea = MapGrid.GetRangeBounds(NorthFromCell, NorthToCell),
            MiddleColumn = MapGrid.GetRangeBounds(MiddleCell, MiddleCell),
            Parents = CreateParents(regionRoots),
            Water = CreateWaterMask(terrain, lakeFilter)
        };
        context.Samples = CreateSamples(context);
        return context;
    }

    private static Transform[] FindRegionRoots(Terrain terrain)
    {
        GameObject[] roots = terrain.gameObject.scene.GetRootGameObjects();
        var regionRoots = new Transform[RegionNames.Length];
        for (int i = 0; i < RegionNames.Length; i++)
        {
            GameObject root = roots.FirstOrDefault(candidate => candidate.name == RegionNames[i]);
            if (root == null)
            {
                Debug.LogError($"씬 루트에 {RegionNames[i]}가 없습니다. 먼저 'Tools/Map/1. 지형과 구역 부모 생성'을 실행하세요.");
                return null;
            }
            regionRoots[i] = root.transform;
        }
        return regionRoots;
    }

    private static Transform[,] CreateParents(Transform[] regionRoots)
    {
        var parents = new Transform[regionRoots.Length, KindParentNames.Length];
        for (int region = 0; region < regionRoots.Length; region++)
        {
            for (int kind = 0; kind < KindParentNames.Length; kind++)
                parents[region, kind] = ResetParent(regionRoots[region], KindParentNames[kind]);
        }
        return parents;
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

    // 강은 메뉴 7과 같은 경로, 호수는 메뉴 6이 놓은 수면 메시의 정점으로 물 위치를 잡는다
    private static WaterMask CreateWaterMask(Terrain terrain, MeshFilter lakeFilter)
    {
        TerrainData data = terrain.terrainData;
        List<Vector2> path = MapRiverBuilder.GetPath(terrain);
        List<Vector2> lakePoints = lakeFilter.sharedMesh.vertices
            .Select(vertex => lakeFilter.transform.TransformPoint(vertex))
            .Select(point => new Vector2(point.x, point.z))
            .ToList();

        var mask = new WaterMask
        {
            RiverPath = path,
            RiverBounds = GetBounds(path),
            LakeBounds = GetBounds(lakePoints),
            LakeVertexSpacing = data.size.x / (data.heightmapResolution - 1)
        };
        foreach (Vector2 point in lakePoints)
        {
            Vector2Int key = GetLakeHashKey(point);
            if (!mask.LakeVertices.TryGetValue(key, out List<Vector2> list))
            {
                list = new List<Vector2>();
                mask.LakeVertices[key] = list;
            }
            list.Add(point);
        }
        return mask;
    }

    private static List<Sample> CreateSamples(ScatterContext context)
    {
        Rect area = context.NorthArea;
        int columns = Mathf.RoundToInt(area.width / ScatterCellSize);
        int rows = Mathf.RoundToInt(area.height / ScatterCellSize);
        var samples = new List<Sample>(columns * rows);
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                var position = new Vector2(area.xMin + (x + 0.5f) * ScatterCellSize, area.yMin + (z + 0.5f) * ScatterCellSize);
                float height = GetGroundHeight(context.Terrain, position);
                samples.Add(new Sample
                {
                    Position = position,
                    Height = height,
                    Slope = GetSlope(context.Terrain, position),
                    Convexity = height - GetProbeAverage(context.Terrain, position),
                    Band = GetBand(position, height),
                    IsEligible = IsInScatterArea(context, position, height)
                });
            }
        }
        return samples;
    }

    private static float GetProbeAverage(Terrain terrain, Vector2 position)
    {
        Vector2[] directions = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        return directions.Average(direction => GetGroundHeight(terrain, position + direction * RidgeProbeDistance));
    }

    public static HeightBand GetBand(Vector2 position, float height)
    {
        float noise = Mathf.PerlinNoise(position.x / BandJitterPeriod + BandNoiseOffset.x, position.y / BandJitterPeriod + BandNoiseOffset.y);
        float jittered = height + (noise * 2f - 1f) * BandJitter;
        if (jittered >= SummitMinHeight) return HeightBand.Summit;
        if (jittered >= FoothillMaxHeight) return HeightBand.Mid;
        return HeightBand.Foothill;
    }

    // 북쪽 줄 안이고, A1, C1이면 산이 끝난 바깥 평지가 아닌 곳
    private static bool IsInScatterArea(ScatterContext context, Vector2 position, float height)
    {
        if (!context.NorthArea.Contains(position)) return false;
        if (GetRegionIndex(context, position) == MiddleRegionIndex) return true;
        return height >= MapTerrainSetup.BaseHeight + OuterFlatMaxRise;
    }

    private static int GetRegionIndex(ScatterContext context, Vector2 position)
    {
        if (position.x < context.MiddleColumn.xMin) return WestRegionIndex;
        if (position.x >= context.MiddleColumn.xMax) return EastRegionIndex;
        return MiddleRegionIndex;
    }

    // ---------- 룬 돌 ----------

    // 정상부, 정상 양옆 능선 고개, 호숫가. 산을 오르내리는 길목과 멀리서 보이는 곳에 하나씩 둔다
    private static void PlaceLandmarks(ScatterContext context)
    {
        var random = new System.Random(LandmarkSeed);
        GameObject[] prefabs = LoadPrefabs(LandmarkPaths);
        Sample summit = context.Samples.Where(sample => sample.IsEligible).OrderByDescending(sample => sample.Height).First();

        LandmarkSpot[] spots =
        {
            new LandmarkSpot { Label = "정상부", Candidates = FindGentleSpots(context, summit.Position, SummitLandmarkSearchRadius) },
            new LandmarkSpot { Label = "서쪽 능선 고개", Candidates = FindGentleSpots(context, FindPass(context, summit.Position.x, -1f), PassLandmarkSearchRadius) },
            new LandmarkSpot { Label = "동쪽 능선 고개", Candidates = FindGentleSpots(context, FindPass(context, summit.Position.x, 1f), PassLandmarkSearchRadius) },
            new LandmarkSpot { Label = "서쪽 호숫가", Candidates = FindLakeShoreSpots(context) }
        };
        foreach (LandmarkSpot spot in spots)
            TryPlaceLandmark(context, random, prefabs, spot);
    }

    // 무작위 값은 자리를 찾았는지와 상관없이 같은 순서로 뽑는다
    private static void TryPlaceLandmark(ScatterContext context, System.Random random, GameObject[] prefabs, LandmarkSpot spot)
    {
        GameObject prefab = prefabs[random.Next(prefabs.Length)];
        float yaw = RandomRange(random, 0f, 360f);
        float radius = GetShape(context, prefab).FootprintRadius;
        foreach (Vector2 position in spot.Candidates)
        {
            if (!CanPlaceLarge(context, position, radius, LandmarkRule)) continue;

            var placement = new Placement { Prefab = prefab, Position = position, Yaw = yaw, Scale = 1f, Kind = PlacementKind.Landmark };
            GameObject instance = PlaceGrounded(context, placement, LandmarkRule.SinkRatio);
            context.Blockers.Add(new Blocker { Center = position, Radius = radius + LandmarkClearance });
            context.LandmarkNotes.Add($"{spot.Label} {prefab.name} ({position.x:F0}, {position.y:F0}) 높이 {instance.transform.position.y:F0}m " +
                                      $"경사 {GetSlope(context.Terrain, position):F0}도");
            return;
        }
        Debug.LogWarning($"[MapNorthScatter] 룬 돌 '{spot.Label}' 자리를 찾지 못했습니다.");
    }

    // center에서 radius 안의 칸 중심을 경사가 완만한 순서로
    private static IEnumerable<Vector2> FindGentleSpots(ScatterContext context, Vector2 center, float radius)
    {
        return context.Samples
            .Where(sample => sample.IsEligible && Vector2.Distance(sample.Position, center) <= radius)
            .OrderBy(sample => sample.Slope)
            .Select(sample => sample.Position);
    }

    // 능선 마루 = 같은 열(x)에서 가장 높은 칸. 정상과 옆 봉우리 사이에서 마루가 가장 낮은 열의 마루가 고개다
    private static Vector2 FindPass(ScatterContext context, float peakX, float side)
    {
        float xMin = Mathf.Min(peakX + side * PassSearchMin, peakX + side * PassSearchMax);
        float xMax = Mathf.Max(peakX + side * PassSearchMin, peakX + side * PassSearchMax);
        return context.Samples
            .Where(sample => sample.IsEligible && sample.Position.x >= xMin && sample.Position.x <= xMax)
            .GroupBy(sample => sample.Position.x)
            .Select(column => column.OrderByDescending(sample => sample.Height).First())
            .OrderBy(crest => crest.Height)
            .First().Position;
    }

    // 호수 중심에서 LakeLandmarkDirection으로 나가며 물 둘레를 벗어난 첫 자리를 찾는다
    private static IEnumerable<Vector2> FindLakeShoreSpots(ScatterContext context)
    {
        Vector2 center = context.Water.LakeBounds.center;
        for (float distance = 0f; distance <= LakeLandmarkSearchMax; distance += RaySearchStep)
            yield return center + LakeLandmarkDirection * distance;
    }

    // ---------- 절벽 ----------

    private static void PlaceCliffs(ScatterContext context)
    {
        var random = new System.Random(CliffSeed);
        PrefabShape[] shapes = LoadPrefabs(CliffPaths).Select(prefab => GetShape(context, prefab)).ToArray();
        foreach (PrefabShape shape in shapes)
            shape.FaceAxis = FindCliffFace(context, shape);
        PlaceLakeWallCliffs(context, random, shapes);
        PlaceSteepCliffs(context, random, shapes);
    }

    // 앞면(바위 벽)은 가파른 면이 많은 쪽으로 본다. 수평으로 가장 얇은 축(절벽 깊이)의 +, - 쪽 중
    // 가파른 면 넓이가 많은 쪽을 고른다. 뒷면은 비탈에 묻히도록 비스듬하거나 단순한 면이라는 가정이다 (미검증)
    private static Vector3 FindCliffFace(ScatterContext context, PrefabShape shape)
    {
        Vector3 size = shape.LocalBounds.size;
        Vector3 thinAxis = size.x <= size.z ? Vector3.right : Vector3.forward;
        string axisName = size.x <= size.z ? "x" : "z";
        Vector3 fallback = thinAxis * CliffFaceSign;

        // 첫 MeshFilter가 LOD0이다
        MeshFilter filter = shape.Prefab.GetComponentInChildren<MeshFilter>();
        Vector3[] vertices = filter.sharedMesh.vertices;
        int[] triangles = filter.sharedMesh.triangles;
        if (vertices.Length == 0)
        {
            context.CliffFaceNotes.Add($"{shape.Prefab.name} : 메시를 읽지 못해 얇은 축 {axisName}의 {CliffFaceSign:+0;-0} 방향을 앞면으로 둠 (미검증)");
            return fallback;
        }

        Matrix4x4 toRoot = shape.Prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
        float plusArea = 0f;
        float minusArea = 0f;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = toRoot.MultiplyPoint3x4(vertices[triangles[i]]);
            Vector3 b = toRoot.MultiplyPoint3x4(vertices[triangles[i + 1]]);
            Vector3 c = toRoot.MultiplyPoint3x4(vertices[triangles[i + 2]]);
            // 크기는 삼각형 넓이의 2배, 방향은 면의 바깥쪽
            Vector3 cross = Vector3.Cross(b - a, c - a);
            Vector3 normal = cross.normalized;
            if (Mathf.Abs(normal.y) >= CliffSteepNormalY) continue;

            float facing = Vector3.Dot(normal, thinAxis) * cross.magnitude * 0.5f;
            if (facing > 0f) plusArea += facing;
            else minusArea -= facing;
        }

        float ratio = Mathf.Max(plusArea, minusArea) / Mathf.Max(Mathf.Min(plusArea, minusArea), 0.001f);
        if (ratio < CliffFaceMinRatio)
        {
            context.CliffFaceNotes.Add($"{shape.Prefab.name} : 얇은 축 {axisName} 가파른 면 넓이 +{plusArea:F0} / -{minusArea:F0}m² (비 {ratio:F2})로 구분이 안 돼 " +
                                       $"{CliffFaceSign:+0;-0} 방향을 앞면으로 둠 (미검증)");
            return fallback;
        }

        float sign = plusArea > minusArea ? 1f : -1f;
        context.CliffFaceNotes.Add($"{shape.Prefab.name} : 얇은 축 {axisName} 가파른 면 넓이 +{plusArea:F0} / -{minusArea:F0}m² (비 {ratio:F2}) → {sign:+0;-0} 방향을 앞면으로 둠");
        return thinAxis * sign;
    }

    // (다) 호수 뒤(북쪽) 깎인 벽. 방향마다 호수에서 나가며, 조건을 맞추는 자리 중 가장 가파른 곳에 놓는다
    private static void PlaceLakeWallCliffs(ScatterContext context, System.Random random, PrefabShape[] shapes)
    {
        Vector2 lakeCenter = context.Water.LakeBounds.center;
        for (int i = 0; i < LakeWallCliffCount; i++)
        {
            CliffDraw draw = DrawCliff(random, shapes);
            float angle = Mathf.Lerp(-LakeWallArcHalfAngle, LakeWallArcHalfAngle, (i + 0.5f) / LakeWallCliffCount) * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));

            CliffFit? best = null;
            for (float distance = LakeWallSearchMin; distance <= LakeWallSearchMax; distance += RaySearchStep)
            {
                Vector2 position = lakeCenter + direction * distance;
                if (best != null && GetSlope(context.Terrain, position) <= best.Value.Slope) continue;
                if (TryFitCliff(context, draw, position, out CliffFit fit)) best = fit;
            }

            if (best != null)
            {
                PlaceCliff(context, draw.Shape, best.Value, "호수 뒤 벽");
                continue;
            }
            context.CliffFitFailCount++;
            Debug.LogWarning($"[MapNorthScatter] 호수 뒤 벽 절벽 {i + 1}번 자리를 찾지 못했습니다.");
        }
    }

    // (가) 나무 기준 경사를 넘는 가파른 비탈. 섞은 순서로 돌며 다른 절벽과 떨어지고 조건을 맞추는 자리에 놓는다
    private static void PlaceSteepCliffs(ScatterContext context, System.Random random, PrefabShape[] shapes)
    {
        List<Sample> candidates = context.Samples
            .Where(sample => sample.IsEligible && sample.Slope > CliffSlopeMin && sample.Slope <= CliffSlopeMax)
            .ToList();
        Shuffle(candidates, random);
        int placedCount = 0;
        foreach (Sample candidate in candidates)
        {
            if (placedCount >= SteepCliffCount) break;

            CliffDraw draw = DrawCliff(random, shapes);
            if (!CanPlaceCliff(context, candidate.Position, draw.Shape.FootprintRadius * draw.Scale)) continue;
            if (!TryFitCliff(context, draw, candidate.Position, out CliffFit fit))
            {
                context.CliffFitFailCount++;
                continue;
            }

            PlaceCliff(context, draw.Shape, fit, "가파른 비탈");
            placedCount++;
        }
        if (placedCount < SteepCliffCount) Debug.LogWarning($"[MapNorthScatter] 가파른 비탈 절벽을 {placedCount}/{SteepCliffCount}개만 놓았습니다.");
    }

    private static CliffDraw DrawCliff(System.Random random, PrefabShape[] shapes)
    {
        return new CliffDraw
        {
            Shape = shapes[random.Next(shapes.Length)],
            Scale = RandomRange(random, CliffScaleMin, CliffScaleMax),
            YawJitter = RandomRange(random, -CliffYawJitter, CliffYawJitter)
        };
    }

    private static bool CanPlaceCliff(ScatterContext context, Vector2 position, float radius)
    {
        if (context.Cliffs.Any(cliff => Vector2.Distance(cliff.Center, position) < CliffMinSpacing)) return false;
        return CanPlaceLarge(context, position, radius, CliffRule);
    }

    // 경사가 범위 안이면, 처음 배율부터 CliffScaleMax까지 키워 가며 윗면 돌출과 뜸 조건을 맞추는 배율을 찾는다
    private static bool TryFitCliff(ScatterContext context, CliffDraw draw, Vector2 position, out CliffFit fit)
    {
        fit = default;
        float slope = GetSlope(context.Terrain, position);
        if (slope <= CliffSlopeMin || slope > CliffSlopeMax) return false;

        float yaw = GetCliffYaw(context.Terrain, draw, position);
        float scale = draw.Scale;
        while (CanPlaceCliff(context, position, draw.Shape.FootprintRadius * scale))
        {
            fit = MeasureCliff(context, draw.Shape, position, yaw, scale);
            fit.Slope = slope;
            if (fit.Exposure >= CliffMinExposure && !fit.IsFloating) return true;
            if (scale >= CliffScaleMax) break;
            scale = Mathf.Min(scale + CliffScaleStep, CliffScaleMax);
        }
        return false;
    }

    // 밑동을 앞쪽(비탈 아래) 끝 땅에 맞춰 CliffBaseSink만 박았을 때, 윗면이 바닥 범위 안 가장 높은 땅보다 얼마나 솟는지 잰다.
    // 비탈에서는 뒤쪽 끝 땅이 (절벽 깊이 × tan(경사))만큼 높아 이 값만큼 윗면 돌출이 줄어든다
    private static CliffFit MeasureCliff(ScatterContext context, PrefabShape shape, Vector2 position, float yaw, float scale)
    {
        Bounds bounds = shape.LocalBounds;
        Vector3 sideAxis = Mathf.Abs(shape.FaceAxis.x) > 0f ? Vector3.forward : Vector3.right;
        Vector3 sideExtent = Vector3.Scale(sideAxis, bounds.extents);
        Vector3 faceExtent = Vector3.Scale(shape.FaceAxis, bounds.extents);
        var bottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        Matrix4x4 toWorld = Matrix4x4.TRS(new Vector3(position.x, 0f, position.y), Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale);

        float frontMin = float.MaxValue;
        float frontMax = float.MinValue;
        float groundMin = float.MaxValue;
        float groundMax = float.MinValue;
        for (int depth = 0; depth < CliffDepthSamples; depth++)
        {
            // -1 뒤쪽 끝(비탈 위) ~ 1 앞쪽 끝(비탈 아래)
            float depthT = Mathf.Lerp(-1f, 1f, depth / (CliffDepthSamples - 1f));
            for (int width = 0; width < CliffWidthSamples; width++)
            {
                float widthT = Mathf.Lerp(-1f, 1f, width / (CliffWidthSamples - 1f));
                Vector3 point = toWorld.MultiplyPoint3x4(bottomCenter + sideExtent * widthT + faceExtent * depthT);
                float ground = GetGroundHeight(context.Terrain, new Vector2(point.x, point.z));
                groundMin = Mathf.Min(groundMin, ground);
                groundMax = Mathf.Max(groundMax, ground);
                if (depth < CliffDepthSamples - 1) continue;

                frontMin = Mathf.Min(frontMin, ground);
                frontMax = Mathf.Max(frontMax, ground);
            }
        }

        float bottom = frontMin - CliffBaseSink;
        return new CliffFit
        {
            Position = position,
            Yaw = yaw,
            Scale = scale,
            RootHeight = bottom - bounds.min.y * scale,
            SinkMin = frontMin - bottom,
            SinkMax = frontMax - bottom,
            Exposure = bottom + bounds.size.y * scale - groundMax,
            IsFloating = groundMin < bottom
        };
    }

    private static void PlaceCliff(ScatterContext context, PrefabShape shape, CliffFit fit, string label)
    {
        var placement = new Placement
        {
            Prefab = shape.Prefab,
            Position = fit.Position,
            Yaw = fit.Yaw,
            Scale = fit.Scale,
            Kind = PlacementKind.Cliff
        };
        GameObject instance = PlaceAt(context, placement, fit.RootHeight);

        // 렌더러가 LOD 자식마다 있어 렌더러가 있는 오브젝트마다 붙인다 (원본 prefab은 고치지 않는다)
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            renderer.gameObject.AddComponent(context.DisallowType);

        var blocker = new Blocker { Center = fit.Position, Radius = shape.FootprintRadius * fit.Scale };
        context.Blockers.Add(blocker);
        context.Cliffs.Add(blocker);
        context.CliffNotes.Add($"{label} {shape.Prefab.name} ({fit.Position.x:F0}, {fit.Position.y:F0}) 경사 {fit.Slope:F0}도 배율 {fit.Scale:F2} " +
                               $"회전 {fit.Yaw:F0}도 / 앞쪽 땅 대비 밑동 깊이 {fit.SinkMin:F1} ~ {fit.SinkMax:F1}m / " +
                               $"뒤쪽 땅 대비 윗면 돌출 {fit.Exposure:F1}m");
    }

    // 절벽 앞면(FaceAxis)이 비탈 아래쪽을 보도록 돌린다
    private static float GetCliffYaw(Terrain terrain, CliffDraw draw, Vector2 position)
    {
        // 지면 법선의 수평 성분은 비탈 아래쪽을 가리킨다
        Vector3 normal = GetNormal(terrain, position);
        float downhillYaw = Mathf.Atan2(normal.x, normal.z) * Mathf.Rad2Deg;
        Vector3 face = draw.Shape.FaceAxis;
        float faceYaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
        return downhillYaw - faceYaw + draw.YawJitter;
    }

    // ---------- 큰 바위 ----------

    private static void PlaceBigRocks(ScatterContext context)
    {
        var random = new System.Random(BigRockSeed);
        GameObject[] prefabs = LoadPrefabs(BigRockPaths);

        foreach (Blocker cliff in context.Cliffs)
        {
            var cluster = new Cluster
            {
                Center = cliff.Center,
                DistanceMin = cliff.Radius * CliffClusterDistanceMin,
                DistanceMax = cliff.Radius * CliffClusterDistanceMax
            };
            int count = random.Next(CliffClusterRockMin, CliffClusterRockMax + 1);
            for (int i = 0; i < count; i++)
            {
                if (TryPlaceBigRock(context, random, prefabs, cluster)) context.CliffClusterRockCount++;
            }
        }

        foreach (Vector2 center in FindRidgeClusterCenters(context, random))
        {
            var cluster = new Cluster { Center = center, DistanceMin = 0f, DistanceMax = RidgeClusterRadius };
            int count = random.Next(RidgeClusterRockMin, RidgeClusterRockMax + 1);
            for (int i = 0; i < count; i++)
            {
                if (TryPlaceBigRock(context, random, prefabs, cluster)) context.RidgeClusterRockCount++;
            }
        }
    }

    // 중턱 이상에서 볼록한 칸(능선, 봉우리) 중 무리끼리, 절벽과 떨어진 곳
    private static List<Vector2> FindRidgeClusterCenters(ScatterContext context, System.Random random)
    {
        List<Sample> candidates = context.Samples
            .Where(sample => sample.IsEligible && sample.Height >= FoothillMaxHeight && sample.Convexity >= RidgeConvexity)
            .ToList();
        Shuffle(candidates, random);

        var centers = new List<Vector2>();
        foreach (Sample candidate in candidates)
        {
            if (centers.Count >= RidgeClusterCount) break;
            Vector2 position = candidate.Position;
            if (centers.Any(center => Vector2.Distance(center, position) < RidgeClusterSpacing)) continue;
            if (context.Cliffs.Any(cliff => Vector2.Distance(cliff.Center, position) < RidgeClusterSpacing)) continue;

            centers.Add(position);
        }
        return centers;
    }

    private static bool TryPlaceBigRock(ScatterContext context, System.Random random, GameObject[] prefabs, Cluster cluster)
    {
        for (int attempt = 0; attempt < MaxPlaceAttempts; attempt++)
        {
            float angle = RandomRange(random, 0f, Mathf.PI * 2f);
            float distance = RandomRange(random, cluster.DistanceMin, cluster.DistanceMax);
            var placement = new Placement
            {
                Prefab = prefabs[random.Next(prefabs.Length)],
                Position = cluster.Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance,
                Yaw = RandomRange(random, 0f, 360f),
                Scale = RandomRange(random, BigRockScaleMin, BigRockScaleMax),
                Kind = PlacementKind.Rock
            };
            float radius = GetShape(context, placement.Prefab).FootprintRadius * placement.Scale;
            if (!CanPlaceLarge(context, placement.Position, radius, BigRockRule)) continue;

            PlaceGrounded(context, placement, BigRockRule.SinkRatio);
            context.Blockers.Add(new Blocker { Center = placement.Position, Radius = radius });
            return true;
        }
        return false;
    }

    // ---------- 나무, 바위 ----------

    private static BandStats ScatterTreesAndRocks(ScatterContext context)
    {
        context.ScatterRandom = new System.Random(ScatterSeed);
        context.TreePrefabs = new[]
        {
            LoadPrefabs(MapTreeScatter.MainTreePaths), LoadPrefabs(MapTreeScatter.DeadTreePaths), LoadPrefabs(MapTreeScatter.GiantTreePaths)
        };
        context.RockPrefabs = LoadPrefabs(MapTreeScatter.RockPaths.Where(path => path.StartsWith(StonePathPrefix)));

        var stats = new BandStats();
        foreach (Sample sample in context.Samples)
        {
            if (!sample.IsEligible) continue;

            int band = (int)sample.Band;
            stats.CellCounts[band]++;
            if (sample.Slope <= MapTreeScatter.MaxSlope) stats.TreeableCellCounts[band]++;

            Vector2 cellMin = sample.Position - Vector2.one * (ScatterCellSize / 2f);
            // 거대목은 중앙과 같이 숲(빽빽한 기슭)에서만 나온다
            bool canBeGiant = sample.Band == HeightBand.Foothill;
            int treeCount = RollCount(context.ScatterRandom, TreeDensityMin[band], TreeDensityMax[band]);
            for (int i = 0; i < treeCount; i++)
            {
                if (TryAddTree(context, cellMin, canBeGiant)) stats.TreeCounts[band]++;
            }
            int rockCount = RollCount(context.ScatterRandom, RockDensityMin[band], RockDensityMax[band]);
            for (int i = 0; i < rockCount; i++)
            {
                if (TryAddRock(context, cellMin)) stats.RockCounts[band]++;
            }
        }
        return stats;
    }

    // 무작위 값은 조건과 상관없이 같은 순서로 뽑아 결과가 항상 같게 한다
    private static bool TryAddTree(ScatterContext context, Vector2 cellMin, bool canBeGiant)
    {
        System.Random random = context.ScatterRandom;
        Vector2 position = cellMin + new Vector2(RandomRange(random, 0f, ScatterCellSize), RandomRange(random, 0f, ScatterCellSize));
        GameObject[] prefabs = context.TreePrefabs[(int)PickTreeKind(random, canBeGiant)];
        var placement = new Placement
        {
            Prefab = prefabs[random.Next(prefabs.Length)],
            Position = position,
            Scale = RandomRange(random, MapTreeScatter.TreeScaleMin, MapTreeScatter.TreeScaleMax),
            Yaw = RandomRange(random, 0f, 360f),
            Kind = PlacementKind.Tree
        };
        if (!CanPlaceSmall(context, position, MapTreeScatter.MaxSlope)) return false;

        PlaceAt(context, placement, GetGroundHeight(context.Terrain, position));
        return true;
    }

    private static bool TryAddRock(ScatterContext context, Vector2 cellMin)
    {
        System.Random random = context.ScatterRandom;
        Vector2 position = cellMin + new Vector2(RandomRange(random, 0f, ScatterCellSize), RandomRange(random, 0f, ScatterCellSize));
        var placement = new Placement
        {
            Prefab = context.RockPrefabs[random.Next(context.RockPrefabs.Length)],
            Position = position,
            Scale = RandomRange(random, MapTreeScatter.RockScaleMin, MapTreeScatter.RockScaleMax),
            Yaw = RandomRange(random, 0f, 360f),
            Kind = PlacementKind.Rock
        };
        if (!CanPlaceSmall(context, position, RockMaxSlope)) return false;

        PlaceAt(context, placement, GetLowestGround(context.Terrain, position, RockFootprintRadius * placement.Scale));
        return true;
    }

    private static TreeKind PickTreeKind(System.Random random, bool canBeGiant)
    {
        double roll = random.NextDouble();
        if (canBeGiant)
        {
            if (roll < MapTreeScatter.GiantTreeRatio) return TreeKind.Giant;
            roll -= MapTreeScatter.GiantTreeRatio;
        }
        return roll < MapTreeScatter.DeadTreeRatio ? TreeKind.Dead : TreeKind.Main;
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

    // ---------- 놓기 공통 ----------

    private static bool CanPlaceSmall(ScatterContext context, Vector2 position, float maxSlope)
    {
        if (GetSlope(context.Terrain, position) > maxSlope)
        {
            context.SlopeSkipCount++;
            return false;
        }
        if (IsNearWater(context.Water, position, 0f))
        {
            context.WaterSkipCount++;
            return false;
        }
        if (context.Blockers.Any(blocker => Vector2.Distance(blocker.Center, position) < blocker.Radius))
        {
            context.BlockerSkipCount++;
            return false;
        }
        return true;
    }

    // 바닥 반지름 radius까지 물 둘레를 피하고, 먼저 놓은 큰 오브젝트와 rule.OverlapRatio 이상 겹치지 않는 곳
    private static bool CanPlaceLarge(ScatterContext context, Vector2 position, float radius, LargeRule rule)
    {
        if (!IsInScatterArea(context, position, GetGroundHeight(context.Terrain, position))) return false;
        if (GetSlope(context.Terrain, position) > rule.MaxSlope) return false;
        if (IsNearWater(context.Water, position, radius)) return false;
        return !context.Blockers.Any(blocker => Vector2.Distance(blocker.Center, position) < (blocker.Radius + radius) * rule.OverlapRatio);
    }

    // prefab 연결을 유지한 채 놓는다. Static 플래그는 켜지 않는다 (GPU Resident Drawer로 그림)
    private static GameObject PlaceAt(ScatterContext context, Placement placement, float groundHeight)
    {
        int region = GetRegionIndex(context, placement.Position);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(placement.Prefab, context.Parents[region, (int)placement.Kind]);
        instance.transform.SetPositionAndRotation(
            new Vector3(placement.Position.x, groundHeight, placement.Position.y),
            Quaternion.Euler(0f, placement.Yaw, 0f));
        instance.transform.localScale = Vector3.one * placement.Scale;
        if (MapTreeScatter.AddMissingCollider(instance, placement.Prefab.name)) context.ColliderCount++;
        return instance;
    }

    // 큰 오브젝트는 바닥 네 모서리와 가운데 중 가장 낮은 땅보다 (높이 × sinkRatio)만큼 아래에 밑면을 맞춘다.
    // 비탈 아래쪽 모서리가 뜨지 않고, 위쪽은 비탈 속에 묻힌다
    private static GameObject PlaceGrounded(ScatterContext context, Placement placement, float sinkRatio)
    {
        PrefabShape shape = GetShape(context, placement.Prefab);
        GameObject instance = PlaceAt(context, placement, 0f);
        Vector3[] corners = GetWorldCorners(instance.transform, shape.LocalBounds);

        float lowestGround = corners
            .Select(corner => GetGroundHeight(context.Terrain, new Vector2(corner.x, corner.z)))
            .Append(GetGroundHeight(context.Terrain, placement.Position))
            .Min();
        float bottom = corners.Min(corner => corner.y);
        float height = corners.Max(corner => corner.y) - bottom;
        instance.transform.position += Vector3.up * (lowestGround - height * sinkRatio - bottom);
        return instance;
    }

    private static PrefabShape GetShape(ScatterContext context, GameObject prefab)
    {
        if (context.Shapes.TryGetValue(prefab, out PrefabShape shape)) return shape;

        // 메시 범위를 prefab 루트 기준으로 모은다 (LOD 자식의 위치, 회전, 크기를 반영)
        Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;
        var points = new List<Vector3>();
        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>())
        {
            Matrix4x4 toRoot = rootInverse * filter.transform.localToWorldMatrix;
            points.AddRange(GetCorners(filter.sharedMesh.bounds).Select(corner => toRoot.MultiplyPoint3x4(corner)));
        }
        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (Vector3 point in points)
            bounds.Encapsulate(point);

        shape = new PrefabShape
        {
            Prefab = prefab,
            LocalBounds = bounds,
            FootprintRadius = GetCorners(bounds).Max(corner => new Vector2(corner.x, corner.z).magnitude)
        };
        context.Shapes[prefab] = shape;
        return shape;
    }

    // ---------- 물 ----------

    // 강은 물가 밖 MapTreeScatter.RiverClearMargin, 호수는 LakeClearMargin에 바닥 반지름 radius를 더한 거리 안이면 true
    private static bool IsNearWater(WaterMask water, Vector2 position, float radius)
    {
        return IsNearRiver(water, position, MapTreeScatter.RiverClearMargin + radius) || IsNearLake(water, position, LakeClearMargin + radius);
    }

    private static bool IsNearRiver(WaterMask water, Vector2 position, float margin)
    {
        if (!Expand(water.RiverBounds, margin + RiverBoundsPadding).Contains(position)) return false;
        return MapRiverBuilder.IsNearRiver(water.RiverPath, new Vector3(position.x, 0f, position.y), margin);
    }

    // 수면 메시 정점 중 하나라도 distance 안에 있으면 true
    private static bool IsNearLake(WaterMask water, Vector2 position, float distance)
    {
        if (!Expand(water.LakeBounds, distance).Contains(position)) return false;

        Vector2Int key = GetLakeHashKey(position);
        int range = Mathf.CeilToInt(distance / LakeHashCellSize);
        float sqrDistance = distance * distance;
        for (int z = key.y - range; z <= key.y + range; z++)
        {
            for (int x = key.x - range; x <= key.x + range; x++)
            {
                if (!water.LakeVertices.TryGetValue(new Vector2Int(x, z), out List<Vector2> vertices)) continue;
                if (vertices.Any(vertex => (vertex - position).sqrMagnitude <= sqrDistance)) return true;
            }
        }
        return false;
    }

    // 여유 없이 물 위인지 (강 물 폭 안, 또는 호수 수면 메시 위)
    private static bool IsOverWater(WaterMask water, Vector2 position)
    {
        return IsNearRiver(water, position, 0f) || IsNearLake(water, position, water.LakeVertexSpacing);
    }

    private static Vector2Int GetLakeHashKey(Vector2 position)
    {
        return new Vector2Int(Mathf.FloorToInt(position.x / LakeHashCellSize), Mathf.FloorToInt(position.y / LakeHashCellSize));
    }

    // ---------- 지형, 계산 ----------

    // SampleHeight는 Terrain 기준 높이라 Terrain의 y를 더한다
    private static float GetGroundHeight(Terrain terrain, Vector2 position)
    {
        return terrain.SampleHeight(new Vector3(position.x, 0f, position.y)) + terrain.transform.position.y;
    }

    private static float GetLowestGround(Terrain terrain, Vector2 position, float radius)
    {
        Vector2[] offsets = { Vector2.zero, Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        return offsets.Min(offset => GetGroundHeight(terrain, position + offset * radius));
    }

    private static float GetSlope(Terrain terrain, Vector2 position)
    {
        Vector2 normalized = ToNormalized(terrain, position);
        return terrain.terrainData.GetSteepness(normalized.x, normalized.y);
    }

    private static Vector3 GetNormal(Terrain terrain, Vector2 position)
    {
        Vector2 normalized = ToNormalized(terrain, position);
        return terrain.terrainData.GetInterpolatedNormal(normalized.x, normalized.y);
    }

    private static Vector2 ToNormalized(Terrain terrain, Vector2 position)
    {
        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        return new Vector2((position.x - origin.x) / size.x, (position.y - origin.z) / size.z);
    }

    private static Vector3[] GetCorners(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        return new[]
        {
            new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z)
        };
    }

    private static Vector3[] GetWorldCorners(Transform transform, Bounds localBounds)
    {
        Matrix4x4 toWorld = transform.localToWorldMatrix;
        return GetCorners(localBounds).Select(corner => toWorld.MultiplyPoint3x4(corner)).ToArray();
    }

    private static Rect GetBounds(IEnumerable<Vector2> points)
    {
        List<Vector2> list = points.ToList();
        Vector2 min = list.Aggregate(Vector2.Min);
        Vector2 max = list.Aggregate(Vector2.Max);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static Rect Expand(Rect rect, float amount)
    {
        return Rect.MinMaxRect(rect.xMin - amount, rect.yMin - amount, rect.xMax + amount, rect.yMax + amount);
    }

    private static GameObject[] LoadPrefabs(IEnumerable<string> paths)
    {
        return paths
            .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + path + ".prefab"))
            .ToArray();
    }

    private static void Shuffle<T>(List<T> list, System.Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static float RandomRange(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    // ---------- 로그 ----------

    // 검수(높이대별 밀도가 표 범위 안인지, 큰 오브젝트 수와 위치, 물 위 배치 0개, 성능 측정용 배치 수)에 쓰는 값이라 로그를 남긴다
    private static void LogResult(ScatterContext context, BandStats stats)
    {
        LogBands(stats);
        Debug.Log($"[MapNorthScatter] 나무·바위 건너뜀 : 경사 {context.SlopeSkipCount}개, 물 둘레 {context.WaterSkipCount}개, " +
                  $"큰 오브젝트 자리 {context.BlockerSkipCount}개. 충돌체 추가 {context.ColliderCount}개");
        LogLargeObjects(context);
        LogWaterCheck(context);
        LogRegionTotals(context);
    }

    // 나무 밀도는 나무를 놓을 수 있는 면적(경사 기준 이하) 기준으로도 함께 본다 (가파른 곳은 규칙상 비어 있다)
    private static void LogBands(BandStats stats)
    {
        float cellArea = ScatterCellSize * ScatterCellSize / HectareArea;
        for (int band = 0; band < BandLabels.Length; band++)
        {
            float area = stats.CellCounts[band] * cellArea;
            float treeableArea = stats.TreeableCellCounts[band] * cellArea;
            float treeDensity = treeableArea > 0f ? stats.TreeCounts[band] / treeableArea : 0f;
            float rockDensity = area > 0f ? stats.RockCounts[band] / area : 0f;
            Debug.Log($"[MapNorthScatter] {BandLabels[band]} : 면적 {area:F1}ha (나무 가능 {treeableArea:F1}ha) / " +
                      $"나무 {stats.TreeCounts[band]}그루, 전체 면적 기준 {(area > 0f ? stats.TreeCounts[band] / area : 0f):F1}/ha, " +
                      $"나무 가능 면적 기준 {treeDensity:F1}/ha (표 {TreeDensityMin[band]} ~ {TreeDensityMax[band]}, {GetRangeLabel(treeDensity, TreeDensityMin[band], TreeDensityMax[band])}) / " +
                      $"바위 {stats.RockCounts[band]}개, {rockDensity:F1}/ha (표 {RockDensityMin[band]} ~ {RockDensityMax[band]}, {GetRangeLabel(rockDensity, RockDensityMin[band], RockDensityMax[band])})");
        }
        Debug.Log($"[MapNorthScatter] 높이대 경계 : 기슭 ~ {FoothillMaxHeight}m, 정상 부근 {SummitMinHeight}m ~ (± {BandJitter}m 흔듦). " +
                  $"A1, C1은 바탕 + {OuterFlatMaxRise}m 미만 비움");
    }

    private static string GetRangeLabel(float value, float min, float max)
    {
        return value >= min && value <= max ? "범위 안" : "범위 밖";
    }

    private static void LogLargeObjects(ScatterContext context)
    {
        int bigRockCount = context.CliffClusterRockCount + context.RidgeClusterRockCount;
        Debug.Log($"[MapNorthScatter] 큰 바위 {bigRockCount}개 (절벽 둘레 {context.CliffClusterRockCount}, 능선·정상부 {context.RidgeClusterRockCount})");
        if (bigRockCount < BigRockTargetMin || bigRockCount > BigRockTargetMax)
            Debug.LogWarning($"[MapNorthScatter] 큰 바위 수가 승인 범위 {BigRockTargetMin} ~ {BigRockTargetMax}를 벗어났습니다.");

        Debug.Log($"[MapNorthScatter] 절벽 앞면 축 :\n{string.Join("\n", context.CliffFaceNotes)}");
        Debug.Log($"[MapNorthScatter] 절벽 {context.CliffNotes.Count}개 (경사 {CliffSlopeMin} ~ {CliffSlopeMax}도, 밑동 {CliffBaseSink}m 박기, " +
                  $"윗면 돌출 {CliffMinExposure}m 이상) :\n{string.Join("\n", context.CliffNotes)}");
        Debug.Log($"[MapNorthScatter] 절벽 조건 못 맞춤 {context.CliffFitFailCount}개 " +
                  "(가파른 비탈 후보 중 최대 배율에서도 윗면 돌출, 뜸 조건 탈락 + 자리를 못 찾은 호수 뒤 벽 방향)");
        Debug.Log($"[MapNorthScatter] 룬 돌 {context.LandmarkNotes.Count}개 :\n{string.Join("\n", context.LandmarkNotes)}");
    }

    // 놓인 오브젝트를 다시 읽어 물 위에 있는 것을 센다. 나무는 피벗, 나머지는 피벗과 바닥 네 모서리를 본다. 0이어야 한다
    private static void LogWaterCheck(ScatterContext context)
    {
        int overWaterCount = 0;
        for (int region = 0; region < RegionNames.Length; region++)
        {
            for (int kind = 0; kind < KindParentNames.Length; kind++)
            {
                foreach (Transform child in context.Parents[region, kind])
                {
                    if (IsPlacedOverWater(context, child, (PlacementKind)kind)) overWaterCount++;
                }
            }
        }
        Debug.Log($"[MapNorthScatter] 물 위에 놓인 오브젝트 {overWaterCount}개 (0이어야 한다)");
    }

    private static bool IsPlacedOverWater(ScatterContext context, Transform placed, PlacementKind kind)
    {
        var pivot = new Vector2(placed.position.x, placed.position.z);
        if (IsOverWater(context.Water, pivot)) return true;
        if (kind == PlacementKind.Tree) return false;

        GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(placed.gameObject);
        return GetWorldCorners(placed, GetShape(context, prefab).LocalBounds)
            .Any(corner => IsOverWater(context.Water, new Vector2(corner.x, corner.z)));
    }

    private static void LogRegionTotals(ScatterContext context)
    {
        int total = 0;
        for (int region = 0; region < RegionNames.Length; region++)
        {
            int[] counts = Enumerable.Range(0, KindParentNames.Length).Select(kind => context.Parents[region, kind].childCount).ToArray();
            total += counts.Sum();
            string detail = string.Join(", ", Enumerable.Range(0, KindParentNames.Length).Select(kind => $"{KindParentNames[kind]} {counts[kind]}"));
            Debug.Log($"[MapNorthScatter] {RegionNames[region]} 배치 GameObject {counts.Sum()}개 ({detail})");
        }
        Debug.Log($"[MapNorthScatter] 북쪽 줄 배치 GameObject 총 {total}개");
    }
}
