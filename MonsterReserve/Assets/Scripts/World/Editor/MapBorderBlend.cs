using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 메뉴 27 : 3 × 3 격자의 안쪽 경계 12곳에서 나무 밀도, 지면 레이어(알파맵), 바닥 식생(Terrain 디테일)을 섞어 경계가 곧은 선으로 보이지 않게 한다.
// 높이, 바위, 랜드마크, 소품은 건드리지 않는다. Terrain 레이어와 디테일 프로토타입을 새로 넣지 않는다.
// 규칙 (사용자 승인 임시값)
// - 흔들린 경계 : 곧은 경계선 + 흔들림(경계를 따라가는 좌표의 Perlin, 주기 WobblePeriod, ±WobbleAmplitude). 같은 직선 위 경계는 한 노이즈를 이어 쓴다.
// - 섞기 비율 t (건너편 몫) : 흔들린 경계까지 자기 쪽으로 잰 거리를 띠 폭(한쪽 BandHalfWidth)으로 나눠 smoothstep. 흔들린 경계 0.5, 자기 쪽 띠 끝 0.
//   흔들림이 띠보다 작아 실제로 바뀌는 곳은 곧은 선에서 ReachDistance(40 + 25 = 65m) 안이다.
// - 교차점 (±250, ±250) 부근에서 두 띠가 겹치면 곧은 선에 더 가까운 경계 하나만 쓴다 (같으면 남북 방향 선).
// 나무
// - 기준 밀도 : 경계를 따라 DensityChunkLength 구간마다, 양쪽 칸의 띠 밖 기준 띠(곧은 선에서 ReachDistance ~ + ReferenceDepth)에서 잰 1ha당 나무 수.
//   다른 경계 띠에 든 곳은 빼고 재고, 남은 넓이가 모자란 구간은 가장 가까운 구간 값을 빌린다. 구간 중심 사이는 직선으로 잇는다.
// - 목표 밀도 = 자기 쪽 기준 × (1 - t) + 건너편 기준 × t.
// - 솎기 (빽빽한 쪽) : 나무마다 (목표 / 자기 쪽 기준) 확률로 남긴다 (seed 고정). 숲 무늬를 지키며 칸 평균이 목표가 되게 한다.
// - 심기 (드문 쪽) : 띠 안 PlantCellSize 칸마다 (목표 개수 - 칸 안 나무 수)가 모자라면 그만큼 심는다 (소수 부분은 확률, seed 고정).
//   원본은 빽빽한 쪽 가까운 나무(같은 경계, 앞뒤 SourceChunkReach 구간, 곧은 선에서 ReachDistance + ReferenceDepth 안)를 하나 골라
//   그 prefab(PrefabUtility로 원본 확인)과 크기를 그대로 쓴다. 그래서 종류, 크기 분포, 고사목 비율이 빽빽한 쪽을 따른다.
//   원본은 MapTreeScatter의 나무 prefab 목록(LOD, DOTS 인스턴싱, 충돌체 규칙이 확인된 것)만 쓴다.
//   놓는 방식은 구역 배치 메뉴와 같다 : prefab 연결 유지, 피벗을 지면 높이에, y축 0 ~ 360° 무작위 회전, MapTreeScatter.AddMissingCollider.
//   이름 앞에 BlendPrefix를 붙여 놓인 자리가 속한 칸의 <구역>/Trees 바로 밑에 둔다.
// 지면 레이어 : 띠 안 알파맵 칸마다 곧은 선 너머 거울 자리 칸과 레이어 비중을 t 비율로 섞고 합 1로 맞춘다.
//   곧은 선 좌표를 담은 알파맵 칸 줄(거울 칸이 자기 자신인 칸)은 양쪽 구역 칠하기 메뉴가 칠하지 않아 9-1의 풀 그대로 남고, 섞기도 자기와 섞여 바뀌지 않는다.
//   그래서 섞은 뒤 이 줄을 선에 수직인 양옆 이웃 칸(번호 -1, +1)의 평균으로 메운다 (FillSeams, 메뉴 27-1과 같은 처리).
//   두 선이 만나는 교차점 칸은 가로 방향(x -1, +1) 양옆 평균으로 메운다. 그래서 동서 방향 선(z)을 먼저 메우고 교차점은 남북 방향 선(x) 차례에 메운다.
// 바닥 식생 : 띠 안 디테일 칸마다 t 확률(위치 해시)로 거울 자리 칸의 값(모든 프로토타입 묶음)을 통째로 가져오거나 자기 값을 둔다. 평균 내지 않는다.
// 건드리지 않는 곳 (지우기, 심기, 칠하기, 식생 모두)
// - 강 (중심선에서 물 반폭 + RiverMargin), 호수 (물이 보이는 수면 정점에서 LakeShoreMargin), C3 지면 SouthEastPlaceMinHeight 미만,
//   등산로 (중심선에서 길 반폭 + MapTreeScatter.TrailClearMargin), 동쪽 버려진 수레길 (중심선에서 CartRoadHalfWidth + LandmarkClearance),
//   모든 구역 <구역>/Landmarks 렌더러 범위 + LandmarkClearance, 서식지 반경, 거점 비움 반경.
// - 심기만 더 막는 곳 : 경사 MapTreeScatter.MaxSlope 이상, <구역>/Rocks·Demo 렌더러 범위 + TreeFootprintRadius, 다른 나무에서 TreeMinSpacing 안.
// - 바닥 식생만 더 막는 곳 : 경사 MaxSlope 이상 칸, <구역>/Rocks·Demo 렌더러 범위 + DetailFootprintMargin 발밑 (구역 칠하기 메뉴가 비워 둔 칸).
// - 지면, 바닥 식생은 자기 칸이나 거울 자리 칸 중 하나라도 이런 곳이면 그 칸을 섞지 않는다.
// 실행 순서 : ... → 25 → 26 → 27 → 10. 맨 마지막에 한 번만 실행하고 실행 후 씬을 저장한다.
//   이 메뉴만 다시 실행하면 이미 섞은 결과를 원본으로 읽어 두 번 섞인다. 앞쪽 구역 메뉴를 다시 실행했을 때만 다시 실행한다.
//   섞기 전으로 되돌리기 : 3 → 8-1 → 9-1 → 4 → 9-2 → 9-3 → 12 → 13 → 15 → 16 → 18 → 19 → 22 → 23 → 24 → 25 → 26 → 27
// 메뉴 27-1 : 경계 선 이음매(위 알파맵 칸 줄)만 메운다. 27이 끝에서 같은 메우기를 하므로 27 다음에 따로 실행할 필요는 없다.
//   양옆 이웃 칸은 바꾸지 않고 그 현재 값만 읽으므로, 몇 번을 실행해도 결과가 같다 (언제든 단독으로 실행해도 된다).
//   예 : 이음매 메우기가 없던 때 27을 실행한 씬, 구역 칠하기 메뉴만 다시 실행한 뒤의 이음매.
public static class MapBorderBlend
{
    private enum SkipReason
    {
        Habitat = 0,
        Water = 1,
        Path = 2,
        Landmark = 3,
        Slope = 4,
        Rock = 5,
        Tree = 6,
        NoSource = 7
    }

    private static readonly string[] SkipLabels = { "서식지·거점", "물", "길", "랜드마크 둘레", "경사", "바위·데모 오브젝트", "다른 나무 간격", "원본 나무 없음" };

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
        // [쪽, 구간] 기준 밀도 (1ha당 나무 수)
        public float[,] Densities;
        public int BorrowedChunkCount;
        // [쪽, 구간] 심을 때 원본으로 쓸 수 있는 나무
        public List<TreeRecord>[,] SourcePools;
    }

    private class BoundaryStats
    {
        // 곧은 선 기준 -100 ~ +100m, 25m 띠별 1ha당 나무 수 (낮은 쪽 → 높은 쪽)
        public float[] BeforeBins;
        public float[] AfterBins;
        public int ThinCount;
        // 솎을 차례였지만 건드리지 않는 곳이라 남긴 나무
        public int ProtectedKeepCount;
        public int PlantCount;
        public int[] PlantFailCounts = new int[SkipLabels.Length];
        public int ColliderCount;
        public int AlphaBlendCount;
        public int AlphaSkipCount;
        public int DetailSwapCount;
        // 거울 칸 값을 고른 칸 중 값이 원래와 같아 바뀌지 않은 칸
        public int DetailSameCount;
        public int DetailKeepCount;
        public int DetailSkipCount;
    }

    private class TreeRecord
    {
        public Transform Transform;
        public Vector2 Position;
        // 원본 나무가 솎여 지워진 뒤에도 심을 때 쓸 수 있게 크기를 따로 담는다
        public Vector3 Scale;
        // 심을 때 원본으로 쓸 수 있는 나무면 그 prefab, 아니면 null
        public GameObject SourcePrefab;
        // 든 경계 띠 번호와 그 경계에서의 쪽. 띠 밖이면 BoundaryIndex = -1
        public int BoundaryIndex;
        public int Side;
    }

    // 선분(점이면 From == To) 둘레 Radius 안을 막는다
    private struct Segment
    {
        public Vector2 From;
        public Vector2 To;
        public float Radius;
    }

    // 심을 칸 하나
    private struct PlantSpot
    {
        public int BoundaryIndex;
        public int DenseSide;
        public Rect Area;
        public float Along;
    }

    // 곧은 선 하나를 따라 맵 끝에서 끝까지 띠 폭만큼 읽은 디테일 원본
    private class DetailStrip
    {
        public bool IsVertical;
        public float Position;
        public RectInt Range;
        // [프로토타입][z, x] (Range 기준 번호)
        public int[][,] Layers;
    }

    private class BlendContext
    {
        public Terrain Terrain;
        public Boundary[] Boundaries;
        public BoundaryStats[] Stats;
        // CellNames 순서
        public Transform[] TreeParents;
        public List<Vector2> RiverPath;
        public List<List<Vector2>> Trails;
        // 메뉴 25가 길을 찾지 못했으면 null
        public List<Vector2> CartRoad;
        public HashSet<GameObject> SourcePrefabs = new HashSet<GameObject>();
        public HashSet<GameObject> DeadPrefabs = new HashSet<GameObject>();
        // 막는 곳과 나무 위치 (HashCellSize 칸으로 나눠 담는다)
        public Dictionary<Vector2Int, List<Segment>> WaterSegments = new Dictionary<Vector2Int, List<Segment>>();
        public Dictionary<Vector2Int, List<Segment>> PathSegments = new Dictionary<Vector2Int, List<Segment>>();
        public Dictionary<Vector2Int, List<Rect>> LandmarkAreas = new Dictionary<Vector2Int, List<Rect>>();
        public Dictionary<Vector2Int, List<Rect>> RockAreas = new Dictionary<Vector2Int, List<Rect>>();
        public Dictionary<Vector2Int, List<Vector2>> TreePoints = new Dictionary<Vector2Int, List<Vector2>>();
        public System.Random PlantRandom = new System.Random(PlantSeed);
        public List<TreeRecord> PlantedTrees = new List<TreeRecord>();
        public int LakePointCount;
        public int LandmarkRectCount;
        public int RockRectCount;
        public int NonSourceTreeCount;
    }

    private const int LowSide = 0;
    private const int HighSide = 1;

    // CellNames 순서의 Hierarchy 구역 부모 이름 (MapTerrainSetup.RegionNames와 같은 이름. 그쪽이 private이고 순서가 달라 칸 순서로 다시 둔다)
    private static readonly string[] CellNames = { "A1", "B1", "C1", "A2", "B2", "C2", "A3", "B3", "C3" };
    private static readonly string[] RegionNames = { "NorthWest", "North", "NorthEast", "West", "Center", "East", "SouthWest", "South", "SouthEast" };
    private static readonly Rect[] CellRects = CellNames.Select(name => MapGrid.GetRangeBounds(name, name)).ToArray();
    private static readonly int SouthEastCell = System.Array.IndexOf(CellNames, MapSouthEastShaper.SouthEastCell);

    // 안쪽 경계 12곳. 남북 방향 선을 먼저 두어, 두 띠의 곧은 선까지 거리가 같으면 남북 방향 선을 쓰게 한다
    private static readonly string[][] BoundaryCellPairs =
    {
        new[] { "A1", "B1" }, new[] { "B1", "C1" }, new[] { "A2", "B2" }, new[] { "B2", "C2" }, new[] { "A3", "B3" }, new[] { "B3", "C3" },
        new[] { "A1", "A2" }, new[] { "B1", "B2" }, new[] { "C1", "C2" }, new[] { "A2", "A3" }, new[] { "B2", "B3" }, new[] { "C2", "C3" }
    };

    // 안쪽 경계가 놓인 곧은 선 (가운데 칸 B2의 네 변). 칸 크기는 MapGrid가 정한다
    private static readonly Rect MiddleCellRect = MapGrid.GetRangeBounds("B2", "B2");
    private static readonly float[] VerticalLines = { MiddleCellRect.xMin, MiddleCellRect.xMax };
    private static readonly float[] HorizontalLines = { MiddleCellRect.yMin, MiddleCellRect.yMax };

    // 앞 메뉴들의 seed(20261109 ~ 20261161)와 겹치지 않게 둔다
    private const int WobbleSeed = 20261162;
    private const int ThinSeed = 20261163;
    private const int PlantSeed = 20261164;
    private const int DetailSeed = 20261165;

    // ---------- 띠 (사용자 승인 임시값) ----------
    private const float BandHalfWidth = 40f;
    private const float WobbleAmplitude = 25f;
    private const float WobblePeriod = 100f;
    // 곧은 선에서 이 거리 안까지만 바뀔 수 있다 (띠 + 흔들림)
    private const float ReachDistance = BandHalfWidth + WobbleAmplitude;
    // Perlin 입력이 음수가 되지 않도록 노이즈 위치를 양수 쪽으로 옮기는 범위 (다른 맵 스크립트와 같은 값)
    private const float NoiseOffsetMin = 100f;
    private const float NoiseOffsetMax = 1000f;

    // ---------- 나무 ----------
    private const string TreeParentName = "Trees";
    private const string RockParentName = "Rocks";
    private const string LandmarkParentName = "Landmarks";
    // 북쪽 데모 오브젝트 (메뉴 8-1의 <구역>/Demo)
    private const string DemoParentName = "Demo";
    // 호수 수면 (메뉴 6의 North/Lake. MapLakeBuilder의 이름과 같은 값, 그쪽이 private이라 다시 둔다)
    private const string LakeRegionName = "North";
    private const string LakeParentName = "Lake";
    // 새로 심은 나무 이름 앞에 붙인다 (로그·검수에서 구분)
    private const string BlendPrefix = "Blend_";
    // 기준 밀도 구간 길이 (브리핑 예시 값)
    private const float DensityChunkLength = 50f;
    // 기준 띠 깊이 (임시값). 띠 밖 ReachDistance(65m) ~ 125m
    private const float ReferenceDepth = 60f;
    // 기준 띠 넓이 중 다른 경계 띠를 뺀 나머지가 이 비율 미만이면 가장 가까운 구간 값을 빌린다 (임시값)
    private const float ReferenceMinAreaRatio = 0.5f;
    // 넓이를 재는 표본 간격 (m)
    private const float AreaSampleSpacing = 5f;
    private const float HectareArea = 10000f;
    // 심기 칸 (브리핑 권장 10m)
    private const float PlantCellSize = 10f;
    // 심을 자리를 칸 안에서 다시 뽑는 횟수 (임시값)
    private const int PlantAttempts = 3;
    // 원본 나무를 찾는 범위 : 심는 칸이 든 구간 앞뒤 이 구간 수 (임시값, 50m × 3 = 150m)
    private const int SourceChunkReach = 1;
    // 피벗 위치가 떠 보이는지 볼 때의 계산 오차 (다른 배치 스크립트와 같은 값)
    private const float HeightTolerance = 0.01f;

    // ---------- 건드리지 않는 곳 ----------
    // 나무 줄기·뿌리 자리 반지름. MapSouthEastScatter의 TreeFootprintRadius와 같은 값 (그쪽이 private이라 다시 둔다. 바뀌면 함께 바꾼다)
    private const float TreeFootprintRadius = 1f;
    // 다른 나무와 줄기 자리가 겹치지 않는 간격 (임시값)
    private const float TreeMinSpacing = TreeFootprintRadius * 2f;
    // 랜드마크 둘레 비움. 서쪽·남서쪽·남쪽·남동쪽·동쪽 배치의 LandmarkClearance와 같은 값 (그쪽이 private이라 다시 둔다. 바뀌면 함께 바꾼다)
    private const float LandmarkClearance = 8f;
    // MapEastScatter의 RoadHalfWidth(길 폭 4m의 반)와 같은 값 (그쪽이 private이라 다시 둔다. 바뀌면 함께 바꾼다). 메뉴 25처럼 + LandmarkClearance 안을 막는다
    private const float CartRoadHalfWidth = 2f;
    // MapSouthEastScatter의 PlaceMinHeight와 같은 값 (그쪽이 private이라 다시 둔다. 바뀌면 함께 바꾼다)
    private const float SouthEastPlaceMinHeight = 29.3f;
    // 호수 물이 보이는 수면 정점에서 이 거리 안 (임시값). 물가 레이어 칠(물 끝에서 최대 5.5m)과 나무 여유를 덮는다
    private const float LakeShoreMargin = 10f;
    // 바위·데모 발밑 바닥 식생 비움 여유. MapEastPainter, MapSouthEastPainter의 FootprintMargin과 같은 값 (그쪽이 private이라 다시 둔다)
    private const float DetailFootprintMargin = 0.3f;
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

    // ---------- 칸 나누기, 디테일 ----------
    // 막는 곳, 나무 위치를 나눠 담는 칸 (m). TreeMinSpacing보다 커야 이웃 3 × 3칸만 보면 된다
    private const float HashCellSize = 10f;
    // 막는 곳을 담을 때 곧은 선에서 이 거리 밖의 것은 담지 않는다 (판정 위치는 모두 ReachDistance + 칸 하나 안이다)
    private const float BandSearchDistance = ReachDistance + HashCellSize;
    // 디테일 띠를 읽을 때 ReachDistance 밖으로 더 읽는 칸 수 (거울 칸 번호 반올림 여유)
    private const int StripMarginCells = 2;

    // ---------- 로그 ----------
    // 메인 Claude가 잰 측정과 같은 띠 (곧은 선 기준 -100 ~ +100m, 25m)
    private const float LogBinWidth = 25f;
    private const int LogBinCount = 8;
    private const float WobbleLogStep = 1f;
    // 이음매 칸이 양옆 평균과 레이어 비중이 이만큼 넘게 다르면 눈에 띄는 이음매로 센다 (메인 Claude 측정 기준과 같은 값)
    private const float SeamCheckDifference = 0.3f;

    [MenuItem("Tools/Map/27. 구역 경계 섞기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        Scene scene = terrain.gameObject.scene;
        if (scene != SceneManager.GetActiveScene())
        {
            Debug.LogError("[MapBorderBlend] Game 씬을 활성 씬으로 두고 실행하세요.");
            return;
        }

        BlendContext context = CreateContext(terrain);
        if (context == null) return;

        List<TreeRecord> trees = CollectTrees(context);
        StoreBins(context, trees, true);
        MeasureDensities(context, trees);
        BuildSourcePools(context, trees);
        List<TreeRecord> survivors = ThinTrees(context, trees);
        foreach (TreeRecord tree in survivors)
            AddPoint(context.TreePoints, tree.Position);
        PlantTrees(context);
        StoreBins(context, survivors.Concat(context.PlantedTrees).ToList(), false);

        List<string> seamLines = BlendAlphamaps(context);
        BlendDetails(context);

        EditorUtility.SetDirty(terrain.terrainData);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        LogWobble(context);
        LogBoundaries(context);
        LogSeams(seamLines);
        LogPlanted(context);
    }

    // 지금 알파맵 값으로 이음매만 메운다. 나무, 바닥 식생, 씬은 바꾸지 않는다
    [MenuItem("Tools/Map/27-1. 경계 선 이음매 메우기")]
    private static void RunSeamFill()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        BlendContext context = CreateContext(terrain);
        if (context == null) return;

        TerrainData data = terrain.terrainData;
        int resolution = data.alphamapResolution;
        float[,,] alphas = data.GetAlphamaps(0, 0, resolution, resolution);
        List<string> seamLines = FillSeams(context, alphas);
        data.SetAlphamaps(0, 0, alphas);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        LogSeams(seamLines);
    }

    // ---------- 준비 ----------

    // 판정에 필요한 부모, 강, 길, 호수, 막는 곳을 모은다. 하나라도 없으면 아무것도 바꾸지 않고 null
    private static BlendContext CreateContext(Terrain terrain)
    {
        GameObject[] roots = terrain.gameObject.scene.GetRootGameObjects();
        Boundary[] boundaries = CreateBoundaries();
        var context = new BlendContext
        {
            Terrain = terrain,
            Boundaries = boundaries,
            Stats = boundaries.Select(boundary => new BoundaryStats()).ToArray(),
            TreeParents = new Transform[CellNames.Length],
            Trails = MapTrailBuilder.GetTrails()
        };

        var regionRoots = new Transform[CellNames.Length];
        var missing = new List<string>();
        for (int cell = 0; cell < CellNames.Length; cell++)
        {
            regionRoots[cell] = FindRoot(roots, RegionNames[cell]);
            Transform trees = regionRoots[cell] != null ? regionRoots[cell].Find(TreeParentName) : null;
            if (trees == null) missing.Add($"{RegionNames[cell]}/{TreeParentName}");
            context.TreeParents[cell] = trees;
        }
        Transform lakeRegion = FindRoot(roots, LakeRegionName);
        Transform lakeParent = lakeRegion != null ? lakeRegion.Find(LakeParentName) : null;
        MeshFilter lake = lakeParent != null ? lakeParent.GetComponentInChildren<MeshFilter>() : null;
        if (lake == null) missing.Add($"{LakeRegionName}/{LakeParentName} 수면 (메뉴 6)");
        if (context.Trails == null) missing.Add("등산로 저장 파일 (메뉴 7-1)");
        if (missing.Count > 0)
        {
            Debug.LogError($"[MapBorderBlend] {string.Join(", ", missing)}이(가) 없어 나무와 건드리지 않는 곳을 판정할 수 없습니다. 해당 메뉴를 먼저 실행하세요. 아무것도 바꾸지 않았습니다.");
            return null;
        }

        context.RiverPath = MapRiverBuilder.GetPath(terrain);
        context.CartRoad = MapEastScatter.GetCartRoadPath(terrain);
        if (context.CartRoad == null) Debug.LogWarning("[MapBorderBlend] 버려진 수레길 선을 찾지 못해 수레길 막기 없이 섞습니다. 메뉴 25 로그를 확인하세요.");

        AddPathSegments(context.WaterSegments, context.RiverPath, MapRiverBuilder.HalfWidth + RiverMargin);
        context.LakePointCount = AddLakePoints(context, lake);
        foreach (List<Vector2> trail in context.Trails)
            AddPathSegments(context.PathSegments, trail, MapTrailBuilder.HalfWidth + MapTreeScatter.TrailClearMargin);
        if (context.CartRoad != null) AddPathSegments(context.PathSegments, context.CartRoad, CartRoadHalfWidth + LandmarkClearance);

        foreach (Transform root in regionRoots)
        {
            context.LandmarkRectCount += AddRendererRects(context.LandmarkAreas, root.Find(LandmarkParentName), LandmarkClearance, 0f);
            context.RockRectCount += AddRendererRects(context.RockAreas, root.Find(RockParentName), 0f, TreeFootprintRadius);
            context.RockRectCount += AddRendererRects(context.RockAreas, root.Find(DemoParentName), 0f, TreeFootprintRadius);
        }

        AddPrefabs(context.SourcePrefabs, MapTreeScatter.MainTreePaths);
        AddPrefabs(context.SourcePrefabs, MapTreeScatter.DeadTreePaths);
        AddPrefabs(context.SourcePrefabs, MapTreeScatter.GiantTreePaths);
        AddPrefabs(context.DeadPrefabs, MapTreeScatter.DeadTreePaths);
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

    private static void AddPrefabs(HashSet<GameObject> set, string[] paths)
    {
        foreach (string path in paths)
            set.Add(AssetDatabase.LoadAssetAtPath<GameObject>(MapTreeScatter.PrefabFolder + path + ".prefab"));
    }

    // 같은 직선 위 경계는 같은 흔들림 노이즈 위치를 쓴다. 노이즈 위치는 직선이 처음 나온 순서대로 seed에서 뽑는다
    private static Boundary[] CreateBoundaries()
    {
        var random = new System.Random(WobbleSeed);
        var lineOffsets = new Dictionary<Vector2, Vector2>();
        var boundaries = new Boundary[BoundaryCellPairs.Length];
        for (int i = 0; i < BoundaryCellPairs.Length; i++)
        {
            Boundary boundary = CreateBoundary(BoundaryCellPairs[i][0], BoundaryCellPairs[i][1]);
            var lineKey = new Vector2(boundary.IsVertical ? 1f : 0f, boundary.Position);
            if (!lineOffsets.TryGetValue(lineKey, out Vector2 offset))
            {
                offset = new Vector2(RandomRange(random, NoiseOffsetMin, NoiseOffsetMax), RandomRange(random, NoiseOffsetMin, NoiseOffsetMax));
                lineOffsets[lineKey] = offset;
            }
            boundary.WobbleOffset = offset;
            boundaries[i] = boundary;
        }
        return boundaries;
    }

    // 두 칸이 맞닿은 변. 3 × 3 격자라 맞닿은 두 칸의 변 길이가 같다
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

    // 곧은 선 근처(BandSearchDistance)에 걸치는 선분만 담는다
    private static void AddPathSegments(Dictionary<Vector2Int, List<Segment>> index, List<Vector2> path, float radius)
    {
        for (int i = 1; i < path.Count; i++)
        {
            Rect cover = Rect.MinMaxRect(
                Mathf.Min(path[i - 1].x, path[i].x) - radius, Mathf.Min(path[i - 1].y, path[i].y) - radius,
                Mathf.Max(path[i - 1].x, path[i].x) + radius, Mathf.Max(path[i - 1].y, path[i].y) + radius);
            if (!IsNearBand(cover)) continue;
            AddToCells(index, cover, new Segment { From = path[i - 1], To = path[i], Radius = radius });
        }
    }

    // 호수 수면 메시는 물가 밖 지형 속까지 깔려 있어, 지형이 수면보다 낮은(물이 보이는) 정점만 쓴다 (MapGroundPainter.CreateWaterSource와 같은 방식)
    private static int AddLakePoints(BlendContext context, MeshFilter lake)
    {
        int count = 0;
        foreach (Vector3 vertex in lake.sharedMesh.vertices)
        {
            Vector3 world = lake.transform.TransformPoint(vertex);
            Vector2 point = ToXZ(world);
            var cover = new Rect(point - Vector2.one * LakeShoreMargin, Vector2.one * LakeShoreMargin * 2f);
            if (!IsNearBand(cover) || GetGroundHeight(context.Terrain, point) >= world.y) continue;

            AddToCells(context.WaterSegments, cover, new Segment { From = point, To = point, Radius = LakeShoreMargin });
            count++;
        }
        return count;
    }

    // 렌더러마다 범위 XZ를 grow만큼 넓힌 사각형을 담는다. 질의할 때 coverMargin까지 더 넓혀 볼 수 있게 그만큼 넓은 칸에 담는다.
    // 묶음이 없는 구역(예 : Center/Landmarks)은 0개
    private static int AddRendererRects(Dictionary<Vector2Int, List<Rect>> index, Transform parent, float grow, float coverMargin)
    {
        if (parent == null) return 0;

        int count = 0;
        foreach (Renderer renderer in parent.GetComponentsInChildren<Renderer>(true))
        {
            Rect rect = ToXZRect(renderer.bounds, grow);
            Rect cover = Rect.MinMaxRect(rect.xMin - coverMargin, rect.yMin - coverMargin, rect.xMax + coverMargin, rect.yMax + coverMargin);
            if (!IsNearBand(cover)) continue;
            AddToCells(index, cover, rect);
            count++;
        }
        return count;
    }

    private static bool IsNearBand(Rect area)
    {
        return VerticalLines.Any(x => area.xMin - BandSearchDistance <= x && x <= area.xMax + BandSearchDistance)
               || HorizontalLines.Any(z => area.yMin - BandSearchDistance <= z && z <= area.yMax + BandSearchDistance);
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

    // 곧은 선 위(across = 0)는 GetCell처럼 높은 쪽 칸이다
    private static int GetSide(Boundary boundary, Vector2 position)
    {
        return GetAcross(boundary, position) >= 0f ? HighSide : LowSide;
    }

    // 흔들린 경계가 곧은 선에서 높은 쪽으로 벗어난 거리 (±WobbleAmplitude)
    private static float GetWobble(Boundary boundary, float along)
    {
        float noise = Mathf.PerlinNoise(along / WobblePeriod + boundary.WobbleOffset.x, boundary.WobbleOffset.y);
        return Mathf.Clamp((noise * 2f - 1f) * WobbleAmplitude, -WobbleAmplitude, WobbleAmplitude);
    }

    // 섞기 비율 t (건너편 몫). 흔들린 경계에서 자기 쪽으로 잰 거리 -BandHalfWidth ~ +BandHalfWidth를 smoothstep으로 1 ~ 0에 잇는다
    private static float GetOtherShare(Boundary boundary, int side, Vector2 position)
    {
        float offset = GetAcross(boundary, position) - GetWobble(boundary, GetAlong(boundary, position));
        float ownDistance = side == HighSide ? offset : -offset;
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-BandHalfWidth, BandHalfWidth, ownDistance));
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

    // ---------- 건드리지 않는 곳 ----------

    // 모든 작업이 피하는 곳. isTree면 물을 나무 줄기 자리(피벗과 둘레 4곳)로 본다. 없으면 null
    private static SkipReason? FindProtection(BlendContext context, Vector2 position, bool isTree)
    {
        if (IsInClearCircle(position)) return SkipReason.Habitat;
        if (IsWater(context, position, isTree)) return SkipReason.Water;
        if (IsInSegments(context.PathSegments, position)) return SkipReason.Path;
        if (IsInRects(context.LandmarkAreas, position, 0f)) return SkipReason.Landmark;
        return null;
    }

    // 심기만 더 막는 곳까지 본다
    private static SkipReason? FindPlantProtection(BlendContext context, Vector2 position)
    {
        SkipReason? reason = FindProtection(context, position, true);
        if (reason != null) return reason;
        if (GetSlope(context.Terrain, position) >= MapTreeScatter.MaxSlope) return SkipReason.Slope;
        if (IsInRects(context.RockAreas, position, TreeFootprintRadius)) return SkipReason.Rock;
        if (IsNearPoint(context.TreePoints, position, TreeMinSpacing)) return SkipReason.Tree;
        return null;
    }

    // 바닥 식생을 섞지 않는 칸 (구역 칠하기 메뉴가 비워 둔 가파른 칸, 바위·데모 발밑 포함)
    private static bool IsDetailBlocked(BlendContext context, Vector2 position)
    {
        return FindProtection(context, position, false) != null
               || GetSlope(context.Terrain, position) >= MapTreeScatter.MaxSlope
               || IsInRects(context.RockAreas, position, DetailFootprintMargin);
    }

    private static bool IsInClearCircle(Vector2 position)
    {
        for (int i = 0; i < ClearCenters.Length; i++)
        {
            if (Vector2.Distance(position, ClearCenters[i]) < ClearRadii[i]) return true;
        }
        return false;
    }

    // 강, 호수는 담아 둔 선분·점으로, C3 물은 지면 높이로 본다 (남동쪽 배치의 나무 자리 판정과 같은 방식)
    private static bool IsWater(BlendContext context, Vector2 position, bool isTree)
    {
        if (IsInSegments(context.WaterSegments, position)) return true;

        IEnumerable<Vector2> points = isTree ? GetFootprintPoints(position) : new[] { position };
        return points.Any(point => GetCell(point) == SouthEastCell && GetGroundHeight(context.Terrain, point) < SouthEastPlaceMinHeight);
    }

    private static IEnumerable<Vector2> GetFootprintPoints(Vector2 position)
    {
        yield return position;
        yield return position + Vector2.up * TreeFootprintRadius;
        yield return position + Vector2.down * TreeFootprintRadius;
        yield return position + Vector2.left * TreeFootprintRadius;
        yield return position + Vector2.right * TreeFootprintRadius;
    }

    // ---------- 나무 ----------

    // 모든 구역 <구역>/Trees 아래의 prefab 인스턴스 (메뉴 25 쓰러진 나무처럼 Landmarks 아래 나무는 들지 않는다)
    private static List<TreeRecord> CollectTrees(BlendContext context)
    {
        var records = new List<TreeRecord>();
        foreach (Transform parent in context.TreeParents)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(child.gameObject)) continue;

                GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                bool isSource = prefab != null && context.SourcePrefabs.Contains(prefab);
                if (!isSource) context.NonSourceTreeCount++;

                Vector2 position = ToXZ(child.position);
                int boundaryIndex = FindBoundary(context.Boundaries, position);
                records.Add(new TreeRecord
                {
                    Transform = child,
                    Position = position,
                    Scale = child.localScale,
                    SourcePrefab = isSource ? prefab : null,
                    BoundaryIndex = boundaryIndex,
                    Side = boundaryIndex >= 0 ? GetSide(context.Boundaries[boundaryIndex], position) : LowSide
                });
            }
        }
        return records;
    }

    private static int GetChunkCount(Boundary boundary)
    {
        return Mathf.RoundToInt((boundary.AlongMax - boundary.AlongMin) / DensityChunkLength);
    }

    private static int GetChunk(Boundary boundary, float along)
    {
        return Mathf.Clamp((int)((along - boundary.AlongMin) / DensityChunkLength), 0, GetChunkCount(boundary) - 1);
    }

    private static void MeasureDensities(BlendContext context, List<TreeRecord> trees)
    {
        foreach (Boundary boundary in context.Boundaries)
        {
            int chunkCount = GetChunkCount(boundary);
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
        return GetChunk(boundary, GetAlong(boundary, position));
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

    // 목표 밀도 = 자기 쪽 기준 × (1 - t) + 건너편 기준 × t
    private static float GetTargetDensity(Boundary boundary, int side, Vector2 position)
    {
        float along = GetAlong(boundary, position);
        return Mathf.Lerp(GetDensity(boundary, side, along), GetDensity(boundary, 1 - side, along), GetOtherShare(boundary, side, position));
    }

    // 경계·쪽·구간마다 원본으로 쓸 수 있는 나무 (솎기 전, 곧은 선에서 ReachDistance + ReferenceDepth 안)
    private static void BuildSourcePools(BlendContext context, List<TreeRecord> trees)
    {
        foreach (Boundary boundary in context.Boundaries)
        {
            int chunkCount = GetChunkCount(boundary);
            boundary.SourcePools = new List<TreeRecord>[2, chunkCount];
            for (int side = LowSide; side <= HighSide; side++)
            {
                for (int chunk = 0; chunk < chunkCount; chunk++)
                    boundary.SourcePools[side, chunk] = new List<TreeRecord>();
            }

            foreach (TreeRecord tree in trees)
            {
                if (tree.SourcePrefab == null) continue;

                int cell = GetCell(tree.Position);
                int side = cell == boundary.Cells[HighSide] ? HighSide : cell == boundary.Cells[LowSide] ? LowSide : -1;
                if (side < 0) continue;

                float depth = Mathf.Abs(GetAcross(boundary, tree.Position));
                if (depth >= ReachDistance + ReferenceDepth) continue;
                boundary.SourcePools[side, GetChunk(boundary, GetAlong(boundary, tree.Position))].Add(tree);
            }
        }
    }

    // 빽빽한 쪽 나무만 (목표 / 자기 쪽 기준) 확률로 남긴다. 무작위 값은 조건과 상관없이 나무마다 하나씩 뽑아 결과가 항상 같게 한다
    private static List<TreeRecord> ThinTrees(BlendContext context, List<TreeRecord> trees)
    {
        var random = new System.Random(ThinSeed);
        var survivors = new List<TreeRecord>();
        foreach (TreeRecord tree in trees)
        {
            double roll = random.NextDouble();
            if (tree.BoundaryIndex < 0 || roll < GetKeepRatio(context.Boundaries[tree.BoundaryIndex], tree))
            {
                survivors.Add(tree);
                continue;
            }

            BoundaryStats stats = context.Stats[tree.BoundaryIndex];
            if (FindProtection(context, tree.Position, false) != null)
            {
                stats.ProtectedKeepCount++;
                survivors.Add(tree);
                continue;
            }
            stats.ThinCount++;
            Object.DestroyImmediate(tree.Transform.gameObject);
        }
        return survivors;
    }

    // 드문 쪽은 1 (지우지 않는다)
    private static float GetKeepRatio(Boundary boundary, TreeRecord tree)
    {
        float own = GetDensity(boundary, tree.Side, GetAlong(boundary, tree.Position));
        float other = GetDensity(boundary, 1 - tree.Side, GetAlong(boundary, tree.Position));
        if (own <= other) return 1f;
        return GetTargetDensity(boundary, tree.Side, tree.Position) / own;
    }

    private static void PlantTrees(BlendContext context)
    {
        for (int i = 0; i < context.Boundaries.Length; i++)
        {
            Boundary boundary = context.Boundaries[i];
            int alongCount = Mathf.RoundToInt((boundary.AlongMax - boundary.AlongMin) / PlantCellSize);
            int acrossCount = Mathf.CeilToInt(ReachDistance / PlantCellSize);
            for (int along = 0; along < alongCount; along++)
            {
                for (int across = -acrossCount; across < acrossCount; across++)
                    PlantCell(context, i, boundary.AlongMin + along * PlantCellSize, across * PlantCellSize);
            }
        }
    }

    // 칸이 곧은 선에서 시작하므로 칸 하나는 한쪽에만 든다. 칸 중심이 이 경계 띠가 아니면(교차점에서 다른 경계가 더 가까움) 건너뛴다
    private static void PlantCell(BlendContext context, int boundaryIndex, float alongMin, float acrossMin)
    {
        Boundary boundary = context.Boundaries[boundaryIndex];
        float along = alongMin + PlantCellSize / 2f;
        Vector2 center = ToPosition(boundary, along, acrossMin + PlantCellSize / 2f);
        if (FindBoundary(context.Boundaries, center) != boundaryIndex) return;

        int side = GetSide(boundary, center);
        if (GetDensity(boundary, side, along) >= GetDensity(boundary, 1 - side, along)) return;

        Rect area = boundary.IsVertical
            ? new Rect(boundary.Position + acrossMin, alongMin, PlantCellSize, PlantCellSize)
            : new Rect(alongMin, boundary.Position + acrossMin, PlantCellSize, PlantCellSize);
        float expected = GetTargetDensity(boundary, side, center) * PlantCellSize * PlantCellSize / HectareArea;
        float deficit = expected - CountPoints(context.TreePoints, area);
        if (deficit <= 0f) return;

        int count = Mathf.FloorToInt(deficit);
        if (context.PlantRandom.NextDouble() < deficit - count) count++;
        var spot = new PlantSpot { BoundaryIndex = boundaryIndex, DenseSide = 1 - side, Area = area, Along = along };
        for (int i = 0; i < count; i++)
            TryPlant(context, spot);
    }

    // 빽빽한 쪽 가까운 나무 하나를 원본으로 골라, 칸 안 자리를 PlantAttempts번까지 뽑아 본다
    private static void TryPlant(BlendContext context, PlantSpot spot)
    {
        BoundaryStats stats = context.Stats[spot.BoundaryIndex];
        List<TreeRecord> pool = GetSourcePool(context.Boundaries[spot.BoundaryIndex], spot.DenseSide, spot.Along);
        if (pool.Count == 0)
        {
            stats.PlantFailCounts[(int)SkipReason.NoSource]++;
            return;
        }

        System.Random random = context.PlantRandom;
        TreeRecord source = pool[random.Next(pool.Count)];
        SkipReason? reason = null;
        for (int attempt = 0; attempt < PlantAttempts; attempt++)
        {
            var position = new Vector2(
                spot.Area.xMin + (float)random.NextDouble() * spot.Area.width,
                spot.Area.yMin + (float)random.NextDouble() * spot.Area.height);
            reason = FindPlantProtection(context, position);
            if (reason != null) continue;

            PlaceTree(context, spot, source, position);
            return;
        }
        stats.PlantFailCounts[(int)reason.Value]++;
    }

    private static List<TreeRecord> GetSourcePool(Boundary boundary, int side, float along)
    {
        int center = GetChunk(boundary, along);
        int last = boundary.SourcePools.GetLength(1) - 1;
        var pool = new List<TreeRecord>();
        for (int chunk = Mathf.Max(center - SourceChunkReach, 0); chunk <= Mathf.Min(center + SourceChunkReach, last); chunk++)
            pool.AddRange(boundary.SourcePools[side, chunk]);
        return pool;
    }

    // 구역 배치 메뉴와 같은 방식 : prefab 연결 유지, 피벗을 지면 높이에, y축 무작위 회전, 없는 충돌체만 붙임. Static 플래그는 켜지 않는다 (GPU Resident Drawer로 그림)
    private static void PlaceTree(BlendContext context, PlantSpot spot, TreeRecord source, Vector2 position)
    {
        Transform parent = context.TreeParents[GetCell(position)];
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source.SourcePrefab, parent);
        instance.name = BlendPrefix + source.SourcePrefab.name;
        float yaw = (float)context.PlantRandom.NextDouble() * 360f;
        instance.transform.SetPositionAndRotation(ToWorld(position, GetGroundHeight(context.Terrain, position)), Quaternion.Euler(0f, yaw, 0f));
        instance.transform.localScale = source.Scale;

        BoundaryStats stats = context.Stats[spot.BoundaryIndex];
        if (MapTreeScatter.AddMissingCollider(instance, source.SourcePrefab.name)) stats.ColliderCount++;
        stats.PlantCount++;
        AddPoint(context.TreePoints, position);
        context.PlantedTrees.Add(new TreeRecord
        {
            Transform = instance.transform,
            Position = position,
            Scale = source.Scale,
            SourcePrefab = source.SourcePrefab,
            BoundaryIndex = spot.BoundaryIndex,
            Side = 1 - spot.DenseSide
        });
    }

    // ---------- 지면 레이어 ----------

    // 맵 전체를 읽어 띠 안 칸만 원본(읽은 값)끼리 섞고, 섞은 값으로 이음매를 메운 뒤 그대로 다시 쓴다.
    // 거울 칸도 원본에서 읽으므로 처리 순서와 상관없다. 이음매 로그 줄을 돌려준다
    private static List<string> BlendAlphamaps(BlendContext context)
    {
        TerrainData data = context.Terrain.terrainData;
        int resolution = data.alphamapResolution;
        int layers = data.alphamapLayers;
        float cellSize = data.size.x / resolution;
        Vector2 origin = ToXZ(context.Terrain.transform.position);
        float[,,] original = data.GetAlphamaps(0, 0, resolution, resolution);
        var result = (float[,,])original.Clone();

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                Vector2 center = GetCellCenter(new Vector2Int(x, z), origin, cellSize);
                int owner = FindBoundary(context.Boundaries, center);
                if (owner < 0) continue;

                Boundary boundary = context.Boundaries[owner];
                float share = GetOtherShare(boundary, GetSide(boundary, center), center);
                if (share <= 0f) continue;

                Vector2Int mirror = ToIndex(Mirror(boundary, center), origin, cellSize, resolution);
                Vector2 mirrorCenter = GetCellCenter(mirror, origin, cellSize);
                BoundaryStats stats = context.Stats[owner];
                if (FindProtection(context, center, false) != null || FindProtection(context, mirrorCenter, false) != null)
                {
                    stats.AlphaSkipCount++;
                    continue;
                }

                float sum = 0f;
                for (int layer = 0; layer < layers; layer++)
                {
                    result[z, x, layer] = Mathf.Lerp(original[z, x, layer], original[mirror.y, mirror.x, layer], share);
                    sum += result[z, x, layer];
                }
                for (int layer = 0; layer < layers && sum > 0f; layer++)
                    result[z, x, layer] /= sum;
                stats.AlphaBlendCount++;
            }
        }
        List<string> seamLines = FillSeams(context, result);
        data.SetAlphamaps(0, 0, result);
        return seamLines;
    }

    // ---------- 경계 선 이음매 ----------

    // 동서 방향 선(z)을 먼저 메운 뒤 남북 방향 선(x)을 메운다. 교차점 칸은 남북 방향 선 차례에 가로 양옆(이미 메운 동서 방향 이음매 칸) 평균으로 메운다.
    // alphas를 바로 고친다. 선마다 로그 줄을 돌려준다
    private static List<string> FillSeams(BlendContext context, float[,,] alphas)
    {
        var lines = new List<string>();
        foreach (float z in HorizontalLines)
            lines.Add(FillSeamLine(context, alphas, false, z));
        foreach (float x in VerticalLines)
            lines.Add(FillSeamLine(context, alphas, true, x));
        return lines;
    }

    // 곧은 선 좌표를 담은 칸 번호. 그 칸의 거울 칸이 자기 자신일 때만 이음매다 (아니면 -1)
    private static int FindSeamIndex(float line, float origin, float cellSize, int resolution)
    {
        int index = Mathf.FloorToInt((line - origin) / cellSize);
        if (index < 1 || index > resolution - 2) return -1;

        float center = origin + (index + 0.5f) * cellSize;
        int mirror = Mathf.FloorToInt((2f * line - center - origin) / cellSize);
        return mirror == index ? index : -1;
    }

    // 선 하나의 이음매 칸을 선에 수직인 양옆(번호 -1, +1) 현재 값 평균으로 바꾸고 합 1로 맞춘다. 양옆 칸은 바꾸지 않는다.
    // 동서 방향 선은 교차점 칸(남북 방향 선의 이음매 열)을 건너뛴다 (남북 방향 선 차례에 메운다)
    private static string FillSeamLine(BlendContext context, float[,,] alphas, bool isVertical, float line)
    {
        TerrainData data = context.Terrain.terrainData;
        int resolution = data.alphamapResolution;
        int layers = data.alphamapLayers;
        float cellSize = data.size.x / resolution;
        Vector2 origin = ToXZ(context.Terrain.transform.position);
        int[] verticalSeams = VerticalLines.Select(x => FindSeamIndex(x, origin.x, cellSize, resolution)).ToArray();
        string label = isVertical ? $"x = {line}" : $"z = {line}";
        int seam = FindSeamIndex(line, isVertical ? origin.x : origin.y, cellSize, resolution);
        if (seam < 0) return $"{label} : 거울 칸이 자기 자신인 칸 줄이 없어 건너뜀";

        int filled = 0, skipped = 0, visibleBefore = 0;
        var average = new float[layers];
        for (int along = 0; along < resolution; along++)
        {
            if (!isVertical && verticalSeams.Contains(along)) continue;

            Vector2Int cell = isVertical ? new Vector2Int(seam, along) : new Vector2Int(along, seam);
            Vector2Int step = isVertical ? Vector2Int.right : Vector2Int.up;
            if (FindProtection(context, GetCellCenter(cell, origin, cellSize), false) != null)
            {
                skipped++;
                continue;
            }

            Vector2Int before = cell - step;
            Vector2Int after = cell + step;
            float sum = 0f, difference = 0f;
            for (int layer = 0; layer < layers; layer++)
            {
                average[layer] = (alphas[before.y, before.x, layer] + alphas[after.y, after.x, layer]) / 2f;
                difference = Mathf.Max(difference, Mathf.Abs(alphas[cell.y, cell.x, layer] - average[layer]));
                sum += average[layer];
            }
            if (difference > SeamCheckDifference) visibleBefore++;
            for (int layer = 0; layer < layers && sum > 0f; layer++)
                alphas[cell.y, cell.x, layer] = average[layer] / sum;
            filled++;
        }
        return $"{label} (칸 번호 {seam}, 칸 중심 {(isVertical ? origin.x : origin.y) + (seam + 0.5f) * cellSize:F2}) : 메운 칸 {filled}, 건너뛴 칸 {skipped}, " +
               $"메우기 전 양옆 평균과 {SeamCheckDifference} 넘게 다른 칸 {visibleBefore}";
    }

    // ---------- 바닥 식생 ----------

    // 곧은 선마다 띠만 읽어(맵 전체를 읽으면 프로토타입 수만큼 커진다) 바꿀 칸을 원본끼리 정한 뒤, 띠마다 바꾼 칸을 넣어 다시 쓴다.
    // 두 선의 띠가 겹치는 교차점 칸은 두 띠 모두 같은 최종 값을 쓴다
    private static void BlendDetails(BlendContext context)
    {
        TerrainData data = context.Terrain.terrainData;
        float cellSize = data.size.x / data.detailResolution;
        Vector2 origin = ToXZ(context.Terrain.transform.position);
        List<DetailStrip> strips = ReadDetailStrips(data, origin, cellSize);
        var changes = new Dictionary<Vector2Int, int[]>();

        foreach (DetailStrip strip in strips)
        {
            for (int z = strip.Range.yMin; z < strip.Range.yMax; z++)
            {
                for (int x = strip.Range.xMin; x < strip.Range.xMax; x++)
                    BlendDetailCell(context, strip, new Vector2Int(x, z), changes);
            }
        }
        foreach (DetailStrip strip in strips)
            WriteDetailStrip(data, strip, changes);
    }

    private static List<DetailStrip> ReadDetailStrips(TerrainData data, Vector2 origin, float cellSize)
    {
        int resolution = data.detailResolution;
        float halfWidth = ReachDistance + StripMarginCells * cellSize;
        var strips = new List<DetailStrip>();
        foreach (float x in VerticalLines)
        {
            int min = Mathf.Clamp(Mathf.FloorToInt((x - halfWidth - origin.x) / cellSize), 0, resolution);
            int max = Mathf.Clamp(Mathf.CeilToInt((x + halfWidth - origin.x) / cellSize), 0, resolution);
            strips.Add(new DetailStrip { IsVertical = true, Position = x, Range = new RectInt(min, 0, max - min, resolution) });
        }
        foreach (float z in HorizontalLines)
        {
            int min = Mathf.Clamp(Mathf.FloorToInt((z - halfWidth - origin.y) / cellSize), 0, resolution);
            int max = Mathf.Clamp(Mathf.CeilToInt((z + halfWidth - origin.y) / cellSize), 0, resolution);
            strips.Add(new DetailStrip { IsVertical = false, Position = z, Range = new RectInt(0, min, resolution, max - min) });
        }

        int prototypeCount = data.detailPrototypes.Length;
        foreach (DetailStrip strip in strips)
        {
            RectInt range = strip.Range;
            strip.Layers = Enumerable.Range(0, prototypeCount)
                .Select(layer => data.GetDetailLayer(range.x, range.y, range.width, range.height, layer))
                .ToArray();
        }
        return strips;
    }

    // 이 띠의 선을 쓰는 칸만 처리한다 (교차점에서 다른 선이 더 가까운 칸은 그 선의 띠가 처리한다)
    private static void BlendDetailCell(BlendContext context, DetailStrip strip, Vector2Int cell, Dictionary<Vector2Int, int[]> changes)
    {
        TerrainData data = context.Terrain.terrainData;
        float cellSize = data.size.x / data.detailResolution;
        Vector2 origin = ToXZ(context.Terrain.transform.position);
        Vector2 center = GetCellCenter(cell, origin, cellSize);
        int owner = FindBoundary(context.Boundaries, center);
        if (owner < 0) return;

        Boundary boundary = context.Boundaries[owner];
        if (boundary.IsVertical != strip.IsVertical || !Mathf.Approximately(boundary.Position, strip.Position)) return;

        float share = GetOtherShare(boundary, GetSide(boundary, center), center);
        if (share <= 0f) return;

        BoundaryStats stats = context.Stats[owner];
        Vector2Int mirror = ClampToRange(ToIndex(Mirror(boundary, center), origin, cellSize, data.detailResolution), strip.Range);
        Vector2 mirrorCenter = GetCellCenter(mirror, origin, cellSize);
        if (IsDetailBlocked(context, center) || IsDetailBlocked(context, mirrorCenter))
        {
            stats.DetailSkipCount++;
            return;
        }
        if (GetHash(cell, DetailSeed) >= share)
        {
            stats.DetailKeepCount++;
            return;
        }

        int[] values = strip.Layers.Select(layer => layer[mirror.y - strip.Range.y, mirror.x - strip.Range.x]).ToArray();
        bool isSame = strip.Layers.Select((layer, i) => layer[cell.y - strip.Range.y, cell.x - strip.Range.x] == values[i]).All(same => same);
        if (isSame)
        {
            stats.DetailSameCount++;
            return;
        }
        changes[cell] = values;
        stats.DetailSwapCount++;
    }

    private static void WriteDetailStrip(TerrainData data, DetailStrip strip, Dictionary<Vector2Int, int[]> changes)
    {
        RectInt range = strip.Range;
        int[][,] layers = strip.Layers.Select(layer => (int[,])layer.Clone()).ToArray();
        foreach (KeyValuePair<Vector2Int, int[]> change in changes)
        {
            if (!range.Contains(change.Key)) continue;
            for (int layer = 0; layer < layers.Length; layer++)
                layers[layer][change.Key.y - range.y, change.Key.x - range.x] = change.Value[layer];
        }
        for (int layer = 0; layer < layers.Length; layer++)
            data.SetDetailLayer(range.x, range.y, layer, layers[layer]);
    }

    // ---------- 칸으로 나눠 담기 ----------

    private static Vector2Int GetHashCell(Vector2 position)
    {
        return new Vector2Int(Mathf.FloorToInt(position.x / HashCellSize), Mathf.FloorToInt(position.y / HashCellSize));
    }

    private static void AddToCells<T>(Dictionary<Vector2Int, List<T>> index, Rect cover, T item)
    {
        Vector2Int min = GetHashCell(cover.min);
        Vector2Int max = GetHashCell(cover.max);
        for (int z = min.y; z <= max.y; z++)
        {
            for (int x = min.x; x <= max.x; x++)
            {
                var key = new Vector2Int(x, z);
                if (!index.TryGetValue(key, out List<T> list))
                {
                    list = new List<T>();
                    index[key] = list;
                }
                list.Add(item);
            }
        }
    }

    private static bool IsInSegments(Dictionary<Vector2Int, List<Segment>> index, Vector2 position)
    {
        return index.TryGetValue(GetHashCell(position), out List<Segment> list)
               && list.Any(segment => GetSegmentDistance(position, segment.From, segment.To) <= segment.Radius);
    }

    // margin은 담을 때 넓힌 coverMargin 이하여야 한다
    private static bool IsInRects(Dictionary<Vector2Int, List<Rect>> index, Vector2 position, float margin)
    {
        return index.TryGetValue(GetHashCell(position), out List<Rect> list)
               && list.Any(rect => position.x >= rect.xMin - margin && position.x <= rect.xMax + margin
                                   && position.y >= rect.yMin - margin && position.y <= rect.yMax + margin);
    }

    private static void AddPoint(Dictionary<Vector2Int, List<Vector2>> points, Vector2 position)
    {
        AddToCells(points, new Rect(position, Vector2.zero), position);
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

    private static int CountPoints(Dictionary<Vector2Int, List<Vector2>> points, Rect area)
    {
        Vector2Int min = GetHashCell(area.min);
        Vector2Int max = GetHashCell(area.max);
        int count = 0;
        for (int z = min.y; z <= max.y; z++)
        {
            for (int x = min.x; x <= max.x; x++)
            {
                if (points.TryGetValue(new Vector2Int(x, z), out List<Vector2> list)) count += list.Count(point => area.Contains(point));
            }
        }
        return count;
    }

    // ---------- 공통 계산 ----------

    // 칸 번호 위치 해시 (0 ~ 1). 같은 칸과 seed는 늘 같은 값이다
    private static float GetHash(Vector2Int cell, int seed)
    {
        unchecked
        {
            uint hash = (uint)seed;
            hash = MixHash(hash ^ (uint)cell.x);
            hash = MixHash(hash ^ (uint)cell.y);
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

    // 월드 XZ가 든 알파맵·디테일 칸 번호 (맵 안으로 자른다)
    private static Vector2Int ToIndex(Vector2 position, Vector2 origin, float cellSize, int resolution)
    {
        return new Vector2Int(
            Mathf.Clamp(Mathf.FloorToInt((position.x - origin.x) / cellSize), 0, resolution - 1),
            Mathf.Clamp(Mathf.FloorToInt((position.y - origin.y) / cellSize), 0, resolution - 1));
    }

    // 알파맵·디테일 칸 중심의 월드 XZ
    private static Vector2 GetCellCenter(Vector2Int cell, Vector2 origin, float cellSize)
    {
        return origin + new Vector2(cell.x + 0.5f, cell.y + 0.5f) * cellSize;
    }

    private static Vector2Int ClampToRange(Vector2Int index, RectInt range)
    {
        return new Vector2Int(Mathf.Clamp(index.x, range.xMin, range.xMax - 1), Mathf.Clamp(index.y, range.yMin, range.yMax - 1));
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

    private static float GetSegmentDistance(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 segment = to - from;
        float lengthSquared = segment.sqrMagnitude;
        float t = lengthSquared > 0f ? Mathf.Clamp01(Vector2.Dot(point - from, segment) / lengthSquared) : 0f;
        return Vector2.Distance(point, from + segment * t);
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
    // 검수(경계별 전후 밀도, 지운·심은 나무, 심기 실패 이유, 섞은·건너뛴 칸, 심은 나무가 막는 곳에 들지 않았는지, 흔들림 범위)에 쓰는 값이라 남긴다

    // 곧은 선 기준 -100 ~ +100m를 25m 띠로 나눈 1ha당 나무 수 (선분 전체 길이). 실행 전은 isBefore
    private static void StoreBins(BlendContext context, List<TreeRecord> trees, bool isBefore)
    {
        for (int i = 0; i < context.Boundaries.Length; i++)
        {
            Boundary boundary = context.Boundaries[i];
            var counts = new int[LogBinCount];
            float range = LogBinWidth * LogBinCount / 2f;
            foreach (TreeRecord tree in trees)
            {
                float along = GetAlong(boundary, tree.Position);
                float across = GetAcross(boundary, tree.Position);
                if (along < boundary.AlongMin || along >= boundary.AlongMax || across < -range || across >= range) continue;
                counts[Mathf.FloorToInt((across + range) / LogBinWidth)]++;
            }

            float binArea = LogBinWidth * (boundary.AlongMax - boundary.AlongMin);
            float[] bins = counts.Select(count => count / binArea * HectareArea).ToArray();
            if (isBefore) context.Stats[i].BeforeBins = bins;
            else context.Stats[i].AfterBins = bins;
        }
    }

    private static string FormatBins(float[] bins)
    {
        int half = bins.Length / 2;
        return string.Join(" ", bins.Take(half).Select(value => value.ToString("F0"))) + " | " + string.Join(" ", bins.Skip(half).Select(value => value.ToString("F0")));
    }

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
        Debug.Log($"[MapBorderBlend] 흔들린 경계 - 곧은 선 (동·북 +, 허용 ±{WobbleAmplitude}m, 주기 {WobblePeriod}m, 띠 한쪽 {BandHalfWidth}m, 바뀔 수 있는 범위 곧은 선 ±{ReachDistance}m)\n" +
                  string.Join("\n", lines));
    }

    private static void LogBoundaries(BlendContext context)
    {
        for (int i = 0; i < context.Boundaries.Length; i++)
        {
            Boundary boundary = context.Boundaries[i];
            BoundaryStats stats = context.Stats[i];
            string low = GetSideName(boundary, LowSide);
            string high = GetSideName(boundary, HighSide);
            string line = boundary.IsVertical ? $"x = {boundary.Position}, z {boundary.AlongMin} ~ {boundary.AlongMax}" : $"z = {boundary.Position}, x {boundary.AlongMin} ~ {boundary.AlongMax}";
            int chunkCount = boundary.Densities.GetLength(1);
            float lowAverage = Enumerable.Range(0, chunkCount).Average(chunk => boundary.Densities[LowSide, chunk]);
            float highAverage = Enumerable.Range(0, chunkCount).Average(chunk => boundary.Densities[HighSide, chunk]);
            string fails = string.Join(", ", Enumerable.Range(0, SkipLabels.Length)
                .Where(reason => stats.PlantFailCounts[reason] > 0)
                .Select(reason => $"{SkipLabels[reason]} {stats.PlantFailCounts[reason]}"));

            Debug.Log($"[MapBorderBlend] {boundary.Label} ({line}, {low} → {high})\n" +
                      $"기준 밀도 평균 (1ha당, 곧은 선에서 {ReachDistance} ~ {ReachDistance + ReferenceDepth}m, 구간 {DensityChunkLength}m) {low} {lowAverage:F1} / {high} {highAverage:F1}, 빌린 구간 {boundary.BorrowedChunkCount}\n" +
                      $"25m 띠별 나무 밀도 (1ha당, -100 ~ +100m) 전 : {FormatBins(stats.BeforeBins)}\n" +
                      $"25m 띠별 나무 밀도 (1ha당, -100 ~ +100m) 후 : {FormatBins(stats.AfterBins)}\n" +
                      $"지운 나무 {stats.ThinCount} (건드리지 않는 곳이라 남긴 {stats.ProtectedKeepCount}), 심은 나무 {stats.PlantCount} (충돌체 추가 {stats.ColliderCount}), " +
                      $"심기 실패 {stats.PlantFailCounts.Sum()} ({(fails.Length > 0 ? fails : "없음")})\n" +
                      $"지면 섞은 칸 {stats.AlphaBlendCount}, 건너뛴 칸 {stats.AlphaSkipCount} / 바닥 식생 거울 칸 값으로 바꾼 칸 {stats.DetailSwapCount} " +
                      $"(골랐지만 값이 같은 칸 {stats.DetailSameCount}), 자기 값을 둔 칸 {stats.DetailKeepCount}, 건너뛴 칸 {stats.DetailSkipCount}");
        }
    }

    // 다시 실행하면 "메우기 전 다른 칸"이 0이어야 한다 (이미 메운 줄)
    private static void LogSeams(List<string> lines)
    {
        Debug.Log("[MapBorderBlend] 경계 선 이음매 (선에 수직인 양옆 칸 평균, 동서 방향 선 먼저, 교차점은 가로 양옆)\n" + string.Join("\n", lines));
    }

    // 심은 나무를 다시 읽어 막는 곳에 들었는지 센다. 강, 등산로는 원래 판정 함수(MapRiverBuilder.IsNearRiver, MapTrailBuilder.IsNearTrail)로 다시 잰다. 모두 0이어야 한다
    private static void LogPlanted(BlendContext context)
    {
        List<TreeRecord> planted = context.PlantedTrees;
        int riverCount = planted.Count(tree => MapRiverBuilder.IsNearRiver(context.RiverPath, ToWorld(tree.Position, 0f), RiverMargin));
        int trailCount = planted.Count(tree => MapTrailBuilder.IsNearTrail(context.Trails, ToWorld(tree.Position, 0f), MapTreeScatter.TrailClearMargin));
        int protectedCount = planted.Count(tree => FindProtection(context, tree.Position, true) != null);
        int slopeCount = planted.Count(tree => GetSlope(context.Terrain, tree.Position) >= MapTreeScatter.MaxSlope);
        int floatingCount = planted.Count(tree => Mathf.Abs(tree.Transform.position.y - GetGroundHeight(context.Terrain, tree.Position)) > HeightTolerance);
        int wrongParentCount = planted.Count(tree => tree.Transform.parent != context.TreeParents[GetCell(tree.Position)]);
        int deadCount = planted.Count(tree => context.DeadPrefabs.Contains(tree.SourcePrefab));
        int thinned = context.Stats.Sum(stats => stats.ThinCount);

        Debug.Log($"[MapBorderBlend] 합계 지운 나무 {thinned}, 심은 나무 {planted.Count} (이름 앞 {BlendPrefix}, 고사목 {deadCount}), 배치 GameObject 증감 {planted.Count - thinned:+0;-0;0}. " +
                  $"원본 후보가 아닌 나무 (MapTreeScatter 나무 목록 밖 prefab) {context.NonSourceTreeCount}그루\n" +
                  $"심은 나무 검사 (모두 0이어야 한다) : 강 둘레 {riverCount}, 등산로 둘레 {trailCount}, 건드리지 않는 곳 {protectedCount}, 경사 {MapTreeScatter.MaxSlope}° 이상 {slopeCount}, " +
                  $"피벗이 지면에서 {HeightTolerance}m 넘게 뜨거나 묻힘 {floatingCount}, 놓인 칸과 다른 구역 부모 {wrongParentCount}\n" +
                  $"막는 곳 : 호수 물 정점 {context.LakePointCount}, 랜드마크 렌더러 {context.LandmarkRectCount}, 바위·데모 렌더러 {context.RockRectCount}, 수레길 {(context.CartRoad != null ? "있음" : "없음")} (곧은 선에서 {BandSearchDistance}m 안만 담음)");
    }
}
