using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// 메뉴 7 : 북쪽 호수(메뉴 6)에서 시작해 중앙을 지나 남쪽 맵 끝까지 흐르는 얕은 강을 만든다.
// 1) 경로 : 고정 조절점을 잇는 곡선 + 고정 seed 굽이. 2) 수면 : 강 양옆 지형을 기준으로 하류로 갈수록 낮아지게 맞춘다.
// 3) 지형 : 강 양옆 고치는 범위(점마다 CarveHalfWidth ~ MaxCarveHalfWidth) 안만 강바닥, 강둑, 깎은/메운 비탈로 고친다.
//    깎은 비탈이 CarveHalfWidth 안에서 원래 땅과 만나지 못하는 점만 범위를 넓힌다. 4) 수면 : 구역(북, 중앙, 남)마다 리본 메시를 하나씩 놓는다.
//
// 지형은 "단면 높이 범위 안으로 자르기(clamp)"만 하므로 같은 수면으로 두 번 적용해도 한 번 적용한 것과 같다.
// 수면 높이는 강에서 CarveHalfWidth 밖, 호수 수면 밖의 지형만 읽는데, 범위를 넓힌 곳에서는 이 기준점이 고친 범위 안에 들 수 있다.
// 그래서 실행 끝에 "메뉴 7만 다시 실행했을 때"를 지형에 쓰지 않고 계산해 수면, 지형 차이를 로그로 남긴다 (0에 가까우면 메뉴 7만 다시 실행해도 된다).
// 호수 구간은 씬의 호수 수면 메시(메뉴 6)가 덮는 칸으로 정하므로, 메뉴 6을 먼저 실행해야 한다.
// 단, 경로나 단면 값을 바꾼 뒤에는 이전 강 자국이 남으므로 메뉴 1-1 → 2 → 5-1 → 6 → 7을 다시 실행한다.
// 강이 호수 남쪽 둑을 파 놓은 뒤에 메뉴 6을 실행하면 물이 강을 따라 새어 호수가 만들어지지 않는다 (메뉴 5-1부터 다시 실행).
public static class MapRiverBuilder
{
    // 경로 (임시값). 첫 점은 호수 중심(MapDemoTransplant.GetLakeCenter)에서 남쪽으로 StartInset만큼 들어간 곳이고, 나머지는 아래 조절점(월드 x, z)이다.
    // 지금 호수 중심 (37.8, 684.6)이면 첫 점은 약 (38, 625)로, 호수 남쪽 물가(x 38에서 z 약 600 ~ 610) 안쪽 물 위다.
    // 북(B1)에서는 호수 남쪽 둑(높이 약 47 ~ 55m, z 약 490 ~ 570)을 깎아 지나 x 약 30 ~ 38의 낮은 골짜기를 따라 z 250으로 내려오고,
    // 중앙(B2)에서는 거점 동쪽으로 비켜 가며, 남(B3)에서는 좌우로 굽이친다. (15, 230)부터는 이전 경로와 같다
    private const float StartInset = 60f;
    private static readonly Vector2[] PathPoints =
    {
        new Vector2(38f, 560f), new Vector2(38f, 480f), new Vector2(36f, 400f), new Vector2(30f, 320f), new Vector2(15f, 230f),
        new Vector2(70f, 160f), new Vector2(125f, 75f), new Vector2(140f, -10f), new Vector2(115f, -100f),
        new Vector2(65f, -180f), new Vector2(20f, -260f), new Vector2(-45f, -370f), new Vector2(-25f, -500f),
        new Vector2(55f, -630f), new Vector2(45f, -760f), new Vector2(-40f, -890f), new Vector2(-60f, -1020f),
        new Vector2(5f, -1140f), new Vector2(20f, -1250f)
    };
    // 조절점 사이 곡선을 촘촘히 그린 뒤 이 간격으로 다시 나눈다 (높이맵 간격 약 1.22m보다 조금 넓게)
    private const int CurveStepsPerSegment = 100;
    private const float SampleSpacing = 2f;
    // 조절점 곡선 위에 얹는 잔굽이 (고정 seed). 호수에서 남쪽 둑을 지나는 동안(첫 점에서 약 135m, z 약 490까지)은 곧게 두고 그 뒤로 서서히 살린다
    private const int Seed = 20261112;
    private const float MeanderAmplitude = 8f;
    private const float MeanderPeriod = 90f;
    private const float MeanderFadeStart = 150f;
    private const float MeanderFadeLength = 60f;
    // 거점에서 이보다 가까우면 경고한다 (사용자 승인 값 "최소 약 120m")
    private const float MinBaseDistance = 120f;

    // 단면 (임시값). 폭 12m, 가운데 수심 0.8m의 걸어서 건너는 얕은 강
    public const float HalfWidth = 6f;
    private const float WaterDepth = 0.8f;
    // 평지에서 수면이 양옆 땅보다 낮은 높이
    private const float GroundDrop = 1f;
    // 물가에서 강둑 꼭대기까지의 거리와 높이. 강둑 꼭대기를 GroundDrop보다 낮게 두어 평지에 둑이 솟지 않게 한다
    private const float BankWidth = 2f;
    private const float BankHeight = 0.8f;
    // 강둑 밖 깎은 비탈(약 31도)과 메운 비탈(약 27도)의 기울기. 깎은 비탈은 강둑 기울기(0.4)보다 가팔라야 단면 범위가 뒤집히지 않는다
    private const float CutSlope = 0.6f;
    private const float FillSlope = 0.5f;
    // 강 중심선에서 이 거리까지 지형을 고친다 (기본 범위)
    private const float CarveHalfWidth = 40f;
    // 기본 범위 안에서 비탈이 원래 땅과 만나지 못하는 점은 (만나는 거리 + CarveMargin)까지 넓히되, 이 값을 넘지 않는다 (임시값)
    private const float MaxCarveHalfWidth = 80f;
    // 비탈이 땅과 만나는 곳 밖으로 더 두는 여유. 범위 끝 단차를 재는 띠(EdgeCheckWidth)보다 넓어야 띠 안에 고친 칸이 없다
    private const float CarveMargin = 5f;
    // 비탈이 땅과 만나는 곳을 찾을 때 중심선 양옆으로 땅을 읽는 간격
    private const float WidthScanStep = 1f;
    // 땅이 단면 높이 범위 밖이라고 볼 최소 차이 (부동소수 오차는 무시)
    private const float SectionTolerance = 0.05f;
    // 굽은 곳에서 점 사이(양옆 땅을 읽지 않은 방향)가 덜 고쳐지지 않도록, 앞뒤 이 거리 안 점 중 가장 넓은 범위를 쓴다
    private const float WidthSmoothDistance = 10f;
    // 수면 기준 땅 높이를 읽는 곳 (강 중심선 양옆). CarveHalfWidth보다 바깥이지만, 범위를 넓힌 곳에서는 고친 지형 안에 들 수 있다
    private const float GroundReferenceOffset = 45f;
    // 수면 리본이 물가 밖 강둑 속으로 묻히는 폭. 이 위치의 강둑은 수면 + 약 0.4m라 물 끝이 드러나지 않는다
    private const float RibbonOverlap = 1f;

    // 호수와 잇기. 강 수면을 호수 수면보다 조금 낮게 시작해 호수 수면 밑으로 겹친다.
    // 호수 수면 메시가 덮는 칸(호수 구간) 안은 수면을 시작 높이로 두고 지형은 깎기만 한다 (호수 바닥을 메우지 않는다)
    private const float LakeOverlapDrop = 0.1f;
    // 호수 수면은 메뉴 6이 North/Lake 아래에 놓는다
    private const string LakeParentName = "Lake";

    // UV 한 칸의 월드 크기. 호수 메시(MapLakeBuilder)와 같은 밀도
    private const float UvTileSize = 20f;
    // 강둑 검사 간격과, 영향 폭 끝 단차를 재는 띠 폭
    private const float BankCheckInterval = 10f;
    private const float EdgeCheckWidth = 3f;
    // Perlin 입력이 음수가 되지 않도록 노이즈 위치를 양수 쪽으로 옮기는 범위
    private const float NoiseOffsetMin = 100f;
    private const float NoiseOffsetMax = 1000f;

    // 강이 지나는 구역. 리본 메시를 구역마다 나눠 MapLayout.md의 "구역의 배치물은 그 구역 부모 아래" 규칙을 지킨다.
    // 나뉜 메시는 경계의 같은 단면 정점을 함께 써서 틈 없이 이어진다
    private static readonly string[] ZoneRegionNames = { "North", "Center", "South" };
    private static readonly string[] ZoneCells = { "B1", "B2", "B3" };
    private const string RiverParentName = "River";
    private const string RiverObjectName = "River";

    private const string PrefabRootFolder = "Assets/Prefabs";
    private const string PrefabFolderName = "World";
    private const string PrefabFolder = PrefabRootFolder + "/" + PrefabFolderName;
    private const string WaterMaterialPath = "Assets/Imports/Polyart/PolyartStudio/DreamscapeMountains/Materials/Terrain/MI_Water_MountainsLake.mat";
    // 물 셰이더는 DOTS 인스턴싱을 지원하지 않아 GPU Resident Drawer에서 빼야 한다 (MapLakeBuilder와 같은 처리)
    private const string DisallowGpuDrivenTypeName = "UnityEngine.Rendering.DisallowGPUDrivenRendering, Unity.RenderPipelines.GPUDriven.Runtime";

    private class RiverPath
    {
        public List<Vector2> Points;
        // 시작점에서 각 점까지의 경로 길이
        public float[] Distances;
        // 각 점의 월드 수면 높이
        public float[] WaterHeights;
        // 각 점에서 지형을 고치는 범위 (중심선에서의 거리)
        public float[] CarveHalfWidths;
    }

    // 강이 고칠 수 있는 범위의 높이맵 꼭짓점들
    private class CarveArea
    {
        public RectInt Range;
        // [0, 0] 꼭짓점의 월드 XZ
        public Vector2 Origin;
        public float Spacing;
        // 월드 높이 [z, x]
        public float[,] Original;
        public float[,] Heights;
        // 강 중심선까지의 거리 (CarveWidths 이상이면 고치지 않는 곳)
        public float[,] PathDistances;
        // 가장 가까운 중심선 위치의 수면 높이
        public float[,] WaterHeights;
        // 가장 가까운 중심선 위치의 고치는 범위. MaxCarveHalfWidth 안에 중심선이 없으면 0
        public float[,] CarveWidths;
    }

    private class CarveStats
    {
        public float MaxCut;
        public Vector2 MaxCutPosition;
        public float MaxFill;
        public Vector2 MaxFillPosition;
        public float MaxEdgeChange;
        public Vector2 MaxEdgeChangePosition;
        // 구역(ZoneCells 순서)마다 최대 깎음. 북쪽 값이 호수 남쪽 둑을 판 깊이다
        public float[] ZoneMaxCuts = new float[ZoneCells.Length];
        public Vector2[] ZoneMaxCutPositions = new Vector2[ZoneCells.Length];
    }

    // 호수 수면 메시(메뉴 6)가 덮는 높이맵 격자 칸. 메시 칸 경계가 Terrain 격자선과 같아 칸 번호로 담는다.
    // 메시는 강이 지형을 고쳐도 바뀌지 않으므로, 다시 실행해도 호수 구간이 같다
    private class LakeFootprint
    {
        public HashSet<Vector2Int> Cells;
        // 격자 [0, 0] 꼭짓점의 월드 XZ (Terrain 위치)
        public Vector2 Origin;
        public float Spacing;
    }

    // 6단계(나무, 바위 배치)가 강을 피할 때 쓴다. 경로는 한 번 구해 두고 IsNearRiver에 넘긴다 (호수 중심 계산이 처음에 데모 높이맵을 읽어 무겁다)
    public static List<Vector2> GetPath(Terrain terrain)
    {
        Vector3 lakeCenter = MapDemoTransplant.GetLakeCenter(terrain);
        return BuildPath(new Vector2(lakeCenter.x, lakeCenter.z));
    }

    // 강 중심선에서 수평 거리가 (물 폭의 반 + margin) 안이면 true. 깎은 비탈까지 피하려면 margin을 넉넉히 준다
    public static bool IsNearRiver(List<Vector2> path, Vector3 world, float margin)
    {
        return GetDistanceToPath(path, new Vector2(world.x, world.z)) <= HalfWidth + margin;
    }

    [MenuItem("Tools/Map/7. 강 만들기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        Transform[] regionRoots = FindRegionRoots(terrain);
        if (regionRoots == null) return;

        System.Type disallowType = System.Type.GetType(DisallowGpuDrivenTypeName);
        if (disallowType == null)
        {
            Debug.LogError($"{DisallowGpuDrivenTypeName} 타입을 찾지 못했습니다. URP 패키지 버전이 바뀌었는지 확인하세요.");
            return;
        }

        // 호수는 북쪽 구역(ZoneRegionNames의 첫 번째, North)에 있다
        LakeFootprint lake = ReadLakeFootprint(terrain, regionRoots[0]);
        if (lake == null) return;

        // y는 호수 수면 높이
        Vector3 lakeCenter = MapDemoTransplant.GetLakeCenter(terrain);
        RiverPath path = CreatePath(terrain, lakeCenter, lake, out int fallbackCount);
        // 첫 점이 호수 밖이면 강 수면이 호수 밑으로 겹쳐 시작하지 못하므로 지형을 고치기 전에 멈춘다
        if (!IsInLake(lake, path.Points[0]))
        {
            Debug.LogError($"[MapRiverBuilder] 강 첫 점 ({path.Points[0].x:F1}, {path.Points[0].y:F1})이 호수 수면 메시 밖입니다. StartInset을 확인하세요. 지형을 고치지 않았습니다.");
            return;
        }

        CarveArea area = CalculateCarve(terrain, path, lake, out CarveStats stats);
        WriteHeights(terrain, area);

        CreatePrefabFolder();
        PlaceZoneMeshes(path, regionRoots, disallowType);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);

        // 검수(경로, 수면, 강둑, 구역 경계)에 쓰는 값이라 로그를 남긴다
        LogPath(path, lakeCenter, lake, fallbackCount);
        LogCarve(stats, path);
        LogBanks(terrain, path, lake);
        LogBoundaries(area);
        LogRerun(terrain, lakeCenter, lake, path);
    }

    // 씬의 North/Lake 아래 수면 메시에서 삼각형마다 그 무게중심이 든 칸을 모은다. 메시가 없으면 null
    private static LakeFootprint ReadLakeFootprint(Terrain terrain, Transform northRoot)
    {
        Transform lakeParent = northRoot.Find(LakeParentName);
        MeshFilter filter = lakeParent != null ? lakeParent.GetComponentInChildren<MeshFilter>() : null;
        if (filter == null || filter.sharedMesh == null)
        {
            Debug.LogError("North/Lake 아래에 호수 수면이 없습니다. 먼저 'Tools/Map/6. 호수 만들기'를 실행하세요.");
            return null;
        }

        TerrainData data = terrain.terrainData;
        var lake = new LakeFootprint
        {
            Cells = new HashSet<Vector2Int>(),
            Origin = new Vector2(terrain.transform.position.x, terrain.transform.position.z),
            Spacing = data.size.x / (data.heightmapResolution - 1)
        };
        Vector3[] vertices = filter.sharedMesh.vertices;
        int[] triangles = filter.sharedMesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 centroid = (vertices[triangles[i]] + vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3f;
            Vector3 world = filter.transform.TransformPoint(centroid);
            lake.Cells.Add(GetLakeCell(lake, new Vector2(world.x, world.z)));
        }
        return lake;
    }

    private static Vector2Int GetLakeCell(LakeFootprint lake, Vector2 world)
    {
        Vector2 grid = (world - lake.Origin) / lake.Spacing;
        return new Vector2Int(Mathf.FloorToInt(grid.x), Mathf.FloorToInt(grid.y));
    }

    private static bool IsInLake(LakeFootprint lake, Vector2 world)
    {
        return lake.Cells.Contains(GetLakeCell(lake, world));
    }

    // 구역 부모(North, Center, South)를 ZoneRegionNames 순서로 찾는다. 하나라도 없으면 null
    private static Transform[] FindRegionRoots(Terrain terrain)
    {
        GameObject[] roots = terrain.gameObject.scene.GetRootGameObjects();
        var regionRoots = new Transform[ZoneRegionNames.Length];
        for (int i = 0; i < ZoneRegionNames.Length; i++)
        {
            GameObject root = roots.FirstOrDefault(candidate => candidate.name == ZoneRegionNames[i]);
            if (root == null)
            {
                Debug.LogError($"씬 루트에 {ZoneRegionNames[i]}가 없습니다. 먼저 'Tools/Map/1. 지형과 구역 부모 생성'을 실행하세요.");
                return null;
            }
            regionRoots[i] = root.transform;
        }
        return regionRoots;
    }

    private static RiverPath CreatePath(Terrain terrain, Vector3 lakeCenter, LakeFootprint lake, out int fallbackCount)
    {
        List<Vector2> points = BuildPath(new Vector2(lakeCenter.x, lakeCenter.z));
        return new RiverPath
        {
            Points = points,
            Distances = GetDistances(points),
            WaterHeights = CalculateWaterHeights(terrain, points, lakeCenter.y, lake, out fallbackCount)
        };
    }

    // ---------- 경로 ----------

    private static List<Vector2> BuildPath(Vector2 lakeCenter)
    {
        List<Vector2> controls = GetControlPoints(lakeCenter);
        List<Vector2> points = Resample(SampleCurve(controls), SampleSpacing);
        return ApplyMeander(points);
    }

    private static List<Vector2> GetControlPoints(Vector2 lakeCenter)
    {
        var controls = new List<Vector2> { lakeCenter + Vector2.down * StartInset };
        controls.AddRange(PathPoints);
        return controls;
    }

    // 조절점을 모두 지나는 부드러운 곡선 (Catmull-Rom). 양 끝은 바깥으로 이어진 가상의 점을 둔다
    private static List<Vector2> SampleCurve(List<Vector2> controls)
    {
        int count = controls.Count;
        var curve = new List<Vector2>();
        for (int i = 0; i < count - 1; i++)
        {
            Vector2 before = i > 0 ? controls[i - 1] : 2f * controls[0] - controls[1];
            Vector2 after = i + 2 < count ? controls[i + 2] : 2f * controls[count - 1] - controls[count - 2];
            for (int step = 0; step < CurveStepsPerSegment; step++)
                curve.Add(GetCatmullRom(before, controls[i], controls[i + 1], after, (float)step / CurveStepsPerSegment));
        }
        curve.Add(controls[count - 1]);
        return curve;
    }

    private static Vector2 GetCatmullRom(Vector2 before, Vector2 from, Vector2 to, Vector2 after, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (2f * from + (to - before) * t + (2f * before - 5f * from + 4f * to - after) * t2
                       + (3f * from - before - 3f * to + after) * t3);
    }

    // 곡선을 같은 간격의 점으로 다시 나눈다. 마지막 점(남쪽 맵 끝)은 그대로 남긴다
    private static List<Vector2> Resample(List<Vector2> curve, float spacing)
    {
        var points = new List<Vector2> { curve[0] };
        // 마지막으로 찍은 점 뒤로 지나온 거리
        float carried = 0f;
        for (int i = 1; i < curve.Count; i++)
        {
            float length = Vector2.Distance(curve[i - 1], curve[i]);
            float walked = 0f;
            while (carried + length - walked >= spacing)
            {
                walked += spacing - carried;
                points.Add(Vector2.Lerp(curve[i - 1], curve[i], walked / length));
                carried = 0f;
            }
            carried += length - walked;
        }

        // 남은 거리가 짧으면 마지막 점을 끝점으로 옮겨 아주 짧은 구간이 생기지 않게 한다
        if (carried > spacing * 0.5f) points.Add(curve[curve.Count - 1]);
        else points[points.Count - 1] = curve[curve.Count - 1];
        return points;
    }

    // 경로 오른쪽 방향으로 Perlin 노이즈만큼 옮겨 잔굽이를 만든다
    private static List<Vector2> ApplyMeander(List<Vector2> points)
    {
        var random = new System.Random(Seed);
        float offsetX = NoiseOffsetMin + (float)random.NextDouble() * (NoiseOffsetMax - NoiseOffsetMin);
        float offsetY = NoiseOffsetMin + (float)random.NextDouble() * (NoiseOffsetMax - NoiseOffsetMin);
        float[] distances = GetDistances(points);

        var meandered = new List<Vector2>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MeanderFadeStart, MeanderFadeStart + MeanderFadeLength, distances[i]));
            float noise = Mathf.PerlinNoise(distances[i] / MeanderPeriod + offsetX, offsetY) * 2f - 1f;
            meandered.Add(points[i] + GetRight(points, i) * (noise * MeanderAmplitude * fade));
        }
        return meandered;
    }

    private static float[] GetDistances(List<Vector2> points)
    {
        var distances = new float[points.Count];
        for (int i = 1; i < points.Count; i++)
            distances[i] = distances[i - 1] + Vector2.Distance(points[i - 1], points[i]);
        return distances;
    }

    // 하류를 바라볼 때 오른쪽 방향 (단위 벡터)
    private static Vector2 GetRight(List<Vector2> points, int index)
    {
        Vector2 previous = points[Mathf.Max(index - 1, 0)];
        Vector2 next = points[Mathf.Min(index + 1, points.Count - 1)];
        Vector2 direction = (next - previous).normalized;
        return new Vector2(direction.y, -direction.x);
    }

    private static float GetDistanceToPath(List<Vector2> points, Vector2 position)
    {
        float minDistance = float.MaxValue;
        for (int i = 0; i < points.Count - 1; i++)
        {
            float t = GetProjection(points[i], points[i + 1], position);
            minDistance = Mathf.Min(minDistance, Vector2.Distance(position, Vector2.Lerp(points[i], points[i + 1], t)));
        }
        return minDistance;
    }

    // 선분 a-b 위에서 position에 가장 가까운 곳의 비율 (0 = a, 1 = b)
    private static float GetProjection(Vector2 a, Vector2 b, Vector2 position)
    {
        Vector2 segment = b - a;
        return Mathf.Clamp01(Vector2.Dot(position - a, segment) / segment.sqrMagnitude);
    }

    // ---------- 수면 높이 ----------

    // 점마다 목표 수면(양옆 땅 - GroundDrop, 호수 수면 아래로 제한)을 구하고, 하류로 가며 낮아지기만 하도록 맞춘다.
    // 언덕을 지날 때는 수면이 오르지 않아 언덕을 깎고, 웅덩이를 지날 때는 앞뒤와 평균을 내 메운다.
    // 호수 구간은 목표가 시작 높이이고 뒤의 목표가 모두 시작 높이 이하라, 맞춘 뒤에도 시작 높이 그대로 남는다
    private static float[] CalculateWaterHeights(Terrain terrain, List<Vector2> points, float lakeHeight, LakeFootprint lake, out int fallbackCount)
    {
        float startHeight = lakeHeight - LakeOverlapDrop;
        var targets = new float[points.Count];
        float previousGround = startHeight + GroundDrop;
        fallbackCount = 0;
        for (int i = 0; i < points.Count; i++)
        {
            if (IsInLake(lake, points[i]))
            {
                targets[i] = startHeight;
                continue;
            }

            // 양옆 기준점이 모두 강의 다른 구간 가까이나 호수 안에 있으면 앞 점의 땅 높이를 그대로 쓴다
            if (TrySampleGroundReference(terrain, points, i, lake, out float ground)) previousGround = ground;
            else fallbackCount++;
            targets[i] = Mathf.Min(previousGround - GroundDrop, startHeight);
        }
        return FitDecreasing(targets);
    }

    // 강 양옆 GroundReferenceOffset 위치의 땅 높이 평균. 강이 고치는 범위 안에 든 기준점은 빼서, 다시 실행해도 같은 값이 나오게 한다.
    // 호수 안 기준점(호수 바닥)도 뺀다. 물가 근처에서 호수 바닥을 읽으면 수면이 호수 수면보다 크게 낮아져 호수와 강 사이가 끊긴다
    private static bool TrySampleGroundReference(Terrain terrain, List<Vector2> points, int index, LakeFootprint lake, out float ground)
    {
        Vector2 right = GetRight(points, index) * GroundReferenceOffset;
        float sum = 0f;
        int count = 0;
        foreach (float side in new[] { -1f, 1f })
        {
            Vector2 reference = points[index] + right * side;
            if (IsInLake(lake, reference) || GetDistanceToPath(points, reference) < CarveHalfWidth) continue;

            sum += SampleWorldHeight(terrain, reference);
            count++;
        }
        ground = count > 0 ? sum / count : 0f;
        return count > 0;
    }

    private static float SampleWorldHeight(Terrain terrain, Vector2 position)
    {
        return terrain.SampleHeight(new Vector3(position.x, 0f, position.y)) + terrain.transform.position.y;
    }

    // 앞에서 뒤로 낮아지기만(같아도 됨) 하면서 targets와의 차이 제곱합이 가장 작은 값 (Pool Adjacent Violators).
    // 뒤 값이 앞 값보다 높으면 두 묶음을 평균으로 합치는 일을 반복한다
    private static float[] FitDecreasing(float[] targets)
    {
        var blockValues = new List<float>();
        var blockCounts = new List<int>();
        foreach (float target in targets)
        {
            blockValues.Add(target);
            blockCounts.Add(1);
            while (blockValues.Count > 1 && blockValues[blockValues.Count - 1] > blockValues[blockValues.Count - 2])
            {
                int last = blockValues.Count - 1;
                int merged = blockCounts[last] + blockCounts[last - 1];
                blockValues[last - 1] = (blockValues[last] * blockCounts[last] + blockValues[last - 1] * blockCounts[last - 1]) / merged;
                blockCounts[last - 1] = merged;
                blockValues.RemoveAt(last);
                blockCounts.RemoveAt(last);
            }
        }

        var fitted = new float[targets.Length];
        int index = 0;
        for (int block = 0; block < blockValues.Count; block++)
        {
            for (int i = 0; i < blockCounts[block]; i++)
                fitted[index++] = blockValues[block];
        }
        return fitted;
    }

    // ---------- 지형 ----------

    // 점마다 고치는 범위를 정하고, 지금 지형을 단면 높이 범위로 자른 결과를 만든다 (지형에 쓰지는 않는다)
    private static CarveArea CalculateCarve(Terrain terrain, RiverPath path, LakeFootprint lake, out CarveStats stats)
    {
        path.CarveHalfWidths = CalculateCarveHalfWidths(terrain, path, lake);
        CarveArea area = ReadCarveArea(terrain, path.Points);
        FindNearestWater(area, path);
        stats = ApplyCarve(area, lake);
        return area;
    }

    // 점마다 중심선 양옆 땅을 MaxCarveHalfWidth에서 안쪽으로 읽어, 단면 높이 범위 밖인 가장 먼 거리(비탈이 땅과 만나는 곳)를 찾는다.
    // 범위 = 그 거리 + CarveMargin을 [CarveHalfWidth, MaxCarveHalfWidth]로 자른 값. 넓힐 필요가 없는 점은 CarveHalfWidth 그대로다.
    // 마지막으로 앞뒤 WidthSmoothDistance 안 점의 가장 넓은 값을 쓴다
    private static float[] CalculateCarveHalfWidths(Terrain terrain, RiverPath path, LakeFootprint lake)
    {
        int count = path.Points.Count;
        var needed = new float[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 right = GetRight(path.Points, i);
            float farthest = 0f;
            foreach (float side in new[] { -1f, 1f })
            {
                for (float distance = MaxCarveHalfWidth; distance > CarveHalfWidth - CarveMargin; distance -= WidthScanStep)
                {
                    Vector2 world = path.Points[i] + right * (side * distance);
                    float ground = SampleWorldHeight(terrain, world);
                    float clamped = ClampToSection(ground, distance, path.WaterHeights[i], !IsInLake(lake, world));
                    if (Mathf.Abs(clamped - ground) <= SectionTolerance) continue;

                    farthest = Mathf.Max(farthest, distance);
                    break;
                }
            }
            needed[i] = Mathf.Clamp(farthest + CarveMargin, CarveHalfWidth, MaxCarveHalfWidth);
        }

        int window = Mathf.CeilToInt(WidthSmoothDistance / SampleSpacing);
        var widths = new float[count];
        for (int i = 0; i < count; i++)
        {
            for (int j = Mathf.Max(i - window, 0); j <= Mathf.Min(i + window, count - 1); j++)
                widths[i] = Mathf.Max(widths[i], needed[j]);
        }
        return widths;
    }

    private static CarveArea ReadCarveArea(Terrain terrain, List<Vector2> points)
    {
        TerrainData data = terrain.terrainData;
        int divisions = data.heightmapResolution - 1;
        Vector3 position = terrain.transform.position;
        Rect worldRect = GetCarveRect(points, new Rect(position.x, position.z, data.size.x, data.size.z));
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, worldRect, divisions);
        float spacing = data.size.x / divisions;

        // 높이맵은 꼭짓점 기준이라 끝 줄까지 포함한다
        float[,] normalized = data.GetHeights(range.x, range.y, range.width + 1, range.height + 1);
        var original = new float[range.height + 1, range.width + 1];
        for (int z = 0; z <= range.height; z++)
        {
            for (int x = 0; x <= range.width; x++)
                original[z, x] = normalized[z, x] * data.size.y + position.y;
        }

        return new CarveArea
        {
            Range = range,
            Origin = new Vector2(position.x + range.x * spacing, position.z + range.y * spacing),
            Spacing = spacing,
            Original = original,
            Heights = (float[,])original.Clone(),
            PathDistances = new float[range.height + 1, range.width + 1],
            WaterHeights = new float[range.height + 1, range.width + 1],
            CarveWidths = new float[range.height + 1, range.width + 1]
        };
    }

    // 경로를 MaxCarveHalfWidth만큼 넓힌 범위를 Terrain 안으로 자른다
    private static Rect GetCarveRect(List<Vector2> points, Rect terrainRect)
    {
        Vector2 min = points.Aggregate(Vector2.Min) - Vector2.one * MaxCarveHalfWidth;
        Vector2 max = points.Aggregate(Vector2.Max) + Vector2.one * MaxCarveHalfWidth;
        return Rect.MinMaxRect(
            Mathf.Max(min.x, terrainRect.xMin), Mathf.Max(min.y, terrainRect.yMin),
            Mathf.Min(max.x, terrainRect.xMax), Mathf.Min(max.y, terrainRect.yMax));
    }

    // 꼭짓점마다 가장 가까운 중심선 위치까지의 거리와 그곳의 수면 높이, 고치는 범위를 구한다.
    // 구간마다 그 둘레 꼭짓점만 보아 계산량을 줄인다
    private static void FindNearestWater(CarveArea area, RiverPath path)
    {
        int depth = area.Original.GetLength(0);
        int width = area.Original.GetLength(1);
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
                area.PathDistances[z, x] = MaxCarveHalfWidth;
        }

        for (int i = 0; i < path.Points.Count - 1; i++)
        {
            Vector2 from = path.Points[i];
            Vector2 to = path.Points[i + 1];
            RectInt cells = GetCellRange(area, from, to);
            for (int z = cells.yMin; z <= cells.yMax; z++)
            {
                for (int x = cells.xMin; x <= cells.xMax; x++)
                {
                    Vector2 world = area.Origin + new Vector2(x, z) * area.Spacing;
                    float t = GetProjection(from, to, world);
                    float distance = Vector2.Distance(world, Vector2.Lerp(from, to, t));
                    if (distance >= area.PathDistances[z, x]) continue;

                    area.PathDistances[z, x] = distance;
                    area.WaterHeights[z, x] = Mathf.Lerp(path.WaterHeights[i], path.WaterHeights[i + 1], t);
                    area.CarveWidths[z, x] = Mathf.Lerp(path.CarveHalfWidths[i], path.CarveHalfWidths[i + 1], t);
                }
            }
        }
    }

    // 구간 from-to에서 MaxCarveHalfWidth 안에 들 수 있는 꼭짓점 범위 (xMax, yMax 포함)
    private static RectInt GetCellRange(CarveArea area, Vector2 from, Vector2 to)
    {
        int lastX = area.Original.GetLength(1) - 1;
        int lastZ = area.Original.GetLength(0) - 1;
        Vector2 min = (Vector2.Min(from, to) - Vector2.one * MaxCarveHalfWidth - area.Origin) / area.Spacing;
        Vector2 max = (Vector2.Max(from, to) + Vector2.one * MaxCarveHalfWidth - area.Origin) / area.Spacing;
        int xMin = Mathf.Clamp(Mathf.FloorToInt(min.x), 0, lastX);
        int zMin = Mathf.Clamp(Mathf.FloorToInt(min.y), 0, lastZ);
        int xMax = Mathf.Clamp(Mathf.CeilToInt(max.x), 0, lastX);
        int zMax = Mathf.Clamp(Mathf.CeilToInt(max.y), 0, lastZ);
        return new RectInt(xMin, zMin, xMax - xMin, zMax - zMin);
    }

    private static CarveStats ApplyCarve(CarveArea area, LakeFootprint lake)
    {
        var stats = new CarveStats();
        Rect[] zoneBounds = ZoneCells.Select(cell => MapGrid.GetRangeBounds(cell, cell)).ToArray();
        for (int z = 0; z < area.Original.GetLength(0); z++)
        {
            for (int x = 0; x < area.Original.GetLength(1); x++)
            {
                float distance = area.PathDistances[z, x];
                float carveWidth = area.CarveWidths[z, x];
                if (distance >= carveWidth) continue;

                Vector2 world = area.Origin + new Vector2(x, z) * area.Spacing;
                bool canFill = !IsInLake(lake, world);
                float original = area.Original[z, x];
                float height = ClampToSection(original, distance, area.WaterHeights[z, x], canFill);
                area.Heights[z, x] = height;
                RecordChange(stats, world, height - original, distance, carveWidth);
                RecordZoneCut(stats, zoneBounds, world, original - height);
            }
        }
        return stats;
    }

    private static void RecordZoneCut(CarveStats stats, Rect[] zoneBounds, Vector2 world, float cut)
    {
        for (int zone = 0; zone < zoneBounds.Length; zone++)
        {
            if (!zoneBounds[zone].Contains(world) || cut <= stats.ZoneMaxCuts[zone]) continue;

            stats.ZoneMaxCuts[zone] = cut;
            stats.ZoneMaxCutPositions[zone] = world;
        }
    }

    // 중심선에서 distance 떨어진 곳의 지형을 단면 높이 범위 [바닥, 천장] 안으로 자른다.
    // 물 안은 바닥 = 천장 = 강바닥(가운데 수면 - WaterDepth, 물가에서 수면), 물가 밖은 바닥이 강둑을 거쳐 메운 비탈로 내려가고
    // 천장은 깎은 비탈로 올라간다. 원래 땅이 두 비탈 사이에 있으면 그대로 남아, 비탈이 땅과 만나는 곳에서 강 둘레가 끝난다
    private static float ClampToSection(float original, float distance, float water, bool canFill)
    {
        float floor;
        float ceiling;
        if (distance <= HalfWidth)
        {
            float ratio = distance / HalfWidth;
            floor = water - WaterDepth * (1f - ratio * ratio);
            ceiling = floor;
        }
        else
        {
            float bankDistance = distance - HalfWidth;
            ceiling = water + bankDistance * CutSlope;
            floor = bankDistance <= BankWidth
                ? water + bankDistance * BankHeight / BankWidth
                : water + BankHeight - (bankDistance - BankWidth) * FillSlope;
        }

        // 호수 둘레는 깎기만 해서 호수 바닥을 강바닥 높이로 메우지 않는다
        if (!canFill) return Mathf.Min(original, ceiling);
        return Mathf.Clamp(original, floor, ceiling);
    }

    private static void RecordChange(CarveStats stats, Vector2 world, float change, float distance, float carveWidth)
    {
        if (-change > stats.MaxCut)
        {
            stats.MaxCut = -change;
            stats.MaxCutPosition = world;
        }
        if (change > stats.MaxFill)
        {
            stats.MaxFill = change;
            stats.MaxFillPosition = world;
        }
        if (distance >= carveWidth - EdgeCheckWidth && Mathf.Abs(change) > stats.MaxEdgeChange)
        {
            stats.MaxEdgeChange = Mathf.Abs(change);
            stats.MaxEdgeChangePosition = world;
        }
    }

    private static void WriteHeights(Terrain terrain, CarveArea area)
    {
        TerrainData data = terrain.terrainData;
        float baseY = terrain.transform.position.y;
        int depth = area.Heights.GetLength(0);
        int width = area.Heights.GetLength(1);
        var normalized = new float[depth, width];
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
                normalized[z, x] = (area.Heights[z, x] - baseY) / data.size.y;
        }
        data.SetHeights(area.Range.x, area.Range.y, normalized);
        EditorUtility.SetDirty(data);
    }

    // ---------- 수면 메시 ----------

    private static void CreatePrefabFolder()
    {
        if (!AssetDatabase.IsValidFolder(PrefabRootFolder)) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder(PrefabRootFolder, PrefabFolderName);
    }

    // 경로 구간을 가운데 점이 든 구역에 나눠 담고, 구역마다 리본 메시와 prefab을 만들어 <구역>/River 아래에 놓는다
    private static void PlaceZoneMeshes(RiverPath path, Transform[] regionRoots, System.Type disallowType)
    {
        var isPlaced = new bool[path.Points.Count - 1];
        for (int zone = 0; zone < ZoneRegionNames.Length; zone++)
        {
            Transform parent = ResetParent(regionRoots[zone], RiverParentName);
            Rect bounds = MapGrid.GetRangeBounds(ZoneCells[zone], ZoneCells[zone]);
            List<int> segments = FindZoneSegments(path.Points, bounds, isPlaced);
            if (segments.Count == 0)
            {
                Debug.LogWarning($"[MapRiverBuilder] {ZoneRegionNames[zone]}({ZoneCells[zone]})을 지나는 강 구간이 없습니다.");
                continue;
            }

            string name = RiverObjectName + ZoneRegionNames[zone];
            Mesh mesh = LoadOrCreateMesh($"{PrefabFolder}/{name}WaterMesh.asset");
            Vector3 pivot = BuildRiverMesh(mesh, path, segments);
            GameObject prefab = SaveRiverPrefab(mesh, disallowType, $"{PrefabFolder}/{name}.prefab");

            // prefab 연결을 유지한 채 놓는다. Static 플래그는 켜지 않는다
            var river = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            river.transform.position = pivot;
            Debug.Log($"[MapRiverBuilder] {ZoneRegionNames[zone]}/{RiverParentName} 수면 메시 정점 {mesh.vertexCount}, 삼각형 {mesh.GetIndexCount(0) / 3}");
        }

        int outsideCount = isPlaced.Count(placed => !placed);
        if (outsideCount > 0) Debug.LogWarning($"[MapRiverBuilder] 북, 중앙, 남 구역 밖으로 나간 강 구간 {outsideCount}개는 수면을 만들지 않았습니다. 경로 점을 확인하세요.");
    }

    private static List<int> FindZoneSegments(List<Vector2> points, Rect bounds, bool[] isPlaced)
    {
        var segments = new List<int>();
        for (int i = 0; i < points.Count - 1; i++)
        {
            if (isPlaced[i] || !bounds.Contains((points[i] + points[i + 1]) * 0.5f)) continue;

            isPlaced[i] = true;
            segments.Add(i);
        }
        return segments;
    }

    // 이미 있으면 같은 에셋을 다시 채워, prefab이 가리키는 메시 참조가 끊기지 않게 한다
    private static Mesh LoadOrCreateMesh(string path)
    {
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;

        mesh = new Mesh();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // 경로 점마다 왼쪽, 오른쪽 정점 2개를 두는 평평한 띠. 정점 높이는 그 점의 수면 높이다.
    // 메시 원점(pivot)은 구역 첫 점의 수면이고 회전 없이 놓으므로 메시 x, z가 월드 동, 북과 같다
    private static Vector3 BuildRiverMesh(Mesh mesh, RiverPath path, List<int> segments)
    {
        int first = segments[0];
        var pivot = new Vector3(path.Points[first].x, path.WaterHeights[first], path.Points[first].y);
        float halfRibbon = HalfWidth + RibbonOverlap;
        // 점 번호 → 그 점의 왼쪽 정점 번호 (오른쪽은 + 1). 이웃 구간이 정점을 함께 쓴다
        var vertexStarts = new Dictionary<int, int>();
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        int GetVertices(int index)
        {
            if (vertexStarts.TryGetValue(index, out int start)) return start;

            Vector2 right = GetRight(path.Points, index) * halfRibbon;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector2 edge = path.Points[index] + right * side;
                vertices.Add(new Vector3(edge.x, path.WaterHeights[index], edge.y) - pivot);
                uvs.Add(new Vector2(side * halfRibbon, path.Distances[index]) / UvTileSize);
            }
            vertexStarts[index] = vertices.Count - 2;
            return vertices.Count - 2;
        }

        foreach (int segment in segments)
        {
            int from = GetVertices(segment);
            int to = GetVertices(segment + 1);
            // 앞면이 위를 향하도록, 위에서 내려다볼 때 시계 방향 순서로 넣는다 (오른쪽 → 왼쪽 → 다음 왼쪽)
            triangles.AddRange(new[] { from + 1, from, to, from + 1, to, to + 1 });
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(Enumerable.Repeat(Vector3.up, vertices.Count).ToList());
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return pivot;
    }

    // 임시 GameObject로 구성해 prefab으로 저장한다. 같은 경로에 저장하면 GUID가 유지되어 다른 참조가 끊기지 않는다.
    // 물에 들어가는 동작은 기획에 없으므로 충돌체는 붙이지 않는다
    private static GameObject SaveRiverPrefab(Mesh mesh, System.Type disallowType, string prefabPath)
    {
        var river = new GameObject(RiverObjectName);
        river.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = river.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
        // 투명한 수면이 바닥에 그림자를 드리우지 않게 한다
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        river.AddComponent(disallowType);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(river, prefabPath);
        Object.DestroyImmediate(river);
        return prefab;
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

    // ---------- 로그 ----------

    private static void LogPath(RiverPath path, Vector3 lakeCenter, LakeFootprint lake, int fallbackCount)
    {
        List<Vector2> controls = GetControlPoints(new Vector2(lakeCenter.x, lakeCenter.z));
        string controlText = string.Join(", ", controls.Select(point => $"({point.x:F0}, {point.y:F0})"));
        int closest = Enumerable.Range(0, path.Points.Count).OrderBy(i => path.Points[i].magnitude).First();
        float baseDistance = path.Points[closest].magnitude;
        // 첫 점부터 호수 수면 메시를 벗어나기 전까지 = 강 수면이 호수 수면 밑으로 겹친 구간
        int exit = Enumerable.Range(0, path.Points.Count).FirstOrDefault(i => !IsInLake(lake, path.Points[i]));

        Debug.Log($"[MapRiverBuilder] 호수 중심 ({lakeCenter.x:F1}, {lakeCenter.z:F1}), 수면 {lakeCenter.y:F2}m (MapDemoTransplant.GetLakeCenter). " +
                  $"호수 수면 밑 구간 {path.Distances[exit]:F0}m, 호수를 벗어나는 점 ({path.Points[exit].x:F0}, {path.Points[exit].y:F0})");
        Debug.Log($"[MapRiverBuilder] 조절점 {controls.Count}개 : {controlText}");
        Debug.Log($"[MapRiverBuilder] 경로 점 {path.Points.Count}개 (간격 {SampleSpacing}m), 전체 길이 {path.Distances[path.Distances.Length - 1]:F0}m, " +
                  $"끝점 ({path.Points[path.Points.Count - 1].x:F1}, {path.Points[path.Points.Count - 1].y:F1})");
        Debug.Log($"[MapRiverBuilder] 거점(원점)에서 최소 거리 {baseDistance:F1}m ({path.Points[closest].x:F0}, {path.Points[closest].y:F0})");
        if (baseDistance < MinBaseDistance) Debug.LogWarning($"[MapRiverBuilder] 강이 거점에서 {MinBaseDistance}m보다 가깝습니다. 조절점을 확인하세요.");

        LogWater(path, lakeCenter.y, fallbackCount);
    }

    private static void LogWater(RiverPath path, float lakeHeight, int fallbackCount)
    {
        float[] waters = path.WaterHeights;
        bool isDecreasing = true;
        float steepestSlope = 0f;
        Vector2 steepestPosition = Vector2.zero;
        for (int i = 1; i < waters.Length; i++)
        {
            if (waters[i] > waters[i - 1]) isDecreasing = false;
            float slope = Mathf.Atan2(waters[i - 1] - waters[i], path.Distances[i] - path.Distances[i - 1]) * Mathf.Rad2Deg;
            if (slope <= steepestSlope) continue;

            steepestSlope = slope;
            steepestPosition = path.Points[i];
        }

        Debug.Log($"[MapRiverBuilder] 수면 시작 {waters[0]:F2}m (호수 수면 {lakeHeight:F2}m - {LakeOverlapDrop}m), 끝 {waters[waters.Length - 1]:F2}m, " +
                  $"하류로 단조 감소 {isDecreasing}");
        Debug.Log($"[MapRiverBuilder] 수면 최대 기울기 {steepestSlope:F1}도 ({steepestPosition.x:F0}, {steepestPosition.y:F0}). " +
                  $"양옆 기준점을 못 읽어 앞 점 값을 쓴 점 {fallbackCount}개");
    }

    private static void LogCarve(CarveStats stats, RiverPath path)
    {
        float[] widths = path.CarveHalfWidths;
        int widest = Enumerable.Range(0, widths.Length).OrderByDescending(i => widths[i]).First();
        int widenedCount = widths.Count(width => width > CarveHalfWidth);
        int cappedCount = widths.Count(width => width >= MaxCarveHalfWidth);
        Debug.Log($"[MapRiverBuilder] 고치는 범위 : 기본 {CarveHalfWidth}m, 넓힌 점 {widenedCount}개 / {widths.Length}개, " +
                  $"최대 {widths[widest]:F1}m ({path.Points[widest].x:F0}, {path.Points[widest].y:F0}), 상한 {MaxCarveHalfWidth}m에 닿은 점 {cappedCount}개");
        if (cappedCount > 0) Debug.LogWarning($"[MapRiverBuilder] 상한 {MaxCarveHalfWidth}m 안에서 비탈이 땅과 만나지 못한 점이 있어 그 범위 끝에 단차가 남을 수 있습니다.");

        Debug.Log($"[MapRiverBuilder] 지형 최대 깎음 -{stats.MaxCut:F2}m ({stats.MaxCutPosition.x:F0}, {stats.MaxCutPosition.y:F0}), " +
                  $"최대 메움 +{stats.MaxFill:F2}m ({stats.MaxFillPosition.x:F0}, {stats.MaxFillPosition.y:F0})");
        Debug.Log($"[MapRiverBuilder] 고치는 범위 끝(점마다 범위 - {EdgeCheckWidth}m ~ 범위) 최대 변화 {stats.MaxEdgeChange:F2}m " +
                  $"({stats.MaxEdgeChangePosition.x:F0}, {stats.MaxEdgeChangePosition.y:F0}). 0에 가까워야 범위 끝에 단차가 없다");
        string zoneText = string.Join(", ", Enumerable.Range(0, ZoneCells.Length).Select(zone =>
            $"{ZoneRegionNames[zone]} -{stats.ZoneMaxCuts[zone]:F2}m ({stats.ZoneMaxCutPositions[zone].x:F0}, {stats.ZoneMaxCutPositions[zone].y:F0})"));
        Debug.Log($"[MapRiverBuilder] 구역별 최대 깎음 : {zoneText}. North 값이 호수 남쪽 둑을 판 깊이다 " +
                  $"(깎은 비탈 {CutSlope}로 상한 범위 안에서 깎을 수 있는 최대 약 {(MaxCarveHalfWidth - CarveMargin - HalfWidth) * CutSlope:F1}m)");
    }

    // 메뉴 7만 다시 실행하면 어떻게 되는지, 방금 고친 지형으로 수면과 지형 계산을 한 번 더 해 본다 (지형과 메시는 바꾸지 않는다).
    // 수면 차이와 지형 변화가 0에 가까우면 메뉴 7만 다시 실행해도 같은 결과다
    private static void LogRerun(Terrain terrain, Vector3 lakeCenter, LakeFootprint lake, RiverPath path)
    {
        RiverPath again = CreatePath(terrain, lakeCenter, lake, out _);
        int waterIndex = Enumerable.Range(0, path.Points.Count)
            .OrderByDescending(i => Mathf.Abs(again.WaterHeights[i] - path.WaterHeights[i])).First();
        float waterDifference = Mathf.Abs(again.WaterHeights[waterIndex] - path.WaterHeights[waterIndex]);

        CalculateCarve(terrain, again, lake, out CarveStats rerun);
        bool isCut = rerun.MaxCut >= rerun.MaxFill;
        float change = isCut ? rerun.MaxCut : rerun.MaxFill;
        Vector2 changePosition = isCut ? rerun.MaxCutPosition : rerun.MaxFillPosition;
        Debug.Log($"[MapRiverBuilder] 메뉴 7만 다시 실행하면 : 수면 최대 차이 {waterDifference:F3}m " +
                  $"({path.Points[waterIndex].x:F0}, {path.Points[waterIndex].y:F0}), 지형 최대 변화 {change:F3}m ({changePosition.x:F0}, {changePosition.y:F0}). " +
                  "0에 가까우면 메뉴 7만 다시 실행해도 같은 결과다");
    }

    // 리본 가장자리(물가 밖 RibbonOverlap) 아래 지형이 그 점의 수면보다 얼마나 높은지 본다. 음수면 물 끝이 떠 보인다.
    // 호수 구간은 호수 수면이 덮으므로 뺀다
    private static void LogBanks(Terrain terrain, RiverPath path, LakeFootprint lake)
    {
        float minGap = float.MaxValue;
        Vector2 minPosition = Vector2.zero;
        int checkCount = 0;
        int belowCount = 0;
        float nextCheck = 0f;
        for (int i = 0; i < path.Points.Count; i++)
        {
            if (path.Distances[i] < nextCheck || IsInLake(lake, path.Points[i])) continue;

            nextCheck = path.Distances[i] + BankCheckInterval;
            Vector2 right = GetRight(path.Points, i) * (HalfWidth + RibbonOverlap);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector2 edge = path.Points[i] + right * side;
                float gap = SampleWorldHeight(terrain, edge) - path.WaterHeights[i];
                checkCount++;
                if (gap < 0f) belowCount++;
                if (gap >= minGap) continue;

                minGap = gap;
                minPosition = edge;
            }
        }
        Debug.Log($"[MapRiverBuilder] 강둑 검사 {checkCount}곳 ({BankCheckInterval}m 간격, 양쪽) : (지형 - 수면) 최소 {minGap:F2}m " +
                  $"({minPosition.x:F0}, {minPosition.y:F0}), 음수(물이 떠 보임) {belowCount}곳");
    }

    // 강이 지나는 구역 경계 줄(z = 중앙 칸 북쪽, 남쪽 끝)에서 경계 줄과 바로 옆 줄의 높이 차를 강 둘레만 비교한다.
    // 강은 구역과 상관없이 한 번에 고치므로 깎은 뒤 값은 그 자리 수면 기울기만큼만 나와야 한다
    private static void LogBoundaries(CarveArea area)
    {
        Rect center = MapGrid.GetRangeBounds("B2", "B2");
        foreach (float boundaryZ in new[] { center.yMax, center.yMin })
        {
            int row = Mathf.RoundToInt((boundaryZ - area.Origin.y) / area.Spacing);
            if (row < 1 || row >= area.Original.GetLength(0) - 1) continue;

            float before = 0f;
            float after = 0f;
            int closest = 0;
            for (int x = 0; x < area.Original.GetLength(1); x++)
            {
                if (area.PathDistances[row, x] >= area.CarveWidths[row, x]) continue;

                before = Mathf.Max(before, GetRowStep(area.Original, row, x));
                after = Mathf.Max(after, GetRowStep(area.Heights, row, x));
                if (area.PathDistances[row, x] < area.PathDistances[row, closest]) closest = x;
            }
            Debug.Log($"[MapRiverBuilder] 구역 경계 z {boundaryZ:F0} (강 중심 x {area.Origin.x + closest * area.Spacing:F0}, 수면 {area.WaterHeights[row, closest]:F2}m) : " +
                      $"옆 줄과의 최대 높이 차 깎기 전 {before:F2}m, 후 {after:F2}m (줄 간격 {area.Spacing:F2}m)");
        }
    }

    private static float GetRowStep(float[,] heights, int row, int x)
    {
        return Mathf.Max(Mathf.Abs(heights[row + 1, x] - heights[row, x]), Mathf.Abs(heights[row, x] - heights[row - 1, x]));
    }
}
