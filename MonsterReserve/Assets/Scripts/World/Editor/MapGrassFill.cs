using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 메뉴 9-3 : 북쪽 줄(A1~C1)에서 풀만 칠해지고 바닥 식생이 비어 있는 넓은 풀밭에 바닥 식생을 채운다 (이슈 #5).
// 메뉴 9-2는 데모 원본에 식생이 없던 자리를 풀 텍스처로만 칠해 두므로, 그 빈 곳을 메뉴 4와 같은 칸 규칙(MapDetailPainter)으로 채운다.
// 채우는 칸 : 아래 조건을 모두 만족하는 디테일 칸
//   1) 풀 레이어 알파맵 비중 > GrassWeightMin
//   2) 칸 중심에서 EmptyRadius 안의 모든 디테일 칸에 바닥 식생(모든 프로토타입 합)이 0 (채우기 전 상태로 판정)
//   3) 경사 < MapTreeScatter.MaxSlope
//   4) 물가가 아님 (메뉴 9-2 ClearShoreDetails와 같은 기준)
//   5) 등산로가 아님 (길 중심선에서 TrailReach 안이 아님, 메뉴 9-2 ClearTrailDetails와 같은 기준)
// 높이대(MapNorthScatter.GetBand)가 기슭이면 드문드문, 중턱과 정상 부근이면 개활지 규칙으로 칠한다.
// 채울 칸에만 개수를 더하고, 그 밖의 칸(이미 식생이 있는 칸, 조건에 안 맞는 칸)은 읽은 값 그대로 둔다.
// 채운 칸은 다음 실행에서 조건 2를 만족하지 않으므로 다시 실행해도 더 채우지 않는다.
// 중앙(B2)과 다른 구역은 빈 칸 판정에 읽기만 하고 쓰지 않는다.
// 실행 순서 : 9-1 → 4 → 9-2 → 9-3. 메뉴 9-2를 다시 실행하면 북쪽 줄 바닥 식생이 덮어써지므로 9-3도 다시 실행한다.
public static class MapGrassFill
{
    // 한 번 실행하는 동안 함께 쓰는 북쪽 줄 디테일 범위와 칸별 조건 값 ([z, x], Range 기준)
    private class FillContext
    {
        public Terrain Terrain;
        public TerrainData Data;
        public RectInt Range;
        public int Resolution;
        // Terrain 모서리의 월드 XZ
        public Vector2 Origin;
        public Vector2 CellSize;
        public float[,] GrassWeights;
        public bool[,] IsEmptyAround;
        public float[,] WaterDistances;
        public bool[,] IsNearTrail;
        public Rect MiddleColumn;
    }

    // 로그용 값 (검수 : 채운 넓이, 구역·높이대별 넓이, 추가한 개수, 물가·길 위 개수)
    private class FillStats
    {
        public int FilledCells;
        // 순서는 RegionLabels
        public int[] RegionCells = new int[3];
        // 순서는 MapNorthScatter.HeightBand
        public int[] BandCells = new int[3];
        public long AddedInstances;
        public long AddedOnShore;
        public long AddedOnTrail;
    }

    // ---------- 채우기 조건 (임시값) ----------
    // 이 반경 안이 모두 비어 있는 칸만 채운다
    private const float EmptyRadius = 10f;
    // 풀 레이어 비중이 이 값보다 큰 칸만 채운다
    private const float GrassWeightMin = 0.5f;
    // 메뉴 9-2(MapDemoGroundTransplant)의 TrailDirtFade와 같은 값. 그쪽이 private이라 여기 다시 둔다 (바뀌면 함께 바꾼다)
    private const float TrailDirtFade = 1.5f;
    private const float TrailReach = MapTrailBuilder.HalfWidth + TrailDirtFade;

    private const int FillSeed = 20261125;

    // ---------- 이름, 로그 ----------
    private static readonly string[] RegionLabels = { MapNorthScatter.NorthFromCell, MapNorthScatter.MiddleCell, MapNorthScatter.NorthToCell };
    // 순서는 MapNorthScatter.HeightBand와 같다
    private static readonly string[] BandLabels = { "기슭 (드문드문)", "중턱 (개활지)", "정상 부근 (개활지)" };
    private const float HectareArea = 10000f;

    [MenuItem("Tools/Map/9-3. 북쪽 빈 풀밭 채우기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        List<List<Vector2>> trails = MapTrailBuilder.GetTrails();
        if (trails == null)
        {
            Debug.LogError("[MapGrassFill] 등산로 경로가 없습니다. 먼저 'Tools/Map/7-1. 등산로 만들기'를 실행하세요.");
            return;
        }

        TerrainData data = terrain.terrainData;
        // 방식을 바꾸면 기존 값의 뜻이 달라지므로 바꾸지 않고 멈춘다 (메뉴 9-2가 개수 방식으로 맞춘다)
        if (data.detailScatterMode != DetailScatterMode.InstanceCountMode)
        {
            Debug.LogError($"[MapGrassFill] 디테일 방식이 {data.detailScatterMode}입니다. 먼저 'Tools/Map/9-2. 북쪽 데모 지면 이식'을 실행하세요. 바닥 식생을 바꾸지 않았습니다.");
            return;
        }

        // 메뉴 4, 9-2가 이미 등록한 prefab이라 번호만 다시 찾는다. 기존 값을 읽기 전에 불러 둔다
        int[][] groupPrototypeIndices = MapDetailPainter.RegisterPrototypes(data);
        FillContext context = CreateContext(terrain, trails);
        MapDetailPainter.PaintContext paint = MapDetailPainter.CreateContext(groupPrototypeIndices, context.Range, FillSeed);

        FillStats stats = PaintEmptyCells(context, paint);
        AddToTerrain(context, paint.Layers, stats);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        LogResult(context, stats);
    }

    // ---------- 준비 ----------

    // 조건 값은 모두 채우기 전 상태에서 한 번에 구한다
    private static FillContext CreateContext(Terrain terrain, List<List<Vector2>> trails)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.detailResolution;
        Rect northRow = MapGrid.GetRangeBounds(MapNorthScatter.NorthFromCell, MapNorthScatter.NorthToCell);
        Vector3 position = terrain.transform.position;
        var context = new FillContext
        {
            Terrain = terrain,
            Data = data,
            Range = MapTerrainSetup.ToIndexRange(terrain, northRow, resolution),
            Resolution = resolution,
            Origin = new Vector2(position.x, position.z),
            CellSize = new Vector2(data.size.x / resolution, data.size.z / resolution),
            MiddleColumn = MapGrid.GetRangeBounds(MapNorthScatter.MiddleCell, MapNorthScatter.MiddleCell)
        };
        context.GrassWeights = CreateGrassWeights(context, northRow);
        context.IsEmptyAround = CreateEmptyMask(context);
        context.WaterDistances = MapGroundPainter.CreateWaterDistances(MapGroundPainter.CreateWaterSource(terrain), terrain, context.Range, resolution);
        context.IsNearTrail = CreateTrailMask(context, trails);
        return context;
    }

    // 디테일 칸 중심이 든 알파맵 칸의 풀 레이어 비중
    private static float[,] CreateGrassWeights(FillContext context, Rect northRow)
    {
        TerrainData data = context.Data;
        int alphaResolution = data.alphamapResolution;
        RectInt alphaRange = MapTerrainSetup.ToIndexRange(context.Terrain, northRow, alphaResolution);
        float[,,] alphas = data.GetAlphamaps(alphaRange.x, alphaRange.y, alphaRange.width, alphaRange.height);

        var weights = new float[context.Range.height, context.Range.width];
        for (int z = 0; z < context.Range.height; z++)
        {
            // 두 범위의 끝이 반올림으로 칸 반 개쯤 어긋날 수 있어 알파맵 범위 안으로 자른다
            int alphaZ = Mathf.Clamp(Mathf.FloorToInt(GetNormalized(context, context.Range.y + z) * alphaResolution) - alphaRange.y, 0, alphaRange.height - 1);
            for (int x = 0; x < context.Range.width; x++)
            {
                int alphaX = Mathf.Clamp(Mathf.FloorToInt(GetNormalized(context, context.Range.x + x) * alphaResolution) - alphaRange.x, 0, alphaRange.width - 1);
                weights[z, x] = alphas[alphaZ, alphaX, MapTerrainSetup.GrassLayer];
            }
        }
        return weights;
    }

    // 조건 2 : 칸 중심에서 EmptyRadius 안의 칸이 모두 비었는지.
    // 북쪽 줄 가장자리 칸도 바르게 보도록 반경만큼 넓힌 범위(Terrain 안)를 읽는다. 넓힌 부분은 읽기만 한다
    private static bool[,] CreateEmptyMask(FillContext context)
    {
        bool[,] isOccupied = ReadOccupied(context, out RectInt read);
        List<Vector2Int> offsets = CreateDiskOffsets(context.CellSize);
        var isEmpty = new bool[context.Range.height, context.Range.width];
        for (int z = 0; z < context.Range.height; z++)
        {
            for (int x = 0; x < context.Range.width; x++)
            {
                var center = new Vector2Int(context.Range.x + x - read.x, context.Range.y + z - read.y);
                isEmpty[z, x] = !HasOccupiedAround(isOccupied, center, offsets);
            }
        }
        return isEmpty;
    }

    // 넓힌 범위에서 프로토타입 하나라도 개수가 있는 칸
    private static bool[,] ReadOccupied(FillContext context, out RectInt read)
    {
        int reach = Mathf.CeilToInt(EmptyRadius / Mathf.Min(context.CellSize.x, context.CellSize.y));
        int xMin = Mathf.Max(context.Range.xMin - reach, 0);
        int zMin = Mathf.Max(context.Range.yMin - reach, 0);
        int xMax = Mathf.Min(context.Range.xMax + reach, context.Resolution);
        int zMax = Mathf.Min(context.Range.yMax + reach, context.Resolution);
        read = new RectInt(xMin, zMin, xMax - xMin, zMax - zMin);

        var isOccupied = new bool[read.height, read.width];
        int prototypeCount = context.Data.detailPrototypes.Length;
        for (int i = 0; i < prototypeCount; i++)
        {
            int[,] layer = context.Data.GetDetailLayer(read.x, read.y, read.width, read.height, i);
            for (int z = 0; z < read.height; z++)
            {
                for (int x = 0; x < read.width; x++)
                {
                    if (layer[z, x] > 0) isOccupied[z, x] = true;
                }
            }
        }
        return isOccupied;
    }

    // 칸 중심 사이 거리가 EmptyRadius 이하인 칸의 상대 번호 (자기 칸 포함)
    private static List<Vector2Int> CreateDiskOffsets(Vector2 cellSize)
    {
        int reachX = Mathf.CeilToInt(EmptyRadius / cellSize.x);
        int reachZ = Mathf.CeilToInt(EmptyRadius / cellSize.y);
        var offsets = new List<Vector2Int>();
        for (int dz = -reachZ; dz <= reachZ; dz++)
        {
            for (int dx = -reachX; dx <= reachX; dx++)
            {
                if (new Vector2(dx * cellSize.x, dz * cellSize.y).magnitude <= EmptyRadius) offsets.Add(new Vector2Int(dx, dz));
            }
        }
        return offsets;
    }

    // Terrain 밖(읽은 범위 밖) 칸은 식생이 없는 것으로 본다
    private static bool HasOccupiedAround(bool[,] isOccupied, Vector2Int center, List<Vector2Int> offsets)
    {
        int height = isOccupied.GetLength(0);
        int width = isOccupied.GetLength(1);
        foreach (Vector2Int offset in offsets)
        {
            int x = center.x + offset.x;
            int z = center.y + offset.y;
            if (x < 0 || z < 0 || x >= width || z >= height) continue;
            if (isOccupied[z, x]) return true;
        }
        return false;
    }

    // 조건 5 : 길 중심선에서 TrailReach 안 (메뉴 9-2 ClearTrailDetails와 같이 거리 < TrailReach)
    private static bool[,] CreateTrailMask(FillContext context, List<List<Vector2>> trails)
    {
        var isNear = new bool[context.Range.height, context.Range.width];
        foreach (List<Vector2> points in trails)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 from = points[i];
                Vector2 to = points[i + 1];
                if (!TryGetCellRange(context, Vector2.Min(from, to) - Vector2.one * TrailReach, Vector2.Max(from, to) + Vector2.one * TrailReach, out RectInt cells)) continue;

                for (int z = cells.yMin; z <= cells.yMax; z++)
                {
                    for (int x = cells.xMin; x <= cells.xMax; x++)
                    {
                        if (GetDistanceToSegment(from, to, GetCellWorld(context, x, z)) < TrailReach) isNear[z, x] = true;
                    }
                }
            }
        }
        return isNear;
    }

    // ---------- 채우기 ----------

    // 조건을 모두 만족하는 칸만 paint의 빈 층에 칠한다 (Terrain에는 아직 쓰지 않는다)
    private static FillStats PaintEmptyCells(FillContext context, MapDetailPainter.PaintContext paint)
    {
        var stats = new FillStats();
        float baseY = context.Terrain.transform.position.y;
        for (int z = 0; z < context.Range.height; z++)
        {
            float v = GetNormalized(context, context.Range.y + z);
            for (int x = 0; x < context.Range.width; x++)
            {
                if (context.GrassWeights[z, x] <= GrassWeightMin || !context.IsEmptyAround[z, x] || context.IsNearTrail[z, x]) continue;

                float u = GetNormalized(context, context.Range.x + x);
                if (context.Data.GetSteepness(u, v) >= MapTreeScatter.MaxSlope) continue;

                Vector2 world = GetCellWorld(context, x, z);
                if (MapGroundPainter.GetShoreWeight(context.WaterDistances[z, x], world) > 0f) continue;

                MapNorthScatter.HeightBand band = MapNorthScatter.GetBand(world, context.Data.GetInterpolatedHeight(u, v) + baseY);
                MapTreeScatter.FieldZone zone = band == MapNorthScatter.HeightBand.Foothill ? MapTreeScatter.FieldZone.Sparse : MapTreeScatter.FieldZone.Open;
                MapDetailPainter.PaintDetailCell(paint, x, z, world, zone);

                stats.FilledCells++;
                stats.RegionCells[GetRegionIndex(context, world)]++;
                stats.BandCells[(int)band]++;
            }
        }
        return stats;
    }

    // 기존 층을 읽어 채운 칸에만 개수를 더해 다시 쓴다. 더할 것이 없는 프로토타입은 쓰지 않는다
    private static void AddToTerrain(FillContext context, Dictionary<int, int[,]> addedLayers, FillStats stats)
    {
        RectInt range = context.Range;
        foreach (KeyValuePair<int, int[,]> pair in addedLayers)
        {
            int[,] added = pair.Value;
            if (!HasAnyValue(added)) continue;

            int[,] layer = context.Data.GetDetailLayer(range.x, range.y, range.width, range.height, pair.Key);
            for (int z = 0; z < range.height; z++)
            {
                for (int x = 0; x < range.width; x++)
                {
                    int count = added[z, x];
                    if (count == 0) continue;

                    layer[z, x] += count;
                    stats.AddedInstances += count;
                    if (MapGroundPainter.GetShoreWeight(context.WaterDistances[z, x], GetCellWorld(context, x, z)) > 0f) stats.AddedOnShore += count;
                    if (context.IsNearTrail[z, x]) stats.AddedOnTrail += count;
                }
            }
            context.Data.SetDetailLayer(range.x, range.y, pair.Key, layer);
        }
    }

    private static bool HasAnyValue(int[,] values)
    {
        foreach (int value in values)
        {
            if (value != 0) return true;
        }
        return false;
    }

    // ---------- 격자, 계산 ----------

    // 디테일 칸 번호(Terrain 전체 기준)의 칸 중심을 0 ~ 1로
    private static float GetNormalized(FillContext context, int index)
    {
        return (index + 0.5f) / context.Resolution;
    }

    // Range 안 [z, x] 칸 중심의 월드 XZ
    private static Vector2 GetCellWorld(FillContext context, int x, int z)
    {
        return context.Origin + Vector2.Scale(new Vector2(context.Range.x + x + 0.5f, context.Range.y + z + 0.5f), context.CellSize);
    }

    // 0 : A1, 1 : B1, 2 : C1 (MapNorthScatter와 같은 구분)
    private static int GetRegionIndex(FillContext context, Vector2 world)
    {
        if (world.x < context.MiddleColumn.xMin) return 0;
        if (world.x >= context.MiddleColumn.xMax) return 2;
        return 1;
    }

    // 월드 XZ 사각형(min ~ max)에 걸친 칸의 범위 (Range 기준 번호, xMax·yMax 포함). 겹치는 칸이 없으면 false
    private static bool TryGetCellRange(FillContext context, Vector2 min, Vector2 max, out RectInt cells)
    {
        int xMin = Mathf.Max(Mathf.FloorToInt((min.x - context.Origin.x) / context.CellSize.x) - context.Range.x, 0);
        int zMin = Mathf.Max(Mathf.FloorToInt((min.y - context.Origin.y) / context.CellSize.y) - context.Range.y, 0);
        int xMax = Mathf.Min(Mathf.CeilToInt((max.x - context.Origin.x) / context.CellSize.x) - context.Range.x, context.Range.width - 1);
        int zMax = Mathf.Min(Mathf.CeilToInt((max.y - context.Origin.y) / context.CellSize.y) - context.Range.y, context.Range.height - 1);
        cells = new RectInt(xMin, zMin, xMax - xMin, zMax - zMin);
        return xMin <= xMax && zMin <= zMax;
    }

    private static float GetDistanceToSegment(Vector2 from, Vector2 to, Vector2 position)
    {
        Vector2 segment = to - from;
        if (segment.sqrMagnitude <= 0f) return Vector2.Distance(position, from);

        float t = Mathf.Clamp01(Vector2.Dot(position - from, segment) / segment.sqrMagnitude);
        return Vector2.Distance(position, from + segment * t);
    }

    // ---------- 로그 ----------
    // 검수(채운 넓이, 구역·높이대별 넓이, 추가한 개수, 물가·길 위 0개, 다시 실행하면 0칸)에 쓰는 값이라 로그를 남긴다

    private static void LogResult(FillContext context, FillStats stats)
    {
        float cellArea = context.CellSize.x * context.CellSize.y;
        Debug.Log($"[MapGrassFill] 채운 칸 {stats.FilledCells}개 ({stats.FilledCells * cellArea / HectareArea:F1}ha), 추가한 바닥 식생 {stats.AddedInstances}개 / " +
                  $"조건 : 풀 비중 > {GrassWeightMin}, 반경 {EmptyRadius}m 안 모두 빈 칸, 경사 < {MapTreeScatter.MaxSlope}도, 물가 밖, 길 중심에서 {TrailReach}m 밖 " +
                  "(다시 실행하면 0칸이어야 한다)");

        var regionLines = new List<string>();
        for (int i = 0; i < RegionLabels.Length; i++)
            regionLines.Add($"{RegionLabels[i]} : {stats.RegionCells[i]}칸, {stats.RegionCells[i] * cellArea / HectareArea:F1}ha");
        Debug.Log("[MapGrassFill] 구역별 채운 넓이\n" + string.Join("\n", regionLines));

        var bandLines = new List<string>();
        for (int i = 0; i < BandLabels.Length; i++)
            bandLines.Add($"{BandLabels[i]} : {stats.BandCells[i]}칸, {stats.BandCells[i] * cellArea / HectareArea:F1}ha");
        Debug.Log("[MapGrassFill] 높이대별 채운 넓이\n" + string.Join("\n", bandLines));

        Debug.Log($"[MapGrassFill] 물가에 추가한 바닥 식생 {stats.AddedOnShore}개, 길 위(길 중심에서 {TrailReach}m 안)에 추가한 바닥 식생 {stats.AddedOnTrail}개 (둘 다 0이어야 한다)");
    }
}
