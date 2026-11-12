using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 메뉴 23 : 남동쪽 구역(C3, 물이 퍼진 낮은 습지와 바위 언덕)의 지면 레이어와 바닥 식생(Terrain 디테일)을 칠한다 (이슈 #13).
// 구조는 남서쪽 메뉴 16(MapSouthWestPainter)과 남쪽 메뉴 19(MapSouthPainter)를 합친 것이다.
// 구분은 MapSouthEastScatter.CalculateZoneThresholds, GetSouthEastZone으로 해서 나무 분포(메뉴 22)와 맞춘다.
// 물은 연못 목록 없이 지형에서 직접 잰다 : 칸 중심 지형 높이 < MapSouthEastShaper.WaterLevel(29m)이면 물속이다.
// 물까지 거리는 알파맵, 디테일 격자마다 물 칸에서 앞뒤로 두 번 훑는 chamfer 거리 변환(메뉴 22와 같은 방식)으로 구하고,
// 칸 중심끼리 잰 거리라 물 경계까지 거리로 쓰려고 칸 반 크기를 뺀다. 물은 C3 경계 80m 안에 생기지 않아(메뉴 20) C3 밖을 보지 않아도 된다.
// 규칙 (사용자 승인 임시값)
// - 지면 : 풀습지·드문드문은 풀, 작은 무리는 숲 바닥. 경사 StoneBlendStartSlope부터 MapTreeScatter.MaxSlope까지 돌로 섞는다 (남서쪽과 같은 규칙).
//   물속 바닥은 물가 레이어만 쓴다. 물 밖은 물 경계에서 ShoreFullDistance 안은 물가 레이어, ShoreBlendEndDistance까지 아래 레이어로 섞는다 (smoothstep).
//   흙(Dirt) 레이어는 쓰지 않는다.
// - 바닥 식생 : 프로토타입 이름, 비중, 칸당 개수는 남서쪽(메뉴 16)과 같다. 풀습지 = 남서쪽 풀밭(풀 2 ~ 3), 드문드문 풀 1 ~ 2,
//   작은 무리 = 나무 무리(고사리 0 ~ 1, 낙엽 1). 꽃은 풀습지와 드문드문에 피고, 꽃이 피는 칸 비율을 서쪽 규칙의 FlowerAreaMultiplier배로 늘린다.
//   물가 띠(물 밖, 물 경계에서 ShoreBlendEndDistance 안)는 고사리 1 + 풀 0 ~ 1이다 (남쪽 물가 띠와 같은 칸당 개수). 낙엽은 작은 무리 안에만 둔다.
// - 바닥 식생을 비우는 곳 : 지형 DetailMinHeight(수면 + 0.3m = 29.3m) 미만(물속 포함), 경사 MaxSlope 이상,
//   야영지·다리·절벽 조각 발밑(렌더러 범위 XZ + FootprintMargin. 다리는 걷는 면 상자 범위도 넣는다), 서식지 표시 자리(HabitatMarkerClearHalfSize).
// - Terrain에 디테일 프로토타입이나 지면 레이어를 새로 넣지 않는다. 쓸 프로토타입이 등록되어 있지 않으면 바꾸지 않고 멈춘다.
// C3 범위(칸 중심이 C3 안인 칸)만 매번 다시 계산해 덮어쓰므로 여러 번 실행해도 결과가 같고, 다른 구역의 지면과 바닥 식생은 그대로 남는다.
// 실행 순서 : ... → 19 → 20 → 21 → 22 → 23 → 10.
//   9-1(전체 초기화), 20(남동쪽 지형), 22(구분과 랜드마크 위치)를 다시 실행하면 23도 다시 실행한다.
public static class MapSouthEastPainter
{
    // 순서는 MapDetailPainter의 그룹 순서와 같다 (MapDetailPainter.CreateContext가 받는 그룹별 프로토타입 번호 배열의 순서)
    private enum DetailGroup
    {
        Grass = 0,
        Fern = 1,
        Leaf = 2,
        Flower = 3
    }

    // 꽃이 피는 칸 비율 (서쪽 규칙으로 잰 비율, 늘린 목표 비율, 그 비율이 되는 노이즈 경계)
    private struct FlowerRule
    {
        public float WestRatio;
        public float TargetRatio;
        public float Threshold;
    }

    // 알파맵 또는 디테일 격자 하나 (C3 범위)의 칸별 지면 높이와 물 경계까지 거리. 배열은 [z, x]
    private class GroundGrid
    {
        public RectInt Range;
        public int Resolution;
        public float[,] Heights;
        // 물 칸은 0, 물이 없으면 무한대 (m)
        public float[,] WaterDistances;
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
        public float[] ZoneThresholds;
        public FlowerRule Flower;
        // 발밑, 서식지 표시 자리에 걸치는 디테일 칸
        public bool[,] IsFootprint;
        public bool[,] IsHabitatMarker;
    }

    private const string RegionName = "SouthEast";
    private const string LandmarkParentName = "Landmarks";
    private const string RockParentName = "Rocks";
    private const string HabitatName = "Habitat";
    // SouthEast/Rocks 아래 prefab 중 이 이름으로 시작하는 것을 절벽 조각으로 본다 (메뉴 22의 CliffPaths. 바위, 물가 소품은 발밑을 비우지 않는다)
    private const string CliffPrefabPrefix = "Prefab_Cliff_";

    // 앞 메뉴들의 seed(20261109 ~ 20261154)와 겹치지 않게 둔다
    private const int Seed = 20261155;

    // MapDetailPainter의 StoneBlendStartSlope와 같은 값. 그쪽이 private이라 여기 다시 둔다 (바뀌면 함께 바꾼다)
    private const float StoneBlendStartSlope = 25f;

    // ---------- 물가 (사용자 승인 임시값) ----------
    // 물 경계에서 이 거리 안은 물가 레이어, 그 밖 ShoreBlendEndDistance까지 아래 레이어로 섞는다. 물가 띠 바닥 식생도 ShoreBlendEndDistance까지다
    private const float ShoreFullDistance = 5f;
    private const float ShoreBlendEndDistance = 10f;
    // 지형이 수면 + 이 높이 미만인 칸은 바닥 식생을 두지 않는다 (물가에서 풀이 물에 잠겨 보이지 않게)
    private const float DetailWaterClearance = 0.3f;
    private const float DetailMinHeight = MapSouthEastShaper.WaterLevel + DetailWaterClearance;
    // 랜드마크·절벽 조각 발밑 : 렌더러 범위 XZ를 이만큼 넓힌 사각형에 걸치는 칸을 비운다 (남쪽 메뉴 19와 같은 값)
    private const float FootprintMargin = 0.3f;
    // 서식지 표시 자리 : 표시 위치에서 이 반폭의 사각형에 걸치는 칸을 비운다 (임시값, 사용자 확인 필요)
    private const float HabitatMarkerClearHalfSize = 1f;

    // ---------- 바닥 식생 규칙 ----------
    // 아래 프로토타입 이름, 비중, 칸당 개수, 꽃 노이즈는 MapDetailPainter, MapSouthWestPainter와 같은 값이다. 그쪽이 private이라 여기 다시 둔다 (바뀌면 함께 바꾼다)
    private static readonly string[][] GroupPrefabNames =
    {
        new[] { "Prefab_Terrain_Grass_Medium01", "Prefab_Terrain_Grass_Medium02", "Prefab_Terrain_Grass_Medium03", "Prefab_Terrain_Grass_Small01" },
        new[] { "Prefab_Terrain_Fern_02", "Prefab_Terrain_Fern_03" },
        new[] { "Prefab_Terrain_Groundleaf_01" },
        new[] { "Prefab_FlowerGroup_White_Detail", "Prefab_FlowerGroup_Yellow_Detail", "Prefab_FlowerSingle_Purple_Detail", "Prefab_FlowerSingle_Red_Detail" }
    };
    private static readonly float[][] GroupPrefabWeights =
    {
        new[] { 0.85f / 3f, 0.85f / 3f, 0.85f / 3f, 0.15f },
        new[] { 1f, 1f },
        new[] { 1f },
        new[] { 1f, 1f, 1f, 1f }
    };
    private const int ForestFernMax = 1;
    private const int ForestLeafCount = 1;
    private const int SparseGrassMin = 1;
    private const int SparseGrassMax = 2;
    private const int MarshGrassMin = 2;
    private const int MarshGrassMax = 3;
    private const int FlowerCountMin = 1;
    private const int FlowerCountMax = 2;
    private const float FlowerNoisePeriod = 30f;
    private static readonly Vector2 FlowerNoiseOffset = new Vector2(523.9f, 187.1f);
    // 서쪽(와 중앙)에서 꽃이 피는 노이즈 경계
    private const float WestFlowerNoiseThreshold = 0.65f;
    // 꽃이 피는 칸 비율을 서쪽 규칙의 몇 배로 할지 (사용자 승인 임시값, 남서쪽과 같은 값)
    private const float FlowerAreaMultiplier = 2f;
    // 물가 띠 칸당 개수 : 고사리 1 + 풀 0 ~ 1 (남쪽 메뉴 19와 같은 값)
    private const int ShoreFernCount = 1;
    private const int ShoreGrassMax = 1;

    // ---------- 검수 로그 기준 ----------
    // 풀 레이어 비중이 이 값보다 큰 칸을 풀밭으로 본다 (메뉴 9-3 MapGrassFill과 같은 값)
    private const float GrassWeightMin = 0.5f;
    // 풀밭인데 바닥 식생이 0인 칸의 목표 비율 (이하)
    private const float EmptyGrassRatioMax = 0.1f;
    private const float HectareArea = 10000f;
    // 지면 비율 로그의 묶음. 0 ~ 2는 MapTreeScatter.FieldZone (풀습지, 드문드문, 작은 무리)이고, 물속 > 물가 띠 > 구분 순으로 고른다
    private const int WaterCategory = 3;
    private const int ShoreBandCategory = 4;
    private static readonly string[] CategoryLabels = { "풀습지", "드문드문", "작은 무리", "물속", "물가 띠" };

    [MenuItem("Tools/Map/23. 남동쪽 지면과 바닥 식생 칠하기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        TerrainData data = terrain.terrainData;
        // 방식을 바꾸면 다른 구역의 기존 값 뜻이 달라지므로 바꾸지 않고 멈춘다 (메뉴 9-2가 개수 방식으로 맞춘다)
        if (data.detailScatterMode != DetailScatterMode.InstanceCountMode)
        {
            Debug.LogError($"[MapSouthEastPainter] 디테일 방식이 {data.detailScatterMode}입니다. 먼저 'Tools/Map/9-2. 북쪽 데모 지면 이식'을 실행하세요. 지면과 바닥 식생을 바꾸지 않았습니다.");
            return;
        }

        FootprintSet footprints = FindFootprints(terrain);
        if (footprints == null) return;

        int[][] groupPrototypeIndices = FindPrototypeIndices(data);
        if (groupPrototypeIndices == null) return;

        Rect southEast = MapGrid.GetRangeBounds(MapSouthEastShaper.SouthEastCell, MapSouthEastShaper.SouthEastCell);
        float[] thresholds = MapSouthEastScatter.CalculateZoneThresholds(terrain);
        GroundGrid alphaGrid = CreateGroundGrid(terrain, southEast, data.alphamapResolution);
        GroundGrid detailGrid = CreateGroundGrid(terrain, southEast, data.detailResolution);
        var plan = new DetailPlan
        {
            Terrain = terrain,
            Grid = detailGrid,
            GroupPrototypeIndices = groupPrototypeIndices,
            ZoneThresholds = thresholds,
            Flower = CalculateFlowerRule(terrain, detailGrid.Range),
            IsFootprint = MarkCells(terrain, detailGrid.Range, footprints.Areas),
            IsHabitatMarker = MarkCells(terrain, detailGrid.Range, new List<Rect> { footprints.HabitatArea })
        };

        PaintAlphamaps(terrain, alphaGrid, thresholds);
        PaintDetails(plan);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        LogFootprints(footprints);
        LogAlphamaps(terrain, alphaGrid, thresholds);
        LogDetails(plan, alphaGrid.Range);
        LogWriteRanges(terrain, southEast, alphaGrid.Range, detailGrid.Range);
    }

    // ---------- 준비 ----------

    // 야영지(배 포함)·다리(걷는 면 상자 포함)는 SouthEast/Landmarks 아래 모든 prefab 조각, 절벽은 SouthEast/Rocks 아래 Prefab_Cliff_* 조각의 발밑.
    // 서식지 표시 자리는 SouthEast/Habitat 위치에서 잰다 (메뉴 22가 놓은 자리를 그대로 쓴다)
    private static FootprintSet FindFootprints(Terrain terrain)
    {
        GameObject southEastRoot = terrain.gameObject.scene.GetRootGameObjects().FirstOrDefault(root => root.name == RegionName);
        Transform landmarks = southEastRoot != null ? southEastRoot.transform.Find(LandmarkParentName) : null;
        Transform rocks = southEastRoot != null ? southEastRoot.transform.Find(RockParentName) : null;
        Transform habitat = southEastRoot != null ? southEastRoot.transform.Find(HabitatName) : null;
        if (landmarks == null || rocks == null || habitat == null)
        {
            Debug.LogError("[MapSouthEastPainter] SouthEast/Landmarks, Rocks, Habitat 중 없는 것이 있습니다. 먼저 'Tools/Map/22. 남동쪽 나무·바위·랜드마크·서식지 배치'를 실행하세요. 지면과 바닥 식생을 바꾸지 않았습니다.");
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

        List<Rect> cliffs = MeasureFootprints(rocks.Cast<Transform>()
            .Select(child => child.gameObject)
            .Where(piece => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(piece).Contains("/" + CliffPrefabPrefix)));
        set.Areas.AddRange(cliffs);
        set.Notes.Add(DescribeFootprints("절벽 조각", cliffs));
        return set;
    }

    // 조각마다 렌더러 범위 XZ (걷는 면 상자가 있으면 그 꼭짓점도 넣음)를 FootprintMargin만큼 넓힌 사각형
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
            // 다리 걷는 면 상자는 렌더러가 없고 줄기 끝보다 길 수 있어 따로 넣는다.
            // Collider.bounds는 에디터에서 변환 반영이 늦을 수 있어 상자 꼭짓점을 직접 월드로 옮긴다
            foreach (BoxCollider box in piece.GetComponentsInChildren<BoxCollider>())
            {
                foreach (Vector3 corner in GetBoxCorners(box))
                    bounds.Encapsulate(corner);
            }
            areas.Add(Rect.MinMaxRect(
                bounds.min.x - FootprintMargin, bounds.min.z - FootprintMargin,
                bounds.max.x + FootprintMargin, bounds.max.z + FootprintMargin));
        }
        return areas;
    }

    private static IEnumerable<Vector3> GetBoxCorners(BoxCollider box)
    {
        Vector3 half = box.size * 0.5f;
        for (int i = 0; i < 8; i++)
        {
            var sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
            yield return box.transform.TransformPoint(box.center + Vector3.Scale(half, sign));
        }
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

        Debug.LogError($"[MapSouthEastPainter] Terrain에 등록되지 않은 디테일 프로토타입이 있습니다 : {string.Join(", ", missing)}. " +
                       "먼저 'Tools/Map/4. 중앙 바닥 식생 칠하기'를 실행하세요. 지면과 바닥 식생을 바꾸지 않았습니다.");
        return null;
    }

    // C3 범위 칸마다 지면 높이를 재고, 물(WaterLevel 미만) 칸에서 물 경계까지 거리를 구한다.
    // 칸 중심끼리 잰 거리에서 칸 반 크기를 빼 물 경계까지 거리로 쓴다 (물 바로 옆 칸 ≈ 칸 반 크기)
    private static GroundGrid CreateGroundGrid(Terrain terrain, Rect southEast, int resolution)
    {
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, southEast, resolution);
        var grid = new GroundGrid { Range = range, Resolution = resolution, Heights = new float[range.height, range.width] };
        var isWater = new bool[range.height, range.width];
        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                float height = GetGroundHeight(terrain, GetCellWorld(terrain, range.x + x, range.y + z, resolution));
                grid.Heights[z, x] = height;
                isWater[z, x] = height < MapSouthEastShaper.WaterLevel;
            }
        }

        // Terrain이 정사각형이라 칸도 정사각형이다
        float spacing = GetCellSize(terrain, resolution).x;
        grid.WaterDistances = CalculateDistance(isWater, spacing);
        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                if (!isWater[z, x]) grid.WaterDistances[z, x] -= spacing * 0.5f;
            }
        }
        return grid;
    }

    // isSource 칸까지의 대략 거리 (m). 앞뒤로 두 번 훑는 chamfer 방식 (가로세로 spacing, 대각선 spacing × √2).
    // MapSouthEastScatter.CalculateDistance와 같은 방식이다. 그쪽이 private이라 여기 다시 둔다
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

    // C3 디테일 칸 전체에서 서쪽 규칙(노이즈 > 0.65)으로 꽃이 피는 칸 비율을 재고, 그 FlowerAreaMultiplier배가 되는 노이즈 경계를 분위수로 구한다.
    // 노이즈 무늬는 서쪽과 같으므로 꽃 무리 모양은 같고 넓이만 늘어난다 (남서쪽 메뉴 16과 같은 방식)
    private static FlowerRule CalculateFlowerRule(Terrain terrain, RectInt range)
    {
        int resolution = terrain.terrainData.detailResolution;
        var noises = new List<float>(range.width * range.height);
        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
                noises.Add(SampleFlowerNoise(GetCellWorld(terrain, range.x + x, range.y + z, resolution)));
        }

        int total = noises.Count;
        float westRatio = (float)noises.Count(noise => noise > WestFlowerNoiseThreshold) / total;
        float targetRatio = Mathf.Min(1f, westRatio * FlowerAreaMultiplier);
        int targetCount = Mathf.RoundToInt(total * targetRatio);
        noises.Sort();

        // 경계보다 큰 칸이 targetCount개가 되도록 고른다
        float threshold = targetCount >= total ? float.MinValue : noises[total - targetCount - 1];
        return new FlowerRule { WestRatio = westRatio, TargetRatio = targetRatio, Threshold = threshold };
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

    // 1이면 완전히 물가 레이어, 0이면 물가가 아니다
    private static float GetShoreWeight(float waterDistance)
    {
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ShoreFullDistance, ShoreBlendEndDistance, waterDistance));
    }

    // ---------- 칠하기 ----------

    // 지면 레이어 : 물속은 물가 레이어만, 물 밖은 구분 레이어 + 경사 돌 → 물가 띠 순으로 덮는다 (합 1 유지). C3 범위의 모든 레이어를 새로 쓴다
    private static void PaintAlphamaps(Terrain terrain, GroundGrid grid, float[] thresholds)
    {
        TerrainData data = terrain.terrainData;
        RectInt range = grid.Range;
        var weights = new float[range.height, range.width, data.alphamapLayers];

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                if (grid.Heights[z, x] < MapSouthEastShaper.WaterLevel)
                {
                    weights[z, x, MapTerrainSetup.ShoreLayer] = 1f;
                    continue;
                }

                Vector2 world = GetCellWorld(terrain, range.x + x, range.y + z, grid.Resolution);
                int groundLayer = MapSouthEastScatter.GetSouthEastZone(world, thresholds) == MapTreeScatter.FieldZone.Forest
                    ? MapTerrainSetup.ForestGroundLayer
                    : MapTerrainSetup.GrassLayer;
                float stoneWeight = Mathf.InverseLerp(StoneBlendStartSlope, MapTreeScatter.MaxSlope, GetCellSlope(data, range.x + x, range.y + z, grid.Resolution));
                weights[z, x, groundLayer] = 1f - stoneWeight;
                weights[z, x, MapTerrainSetup.StoneLayer] = stoneWeight;

                MapGroundPainter.BlendShore(weights, z, x, GetShoreWeight(grid.WaterDistances[z, x]));
            }
        }
        data.SetAlphamaps(range.x, range.y, weights);
    }

    // 바닥 식생 : 프로토타입마다 C3 범위의 빈 층을 만들어 새로 계산해 덮어쓴다 (남서쪽, 남쪽과 같은 방식)
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
                if (grid.Heights[z, x] < DetailMinHeight) continue;
                if (GetCellSlope(data, range.x + x, range.y + z, grid.Resolution) >= MapTreeScatter.MaxSlope) continue;
                if (plan.IsFootprint[z, x] || plan.IsHabitatMarker[z, x]) continue;

                Vector2 world = GetCellWorld(plan.Terrain, range.x + x, range.y + z, grid.Resolution);
                if (grid.WaterDistances[z, x] < ShoreBlendEndDistance) PaintShoreCell(context, x, z);
                else PaintZoneCell(context, x, z, world, MapSouthEastScatter.GetSouthEastZone(world, plan.ZoneThresholds), plan.Flower.Threshold);
            }
        }

        foreach (KeyValuePair<int, int[,]> layer in context.Layers)
            data.SetDetailLayer(range.x, range.y, layer.Key, layer.Value);
    }

    // 물가 띠 칸 : 고사리 1 + 풀 0 ~ 1
    private static void PaintShoreCell(MapDetailPainter.PaintContext context, int x, int z)
    {
        AddDetail(context, x, z, DetailGroup.Fern, ShoreFernCount);
        AddDetail(context, x, z, DetailGroup.Grass, context.Random.Next(0, ShoreGrassMax + 1));
    }

    // 칸 하나 : 작은 무리는 고사리·낙엽, 드문드문은 풀 1 ~ 2, 풀습지는 풀 2 ~ 3. 풀습지와 드문드문은 꽃 노이즈가 경계보다 크면 꽃 1 ~ 2를 더한다
    private static void PaintZoneCell(MapDetailPainter.PaintContext context, int x, int z, Vector2 world, MapTreeScatter.FieldZone zone, float flowerThreshold)
    {
        System.Random random = context.Random;
        if (zone == MapTreeScatter.FieldZone.Forest)
        {
            AddDetail(context, x, z, DetailGroup.Fern, random.Next(0, ForestFernMax + 1));
            AddDetail(context, x, z, DetailGroup.Leaf, ForestLeafCount);
            return;
        }

        int grassCount = zone == MapTreeScatter.FieldZone.Sparse
            ? random.Next(SparseGrassMin, SparseGrassMax + 1)
            : random.Next(MarshGrassMin, MarshGrassMax + 1);
        AddDetail(context, x, z, DetailGroup.Grass, grassCount);
        if (SampleFlowerNoise(world) > flowerThreshold)
            AddDetail(context, x, z, DetailGroup.Flower, random.Next(FlowerCountMin, FlowerCountMax + 1));
    }

    // 그룹 안에서 prefab 하나를 비중대로 골라 그 칸의 개수를 더한다
    private static void AddDetail(MapDetailPainter.PaintContext context, int x, int z, DetailGroup group, int count)
    {
        int[] indices = context.GroupPrototypeIndices[(int)group];
        int prototypeIndex = indices[PickWeightedIndex(context.Random, GroupPrefabWeights[(int)group])];
        context.Layers[prototypeIndex][z, x] += count;
    }

    private static int PickWeightedIndex(System.Random random, float[] weights)
    {
        double roll = random.NextDouble() * weights.Sum();
        for (int i = 0; i < weights.Length - 1; i++)
        {
            roll -= weights[i];
            if (roll < 0) return i;
        }
        return weights.Length - 1;
    }

    private static float SampleFlowerNoise(Vector2 world)
    {
        return Mathf.PerlinNoise(world.x / FlowerNoisePeriod + FlowerNoiseOffset.x, world.y / FlowerNoisePeriod + FlowerNoiseOffset.y);
    }

    // ---------- 격자, 계산 ----------

    // 칸 번호(Terrain 전체 기준)의 칸 중심을 0 ~ 1로
    private static float GetNormalized(int index, int resolution)
    {
        return (index + 0.5f) / resolution;
    }

    private static float GetCellSlope(TerrainData data, int indexX, int indexZ, int resolution)
    {
        return data.GetSteepness(GetNormalized(indexX, resolution), GetNormalized(indexZ, resolution));
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

    // 메뉴 22와 같은 방식 (SampleHeight는 Terrain 기준 높이라 Terrain의 y를 더한다)
    private static float GetGroundHeight(Terrain terrain, Vector2 position)
    {
        return terrain.SampleHeight(new Vector3(position.x, 0f, position.y)) + terrain.transform.position.y;
    }

    private static Vector2 ToXZ(Vector3 world)
    {
        return new Vector2(world.x, world.z);
    }

    // ---------- 로그 ----------
    // 검수(구분별 지면 레이어 비율과 흙 0%, 디테일 종류별 수와 꽃 비율, 빈 풀밭 비율, 물속·29.3m 미만·발밑·서식지 표시 자리 식생 0개,
    // C3 밖 쓰기 없음)에 쓰는 값이라 남긴다. 모두 칠한 뒤 Terrain에서 다시 읽어 잰다

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
        Debug.Log($"[MapSouthEastPainter] 바닥 식생을 비운 발밑 (렌더러 범위 + {FootprintMargin}m, 다리는 걷는 면 상자 포함)\n" +
                  string.Join("\n", footprints.Notes) + "\n" +
                  $"서식지 표시 자리 : 중심 ({habitat.center.x:F1}, {habitat.center.y:F1}), {habitat.width:F1}m × {habitat.height:F1}m");
    }

    // 칸이 속한 로그 묶음. 물속 > 물가 띠 > 구분
    private static int GetCategory(GroundGrid grid, int x, int z, Vector2 world, float[] thresholds)
    {
        if (grid.Heights[z, x] < MapSouthEastShaper.WaterLevel) return WaterCategory;
        if (grid.WaterDistances[z, x] < ShoreBlendEndDistance) return ShoreBandCategory;
        return (int)MapSouthEastScatter.GetSouthEastZone(world, thresholds);
    }

    // C3 전체와 묶음별 지면 레이어 비율
    private static void LogAlphamaps(Terrain terrain, GroundGrid grid, float[] thresholds)
    {
        TerrainData data = terrain.terrainData;
        RectInt range = grid.Range;
        int layerCount = data.alphamapLayers;
        float[,,] alphas = data.GetAlphamaps(range.x, range.y, range.width, range.height);
        var sums = new double[CategoryLabels.Length, layerCount];
        var counts = new int[CategoryLabels.Length];

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                Vector2 world = GetCellWorld(terrain, range.x + x, range.y + z, grid.Resolution);
                int category = GetCategory(grid, x, z, world, thresholds);
                counts[category]++;
                for (int layer = 0; layer < layerCount; layer++)
                    sums[category, layer] += alphas[z, x, layer];
            }
        }

        string[] layerNames = data.terrainLayers.Select(layer => layer.name.Replace("Layer_", "")).ToArray();
        int total = counts.Sum();
        string totalRatios = string.Join(", ", Enumerable.Range(0, layerCount)
            .Select(layer => $"{layerNames[layer]} {Enumerable.Range(0, CategoryLabels.Length).Sum(category => sums[category, layer]) * 100 / total:F1}%"));
        double dirtRatio = Enumerable.Range(0, CategoryLabels.Length).Sum(category => sums[category, MapTerrainSetup.DirtLayer]) * 100 / total;
        var lines = new List<string> { $"C3 전체 : {totalRatios}", $"흙(Dirt) {dirtRatio:F2}% (0이어야 한다)" };
        for (int category = 0; category < CategoryLabels.Length; category++)
        {
            if (counts[category] == 0) continue;
            string ratios = string.Join(", ", Enumerable.Range(0, layerCount).Select(layer => $"{layerNames[layer]} {sums[category, layer] * 100 / counts[category]:F1}%"));
            lines.Add($"{CategoryLabels[category]} (C3의 {counts[category] * 100f / total:F1}%) : {ratios}");
        }
        Debug.Log($"[MapSouthEastPainter] 지면 레이어 비율 (물속 = 지형 {MapSouthEastShaper.WaterLevel}m 미만, 물가 띠 = 물 밖이면서 물 경계 {ShoreBlendEndDistance}m 안)\n" +
                  string.Join("\n", lines));
    }

    private static void LogDetails(DetailPlan plan, RectInt alphaRange)
    {
        TerrainData data = plan.Terrain.terrainData;
        RectInt range = plan.Grid.Range;
        Dictionary<int, int[,]> layers = Enumerable.Range(0, data.detailPrototypes.Length)
            .ToDictionary(index => index, index => data.GetDetailLayer(range.x, range.y, range.width, range.height, index));
        int[] flowerIndices = plan.GroupPrototypeIndices[(int)DetailGroup.Flower];

        long totalInstances = layers.Values.Sum(layer => (long)layer.Cast<int>().Sum());
        long flowerInstances = flowerIndices.Sum(index => (long)layers[index].Cast<int>().Sum());
        IEnumerable<string> kindLines = layers
            .Select(layer => (Index: layer.Key, Count: layer.Value.Cast<int>().Sum()))
            .Where(kind => kind.Count > 0)
            .Select(kind => $"{GetPrototypeName(data.detailPrototypes[kind.Index])} : {kind.Count}개");
        Vector2 cellSize = GetCellSize(plan.Terrain, data.detailResolution);
        float area = range.width * range.height * cellSize.x * cellSize.y / HectareArea;
        Debug.Log($"[MapSouthEastPainter] C3 바닥 식생 인스턴스 {totalInstances}개 ({area:F1}ha, 1ha당 {totalInstances / area:F0}), " +
                  $"꽃 {flowerInstances}개 (전체의 {(totalInstances > 0 ? (float)flowerInstances / totalInstances : 0f):P1})\n" + string.Join("\n", kindLines));

        LogCellChecks(plan, alphaRange, layers, flowerIndices);
    }

    // 텍스처 방식 프로토타입(다른 메뉴가 넣었을 수 있음)은 prefab이 없어 텍스처 이름을 쓴다
    private static string GetPrototypeName(DetailPrototype prototype)
    {
        if (prototype.prototype != null) return prototype.prototype.name;
        return prototype.prototypeTexture != null ? prototype.prototypeTexture.name : "(이름 없음)";
    }

    // 칸 단위 검사 : 비워야 하는 칸(물속, 29.3m 미만, 발밑, 서식지 표시 자리)의 식생 수, 꽃이 핀 칸 비율 (칠한 풀습지·드문드문 칸),
    // 빈 풀밭 비율 (비우는 칸과 경사 MaxSlope 이상 칸 제외)
    private static void LogCellChecks(DetailPlan plan, RectInt alphaRange, Dictionary<int, int[,]> layers, int[] flowerIndices)
    {
        TerrainData data = plan.Terrain.terrainData;
        GroundGrid grid = plan.Grid;
        RectInt range = grid.Range;
        int resolution = grid.Resolution;
        int alphaResolution = data.alphamapResolution;
        float[,,] alphas = data.GetAlphamaps(alphaRange.x, alphaRange.y, alphaRange.width, alphaRange.height);

        long inWaterInstances = 0, nearWaterInstances = 0, onFootprintInstances = 0, onHabitatInstances = 0;
        int footprintCells = 0, habitatCells = 0, fieldCells = 0, flowerCells = 0, grassCells = 0, emptyGrassCells = 0;
        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                int count = layers.Values.Sum(layer => layer[z, x]);
                float height = grid.Heights[z, x];
                if (height < MapSouthEastShaper.WaterLevel)
                {
                    inWaterInstances += count;
                    continue;
                }
                if (height < DetailMinHeight)
                {
                    nearWaterInstances += count;
                    continue;
                }
                if (plan.IsFootprint[z, x])
                {
                    footprintCells++;
                    onFootprintInstances += count;
                    continue;
                }
                if (plan.IsHabitatMarker[z, x])
                {
                    habitatCells++;
                    onHabitatInstances += count;
                    continue;
                }
                if (GetCellSlope(data, range.x + x, range.y + z, resolution) >= MapTreeScatter.MaxSlope) continue;

                Vector2 world = GetCellWorld(plan.Terrain, range.x + x, range.y + z, resolution);
                if (grid.WaterDistances[z, x] >= ShoreBlendEndDistance
                    && MapSouthEastScatter.GetSouthEastZone(world, plan.ZoneThresholds) != MapTreeScatter.FieldZone.Forest)
                {
                    fieldCells++;
                    if (flowerIndices.Any(index => layers[index][z, x] > 0)) flowerCells++;
                }

                // 두 범위의 끝이 반올림으로 칸 반 개쯤 어긋날 수 있어 알파맵 범위 안으로 자른다
                int alphaX = Mathf.Clamp(Mathf.FloorToInt(GetNormalized(range.x + x, resolution) * alphaResolution) - alphaRange.x, 0, alphaRange.width - 1);
                int alphaZ = Mathf.Clamp(Mathf.FloorToInt(GetNormalized(range.y + z, resolution) * alphaResolution) - alphaRange.y, 0, alphaRange.height - 1);
                if (alphas[alphaZ, alphaX, MapTerrainSetup.GrassLayer] <= GrassWeightMin) continue;

                grassCells++;
                if (count == 0) emptyGrassCells++;
            }
        }

        FlowerRule flower = plan.Flower;
        Debug.Log($"[MapSouthEastPainter] 꽃 : 서쪽 규칙(노이즈 > {WestFlowerNoiseThreshold})이면 C3 칸의 {flower.WestRatio:P1}, " +
                  $"{FlowerAreaMultiplier}배 목표 {flower.TargetRatio:P1} (노이즈 경계 {flower.Threshold:F3}). " +
                  $"실제 칠한 풀습지·드문드문 칸(물가 띠 제외) 중 꽃이 핀 칸 {flowerCells}/{fieldCells} ({(fieldCells > 0 ? (float)flowerCells / fieldCells : 0f):P1})");

        float emptyRatio = grassCells > 0 ? (float)emptyGrassCells / grassCells : 0f;
        Debug.Log($"[MapSouthEastPainter] 풀 비중 > {GrassWeightMin}인데 바닥 식생 0인 칸 {emptyGrassCells}/{grassCells}칸 ({emptyRatio:P1}, 목표 {EmptyGrassRatioMax:P0} 이하, " +
                  $"{(emptyRatio <= EmptyGrassRatioMax ? "안" : "밖")}) / 지형 {DetailMinHeight}m 미만 칸, 발밑 칸, 서식지 표시 자리 칸, 경사 {MapTreeScatter.MaxSlope}도 이상 칸 제외");
        Debug.Log($"[MapSouthEastPainter] 바닥 식생 (모두 0이어야 한다) : 물속(지형 {MapSouthEastShaper.WaterLevel}m 미만) {inWaterInstances}개, " +
                  $"지형 {MapSouthEastShaper.WaterLevel} ~ {DetailMinHeight}m {nearWaterInstances}개, 발밑 칸 {footprintCells}칸에 {onFootprintInstances}개, " +
                  $"서식지 표시 자리 칸 {habitatCells}칸에 {onHabitatInstances}개");
    }

    private static void LogWriteRanges(Terrain terrain, Rect southEast, RectInt alphaRange, RectInt detailRange)
    {
        TerrainData data = terrain.terrainData;
        Debug.Log($"[MapSouthEastPainter] 쓴 범위 (C3 = x {southEast.xMin} ~ {southEast.xMax}, z {southEast.yMin} ~ {southEast.yMax})\n" +
                  DescribeRange(terrain, southEast, alphaRange, data.alphamapResolution, "알파맵") + "\n" +
                  DescribeRange(terrain, southEast, detailRange, data.detailResolution, "디테일"));
    }

    // 쓴 칸의 중심이 모두 C3 안인지(C3 밖 쓰기 없음), 바로 바깥 칸의 중심이 모두 C3 밖인지(C3를 빠짐없이 덮음)를 본다.
    // 칸 중심은 번호 순으로 커지므로 네 모서리 칸과 바로 바깥 네 줄의 가운데 칸만 보면 된다
    private static string DescribeRange(Terrain terrain, Rect southEast, RectInt range, int resolution, string label)
    {
        Vector2 first = GetCellWorld(terrain, range.xMin, range.yMin, resolution);
        Vector2 last = GetCellWorld(terrain, range.xMax - 1, range.yMax - 1, resolution);
        bool isInside = southEast.Contains(first) && southEast.Contains(last);

        int middleX = range.xMin + range.width / 2;
        int middleZ = range.yMin + range.height / 2;
        bool isCovered = !southEast.Contains(GetCellWorld(terrain, range.xMin - 1, middleZ, resolution))
                         && !southEast.Contains(GetCellWorld(terrain, range.xMax, middleZ, resolution))
                         && !southEast.Contains(GetCellWorld(terrain, middleX, range.yMin - 1, resolution))
                         && !southEast.Contains(GetCellWorld(terrain, middleX, range.yMax, resolution));

        Vector2 half = GetCellSize(terrain, resolution) * 0.5f;
        return $"{label} : 칸 x {range.xMin} ~ {range.xMax - 1}, z {range.yMin} ~ {range.yMax - 1} / " +
               $"칸 가장자리 x {first.x - half.x:F2} ~ {last.x + half.x:F2}, z {first.y - half.y:F2} ~ {last.y + half.y:F2} / " +
               $"칸 중심 {(isInside ? "모두 C3 안" : "C3 밖 칸 있음")}, C3 {(isCovered ? "빠짐없이 덮음" : "덮지 못한 칸 있음")}";
    }
}
