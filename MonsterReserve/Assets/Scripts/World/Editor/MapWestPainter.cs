using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 메뉴 13 : 서쪽 구역(A2, 침엽수림)의 지면 레이어와 바닥 식생(Terrain 디테일)을 칠한다 (이슈 #7).
// 규칙은 중앙 메뉴 4(MapDetailPainter)와 같고, 영역 구분만 MapWestScatter.GetWestZone으로 해서 나무 분포(메뉴 12)와 맞춘다.
// - 지면 : 숲은 숲 바닥, 드문드문·빈터는 풀. 경사 StoneBlendStartSlope부터 MapTreeScatter.MaxSlope까지 돌로 섞는다.
// - 바닥 식생 : 칸마다 구분에 맞는 규칙(MapDetailPainter.PaintDetailCell)으로 놓는다. 경사 MaxSlope 이상은 비운다.
// - 랜드마크 바닥(West/Landmarks 묶음마다 렌더러 범위로 구한 바닥 반지름) 안은 바닥 식생을 비운다. 지면 레이어는 규칙대로 둔다.
// A2에는 강, 호수, 등산로가 없어 물가·길 칠하기는 하지 않는다.
// A2 범위만 매번 다시 계산해 덮어쓰므로 여러 번 실행해도 결과가 같고, 다른 구역의 지면과 바닥 식생은 그대로 남는다.
// 실행 순서 : 9-3 → 11 → 12 → 13 → 10.
//   9-1(전체 초기화)을 다시 실행하면 13도 다시 실행한다. 11(지형 높이), 12(랜드마크 위치)를 다시 실행해도 13을 다시 실행한다.
public static class MapWestPainter
{
    // 랜드마크 묶음 하나의 바닥 (월드 XZ)
    private struct LandmarkFloor
    {
        public string Label;
        public Vector2 Center;
        public float Radius;
    }

    private const string WestCell = "A2";
    private const string WestRegionName = "West";
    private const string LandmarkParentName = "Landmarks";

    // 앞 메뉴들의 seed(20261109 ~ 20261127)와 겹치지 않게 둔다
    private const int Seed = 20261128;

    // MapDetailPainter의 StoneBlendStartSlope와 같은 값. 그쪽이 private이라 여기 다시 둔다 (바뀌면 함께 바꾼다)
    private const float StoneBlendStartSlope = 25f;

    // ---------- 검수 로그 기준 ----------
    // 풀 레이어 비중이 이 값보다 큰 칸을 풀밭으로 본다 (메뉴 9-3 MapGrassFill과 같은 값)
    private const float GrassWeightMin = 0.5f;
    // 풀밭인데 바닥 식생이 0인 칸의 목표 비율 (이하)
    private const float EmptyGrassRatioMax = 0.1f;
    private const float HectareArea = 10000f;
    // 순서는 MapTreeScatter.FieldZone (빈터, 드문드문, 숲)
    private static readonly string[] ZoneLabels = { "빈터", "드문드문", "숲" };

    [MenuItem("Tools/Map/13. 서쪽 지면과 바닥 식생 칠하기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        TerrainData data = terrain.terrainData;
        // 방식을 바꾸면 다른 구역의 기존 값 뜻이 달라지므로 바꾸지 않고 멈춘다 (메뉴 9-2가 개수 방식으로 맞춘다)
        if (data.detailScatterMode != DetailScatterMode.InstanceCountMode)
        {
            Debug.LogError($"[MapWestPainter] 디테일 방식이 {data.detailScatterMode}입니다. 먼저 'Tools/Map/9-2. 북쪽 데모 지면 이식'을 실행하세요. 지면과 바닥 식생을 바꾸지 않았습니다.");
            return;
        }

        List<LandmarkFloor> floors = FindLandmarkFloors(terrain);
        if (floors == null) return;

        Rect west = MapGrid.GetRangeBounds(WestCell, WestCell);
        int[][] groupPrototypeIndices = MapDetailPainter.RegisterPrototypes(data);
        RectInt alphaRange = PaintAlphamaps(terrain, west);
        RectInt detailRange = PaintDetails(terrain, west, groupPrototypeIndices, floors);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        LogLandmarkFloors(floors);
        LogAlphamaps(terrain, alphaRange);
        LogDetails(terrain, alphaRange, detailRange, floors);
        LogWriteRanges(terrain, west, alphaRange, detailRange);
    }

    // ---------- 랜드마크 바닥 ----------

    // 메뉴 12는 묶음 GameObject를 바닥 중심에 두므로 그 위치를 중심으로 쓴다.
    // 반지름은 메뉴 12와 같은 방식 : 묶음 안 오브젝트마다 (중심 ~ 렌더러 범위 수평 중심 거리 + 범위의 큰 쪽 반폭) 중 가장 큰 값
    private static List<LandmarkFloor> FindLandmarkFloors(Terrain terrain)
    {
        GameObject westRoot = terrain.gameObject.scene.GetRootGameObjects().FirstOrDefault(root => root.name == WestRegionName);
        Transform parent = westRoot != null ? westRoot.transform.Find(LandmarkParentName) : null;
        if (parent == null)
        {
            Debug.LogError("[MapWestPainter] West/Landmarks가 없습니다. 먼저 'Tools/Map/12. 서쪽 나무·바위·랜드마크·서식지 배치'를 실행하세요. 지면과 바닥 식생을 바꾸지 않았습니다.");
            return null;
        }

        var floors = new List<LandmarkFloor>();
        foreach (Transform group in parent)
        {
            Vector2 center = ToXZ(group.position);
            float radius = 0f;
            foreach (Transform item in group)
            {
                Renderer[] renderers = item.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;

                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                radius = Mathf.Max(radius, Vector2.Distance(center, ToXZ(bounds.center)) + Mathf.Max(bounds.extents.x, bounds.extents.z));
            }
            floors.Add(new LandmarkFloor { Label = group.name, Center = center, Radius = radius });
        }
        return floors;
    }

    // 디테일 칸(사각형)이 바닥 원과 조금이라도 겹치면 true. 칸 안 어디에 놓여도 바닥 안으로 들어오지 않게 한다
    private static bool IsOnLandmarkFloor(List<LandmarkFloor> floors, Vector2 cellCenter, Vector2 cellSize)
    {
        Vector2 half = cellSize * 0.5f;
        foreach (LandmarkFloor floor in floors)
        {
            var nearest = new Vector2(
                Mathf.Clamp(floor.Center.x, cellCenter.x - half.x, cellCenter.x + half.x),
                Mathf.Clamp(floor.Center.y, cellCenter.y - half.y, cellCenter.y + half.y));
            if (Vector2.Distance(nearest, floor.Center) < floor.Radius) return true;
        }
        return false;
    }

    // ---------- 칠하기 ----------

    // 지면 레이어 : 숲은 숲 바닥, 그 밖은 풀. 경사면은 돌로 섞는다 (메뉴 4와 같은 규칙). A2 범위의 모든 레이어를 새로 쓴다
    private static RectInt PaintAlphamaps(Terrain terrain, Rect west)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.alphamapResolution;
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, west, resolution);
        var weights = new float[range.height, range.width, data.alphamapLayers];

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                Vector2 world = GetCellWorld(terrain, range.x + x, range.y + z, resolution);
                int groundLayer = MapWestScatter.GetWestZone(world) == MapTreeScatter.FieldZone.Forest
                    ? MapTerrainSetup.ForestGroundLayer
                    : MapTerrainSetup.GrassLayer;
                float slope = data.GetSteepness(GetNormalized(range.x + x, resolution), GetNormalized(range.y + z, resolution));
                float stoneWeight = Mathf.InverseLerp(StoneBlendStartSlope, MapTreeScatter.MaxSlope, slope);
                weights[z, x, groundLayer] = 1f - stoneWeight;
                weights[z, x, MapTerrainSetup.StoneLayer] = stoneWeight;
            }
        }
        data.SetAlphamaps(range.x, range.y, weights);
        return range;
    }

    // 바닥 식생 : MapDetailPainter가 등록한 프로토타입 층을 A2 범위만 새로 계산해 덮어쓴다 (메뉴 4와 같은 방식)
    private static RectInt PaintDetails(Terrain terrain, Rect west, int[][] groupPrototypeIndices, List<LandmarkFloor> floors)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.detailResolution;
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, west, resolution);
        Vector2 cellSize = GetCellSize(terrain, resolution);
        MapDetailPainter.PaintContext context = MapDetailPainter.CreateContext(groupPrototypeIndices, range, Seed);

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                float slope = data.GetSteepness(GetNormalized(range.x + x, resolution), GetNormalized(range.y + z, resolution));
                if (slope >= MapTreeScatter.MaxSlope) continue;

                Vector2 world = GetCellWorld(terrain, range.x + x, range.y + z, resolution);
                if (IsOnLandmarkFloor(floors, world, cellSize)) continue;

                MapDetailPainter.PaintDetailCell(context, x, z, world, MapWestScatter.GetWestZone(world));
            }
        }

        foreach (KeyValuePair<int, int[,]> layer in context.Layers)
            data.SetDetailLayer(range.x, range.y, layer.Key, layer.Value);
        return range;
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

    private static Vector2 ToXZ(Vector3 world)
    {
        return new Vector2(world.x, world.z);
    }

    // ---------- 로그 ----------
    // 검수(구분별 레이어 비율, 바닥 식생 수와 밀도, 빈 풀밭 비율, 랜드마크 바닥 안 식생 0개, A2 밖 쓰기 없음)에 쓰는 값이라 남긴다.
    // 모두 칠한 뒤 Terrain에서 다시 읽어 잰다

    private static void LogLandmarkFloors(List<LandmarkFloor> floors)
    {
        IEnumerable<string> lines = floors.Select(floor => $"{floor.Label} : 중심 ({floor.Center.x:F0}, {floor.Center.y:F0}), 바닥 반지름 {floor.Radius:F1}m");
        Debug.Log($"[MapWestPainter] 바닥 식생을 비운 랜드마크 바닥 {floors.Count}곳\n" + string.Join("\n", lines));
    }

    private static void LogAlphamaps(Terrain terrain, RectInt range)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.alphamapResolution;
        int layerCount = data.alphamapLayers;
        float[,,] alphas = data.GetAlphamaps(range.x, range.y, range.width, range.height);
        var sums = new double[ZoneLabels.Length, layerCount];
        var counts = new int[ZoneLabels.Length];

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                int zone = (int)MapWestScatter.GetWestZone(GetCellWorld(terrain, range.x + x, range.y + z, resolution));
                counts[zone]++;
                for (int layer = 0; layer < layerCount; layer++)
                    sums[zone, layer] += alphas[z, x, layer];
            }
        }

        string[] layerNames = data.terrainLayers.Select(layer => layer.name.Replace("Layer_", "")).ToArray();
        int total = counts.Sum();
        var lines = new List<string>();
        for (int zone = 0; zone < ZoneLabels.Length; zone++)
        {
            if (counts[zone] == 0) continue;
            string ratios = string.Join(", ", Enumerable.Range(0, layerCount).Select(layer => $"{layerNames[layer]} {sums[zone, layer] * 100 / counts[zone]:F1}%"));
            lines.Add($"{ZoneLabels[zone]} (A2의 {counts[zone] * 100f / total:F1}%) : {ratios}");
        }
        Debug.Log("[MapWestPainter] 구분별 지면 레이어 비율\n" + string.Join("\n", lines));
    }

    private static void LogDetails(Terrain terrain, RectInt alphaRange, RectInt detailRange, List<LandmarkFloor> floors)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.detailResolution;
        int alphaResolution = data.alphamapResolution;
        Vector2 cellSize = GetCellSize(terrain, resolution);
        float cellArea = cellSize.x * cellSize.y;
        int[,] totals = ReadDetailTotals(data, detailRange);
        float[,,] alphas = data.GetAlphamaps(alphaRange.x, alphaRange.y, alphaRange.width, alphaRange.height);

        var zoneInstances = new long[ZoneLabels.Length];
        var zoneCells = new int[ZoneLabels.Length];
        long onFloorInstances = 0;
        int grassCells = 0;
        int emptyGrassCells = 0;
        int emptyGrassOnFloorCells = 0;

        for (int z = 0; z < detailRange.height; z++)
        {
            float v = GetNormalized(detailRange.y + z, resolution);
            for (int x = 0; x < detailRange.width; x++)
            {
                float u = GetNormalized(detailRange.x + x, resolution);
                Vector2 world = GetCellWorld(terrain, detailRange.x + x, detailRange.y + z, resolution);
                int count = totals[z, x];
                int zone = (int)MapWestScatter.GetWestZone(world);
                zoneCells[zone]++;
                zoneInstances[zone] += count;

                bool isOnFloor = IsOnLandmarkFloor(floors, world, cellSize);
                if (isOnFloor) onFloorInstances += count;

                // 빈 풀밭 검사 : 경사 MaxSlope 이상 칸은 뺀다
                if (data.GetSteepness(u, v) >= MapTreeScatter.MaxSlope) continue;

                // 두 범위의 끝이 반올림으로 칸 반 개쯤 어긋날 수 있어 알파맵 범위 안으로 자른다
                int alphaX = Mathf.Clamp(Mathf.FloorToInt(u * alphaResolution) - alphaRange.x, 0, alphaRange.width - 1);
                int alphaZ = Mathf.Clamp(Mathf.FloorToInt(v * alphaResolution) - alphaRange.y, 0, alphaRange.height - 1);
                if (alphas[alphaZ, alphaX, MapTerrainSetup.GrassLayer] <= GrassWeightMin) continue;

                grassCells++;
                if (count > 0) continue;
                emptyGrassCells++;
                if (isOnFloor) emptyGrassOnFloorCells++;
            }
        }

        long totalInstances = zoneInstances.Sum();
        float totalArea = zoneCells.Sum() * cellArea / HectareArea;
        var lines = new List<string>();
        for (int zone = 0; zone < ZoneLabels.Length; zone++)
        {
            float area = zoneCells[zone] * cellArea / HectareArea;
            float density = area > 0f ? zoneInstances[zone] / area : 0f;
            lines.Add($"{ZoneLabels[zone]} {area:F1}ha : {zoneInstances[zone]}개 (1ha당 {density:F0})");
        }
        Debug.Log($"[MapWestPainter] A2 바닥 식생 인스턴스 {totalInstances}개 ({totalArea:F1}ha, 1ha당 {totalInstances / totalArea:F0})\n" + string.Join("\n", lines));

        float emptyRatio = grassCells > 0 ? (float)emptyGrassCells / grassCells : 0f;
        Debug.Log($"[MapWestPainter] 풀 비중 > {GrassWeightMin}인데 바닥 식생 0인 칸 {emptyGrassCells}/{grassCells}칸 ({emptyRatio:P1}, 목표 {EmptyGrassRatioMax:P0} 이하, " +
                  $"{(emptyRatio <= EmptyGrassRatioMax ? "안" : "밖")}) / 경사 {MapTreeScatter.MaxSlope}도 이상 칸 제외, 이 중 랜드마크 바닥 칸 {emptyGrassOnFloorCells}");
        Debug.Log($"[MapWestPainter] 랜드마크 바닥 안 바닥 식생 {onFloorInstances}개 (0이어야 한다)");
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

    private static void LogWriteRanges(Terrain terrain, Rect west, RectInt alphaRange, RectInt detailRange)
    {
        TerrainData data = terrain.terrainData;
        Debug.Log($"[MapWestPainter] 쓴 범위 (A2 = x {west.xMin} ~ {west.xMax}, z {west.yMin} ~ {west.yMax})\n" +
                  DescribeRange(terrain, west, alphaRange, data.alphamapResolution, "알파맵") + "\n" +
                  DescribeRange(terrain, west, detailRange, data.detailResolution, "디테일"));
    }

    // 쓴 칸의 중심이 모두 A2 안인지(A2 밖 쓰기 없음), 바로 바깥 칸의 중심이 모두 A2 밖인지(A2를 빠짐없이 덮음)를 본다.
    // 칸 중심은 번호 순으로 커지므로 네 모서리 칸과 바로 바깥 네 줄의 가운데 칸만 보면 된다
    private static string DescribeRange(Terrain terrain, Rect west, RectInt range, int resolution, string label)
    {
        Vector2 first = GetCellWorld(terrain, range.xMin, range.yMin, resolution);
        Vector2 last = GetCellWorld(terrain, range.xMax - 1, range.yMax - 1, resolution);
        bool isInside = west.Contains(first) && west.Contains(last);

        int middleX = range.xMin + range.width / 2;
        int middleZ = range.yMin + range.height / 2;
        bool isCovered = !west.Contains(GetCellWorld(terrain, range.xMin - 1, middleZ, resolution))
                         && !west.Contains(GetCellWorld(terrain, range.xMax, middleZ, resolution))
                         && !west.Contains(GetCellWorld(terrain, middleX, range.yMin - 1, resolution))
                         && !west.Contains(GetCellWorld(terrain, middleX, range.yMax, resolution));

        Vector2 half = GetCellSize(terrain, resolution) * 0.5f;
        return $"{label} : 칸 x {range.xMin} ~ {range.xMax - 1}, z {range.yMin} ~ {range.yMax - 1} / " +
               $"칸 가장자리 x {first.x - half.x:F2} ~ {last.x + half.x:F2}, z {first.y - half.y:F2} ~ {last.y + half.y:F2} / " +
               $"칸 중심 {(isInside ? "모두 A2 안" : "A2 밖 칸 있음")}, A2 {(isCovered ? "빠짐없이 덮음" : "덮지 못한 칸 있음")}";
    }
}
