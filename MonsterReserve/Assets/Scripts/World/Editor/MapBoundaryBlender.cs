using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 메뉴 27 : 테마가 정해진 구역끼리 맞닿은 경계 8곳 (B1|B2, A2|B2, B2|C2, B2|B3, A2|A3, C2|C3, A3|B3, B3|C3)에서
// 지면 레이어와 나무 밀도가 곧은 선으로 끊겨 보이지 않게 섞는다 (이슈 #15). A1, C1은 테마 미정이라 그 칸과의 경계는 섞지 않고 칸 안도 건드리지 않는다.
// 규칙 (사용자 승인 임시값)
// - 중심선 : 곧은 경계선을 노이즈로 ±WobbleAmplitude(20m), 주기 WobblePeriod(100m)만큼 흔든 선. 같은 직선 위의 경계(예 : z = -250의 A2|A3, B2|B3, C2|C3)는
//   한 노이즈를 이어 써서 교차점에서 끊기지 않는다. 거리는 경계선에 수직인 축 방향(x 또는 z 차)으로 잰다.
// - 띠 : 흔들린 중심선에서 한쪽 BlendHalfWidth(80m). 칸에서 본 건너편 몫 = 중심선 50% → 자기 쪽 띠 끝 0% (직선).
// - 띠 두 개가 겹치는 교차점 부근은 칸(나무)마다 곧은 선에 더 가까운 경계 하나만 쓴다. 알파맵 칸은 곧은 선까지 칸 수로 견주고, 같으면 남북 방향 선을 쓴다.
//   거울 짝(아래)이 다른 경계를 쓰면 그 짝은 섞지 않는다. 그래서 A1, C1 쪽 모서리 (±250, 250) 근처에는 섞지 않는 쐐기 모양 칸이 조금 남는다.
// 지면 레이어 (알파맵)
// - 칸마다 레이어 비중을 섞어 쓰면 다시 실행할 때 이 메뉴가 쓴 값을 원본으로 읽어 또 섞게 되고, 원본을 따로 둘 곳(새 파일, 새 레이어)이 없다.
//   그래서 칸 단위 비중 섞기 대신 "골라 가져오기"로 섞는다. 곧은 경계선에 가장 가까운 격자선을 사이에 두고 마주 보는 두 칸(거울 짝)마다
//   무늬 노이즈(주기 PatchNoisePeriod)를 0 ~ 1 순위로 바꾼 값 u를 하나 정해, u < (낮은 쪽 칸의 건너편 몫)이면 낮은 쪽 칸이 짝 칸 값을 그대로 가져오고,
//   u ≥ 1 - (높은 쪽 칸의 건너편 몫)이면 높은 쪽 칸이 짝 칸 값을 가져온다. 두 몫의 합은 1보다 작아 한 짝에서 둘 다 가져오는 일은 없다.
// - 가져오는 칸의 원본(짝 칸)은 이 메뉴가 쓰지 않는 칸이라 구역 칠하기 메뉴가 쓴 값만 읽는다. 가져올지는 위치로만 정해지므로 다시 실행해도 같은 칸이
//   같은 값을 가져와 결과가 같고, 한쪽 구역만 다시 칠한 뒤 실행해도 처음부터 실행한 것과 같다. 가져온 값도 원래 칸 값 그대로라 레이어 합 1이 유지된다.
// - 넓이로 보면 중심선에서 건너편 지면이 50%, 띠 끝에서 0%이다 (한 칸 안에서 비중을 섞지는 않는다). 띠 밖 칸은 바꾸지 않는다.
// 나무
// - 기준 밀도 : 경계를 따라 DensityChunkLength 구간마다, 양쪽 칸의 띠 밖 기준 띠(곧은 선에서 ReachDistance ~ + ReferenceDepth)에서 잰 1ha당 나무 수.
//   기준 띠 중 다른 경계 띠에 든 곳은 빼고 재며, 남은 넓이가 ReferenceMinAreaRatio 미만인 구간은 가장 가까운 구간 값을 빌린다. 구간 중심 사이는 직선으로 잇는다.
//   이 메뉴가 바꾸지 않는 곳에서만 재므로 다시 실행해도 같은 값이다.
// - 목표 밀도 = 성긴 쪽 + (빽빽한 쪽 - 성긴 쪽) × 빽빽한 쪽 몫. 빽빽한 쪽 몫은 흔들린 중심선 50%, 빽빽한 쪽 띠 끝 100%, 성긴 쪽 띠 끝 0%.
// - 솎기 : 빽빽한 쪽 <구역>/Trees 아래 나무를 (목표 / 빽빽한 쪽 기준 밀도) 확률로 남긴다. 남길지는 나무 위치 해시로 정해 다시 실행해도 같은 나무만 빠진다.
// - 채우기 : 남은 빽빽한 쪽 나무를 곧은 경계선 너머 거울 자리에 같은 prefab으로 놓는다 (확률 = 그 자리에 모자란 밀도 / 원본 자리 목표 밀도, 위치 해시).
//   흔들린 중심선으로 비추면 중심선이 성긴 칸 쪽으로 들어간 곳에서 비춘 자리가 다시 성긴 칸 안이라 채울 나무가 모자라므로,
//   비추기는 곧은 선으로 하고 몫(밀도 기울기)만 흔들린 중심선으로 잰다. 두 쪽 밀도는 흔들린 중심선에서 (두 쪽 평균)으로 만난다.
//   크기는 원본 나무와 같고, 방향은 위치 해시로 돌린다. 높이는 지면에 맞추고, 충돌체는 MapTreeScatter.AddMissingCollider, Static 플래그는 끈다.
//   채운 나무는 성긴 쪽 <구역>/Trees/Boundary에 두고, 다시 실행하면 모든 Boundary 묶음의 자식만 지우고 다시 만든다.
// - 채우지 않는 곳 : 경사 MapTreeScatter.MaxSlope 이상, 물 (강 중심선에서 물 반폭 + RiverMargin, 호수 수면 메시 범위에서 지면 < 수면 + WaterClearHeight,
//   C3에서 지면 < SouthEastPlaceMinHeight. 피벗과 둘레 TreeFootprintRadius 4곳), 길 (등산로 중심에서 길 반폭 + MapTreeScatter.TrailClearMargin,
//   동쪽 수레길 중심에서 CartRoadHalfWidth + LandmarkClearance), <구역>/Landmarks 렌더러 범위 + LandmarkClearance,
//   <구역>/Rocks·Demo 렌더러 범위 + TreeFootprintRadius, 서식지 반경과 거점 비움 반경, 다른 나무에서 TreeMinSpacing 안, 거울 자리가 다른 경계를 쓰는 모서리.
// - 바닥 식생(디테일)과 바위는 건드리지 않는다. Terrain 레이어, 디테일 프로토타입을 새로 넣지 않는다.
// 실행 순서 : ... → 23 → 24 → 25 → 26 → 27 → 10.
//   구역 메뉴(지형, 배치, 칠하기)를 다시 실행하면 27도 다시 실행한다.
//   한쪽 구역의 나무만 다시 만들면 건너편 기준 밀도가 바뀔 수 있다. 이미 솎은 건너편 나무는 되살리지 않으므로, 정확히 맞추려면 양쪽 배치 메뉴를 다시 실행한 뒤 27을 실행한다.
//   이 스크립트의 상수(띠 폭, 흔들기, 무늬, seed, 구간)를 바꾸면 전에 가져온 칸과 솎은 나무가 그대로 남으므로, 섞는 경계에 맞닿은 구역의 배치·칠하기 메뉴를 먼저 다시 실행한다.
public static class MapBoundaryBlender
{
    private enum SkipReason
    {
        Corner = 0,
        Habitat = 1,
        Water = 2,
        Path = 3,
        Slope = 4,
        Landmark = 5,
        Rock = 6,
        Tree = 7,
        NoPrefab = 8
    }

    private static readonly string[] SkipLabels = { "거울 자리가 다른 경계", "서식지·거점", "물", "길", "경사", "랜드마크 둘레", "바위·데모 오브젝트 둘레", "다른 나무 간격", "prefab 인스턴스 아님" };

    // 두 칸이 맞닿은 경계 선분 하나
    private class Boundary
    {
        public string Label;
        // true면 x = Position인 남북 방향 선 (낮은 쪽 = 서), false면 z = Position인 동서 방향 선 (낮은 쪽 = 남)
        public bool IsVertical;
        public float Position;
        public float AlongMin;
        public float AlongMax;
        // [LowSide, HighSide] 칸 번호 (CellNames 순서)
        public int[] Cells;
        // 같은 직선 위의 경계끼리 같은 값
        public Vector2 WobbleOffset;
        public Vector2 PatchOffset;
        // [쪽, 구간] 기준 밀도 (1ha당 나무 수)
        public float[,] Densities;
        public int BorrowedChunkCount;
    }

    private class BoundaryStats
    {
        public int PairCount;
        // 짝 칸이 다른 경계를 써서 섞지 않은 칸
        public int UnpairedCount;
        // [쪽] 짝 칸 값을 가져온 칸 수
        public int[] CopyCounts = new int[2];
        public int ChangedCount;
        public int ThinCount;
        // [쪽] 채운 나무 수
        public int[] FillCounts = new int[2];
        public int FillCandidateCount;
        public int[] SkipCounts = new int[SkipLabels.Length];
        public int ColliderCount;
    }

    private struct WaterSurface
    {
        public Rect Bounds;
        public float Height;
    }

    private class BlendContext
    {
        public Terrain Terrain;
        public Boundary[] Boundaries;
        public BoundaryStats[] Stats;
        // CellNames 순서. 섞는 경계에 들지 않는 칸(A1, C1)은 null
        public Transform[] RegionRoots;
        public Transform[] TreeParents;
        public Transform[] BoundaryParents;
        public List<Vector2> RiverPath;
        public List<List<Vector2>> Trails;
        // 메뉴 25가 길을 찾지 못했으면 null (길 조각도 없다)
        public List<Vector2> CartRoad;
        public List<WaterSurface> Waters;
        // 비움 사각형과 나무 위치 (HashCellSize 칸으로 나눠 담는다)
        public Dictionary<Vector2Int, List<Rect>> LandmarkAreas = new Dictionary<Vector2Int, List<Rect>>();
        public Dictionary<Vector2Int, List<Rect>> RockAreas = new Dictionary<Vector2Int, List<Rect>>();
        public Dictionary<Vector2Int, List<Vector2>> TreePoints = new Dictionary<Vector2Int, List<Vector2>>();
        public int LandmarkRendererCount;
        public int RockRendererCount;
        // 무늬 노이즈 값을 0 ~ 1 순위로 바꾸는 정렬된 표본
        public float[] UniformTable;
    }

    private class AlphaGrid
    {
        public int Resolution;
        public Vector2 Origin;
        public float CellSize;
        public int Layers;
        // 경계마다 곧은 선에 가장 가까운 격자선 번호. 이 번호의 칸부터 높은 쪽 칸이다 (칸 중심 기준 구역과 같다)
        public int[] Splits;
        // 칸이 쓰는 경계 번호. 띠 밖은 -1
        public int[,] Owners;
    }

    private class AlphaCheck
    {
        public int StoredMismatchCount;
        public int RerunChangeCount;
        public float SumMin = float.MaxValue;
        public float SumMax = float.MinValue;
    }

    private class TreeRecord
    {
        public Transform Transform;
        public Vector2 Position;
        // 든 경계 번호와 그 경계에서의 쪽. 띠 밖이면 BoundaryIndex = -1
        public int BoundaryIndex;
        public int Side;
    }

    private const int LowSide = 0;
    private const int HighSide = 1;

    // CellNames 순서의 Hierarchy 구역 부모 이름 (MapTerrainSetup.RegionNames와 같은 이름. 그쪽이 private이고 순서가 달라 칸 순서로 다시 둔다)
    private static readonly string[] CellNames = { "A1", "B1", "C1", "A2", "B2", "C2", "A3", "B3", "C3" };
    private static readonly string[] RegionNames = { "NorthWest", "North", "NorthEast", "West", "Center", "East", "SouthWest", "South", "SouthEast" };
    private static readonly Rect[] CellRects = CellNames.Select(name => MapGrid.GetRangeBounds(name, name)).ToArray();
    private static readonly int SouthEastCell = System.Array.IndexOf(CellNames, MapSouthEastShaper.SouthEastCell);

    // 섞는 경계 (사용자 승인). A1, C1은 테마 미정이라 그 칸과의 경계는 넣지 않는다
    private static readonly string[][] BoundaryCellPairs =
    {
        new[] { "B1", "B2" }, new[] { "A2", "B2" }, new[] { "B2", "C2" }, new[] { "B2", "B3" },
        new[] { "A2", "A3" }, new[] { "C2", "C3" }, new[] { "A3", "B3" }, new[] { "B3", "C3" }
    };

    // 앞 메뉴들의 seed(20261109 ~ 20261161)와 겹치지 않게 둔다
    private const int WobbleSeed = 20261162;
    private const int PatchSeed = 20261163;
    private const int ThinSeed = 20261164;
    private const int FillSeed = 20261165;
    private const int YawSeed = 20261166;

    // ---------- 띠 (사용자 승인 임시값) ----------
    private const float BlendHalfWidth = 80f;
    private const float WobbleAmplitude = 20f;
    private const float WobblePeriod = 100f;
    // 곧은 선에서 이 거리 안까지만 띠가 닿을 수 있다 (띠 + 흔들림)
    private const float ReachDistance = BlendHalfWidth + WobbleAmplitude;
    // Perlin 입력이 음수가 되지 않도록 노이즈 위치를 양수 쪽으로 옮기는 범위 (다른 맵 스크립트와 같은 값)
    private const float NoiseOffsetMin = 100f;
    private const float NoiseOffsetMax = 1000f;

    // ---------- 지면 (임시값) ----------
    // 무늬 노이즈 주기 (m). 알파맵 칸(약 2.4m) 몇 개 크기의 덩어리로 가져오게 해 칸 하나짜리 점무늬를 피한다
    private const float PatchNoisePeriod = 12f;
    // 무늬 노이즈 순위 표를 만드는 표본 수(한 변)와 간격 (노이즈 단위)
    private const int UniformSampleCount = 200;
    private const float UniformSampleStep = 0.37f;
    // 알파맵은 8비트로 저장되어 다시 읽으면 조금 다를 수 있다
    private const float AlphaTolerance = 0.5f / 255f;

    // ---------- 나무 ----------
    private const string TreeParentName = "Trees";
    private const string BoundaryGroupName = "Boundary";
    private const string RockParentName = "Rocks";
    private const string LandmarkParentName = "Landmarks";
    // 북쪽 데모 오브젝트 (메뉴 8-1의 North/Demo)
    private const string DemoParentName = "Demo";
    // 호수 수면 (메뉴 6의 North/Lake)
    private const string LakeRegionName = "North";
    private const string LakeParentName = "Lake";
    // 기준 밀도 구간 길이, 기준 띠 깊이, 기준 띠 넓이 중 다른 띠를 뺀 나머지가 이 비율 미만이면 옆 구간 값을 빌린다 (임시값)
    private const float DensityChunkLength = 100f;
    private const float ReferenceDepth = 100f;
    private const float ReferenceMinAreaRatio = 0.5f;
    // 넓이를 재는 표본 간격 (m, 구분 분위수 표본과 같은 간격)
    private const float AreaSampleSpacing = 5f;
    private const float HectareArea = 10000f;
    // 로그의 띠 안 거리 구간 폭 (m)
    private const float DensityBinWidth = 20f;

    // ---------- 채우지 않는 곳 ----------
    // 나무 줄기·뿌리 자리 반지름. MapSouthEastScatter, MapDemoObjectTransplant의 TreeFootprintRadius와 같은 값 (그쪽이 private이라 다시 둔다. 바뀌면 함께 바꾼다).
    // 다른 나무와는 두 자리가 겹치지 않게 (반지름 × 2), 바위·데모 오브젝트와는 렌더러 범위에서 이만큼 떨어지게 한다
    private const float TreeFootprintRadius = 1f;
    private const float TreeMinSpacing = TreeFootprintRadius * 2f;
    // 랜드마크 둘레 비움. 서쪽·남서쪽·남쪽·남동쪽·동쪽 배치의 LandmarkClearance와 같은 값 (그쪽이 private이라 다시 둔다. 바뀌면 함께 바꾼다)
    private const float LandmarkClearance = 8f;
    // MapEastScatter의 RoadHalfWidth(길 폭 4m의 반)와 같은 값. 그쪽이 private이라 다시 둔다 (바뀌면 함께 바꾼다). 메뉴 25처럼 + LandmarkClearance 안을 비운다
    private const float CartRoadHalfWidth = 2f;
    // MapSouthEastScatter의 PlaceMinHeight와 같은 값. 그쪽이 private이라 다시 둔다 (바뀌면 함께 바꾼다). 호수도 같은 여유(수면 + 0.3m)를 쓴다
    private const float SouthEastPlaceMinHeight = 29.3f;
    private const float WaterClearHeight = SouthEastPlaceMinHeight - MapSouthEastShaper.WaterLevel;
    // 강 물가 밖 여유. 중앙·북쪽 배치(물가 밖 12m)와 남쪽 강 보호 구역(중심 20m) 중 넓은 쪽
    private static readonly float RiverMargin = Mathf.Max(MapTreeScatter.RiverClearMargin, MapSouthShaper.RiverProtectDistance - MapRiverBuilder.HalfWidth);
    // 서식지 반경 (구역 스크립트 값)과 거점 비움 반경
    private static readonly Vector2[] ClearCenters =
    {
        Vector2.zero, MapWestScatter.HabitatCenter, MapSouthWestScatter.HabitatCenter,
        MapSouthScatter.HabitatCenter, MapSouthEastShaper.HabitatCenter, MapEastShaper.HabitatCenter
    };
    private static readonly float[] ClearRadii =
    {
        MapTreeScatter.BaseClearRadius, MapWestScatter.HabitatRadius, MapSouthWestScatter.HabitatRadius,
        MapSouthScatter.HabitatRadius, MapSouthEastShaper.HabitatRadius, MapEastShaper.HabitatRadius
    };

    // 위치 해시는 cm 단위로 반올림한 좌표로 만든다
    private const float HashScale = 100f;
    // 비움 사각형, 나무 위치를 나눠 담는 칸 (m). TreeMinSpacing보다 커야 이웃 3 × 3칸만 보면 된다
    private const float HashCellSize = 10f;
    private const float WobbleLogStep = 1f;

    [MenuItem("Tools/Map/27. 구역 경계 섞기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        Scene scene = terrain.gameObject.scene;
        if (scene != SceneManager.GetActiveScene())
        {
            Debug.LogError("[MapBoundaryBlender] Game 씬을 활성 씬으로 두고 실행하세요.");
            return;
        }

        BlendContext context = CreateContext(terrain);
        if (context == null) return;

        AlphaCheck alphaCheck = BlendAlphamaps(context);
        List<TreeRecord> trees = BlendTrees(context);

        EditorUtility.SetDirty(terrain.terrainData);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        LogWobble(context);
        LogAlphamaps(context, alphaCheck);
        LogTrees(context);
        LogForbidden(context);
        LogDensityBins(context, trees);
    }

    // ---------- 준비 ----------

    // 판정에 필요한 부모, 강, 길, 호수, 비움 사각형을 모은다. 하나라도 없으면 아무것도 바꾸지 않고 null
    private static BlendContext CreateContext(Terrain terrain)
    {
        GameObject[] roots = terrain.gameObject.scene.GetRootGameObjects();
        Boundary[] boundaries = CreateBoundaries();
        var context = new BlendContext
        {
            Terrain = terrain,
            Boundaries = boundaries,
            Stats = boundaries.Select(boundary => new BoundaryStats()).ToArray(),
            RegionRoots = new Transform[CellNames.Length],
            TreeParents = new Transform[CellNames.Length],
            BoundaryParents = new Transform[CellNames.Length],
            Trails = MapTrailBuilder.GetTrails()
        };

        var missing = new List<string>();
        foreach (int cell in GetUsedCells(boundaries))
        {
            Transform root = FindRoot(roots, RegionNames[cell]);
            Transform trees = root != null ? root.Find(TreeParentName) : null;
            if (trees == null) missing.Add($"{RegionNames[cell]}/{TreeParentName}");
            context.RegionRoots[cell] = root;
            context.TreeParents[cell] = trees;
        }
        Transform lakeRegion = FindRoot(roots, LakeRegionName);
        Transform lake = lakeRegion != null ? lakeRegion.Find(LakeParentName) : null;
        if (lake == null) missing.Add($"{LakeRegionName}/{LakeParentName} (메뉴 6)");
        if (context.Trails == null) missing.Add("등산로 저장 파일 (메뉴 7-1)");
        if (missing.Count > 0)
        {
            Debug.LogError($"[MapBoundaryBlender] {string.Join(", ", missing)}이(가) 없어 나무와 금지 구역을 판정할 수 없습니다. 해당 메뉴를 먼저 실행하세요. 지면과 나무를 바꾸지 않았습니다.");
            return null;
        }

        context.RiverPath = MapRiverBuilder.GetPath(terrain);
        context.CartRoad = MapEastScatter.GetCartRoadPath(terrain);
        context.Waters = lake.GetComponentsInChildren<MeshRenderer>()
            .Select(renderer => new WaterSurface { Bounds = ToXZRect(renderer.bounds, 0f), Height = renderer.transform.position.y })
            .ToList();
        foreach (int cell in GetUsedCells(boundaries))
        {
            Transform root = context.RegionRoots[cell];
            context.LandmarkRendererCount += AddFootprints(context.LandmarkAreas, root.Find(LandmarkParentName), LandmarkClearance);
            context.RockRendererCount += AddFootprints(context.RockAreas, root.Find(RockParentName), TreeFootprintRadius);
            context.RockRendererCount += AddFootprints(context.RockAreas, root.Find(DemoParentName), TreeFootprintRadius);
        }
        context.UniformTable = CreateUniformTable();
        return context;
    }

    private static Transform FindRoot(GameObject[] roots, string name)
    {
        foreach (GameObject root in roots)
        {
            if (root.name == name) return root.transform;
        }
        return null;
    }

    private static IEnumerable<int> GetUsedCells(Boundary[] boundaries)
    {
        return boundaries.SelectMany(boundary => boundary.Cells).Distinct().OrderBy(cell => cell);
    }

    // 같은 직선 위 경계는 같은 흔들림·무늬 노이즈 위치를 쓴다. 노이즈 위치는 직선이 처음 나온 순서대로 seed에서 뽑는다
    private static Boundary[] CreateBoundaries()
    {
        var wobbleRandom = new System.Random(WobbleSeed);
        var patchRandom = new System.Random(PatchSeed);
        var lineOffsets = new Dictionary<Vector2, Vector2[]>();
        var boundaries = new Boundary[BoundaryCellPairs.Length];
        for (int i = 0; i < BoundaryCellPairs.Length; i++)
        {
            Boundary boundary = CreateBoundary(BoundaryCellPairs[i][0], BoundaryCellPairs[i][1]);
            var lineKey = new Vector2(boundary.IsVertical ? 1f : 0f, boundary.Position);
            if (!lineOffsets.TryGetValue(lineKey, out Vector2[] offsets))
            {
                offsets = new[] { CreateNoiseOffset(wobbleRandom), CreateNoiseOffset(patchRandom) };
                lineOffsets[lineKey] = offsets;
            }
            boundary.WobbleOffset = offsets[0];
            boundary.PatchOffset = offsets[1];
            boundaries[i] = boundary;
        }
        return boundaries;
    }

    // 두 칸이 맞닿은 변 (칸 크기는 MapGrid가 정한다). 3 × 3 격자라 맞닿은 두 칸의 변 길이가 같다
    private static Boundary CreateBoundary(string first, string second)
    {
        int firstCell = System.Array.IndexOf(CellNames, first);
        int secondCell = System.Array.IndexOf(CellNames, second);
        Rect a = CellRects[firstCell];
        Rect b = CellRects[secondCell];
        bool isVertical = Mathf.Approximately(a.xMax, b.xMin) || Mathf.Approximately(a.xMin, b.xMax);
        bool isFirstLow = isVertical ? a.center.x < b.center.x : a.center.y < b.center.y;
        Rect low = isFirstLow ? a : b;
        return new Boundary
        {
            Label = $"{first}|{second}",
            IsVertical = isVertical,
            Position = isVertical ? low.xMax : low.yMax,
            AlongMin = isVertical ? low.yMin : low.xMin,
            AlongMax = isVertical ? low.yMax : low.xMax,
            Cells = isFirstLow ? new[] { firstCell, secondCell } : new[] { secondCell, firstCell }
        };
    }

    private static Vector2 CreateNoiseOffset(System.Random random)
    {
        return new Vector2(RandomRange(random, NoiseOffsetMin, NoiseOffsetMax), RandomRange(random, NoiseOffsetMin, NoiseOffsetMax));
    }

    // 렌더러마다 범위 XZ를 margin만큼 넓힌 사각형을 담는다. 묶음이 없는 구역(예 : Center/Landmarks)은 0개
    private static int AddFootprints(Dictionary<Vector2Int, List<Rect>> areas, Transform parent, float margin)
    {
        if (parent == null) return 0;

        Renderer[] renderers = parent.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
            AddRect(areas, ToXZRect(renderer.bounds, margin));
        return renderers.Length;
    }

    // Perlin 값은 가운데에 몰려 있어 몫과 바로 견주면 비율이 맞지 않는다. 여러 곳의 값을 정렬해 두고 순위(0 ~ 1)로 바꾼다 (구분 노이즈 분위수와 같은 방식)
    private static float[] CreateUniformTable()
    {
        var samples = new float[UniformSampleCount * UniformSampleCount];
        for (int z = 0; z < UniformSampleCount; z++)
        {
            for (int x = 0; x < UniformSampleCount; x++)
                samples[z * UniformSampleCount + x] = Mathf.PerlinNoise(NoiseOffsetMin + x * UniformSampleStep, NoiseOffsetMin + z * UniformSampleStep);
        }
        System.Array.Sort(samples);
        return samples;
    }

    private static float ToUniform(float[] table, float value)
    {
        int index = System.Array.BinarySearch(table, value);
        if (index < 0) index = ~index;
        return (float)Mathf.Min(index, table.Length - 1) / table.Length;
    }

    // ---------- 경계 계산 ----------

    // 칸 번호 (CellNames 순서). Rect.Contains는 최소 쪽만 포함해 경계 위 점도 한 칸에만 든다. 맵 밖이면 -1
    private static int GetCell(Vector2 position)
    {
        for (int i = 0; i < CellRects.Length; i++)
        {
            if (CellRects[i].Contains(position)) return i;
        }
        return -1;
    }

    private static float GetAlong(Boundary boundary, Vector2 position)
    {
        return boundary.IsVertical ? position.y : position.x;
    }

    // 곧은 선에서 높은 쪽(동, 북)으로 + 인 거리
    private static float GetAcross(Boundary boundary, Vector2 position)
    {
        return (boundary.IsVertical ? position.x : position.y) - boundary.Position;
    }

    private static Vector2 ToPosition(Boundary boundary, float along, float across)
    {
        return boundary.IsVertical ? new Vector2(boundary.Position + across, along) : new Vector2(along, boundary.Position + across);
    }

    // 곧은 선 너머 거울 자리
    private static Vector2 Mirror(Boundary boundary, Vector2 position)
    {
        return ToPosition(boundary, GetAlong(boundary, position), -GetAcross(boundary, position));
    }

    // 흔들린 중심선이 곧은 선에서 높은 쪽으로 벗어난 거리 (±WobbleAmplitude)
    private static float GetWobble(Boundary boundary, float along)
    {
        float noise = Mathf.PerlinNoise(along / WobblePeriod + boundary.WobbleOffset.x, boundary.WobbleOffset.y);
        return Mathf.Clamp((noise * 2f - 1f) * WobbleAmplitude, -WobbleAmplitude, WobbleAmplitude);
    }

    // 흔들린 중심선에서 side 칸 쪽으로 잰 거리 (건너편 쪽이면 음수)
    private static float GetOwnDistance(Boundary boundary, int side, Vector2 position)
    {
        float offset = GetAcross(boundary, position) - GetWobble(boundary, GetAlong(boundary, position));
        return side == HighSide ? offset : -offset;
    }

    // 자기 쪽으로 ownDistance인 곳의 건너편 몫. 중심선 0.5, 자기 쪽 띠 끝 0
    private static float GetOtherShare(float ownDistance)
    {
        return Mathf.Clamp01(0.5f - 0.5f * ownDistance / BlendHalfWidth);
    }

    // 위치가 든 경계 띠 (그 경계의 두 칸 중 하나이고 곧은 선까지 ReachDistance 안). 두 띠에 들면 곧은 선에 더 가까운 경계. 없으면 -1
    private static int FindBoundary(Boundary[] boundaries, Vector2 position)
    {
        int cell = GetCell(position);
        int best = -1;
        float bestDistance = ReachDistance;
        for (int i = 0; i < boundaries.Length; i++)
        {
            Boundary boundary = boundaries[i];
            if (cell != boundary.Cells[LowSide] && cell != boundary.Cells[HighSide]) continue;

            float distance = Mathf.Abs(GetAcross(boundary, position));
            if (distance >= bestDistance) continue;
            best = i;
            bestDistance = distance;
        }
        return best;
    }

    // ---------- 지면 레이어 ----------

    // 거울 짝마다 한쪽 칸만 짝 칸 값을 가져온다. 맵 전체를 읽어 가져오는 칸만 바꾼 뒤 그대로 다시 쓴다
    private static AlphaCheck BlendAlphamaps(BlendContext context)
    {
        TerrainData data = context.Terrain.terrainData;
        AlphaGrid grid = CreateAlphaGrid(context);
        int resolution = grid.Resolution;
        float[,,] alphas = data.GetAlphamaps(0, 0, resolution, resolution);
        // (가져온 칸, 원본 칸)의 1차원 번호
        var copies = new List<Vector2Int>();
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
                BlendTexel(context, grid, alphas, new Vector2Int(x, z), copies);
        }
        data.SetAlphamaps(0, 0, alphas);
        return CheckAlphamaps(data, alphas, copies);
    }

    private static AlphaGrid CreateAlphaGrid(BlendContext context)
    {
        TerrainData data = context.Terrain.terrainData;
        Vector3 position = context.Terrain.transform.position;
        var grid = new AlphaGrid
        {
            Resolution = data.alphamapResolution,
            Origin = new Vector2(position.x, position.z),
            CellSize = data.size.x / data.alphamapResolution,
            Layers = data.alphamapLayers
        };
        grid.Splits = context.Boundaries.Select(boundary => GetSplit(grid, boundary)).ToArray();
        grid.Owners = new int[grid.Resolution, grid.Resolution];
        for (int z = 0; z < grid.Resolution; z++)
        {
            for (int x = 0; x < grid.Resolution; x++)
                grid.Owners[z, x] = FindTexelBoundary(context.Boundaries, grid, x, z);
        }
        return grid;
    }

    // 칸 중심이 곧은 선 이상인 첫 칸 번호 (Rect.Contains와 같은 기준)
    private static int GetSplit(AlphaGrid grid, Boundary boundary)
    {
        float origin = boundary.IsVertical ? grid.Origin.x : grid.Origin.y;
        return Mathf.CeilToInt((boundary.Position - origin) / grid.CellSize - 0.5f);
    }

    // 곧은 선에서 몇 번째 칸인지 (선에 붙은 칸 = 0). 거울 짝은 같은 값이다
    private static int GetTexelOffset(int index, int split)
    {
        return index < split ? split - 1 - index : index - split;
    }

    private static Vector2 GetTexelCenter(AlphaGrid grid, int x, int z)
    {
        return grid.Origin + new Vector2(x + 0.5f, z + 0.5f) * grid.CellSize;
    }

    // 칸이 쓰는 경계. 연속 위치 대신 칸 수로 견줘야 거울 짝(곧은 선까지 칸 수가 같다)이 늘 같은 판정을 받는다. 같으면 남북 방향 선
    private static int FindTexelBoundary(Boundary[] boundaries, AlphaGrid grid, int x, int z)
    {
        int cell = GetCell(GetTexelCenter(grid, x, z));
        int best = -1;
        int bestOffset = int.MaxValue;
        for (int i = 0; i < boundaries.Length; i++)
        {
            Boundary boundary = boundaries[i];
            if (cell != boundary.Cells[LowSide] && cell != boundary.Cells[HighSide]) continue;

            int offset = GetTexelOffset(boundary.IsVertical ? x : z, grid.Splits[i]);
            if ((offset + 0.5f) * grid.CellSize >= ReachDistance) continue;
            if (offset > bestOffset || (offset == bestOffset && !boundary.IsVertical)) continue;
            best = i;
            bestOffset = offset;
        }
        return best;
    }

    // 낮은 쪽 칸에서 짝을 찾아 한 번만 처리한다. 높은 쪽 칸은 짝이 다른 경계를 쓸 때만 센다
    private static void BlendTexel(BlendContext context, AlphaGrid grid, float[,,] alphas, Vector2Int texel, List<Vector2Int> copies)
    {
        int owner = grid.Owners[texel.y, texel.x];
        if (owner < 0) return;

        Boundary boundary = context.Boundaries[owner];
        int split = grid.Splits[owner];
        int index = boundary.IsVertical ? texel.x : texel.y;
        int partnerIndex = 2 * split - 1 - index;
        Vector2Int partner = boundary.IsVertical ? new Vector2Int(partnerIndex, texel.y) : new Vector2Int(texel.x, partnerIndex);
        BoundaryStats stats = context.Stats[owner];
        if (grid.Owners[partner.y, partner.x] != owner)
        {
            stats.UnpairedCount++;
            return;
        }
        if (index >= split) return;

        stats.PairCount++;
        int copySide = PickCopySide(context, grid, boundary, texel, partner);
        if (copySide < 0) return;

        Vector2Int destination = copySide == LowSide ? texel : partner;
        Vector2Int source = copySide == LowSide ? partner : texel;
        bool isChanged = false;
        for (int layer = 0; layer < grid.Layers; layer++)
        {
            float value = alphas[source.y, source.x, layer];
            if (alphas[destination.y, destination.x, layer] != value) isChanged = true;
            alphas[destination.y, destination.x, layer] = value;
        }
        stats.CopyCounts[copySide]++;
        if (isChanged) stats.ChangedCount++;
        copies.Add(new Vector2Int(destination.y * grid.Resolution + destination.x, source.y * grid.Resolution + source.x));
    }

    // 가져오는 쪽 (LowSide, HighSide). 둘 다 그대로면 -1.
    // 두 칸의 흔들린 중심선까지 거리 합 = 두 칸 중심 사이 거리 > 0이라 두 몫의 합은 1보다 작고, 아래 두 조건이 함께 맞는 일은 없다
    private static int PickCopySide(BlendContext context, AlphaGrid grid, Boundary boundary, Vector2Int low, Vector2Int high)
    {
        Vector2 lowCenter = GetTexelCenter(grid, low.x, low.y);
        Vector2 highCenter = GetTexelCenter(grid, high.x, high.y);
        float lowShare = GetOtherShare(GetOwnDistance(boundary, LowSide, lowCenter));
        float highShare = GetOtherShare(GetOwnDistance(boundary, HighSide, highCenter));

        // 짝마다 하나인 값. 무늬 노이즈를 (경계를 따라 간 거리, 곧은 선에서 짝까지 거리) 자리에서 읽는다
        float distance = Mathf.Abs(GetAcross(boundary, lowCenter));
        float noise = Mathf.PerlinNoise(GetAlong(boundary, lowCenter) / PatchNoisePeriod + boundary.PatchOffset.x, distance / PatchNoisePeriod + boundary.PatchOffset.y);
        float u = ToUniform(context.UniformTable, noise);
        if (u < lowShare) return LowSide;
        if (u >= 1f - highShare) return HighSide;
        return -1;
    }

    // 다시 읽어 (1) 쓴 값과 저장된 값이 같은지 (2) 가져온 칸이 원본 칸과 같은지(= 다시 실행해도 바뀌지 않는지) (3) 레이어 합을 본다
    private static AlphaCheck CheckAlphamaps(TerrainData data, float[,,] written, List<Vector2Int> copies)
    {
        int resolution = data.alphamapResolution;
        int layers = data.alphamapLayers;
        float[,,] stored = data.GetAlphamaps(0, 0, resolution, resolution);
        var check = new AlphaCheck();
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                for (int layer = 0; layer < layers; layer++)
                {
                    if (Mathf.Abs(stored[z, x, layer] - written[z, x, layer]) <= AlphaTolerance) continue;
                    check.StoredMismatchCount++;
                    break;
                }
            }
        }

        foreach (Vector2Int copy in copies)
        {
            int destinationZ = copy.x / resolution, destinationX = copy.x % resolution;
            int sourceZ = copy.y / resolution, sourceX = copy.y % resolution;
            float sum = 0f;
            bool isDifferent = false;
            for (int layer = 0; layer < layers; layer++)
            {
                sum += stored[destinationZ, destinationX, layer];
                if (Mathf.Abs(stored[destinationZ, destinationX, layer] - stored[sourceZ, sourceX, layer]) > AlphaTolerance) isDifferent = true;
            }
            if (isDifferent) check.RerunChangeCount++;
            check.SumMin = Mathf.Min(check.SumMin, sum);
            check.SumMax = Mathf.Max(check.SumMax, sum);
        }
        return check;
    }

    // ---------- 나무 ----------

    // 채운 나무를 지우고, 기준 밀도를 잰 뒤 솎고 채운다. 로그용으로 남은 원래 나무와 채운 나무를 돌려준다
    private static List<TreeRecord> BlendTrees(BlendContext context)
    {
        ResetBoundaryParents(context);
        List<TreeRecord> trees = CollectTrees(context);
        MeasureDensities(context, trees);
        List<TreeRecord> survivors = ThinTrees(context, trees);
        foreach (TreeRecord tree in survivors)
            AddPoint(context.TreePoints, tree.Position);
        List<TreeRecord> filled = FillTrees(context, survivors);
        return survivors.Concat(filled).ToList();
    }

    // <구역>/Trees/Boundary가 없으면 만들고, 있으면 자식을 모두 지운다
    private static void ResetBoundaryParents(BlendContext context)
    {
        foreach (int cell in GetUsedCells(context.Boundaries))
        {
            Transform trees = context.TreeParents[cell];
            Transform group = trees.Find(BoundaryGroupName);
            if (group == null)
            {
                group = new GameObject(BoundaryGroupName).transform;
                group.SetParent(trees, false);
            }
            for (int i = group.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(group.GetChild(i).gameObject);
            context.BoundaryParents[cell] = group;
        }
    }

    // 구역 배치 메뉴가 놓은 나무 (<구역>/Trees의 바로 아래 자식, Boundary 묶음 제외)
    private static List<TreeRecord> CollectTrees(BlendContext context)
    {
        var records = new List<TreeRecord>();
        foreach (int cell in GetUsedCells(context.Boundaries))
        {
            foreach (Transform child in context.TreeParents[cell])
            {
                if (child == context.BoundaryParents[cell]) continue;

                Vector2 position = ToXZ(child.position);
                int boundaryIndex = FindBoundary(context.Boundaries, position);
                int side = boundaryIndex >= 0 && GetCell(position) == context.Boundaries[boundaryIndex].Cells[HighSide] ? HighSide : LowSide;
                records.Add(new TreeRecord { Transform = child, Position = position, BoundaryIndex = boundaryIndex, Side = side });
            }
        }
        return records;
    }

    private static void MeasureDensities(BlendContext context, List<TreeRecord> trees)
    {
        foreach (Boundary boundary in context.Boundaries)
        {
            int chunkCount = Mathf.RoundToInt((boundary.AlongMax - boundary.AlongMin) / DensityChunkLength);
            boundary.Densities = new float[2, chunkCount];
            for (int side = LowSide; side <= HighSide; side++)
            {
                var counts = new int[chunkCount];
                foreach (TreeRecord tree in trees)
                {
                    int chunk = GetReferenceChunk(context, boundary, side, tree.Position);
                    if (chunk >= 0) counts[chunk]++;
                }
                SetDensities(boundary, side, counts, MeasureReferenceAreas(context, boundary, side, chunkCount));
            }
        }
    }

    // 기준 띠 (side 칸 안, 곧은 선에서 ReachDistance ~ + ReferenceDepth, 어느 경계 띠에도 들지 않음) 안이면 구간 번호, 아니면 -1
    private static int GetReferenceChunk(BlendContext context, Boundary boundary, int side, Vector2 position)
    {
        if (GetCell(position) != boundary.Cells[side]) return -1;

        float depth = side == HighSide ? GetAcross(boundary, position) : -GetAcross(boundary, position);
        if (depth < ReachDistance || depth >= ReachDistance + ReferenceDepth) return -1;
        if (FindBoundary(context.Boundaries, position) >= 0) return -1;
        return Mathf.Min((int)((GetAlong(boundary, position) - boundary.AlongMin) / DensityChunkLength), boundary.Densities.GetLength(1) - 1);
    }

    // 구간마다 기준 띠 중 다른 경계 띠에 들지 않은 넓이 (m²)
    private static float[] MeasureReferenceAreas(BlendContext context, Boundary boundary, int side, int chunkCount)
    {
        var areas = new float[chunkCount];
        float sign = side == HighSide ? 1f : -1f;
        float sampleArea = AreaSampleSpacing * AreaSampleSpacing;
        for (float along = boundary.AlongMin + AreaSampleSpacing / 2f; along < boundary.AlongMax; along += AreaSampleSpacing)
        {
            for (float depth = ReachDistance + AreaSampleSpacing / 2f; depth < ReachDistance + ReferenceDepth; depth += AreaSampleSpacing)
            {
                int chunk = GetReferenceChunk(context, boundary, side, ToPosition(boundary, along, sign * depth));
                if (chunk >= 0) areas[chunk] += sampleArea;
            }
        }
        return areas;
    }

    // 넓이가 모자란 구간(다른 경계 띠에 많이 걸린 교차점 쪽)은 가장 가까운 구간 값을 빌린다
    private static void SetDensities(Boundary boundary, int side, int[] counts, float[] areas)
    {
        float minArea = DensityChunkLength * ReferenceDepth * ReferenceMinAreaRatio;
        for (int chunk = 0; chunk < counts.Length; chunk++)
        {
            if (areas[chunk] >= minArea) boundary.Densities[side, chunk] = counts[chunk] / areas[chunk] * HectareArea;
        }
        for (int chunk = 0; chunk < counts.Length; chunk++)
        {
            if (areas[chunk] >= minArea) continue;
            boundary.Densities[side, chunk] = boundary.Densities[side, FindNearestValidChunk(areas, chunk, minArea)];
            boundary.BorrowedChunkCount++;
        }
    }

    // 가까운 순, 같으면 앞 구간
    private static int FindNearestValidChunk(float[] areas, int chunk, float minArea)
    {
        for (int distance = 1; distance < areas.Length; distance++)
        {
            if (chunk - distance >= 0 && areas[chunk - distance] >= minArea) return chunk - distance;
            if (chunk + distance < areas.Length && areas[chunk + distance] >= minArea) return chunk + distance;
        }
        return chunk;
    }

    // 구간 중심 사이를 직선으로 이은 기준 밀도
    private static float GetDensity(Boundary boundary, int side, float along)
    {
        int count = boundary.Densities.GetLength(1);
        float position = Mathf.Clamp((along - boundary.AlongMin) / DensityChunkLength - 0.5f, 0f, count - 1);
        int index = Mathf.Min((int)position, count - 2);
        return Mathf.Lerp(boundary.Densities[side, index], boundary.Densities[side, index + 1], position - index);
    }

    private static List<TreeRecord> ThinTrees(BlendContext context, List<TreeRecord> trees)
    {
        var survivors = new List<TreeRecord>();
        foreach (TreeRecord tree in trees)
        {
            if (tree.BoundaryIndex >= 0 && !ShouldKeep(context, tree))
            {
                context.Stats[tree.BoundaryIndex].ThinCount++;
                Object.DestroyImmediate(tree.Transform.gameObject);
                continue;
            }
            survivors.Add(tree);
        }
        return survivors;
    }

    // 빽빽한 쪽 나무만 (목표 밀도 / 빽빽한 쪽 기준 밀도) 확률로 남긴다. 성긴 쪽 나무는 그대로 둔다
    private static bool ShouldKeep(BlendContext context, TreeRecord tree)
    {
        Boundary boundary = context.Boundaries[tree.BoundaryIndex];
        float along = GetAlong(boundary, tree.Position);
        float own = GetDensity(boundary, tree.Side, along);
        float other = GetDensity(boundary, 1 - tree.Side, along);
        if (own <= other) return true;

        float ownShare = 1f - GetOtherShare(GetOwnDistance(boundary, tree.Side, tree.Position));
        float target = other + (own - other) * ownShare;
        return GetHash(tree.Position, ThinSeed) < target / own;
    }

    // 남은 빽빽한 쪽 나무를 곧은 선 너머 거울 자리에 놓는다. 거울 자리에 모자란 밀도 ≤ 원본 자리 목표 밀도라 확률은 1을 넘지 않는다
    private static List<TreeRecord> FillTrees(BlendContext context, List<TreeRecord> survivors)
    {
        var filled = new List<TreeRecord>();
        foreach (TreeRecord source in survivors)
        {
            if (source.BoundaryIndex < 0) continue;

            Boundary boundary = context.Boundaries[source.BoundaryIndex];
            float along = GetAlong(boundary, source.Position);
            float dense = GetDensity(boundary, source.Side, along);
            float sparse = GetDensity(boundary, 1 - source.Side, along);
            if (dense <= sparse) continue;

            Vector2 target = Mirror(boundary, source.Position);
            float missing = (dense - sparse) * GetOtherShare(GetOwnDistance(boundary, 1 - source.Side, target));
            float sourceDensity = sparse + (dense - sparse) * (1f - GetOtherShare(GetOwnDistance(boundary, source.Side, source.Position)));
            if (GetHash(source.Position, FillSeed) >= missing / sourceDensity) continue;

            BoundaryStats stats = context.Stats[source.BoundaryIndex];
            stats.FillCandidateCount++;
            SkipReason? reason = FindSkipReason(context, source, target);
            GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(source.Transform.gameObject);
            if (reason == null && prefab == null) reason = SkipReason.NoPrefab;
            if (reason != null)
            {
                stats.SkipCounts[(int)reason.Value]++;
                continue;
            }
            filled.Add(PlaceFill(context, source, prefab, target));
        }
        return filled;
    }

    private static SkipReason? FindSkipReason(BlendContext context, TreeRecord source, Vector2 target)
    {
        if (FindBoundary(context.Boundaries, target) != source.BoundaryIndex) return SkipReason.Corner;

        SkipReason? forbidden = FindForbiddenReason(context, target);
        if (forbidden != null) return forbidden;
        if (IsNearPoint(context.TreePoints, target, TreeMinSpacing)) return SkipReason.Tree;
        return null;
    }

    // prefab 연결을 유지한 채 놓는다. Static 플래그는 켜지 않는다 (GPU Resident Drawer로 그림)
    private static TreeRecord PlaceFill(BlendContext context, TreeRecord source, GameObject prefab, Vector2 target)
    {
        Boundary boundary = context.Boundaries[source.BoundaryIndex];
        int side = 1 - source.Side;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, context.BoundaryParents[boundary.Cells[side]]);
        instance.transform.SetPositionAndRotation(
            ToWorld(target, GetGroundHeight(context.Terrain, target)),
            Quaternion.Euler(0f, GetHash(target, YawSeed) * 360f, 0f));
        instance.transform.localScale = source.Transform.localScale;

        BoundaryStats stats = context.Stats[source.BoundaryIndex];
        if (MapTreeScatter.AddMissingCollider(instance, prefab.name)) stats.ColliderCount++;
        ClearStaticFlags(instance);
        stats.FillCounts[side]++;
        AddPoint(context.TreePoints, target);
        return new TreeRecord { Transform = instance.transform, Position = target, BoundaryIndex = source.BoundaryIndex, Side = side };
    }

    // Static Batching과 섞지 않는다 (MapLayout.md 성능 기준)
    private static void ClearStaticFlags(GameObject instance)
    {
        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetStaticEditorFlags(child.gameObject) != 0) GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
        }
    }

    // ---------- 채우지 않는 곳 ----------

    // 다른 나무 간격을 뺀 금지 구역. 없으면 null
    private static SkipReason? FindForbiddenReason(BlendContext context, Vector2 position)
    {
        if (IsInClearCircle(position)) return SkipReason.Habitat;
        if (IsWater(context, position)) return SkipReason.Water;
        if (IsOnPath(context, position)) return SkipReason.Path;
        if (GetSlope(context.Terrain, position) >= MapTreeScatter.MaxSlope) return SkipReason.Slope;
        if (IsInAreas(context.LandmarkAreas, position)) return SkipReason.Landmark;
        if (IsInAreas(context.RockAreas, position)) return SkipReason.Rock;
        return null;
    }

    private static bool IsInClearCircle(Vector2 position)
    {
        for (int i = 0; i < ClearCenters.Length; i++)
        {
            if (Vector2.Distance(position, ClearCenters[i]) < ClearRadii[i]) return true;
        }
        return false;
    }

    // 강은 중심선 거리로, 호수와 C3 물은 피벗과 둘레 TreeFootprintRadius 4곳의 지면 높이로 본다 (남동쪽 배치의 나무 자리 판정과 같은 방식)
    private static bool IsWater(BlendContext context, Vector2 position)
    {
        if (MapRiverBuilder.IsNearRiver(context.RiverPath, ToWorld(position, 0f), RiverMargin)) return true;

        foreach (Vector2 point in GetFootprintPoints(position))
        {
            float ground = GetGroundHeight(context.Terrain, point);
            if (GetCell(point) == SouthEastCell && ground < SouthEastPlaceMinHeight) return true;
            if (context.Waters.Any(water => water.Bounds.Contains(point) && ground < water.Height + WaterClearHeight)) return true;
        }
        return false;
    }

    private static IEnumerable<Vector2> GetFootprintPoints(Vector2 position)
    {
        yield return position;
        yield return position + Vector2.up * TreeFootprintRadius;
        yield return position + Vector2.down * TreeFootprintRadius;
        yield return position + Vector2.left * TreeFootprintRadius;
        yield return position + Vector2.right * TreeFootprintRadius;
    }

    private static bool IsOnPath(BlendContext context, Vector2 position)
    {
        if (MapTrailBuilder.IsNearTrail(context.Trails, ToWorld(position, 0f), MapTreeScatter.TrailClearMargin)) return true;
        return context.CartRoad != null && GetPathDistance(context.CartRoad, position) < CartRoadHalfWidth + LandmarkClearance;
    }

    // 길 선(선분 목록)까지 가장 짧은 거리. MapEastScatter.GetPathDistance와 같은 방식이다. 그쪽이 private이라 여기 다시 둔다
    private static float GetPathDistance(List<Vector2> path, Vector2 position)
    {
        float distance = float.PositiveInfinity;
        for (int i = 1; i < path.Count; i++)
            distance = Mathf.Min(distance, GetSegmentDistance(position, path[i - 1], path[i]));
        return distance;
    }

    private static float GetSegmentDistance(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 segment = to - from;
        float lengthSquared = segment.sqrMagnitude;
        float t = lengthSquared > 0f ? Mathf.Clamp01(Vector2.Dot(point - from, segment) / lengthSquared) : 0f;
        return Vector2.Distance(point, from + segment * t);
    }

    // ---------- 칸으로 나눠 담기 ----------

    private static Vector2Int GetHashCell(Vector2 position)
    {
        return new Vector2Int(Mathf.FloorToInt(position.x / HashCellSize), Mathf.FloorToInt(position.y / HashCellSize));
    }

    private static void AddRect(Dictionary<Vector2Int, List<Rect>> areas, Rect rect)
    {
        Vector2Int min = GetHashCell(rect.min);
        Vector2Int max = GetHashCell(rect.max);
        for (int z = min.y; z <= max.y; z++)
        {
            for (int x = min.x; x <= max.x; x++)
            {
                var key = new Vector2Int(x, z);
                if (!areas.TryGetValue(key, out List<Rect> list))
                {
                    list = new List<Rect>();
                    areas[key] = list;
                }
                list.Add(rect);
            }
        }
    }

    private static bool IsInAreas(Dictionary<Vector2Int, List<Rect>> areas, Vector2 position)
    {
        return areas.TryGetValue(GetHashCell(position), out List<Rect> list) && list.Any(rect => rect.Contains(position));
    }

    private static void AddPoint(Dictionary<Vector2Int, List<Vector2>> points, Vector2 position)
    {
        Vector2Int key = GetHashCell(position);
        if (!points.TryGetValue(key, out List<Vector2> list))
        {
            list = new List<Vector2>();
            points[key] = list;
        }
        list.Add(position);
    }

    // distance는 HashCellSize 이하라 이웃 3 × 3칸만 본다
    private static bool IsNearPoint(Dictionary<Vector2Int, List<Vector2>> points, Vector2 position, float distance)
    {
        Vector2Int center = GetHashCell(position);
        for (int z = center.y - 1; z <= center.y + 1; z++)
        {
            for (int x = center.x - 1; x <= center.x + 1; x++)
            {
                if (!points.TryGetValue(new Vector2Int(x, z), out List<Vector2> list)) continue;
                if (list.Any(point => Vector2.Distance(point, position) < distance)) return true;
            }
        }
        return false;
    }

    // ---------- 공통 계산 ----------

    // 위치 해시 (0 ~ 1). 같은 위치와 seed는 늘 같은 값이다
    private static float GetHash(Vector2 position, int seed)
    {
        unchecked
        {
            uint hash = (uint)seed;
            hash = MixHash(hash ^ (uint)Mathf.RoundToInt(position.x * HashScale));
            hash = MixHash(hash ^ (uint)Mathf.RoundToInt(position.y * HashScale));
            return (hash >> 8) / 16777216f;
        }
    }

    // 32비트 정수 섞기 (비트를 고르게 퍼뜨린다)
    private static uint MixHash(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return value;
        }
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

    private static Rect ToXZRect(Bounds bounds, float margin)
    {
        return Rect.MinMaxRect(bounds.min.x - margin, bounds.min.z - margin, bounds.max.x + margin, bounds.max.z + margin);
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
    // 검수(흔들림 범위, 경계별 섞인 칸 수, 다시 실행 시 바뀌는 칸 0, 솎은·채운 나무 수, 금지 구역에 놓인 나무 0, 띠 안 거리별 밀도)에 쓰는 값이라 남긴다

    private static string GetSideName(Boundary boundary, int side)
    {
        return CellNames[boundary.Cells[side]];
    }

    private static void LogWobble(BlendContext context)
    {
        var lines = new List<string>();
        foreach (Boundary boundary in context.Boundaries)
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            for (float along = boundary.AlongMin; along <= boundary.AlongMax; along += WobbleLogStep)
            {
                float wobble = GetWobble(boundary, along);
                min = Mathf.Min(min, wobble);
                max = Mathf.Max(max, wobble);
            }
            lines.Add($"{boundary.Label} : {min:+0.0;-0.0}m ~ {max:+0.0;-0.0}m");
        }
        Debug.Log($"[MapBoundaryBlender] 흔들린 중심선 - 곧은 선 (동·북 +, 허용 ±{WobbleAmplitude}m, 주기 {WobblePeriod}m)\n" + string.Join("\n", lines));
    }

    private static void LogAlphamaps(BlendContext context, AlphaCheck check)
    {
        var lines = new List<string>();
        for (int i = 0; i < context.Boundaries.Length; i++)
        {
            Boundary boundary = context.Boundaries[i];
            BoundaryStats stats = context.Stats[i];
            int copied = stats.CopyCounts[LowSide] + stats.CopyCounts[HighSide];
            float ratio = stats.PairCount > 0 ? copied / (2f * stats.PairCount) : 0f;
            lines.Add($"{boundary.Label} : 짝 {stats.PairCount}쌍, 섞인 칸 {copied} (짝 칸의 {ratio:P1}. {GetSideName(boundary, LowSide)} 칸 ← {GetSideName(boundary, HighSide)} {stats.CopyCounts[LowSide]}, " +
                      $"{GetSideName(boundary, HighSide)} 칸 ← {GetSideName(boundary, LowSide)} {stats.CopyCounts[HighSide]}), 값이 실제로 바뀐 칸 {stats.ChangedCount}, " +
                      $"짝이 다른 경계라 안 섞은 칸 {stats.UnpairedCount}");
        }
        string sums = check.SumMax >= check.SumMin ? $"{check.SumMin:F3} ~ {check.SumMax:F3}" : "-";
        Debug.Log($"[MapBoundaryBlender] 지면 레이어 (거울 짝마다 한쪽 칸만 짝 칸 값을 가져옴, 무늬 주기 {PatchNoisePeriod}m, 띠 밖 칸은 쓰지 않음)\n" + string.Join("\n", lines));
        Debug.Log($"[MapBoundaryBlender] 쓴 값과 다시 읽은 값이 다른 칸 {check.StoredMismatchCount}, 다시 실행하면 바뀔 칸(가져온 칸 ≠ 원본 칸) {check.RerunChangeCount} (둘 다 0이어야 한다), " +
                  $"섞인 칸 레이어 합 {sums}");
    }

    private static void LogTrees(BlendContext context)
    {
        var lines = new List<string>();
        for (int i = 0; i < context.Boundaries.Length; i++)
        {
            Boundary boundary = context.Boundaries[i];
            BoundaryStats stats = context.Stats[i];
            int chunkCount = boundary.Densities.GetLength(1);
            int lowDenseCount = Enumerable.Range(0, chunkCount).Count(chunk => boundary.Densities[LowSide, chunk] > boundary.Densities[HighSide, chunk]);
            int highDenseCount = Enumerable.Range(0, chunkCount).Count(chunk => boundary.Densities[HighSide, chunk] > boundary.Densities[LowSide, chunk]);
            string low = GetSideName(boundary, LowSide);
            string high = GetSideName(boundary, HighSide);
            string skips = string.Join(", ", Enumerable.Range(0, SkipLabels.Length)
                .Where(reason => stats.SkipCounts[reason] > 0)
                .Select(reason => $"{SkipLabels[reason]} {stats.SkipCounts[reason]}"));
            lines.Add($"{boundary.Label} : 기준 밀도 평균 {low} {AverageDensity(boundary, LowSide):F1} / {high} {AverageDensity(boundary, HighSide):F1} (1ha당), " +
                      $"빽빽한 쪽 구간 {low} {lowDenseCount} · {high} {highDenseCount} / {chunkCount} (빌린 구간 {boundary.BorrowedChunkCount}), " +
                      $"솎은 나무 {stats.ThinCount}, 채운 나무 {stats.FillCounts[LowSide] + stats.FillCounts[HighSide]} ({low}에 {stats.FillCounts[LowSide]}, {high}에 {stats.FillCounts[HighSide]}, " +
                      $"후보 {stats.FillCandidateCount}), 건너뜀 : {(skips.Length > 0 ? skips : "없음")}");
        }
        int thinned = context.Stats.Sum(stats => stats.ThinCount);
        int filled = context.Stats.Sum(stats => stats.FillCounts.Sum());
        int groups = context.BoundaryParents.Count(parent => parent != null);
        Debug.Log($"[MapBoundaryBlender] 나무 (기준 띠 = 곧은 선에서 {ReachDistance} ~ {ReachDistance + ReferenceDepth}m, 구간 {DensityChunkLength}m)\n" + string.Join("\n", lines));
        Debug.Log($"[MapBoundaryBlender] 합계 솎은 나무 {thinned}, 채운 나무 {filled}, 배치 GameObject 증감 {filled - thinned:+0;-0;0} (+ Boundary 묶음 {groups}개), " +
                  $"충돌체 추가 {context.Stats.Sum(stats => stats.ColliderCount)}. 비움 사각형 : 랜드마크 렌더러 {context.LandmarkRendererCount}, 바위·데모 렌더러 {context.RockRendererCount}, " +
                  $"호수 수면 {context.Waters.Count}, 수레길 {(context.CartRoad != null ? "있음" : "없음")}");
    }

    private static float AverageDensity(Boundary boundary, int side)
    {
        int count = boundary.Densities.GetLength(1);
        return Enumerable.Range(0, count).Average(chunk => boundary.Densities[side, chunk]);
    }

    // 놓인 채운 나무를 다시 읽어 금지 구역에 든 수를 센다 (0이어야 한다)
    private static void LogForbidden(BlendContext context)
    {
        var counts = new int[SkipLabels.Length];
        int total = 0;
        foreach (Transform group in context.BoundaryParents.Where(parent => parent != null))
        {
            foreach (Transform tree in group)
            {
                total++;
                SkipReason? reason = FindForbiddenReason(context, ToXZ(tree.position));
                if (reason != null) counts[(int)reason.Value]++;
            }
        }
        string details = string.Join(", ", Enumerable.Range((int)SkipReason.Habitat, (int)SkipReason.Rock).Select(reason => $"{SkipLabels[reason]} {counts[reason]}"));
        Debug.Log($"[MapBoundaryBlender] 채운 나무 {total}그루 중 금지 구역에 놓인 나무 {counts.Sum()} (0이어야 한다) : {details}");
    }

    // 경계마다 흔들린 중심선에서 잰 거리 구간별 1ha당 나무 수 (남은 원래 나무 + 채운 나무). 넓이는 그 경계를 쓰는 표본점으로 잰다
    private static void LogDensityBins(BlendContext context, List<TreeRecord> trees)
    {
        int binCount = Mathf.RoundToInt(2f * BlendHalfWidth / DensityBinWidth);
        var lines = new List<string>();
        for (int i = 0; i < context.Boundaries.Length; i++)
        {
            Boundary boundary = context.Boundaries[i];
            var counts = new int[binCount];
            foreach (TreeRecord tree in trees.Where(tree => tree.BoundaryIndex == i))
            {
                int bin = GetDensityBin(boundary, tree.Position, binCount);
                if (bin >= 0) counts[bin]++;
            }
            float[] areas = MeasureBinAreas(context, i, binCount);
            string bins = string.Join(", ", Enumerable.Range(0, binCount).Select(bin =>
                $"{bin * DensityBinWidth - BlendHalfWidth:+0;-0;0}~{(bin + 1) * DensityBinWidth - BlendHalfWidth:+0;-0;0}m " +
                $"{(areas[bin] > 0f ? counts[bin] / areas[bin] * HectareArea : 0f):F1}"));
            lines.Add($"{boundary.Label} (- = {GetSideName(boundary, LowSide)} 쪽, + = {GetSideName(boundary, HighSide)} 쪽) : {bins}");
        }
        Debug.Log("[MapBoundaryBlender] 띠 안 거리 구간별 나무 밀도 (1ha당, 흔들린 중심선 기준, 솎기·채우기 후)\n" + string.Join("\n", lines));
    }

    private static int GetDensityBin(Boundary boundary, Vector2 position, int binCount)
    {
        float offset = GetOwnDistance(boundary, HighSide, position);
        int bin = Mathf.FloorToInt((offset + BlendHalfWidth) / DensityBinWidth);
        return bin >= 0 && bin < binCount ? bin : -1;
    }

    private static float[] MeasureBinAreas(BlendContext context, int boundaryIndex, int binCount)
    {
        Boundary boundary = context.Boundaries[boundaryIndex];
        var areas = new float[binCount];
        float sampleArea = AreaSampleSpacing * AreaSampleSpacing;
        for (float along = boundary.AlongMin + AreaSampleSpacing / 2f; along < boundary.AlongMax; along += AreaSampleSpacing)
        {
            for (float across = -ReachDistance + AreaSampleSpacing / 2f; across < ReachDistance; across += AreaSampleSpacing)
            {
                Vector2 position = ToPosition(boundary, along, across);
                if (FindBoundary(context.Boundaries, position) != boundaryIndex) continue;

                int bin = GetDensityBin(boundary, position, binCount);
                if (bin >= 0) areas[bin] += sampleArea;
            }
        }
        return areas;
    }
}
