using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 메뉴 17 : 남쪽 구역(B3)에 강을 따라 길게 이어지는 골짜기 지형과 높은 언덕 하나를 만든다.
// 강 중심에서 멀어질수록 땅이 올라가(골짜기 상승분 + 노이즈) 강이 골짜기 바닥을 흐르게 보이게 한다.
// 상승 가중치는 강 보호 거리에서 0이라 그 자리 높이가 정확히 바탕 높이(30m)이고, 노이즈 진폭이 상승분보다 작아 바탕 아래로 내려가지 않는다.
// 그래서 강 수면(약 28.85m)보다 높은 땅이 강을 둘러싼다.
// 강 서쪽 z FlatZMin ~ FlatZMax(강가 평지, 강 서쪽 시가지 자리)는 골짜기 상승분과 노이즈를 빼고 바탕 높이 + 완만한 굴곡(0 ~ FlatUndulation)만 남긴다.
// 평지 z 범위 바깥 FlatBlendWidth 안에서 평지 가중치가 부드럽게 0으로 줄어 골짜기 지형과 이어진다.
// 강 서쪽 판정은 강 중심선 기준이라 판정이 바뀌는 자리는 강 보호 구역 안이고, 그 안은 쓰지 않으므로 평지가 강 동쪽으로 새지 않는다.
// 굴곡에도 골짜기 상승 가중치를 곱해 강 보호 거리에서 바탕 높이로 이어지게 한다.
// 강 중심에서 RiverProtectDistance 안의 높이맵 점은 쓰지 않아 메뉴 7이 깎은 강바닥과 물가를 그대로 둔다.
// 높은 언덕은 구역을 멀리 내려다보는 지점이며, 남서쪽(메뉴 14)과 같은 이유로 노이즈 지형 위에 언덕 상승량을 더하고
// 거리를 재는 위치를 노이즈로 비틀어(도메인 워핑) 윤곽이 원이 아니게 한다.
// 네 경계 안쪽 감쇠 폭(기본 75m, 위치에 따라 50 ~ 100m로 흔들림) 안에서 바탕 높이로 돌아가고, 경계 줄은 쓰지 않아
// 옆 구역(B2, A3, C3)의 높이를 건드리지 않는다. 폭을 고정하면 경계 안쪽 둑이 경계와 나란한 직선으로 보여서 폭을 흔든다.
// 강 경로는 메뉴 7과 같은 정의(MapRiverBuilder.GetPath)를 쓰고, 보호 구역 밖은 매번 바탕 높이 기준으로 다시 계산해 덮어쓰므로 여러 번 실행해도 결과가 같다.
// 실행 순서 : ... → 14 → 15 → 16 → 17 → 18 → 19 → 10.
// 7(강)을 다시 실행하면 17도 다시 실행한다. 17을 다시 실행하면 지형 높이가 바뀌므로 18, 19도 순서대로 다시 실행한다.
public static class MapSouthShaper
{
    private const string SouthFromCell = "B3";
    private const string SouthToCell = "B3";

    // 임시값. 다른 구역과 노이즈 무늬가 겹치지 않도록 새 값을 쓴다
    private const int Seed = 20261141;

    // 강 중심에서 이 거리 안은 쓰지 않는다 (강바닥, 물가 보호). 골짜기 상승도 이 거리에서 시작한다.
    // 남쪽 배치(메뉴 18)도 이 거리 안을 강 보호 구역으로 비우므로 공개한다
    public const float RiverProtectDistance = 20f;
    // 이 거리에서 골짜기 상승 가중치가 1이 된다
    private const float ValleyRiseEndDistance = 120f;
    // 상승 가중치 1에서 바탕 위로 올라가는 높이와, 그 위에 더하는 노이즈(-1 ~ 1)의 진폭 (m).
    // 노이즈 진폭이 상승분보다 작아 바탕 아래로 내려가지 않는다
    private const float ValleyRise = 20f;
    private const float NoiseAmplitude = 8f;

    // 강 서쪽 강가 평지 z 범위 (사용자 승인 임시값). 남쪽 배치(메뉴 18)의 강 서쪽 시가지 구역 중심 z 범위도 이 값을 쓰므로 공개한다
    public const float FlatZMin = -760f;
    public const float FlatZMax = -300f;
    // 평지 z 범위 바깥에서 평지 가중치가 1 → 0으로 줄어드는 폭 (m)
    private const float FlatBlendWidth = 60f;
    // 평지 굴곡 높이 (바탕 위 0 ~ 이 값, m)와 굴곡 노이즈 주기 (m)
    private const float FlatUndulation = 1.5f;
    private const float FlatUndulationPeriod = 150f;
    // 굴곡 노이즈를 지형, 경계 폭, 언덕 비틀기 노이즈와 다른 무늬에서 뽑기 위한 위치
    private static readonly Vector2 FlatNoiseOffset = new Vector2(310f, 740f);

    // 경계 감쇠 폭 (기본 폭 ± 흔들림 비율 = 50 ~ 100m)과 폭 노이즈 주기 (m)
    private const float EdgeFadeWidth = 75f;
    private const float EdgeFadeWobble = 1f / 3f;
    private const float EdgeFadeWobblePeriod = 150f;
    // 폭 노이즈를 지형, 언덕 비틀기 노이즈와 다른 무늬에서 뽑기 위한 위치
    private static readonly Vector2 EdgeFadeNoiseOffset = new Vector2(620f, 380f);

    // 높은 언덕 (임시값). 중심, 반지름은 남쪽 배치(메뉴 18)도 언덕 꼭대기와 언덕 위 판정에 쓰므로 공개한다
    public static readonly Vector2 HillCenter = new Vector2(140f, -1000f);
    private const float HillPeakHeight = 65f;
    public const float HillRadius = 80f;
    // 언덕까지 거리를 잴 위치를 비트는 최대 거리와 비틀기 노이즈 주기 (m)
    private const float HillWarpDistance = 25f;
    private const float HillWarpPeriod = 120f;
    // x, z 비틀기가 서로 다른 무늬를 쓰도록 노이즈 위치를 따로 둔다
    private static readonly Vector2 HillWarpOffsetX = new Vector2(520f, 160f);
    private static readonly Vector2 HillWarpOffsetZ = new Vector2(240f, 860f);

    // 여러 겹의 Perlin 노이즈 (큰 오르내림 + 중간 굴곡 + 잔굴곡). 남서쪽과 같은 주기, 가중치
    private static readonly float[] OctavePeriods = { 500f, 200f, 70f };
    private static readonly float[] OctaveWeights = { 0.6f, 0.3f, 0.1f };

    // Perlin 입력이 음수가 되지 않도록 노이즈 위치를 양수 쪽으로 옮기는 범위
    private const float NoiseOffsetMin = 100f;
    private const float NoiseOffsetMax = 1000f;

    // 강 보호 구역 점이 바뀌었다고 볼 최소 높이 차 (m). 높이맵 저장 단위(약 0.012m)보다 작다
    private const float UnchangedTolerance = 0.001f;

    // 검수 로그에 쓸 높이 통계 (강 보호 구역 점은 높이 통계에서 뺀다)
    private struct HeightStats
    {
        // 언덕 윤곽 밖의 높이 (x = 최저, y = 최고, m)
        public Vector2 OutsideHillRange;
        public float PeakHeight;
        public Vector2 PeakPosition;
        // 쓴 점들에서 실제 경계 감쇠 폭의 최저, 최고 (m)
        public float FadeWidthMin;
        public float FadeWidthMax;
        // 강 보호 구역 밖(쓴 점)의 최저 높이 (m)
        public float WrittenMin;
        public int ProtectedCount;
        // 평지 가중치가 1인 점(강 서쪽 z FlatZMin ~ FlatZMax)의 높이 (x = 최저, y = 최고, m)
        public Vector2 FlatRange;
    }

    // 높이 계산에 함께 쓰는 값
    private class ShapeField
    {
        public Rect South;
        public Vector2[] Offsets;
        public float HillLift;
        public List<Vector2> River;
    }

    [MenuItem("Tools/Map/17. 남쪽 지형 만들기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        TerrainData data = terrain.terrainData;
        Rect south = MapGrid.GetRangeBounds(SouthFromCell, SouthToCell);
        int divisions = data.heightmapResolution - 1;
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, south, divisions);

        // 가장자리 줄은 옆 구역과 같이 쓰는 꼭짓점이라 바탕 높이 그대로 두고, 그 안쪽만 덮어쓴다
        var inner = new RectInt(range.x + 1, range.y + 1, range.width - 1, range.height - 1);
        List<Vector2> river = MapRiverBuilder.GetPath(terrain);
        float[,] original = data.GetHeights(inner.x, inner.y, inner.width, inner.height);
        float[,] riverDistances = CalculateRiverDistances(terrain, inner, river);

        var field = new ShapeField { South = south, Offsets = CreateOctaveOffsets(), River = river };
        field.HillLift = CalculateHillLift(river, field);
        float[,] heights = CreateHeights(terrain, inner, original, riverDistances, field, out HeightStats stats);
        data.SetHeights(inner.x, inner.y, heights);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        // 검수 때 높이 범위, 언덕 꼭대기, 경계가 이어졌는지, 강을 건드리지 않았는지, B3 밖을 건드리지 않았는지 확인할 값이라 남겨 둔다
        int changedProtectedCount = CountChangedProtected(data, inner, original, riverDistances);
        float borderDeviation = GetMaxBorderDeviation(data, range);
        bool isInsideSouth = IsInside(terrain, south, inner);
        Debug.Log($"[MapSouthShaper] 남쪽(B3) 언덕 윤곽 밖 높이 최저 {stats.OutsideHillRange.x:F1}m, 최고 {stats.OutsideHillRange.y:F1}m (강 보호 구역 제외), " +
                  $"언덕 꼭대기 {stats.PeakHeight:F1}m (x {stats.PeakPosition.x:F1}, z {stats.PeakPosition.y:F1}), 언덕 상승량 {field.HillLift:F1}m");
        Debug.Log($"[MapSouthShaper] 경계 줄 {MapTerrainSetup.BaseHeight}m 대비 최대 차이 {borderDeviation:F3}m, " +
                  $"경계 감쇠 폭 {stats.FadeWidthMin:F1} ~ {stats.FadeWidthMax:F1}m");
        Debug.Log($"[MapSouthShaper] 강 보호 구역({RiverProtectDistance}m 안) 점 {stats.ProtectedCount}개 중 바뀐 점 {changedProtectedCount}개 (0이어야 함), " +
                  $"보호 구역 밖 최저 높이 {stats.WrittenMin:F2}m ({MapTerrainSetup.BaseHeight}m 이상이어야 함)");
        Debug.Log($"[MapSouthShaper] 강 서쪽 평지(z {FlatZMin} ~ {FlatZMax}, 강 보호 구역 제외) 높이 최저 {stats.FlatRange.x:F2}m, 최고 {stats.FlatRange.y:F2}m " +
                  $"(바탕 {MapTerrainSetup.BaseHeight}m + 굴곡 0 ~ {FlatUndulation}m 안이어야 함)");
        Debug.Log($"[MapSouthShaper] B3 밖 쓰기 {(isInsideSouth ? "없음" : "있음")} " +
                  $"(높이맵 번호 x {inner.xMin} ~ {inner.xMax - 1}, z {inner.yMin} ~ {inner.yMax - 1})");
    }

    // 쓰는 범위의 높이맵 점마다 강 중심선까지의 거리. ValleyRiseEndDistance보다 먼 점은 그 값으로 둔다 (상승 가중치가 같은 1이라).
    // 구간마다 그 둘레 점만 보아 계산량을 줄인다 (메뉴 7의 FindNearestWater와 같은 방식)
    private static float[,] CalculateRiverDistances(Terrain terrain, RectInt inner, List<Vector2> river)
    {
        TerrainData data = terrain.terrainData;
        float spacing = data.size.x / (data.heightmapResolution - 1);
        Vector3 position = terrain.transform.position;
        var origin = new Vector2(position.x + inner.x * spacing, position.z + inner.y * spacing);

        var distances = new float[inner.height, inner.width];
        for (int z = 0; z < inner.height; z++)
        {
            for (int x = 0; x < inner.width; x++)
                distances[z, x] = ValleyRiseEndDistance;
        }

        for (int i = 0; i < river.Count - 1; i++)
        {
            Vector2 from = river[i];
            Vector2 to = river[i + 1];
            Vector2 min = (Vector2.Min(from, to) - Vector2.one * ValleyRiseEndDistance - origin) / spacing;
            Vector2 max = (Vector2.Max(from, to) + Vector2.one * ValleyRiseEndDistance - origin) / spacing;
            // 이 구간에서 ValleyRiseEndDistance 안에 드는 점이 쓰는 범위에 없으면 건너뛴다
            if (max.x < 0f || max.y < 0f || min.x > inner.width - 1 || min.y > inner.height - 1) continue;

            int xMin = Mathf.Max(Mathf.FloorToInt(min.x), 0);
            int zMin = Mathf.Max(Mathf.FloorToInt(min.y), 0);
            int xMax = Mathf.Min(Mathf.CeilToInt(max.x), inner.width - 1);
            int zMax = Mathf.Min(Mathf.CeilToInt(max.y), inner.height - 1);
            for (int z = zMin; z <= zMax; z++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    Vector2 world = origin + new Vector2(x, z) * spacing;
                    float distance = Vector2.Distance(world, Vector2.Lerp(from, to, GetProjection(from, to, world)));
                    if (distance < distances[z, x]) distances[z, x] = distance;
                }
            }
        }
        return distances;
    }

    // 한 위치에서 강 중심선(MapRiverBuilder.GetPath 또는 그 일부 구간)까지의 거리. 넘겨받은 모든 구간을 본다.
    // 남쪽 배치(메뉴 18)가 강 보호 구역 판정에 같은 계산을 쓰도록 공개한다
    public static float GetDistanceToRiver(List<Vector2> river, Vector2 world)
    {
        float minDistance = float.MaxValue;
        for (int i = 0; i < river.Count - 1; i++)
        {
            float t = GetProjection(river[i], river[i + 1], world);
            minDistance = Mathf.Min(minDistance, Vector2.Distance(world, Vector2.Lerp(river[i], river[i + 1], t)));
        }
        return minDistance;
    }

    // 선분 a-b 위에서 position에 가장 가까운 곳의 비율 (0 = a, 1 = b)
    private static float GetProjection(Vector2 a, Vector2 b, Vector2 position)
    {
        Vector2 segment = b - a;
        return Mathf.Clamp01(Vector2.Dot(position - a, segment) / segment.sqrMagnitude);
    }

    // 언덕 중심이 꼭대기 목표 높이에 닿도록, 중심의 (언덕을 더하기 전) 골짜기 지형 높이와의 차이만큼 올린다
    private static float CalculateHillLift(List<Vector2> river, ShapeField field)
    {
        float centerHeight = CalculateValleyHeight(HillCenter, GetDistanceToRiver(river, HillCenter),
            CalculateEdgeMask(HillCenter, field.South), field.Offsets);
        return HillPeakHeight - centerHeight;
    }

    // 강 보호 구역 점은 읽어 둔 원래 높이를 그대로 다시 쓰고, 나머지는 새로 계산한다
    private static float[,] CreateHeights(Terrain terrain, RectInt inner, float[,] original, float[,] riverDistances,
        ShapeField field, out HeightStats stats)
    {
        TerrainData data = terrain.terrainData;
        Vector3 position = terrain.transform.position;
        float sampleSpacing = data.size.x / (data.heightmapResolution - 1);

        var heights = new float[inner.height, inner.width];
        stats = new HeightStats
        {
            OutsideHillRange = new Vector2(float.MaxValue, float.MinValue), PeakHeight = float.MinValue,
            FadeWidthMin = float.MaxValue, FadeWidthMax = float.MinValue, WrittenMin = float.MaxValue,
            FlatRange = new Vector2(float.MaxValue, float.MinValue)
        };
        for (int z = 0; z < inner.height; z++)
        {
            // 한 줄은 z가 같으므로 강 서쪽 판정에 쓸 강 x를 줄마다 한 번만 구한다
            float riverX = GetRiverX(field.River, position.z + (inner.y + z) * sampleSpacing);
            for (int x = 0; x < inner.width; x++)
            {
                if (riverDistances[z, x] < RiverProtectDistance)
                {
                    heights[z, x] = original[z, x];
                    stats.ProtectedCount++;
                    continue;
                }

                var world = new Vector2(position.x + (inner.x + x) * sampleSpacing, position.z + (inner.y + z) * sampleSpacing);
                float flatWeight = CalculateFlatWeight(world, riverX);
                float height = CalculateHeight(world, riverDistances[z, x], flatWeight, field);
                heights[z, x] = height / data.size.y;
                RecordStats(ref stats, world, height, flatWeight);
            }
        }
        return heights;
    }

    private static void RecordStats(ref HeightStats stats, Vector2 world, float height, float flatWeight)
    {
        float fadeWidth = CalculateEdgeFadeWidth(world);
        stats.FadeWidthMin = Mathf.Min(stats.FadeWidthMin, fadeWidth);
        stats.FadeWidthMax = Mathf.Max(stats.FadeWidthMax, fadeWidth);
        stats.WrittenMin = Mathf.Min(stats.WrittenMin, height);
        if (flatWeight >= 1f)
        {
            stats.FlatRange.x = Mathf.Min(stats.FlatRange.x, height);
            stats.FlatRange.y = Mathf.Max(stats.FlatRange.y, height);
        }

        // 비틀린 윤곽 기준. B3 안쪽은 경계 감쇠값이 0보다 커서 언덕 모양 가중치만 보면 된다
        bool isInsideHill = CalculateHillWeight(world) > 0f;
        if (!isInsideHill)
        {
            stats.OutsideHillRange.x = Mathf.Min(stats.OutsideHillRange.x, height);
            stats.OutsideHillRange.y = Mathf.Max(stats.OutsideHillRange.y, height);
            return;
        }

        if (height <= stats.PeakHeight) return;
        stats.PeakHeight = height;
        stats.PeakPosition = world;
    }

    private static Vector2[] CreateOctaveOffsets()
    {
        var random = new System.Random(Seed);
        var offsets = new Vector2[OctavePeriods.Length];
        for (int i = 0; i < offsets.Length; i++)
        {
            float x = NoiseOffsetMin + (float)random.NextDouble() * (NoiseOffsetMax - NoiseOffsetMin);
            float z = NoiseOffsetMin + (float)random.NextDouble() * (NoiseOffsetMax - NoiseOffsetMin);
            offsets[i] = new Vector2(x, z);
        }
        return offsets;
    }

    private static float CalculateHeight(Vector2 world, float riverDistance, float flatWeight, ShapeField field)
    {
        // 언덕도 경계 감쇠를 받아, 비틀린 윤곽이 감쇠 구역에 들어가도 경계 줄이 바탕 높이로 남게 한다.
        // 비틀린 윤곽 밖은 가중치 0이라 골짜기 지형 그대로 남는다
        float edgeMask = CalculateEdgeMask(world, field.South);
        float hillWeight = CalculateHillWeight(world) * edgeMask;
        float valleyHeight = CalculateValleyHeight(world, riverDistance, edgeMask, field.Offsets);
        // 평지 가중치만큼 골짜기 지형을 평지 높이로 바꾼다. 두 높이 모두 바탕 이상이라 섞어도 바탕 아래로 내려가지 않는다
        float height = Mathf.Lerp(valleyHeight, CalculateFlatHeight(world, riverDistance, edgeMask), flatWeight);
        return height + field.HillLift * hillWeight;
    }

    // 바탕 + (골짜기 상승분 + 노이즈) × 상승 가중치 × 경계 감쇠. 노이즈는 -1 ~ 1이고 진폭이 상승분보다 작아 바탕 아래로 내려가지 않는다.
    // 남서쪽과 달리 노이즈를 정규화하지 않는다 (승인 사양)
    private static float CalculateValleyHeight(Vector2 world, float riverDistance, float edgeMask, Vector2[] offsets)
    {
        float noise = SampleNoise(world, offsets);
        return MapTerrainSetup.BaseHeight + (ValleyRise + noise * NoiseAmplitude) * CalculateRiseWeight(riverDistance) * edgeMask;
    }

    // 강 보호 거리에서 0, ValleyRiseEndDistance에서 1인 골짜기 상승 가중치
    private static float CalculateRiseWeight(float riverDistance)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RiverProtectDistance, ValleyRiseEndDistance, riverDistance));
    }

    // 강가 평지 높이 : 바탕 + 굴곡(0 ~ FlatUndulation) × 상승 가중치 × 경계 감쇠.
    // 굴곡은 0 이상이라 바탕 아래로 내려가지 않고, 강 보호 거리와 경계 줄에서는 바탕 높이다
    private static float CalculateFlatHeight(Vector2 world, float riverDistance, float edgeMask)
    {
        float perlin = Mathf.PerlinNoise(
            world.x / FlatUndulationPeriod + FlatNoiseOffset.x,
            world.y / FlatUndulationPeriod + FlatNoiseOffset.y);
        return MapTerrainSetup.BaseHeight + FlatUndulation * Mathf.Clamp01(perlin) * CalculateRiseWeight(riverDistance) * edgeMask;
    }

    // 강 서쪽 z FlatZMin ~ FlatZMax 안이면 1, 그 바깥 FlatBlendWidth 안에서 부드럽게 0으로 줄어든다. 강 동쪽은 0.
    // 강 서쪽 판정이 바뀌는 자리(강 중심선)는 강 보호 구역 안이라 쓰지 않으므로, 판정이 거기서 뚝 끊겨도 지형에 계단이 생기지 않는다
    private static float CalculateFlatWeight(Vector2 world, float riverX)
    {
        if (world.x >= riverX) return 0f;

        float outsideDistance = Mathf.Max(0f, Mathf.Max(FlatZMin - world.y, world.y - FlatZMax));
        return Mathf.SmoothStep(0f, 1f, 1f - outsideDistance / FlatBlendWidth);
    }

    // 강 중심선은 북에서 남으로 z가 줄어들기만 하므로, z가 가장 가까운 강 점의 x를 그 z의 강 위치로 본다
    // (남쪽 배치 메뉴 18의 강 서쪽 판정과 같은 방식)
    private static float GetRiverX(List<Vector2> river, float worldZ)
    {
        Vector2 nearest = river[0];
        foreach (Vector2 point in river)
        {
            if (Mathf.Abs(point.y - worldZ) < Mathf.Abs(nearest.y - worldZ)) nearest = point;
        }
        return nearest.x;
    }

    // 남쪽 구역 경계에 가까울수록 0에 가까워져, 옆 구역과 바탕 높이에서 만나게 한다.
    // 가장 가까운 경계까지 거리를 그 위치의 감쇠 폭으로 나누므로, 거리 0(경계 줄)에서는 폭과 상관없이 0이다
    private static float CalculateEdgeMask(Vector2 world, Rect south)
    {
        float edgeDistance = Mathf.Min(
            Mathf.Min(world.x - south.xMin, south.xMax - world.x),
            Mathf.Min(world.y - south.yMin, south.yMax - world.y));
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, CalculateEdgeFadeWidth(world), edgeDistance));
    }

    // 위치에 따라 흔들리는 감쇠 폭 (m). 월드 위치로 노이즈를 뽑아 경계를 따라, 그리고 모서리를 돌아서도 끊김 없이 이어진다
    private static float CalculateEdgeFadeWidth(Vector2 world)
    {
        float perlin = Mathf.PerlinNoise(
            world.x / EdgeFadeWobblePeriod + EdgeFadeNoiseOffset.x,
            world.y / EdgeFadeWobblePeriod + EdgeFadeNoiseOffset.y);
        float wobble = Mathf.Clamp(perlin * 2f - 1f, -1f, 1f);
        return EdgeFadeWidth * (1f + EdgeFadeWobble * wobble);
    }

    // 비틀린 위치가 언덕 중심에 있으면 1, 반지름 끝에서 0이 되도록 부드럽게 줄어드는 가중치.
    // 남쪽 배치(메뉴 18)가 언덕 위 판정을 지형과 같은 윤곽으로 하도록 공개한다 (경계 감쇠는 곱하지 않은 값)
    public static float CalculateHillWeight(Vector2 world)
    {
        Vector2 warped = world + SampleHillWarp(world);
        float distance = Vector2.Distance(warped, HillCenter);
        return Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01(distance / HillRadius));
    }

    // x, z 각각 -1 ~ 1 노이즈에 최대 비틀기 거리를 곱한 벡터 (m)
    private static Vector2 SampleHillWarp(Vector2 world)
    {
        Vector2 sample = world / HillWarpPeriod;
        float warpX = Mathf.PerlinNoise(sample.x + HillWarpOffsetX.x, sample.y + HillWarpOffsetX.y) * 2f - 1f;
        float warpZ = Mathf.PerlinNoise(sample.x + HillWarpOffsetZ.x, sample.y + HillWarpOffsetZ.y) * 2f - 1f;
        return new Vector2(warpX, warpZ) * HillWarpDistance;
    }

    // -1 ~ 1 범위의 여러 겹 노이즈
    private static float SampleNoise(Vector2 world, Vector2[] offsets)
    {
        float noise = 0f;
        for (int i = 0; i < OctavePeriods.Length; i++)
        {
            float perlin = Mathf.PerlinNoise(world.x / OctavePeriods[i] + offsets[i].x, world.y / OctavePeriods[i] + offsets[i].y);
            noise += OctaveWeights[i] * (perlin * 2f - 1f);
        }
        return Mathf.Clamp(noise, -1f, 1f);
    }

    // 쓰고 난 뒤 높이맵을 다시 읽어, 강 보호 구역 점 중 쓰기 전 값과 달라진 점의 수를 센다
    private static int CountChangedProtected(TerrainData data, RectInt inner, float[,] original, float[,] riverDistances)
    {
        float[,] written = data.GetHeights(inner.x, inner.y, inner.width, inner.height);
        int changedCount = 0;
        for (int z = 0; z < inner.height; z++)
        {
            for (int x = 0; x < inner.width; x++)
            {
                if (riverDistances[z, x] >= RiverProtectDistance) continue;
                if (Mathf.Abs(written[z, x] - original[z, x]) * data.size.y > UnchangedTolerance) changedCount++;
            }
        }
        return changedCount;
    }

    // 네 가장자리 줄의 높이가 바탕 높이에서 가장 많이 벗어난 값 (m).
    // 강이 경계 줄(z = -250, -1250)을 지나는 곳은 강바닥이라 바탕보다 낮게 나온다 (메뉴 7이 깎은 값)
    private static float GetMaxBorderDeviation(TerrainData data, RectInt range)
    {
        int width = range.width + 1;
        int depth = range.height + 1;
        float[,] heights = data.GetHeights(range.x, range.y, width, depth);

        float maxDeviation = 0f;
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                bool isBorder = x == 0 || x == width - 1 || z == 0 || z == depth - 1;
                if (!isBorder) continue;
                maxDeviation = Mathf.Max(maxDeviation, Mathf.Abs(heights[z, x] * data.size.y - MapTerrainSetup.BaseHeight));
            }
        }
        return maxDeviation;
    }

    // 쓴 범위의 양 끝 꼭짓점이 B3 경계선보다 안쪽에 있는지 (경계선 위도 밖으로 본다)
    private static bool IsInside(Terrain terrain, Rect south, RectInt inner)
    {
        TerrainData data = terrain.terrainData;
        Vector3 position = terrain.transform.position;
        float sampleSpacing = data.size.x / (data.heightmapResolution - 1);
        var first = new Vector2(position.x + inner.xMin * sampleSpacing, position.z + inner.yMin * sampleSpacing);
        var last = new Vector2(position.x + (inner.xMax - 1) * sampleSpacing, position.z + (inner.yMax - 1) * sampleSpacing);
        return first.x > south.xMin && first.y > south.yMin && last.x < south.xMax && last.y < south.yMax;
    }
}
