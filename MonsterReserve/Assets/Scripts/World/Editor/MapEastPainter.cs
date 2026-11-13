using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 메뉴 26 : 동쪽 구역(C2, 데모 산맥을 옮긴 구릉진 혼합 숲)의 지면 레이어와 바닥 식생(Terrain 디테일)을 칠한다 (이슈 #15).
// 구조는 서쪽 메뉴 13(MapWestPainter)과 남동쪽 메뉴 23(MapSouthEastPainter)을 합친 것이다.
// 구분은 MapEastScatter.GetEastZone, 산 위 높이는 MapEastScatter.MountainTopHeight, 길 선은 MapEastScatter.GetCartRoadPath로 해서 메뉴 25와 맞춘다.
// 규칙 (사용자 승인 임시값)
// - 지면 : 숲은 숲 바닥, 드문드문·빈터는 풀 (서쪽과 같은 규칙).
//   지면 높이 MountainRockBlendStartHeight → MountainRockBlendEndHeight에서 흙바위(DirtRock) 비중을 0 → MountainRockMaxWeight로 smoothstep으로 올려 아래 레이어와 섞는다.
//   그 위에 경사 StoneBlendStartSlope부터 MapTreeScatter.MaxSlope까지 돌로 섞는다 (가파른 곳은 돌이 우선).
//   버려진 수레길 : 길 중심 RoadHalfWidth 안은 흙(Dirt) 100%, RoadBlendEndDistance까지 주변 레이어로 smoothstep 섞기. 길이 없으면 경고하고 길 칠하기만 건너뛴다.
// - 바닥 식생 : 칸마다 MapDetailPainter.PaintDetailCell(서쪽 규칙, 꽃은 서쪽과 같은 1배)로 놓는다. 지면 MountainTopHeight 이상(산 위)은 드문드문 규칙이다.
// - 바닥 식생을 비우는 곳 : 경사 MaxSlope 이상, East/Landmarks 묶음마다 조각 렌더러 범위 XZ + FootprintMargin 발밑,
//   서식지 표시 자리(East/Habitat 위치에서 HabitatMarkerClearHalfSize), 수레길 폭(길 중심 RoadHalfWidth 안).
// - Terrain에 디테일 프로토타입이나 지면 레이어를 새로 넣지 않는다. 쓸 것이 등록되어 있지 않으면 바꾸지 않고 멈춘다.
// C2 범위(칸 중심이 C2 안인 칸)만 매번 다시 계산해 덮어쓰므로 여러 번 실행해도 결과가 같고, C1 섞기 띠(z 250 ~ 400)와 다른 구역은 그대로 남는다.
// 실행 순서 : ... → 23 → 24 → 25 → 26 → 10.
//   9-1(전체 초기화), 24(동쪽 지형), 25(구분, 랜드마크, 서식지 위치)를 다시 실행하면 26도 다시 실행한다.
public static class MapEastPainter
{
    // 알파맵 또는 디테일 격자 하나 (C2 범위)의 칸별 지면 높이, 경사, 수레길 중심까지 거리. 배열은 [z, x]
    private class GroundGrid
    {
        public RectInt Range;
        public int Resolution;
        public float[,] Heights;
        public float[,] Slopes;
        // 길이 없거나 길에서 먼 칸은 무한대 (m)
        public float[,] RoadDistances;
    }

    // 바닥 식생을 비우는 자리 (월드 XZ 사각형)와 로그용 설명
    private class FootprintSet
    {
        public List<Rect> Areas = new List<Rect>();
        public List<string> Notes = new List<string>();
        public Rect HabitatArea;
    }

    // 바닥 식생을 칠할 때 함께 쓰는 값 묶음
    private class DetailPlan
    {
        public Terrain Terrain;
        public GroundGrid Grid;
        public int[][] GroupPrototypeIndices;
        // 발밑, 서식지 표시 자리에 걸치는 디테일 칸
        public bool[,] IsFootprint;
        public bool[,] IsHabitatMarker;
    }

    private const string RegionName = "East";
    private const string LandmarkParentName = "Landmarks";
    private const string HabitatName = "Habitat";

    // 앞 메뉴들의 seed(20261109 ~ 20261160)와 겹치지 않게 둔다
    private const int Seed = 20261161;

    // MapDetailPainter의 StoneBlendStartSlope와 같은 값. 그쪽이 private이라 여기 다시 둔다 (바뀌면 함께 바꾼다)
    private const float StoneBlendStartSlope = 25f;

    // ---------- 산 위 흙바위 (사용자 승인 임시값) ----------
    private const float MountainRockBlendStartHeight = 80f;
    private const float MountainRockBlendEndHeight = 95f;
    private const float MountainRockMaxWeight = 0.6f;

    // ---------- 버려진 수레길 (사용자 승인 임시값) ----------
    // MapEastScatter의 RoadHalfWidth(길 폭 4m의 반)와 같은 값. 그쪽이 private이라 여기 다시 둔다 (바뀌면 함께 바꾼다)
    private const float RoadHalfWidth = 2f;
    // 길 중심에서 RoadHalfWidth ~ 이 거리까지 흙을 주변 레이어로 섞는다
    private const float RoadBlendEndDistance = 4f;

    // ---------- 바닥 식생 비우기 ----------
    // 랜드마크 조각 발밑 : 렌더러 범위 XZ를 이만큼 넓힌 사각형에 걸치는 칸을 비운다 (MapSouthEastPainter와 같은 값. 그쪽이 private이라 여기 다시 둔다)
    private const float FootprintMargin = 0.3f;
    // 서식지 표시 자리 : 표시 위치에서 이 반폭의 사각형에 걸치는 칸을 비운다 (MapSouthEastPainter와 같은 값. 그쪽이 private이라 여기 다시 둔다)
    private const float HabitatMarkerClearHalfSize = 1f;

    // MapDetailPainter의 GroupPrefabNames와 같은 값, 같은 그룹 순서(풀, 고사리, 낙엽, 꽃). 그쪽이 private이라 여기 다시 둔다 (바뀌면 함께 바꾼다).
    // 등록된 번호를 찾는 데만 쓰고, 칸 규칙과 비중은 MapDetailPainter.PaintDetailCell이 정한다
    private static readonly string[][] GroupPrefabNames =
    {
        new[] { "Prefab_Terrain_Grass_Medium01", "Prefab_Terrain_Grass_Medium02", "Prefab_Terrain_Grass_Medium03", "Prefab_Terrain_Grass_Small01" },
        new[] { "Prefab_Terrain_Fern_02", "Prefab_Terrain_Fern_03" },
        new[] { "Prefab_Terrain_Groundleaf_01" },
        new[] { "Prefab_FlowerGroup_White_Detail", "Prefab_FlowerGroup_Yellow_Detail", "Prefab_FlowerSingle_Purple_Detail", "Prefab_FlowerSingle_Red_Detail" }
    };

    // ---------- 검수 로그 기준 ----------
    // 풀 레이어 비중이 이 값보다 큰 칸을 풀밭으로 본다 (메뉴 9-3 MapGrassFill과 같은 값)
    private const float GrassWeightMin = 0.5f;
    // 풀밭인데 바닥 식생이 0인 칸의 목표 비율 (이하)
    private const float EmptyGrassRatioMax = 0.1f;
    // 알파맵은 8비트로 저장되어 다시 읽으면 1이 조금 모자랄 수 있어, 이 값 이상이면 흙 100% 칸으로 센다
    private const float FullDirtWeightMin = 0.99f;
    private const float HectareArea = 10000f;
    // 로그 묶음. 0 ~ 2는 MapTreeScatter.FieldZone (빈터, 드문드문, 숲)이고, 수레길 띠 > 산 위 > 구분 순으로 고른다
    private const int MountainCategory = 3;
    private const int RoadCategory = 4;
    private static readonly string[] CategoryLabels = { "빈터", "드문드문", "숲", "산 위", "수레길 띠" };

    [MenuItem("Tools/Map/26. 동쪽 지면과 바닥 식생 칠하기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        TerrainData data = terrain.terrainData;
        if (!CanPaint(data)) return;

        FootprintSet footprints = FindFootprints(terrain);
        if (footprints == null) return;

        int[][] groupPrototypeIndices = FindPrototypeIndices(data);
        if (groupPrototypeIndices == null) return;

        List<Vector2> road = MapEastScatter.GetCartRoadPath(terrain);
        if (road == null)
            Debug.LogWarning("[MapEastPainter] 버려진 수레길 선을 찾지 못해 길 칠하기와 길 폭 비우기를 건너뜁니다. 메뉴 25 로그와 MapEastScatter 길 찾기 상수를 확인하세요.");

        Rect east = MapGrid.GetRangeBounds(MapEastShaper.EastCell, MapEastShaper.EastCell);
        GroundGrid alphaGrid = CreateGroundGrid(terrain, east, data.alphamapResolution, road);
        GroundGrid detailGrid = CreateGroundGrid(terrain, east, data.detailResolution, road);
        var plan = new DetailPlan
        {
            Terrain = terrain,
            Grid = detailGrid,
            GroupPrototypeIndices = groupPrototypeIndices,
            IsFootprint = MarkCells(terrain, detailGrid.Range, footprints.Areas),
            IsHabitatMarker = MarkCells(terrain, detailGrid.Range, new List<Rect> { footprints.HabitatArea })
        };

        PaintAlphamaps(terrain, alphaGrid);
        PaintDetails(plan);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        RectInt alphaRange = alphaGrid.Range;
        float[,,] alphas = data.GetAlphamaps(alphaRange.x, alphaRange.y, alphaRange.width, alphaRange.height);
        LogFootprints(footprints);
        LogAlphamaps(terrain, alphaGrid, alphas);
        LogRoad(alphaGrid, alphas, road);
        LogDetails(plan, alphaRange, alphas);
        LogWriteRanges(terrain, east, alphaRange, detailGrid.Range);
    }

    // ---------- 준비 ----------

    // 디테일 방식과 흙바위 레이어를 본다. 바꾸면 다른 구역의 기존 값 뜻이 달라지거나 레이어를 새로 넣어야 하므로 바꾸지 않고 멈춘다
    private static bool CanPaint(TerrainData data)
    {
        if (data.detailScatterMode != DetailScatterMode.InstanceCountMode)
        {
            Debug.LogError($"[MapEastPainter] 디테일 방식이 {data.detailScatterMode}입니다. 먼저 'Tools/Map/9-2. 북쪽 데모 지면 이식'을 실행하세요. 지면과 바닥 식생을 바꾸지 않았습니다.");
            return false;
        }
        if (data.alphamapLayers <= MapTerrainSetup.DirtRockLayer)
        {
            Debug.LogError($"[MapEastPainter] Terrain 지면 레이어가 {data.alphamapLayers}개라 흙바위 레이어({MapTerrainSetup.DirtRockLayer}번)가 없습니다. " +
                           "먼저 'Tools/Map/1. 지형과 구역 부모 생성'으로 레이어를 맞추세요. 지면과 바닥 식생을 바꾸지 않았습니다.");
            return false;
        }
        return true;
    }

    // 랜드마크(수레길, 사당, 쓰러진 나무 지대)는 East/Landmarks 아래 묶음마다 모든 prefab 조각의 발밑.
    // 서식지 표시 자리는 East/Habitat 위치에서 잰다 (메뉴 25가 놓은 자리를 그대로 쓴다)
    private static FootprintSet FindFootprints(Terrain terrain)
    {
        GameObject eastRoot = terrain.gameObject.scene.GetRootGameObjects().FirstOrDefault(root => root.name == RegionName);
        Transform landmarks = eastRoot != null ? eastRoot.transform.Find(LandmarkParentName) : null;
        Transform habitat = eastRoot != null ? eastRoot.transform.Find(HabitatName) : null;
        if (landmarks == null || habitat == null)
        {
            Debug.LogError("[MapEastPainter] East/Landmarks, Habitat 중 없는 것이 있습니다. 먼저 'Tools/Map/25. 동쪽 나무·바위·랜드마크·서식지 배치'를 실행하세요. 지면과 바닥 식생을 바꾸지 않았습니다.");
            return null;
        }

        Vector2 habitatPosition = ToXZ(habitat.position);
        var set = new FootprintSet
        {
            HabitatArea = new Rect(habitatPosition - Vector2.one * HabitatMarkerClearHalfSize, Vector2.one * HabitatMarkerClearHalfSize * 2f)
        };
        foreach (Transform group in landmarks)
        {
            List<Rect> areas = MeasureFootprints(group.GetComponentsInChildren<Transform>(true)
                .Select(child => child.gameObject)
                .Where(PrefabUtility.IsOutermostPrefabInstanceRoot));
            set.Areas.AddRange(areas);
            set.Notes.Add(DescribeFootprints(group.name, areas));
        }
        return set;
    }

    // 조각마다 렌더러 범위 XZ를 FootprintMargin만큼 넓힌 사각형
    private static List<Rect> MeasureFootprints(IEnumerable<GameObject> pieces)
    {
        var areas = new List<Rect>();
        foreach (GameObject piece in pieces)
        {
            Renderer[] renderers = piece.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            areas.Add(Rect.MinMaxRect(
                bounds.min.x - FootprintMargin, bounds.min.z - FootprintMargin,
                bounds.max.x + FootprintMargin, bounds.max.z + FootprintMargin));
        }
        return areas;
    }

    // 이미 등록된 프로토타입 번호만 찾는다. MapDetailPainter.RegisterPrototypes는 없으면 새로 넣으므로 쓰지 않는다.
    // 하나라도 없으면 null (Terrain을 바꾸지 않고 멈춘다)
    private static int[][] FindPrototypeIndices(TerrainData data)
    {
        DetailPrototype[] prototypes = data.detailPrototypes;
        var missing = new List<string>();
        var groupPrototypeIndices = new int[GroupPrefabNames.Length][];
        for (int group = 0; group < GroupPrefabNames.Length; group++)
        {
            groupPrototypeIndices[group] = GroupPrefabNames[group]
                .Select(name => System.Array.FindIndex(prototypes, prototype => prototype.prototype != null && prototype.prototype.name == name))
                .ToArray();
            missing.AddRange(GroupPrefabNames[group].Where((name, i) => groupPrototypeIndices[group][i] < 0));
        }

        if (missing.Count == 0) return groupPrototypeIndices;

        Debug.LogError($"[MapEastPainter] Terrain에 등록되지 않은 디테일 프로토타입이 있습니다 : {string.Join(", ", missing)}. " +
                       "먼저 'Tools/Map/4. 중앙 바닥 식생 칠하기'를 실행하세요. 지면과 바닥 식생을 바꾸지 않았습니다.");
        return null;
    }

    // C2 범위 칸마다 지면 높이, 경사, 수레길 중심까지 거리를 잰다
    private static GroundGrid CreateGroundGrid(Terrain terrain, Rect east, int resolution, List<Vector2> road)
    {
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, east, resolution);
        var grid = new GroundGrid
        {
            Range = range,
            Resolution = resolution,
            Heights = new float[range.height, range.width],
            Slopes = new float[range.height, range.width],
            RoadDistances = new float[range.height, range.width]
        };
        // 길 선 둘레 RoadBlendEndDistance 밖 칸은 거리를 재지 않는다 (칠하기와 비우기에 쓰지 않는 거리라서)
        Rect roadArea = GetRoadArea(road);
        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                Vector2 world = GetCellWorld(terrain, range.x + x, range.y + z, resolution);
                grid.Heights[z, x] = GetGroundHeight(terrain, world);
                grid.Slopes[z, x] = terrain.terrainData.GetSteepness(GetNormalized(range.x + x, resolution), GetNormalized(range.y + z, resolution));
                grid.RoadDistances[z, x] = road != null && roadArea.Contains(world) ? GetPathDistance(road, world) : float.PositiveInfinity;
            }
        }
        return grid;
    }

    // 길 선을 감싸는 사각형을 RoadBlendEndDistance만큼 넓힌 것. 길이 없으면 빈 사각형
    private static Rect GetRoadArea(List<Vector2> road)
    {
        if (road == null) return Rect.zero;

        return Rect.MinMaxRect(
            road.Min(point => point.x) - RoadBlendEndDistance, road.Min(point => point.y) - RoadBlendEndDistance,
            road.Max(point => point.x) + RoadBlendEndDistance, road.Max(point => point.y) + RoadBlendEndDistance);
    }

    // 사각형과 조금이라도 겹치는 디테일 칸 (칸 안 어디에 놓여도 조각 밑으로 들어가지 않게 한다)
    private static bool[,] MarkCells(Terrain terrain, RectInt range, List<Rect> areas)
    {
        int resolution = terrain.terrainData.detailResolution;
        Vector2 cellSize = GetCellSize(terrain, resolution);
        Vector3 position = terrain.transform.position;
        var marks = new bool[range.height, range.width];
        foreach (Rect area in areas)
        {
            // 칸 i는 Terrain 모서리에서 [i, i + 1) × 칸 크기를 덮는다
            int xMin = Mathf.Max(Mathf.FloorToInt((area.xMin - position.x) / cellSize.x) - range.x, 0);
            int zMin = Mathf.Max(Mathf.FloorToInt((area.yMin - position.z) / cellSize.y) - range.y, 0);
            int xMax = Mathf.Min(Mathf.CeilToInt((area.xMax - position.x) / cellSize.x) - 1 - range.x, range.width - 1);
            int zMax = Mathf.Min(Mathf.CeilToInt((area.yMax - position.z) / cellSize.y) - 1 - range.y, range.height - 1);
            for (int z = zMin; z <= zMax; z++)
            {
                for (int x = xMin; x <= xMax; x++)
                    marks[z, x] = true;
            }
        }
        return marks;
    }

    // ---------- 칠하기 ----------

    // 지면 레이어 : 구분 레이어 → 산 위 흙바위 → 경사 돌 → 수레길 흙 순으로 덮는다 (합 1 유지). C2 범위의 모든 레이어를 새로 쓴다
    private static void PaintAlphamaps(Terrain terrain, GroundGrid grid)
    {
        TerrainData data = terrain.terrainData;
        RectInt range = grid.Range;
        var weights = new float[range.height, range.width, data.alphamapLayers];

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                Vector2 world = GetCellWorld(terrain, range.x + x, range.y + z, grid.Resolution);
                int groundLayer = MapEastScatter.GetEastZone(world) == MapTreeScatter.FieldZone.Forest
                    ? MapTerrainSetup.ForestGroundLayer
                    : MapTerrainSetup.GrassLayer;
                weights[z, x, groundLayer] = 1f;

                BlendLayer(weights, z, x, MapTerrainSetup.DirtRockLayer, GetMountainRockWeight(grid.Heights[z, x]));
                BlendLayer(weights, z, x, MapTerrainSetup.StoneLayer, Mathf.InverseLerp(StoneBlendStartSlope, MapTreeScatter.MaxSlope, grid.Slopes[z, x]));
                BlendLayer(weights, z, x, MapTerrainSetup.DirtLayer, GetRoadWeight(grid.RoadDistances[z, x]));
            }
        }
        data.SetAlphamaps(range.x, range.y, weights);
    }

    // 지금까지의 레이어를 (1 - weight)로 줄이고 layer에 weight를 더한다 (MapGroundPainter.BlendShore와 같은 방식)
    private static void BlendLayer(float[,,] weights, int z, int x, int layer, float weight)
    {
        if (weight <= 0f) return;

        for (int i = 0; i < weights.GetLength(2); i++)
            weights[z, x, i] *= 1f - weight;
        weights[z, x, layer] += weight;
    }

    // 80m에서 0, 95m 이상에서 MountainRockMaxWeight (smoothstep)
    private static float GetMountainRockWeight(float height)
    {
        float t = Mathf.InverseLerp(MountainRockBlendStartHeight, MountainRockBlendEndHeight, height);
        return MountainRockMaxWeight * Mathf.SmoothStep(0f, 1f, t);
    }

    // 길 중심 RoadHalfWidth 안은 1, RoadBlendEndDistance 이상은 0 (smoothstep)
    private static float GetRoadWeight(float roadDistance)
    {
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RoadHalfWidth, RoadBlendEndDistance, roadDistance));
    }

    // 바닥 식생 : 프로토타입마다 C2 범위의 빈 층을 만들어 새로 계산해 덮어쓴다 (서쪽, 남동쪽과 같은 방식)
    private static void PaintDetails(DetailPlan plan)
    {
        TerrainData data = plan.Terrain.terrainData;
        GroundGrid grid = plan.Grid;
        RectInt range = grid.Range;
        MapDetailPainter.PaintContext context = MapDetailPainter.CreateContext(plan.GroupPrototypeIndices, range, Seed);

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                if (grid.Slopes[z, x] >= MapTreeScatter.MaxSlope || IsCleared(plan, x, z)) continue;

                Vector2 world = GetCellWorld(plan.Terrain, range.x + x, range.y + z, grid.Resolution);
                MapDetailPainter.PaintDetailCell(context, x, z, world, GetDetailZone(grid, x, z, world));
            }
        }

        foreach (KeyValuePair<int, int[,]> layer in context.Layers)
            data.SetDetailLayer(range.x, range.y, layer.Key, layer.Value);
    }

    // 발밑, 서식지 표시 자리, 수레길 폭 안 디테일 칸 (경사와 상관없이 비우는 칸)
    private static bool IsCleared(DetailPlan plan, int x, int z)
    {
        return plan.IsFootprint[z, x] || plan.IsHabitatMarker[z, x] || plan.Grid.RoadDistances[z, x] < RoadHalfWidth;
    }

    // 산 위(MountainTopHeight 이상)는 드문드문 규칙, 그 밖은 구분대로
    private static MapTreeScatter.FieldZone GetDetailZone(GroundGrid grid, int x, int z, Vector2 world)
    {
        if (grid.Heights[z, x] >= MapEastScatter.MountainTopHeight) return MapTreeScatter.FieldZone.Sparse;
        return MapEastScatter.GetEastZone(world);
    }

    // ---------- 격자, 계산 ----------

    // 칸 번호(Terrain 전체 기준)의 칸 중심을 0 ~ 1로
    private static float GetNormalized(int index, int resolution)
    {
        return (index + 0.5f) / resolution;
    }

    // 알파맵, 디테일 칸 중심의 월드 XZ
    private static Vector2 GetCellWorld(Terrain terrain, int indexX, int indexZ, int resolution)
    {
        Vector3 position = terrain.transform.position;
        Vector2 cellSize = GetCellSize(terrain, resolution);
        return new Vector2(position.x + (indexX + 0.5f) * cellSize.x, position.z + (indexZ + 0.5f) * cellSize.y);
    }

    private static Vector2 GetCellSize(Terrain terrain, int resolution)
    {
        Vector3 size = terrain.terrainData.size;
        return new Vector2(size.x / resolution, size.z / resolution);
    }

    // 메뉴 25와 같은 방식 (SampleHeight는 Terrain 기준 높이라 Terrain의 y를 더한다)
    private static float GetGroundHeight(Terrain terrain, Vector2 position)
    {
        return terrain.SampleHeight(new Vector3(position.x, 0f, position.y)) + terrain.transform.position.y;
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

    private static Vector2 ToXZ(Vector3 world)
    {
        return new Vector2(world.x, world.z);
    }

    // ---------- 로그 ----------
    // 검수(묶음별 지면 레이어 비율, 흙바위 섞인 칸, 수레길 흙 칸, 빈 풀밭 비율, 비우는 칸 식생 0개, C2 밖 쓰기 없음)에 쓰는 값이라 남긴다.
    // 모두 칠한 뒤 Terrain에서 다시 읽어 잰다

    private static string DescribeFootprints(string label, List<Rect> areas)
    {
        if (areas.Count == 0) return $"{label} : 조각 0개";

        float xMin = areas.Min(area => area.xMin);
        float xMax = areas.Max(area => area.xMax);
        float zMin = areas.Min(area => area.yMin);
        float zMax = areas.Max(area => area.yMax);
        return $"{label} : 조각 {areas.Count}개, 발밑을 합친 범위 x {xMin:F1} ~ {xMax:F1}, z {zMin:F1} ~ {zMax:F1}";
    }

    private static void LogFootprints(FootprintSet footprints)
    {
        Rect habitat = footprints.HabitatArea;
        Debug.Log($"[MapEastPainter] 바닥 식생을 비운 랜드마크 발밑 (렌더러 범위 + {FootprintMargin}m)\n" +
                  string.Join("\n", footprints.Notes) + "\n" +
                  $"서식지 표시 자리 : 중심 ({habitat.center.x:F1}, {habitat.center.y:F1}), {habitat.width:F1}m × {habitat.height:F1}m");
    }

    // 칸이 속한 로그 묶음. 수레길 띠 > 산 위 > 구분
    private static int GetCategory(Terrain terrain, GroundGrid grid, int x, int z)
    {
        if (grid.RoadDistances[z, x] < RoadBlendEndDistance) return RoadCategory;
        if (grid.Heights[z, x] >= MapEastScatter.MountainTopHeight) return MountainCategory;
        return (int)MapEastScatter.GetEastZone(GetCellWorld(terrain, grid.Range.x + x, grid.Range.y + z, grid.Resolution));
    }

    // 묶음별 지면 레이어 비율과 흙바위가 섞인 칸 비율
    private static void LogAlphamaps(Terrain terrain, GroundGrid grid, float[,,] alphas)
    {
        TerrainData data = terrain.terrainData;
        RectInt range = grid.Range;
        int layerCount = data.alphamapLayers;
        var sums = new double[CategoryLabels.Length, layerCount];
        var counts = new int[CategoryLabels.Length];
        int rockCells = 0, rockBelowStartCells = 0, highCells = 0;

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                int category = GetCategory(terrain, grid, x, z);
                counts[category]++;
                for (int layer = 0; layer < layerCount; layer++)
                    sums[category, layer] += alphas[z, x, layer];

                bool isHigh = grid.Heights[z, x] > MountainRockBlendStartHeight;
                if (isHigh) highCells++;
                if (alphas[z, x, MapTerrainSetup.DirtRockLayer] <= 0f) continue;
                rockCells++;
                if (!isHigh) rockBelowStartCells++;
            }
        }

        string[] layerNames = data.terrainLayers.Select(layer => layer.name.Replace("Layer_", "")).ToArray();
        int total = counts.Sum();
        var lines = new List<string>();
        for (int category = 0; category < CategoryLabels.Length; category++)
        {
            if (counts[category] == 0) continue;
            string ratios = string.Join(", ", Enumerable.Range(0, layerCount).Select(layer => $"{layerNames[layer]} {sums[category, layer] * 100 / counts[category]:F1}%"));
            lines.Add($"{CategoryLabels[category]} (C2의 {counts[category] * 100f / total:F1}%) : {ratios}");
        }
        Debug.Log($"[MapEastPainter] 지면 레이어 비율 (산 위 = 지면 {MapEastScatter.MountainTopHeight}m 이상, 수레길 띠 = 길 중심 {RoadBlendEndDistance}m 안)\n" +
                  string.Join("\n", lines));
        Debug.Log($"[MapEastPainter] 흙바위가 섞인 칸 {rockCells}/{total} (C2의 {rockCells * 100f / total:F1}%), " +
                  $"지면 {MountainRockBlendStartHeight}m 초과 칸 {highCells}, 흙바위가 섞였는데 {MountainRockBlendStartHeight}m 이하인 칸 {rockBelowStartCells} (0이어야 한다)");
    }

    // 수레길 흙 칸 수. 흙은 길 띠 밖에 없어야 한다
    private static void LogRoad(GroundGrid grid, float[,,] alphas, List<Vector2> road)
    {
        if (road == null)
        {
            Debug.Log("[MapEastPainter] 수레길 : 길 선이 없어 칠하지 않았습니다.");
            return;
        }

        RectInt range = grid.Range;
        int fullCells = 0, blendCells = 0, outsideCells = 0;
        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                float dirt = alphas[z, x, MapTerrainSetup.DirtLayer];
                if (dirt <= 0f) continue;
                if (grid.RoadDistances[z, x] >= RoadBlendEndDistance) outsideCells++;
                else if (dirt >= FullDirtWeightMin) fullCells++;
                else blendCells++;
            }
        }
        Vector2 start = road[0];
        Vector2 end = road[road.Count - 1];
        Debug.Log($"[MapEastPainter] 수레길 ({start.x:F0}, {start.y:F0}) → ({end.x:F0}, {end.y:F0}), 점 {road.Count}개 : " +
                  $"흙 100% 칸 {fullCells}, 섞인 칸 {blendCells}, 길 띠({RoadBlendEndDistance}m) 밖 흙 칸 {outsideCells} (0이어야 한다)");
    }

    private static void LogDetails(DetailPlan plan, RectInt alphaRange, float[,,] alphas)
    {
        TerrainData data = plan.Terrain.terrainData;
        GroundGrid grid = plan.Grid;
        RectInt range = grid.Range;
        int alphaResolution = data.alphamapResolution;
        int[,] totals = ReadDetailTotals(data, range);

        var categoryInstances = new long[CategoryLabels.Length];
        var categoryCells = new int[CategoryLabels.Length];
        long clearedInstances = 0;
        int clearedCells = 0, grassCells = 0, emptyGrassCells = 0;
        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                int count = totals[z, x];
                int category = GetCategory(plan.Terrain, grid, x, z);
                categoryCells[category]++;
                categoryInstances[category] += count;
                if (IsCleared(plan, x, z))
                {
                    clearedCells++;
                    clearedInstances += count;
                    continue;
                }
                if (grid.Slopes[z, x] >= MapTreeScatter.MaxSlope) continue;

                // 두 범위의 끝이 반올림으로 칸 반 개쯤 어긋날 수 있어 알파맵 범위 안으로 자른다
                int alphaX = Mathf.Clamp(Mathf.FloorToInt(GetNormalized(range.x + x, grid.Resolution) * alphaResolution) - alphaRange.x, 0, alphaRange.width - 1);
                int alphaZ = Mathf.Clamp(Mathf.FloorToInt(GetNormalized(range.y + z, grid.Resolution) * alphaResolution) - alphaRange.y, 0, alphaRange.height - 1);
                if (alphas[alphaZ, alphaX, MapTerrainSetup.GrassLayer] <= GrassWeightMin) continue;

                grassCells++;
                if (count == 0) emptyGrassCells++;
            }
        }

        LogDetailDensity(plan.Terrain, categoryInstances, categoryCells);
        float emptyRatio = grassCells > 0 ? (float)emptyGrassCells / grassCells : 0f;
        Debug.Log($"[MapEastPainter] 풀 비중 > {GrassWeightMin}인데 바닥 식생 0인 칸 {emptyGrassCells}/{grassCells}칸 ({emptyRatio:P1}, 목표 {EmptyGrassRatioMax:P0} 이하, " +
                  $"{(emptyRatio <= EmptyGrassRatioMax ? "안" : "밖")}) / 경사 {MapTreeScatter.MaxSlope}도 이상 칸, 랜드마크 발밑, 수레길 폭, 서식지 표시 자리 칸 제외");
        Debug.Log($"[MapEastPainter] 비우는 칸(랜드마크 발밑, 서식지 표시 자리, 수레길 중심 {RoadHalfWidth}m 안) {clearedCells}칸의 바닥 식생 {clearedInstances}개 (0이어야 한다)");
    }

    // 묶음별 바닥 식생 수와 1ha당 밀도
    private static void LogDetailDensity(Terrain terrain, long[] categoryInstances, int[] categoryCells)
    {
        Vector2 cellSize = GetCellSize(terrain, terrain.terrainData.detailResolution);
        float cellArea = cellSize.x * cellSize.y;
        var lines = new List<string>();
        for (int category = 0; category < CategoryLabels.Length; category++)
        {
            float area = categoryCells[category] * cellArea / HectareArea;
            float density = area > 0f ? categoryInstances[category] / area : 0f;
            lines.Add($"{CategoryLabels[category]} {area:F1}ha : {categoryInstances[category]}개 (1ha당 {density:F0})");
        }
        long totalInstances = categoryInstances.Sum();
        float totalArea = categoryCells.Sum() * cellArea / HectareArea;
        Debug.Log($"[MapEastPainter] C2 바닥 식생 인스턴스 {totalInstances}개 ({totalArea:F1}ha, 1ha당 {totalInstances / totalArea:F0})\n" + string.Join("\n", lines));
    }

    // range 칸마다 모든 프로토타입의 개수 합
    private static int[,] ReadDetailTotals(TerrainData data, RectInt range)
    {
        var totals = new int[range.height, range.width];
        int prototypeCount = data.detailPrototypes.Length;
        for (int i = 0; i < prototypeCount; i++)
        {
            int[,] layer = data.GetDetailLayer(range.x, range.y, range.width, range.height, i);
            for (int z = 0; z < range.height; z++)
            {
                for (int x = 0; x < range.width; x++)
                    totals[z, x] += layer[z, x];
            }
        }
        return totals;
    }

    private static void LogWriteRanges(Terrain terrain, Rect east, RectInt alphaRange, RectInt detailRange)
    {
        TerrainData data = terrain.terrainData;
        Debug.Log($"[MapEastPainter] 쓴 범위 (C2 = x {east.xMin} ~ {east.xMax}, z {east.yMin} ~ {east.yMax})\n" +
                  DescribeRange(terrain, east, alphaRange, data.alphamapResolution, "알파맵") + "\n" +
                  DescribeRange(terrain, east, detailRange, data.detailResolution, "디테일"));
    }

    // 쓴 칸의 중심이 모두 C2 안인지(C2 밖 쓰기 없음), 바로 바깥 칸의 중심이 모두 C2 밖인지(C2를 빠짐없이 덮음)를 본다.
    // 칸 중심은 번호 순으로 커지므로 네 모서리 칸과 바로 바깥 네 줄의 가운데 칸만 보면 된다
    private static string DescribeRange(Terrain terrain, Rect east, RectInt range, int resolution, string label)
    {
        Vector2 first = GetCellWorld(terrain, range.xMin, range.yMin, resolution);
        Vector2 last = GetCellWorld(terrain, range.xMax - 1, range.yMax - 1, resolution);
        bool isInside = east.Contains(first) && east.Contains(last);

        int middleX = range.xMin + range.width / 2;
        int middleZ = range.yMin + range.height / 2;
        bool isCovered = !east.Contains(GetCellWorld(terrain, range.xMin - 1, middleZ, resolution))
                         && !east.Contains(GetCellWorld(terrain, range.xMax, middleZ, resolution))
                         && !east.Contains(GetCellWorld(terrain, middleX, range.yMin - 1, resolution))
                         && !east.Contains(GetCellWorld(terrain, middleX, range.yMax, resolution));

        Vector2 half = GetCellSize(terrain, resolution) * 0.5f;
        return $"{label} : 칸 x {range.xMin} ~ {range.xMax - 1}, z {range.yMin} ~ {range.yMax - 1} / " +
               $"칸 가장자리 x {first.x - half.x:F2} ~ {last.x + half.x:F2}, z {first.y - half.y:F2} ~ {last.y + half.y:F2} / " +
               $"칸 중심 {(isInside ? "모두 C2 안" : "C2 밖 칸 있음")}, C2 {(isCovered ? "빠짐없이 덮음" : "덮지 못한 칸 있음")}";
    }
}
