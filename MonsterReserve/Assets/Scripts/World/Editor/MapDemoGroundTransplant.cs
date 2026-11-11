using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 메뉴 9-2 : 북쪽 줄(A1~C1) 지면 칠과 바닥 식생을 데모 지형에서 옮겨 오고, 물가와 등산로를 덧칠한다 (이슈 #5 방향 변경, 6단계).
// 1) 이식 창 안 : 데모 DemoTerrain.asset의 알파맵(지면 칠)과 디테일(바닥 식생)을 메뉴 5-1과 같은 변환(1:1, 위에서 볼 때 180도)으로 옮긴다.
//    해상도가 달라 우리 칸 하나가 덮는 데모 칸들을 겹친 넓이 비율로 섞는다 (알파맵은 가중치 합 1, 디테일은 1m²당 개수 유지).
//    데모 레이어는 같은 TerrainLayer 에셋(없으면 같은 이름, 같은 텍스처)의 우리 레이어로 옮긴다. 대응하지 않는 레이어가 있으면 칠하지 않고 멈춘다.
//    데모 디테일 프로토타입은 같은 prefab(텍스처)의 우리 프로토타입으로 옮기고, 없으면 데모 설정 그대로 우리 목록 뒤에 추가한다 (기존 번호는 그대로).
// 2) 창 밖(A1, C1의 |x| > 750) : 경사 → 돌, 나무 둘레 → 숲 바닥, 나머지 풀. 바닥 식생은 메뉴 4와 같은 칸 규칙(MapDetailPainter)이다.
//    창 가장자리에서 OutsideBlendWidth 안은 데모 칠을 거울처럼 접어 읽은 값(메뉴 5-1의 높이 접기와 같음)과 섞어 x = ±750에서 칠이 끊기지 않게 한다.
// 3) 덧칠 : 호수와 강 둘레 = 물가(MapGroundPainter 물가 규칙, 남쪽 B3 강 둘레 포함), 등산로 = 흙. 물가와 길 칸은 바닥 식생을 비운다.
//    길 흙은 중앙(B2)에도 칠하고, 물가 위에서는 물가가 이긴다 (여울의 물 속 바닥).
// 중앙(B2)은 길 칸 말고는 건드리지 않는다. 맡은 범위를 다시 계산해 덮어쓰므로 여러 번 실행해도 결과가 같다.
// 실행 순서 : 9-1 → 4 → 9-2. 메뉴 5-1 → 6 → 7 → 7-1 → 8-1 다음에 실행한다 (지형, 호수 수면, 강 경로, 등산로, 창 밖 나무를 읽는다).
// 데모 에셋은 읽기만 한다.
public static class MapDemoGroundTransplant
{
    // 칠할 격자(알파맵 또는 디테일)의 한 범위
    private struct GridArea
    {
        public RectInt Range;
        public int Resolution;
        // Terrain 모서리의 월드 XZ
        public Vector2 Origin;
        public Vector2 CellSize;
    }

    // 한 축(동서 또는 남북)에서 우리 칸 하나가 덮는 데모 칸들
    private struct AxisSpan
    {
        // 첫 데모 칸 번호 (데모 격자 전체 기준)
        public int First;
        // 데모 칸마다 우리 칸과 겹치는 길이 (m)
        public float[] Overlaps;
        // 겹친 길이 합. 데모 범위 끝에서 잘리면 우리 칸 길이보다 짧다
        public float Covered;
    }

    // 데모에서 읽은 범위 하나와 그 축별 대응
    private struct DemoSampling
    {
        public AxisSpan[] Columns;
        public AxisSpan[] Rows;
        public RectInt Read;
        public Vector2 DemoCellSize;
    }

    private class TransplantContext
    {
        public Terrain Terrain;
        public TerrainData Demo;
        public Rect NorthRow;
        public Rect Window;
        // 데모 좌표 (0, 0)이 놓이는 우리 월드 XZ. 180도 회전이라 우리 = Pivot - 데모
        public Vector2 DemoPivot;
        public MapGroundPainter.WaterSource Water;
        public List<List<Vector2>> Trails;
        public List<Vector2> Trees;
        // 데모 레이어 번호 → 우리 레이어 번호
        public int[] LayerMap;
        // 데모 프로토타입 번호 → 우리 프로토타입 번호 (-1은 원본이 없어 옮기지 않음)
        public int[] PrototypeMap;
        public int[][] GroupPrototypeIndices;
        public List<string> LayerLines = new List<string>();
        public List<string> PrototypeLines = new List<string>();
    }

    // 로그용 값 (검수 : 데모 원본과 옮긴 결과 비교)
    private class TransplantStats
    {
        // 우리 레이어 순서. 데모 창 안 원본 평균(우리 레이어로 모음)과 옮긴 직후(물가, 길 덧칠 전) 창 안 평균
        public double[] DemoWindowAlpha;
        public double[] WindowBaseAlpha;
        // 우리 프로토타입 순서. 데모 창 안 원본 인스턴스 수(우리 프로토타입으로 모음)와 옮긴 직후(물가 비우기 전) 창 안 수
        public long[] DemoWindowDetails;
        public long[] WindowResampledDetails;
        // 원본이 없어 옮기지 못한 데모 창 안 인스턴스 수
        public long DroppedDetails;
        public int TrailAlphaCells;
    }

    // ---------- 창 밖 (임시값) ----------
    // 창 가장자리 밖에서 데모 칠(거울 접기)이 규칙 칠로 바뀌는 폭
    private const float OutsideBlendWidth = 60f;
    // 경사가 이 범위에서 풀(숲 바닥)에서 돌로 바뀐다
    private const float OutsideStoneStartSlope = 30f;
    private const float OutsideStoneFullSlope = 40f;
    // 나무 둘레 숲 바닥 : 줄기에서 TreeGroundRadius 안은 숲 바닥, 그 밖 TreeGroundFade만큼 풀과 섞는다
    private const float TreeGroundRadius = 3f;
    private const float TreeGroundFade = 4f;
    // 돌이 이 비율 이상인 칸에는 바닥 식생을 두지 않는다 (메뉴 9와 같은 값)
    private const float DetailStoneLimit = 0.5f;
    private const string TreeParentName = "Trees";

    // ---------- 등산로 (임시값) ----------
    // 길 폭(MapTrailBuilder.HalfWidth × 2) 밖으로 이만큼 흙과 원래 지면을 섞는다. 이 안의 칸은 바닥 식생을 비운다
    private const float TrailDirtFade = 1.5f;
    private const float TrailReach = MapTrailBuilder.HalfWidth + TrailDirtFade;
    // 길 범위를 읽을 때 더 두는 여유 (알파맵 칸 약 2.44m보다 넓게)
    private const float TrailBoundsPadding = 5f;

    // ---------- 디테일 ----------
    // 디테일 칸 하나에 넣을 수 있는 최대 개수 (InstanceCountMode)
    private const int MaxDetailCount = 255;
    private const int DemoDetailSeed = 20261122;
    private const int BlendSeed = 20261123;
    private const int RuleDetailSeed = 20261124;

    // ---------- 이름, 로그 ----------
    private const string NorthFromCell = "A1";
    private const string NorthToCell = "C1";
    private const string SouthCell = "B3";
    private static readonly string[] LogCellNames = { "A1", "B1", "C1", "A2", "B2", "C2", "A3", "B3", "C3" };
    // 알파맵 비중이 이 값 이상이면 그 레이어만 칠한 칸으로 본다 (float 오차)
    private const float FullWeight = 0.999f;
    private const float HectareArea = 10000f;

    [MenuItem("Tools/Map/9-2. 북쪽 데모 지면 이식")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        TransplantContext context = CreateContext(terrain);
        if (context == null) return;

        TerrainData data = terrain.terrainData;
        if (data.detailScatterMode != DetailScatterMode.InstanceCountMode)
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);

        var stats = new TransplantStats();
        PaintNorthAlphamaps(context, stats);
        PaintNorthDetails(context, stats);

        // 남쪽 B3는 구역 작업 전이라 강 둘레만 칠한다 (메뉴 9가 하던 것과 같은 함수)
        Rect south = MapGrid.GetRangeBounds(SouthCell, SouthCell);
        MapGroundPainter.PaintSouthShoreAlphamaps(terrain, context.Water, south);
        MapGroundPainter.ClearSouthShoreDetails(terrain, context.Water, south);

        PaintTrails(context, stats);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        LogResult(context, stats);
    }

    // ---------- 준비 ----------

    // 칠하기 전에 멈출 조건을 모두 본다 (레이어 대응 실패, 등산로 없음, 데모 디테일 방식 다름). 멈추면 null
    private static TransplantContext CreateContext(Terrain terrain)
    {
        List<List<Vector2>> trails = MapTrailBuilder.GetTrails();
        if (trails == null)
        {
            Debug.LogError("[MapDemoGroundTransplant] 등산로 경로가 없습니다. 먼저 'Tools/Map/7-1. 등산로 만들기'를 실행하세요.");
            return null;
        }

        TerrainData demo = AssetDatabase.LoadAssetAtPath<TerrainData>(MapDemoTransplant.DemoTerrainDataPath);
        // 데모가 개수가 아닌 덮개(Coverage) 방식이면 1m²당 개수로 바꾸는 규칙이 정해지지 않아 멈춘다
        if (demo.detailScatterMode != DetailScatterMode.InstanceCountMode)
        {
            Debug.LogError($"[MapDemoGroundTransplant] 데모 디테일 방식이 {demo.detailScatterMode}라 개수로 옮길 수 없습니다. 지면을 바꾸지 않았습니다.");
            return null;
        }

        TerrainData data = terrain.terrainData;
        var context = new TransplantContext { Terrain = terrain, Demo = demo };
        context.LayerMap = MapLayers(data, demo, context.LayerLines);
        if (context.LayerMap == null)
        {
            Debug.LogError("[MapDemoGroundTransplant] 우리 레이어와 대응하지 않는 데모 레이어가 있어 멈췄습니다. 지면을 바꾸지 않았습니다.\n" + string.Join("\n", context.LayerLines));
            return null;
        }

        // 우리 프로토타입(메뉴 4)을 먼저 등록해 번호를 고정한 뒤 데모 것을 뒤에 더한다
        context.GroupPrototypeIndices = MapDetailPainter.RegisterPrototypes(data);
        context.PrototypeMap = MapPrototypes(data, demo, context.PrototypeLines);

        context.NorthRow = MapGrid.GetRangeBounds(NorthFromCell, NorthToCell);
        context.Window = MapDemoTransplant.GetGameWindowRect();
        // 데모 Terrain 위치가 원점이라 데모 월드 좌표 = 데모 Terrain 기준 좌표다
        Vector3 pivot = MapDemoTransplant.DemoToGame(Vector3.zero);
        context.DemoPivot = new Vector2(pivot.x, pivot.z);
        context.Water = MapGroundPainter.CreateWaterSource(terrain);
        context.Trails = trails;
        context.Trees = FindTrees(terrain);
        return context;
    }

    // 같은 에셋 → 같은 이름 → 같은 바탕 텍스처 순서로 찾는다. 하나라도 못 찾으면 null
    private static int[] MapLayers(TerrainData data, TerrainData demo, List<string> lines)
    {
        TerrainLayer[] ours = data.terrainLayers;
        TerrainLayer[] demoLayers = demo.terrainLayers;
        var map = new int[demoLayers.Length];
        bool isAllMapped = true;
        for (int i = 0; i < demoLayers.Length; i++)
        {
            TerrainLayer layer = demoLayers[i];
            string method = "같은 에셋";
            int index = System.Array.IndexOf(ours, layer);
            if (index < 0 && layer != null)
            {
                method = "같은 이름";
                index = System.Array.FindIndex(ours, candidate => candidate.name == layer.name);
            }
            if (index < 0 && layer != null && layer.diffuseTexture != null)
            {
                method = "같은 텍스처";
                index = System.Array.FindIndex(ours, candidate => candidate.diffuseTexture == layer.diffuseTexture);
            }

            map[i] = index;
            string demoName = layer != null ? layer.name : "(없음)";
            if (index < 0)
            {
                isAllMapped = false;
                lines.Add($"  데모 {i} {demoName} → 대응 없음");
                continue;
            }
            lines.Add($"  데모 {i} {demoName} → 우리 {index} {ours[index].name} ({method})");
        }
        return isAllMapped ? map : null;
    }

    // 원본(prefab 또는 텍스처)이 같은 우리 프로토타입을 쓰고, 없으면 데모 설정 그대로 뒤에 추가한다.
    // 다시 실행하면 추가한 것도 원본으로 찾으므로 번호가 늘지 않는다
    private static int[] MapPrototypes(TerrainData data, TerrainData demo, List<string> lines)
    {
        var prototypes = new List<DetailPrototype>(data.detailPrototypes);
        DetailPrototype[] demoPrototypes = demo.detailPrototypes;
        var map = new int[demoPrototypes.Length];
        for (int i = 0; i < demoPrototypes.Length; i++)
        {
            DetailPrototype demoPrototype = demoPrototypes[i];
            if (!HasSource(demoPrototype))
            {
                map[i] = -1;
                lines.Add($"  데모 {i} (원본 없음) → 옮기지 않음");
                continue;
            }

            string method = "같은 원본";
            int index = prototypes.FindIndex(candidate => IsSameSource(candidate, demoPrototype));
            if (index < 0)
            {
                method = "추가 (데모 설정 그대로)";
                prototypes.Add(new DetailPrototype(demoPrototype));
                index = prototypes.Count - 1;
            }
            map[i] = index;
            DetailPrototype ours = prototypes[index];
            lines.Add($"  데모 {i} {GetSourceName(demoPrototype)} → 우리 {index} ({method}) / 크기 데모 폭 {demoPrototype.minWidth:F2} ~ {demoPrototype.maxWidth:F2}, " +
                      $"높이 {demoPrototype.minHeight:F2} ~ {demoPrototype.maxHeight:F2} · 우리 폭 {ours.minWidth:F2} ~ {ours.maxWidth:F2}, 높이 {ours.minHeight:F2} ~ {ours.maxHeight:F2}");
        }
        data.detailPrototypes = prototypes.ToArray();
        return map;
    }

    private static bool HasSource(DetailPrototype prototype)
    {
        return prototype.usePrototypeMesh ? prototype.prototype != null : prototype.prototypeTexture != null;
    }

    private static bool IsSameSource(DetailPrototype a, DetailPrototype b)
    {
        if (a.usePrototypeMesh != b.usePrototypeMesh) return false;
        return a.usePrototypeMesh ? a.prototype == b.prototype : a.prototypeTexture == b.prototypeTexture;
    }

    private static string GetSourceName(DetailPrototype prototype)
    {
        return prototype.usePrototypeMesh ? prototype.prototype.name : prototype.prototypeTexture.name + " (텍스처)";
    }

    // 메뉴 8-1이 북쪽 줄 구역의 Trees 아래에 놓은 나무 줄기 위치 (창 밖 숲 바닥에 쓴다)
    private static List<Vector2> FindTrees(Terrain terrain)
    {
        var trees = new List<Vector2>();
        GameObject[] roots = terrain.gameObject.scene.GetRootGameObjects();
        foreach (string regionName in MapNorthScatter.RegionNames)
        {
            GameObject root = roots.FirstOrDefault(candidate => candidate.name == regionName);
            Transform parent = root != null ? root.transform.Find(TreeParentName) : null;
            if (parent == null) continue;

            foreach (Transform child in parent)
                trees.Add(new Vector2(child.position.x, child.position.z));
        }
        return trees;
    }

    // ---------- 데모 좌표 대응 ----------

    // 창 밖 동서는 창 가장자리에서 거울처럼 접는다 (메뉴 5-1의 높이 접기와 같아 접은 칠이 접은 지형과 맞는다)
    private static float MirrorX(TransplantContext context, float worldX)
    {
        float local = worldX - context.Window.xMin;
        if (local < 0f) local = -local;
        else if (local > context.Window.width) local = context.Window.width * 2f - local;
        return context.Window.xMin + local;
    }

    private static DemoSampling CreateSampling(TransplantContext context, GridArea area, int demoResolution)
    {
        var demoCell = new Vector2(context.Demo.size.x / demoResolution, context.Demo.size.z / demoResolution);
        var columns = new AxisSpan[area.Range.width];
        for (int x = 0; x < columns.Length; x++)
        {
            float world = area.Origin.x + (area.Range.x + x + 0.5f) * area.CellSize.x;
            columns[x] = CreateSpan(context.DemoPivot.x - MirrorX(context, world), area.CellSize.x, demoCell.x, demoResolution);
        }
        var rows = new AxisSpan[area.Range.height];
        for (int z = 0; z < rows.Length; z++)
        {
            float world = area.Origin.y + (area.Range.y + z + 0.5f) * area.CellSize.y;
            rows[z] = CreateSpan(context.DemoPivot.y - world, area.CellSize.y, demoCell.y, demoResolution);
        }
        return new DemoSampling { Columns = columns, Rows = rows, Read = GetReadRange(columns, rows), DemoCellSize = demoCell };
    }

    // 데모 좌표 demoCenter를 가운데로 길이 length인 구간이 데모 칸마다 겹치는 길이
    private static AxisSpan CreateSpan(float demoCenter, float length, float demoCell, int demoResolution)
    {
        float extent = demoCell * demoResolution;
        float min = Mathf.Clamp(demoCenter - length * 0.5f, 0f, extent);
        float max = Mathf.Clamp(demoCenter + length * 0.5f, 0f, extent);
        int first = Mathf.Min(Mathf.FloorToInt(min / demoCell), demoResolution - 1);
        int last = Mathf.Clamp(Mathf.CeilToInt(max / demoCell) - 1, first, demoResolution - 1);
        var overlaps = new float[last - first + 1];
        for (int k = 0; k < overlaps.Length; k++)
        {
            float cellMin = (first + k) * demoCell;
            overlaps[k] = Mathf.Max(0f, Mathf.Min(max, cellMin + demoCell) - Mathf.Max(min, cellMin));
        }
        return new AxisSpan { First = first, Overlaps = overlaps, Covered = max - min };
    }

    private static RectInt GetReadRange(AxisSpan[] columns, AxisSpan[] rows)
    {
        int xMin = columns.Min(span => span.First);
        int xMax = columns.Max(span => span.First + span.Overlaps.Length);
        int zMin = rows.Min(span => span.First);
        int zMax = rows.Max(span => span.First + span.Overlaps.Length);
        return new RectInt(xMin, zMin, xMax - xMin, zMax - zMin);
    }

    // 데모 창을 데모 좌표로 (x = 데모 x, y = 데모 z)
    private static Rect GetDemoWindow(TransplantContext context)
    {
        return Rect.MinMaxRect(
            context.DemoPivot.x - context.Window.xMax, context.DemoPivot.y - context.Window.yMax,
            context.DemoPivot.x - context.Window.xMin, context.DemoPivot.y - context.Window.yMin);
    }

    // 창 안 0, 창 밖으로 OutsideBlendWidth만큼 가면 1 (규칙 칠 비중). 열마다 같다
    private static float[] CreateBlends(TransplantContext context, GridArea area)
    {
        var blends = new float[area.Range.width];
        for (int x = 0; x < blends.Length; x++)
        {
            float world = area.Origin.x + (area.Range.x + x + 0.5f) * area.CellSize.x;
            float outside = Mathf.Max(context.Window.xMin - world, world - context.Window.xMax);
            blends[x] = outside <= 0f ? 0f : Mathf.SmoothStep(0f, 1f, outside / OutsideBlendWidth);
        }
        return blends;
    }

    // ---------- 북쪽 줄 지면 칠 ----------

    private static void PaintNorthAlphamaps(TransplantContext context, TransplantStats stats)
    {
        TerrainData data = context.Terrain.terrainData;
        int layerCount = data.alphamapLayers;
        GridArea area = CreateArea(context.Terrain, context.NorthRow, data.alphamapResolution);
        DemoSampling sampling = CreateSampling(context, area, context.Demo.alphamapResolution);
        float[,,] demoAlphas = context.Demo.GetAlphamaps(sampling.Read.x, sampling.Read.y, sampling.Read.width, sampling.Read.height);
        stats.DemoWindowAlpha = SumDemoWindowAlpha(context, sampling, demoAlphas, layerCount);

        float[] blends = CreateBlends(context, area);
        float[,] forest = CreateForestGround(context, area);
        float[,] waterDistances = MapGroundPainter.CreateWaterDistances(context.Water, context.Terrain, area.Range, area.Resolution);
        var weights = new float[area.Range.height, area.Range.width, layerCount];
        var demoWeights = new float[layerCount];
        var ruleWeights = new float[layerCount];
        stats.WindowBaseAlpha = new double[layerCount];
        long windowCount = 0;

        for (int z = 0; z < area.Range.height; z++)
        {
            for (int x = 0; x < area.Range.width; x++)
            {
                float blend = blends[x];
                if (blend < 1f) SampleDemoAlpha(context, sampling, demoAlphas, x, z, demoWeights);
                if (blend > 0f) GetRuleWeights(GetSlope(data, area, x, z), forest[z, x], ruleWeights);
                for (int layer = 0; layer < layerCount; layer++)
                    weights[z, x, layer] = Mathf.Lerp(demoWeights[layer], ruleWeights[layer], blend);

                if (blend <= 0f)
                {
                    windowCount++;
                    for (int layer = 0; layer < layerCount; layer++)
                        stats.WindowBaseAlpha[layer] += weights[z, x, layer];
                }
                MapGroundPainter.BlendShore(weights, z, x, MapGroundPainter.GetShoreWeight(waterDistances[z, x], GetCellWorld(area, x, z)));
            }
        }
        for (int layer = 0; layer < layerCount; layer++)
            stats.WindowBaseAlpha[layer] /= windowCount;
        data.SetAlphamaps(area.Range.x, area.Range.y, weights);
    }

    // 우리 칸이 덮는 데모 칸들을 겹친 넓이 비율로 섞어 우리 레이어로 모은다. 합을 1로 다시 맞춘다
    private static void SampleDemoAlpha(TransplantContext context, DemoSampling sampling, float[,,] demoAlphas, int x, int z, float[] result)
    {
        System.Array.Clear(result, 0, result.Length);
        AxisSpan column = sampling.Columns[x];
        AxisSpan row = sampling.Rows[z];
        float sum = 0f;
        for (int j = 0; j < row.Overlaps.Length; j++)
        {
            int demoZ = row.First + j - sampling.Read.y;
            float rowWeight = row.Overlaps[j] / row.Covered;
            for (int i = 0; i < column.Overlaps.Length; i++)
            {
                int demoX = column.First + i - sampling.Read.x;
                float weight = rowWeight * column.Overlaps[i] / column.Covered;
                for (int layer = 0; layer < context.LayerMap.Length; layer++)
                {
                    float value = weight * demoAlphas[demoZ, demoX, layer];
                    result[context.LayerMap[layer]] += value;
                    sum += value;
                }
            }
        }
        if (sum <= 0f) return;

        for (int layer = 0; layer < result.Length; layer++)
            result[layer] /= sum;
    }

    // 창 밖 규칙 : 경사 → 돌, 나무 둘레 → 숲 바닥, 나머지 풀 (합 1)
    private static void GetRuleWeights(float slope, float forest, float[] result)
    {
        System.Array.Clear(result, 0, result.Length);
        float stone = Mathf.InverseLerp(OutsideStoneStartSlope, OutsideStoneFullSlope, slope);
        result[MapTerrainSetup.StoneLayer] = stone;
        result[MapTerrainSetup.ForestGroundLayer] = forest * (1f - stone);
        result[MapTerrainSetup.GrassLayer] = (1f - forest) * (1f - stone);
    }

    // 나무 줄기 둘레의 숲 바닥 비중. 창 밖 칸에만 쓰므로 그 칸에 닿지 않는 창 안쪽 나무는 건너뛴다
    private static float[,] CreateForestGround(TransplantContext context, GridArea area)
    {
        var forest = new float[area.Range.height, area.Range.width];
        float reach = TreeGroundRadius + TreeGroundFade;
        foreach (Vector2 tree in context.Trees)
        {
            if (tree.x > context.Window.xMin + reach && tree.x < context.Window.xMax - reach) continue;
            if (!TryGetCellRange(area, tree - Vector2.one * reach, tree + Vector2.one * reach, out RectInt cells)) continue;

            for (int z = cells.yMin; z <= cells.yMax; z++)
            {
                for (int x = cells.xMin; x <= cells.xMax; x++)
                {
                    float distance = Vector2.Distance(GetCellWorld(area, x, z), tree);
                    float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TreeGroundRadius, reach, distance));
                    forest[z, x] = Mathf.Max(forest[z, x], weight);
                }
            }
        }
        return forest;
    }

    private static double[] SumDemoWindowAlpha(TransplantContext context, DemoSampling sampling, float[,,] demoAlphas, int layerCount)
    {
        Rect demoWindow = GetDemoWindow(context);
        var sums = new double[layerCount];
        long count = 0;
        for (int z = 0; z < sampling.Read.height; z++)
        {
            for (int x = 0; x < sampling.Read.width; x++)
            {
                var center = new Vector2((sampling.Read.x + x + 0.5f) * sampling.DemoCellSize.x, (sampling.Read.y + z + 0.5f) * sampling.DemoCellSize.y);
                if (!demoWindow.Contains(center)) continue;

                count++;
                for (int layer = 0; layer < context.LayerMap.Length; layer++)
                    sums[context.LayerMap[layer]] += demoAlphas[z, x, layer];
            }
        }
        for (int layer = 0; layer < layerCount; layer++)
            sums[layer] /= count;
        return sums;
    }

    // ---------- 북쪽 줄 바닥 식생 ----------

    private static void PaintNorthDetails(TransplantContext context, TransplantStats stats)
    {
        TerrainData data = context.Terrain.terrainData;
        GridArea area = CreateArea(context.Terrain, context.NorthRow, data.detailResolution);
        DemoSampling sampling = CreateSampling(context, area, context.Demo.detailResolution);
        float[] blends = CreateBlends(context, area);
        bool[,] isDemoCells = CreateDemoChoices(blends, area.Range.height);

        MapDetailPainter.PaintContext paint = MapDetailPainter.CreateContext(context.GroupPrototypeIndices, area.Range, RuleDetailSeed);
        int prototypeCount = data.detailPrototypes.Length;
        // 모든 프로토타입을 덮어써 이전 실행이나 옛 칠의 값이 북쪽 줄에 남지 않게 한다
        for (int i = 0; i < prototypeCount; i++)
        {
            if (!paint.Layers.ContainsKey(i)) paint.Layers[i] = new int[area.Range.height, area.Range.width];
        }

        stats.DemoWindowDetails = new long[prototypeCount];
        PaintDemoDetails(context, area, sampling, isDemoCells, paint.Layers, stats);
        PaintRuleDetails(context, area, isDemoCells, paint);
        stats.WindowResampledDetails = SumWindowDetails(context, area, paint.Layers, prototypeCount);
        ClearShoreDetails(context, area, paint.Layers);

        foreach (KeyValuePair<int, int[,]> layer in paint.Layers)
            data.SetDetailLayer(area.Range.x, area.Range.y, layer.Key, layer.Value);
    }

    // 창 안 칸은 모두 데모, 섞는 띠는 규칙 비중만큼 확률로 규칙 칸, 그 밖은 모두 규칙 칸
    private static bool[,] CreateDemoChoices(float[] blends, int height)
    {
        var random = new System.Random(BlendSeed);
        var choices = new bool[height, blends.Length];
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < blends.Length; x++)
            {
                float blend = blends[x];
                choices[z, x] = blend <= 0f || (blend < 1f && random.NextDouble() >= blend);
            }
        }
        return choices;
    }

    // 데모 칸 개수를 1m²당 밀도로 바꿔 우리 칸 넓이만큼 옮긴다. 소수 부분은 그 확률로 하나를 더한다 (평균 밀도 유지)
    private static void PaintDemoDetails(TransplantContext context, GridArea area, DemoSampling sampling, bool[,] isDemoCells, Dictionary<int, int[,]> layers, TransplantStats stats)
    {
        Rect demoWindow = GetDemoWindow(context);
        float areaRatio = area.CellSize.x * area.CellSize.y / (sampling.DemoCellSize.x * sampling.DemoCellSize.y);
        var random = new System.Random(DemoDetailSeed);
        for (int prototype = 0; prototype < context.PrototypeMap.Length; prototype++)
        {
            int[,] demoLayer = context.Demo.GetDetailLayer(sampling.Read.x, sampling.Read.y, sampling.Read.width, sampling.Read.height, prototype);
            long demoWindowCount = SumDemoWindowDetail(sampling, demoLayer, demoWindow);
            int target = context.PrototypeMap[prototype];
            if (target < 0)
            {
                stats.DroppedDetails += demoWindowCount;
                continue;
            }

            stats.DemoWindowDetails[target] += demoWindowCount;
            int[,] layer = layers[target];
            for (int z = 0; z < area.Range.height; z++)
            {
                for (int x = 0; x < area.Range.width; x++)
                {
                    if (!isDemoCells[z, x]) continue;

                    float expected = SampleDemoCount(sampling, demoLayer, x, z) * areaRatio;
                    if (expected <= 0f) continue;

                    int count = Mathf.FloorToInt(expected);
                    if (random.NextDouble() < expected - count) count++;
                    layer[z, x] = Mathf.Min(layer[z, x] + count, MaxDetailCount);
                }
            }
        }
    }

    // 우리 칸이 덮는 데모 칸 개수의 넓이 비율 평균 (데모 칸 하나 넓이 기준)
    private static float SampleDemoCount(DemoSampling sampling, int[,] demoLayer, int x, int z)
    {
        AxisSpan column = sampling.Columns[x];
        AxisSpan row = sampling.Rows[z];
        float sum = 0f;
        for (int j = 0; j < row.Overlaps.Length; j++)
        {
            int demoZ = row.First + j - sampling.Read.y;
            float rowWeight = row.Overlaps[j] / row.Covered;
            for (int i = 0; i < column.Overlaps.Length; i++)
            {
                int count = demoLayer[demoZ, column.First + i - sampling.Read.x];
                if (count > 0) sum += count * rowWeight * column.Overlaps[i] / column.Covered;
            }
        }
        return sum;
    }

    private static long SumDemoWindowDetail(DemoSampling sampling, int[,] demoLayer, Rect demoWindow)
    {
        long sum = 0;
        for (int z = 0; z < sampling.Read.height; z++)
        {
            for (int x = 0; x < sampling.Read.width; x++)
            {
                if (demoLayer[z, x] == 0) continue;

                var center = new Vector2((sampling.Read.x + x + 0.5f) * sampling.DemoCellSize.x, (sampling.Read.y + z + 0.5f) * sampling.DemoCellSize.y);
                if (demoWindow.Contains(center)) sum += demoLayer[z, x];
            }
        }
        return sum;
    }

    // 창 밖 규칙 칸 : 숲 바닥 → 숲, 풀 → 기슭은 드문드문·중턱은 개활지 (메뉴 9와 같은 대응). 가파른 곳, 돌, 정상 부근은 비운다
    private static void PaintRuleDetails(TransplantContext context, GridArea area, bool[,] isDemoCells, MapDetailPainter.PaintContext paint)
    {
        TerrainData data = context.Terrain.terrainData;
        float[,] forest = CreateForestGround(context, area);
        var ground = new float[data.alphamapLayers];
        for (int z = 0; z < area.Range.height; z++)
        {
            for (int x = 0; x < area.Range.width; x++)
            {
                if (isDemoCells[z, x]) continue;

                float slope = GetSlope(data, area, x, z);
                if (slope >= MapTreeScatter.MaxSlope) continue;

                GetRuleWeights(slope, forest[z, x], ground);
                if (ground[MapTerrainSetup.StoneLayer] >= DetailStoneLimit) continue;

                Vector2 world = GetCellWorld(area, x, z);
                MapNorthScatter.HeightBand band = MapNorthScatter.GetBand(world, GetHeight(context.Terrain, area, x, z));
                if (band == MapNorthScatter.HeightBand.Summit) continue;

                MapTreeScatter.FieldZone zone = ground[MapTerrainSetup.ForestGroundLayer] >= ground[MapTerrainSetup.GrassLayer]
                    ? MapTreeScatter.FieldZone.Forest
                    : band == MapNorthScatter.HeightBand.Foothill ? MapTreeScatter.FieldZone.Sparse : MapTreeScatter.FieldZone.Open;
                MapDetailPainter.PaintDetailCell(paint, x, z, world, zone);
            }
        }
    }

    // 물가(물 속 포함) 칸은 바닥 식생을 비운다 (물가 칠과 같은 범위)
    private static void ClearShoreDetails(TransplantContext context, GridArea area, Dictionary<int, int[,]> layers)
    {
        float[,] waterDistances = MapGroundPainter.CreateWaterDistances(context.Water, context.Terrain, area.Range, area.Resolution);
        for (int z = 0; z < area.Range.height; z++)
        {
            for (int x = 0; x < area.Range.width; x++)
            {
                if (MapGroundPainter.GetShoreWeight(waterDistances[z, x], GetCellWorld(area, x, z)) <= 0f) continue;

                foreach (int[,] layer in layers.Values)
                    layer[z, x] = 0;
            }
        }
    }

    private static long[] SumWindowDetails(TransplantContext context, GridArea area, Dictionary<int, int[,]> layers, int prototypeCount)
    {
        var sums = new long[prototypeCount];
        foreach (KeyValuePair<int, int[,]> layer in layers)
        {
            for (int z = 0; z < area.Range.height; z++)
            {
                for (int x = 0; x < area.Range.width; x++)
                {
                    if (layer.Value[z, x] > 0 && context.Window.Contains(GetCellWorld(area, x, z))) sums[layer.Key] += layer.Value[z, x];
                }
            }
        }
        return sums;
    }

    // ---------- 등산로 ----------

    // 길이 지나는 범위(북쪽 줄, 중앙)만 읽어 길 칸을 흙으로 섞고 바닥 식생을 비운다. 길 밖 칸은 그대로 둔다
    private static void PaintTrails(TransplantContext context, TransplantStats stats)
    {
        Rect bounds = GetTrailBounds(context.Trails);
        PaintTrailAlphamaps(context, bounds, stats);
        ClearTrailDetails(context, bounds);
    }

    // 물가 위에서는 물가가 이긴다 : 흙 비중을 (1 - 물가 비중)으로 줄인다 (여울의 물 속 바닥, 호숫가 길)
    private static void PaintTrailAlphamaps(TransplantContext context, Rect bounds, TransplantStats stats)
    {
        TerrainData data = context.Terrain.terrainData;
        GridArea area = CreateArea(context.Terrain, bounds, data.alphamapResolution);
        float[,,] weights = data.GetAlphamaps(area.Range.x, area.Range.y, area.Range.width, area.Range.height);
        float[,] waterDistances = MapGroundPainter.CreateWaterDistances(context.Water, context.Terrain, area.Range, area.Resolution);
        float[,] trailDistances = CreateTrailDistances(context.Trails, area);

        for (int z = 0; z < area.Range.height; z++)
        {
            for (int x = 0; x < area.Range.width; x++)
            {
                float dirt = GetTrailDirtWeight(trailDistances[z, x]);
                if (dirt <= 0f) continue;

                stats.TrailAlphaCells++;
                dirt *= 1f - MapGroundPainter.GetShoreWeight(waterDistances[z, x], GetCellWorld(area, x, z));
                for (int layer = 0; layer < data.alphamapLayers; layer++)
                    weights[z, x, layer] *= 1f - dirt;
                weights[z, x, MapTerrainSetup.DirtLayer] += dirt;
            }
        }
        data.SetAlphamaps(area.Range.x, area.Range.y, weights);
    }

    private static void ClearTrailDetails(TransplantContext context, Rect bounds)
    {
        TerrainData data = context.Terrain.terrainData;
        GridArea area = CreateArea(context.Terrain, bounds, data.detailResolution);
        float[,] trailDistances = CreateTrailDistances(context.Trails, area);
        int prototypeCount = data.detailPrototypes.Length;
        for (int i = 0; i < prototypeCount; i++)
        {
            int[,] layer = data.GetDetailLayer(area.Range.x, area.Range.y, area.Range.width, area.Range.height, i);
            for (int z = 0; z < area.Range.height; z++)
            {
                for (int x = 0; x < area.Range.width; x++)
                {
                    if (trailDistances[z, x] < TrailReach) layer[z, x] = 0;
                }
            }
            data.SetDetailLayer(area.Range.x, area.Range.y, i, layer);
        }
    }

    // 길 폭 안은 1, 그 밖 TrailDirtFade에 걸쳐 0으로 줄어든다
    private static float GetTrailDirtWeight(float trailDistance)
    {
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MapTrailBuilder.HalfWidth, TrailReach, trailDistance));
    }

    private static Rect GetTrailBounds(List<List<Vector2>> trails)
    {
        Vector2 min = Vector2.positiveInfinity;
        Vector2 max = Vector2.negativeInfinity;
        foreach (Vector2 point in trails.SelectMany(points => points))
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        // 칸 하나 이상 더 넓혀 길 가장자리 칸이 범위 끝에서 잘리지 않게 한다
        Vector2 margin = Vector2.one * (TrailReach + TrailBoundsPadding);
        return Rect.MinMaxRect(min.x - margin.x, min.y - margin.y, max.x + margin.x, max.y + margin.y);
    }

    // area 칸마다 가장 가까운 길 중심선까지의 거리. TrailReach보다 먼 곳은 그 값으로 둔다
    private static float[,] CreateTrailDistances(List<List<Vector2>> trails, GridArea area)
    {
        var distances = new float[area.Range.height, area.Range.width];
        for (int z = 0; z < area.Range.height; z++)
        {
            for (int x = 0; x < area.Range.width; x++)
                distances[z, x] = TrailReach;
        }

        foreach (List<Vector2> points in trails)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 from = points[i];
                Vector2 to = points[i + 1];
                if (!TryGetCellRange(area, Vector2.Min(from, to) - Vector2.one * TrailReach, Vector2.Max(from, to) + Vector2.one * TrailReach, out RectInt cells)) continue;

                for (int z = cells.yMin; z <= cells.yMax; z++)
                {
                    for (int x = cells.xMin; x <= cells.xMax; x++)
                        distances[z, x] = Mathf.Min(distances[z, x], GetDistanceToSegment(from, to, GetCellWorld(area, x, z)));
                }
            }
        }
        return distances;
    }

    // ---------- 격자, 계산 ----------

    // 월드 사각형을 덮는 격자 범위 (Terrain 밖은 잘라낸다)
    private static GridArea CreateArea(Terrain terrain, Rect worldRect, int resolution)
    {
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, worldRect, resolution);
        int xMin = Mathf.Clamp(range.xMin, 0, resolution);
        int zMin = Mathf.Clamp(range.yMin, 0, resolution);
        int xMax = Mathf.Clamp(range.xMax, 0, resolution);
        int zMax = Mathf.Clamp(range.yMax, 0, resolution);
        Vector3 position = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        return new GridArea
        {
            Range = new RectInt(xMin, zMin, xMax - xMin, zMax - zMin),
            Resolution = resolution,
            Origin = new Vector2(position.x, position.z),
            CellSize = new Vector2(size.x / resolution, size.z / resolution)
        };
    }

    private static GridArea CreateFullArea(Terrain terrain, int resolution)
    {
        Vector3 position = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        return CreateArea(terrain, new Rect(position.x, position.z, size.x, size.z), resolution);
    }

    // area 안 [z, x] 칸 중심의 월드 XZ
    private static Vector2 GetCellWorld(GridArea area, int x, int z)
    {
        return area.Origin + Vector2.Scale(new Vector2(area.Range.x + x + 0.5f, area.Range.y + z + 0.5f), area.CellSize);
    }

    private static float GetSlope(TerrainData data, GridArea area, int x, int z)
    {
        return data.GetSteepness((area.Range.x + x + 0.5f) / area.Resolution, (area.Range.y + z + 0.5f) / area.Resolution);
    }

    private static float GetHeight(Terrain terrain, GridArea area, int x, int z)
    {
        float u = (area.Range.x + x + 0.5f) / area.Resolution;
        float v = (area.Range.y + z + 0.5f) / area.Resolution;
        return terrain.terrainData.GetInterpolatedHeight(u, v) + terrain.transform.position.y;
    }

    // 월드 XZ 사각형(min ~ max)에 걸친 칸의 범위 (area 기준 번호, xMax·yMax 포함). 겹치는 칸이 없으면 false
    private static bool TryGetCellRange(GridArea area, Vector2 min, Vector2 max, out RectInt cells)
    {
        int xMin = Mathf.Max(Mathf.FloorToInt((min.x - area.Origin.x) / area.CellSize.x) - area.Range.x, 0);
        int zMin = Mathf.Max(Mathf.FloorToInt((min.y - area.Origin.y) / area.CellSize.y) - area.Range.y, 0);
        int xMax = Mathf.Min(Mathf.CeilToInt((max.x - area.Origin.x) / area.CellSize.x) - area.Range.x, area.Range.width - 1);
        int zMax = Mathf.Min(Mathf.CeilToInt((max.y - area.Origin.y) / area.CellSize.y) - area.Range.y, area.Range.height - 1);
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
    // 검수(레이어·프로토타입 대응, 데모 원본과의 비율·개수 비교, 창 밖 칠, 물 위·길 위 바닥 식생 0개, 옛 얼룩)에 쓰는 값이라 로그를 남긴다

    private static void LogResult(TransplantContext context, TransplantStats stats)
    {
        TerrainData data = context.Terrain.terrainData;
        Debug.Log("[MapDemoGroundTransplant] 데모 레이어 → 우리 레이어\n" + string.Join("\n", context.LayerLines));
        Debug.Log($"[MapDemoGroundTransplant] 데모 디테일 프로토타입 {context.Demo.detailPrototypes.Length}종 → 우리 프로토타입 (지금 {data.detailPrototypes.Length}종)\n" + string.Join("\n", context.PrototypeLines));
        Debug.Log($"[MapDemoGroundTransplant] 해상도 : 데모 크기 {context.Demo.size}, 알파맵 {context.Demo.alphamapResolution}, 디테일 {context.Demo.detailResolution} " +
                  $"(패치 {context.Demo.detailResolutionPerPatch}) / 우리 알파맵 {data.alphamapResolution}, 디테일 {data.detailResolution}. " +
                  $"창 밖 섞는 폭 {OutsideBlendWidth}m, 창 밖 나무 {context.Trees.Count}그루(북쪽 줄 Trees 전체), 길 흙 칸 {stats.TrailAlphaCells}개(알파맵)");

        string[] layerNames = data.terrainLayers.Select(layer => layer.name.Replace("Layer_", "")).ToArray();
        string demoRatios = string.Join(", ", layerNames.Select((name, layer) => $"{name} {stats.DemoWindowAlpha[layer] * 100:F1}%"));
        string ourRatios = string.Join(", ", layerNames.Select((name, layer) => $"{name} {stats.WindowBaseAlpha[layer] * 100:F1}%"));
        Debug.Log($"[MapDemoGroundTransplant] 창 안 지면 (물가·길 덧칠 전) : 데모 원본 {demoRatios} / 옮긴 결과 {ourRatios}");

        LogAlphamaps(context, layerNames);
        LogDetails(context, stats);
    }

    // 구역별 레이어 비율, 창 밖 동서 비율, 북쪽 줄 가중치 합, 옛 얼룩 검사 (북쪽 줄, 중앙, 물가, 길 밖 칸은 풀만 칠해져 있어야 한다)
    private static void LogAlphamaps(TransplantContext context, string[] layerNames)
    {
        TerrainData data = context.Terrain.terrainData;
        GridArea area = CreateFullArea(context.Terrain, data.alphamapResolution);
        float[,,] alphas = data.GetAlphamaps(0, 0, area.Range.width, area.Range.height);
        float[,] waterDistances = MapGroundPainter.CreateWaterDistances(context.Water, context.Terrain, area.Range, area.Resolution);
        float[,] trailDistances = CreateTrailDistances(context.Trails, area);
        Rect[] cellRects = LogCellNames.Select(name => MapGrid.GetRangeBounds(name, name)).ToArray();
        Rect center = MapGrid.GetRangeBounds(MapTerrainSetup.CenterFromCell, MapTerrainSetup.CenterToCell);
        int layerCount = data.alphamapLayers;
        var cellSums = new double[cellRects.Length, layerCount];
        var cellCounts = new int[cellRects.Length];
        // 0 : 창 밖 서쪽, 1 : 창 밖 동쪽
        var outsideSums = new double[2, layerCount];
        var outsideCounts = new int[2];
        float maxSumError = 0f;
        int checkedCount = 0;
        int grassCount = 0;

        for (int z = 0; z < area.Range.height; z++)
        {
            for (int x = 0; x < area.Range.width; x++)
            {
                Vector2 world = GetCellWorld(area, x, z);
                int cell = GetLogCellIndex(cellRects, world);
                cellCounts[cell]++;
                float sum = 0f;
                for (int layer = 0; layer < layerCount; layer++)
                {
                    cellSums[cell, layer] += alphas[z, x, layer];
                    sum += alphas[z, x, layer];
                }

                bool isNorth = context.NorthRow.Contains(world);
                if (isNorth) maxSumError = Mathf.Max(maxSumError, Mathf.Abs(sum - 1f));
                if (isNorth && (world.x < context.Window.xMin || world.x > context.Window.xMax))
                {
                    int side = world.x < context.Window.xMin ? 0 : 1;
                    outsideCounts[side]++;
                    for (int layer = 0; layer < layerCount; layer++)
                        outsideSums[side, layer] += alphas[z, x, layer];
                }

                if (isNorth || center.Contains(world) || trailDistances[z, x] < TrailReach) continue;
                if (MapGroundPainter.GetShoreWeight(waterDistances[z, x], world) > 0f) continue;
                checkedCount++;
                if (alphas[z, x, MapTerrainSetup.GrassLayer] >= FullWeight) grassCount++;
            }
        }

        float cellArea = area.CellSize.x * area.CellSize.y;
        var lines = new List<string>();
        for (int cell = 0; cell < cellRects.Length; cell++)
        {
            string ratios = string.Join(", ", Enumerable.Range(0, layerCount).Select(layer => $"{layerNames[layer]} {cellSums[cell, layer] * 100 / cellCounts[cell]:F1}%"));
            lines.Add($"{LogCellNames[cell]} : {ratios} / 돌 지면 {cellSums[cell, MapTerrainSetup.StoneLayer] * cellArea / HectareArea:F1}ha");
        }
        Debug.Log("[MapDemoGroundTransplant] 구역별 지면\n" + string.Join("\n", lines));

        string[] sideNames = { "서쪽 (A1, x < -750)", "동쪽 (C1, x > 750)" };
        IEnumerable<string> outsideLines = Enumerable.Range(0, 2).Select(side =>
            $"{sideNames[side]} {outsideCounts[side]}칸 : " + string.Join(", ", Enumerable.Range(0, layerCount).Select(layer => $"{layerNames[layer]} {outsideSums[side, layer] * 100 / outsideCounts[side]:F1}%")));
        Debug.Log("[MapDemoGroundTransplant] 창 밖 지면 (물가·길 포함)\n" + string.Join("\n", outsideLines));
        Debug.Log($"[MapDemoGroundTransplant] 북쪽 줄 가중치 합과 1의 최대 차이 {maxSumError:F5} (0에 가까워야 한다)");
        Debug.Log($"[MapDemoGroundTransplant] 옛 얼룩 검사 (북쪽 줄, 중앙, 물가, 길 밖 {checkedCount}칸) : 풀만 칠한 칸 {grassCount * 100f / checkedCount:F2}% (100%여야 한다)");
    }

    // 칸 중심은 칸 경계에 놓이지 않으므로 반드시 한 칸에 든다
    private static int GetLogCellIndex(Rect[] cellRects, Vector2 world)
    {
        for (int i = 0; i < cellRects.Length; i++)
        {
            if (cellRects[i].Contains(world)) return i;
        }
        return cellRects.Length - 1;
    }

    // 프로토타입별 창 안 개수(데모 원본, 옮긴 직후, 최종)와 전체 개수, 물 위·길 위 개수 (0이어야 한다)
    private static void LogDetails(TransplantContext context, TransplantStats stats)
    {
        TerrainData data = context.Terrain.terrainData;
        GridArea area = CreateFullArea(context.Terrain, data.detailResolution);
        float[,] waterDistances = MapGroundPainter.CreateWaterDistances(context.Water, context.Terrain, area.Range, area.Resolution);
        float[,] trailDistances = CreateTrailDistances(context.Trails, area);
        DetailPrototype[] prototypes = data.detailPrototypes;
        var lines = new List<string>();
        long total = 0;
        long northTotal = 0;
        long overWater = 0;
        long onTrail = 0;
        long demoWindowTotal = 0;
        long resampledTotal = 0;
        long windowTotal = 0;

        for (int i = 0; i < prototypes.Length; i++)
        {
            int[,] layer = data.GetDetailLayer(0, 0, area.Range.width, area.Range.height, i);
            long prototypeTotal = 0;
            long prototypeNorth = 0;
            long prototypeWindow = 0;
            for (int z = 0; z < area.Range.height; z++)
            {
                for (int x = 0; x < area.Range.width; x++)
                {
                    int count = layer[z, x];
                    if (count == 0) continue;

                    Vector2 world = GetCellWorld(area, x, z);
                    prototypeTotal += count;
                    if (context.NorthRow.Contains(world)) prototypeNorth += count;
                    if (context.Window.Contains(world)) prototypeWindow += count;
                    if (waterDistances[z, x] <= 0f) overWater += count;
                    if (trailDistances[z, x] < TrailReach) onTrail += count;
                }
            }
            total += prototypeTotal;
            northTotal += prototypeNorth;
            windowTotal += prototypeWindow;
            demoWindowTotal += stats.DemoWindowDetails[i];
            resampledTotal += stats.WindowResampledDetails[i];
            string name = HasSource(prototypes[i]) ? GetSourceName(prototypes[i]) : "(원본 없음)";
            lines.Add($"{i} {name} : 창 안 데모 원본 {stats.DemoWindowDetails[i]}, 옮긴 직후 {stats.WindowResampledDetails[i]}, 최종 {prototypeWindow} / 북쪽 줄 {prototypeNorth}, 맵 전체 {prototypeTotal}");
        }

        float windowHectare = context.Window.width * context.Window.height / HectareArea;
        Debug.Log($"[MapDemoGroundTransplant] 바닥 식생 (창 {windowHectare:F0}ha) : 데모 원본 창 안 {demoWindowTotal} ({demoWindowTotal / windowHectare:F0}/ha), " +
                  $"옮긴 직후 {resampledTotal} ({resampledTotal / windowHectare:F0}/ha), 물가·길 비운 뒤 {windowTotal}. 원본 없어 못 옮긴 데모 {stats.DroppedDetails}개. " +
                  $"북쪽 줄 {northTotal}, 맵 전체 {total}\n" + string.Join("\n", lines));
        Debug.Log($"[MapDemoGroundTransplant] 물 위 바닥 식생 {overWater}개, 길 위(길 중심에서 {TrailReach}m 안) 바닥 식생 {onTrail}개 (둘 다 0이어야 한다)");
    }
}
