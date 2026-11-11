using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 메뉴 8-1 : 북쪽 이식 창(메뉴 5-1)에 데모 씬(DemoScene_V2.0)의 오브젝트 배치와 나무를 그대로 옮기고, 창 밖 A1, C1과 룬 돌을 채운다 (이슈 #5 방향 변경, 5단계).
// 1) 데모 씬을 Additive로 열어 읽기만 하고, 저장하지 않고 닫는다 (활성 씬은 Game 그대로).
//    YAML 직접 파싱 대신 씬을 여는 이유 : prefab 오버라이드(머티리얼, 메시 등)와 중첩 구조, 렌더러 범위를 Unity가 해석한 그대로 읽을 수 있다.
// 2) 오브젝트 : 데모 루트 Cliffs, Rocks, Ruins, StoneHenge, Objects, Foliage 아래에서 피벗이 창 안인 것을 같은 prefab으로 놓고 오버라이드를 복사한다.
//    위치 XZ와 회전은 MapDemoTransplant 변환, 높이는 "데모 지면 대비 상대 높이"를 우리 지면에 더한다.
//    물(호수, 강), 등산로에 걸리거나, 지형이 데모와 달라져(남쪽 이어 붙이는 띠, 강·길이 깎은 곳) 뜨거나 묻히는 것은 건너뛴다.
// 3) 룬 돌 : 등산로 경유점 네 곳 옆(길 가장자리에서 3 ~ 10m)에 키 큰 선돌을 하나씩 놓는다.
// 4) 나무 : 데모 Terrain 나무(TreeInstance) 중 창 안의 것을 프로토타입 prefab 인스턴스로 바꿔 놓는다.
// 5) 창 밖 : A1, C1의 창 밖(|x| > 750)에 메뉴 8과 같은 높이대 밀도로 나무, 바위만 흩뿌린다.
// 다시 실행하면 NorthWest, North, NorthEast 아래 Demo, Trees, Rocks, Landmarks의 자식을 지우고 같은 결과로 다시 만들며, 옛 Cliffs(메뉴 8 잔여물)는 지운다.
// 메뉴 5-1 → 6 → 7 → 7-1 다음에 실행한다 (지형, 호수 수면, 강 경로, 등산로를 읽는다). 이 메뉴는 메뉴 8을 대신한다.
public static class MapDemoObjectTransplant
{
    private enum SkipReason
    {
        Lake = 0,
        River = 1,
        Trail = 2,
        Height = 3,
        Landmark = 4,
        Inactive = 5,
        NoRenderer = 6,
        SceneMesh = 7,
        Excluded = 8
    }

    private static readonly string[] SkipLabels = { "호수", "강", "길", "높이 이상", "룬 돌 빈터", "비활성", "렌더러 없음", "씬 내부 메시", "제외 prefab" };

    // 나무 종류 순서는 MapTreeScatter 경로 묶음과 같다
    private enum TreeKind
    {
        Main = 0,
        Dead = 1,
        Giant = 2
    }

    // 먼저 놓은 오브젝트가 차지한 자리 (우리 XZ)
    private struct Blocker
    {
        public Vector2 Center;
        public float Radius;
    }

    // 데모 오브젝트 하나의 바닥 (모두 우리 XZ). Points[0]이 피벗, 나머지는 렌더러 범위 둘레 8곳
    private struct Footprint
    {
        public Vector2 Center;
        public float Radius;
        public Vector2[] Points;
        // Points와 같은 순서. 데모 지면 대비 우리 지면이 오른 양 (m)
        public float[] GroundChanges;
    }

    private class RootStats
    {
        public string Name;
        public int InWindowCount;
        public int PlacedCount;
        public int[] SkipCounts = new int[SkipLabels.Length];
        public int DisallowCount;
        public int ClonedCount;
        public int NestedCount;
        public int StructuralOverrideCount;
        public int DroppedReferenceCount;
    }

    private class TreeStats
    {
        public int InWindowCount;
        public int PlacedCount;
        public int[] SkipCounts = new int[SkipLabels.Length];
        public int DisallowCount;
        public int[] PrototypeInWindowCounts;
        public int[] PrototypePlacedCounts;
        // 데모 씬을 닫으면 데모 Terrain을 읽을 수 없으므로, 로그에 쓸 프로토타입 이름을 열려 있을 때 담아 둔다
        public string[] PrototypeLabels;
    }

    private class OutsideStats
    {
        public int CellCount;
        public int TreeCount;
        public int RockCount;
        public int SlopeSkipCount;
        public int WaterSkipCount;
        public int BlockerSkipCount;
    }

    private class LakeWater
    {
        // 호수 수면 메시 정점(월드 XZ)을 LakeHashCellSize 칸으로 나눠 담는다
        public Dictionary<Vector2Int, List<Vector2>> Vertices = new Dictionary<Vector2Int, List<Vector2>>();
        public Rect Bounds;
        // 수면 메시 정점 간격 (= 높이맵 간격)
        public float Spacing;
        public float Surface;
    }

    private class TransplantContext
    {
        public Terrain Terrain;
        public Terrain DemoTerrain;
        public System.Type DisallowType;
        public Rect MiddleColumn;
        public List<Vector2> RiverPath;
        public Rect RiverBounds;
        public List<List<Vector2>> Trails;
        public Rect TrailBounds;
        public LakeWater Lake;
        // [구역, 데모 루트] 부모. 구역 순서는 MapNorthScatter.RegionNames, 루트 순서는 DemoRootNames
        public Transform[,] DemoParents;
        public Transform[] TreeParents;
        public Transform[] RockParents;
        public Transform[] LandmarkParents;
        public List<Blocker> Objects = new List<Blocker>();
        public List<Blocker> Landmarks = new List<Blocker>();
        public Dictionary<Shader, bool> GpuDrivenSupport = new Dictionary<Shader, bool>();
        public Dictionary<string, int> NoColliderCounts = new Dictionary<string, int>();
        public int ColliderCount;
        public List<string> LandmarkNotes = new List<string>();
    }

    private const string DemoScenePath = "Assets/Imports/Polyart/PolyartStudio/DreamscapeMountains/Scenes/DemoScene_V2.0.unity";
    // 가져올 데모 루트. Effects, 조명, 볼륨, Scene(호수 평면 등), 카메라는 가져오지 않는다 (색감, 안개는 7단계)
    private static readonly string[] DemoRootNames = { "Cliffs", "Rocks", "Ruins", "StoneHenge", "Objects", "Foliage" };

    private const string DemoParentName = "Demo";
    private const string TreeParentName = "Trees";
    private const string RockParentName = "Rocks";
    private const string LandmarkParentName = "Landmarks";
    // 메뉴 8이 만든 옛 절벽 부모. 데모 절벽은 Demo/Cliffs에 두므로 지운다
    private const string OldCliffParentName = "Cliffs";
    // 호수 수면은 메뉴 6이 North/Lake 아래에 놓는다
    private const string LakeRegionName = "North";
    private const string LakeParentName = "Lake";
    private const float LakeHashCellSize = 5f;

    // 오브젝트 물, 길 피하기 (임시값). 강은 중심선에서 (물 반폭 + 이 값 + 오브젝트 반지름), 길은 (길 반폭 + 이 값 + 오브젝트 반지름) 안이면 놓지 않는다.
    // 오브젝트 반지름은 렌더러 범위(AABB)의 큰 쪽 반폭이라 실제보다 크게 잡힌다
    private const float ObjectRiverMargin = 2f;
    private const float ObjectTrailMargin = 1f;
    // 호수와 높이 이상은 렌더러 범위 둘레 8곳을 본다. AABB는 돌린 오브젝트보다 커서 이 비율만큼 안쪽을 본다 (임시값)
    private const float FootprintInset = 0.7f;
    private static readonly Vector2[] RingOffsets =
    {
        new Vector2(-1f, -1f), new Vector2(0f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 0f),
        new Vector2(1f, 0f), new Vector2(-1f, 1f), new Vector2(0f, 1f), new Vector2(1f, 1f)
    };
    // 높이 이상 (임시값) : 피벗 기준으로 옮겼을 때 둘레 한 곳이라도 지면이 이만큼 더 내려가 있으면 뜨고, 이만큼 더 올라와 있으면 묻힌다고 본다.
    // 창 안의 바뀌지 않은 곳도 높이맵 간격 차이(데모 약 0.7m, 우리 약 1.2m)로 보간 오차가 생겨 0으로 두지 않는다
    private const float MaxFloatGap = 1f;
    private const float MaxBuryGap = 3f;

    // 나무 : 물 검사는 피벗과 이 반지름 둘레 4곳을 본다. 지면이 데모보다 TreeChangedHeight 넘게 바뀐 곳에서 경사가 MapTreeScatter.MaxSlope를 넘으면 건너뛴다 (임시값)
    private const float TreeFootprintRadius = 1f;
    private const float TreeChangedHeight = 1f;
    // 쓰지 않기로 한 prefab (렌더러 없음, MapLayout.md "배치할 때 확인할 것")
    private const string ExcludedPrefabPrefix = "Prefab_Mushroom";

    // 룬 돌 (랜드마크). MapNorthScatter와 같은 키 큰 선돌 목록이다
    private static readonly string[] LandmarkPaths =
    {
        "StandingStones/Prefab_RockFormation_01", "StandingStones/Prefab_RockFormation_02", "StandingStones/Prefab_RockFormation_03",
        "StandingStones/Prefab_RockFormation_04", "StandingStones/Prefab_RockFormation_05", "StandingStones/Prefab_RockFormation_06",
        "StandingStones/Prefab_RockFormation_07", "StandingStones/Prefab_RockFormation_09", "StandingStones/Prefab_RockFormation_10",
        "StandingStones/Prefab_RockFormation_11"
    };
    private static readonly string[] LandmarkWaypointNames =
    {
        MapTrailBuilder.WestLakeWaypointName, MapTrailBuilder.WestSummitWaypointName,
        MapTrailBuilder.EastLakeWaypointName, MapTrailBuilder.EastSummitWaypointName
    };
    private const int LandmarkSeed = 20261120;
    // 룬 돌 바닥 가장자리와 길 가장자리 사이 (사용자 결정 3 ~ 10m), 경사 (사용자 결정 25도 이하)
    private const float LandmarkTrailGapMin = 3f;
    private const float LandmarkTrailGapMax = 10f;
    private const float LandmarkMaxSlope = 25f;
    // 경유점에서 이 거리 안을 가까운 순서로 찾는다 (임시값)
    private const float LandmarkSearchRadius = 40f;
    private const float LandmarkSearchStep = 1f;
    private const float LandmarkAngleStep = 10f;
    // 데모 오브젝트와 (두 반지름의 합 × 이 값)보다 가까우면 피한다. 데모 반지름은 AABB라 크게 잡혀 1보다 작게 둔다 (임시값)
    private const float LandmarkOverlapRatio = 0.7f;
    private const float LandmarkSinkRatio = 0.05f;
    // 룬 돌 둘레에 나무를 두지 않아 멀리서도 보이는 빈터가 되게 한다 (MapNorthScatter와 같은 값)
    private const float LandmarkClearance = 8f;

    // 창 밖 채우기. 밀도와 높이대는 MapNorthScatter(메뉴 8)와 같은 값이다 (1ha당 최소, 최대. 순서는 기슭, 중턱, 정상 부근)
    private const int OutsideSeed = 20261121;
    private const float ScatterCellSize = 10f;
    private const float HectareArea = 10000f;
    private static readonly float[] TreeDensityMin = { 30f, 5f, 0f };
    private static readonly float[] TreeDensityMax = { 50f, 15f, 0f };
    private static readonly float[] RockDensityMin = { 1f, 5f, 10f };
    private static readonly float[] RockDensityMax = { 3f, 10f, 20f };
    private const float RockMaxSlope = 50f;
    private const float RockFootprintRadius = 1f;
    // 산에는 그루터기, 속 빈 통나무(Objects/)를 빼고 돌만 쓴다 (메뉴 8과 같음)
    private const string StonePathPrefix = "Stones/";

    // 이 프로젝트에서 GPU Resident Drawer로 그리면 몸체가 안 보이는 것을 직접 확인한 셰이더 (MapLayout.md 성능 기준 확인 결과).
    // 키워드(DOTS_INSTANCING_ON) 선언이나 소스의 DOTS.hlsl 포함 여부로는 가려낼 수 없어 이름으로 판정한다
    // (CliffAuto는 두 가지를 모두 갖췄는데도 몸체가 안 그려졌다). 새로 확인되면 여기에 더한다
    private static readonly string[] GpuDrivenBrokenShaderNames = { "Polyart/Dreamscape CliffAuto", "M_Water_Lake_Amp" };
    private const string DisallowGpuDrivenTypeName = "UnityEngine.Rendering.DisallowGPUDrivenRendering, Unity.RenderPipelines.GPUDriven.Runtime";

    [MenuItem("Tools/Map/8-1. 북쪽 데모 오브젝트·나무 이식")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        // 데모 씬을 열기 전에 준비한다 (열면 FindAnyObjectByType이 데모 Terrain을 찾을 수 있다)
        TransplantContext context = CreateContext(terrain);
        if (context == null) return;

        RootStats[] rootStats = DemoRootNames.Select(name => new RootStats { Name = name }).ToArray();
        var treeStats = new TreeStats();
        Scene demoScene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Additive);
        try
        {
            context.DemoTerrain = FindDemoTerrain(demoScene);
            if (context.DemoTerrain == null) return;

            ResetParents(context);
            TransplantObjects(context, demoScene, rootStats);
            PlaceLandmarks(context);
            TransplantTrees(context, treeStats);
        }
        finally
        {
            // 데모 씬은 읽기만 했다. 저장하지 않고 닫는다
            if (demoScene.isDirty) Debug.LogWarning("[MapDemoObjectTransplant] 데모 씬이 바뀐 상태로 표시되었습니다. 저장하지 않고 닫습니다.");
            EditorSceneManager.CloseScene(demoScene, true);
            EditorUtility.ClearProgressBar();
        }

        OutsideStats outsideStats = FillOutsideWindow(context);
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        LogResult(context, rootStats, treeStats, outsideStats);
    }

    // ---------- 준비 ----------

    private static TransplantContext CreateContext(Terrain terrain)
    {
        if (terrain.gameObject.scene != SceneManager.GetActiveScene())
        {
            Debug.LogError("[MapDemoObjectTransplant] Game 씬을 활성 씬으로 두고 실행하세요.");
            return null;
        }

        System.Type disallowType = System.Type.GetType(DisallowGpuDrivenTypeName);
        if (disallowType == null)
        {
            Debug.LogError($"{DisallowGpuDrivenTypeName} 타입을 찾지 못했습니다. URP 패키지 버전이 바뀌었는지 확인하세요.");
            return null;
        }

        List<List<Vector2>> trails = MapTrailBuilder.GetTrails();
        if (trails == null)
        {
            Debug.LogError("등산로 저장 파일이 없습니다. 먼저 'Tools/Map/7-1. 등산로 만들기'를 실행하세요.");
            return null;
        }

        LakeWater lake = ReadLake(terrain);
        if (lake == null) return null;

        List<Vector2> river = MapRiverBuilder.GetPath(terrain);
        return new TransplantContext
        {
            Terrain = terrain,
            DisallowType = disallowType,
            MiddleColumn = MapGrid.GetRangeBounds(MapNorthScatter.MiddleCell, MapNorthScatter.MiddleCell),
            RiverPath = river,
            RiverBounds = GetBounds(river),
            Trails = trails,
            TrailBounds = GetBounds(trails.SelectMany(points => points)),
            Lake = lake
        };
    }

    // 메뉴 6이 놓은 호수 수면 메시 정점과 수면 높이
    private static LakeWater ReadLake(Terrain terrain)
    {
        GameObject region = terrain.gameObject.scene.GetRootGameObjects().FirstOrDefault(root => root.name == LakeRegionName);
        Transform parent = region != null ? region.transform.Find(LakeParentName) : null;
        MeshFilter filter = parent != null ? parent.GetComponentInChildren<MeshFilter>() : null;
        if (filter == null)
        {
            Debug.LogError("North/Lake 아래에 호수 수면이 없습니다. 먼저 'Tools/Map/6. 호수 만들기'를 실행하세요.");
            return null;
        }

        List<Vector2> points = filter.sharedMesh.vertices
            .Select(vertex => filter.transform.TransformPoint(vertex))
            .Select(point => new Vector2(point.x, point.z))
            .ToList();
        TerrainData data = terrain.terrainData;
        var lake = new LakeWater
        {
            Bounds = GetBounds(points),
            Spacing = data.size.x / (data.heightmapResolution - 1),
            Surface = MapDemoTransplant.GetLakeCenter(terrain).y
        };
        foreach (Vector2 point in points)
        {
            Vector2Int key = GetLakeHashKey(point);
            if (!lake.Vertices.TryGetValue(key, out List<Vector2> list))
            {
                list = new List<Vector2>();
                lake.Vertices[key] = list;
            }
            list.Add(point);
        }
        return lake;
    }

    // MapDemoTransplant는 데모 Main Terrain이 원점에 있다고 보고 변환한다. 다르면 위치가 어긋나므로 알린다
    private static Terrain FindDemoTerrain(Scene demoScene)
    {
        Terrain demoTerrain = demoScene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Terrain>())
            .FirstOrDefault();
        if (demoTerrain == null)
        {
            Debug.LogError($"[MapDemoObjectTransplant] {DemoScenePath}에서 Terrain을 찾지 못했습니다.");
            return null;
        }
        if (demoTerrain.transform.position != Vector3.zero)
            Debug.LogWarning($"[MapDemoObjectTransplant] 데모 Terrain 위치가 {demoTerrain.transform.position}입니다. MapDemoTransplant는 원점으로 보고 변환하므로 위치가 어긋납니다.");
        return demoTerrain;
    }

    // 이 메뉴가 만드는 부모만 비우고 다시 만든다. 호수(North/Lake)와 강은 건드리지 않는다
    private static void ResetParents(TransplantContext context)
    {
        GameObject[] roots = context.Terrain.gameObject.scene.GetRootGameObjects();
        string[] regionNames = MapNorthScatter.RegionNames;
        context.DemoParents = new Transform[regionNames.Length, DemoRootNames.Length];
        context.TreeParents = new Transform[regionNames.Length];
        context.RockParents = new Transform[regionNames.Length];
        context.LandmarkParents = new Transform[regionNames.Length];
        for (int region = 0; region < regionNames.Length; region++)
        {
            GameObject regionRoot = roots.FirstOrDefault(root => root.name == regionNames[region]);
            if (regionRoot == null) regionRoot = new GameObject(regionNames[region]);

            Transform oldCliffs = regionRoot.transform.Find(OldCliffParentName);
            if (oldCliffs != null) Object.DestroyImmediate(oldCliffs.gameObject);

            Transform demo = ResetParent(regionRoot.transform, DemoParentName);
            for (int root = 0; root < DemoRootNames.Length; root++)
                context.DemoParents[region, root] = ResetParent(demo, DemoRootNames[root]);
            context.TreeParents[region] = ResetParent(regionRoot.transform, TreeParentName);
            context.RockParents[region] = ResetParent(regionRoot.transform, RockParentName);
            context.LandmarkParents[region] = ResetParent(regionRoot.transform, LandmarkParentName);
        }
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

    // 구역은 위치 x로 정한다 (B1 열 왼쪽 = NorthWest, 오른쪽 = NorthEast)
    private static int GetRegionIndex(TransplantContext context, Vector2 position)
    {
        if (position.x < context.MiddleColumn.xMin) return 0;
        if (position.x >= context.MiddleColumn.xMax) return 2;
        return 1;
    }

    // ---------- 데모 오브젝트 ----------

    private static void TransplantObjects(TransplantContext context, Scene demoScene, RootStats[] rootStats)
    {
        GameObject[] roots = demoScene.GetRootGameObjects();
        for (int i = 0; i < DemoRootNames.Length; i++)
        {
            EditorUtility.DisplayProgressBar("북쪽 데모 이식", $"오브젝트 {DemoRootNames[i]}", (float)i / DemoRootNames.Length);
            GameObject root = roots.FirstOrDefault(candidate => candidate.name == DemoRootNames[i]);
            if (root == null)
            {
                Debug.LogWarning($"[MapDemoObjectTransplant] 데모 씬 루트에 {DemoRootNames[i]}가 없습니다.");
                continue;
            }

            var items = new List<GameObject>();
            CollectItems(root.transform, items);
            foreach (GameObject item in items)
                TransplantObject(context, item, i, rootStats[i]);
        }
    }

    // prefab 인스턴스 루트, 또는 Transform 말고도 컴포넌트가 있는 오브젝트를 한 덩어리로 본다. 빈 묶음 오브젝트는 그 안으로 들어간다
    private static void CollectItems(Transform parent, List<GameObject> items)
    {
        foreach (Transform child in parent)
        {
            GameObject item = child.gameObject;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(item) || item.GetComponents<Component>().Length > 1) items.Add(item);
            else CollectItems(child, items);
        }
    }

    private static void TransplantObject(TransplantContext context, GameObject item, int rootIndex, RootStats stats)
    {
        Vector3 demoPivot = item.transform.position;
        if (!MapDemoTransplant.IsInsideWindow(demoPivot)) return;

        stats.InWindowCount++;
        SkipReason? reason = CheckObject(context, item, demoPivot, out Footprint footprint);
        if (reason != null)
        {
            stats.SkipCounts[(int)reason.Value]++;
            return;
        }

        int region = GetRegionIndex(context, footprint.Points[0]);
        GameObject instance = CreateObject(item, context.DemoParents[region, rootIndex], stats);
        // 높이 = 우리 지면 + (데모 높이 - 데모 지면)
        Vector3 position = MapDemoTransplant.DemoToGame(demoPivot);
        position.y = GetGroundHeight(context.Terrain, footprint.Points[0]) + demoPivot.y - GetDemoGroundHeight(context, demoPivot);
        instance.transform.SetPositionAndRotation(position, MapDemoTransplant.DemoRotationToGame(item.transform.rotation));
        instance.transform.localScale = item.transform.lossyScale;

        if (FinishInstance(context, instance, GetSourceName(item))) stats.DisallowCount++;
        context.Objects.Add(new Blocker { Center = footprint.Center, Radius = footprint.Radius });
        stats.PlacedCount++;
    }

    // 놓으면 안 되는 이유. 놓아도 되면 null
    private static SkipReason? CheckObject(TransplantContext context, GameObject item, Vector3 demoPivot, out Footprint footprint)
    {
        footprint = default;
        if (!item.activeInHierarchy) return SkipReason.Inactive;

        Renderer[] renderers = item.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return SkipReason.NoRenderer;
        if (!PrefabUtility.IsAnyPrefabInstanceRoot(item) && HasSceneMesh(item)) return SkipReason.SceneMesh;

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
            bounds.Encapsulate(renderer.bounds);
        footprint = CreateFootprint(context, demoPivot, bounds);

        if (footprint.Points.Any(point => IsLakeWater(context, point))) return SkipReason.Lake;
        if (IsNearRiver(context, footprint.Center, ObjectRiverMargin + footprint.Radius)) return SkipReason.River;
        if (IsNearTrail(context, footprint.Center, ObjectTrailMargin + footprint.Radius)) return SkipReason.Trail;
        if (IsHeightBroken(footprint)) return SkipReason.Height;
        return null;
    }

    // prefab이 아닌 오브젝트가 씬 파일 안에만 있는 메시를 쓰면, 데모 씬을 닫은 뒤 참조가 끊긴다
    private static bool HasSceneMesh(GameObject item)
    {
        return item.GetComponentsInChildren<MeshFilter>()
            .Any(filter => filter.sharedMesh != null && !EditorUtility.IsPersistent(filter.sharedMesh));
    }

    // 180도 회전은 축 정렬 범위를 축 정렬 범위로 옮기므로, 데모 범위 둘레 점을 하나씩 변환해 쓴다
    private static Footprint CreateFootprint(TransplantContext context, Vector3 demoPivot, Bounds bounds)
    {
        var demoPoints = new Vector3[RingOffsets.Length + 1];
        demoPoints[0] = demoPivot;
        for (int i = 0; i < RingOffsets.Length; i++)
        {
            Vector2 offset = RingOffsets[i] * FootprintInset;
            demoPoints[i + 1] = bounds.center + new Vector3(offset.x * bounds.extents.x, 0f, offset.y * bounds.extents.z);
        }

        var footprint = new Footprint
        {
            Center = ToXZ(MapDemoTransplant.DemoToGame(bounds.center)),
            Radius = Mathf.Max(bounds.extents.x, bounds.extents.z),
            Points = new Vector2[demoPoints.Length],
            GroundChanges = new float[demoPoints.Length]
        };
        for (int i = 0; i < demoPoints.Length; i++)
            footprint.GroundChanges[i] = GetGroundChange(context, demoPoints[i], out footprint.Points[i]);
        return footprint;
    }

    // 데모 점 아래 지면을 우리 맵으로 옮긴 높이와 실제 우리 지면의 차이 (+ 이면 우리 지면이 더 높다)
    private static float GetGroundChange(TransplantContext context, Vector3 demoPoint, out Vector2 gamePoint)
    {
        Vector3 demoGround = new Vector3(demoPoint.x, GetDemoGroundHeight(context, demoPoint), demoPoint.z);
        Vector3 moved = MapDemoTransplant.DemoToGame(demoGround);
        gamePoint = ToXZ(moved);
        return GetGroundHeight(context.Terrain, gamePoint) - moved.y;
    }

    // 피벗 기준으로 옮겼을 때 둘레에서 지면이 더 내려간 곳(뜸)이나 더 올라온 곳(묻힘)이 있는지
    private static bool IsHeightBroken(Footprint footprint)
    {
        float pivotChange = footprint.GroundChanges[0];
        for (int i = 1; i < footprint.GroundChanges.Length; i++)
        {
            float difference = footprint.GroundChanges[i] - pivotChange;
            if (difference < -MaxFloatGap || difference > MaxBuryGap) return true;
        }
        return false;
    }

    // prefab 인스턴스는 같은 prefab으로 놓고 데모 인스턴스의 오버라이드를 복사한다. prefab이 아니면 통째로 복제한다
    private static GameObject CreateObject(GameObject item, Transform parent, RootStats stats)
    {
        if (PrefabUtility.IsOutermostPrefabInstanceRoot(item))
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(item);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            CopyOverrides(item, instance, source, stats);
            return instance;
        }
        if (PrefabUtility.IsAnyPrefabInstanceRoot(item))
        {
            // 다른 prefab 안에 든 prefab : 바깥 prefab의 오버라이드는 옮길 수 없어 원본 prefab 그대로 놓는다
            stats.NestedCount++;
            return (GameObject)PrefabUtility.InstantiatePrefab(PrefabUtility.GetCorrespondingObjectFromOriginalSource(item), parent);
        }

        stats.ClonedCount++;
        GameObject clone = Object.Instantiate(item, parent);
        clone.name = item.name;
        return clone;
    }

    // 값 오버라이드(머티리얼, 메시 등)를 복사한다. 루트 위치, 회전, 크기는 따로 맞추므로 빼고,
    // 데모 씬 안의 오브젝트를 가리키는 값은 씬을 닫으면 끊기므로 뺀다. 컴포넌트, 자식 추가와 삭제는 복사하지 않고 센다
    private static void CopyOverrides(GameObject item, GameObject instance, GameObject source, RootStats stats)
    {
        PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(item);
        if (modifications != null)
        {
            PropertyModification[] kept = modifications
                .Where(modification => modification.target != source.transform)
                .Where(modification => modification.objectReference == null || EditorUtility.IsPersistent(modification.objectReference))
                .ToArray();
            stats.DroppedReferenceCount += modifications.Count(modification =>
                modification.objectReference != null && !EditorUtility.IsPersistent(modification.objectReference));
            PrefabUtility.SetPropertyModifications(instance, kept);
        }

        bool hasStructuralOverride = PrefabUtility.GetAddedComponents(item).Count > 0
                                     || PrefabUtility.GetRemovedComponents(item).Count > 0
                                     || PrefabUtility.GetAddedGameObjects(item).Count > 0;
        if (hasStructuralOverride) stats.StructuralOverrideCount++;
    }

    private static string GetSourceName(GameObject item)
    {
        if (!PrefabUtility.IsAnyPrefabInstanceRoot(item)) return item.name;
        return PrefabUtility.GetCorrespondingObjectFromOriginalSource(item).name;
    }

    // 충돌체, GPU Resident Drawer 제외, Static 끔. Disallow를 붙였으면 true
    private static bool FinishInstance(TransplantContext context, GameObject instance, string prefabName)
    {
        // AddMissingCollider는 prefab 이름으로 모양을 고른다. 복제한 오브젝트(prefab 아님)는 이름이 같아도 구조가 다를 수 있어 넘기지 않는다
        bool isPrefab = PrefabUtility.IsPartOfPrefabInstance(instance);
        if (isPrefab && MapTreeScatter.AddMissingCollider(instance, prefabName)) context.ColliderCount++;
        if (instance.GetComponentInChildren<Collider>(true) == null)
        {
            context.NoColliderCounts.TryGetValue(prefabName, out int count);
            context.NoColliderCounts[prefabName] = count + 1;
        }

        // Static Batching과 섞지 않는다 (MapLayout.md 성능 기준). 데모 오버라이드에 Static이 있어도 끈다
        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetStaticEditorFlags(child.gameObject) != 0) GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
        }
        return AddDisallowIfNeeded(context, instance);
    }

    // 몸체가 안 그려지는 것으로 확인된 셰이더를 쓰는 렌더러는 GPU Resident Drawer에서 빼 준다 (원본 prefab은 고치지 않는다)
    private static bool AddDisallowIfNeeded(TransplantContext context, GameObject instance)
    {
        bool isAdded = false;
        foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.sharedMaterials.All(material => material == null || SupportsGpuDriven(context, material.shader))) continue;
            if (renderer.GetComponent(context.DisallowType) != null) continue;

            renderer.gameObject.AddComponent(context.DisallowType);
            isAdded = true;
        }
        return isAdded;
    }

    // 확인된 셰이더 이름 목록에 없으면 GPU Resident Drawer로 그린다. 판정 결과는 로그에 쓰려고 모은다
    private static bool SupportsGpuDriven(TransplantContext context, Shader shader)
    {
        if (context.GpuDrivenSupport.TryGetValue(shader, out bool isSupported)) return isSupported;

        isSupported = !GpuDrivenBrokenShaderNames.Contains(shader.name);
        context.GpuDrivenSupport[shader] = isSupported;
        return isSupported;
    }

    // ---------- 룬 돌 ----------

    private static void PlaceLandmarks(TransplantContext context)
    {
        Dictionary<string, Vector2> waypoints = MapTrailBuilder.GetWaypoints();
        var random = new System.Random(LandmarkSeed);
        GameObject[] prefabs = LoadPrefabs(LandmarkPaths);
        foreach (string name in LandmarkWaypointNames)
        {
            // 무작위 값은 자리를 찾았는지와 상관없이 같은 순서로 뽑는다
            GameObject prefab = prefabs[random.Next(prefabs.Length)];
            float yaw = (float)random.NextDouble() * 360f;
            if (!waypoints.TryGetValue(name, out Vector2 waypoint))
            {
                Debug.LogWarning($"[MapDemoObjectTransplant] 등산로 경유점 '{name}'이 저장 파일에 없습니다.");
                continue;
            }
            TryPlaceLandmark(context, name, waypoint, prefab, yaw);
        }
    }

    // 데모 오브젝트를 피하는 자리를 먼저 찾고, 없으면 피하지 않고 다시 찾는다
    private static void TryPlaceLandmark(TransplantContext context, string name, Vector2 waypoint, GameObject prefab, float yaw)
    {
        Bounds bounds = GetLocalBounds(prefab);
        float radius = GetFootprintRadius(bounds);
        Vector2? spot = FindLandmarkSpot(context, waypoint, radius, true);
        bool isAvoidingObjects = spot != null;
        spot ??= FindLandmarkSpot(context, waypoint, radius, false);
        if (spot == null)
        {
            Debug.LogWarning($"[MapDemoObjectTransplant] 룬 돌 '{name}' 자리를 찾지 못했습니다 (경유점에서 {LandmarkSearchRadius}m 안, 길 가장자리에서 {LandmarkTrailGapMin} ~ {LandmarkTrailGapMax}m, 경사 {LandmarkMaxSlope}도 이하).");
            return;
        }

        Vector2 position = spot.Value;
        Transform parent = context.LandmarkParents[GetRegionIndex(context, position)];
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.transform.SetPositionAndRotation(ToWorld(position, 0f), Quaternion.Euler(0f, yaw, 0f));
        SetGrounded(context, instance, bounds);
        FinishInstance(context, instance, prefab.name);
        context.Landmarks.Add(new Blocker { Center = position, Radius = radius + LandmarkClearance });
        context.LandmarkNotes.Add($"{name} {prefab.name} ({position.x:F1}, {position.y:F1}) 높이 {instance.transform.position.y:F1}m, 경사 {GetSlope(context.Terrain, position):F1}도, " +
                                  $"경유점에서 {Vector2.Distance(position, waypoint):F1}m, 바닥 반지름 {radius:F1}m" +
                                  (isAvoidingObjects ? "" : " (데모 오브젝트를 피하는 자리가 없어 겹칠 수 있음)"));
    }

    // 경유점에서 가까운 순서로 둘레를 돌며 조건을 맞추는 첫 자리
    private static Vector2? FindLandmarkSpot(TransplantContext context, Vector2 waypoint, float radius, bool isAvoidingObjects)
    {
        for (float distance = 0f; distance <= LandmarkSearchRadius; distance += LandmarkSearchStep)
        {
            for (float angle = 0f; angle < 360f; angle += LandmarkAngleStep)
            {
                float radian = angle * Mathf.Deg2Rad;
                Vector2 position = waypoint + new Vector2(Mathf.Cos(radian), Mathf.Sin(radian)) * distance;
                if (CanPlaceLandmark(context, position, radius, isAvoidingObjects)) return position;
            }
        }
        return null;
    }

    // 바닥 가장자리와 길 가장자리 사이가 3 ~ 10m (= 길 중심에서 반폭 + 반지름 + 3m 밖, 반폭 + 반지름 + 10m 안)
    private static bool CanPlaceLandmark(TransplantContext context, Vector2 position, float radius, bool isAvoidingObjects)
    {
        if (IsNearTrail(context, position, radius + LandmarkTrailGapMin)) return false;
        if (!IsNearTrail(context, position, radius + LandmarkTrailGapMax)) return false;
        if (GetSlope(context.Terrain, position) > LandmarkMaxSlope) return false;
        if (IsNearRiver(context, position, ObjectRiverMargin + radius)) return false;
        if (GetRingPoints(position, radius).Any(point => IsLakeWater(context, point))) return false;
        if (context.Landmarks.Any(other => Vector2.Distance(other.Center, position) < other.Radius + radius)) return false;
        if (!isAvoidingObjects) return true;
        return !context.Objects.Any(other => Vector2.Distance(other.Center, position) < (other.Radius + radius) * LandmarkOverlapRatio);
    }

    // 바닥 모서리와 가운데 중 가장 낮은 땅보다 (높이 × LandmarkSinkRatio)만큼 아래에 밑면을 맞춘다. 비탈 아래쪽이 뜨지 않는다
    private static void SetGrounded(TransplantContext context, GameObject instance, Bounds localBounds)
    {
        Matrix4x4 toWorld = instance.transform.localToWorldMatrix;
        Vector3[] corners = GetCorners(localBounds).Select(corner => toWorld.MultiplyPoint3x4(corner)).ToArray();
        float lowestGround = corners
            .Select(corner => GetGroundHeight(context.Terrain, ToXZ(corner)))
            .Append(GetGroundHeight(context.Terrain, ToXZ(instance.transform.position)))
            .Min();
        float bottom = corners.Min(corner => corner.y);
        float height = corners.Max(corner => corner.y) - bottom;
        instance.transform.position += Vector3.up * (lowestGround - height * LandmarkSinkRatio - bottom);
    }

    // ---------- 데모 나무 ----------

    private static void TransplantTrees(TransplantContext context, TreeStats stats)
    {
        EditorUtility.DisplayProgressBar("북쪽 데모 이식", "나무", 1f);
        TerrainData data = context.DemoTerrain.terrainData;
        TreePrototype[] prototypes = data.treePrototypes;
        bool[] isUsable = prototypes.Select(IsUsablePrototype).ToArray();
        stats.PrototypeInWindowCounts = new int[prototypes.Length];
        stats.PrototypePlacedCounts = new int[prototypes.Length];
        stats.PrototypeLabels = prototypes
            .Select((prototype, i) => (prototype.prefab != null ? prototype.prefab.name : "(prefab 없음)") + (isUsable[i] ? "" : " (제외)"))
            .ToArray();
        Vector3 origin = context.DemoTerrain.transform.position;

        foreach (TreeInstance tree in data.treeInstances)
        {
            Vector3 demoPosition = origin + Vector3.Scale(tree.position, data.size);
            if (!MapDemoTransplant.IsInsideWindow(demoPosition)) continue;

            stats.InWindowCount++;
            stats.PrototypeInWindowCounts[tree.prototypeIndex]++;
            SkipReason? reason = isUsable[tree.prototypeIndex] ? CheckTree(context, demoPosition) : SkipReason.Excluded;
            if (reason != null)
            {
                stats.SkipCounts[(int)reason.Value]++;
                continue;
            }

            PlaceTree(context, prototypes[tree.prototypeIndex].prefab, tree, demoPosition, stats);
            stats.PlacedCount++;
            stats.PrototypePlacedCounts[tree.prototypeIndex]++;
        }
    }

    private static bool IsUsablePrototype(TreePrototype prototype)
    {
        GameObject prefab = prototype.prefab;
        return prefab != null && !prefab.name.StartsWith(ExcludedPrefabPrefix) && prefab.GetComponentInChildren<Renderer>() != null;
    }

    private static SkipReason? CheckTree(TransplantContext context, Vector3 demoPosition)
    {
        float change = GetGroundChange(context, demoPosition, out Vector2 position);
        if (GetRingPoints(position, TreeFootprintRadius).Append(position).Any(point => IsLakeWater(context, point))) return SkipReason.Lake;
        if (IsNearRiver(context, position, MapTreeScatter.RiverClearMargin)) return SkipReason.River;
        if (IsNearTrail(context, position, MapTreeScatter.TrailClearMargin)) return SkipReason.Trail;
        if (context.Landmarks.Any(landmark => Vector2.Distance(landmark.Center, position) < landmark.Radius)) return SkipReason.Landmark;
        if (Mathf.Abs(change) > TreeChangedHeight && GetSlope(context.Terrain, position) > MapTreeScatter.MaxSlope) return SkipReason.Height;
        return null;
    }

    // 크기는 TreeInstance의 widthScale(가로), heightScale(세로), 회전은 rotation(라디안, y축)을 쓰고, 높이는 우리 지면에 둔다
    private static void PlaceTree(TransplantContext context, GameObject prefab, TreeInstance tree, Vector3 demoPosition, TreeStats stats)
    {
        Vector2 position = ToXZ(MapDemoTransplant.DemoToGame(demoPosition));
        Transform parent = context.TreeParents[GetRegionIndex(context, position)];
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Quaternion rotation = MapDemoTransplant.DemoRotationToGame(Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f));
        instance.transform.SetPositionAndRotation(ToWorld(position, GetGroundHeight(context.Terrain, position)), rotation);
        instance.transform.localScale = new Vector3(tree.widthScale, tree.heightScale, tree.widthScale);
        if (FinishInstance(context, instance, prefab.name)) stats.DisallowCount++;
    }

    // ---------- 창 밖 채우기 ----------

    // A1, C1의 창 밖에 메뉴 8과 같은 높이대 밀도로 나무, 바위를 흩뿌린다. 바탕 + MapNorthScatter.OuterFlatMaxRise 미만의 평지는
    // 메뉴 8처럼 그 구역 작업 때 채우도록 비운다. 절벽, 큰 바위 무리, 룬 돌은 두지 않는다
    private static OutsideStats FillOutsideWindow(TransplantContext context)
    {
        var stats = new OutsideStats();
        var random = new System.Random(OutsideSeed);
        GameObject[][] treePrefabs =
        {
            LoadPrefabs(MapTreeScatter.MainTreePaths), LoadPrefabs(MapTreeScatter.DeadTreePaths), LoadPrefabs(MapTreeScatter.GiantTreePaths)
        };
        GameObject[] rockPrefabs = LoadPrefabs(MapTreeScatter.RockPaths.Where(path => path.StartsWith(StonePathPrefix)));
        Rect window = MapDemoTransplant.GetGameWindowRect();
        Rect north = MapGrid.GetRangeBounds(MapNorthScatter.NorthFromCell, MapNorthScatter.NorthToCell);
        int columns = Mathf.RoundToInt(north.width / ScatterCellSize);
        int rows = Mathf.RoundToInt(north.height / ScatterCellSize);

        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                var cellMin = new Vector2(north.xMin + x * ScatterCellSize, north.yMin + z * ScatterCellSize);
                Vector2 cellCenter = cellMin + Vector2.one * (ScatterCellSize / 2f);
                float height = GetGroundHeight(context.Terrain, cellCenter);
                if (window.Contains(cellCenter) || height < MapTerrainSetup.BaseHeight + MapNorthScatter.OuterFlatMaxRise) continue;

                stats.CellCount++;
                int band = (int)MapNorthScatter.GetBand(cellCenter, height);
                bool canBeGiant = band == (int)MapNorthScatter.HeightBand.Foothill;
                int treeCount = RollCount(random, TreeDensityMin[band], TreeDensityMax[band]);
                for (int i = 0; i < treeCount; i++)
                {
                    if (TryAddOutsideTree(context, random, cellMin, treePrefabs, canBeGiant, stats)) stats.TreeCount++;
                }
                int rockCount = RollCount(random, RockDensityMin[band], RockDensityMax[band]);
                for (int i = 0; i < rockCount; i++)
                {
                    if (TryAddOutsideRock(context, random, cellMin, rockPrefabs, stats)) stats.RockCount++;
                }
            }
        }
        return stats;
    }

    // 무작위 값은 조건과 상관없이 같은 순서로 뽑아 결과가 항상 같게 한다
    private static bool TryAddOutsideTree(TransplantContext context, System.Random random, Vector2 cellMin, GameObject[][] treePrefabs, bool canBeGiant, OutsideStats stats)
    {
        Vector2 position = cellMin + new Vector2(RandomRange(random, 0f, ScatterCellSize), RandomRange(random, 0f, ScatterCellSize));
        GameObject[] prefabs = treePrefabs[(int)PickTreeKind(random, canBeGiant)];
        GameObject prefab = prefabs[random.Next(prefabs.Length)];
        float scale = RandomRange(random, MapTreeScatter.TreeScaleMin, MapTreeScatter.TreeScaleMax);
        float yaw = RandomRange(random, 0f, 360f);
        if (!CanPlaceOutside(context, position, MapTreeScatter.MaxSlope, stats)) return false;

        PlaceOutside(context, prefab, context.TreeParents, ToWorld(position, GetGroundHeight(context.Terrain, position)), yaw, scale);
        return true;
    }

    // 바위 피벗을 바닥 반지름 안의 가장 낮은 땅에 맞춰 비탈 아래쪽이 뜨지 않게 한다 (메뉴 8과 같음)
    private static bool TryAddOutsideRock(TransplantContext context, System.Random random, Vector2 cellMin, GameObject[] rockPrefabs, OutsideStats stats)
    {
        Vector2 position = cellMin + new Vector2(RandomRange(random, 0f, ScatterCellSize), RandomRange(random, 0f, ScatterCellSize));
        GameObject prefab = rockPrefabs[random.Next(rockPrefabs.Length)];
        float scale = RandomRange(random, MapTreeScatter.RockScaleMin, MapTreeScatter.RockScaleMax);
        float yaw = RandomRange(random, 0f, 360f);
        if (!CanPlaceOutside(context, position, RockMaxSlope, stats)) return false;

        float ground = GetRingPoints(position, RockFootprintRadius * scale).Append(position).Min(point => GetGroundHeight(context.Terrain, point));
        PlaceOutside(context, prefab, context.RockParents, ToWorld(position, ground), yaw, scale);
        return true;
    }

    private static bool CanPlaceOutside(TransplantContext context, Vector2 position, float maxSlope, OutsideStats stats)
    {
        if (GetSlope(context.Terrain, position) > maxSlope)
        {
            stats.SlopeSkipCount++;
            return false;
        }
        if (IsLakeWater(context, position) || IsNearRiver(context, position, MapTreeScatter.RiverClearMargin)
            || IsNearTrail(context, position, MapTreeScatter.TrailClearMargin))
        {
            stats.WaterSkipCount++;
            return false;
        }
        if (context.Objects.Any(blocker => Vector2.Distance(blocker.Center, position) < blocker.Radius))
        {
            stats.BlockerSkipCount++;
            return false;
        }
        return true;
    }

    private static void PlaceOutside(TransplantContext context, GameObject prefab, Transform[] parents, Vector3 position, float yaw, float scale)
    {
        Transform parent = parents[GetRegionIndex(context, ToXZ(position))];
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        instance.transform.localScale = Vector3.one * scale;
        FinishInstance(context, instance, prefab.name);
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

    // ---------- 물, 길 ----------

    // 호수 수면 메시가 덮고 지면이 수면보다 낮은 곳. 섬(수면 위 지면)은 물이 아니다
    private static bool IsLakeWater(TransplantContext context, Vector2 position)
    {
        LakeWater lake = context.Lake;
        if (!Expand(lake.Bounds, lake.Spacing).Contains(position)) return false;
        if (GetGroundHeight(context.Terrain, position) >= lake.Surface) return false;

        Vector2Int key = GetLakeHashKey(position);
        int range = Mathf.CeilToInt(lake.Spacing / LakeHashCellSize);
        float sqrDistance = lake.Spacing * lake.Spacing;
        for (int z = key.y - range; z <= key.y + range; z++)
        {
            for (int x = key.x - range; x <= key.x + range; x++)
            {
                if (!lake.Vertices.TryGetValue(new Vector2Int(x, z), out List<Vector2> vertices)) continue;
                if (vertices.Any(vertex => (vertex - position).sqrMagnitude <= sqrDistance)) return true;
            }
        }
        return false;
    }

    private static Vector2Int GetLakeHashKey(Vector2 position)
    {
        return new Vector2Int(Mathf.FloorToInt(position.x / LakeHashCellSize), Mathf.FloorToInt(position.y / LakeHashCellSize));
    }

    // 경로 범위로 먼저 걸러 경로 거리 계산을 줄인다
    private static bool IsNearRiver(TransplantContext context, Vector2 position, float margin)
    {
        if (!Expand(context.RiverBounds, MapRiverBuilder.HalfWidth + margin).Contains(position)) return false;
        return MapRiverBuilder.IsNearRiver(context.RiverPath, ToWorld(position, 0f), margin);
    }

    private static bool IsNearTrail(TransplantContext context, Vector2 position, float margin)
    {
        if (!Expand(context.TrailBounds, MapTrailBuilder.HalfWidth + margin).Contains(position)) return false;
        return MapTrailBuilder.IsNearTrail(context.Trails, ToWorld(position, 0f), margin);
    }

    // ---------- 지형, 계산 ----------

    // SampleHeight는 Terrain 기준 높이라 Terrain의 y를 더한다
    private static float GetGroundHeight(Terrain terrain, Vector2 position)
    {
        return terrain.SampleHeight(ToWorld(position, 0f)) + terrain.transform.position.y;
    }

    private static float GetDemoGroundHeight(TransplantContext context, Vector3 demoPosition)
    {
        return context.DemoTerrain.SampleHeight(demoPosition) + context.DemoTerrain.transform.position.y;
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

    // 메시 범위를 prefab 루트 기준으로 모은다 (LOD 자식의 위치, 회전, 크기를 반영)
    private static Bounds GetLocalBounds(GameObject prefab)
    {
        Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;
        Vector3[] points = prefab.GetComponentsInChildren<MeshFilter>()
            .SelectMany(filter => GetCorners(filter.sharedMesh.bounds).Select(corner => (rootInverse * filter.transform.localToWorldMatrix).MultiplyPoint3x4(corner)))
            .ToArray();
        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (Vector3 point in points)
            bounds.Encapsulate(point);
        return bounds;
    }

    // 피벗에서 가장 먼 바닥 모서리까지의 수평 거리. 어느 방향으로 돌려도 바닥이 이 원 안에 든다
    private static float GetFootprintRadius(Bounds bounds)
    {
        return GetCorners(bounds).Max(corner => new Vector2(corner.x, corner.z).magnitude);
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

    private static Vector2 ToXZ(Vector3 world)
    {
        return new Vector2(world.x, world.z);
    }

    private static Vector3 ToWorld(Vector2 position, float height)
    {
        return new Vector3(position.x, height, position.y);
    }

    private static GameObject[] LoadPrefabs(IEnumerable<string> paths)
    {
        return paths
            .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + path + ".prefab"))
            .ToArray();
    }

    private static float RandomRange(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    // ---------- 로그 ----------

    // 검수(이식 수와 건너뛴 이유, Disallow 판정, 룬 돌 위치, 물·길 위 0개, 성능 측정용 배치 수)에 쓰는 값이라 로그를 남긴다
    private static void LogResult(TransplantContext context, RootStats[] rootStats, TreeStats treeStats, OutsideStats outsideStats)
    {
        foreach (RootStats stats in rootStats)
        {
            Debug.Log($"[MapDemoObjectTransplant] 데모 {stats.Name} : 창 안 {stats.InWindowCount}, 놓음 {stats.PlacedCount}, 건너뜀 {FormatSkips(stats.SkipCounts)}, " +
                      $"Disallow 붙임 {stats.DisallowCount} / prefab 아님(복제) {stats.ClonedCount}, 중첩 prefab(오버라이드 미복사) {stats.NestedCount}, " +
                      $"컴포넌트·자식 추가/삭제 오버라이드 미복사 {stats.StructuralOverrideCount}, 씬 참조 오버라이드 버림 {stats.DroppedReferenceCount}");
        }
        LogTrees(treeStats);
        Debug.Log($"[MapDemoObjectTransplant] 창 밖 채우기 : 대상 {outsideStats.CellCount * ScatterCellSize * ScatterCellSize / HectareArea:F1}ha " +
                  $"(바탕 + {MapNorthScatter.OuterFlatMaxRise}m 미만 평지 제외), 나무 {outsideStats.TreeCount}그루, 바위 {outsideStats.RockCount}개 / " +
                  $"건너뜀 경사 {outsideStats.SlopeSkipCount}, 물·길 {outsideStats.WaterSkipCount}, 데모 오브젝트 자리 {outsideStats.BlockerSkipCount}");
        Debug.Log($"[MapDemoObjectTransplant] 룬 돌 {context.LandmarkNotes.Count}개 :\n{string.Join("\n", context.LandmarkNotes)}");
        LogShaders(context);
        Debug.Log($"[MapDemoObjectTransplant] 충돌체 추가 {context.ColliderCount}개. 충돌체 없이 놓인 prefab (이름 : 개수) :\n" +
                  string.Join("\n", context.NoColliderCounts.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key} : {pair.Value}")));
        LogPlacementCheck(context);
    }

    // 데모 씬을 닫은 뒤에 불리므로 데모 Terrain을 읽지 않고 TransplantTrees가 담아 둔 이름을 쓴다
    private static void LogTrees(TreeStats stats)
    {
        string detail = string.Join("\n", Enumerable.Range(0, stats.PrototypeLabels.Length).Select(i =>
            $"  {stats.PrototypeLabels[i]} : 창 안 {stats.PrototypeInWindowCounts[i]}, 놓음 {stats.PrototypePlacedCounts[i]}"));
        Debug.Log($"[MapDemoObjectTransplant] 데모 나무 : 창 안 {stats.InWindowCount}, 놓음 {stats.PlacedCount}, 건너뜀 {FormatSkips(stats.SkipCounts)}, Disallow 붙임 {stats.DisallowCount}. 프로토타입별 :\n{detail}");
    }

    // Disallow 판정 근거. GPU Resident Drawer로 그리는 셰이더 중 Shader Graph가 아닌 것(.shader 파일)은 목록 밖이라도 몸체가 그려지는지 눈으로 확인해야 한다
    private static void LogShaders(TransplantContext context)
    {
        string broken = string.Join(", ", context.GpuDrivenSupport.Where(pair => !pair.Value).Select(pair => pair.Key.name));
        string graph = string.Join(", ", context.GpuDrivenSupport.Where(pair => pair.Value && IsShaderGraph(pair.Key)).Select(pair => pair.Key.name));
        string handWritten = string.Join(", ", context.GpuDrivenSupport.Where(pair => pair.Value && !IsShaderGraph(pair.Key)).Select(pair => pair.Key.name));
        Debug.Log($"[MapDemoObjectTransplant] 확인된 비호환 셰이더 (Disallow 붙임) : {(broken.Length > 0 ? broken : "없음")}\n" +
                  $"GPU Resident Drawer로 그림 - Shader Graph : {(graph.Length > 0 ? graph : "없음")}\n" +
                  $"GPU Resident Drawer로 그림 - Shader Graph 아님 (몸체 표시 확인 필요) : {(handWritten.Length > 0 ? handWritten : "없음")}");
    }

    private static bool IsShaderGraph(Shader shader)
    {
        return AssetDatabase.GetAssetPath(shader).EndsWith(".shadergraph");
    }

    // 놓인 오브젝트를 다시 읽어 피벗이 물(여유 없이)과 길 폭 안에 든 것을 센다. 모두 0이어야 한다. 구역별 배치 수도 함께 남긴다
    private static void LogPlacementCheck(TransplantContext context)
    {
        int lakeCount = 0;
        int riverCount = 0;
        int trailCount = 0;
        int total = 0;
        for (int region = 0; region < MapNorthScatter.RegionNames.Length; region++)
        {
            var parents = new List<Transform> { context.TreeParents[region], context.RockParents[region], context.LandmarkParents[region] };
            parents.AddRange(Enumerable.Range(0, DemoRootNames.Length).Select(root => context.DemoParents[region, root]));
            foreach (Transform child in parents.SelectMany(parent => parent.Cast<Transform>()))
            {
                Vector2 position = ToXZ(child.position);
                if (IsLakeWater(context, position)) lakeCount++;
                if (IsNearRiver(context, position, 0f)) riverCount++;
                if (IsNearTrail(context, position, 0f)) trailCount++;
            }

            int[] counts = parents.Select(parent => parent.childCount).ToArray();
            total += counts.Sum();
            string demoDetail = string.Join(", ", Enumerable.Range(0, DemoRootNames.Length).Select(root => $"{DemoRootNames[root]} {context.DemoParents[region, root].childCount}"));
            Debug.Log($"[MapDemoObjectTransplant] {MapNorthScatter.RegionNames[region]} 배치 GameObject {counts.Sum()}개 " +
                      $"(Trees {counts[0]}, Rocks {counts[1]}, Landmarks {counts[2]}, Demo : {demoDetail})");
        }
        Debug.Log($"[MapDemoObjectTransplant] 북쪽 줄 배치 GameObject 총 {total}개");
        Debug.Log($"[MapDemoObjectTransplant] 피벗이 호수 물 위 {lakeCount}개, 강 물 위 {riverCount}개, 길 위 {trailCount}개 (모두 0이어야 한다)");
    }

    private static string FormatSkips(int[] counts)
    {
        string detail = string.Join(", ", Enumerable.Range(0, counts.Length).Where(i => counts[i] > 0).Select(i => $"{SkipLabels[i]} {counts[i]}"));
        return $"{counts.Sum()}" + (detail.Length > 0 ? $" ({detail})" : "");
    }
}
