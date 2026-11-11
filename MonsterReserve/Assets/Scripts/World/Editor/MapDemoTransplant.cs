using System.Text;
using UnityEditor;
using UnityEngine;

// 메뉴 5-1 : Dreamscape 데모 지형(DemoScene_V2.0의 Main Terrain)의 높이맵 일부를 북쪽 줄 가운데에 옮겨 심는다 (이슈 #5 방향 변경, 1단계).
// 데모에서 WindowWidth × WindowDepth 창을 골라 크기 그대로(1:1), 위에서 볼 때 180도 돌려(x, z 모두 뒤집기, 거울 반전 아님) B1 가운데 기준으로 놓는다.
// 그래서 데모 창의 북쪽 끝(낮은 호수 쪽)이 우리 중앙과 맞닿고, 데모 창의 남쪽(높은 봉우리)이 맵 북쪽 끝으로 간다.
// 데모 높이에서 HeightOffset을 빼 바탕 높이에 맞추되, 호수 수면이 MinLakeSurfaceHeight 이상 남아 호수 → 중앙 → 남쪽으로 강이 내리막으로 흐를 수 있게 한다.
// 남쪽 경계 줄은 바탕 높이와 정확히 같게 이어 붙이고, 창 밖 동서(A1, C1 바깥쪽)는 데모 지형을 거울처럼 접어 이어 가며 낮은 언덕으로 낮춘다.
// 데모 에셋은 읽기만 한다. 2단계 이후(호수, 오브젝트, 나무, 지면 칠 이식)는 DemoToGame, DemoRotationToGame, IsInsideWindow, HeightOffset으로 같은 변환을 쓴다.
// 이 메뉴는 메뉴 5(MapNorthShaper)를 대신한다. 호수(메뉴 6)와 강(메뉴 7)은 GetLakeCenter로 이 지형의 호수 위치를 쓴다.
public static class MapDemoTransplant
{
    // 데모 지형. DemoTerrain_V2.0.asset이 아니라 데모 씬의 Main Terrain이 쓰는 DemoTerrain.asset을 읽는다 (지면 칠 이식, 메뉴 9-2도 읽는다)
    public const string DemoTerrainDataPath = "Assets/Imports/Polyart/PolyartStudio/DreamscapeMountains/Scenes/DemoTerrain.asset";
    // 데모 씬 Main Terrain 위치. 데모 월드 좌표에서 이 값을 빼면 데모 높이맵 기준 좌표가 된다
    private static readonly Vector3 DemoTerrainPosition = Vector3.zero;

    // 데모 호수 (Prefab_WaterLakeMountains) 월드 위치. 씬 파일에서 부모 "Scene"(루트, 위치 466.695, 77.775, 642.343, 회전 없음, 크기 1)의
    // 위치에 로컬 위치(245.5, -12.4, 283.1)를 더한 값이다. y가 수면 높이다
    public static readonly Vector3 DemoLakePosition = new Vector3(712.195f, 65.375f, 925.443f);
    // 물 평면 반폭 (메시 20m × 크기 30.739 / 2). 이 사각형 안에서 지형이 수면보다 낮은 곳만 물로 보인다.
    // 호수(메뉴 6)가 물을 찾는 범위의 기준으로도 쓴다
    public const float DemoLakeHalfSize = 307.4f;

    private const string NorthFromCell = "A1";
    private const string NorthToCell = "C1";
    // 창의 동서 가운데를 맞출 칸
    private const string WindowCenterCell = "B1";

    // 창 크기 (임시값). 동서는 데모 폭(1500m)과 같아 동서 위치는 고를 것이 없고, 남북은 북쪽 줄 깊이(1000m)와 같다
    public const float WindowWidth = 1500f;
    public const float WindowDepth = 1000f;
    // 데모에서 창의 서쪽 끝 (창 폭 = 데모 폭이라 0)
    private const float DemoWindowStartX = 0f;
    // 창의 데모 남쪽 끝 후보 간격 (데모 z 0 ~ 데모 깊이 - 창 깊이)
    private const float WindowSearchStep = 20f;
    // 데모 창 → 우리 창 회전 (위에서 볼 때 180도). 위치 변환(DemoToGame)과 회전 변환(DemoRotationToGame)이 같은 회전을 쓴다
    private static readonly Quaternion WindowRotation = Quaternion.Euler(0f, 180f, 0f);

    // 남쪽 경계에서 데모 높이로 이어 붙이는 폭의 후보 (임시값). 가장 좁은 폭부터 넓혀 가며,
    // 이어 붙이는 띠의 평균 높이 차로 생기는 벽의 예상 경사가 MaxBlendWallSlope 이하인 첫 폭을 쓴다. 넓을수록 호수가 띠와 겹치기 쉽다
    private const float MinSouthBlendWidth = 150f;
    private const float MaxSouthBlendWidth = 400f;
    private const float SouthBlendWidthStep = 50f;
    // 이어 붙이는 띠에서 허용하는 벽의 예상 경사 (임시값, 기슭 제한 각도와 같게 둠)
    private const float MaxBlendWallSlope = 35f;
    // SmoothStep의 최대 기울기 (0 → 1을 폭 1에 걸쳐 오를 때 가운데에서 1.5배). 벽 예상 경사 = atan(1.5 × 높이 차 / 폭)
    private const float SmoothStepMaxSlope = 1.5f;

    // 호수 수면의 최저 높이 (임시값). 우리 중앙 바탕(30m)보다 높아야 호수 → 중앙 → 남쪽 맵 끝으로 강이 내리막으로 흐른다
    public const float MinLakeSurfaceHeight = 38f;
    // 창 동서 가장자리 밖에서 낮은 언덕으로 낮아지는 폭 (임시값)
    public const float EdgeFadeWidth = 400f;
    // 낮아진 뒤 남기는 데모 지형 높이 비율 (바탕 높이 위 높이 기준, 임시값). 0이면 바탕 평지, 1이면 데모 높이 그대로
    private const float OuterHillRatio = 0.15f;

    // 높이 맞추기 한계. 최고점이 Terrain 높이 한도(400m)를 넘지 않고, 최저점(데모 호수 바닥)이 0m 아래로 잘리지 않게 한다
    private const float MaxResultHeight = 395f;
    private const float MinResultHeight = 1f;

    // 기슭 경사 제한 적용 여부 (사용자 결정 : 기슭 160m 미만 최대 35도).
    // 데모 모양 유지가 우선이라 기본은 끈다. 꺼도 제한했을 때의 변경량은 항상 계산해 로그로 남기므로, 로그를 보고 켤지 정한다
    private const bool ShouldLimitFoothillSlope = false;
    // 아래 기슭 경사 제한 값은 MapNorthShaper와 같다 (같은 규칙을 이식 결과에 적용하기 위해)
    private const float FoothillMaxHeight = 160f;
    private const float FoothillMaxSlope = 35f;
    // 이웃 칸 높이 차를 제한해도 가운데 차분 경사는 대각선 때문에 최대 이 배율만큼 커질 수 있어, 이웃 사이 제한을 이만큼 낮춘다
    private static readonly float StencilSlopeFactor = Mathf.Sqrt(4f - 2f * Mathf.Sqrt(2f));
    // 제한이 풀리는 높이 폭과 그 위 제한 각도 (기슭 경계에 단이 생기지 않게 서서히 푼다)
    private const float LimitTransitionHeight = 40f;
    private const float NoLimitSlope = 89f;
    // 제한이 풀리기 시작하는 높이를 노이즈로 0 ~ 이만큼 올린다. 메운 산자락 윗끝이 한 등고선으로 줄지어 보이지 않게 한다
    private const float LimitJitter = 15f;
    private const float LimitJitterPeriod = 80f;
    private const int LimitNoiseSeed = 20261113;
    // Perlin 입력이 음수가 되지 않도록 노이즈 위치를 양수 쪽으로 옮기는 범위
    private const float NoiseOffsetMin = 100f;
    private const float NoiseOffsetMax = 1000f;
    // 경사 제한 반복 계산 : 한 번의 순방향 + 역방향 패스에서 바뀐 높이가 이 값 이하면 끝낸다
    private const float RelaxTolerance = 0.0001f;
    private const int MaxRelaxPasses = 200;
    // 경사 검사 허용 오차, 바뀐 칸으로 세는 최소 높이 차
    private const float SlopeCheckTolerance = 0.01f;
    private const float ChangeThreshold = 0.01f;
    private const float SquareMetersPerHectare = 10000f;

    // 순방향 패스에서 이미 지나온 이웃 (x, z). 역방향 패스는 부호를 뒤집는다
    private static readonly Vector2Int[] RelaxNeighbors =
    {
        new Vector2Int(-1, 0), new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1)
    };

    // 데모 높이맵 (미터 단위, 데모 높이맵 기준 좌표)
    private struct DemoHeights
    {
        public float[,] Heights;
        public int Resolution;
        public float SpacingX;
        public float SpacingZ;
        public float Depth;
    }

    // 데모 높이맵 한 줄(같은 z)의 평균, 최고, 최저
    private struct RowStats
    {
        public float[] Mean;
        public float[] Max;
        public float[] Min;
    }

    private struct WindowCandidate
    {
        public float StartZ;
        public float Mean;
        public float SouthBandMean;
        public float Max;
        public float Min;
        public bool HasLake;
        public float Score;
    }

    // 고른 창 (StartZ는 데모 z, 창의 데모 남쪽 끝)과 이어 붙이는 폭, 높이 오프셋, 오프셋을 정한 근거
    private struct DemoWindow
    {
        public float StartZ;
        public float BlendWidth;
        public float SouthBandMean;
        public float HeightOffset;
        public float OffsetTarget;
        public float OffsetLower;
        public float OffsetUpperBottom;
        public float OffsetUpperLake;
        public float EstimatedWallSlope;
    }

    private struct WindowSelection
    {
        public WindowCandidate[] Candidates;
        public int BestIndex;
        public bool HasLakeWindow;
        public Vector2 LakeRangeZ;
        public int LakeCellCount;
        public DemoWindow Window;
        public string BlendTrials;
    }

    private struct FoothillSlope
    {
        public float Max;
        public float SteepRatio;
    }

    // 창 선택은 데모 에셋만으로 정해져 항상 같은 결과가 나오므로, 한 번 계산한 값을 다시 쓴다
    private static DemoWindow? _window;

    // 데모 높이에서 빼는 값 (데모 높이 - HeightOffset = 우리 Terrain 기준 높이)
    public static float HeightOffset => GetWindow().HeightOffset;
    // 데모에서 창의 남쪽 끝 z (데모 월드 좌표). 180도 회전이라 이 끝은 우리 창의 북쪽 끝이 된다
    public static float WindowStartZ => GetWindow().StartZ;
    // 남쪽 경계에서 데모 높이로 이어 붙이는 폭 (고른 값). 2단계 이후 우리 z 남쪽 끝 ~ + 이 폭 안의 오브젝트는 지형 높이로 다시 맞춰야 한다
    public static float SouthBlendWidth => GetWindow().BlendWidth;

    // 창이 놓이는 우리 월드 XZ 범위 (x = 월드 X, y = 월드 Z)
    public static Rect GetGameWindowRect()
    {
        float centerX = MapGrid.GetCellCenter(WindowCenterCell).x;
        float southZ = MapGrid.GetRangeBounds(NorthFromCell, NorthToCell).yMin;
        return new Rect(centerX - WindowWidth / 2f, southZ, WindowWidth, WindowDepth);
    }

    // 데모 월드 좌표가 고른 창 안인지 (높이는 보지 않는다)
    public static bool IsInsideWindow(Vector3 demoPosition)
    {
        DemoWindow window = GetWindow();
        Vector3 local = demoPosition - DemoTerrainPosition;
        return local.x >= DemoWindowStartX && local.x <= DemoWindowStartX + WindowWidth
            && local.z >= window.StartZ && local.z <= window.StartZ + WindowDepth;
    }

    // 데모 월드 좌표 → 우리 월드 좌표 (우리 Terrain y = 0 기준, MapTerrainSetup). 창 가운데를 기준으로 180도 돌린다.
    // 높이는 데모 높이에서 HeightOffset을 뺀 값이라, 남쪽 이어 붙이는 폭과 창 밖에서는 실제 지형 높이와 다르다
    public static Vector3 DemoToGame(Vector3 demoPosition)
    {
        return DemoToGame(demoPosition, GetWindow());
    }

    // 호수 중심과 수면 높이 (x, z = 월드 XZ, y = 월드 수면 높이). 호수(메뉴 6)와 강(메뉴 7, GetPath)이 함께 쓰는 단 하나의 정의다.
    // 데모 물 평면의 중심을 옮긴 값이라 중심 아래 지형이 물 밖일 수 있다 (호수는 물 칸을 따로 찾는다).
    // 호수 자리는 남쪽 이어 붙이는 폭 밖이라 데모 높이 - HeightOffset이 실제 지형과 같다
    public static Vector3 GetLakeCenter(Terrain terrain)
    {
        Vector3 lake = DemoToGame(DemoLakePosition);
        lake.y += terrain.transform.position.y;
        return lake;
    }

    // 데모 월드 회전 → 우리 월드 회전 (위에서 볼 때 180도를 더한다). 데모 오브젝트를 옮길 때 DemoToGame과 함께 쓴다
    public static Quaternion DemoRotationToGame(Quaternion demoRotation)
    {
        return WindowRotation * demoRotation;
    }

    [MenuItem("Tools/Map/5-1. 북쪽 지형 (데모 이식)")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        DemoHeights demo = LoadDemoHeights();
        WindowSelection selection = SelectWindow(demo);
        _window = selection.Window;
        LogSelection(selection);

        TerrainData data = terrain.terrainData;
        int divisions = data.heightmapResolution - 1;
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, MapGrid.GetRangeBounds(NorthFromCell, NorthToCell), divisions);
        float sampleSpacing = data.size.x / divisions;
        Vector2[,] positions = CreateSamplePositions(terrain, range, sampleSpacing);

        float[,] transplanted = CalculateHeights(positions, demo, selection.Window);
        float[,] heights = ApplyFoothillLimit(transplanted, positions, selection.Window, sampleSpacing);

        LogResult(heights, positions, selection.Window);
        data.SetHeights(range.x, range.y, ToNormalized(heights, data.size.y));
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
    }

    private static DemoWindow GetWindow()
    {
        _window ??= SelectWindow(LoadDemoHeights()).Window;
        return _window.Value;
    }

    // 180도 회전 : 데모 창의 동쪽 끝 → 우리 창 서쪽 끝, 데모 창의 북쪽 끝 → 우리 창 남쪽 끝
    private static Vector3 DemoToGame(Vector3 demoPosition, DemoWindow window)
    {
        Rect gameWindow = GetGameWindowRect();
        Vector3 local = demoPosition - DemoTerrainPosition;
        return new Vector3(
            gameWindow.xMin + (DemoWindowStartX + WindowWidth - local.x),
            local.y - window.HeightOffset,
            gameWindow.yMin + (window.StartZ + WindowDepth - local.z));
    }

    // 우리 창 기준 위치(서쪽 끝, 남쪽 끝에서 잰 거리) → 데모 높이맵 기준 좌표. DemoToGame의 역변환이다
    private static Vector2 GameLocalToDemo(float gameLocalX, float gameLocalZ, DemoWindow window)
    {
        return new Vector2(DemoWindowStartX + WindowWidth - gameLocalX, window.StartZ + WindowDepth - gameLocalZ);
    }

    // 데모 z → 우리 월드 z (180도 회전)
    private static float DemoZToGame(float demoLocalZ, DemoWindow window)
    {
        return GetGameWindowRect().yMin + (window.StartZ + WindowDepth - demoLocalZ);
    }

    // 데모 높이맵을 미터 단위로 한 번에 읽는다 (칸마다 TerrainData에 묻지 않기 위해)
    private static DemoHeights LoadDemoHeights()
    {
        TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(DemoTerrainDataPath);
        int resolution = data.heightmapResolution;
        float[,] heights = data.GetHeights(0, 0, resolution, resolution);
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
                heights[z, x] *= data.size.y;
        }
        return new DemoHeights
        {
            Heights = heights,
            Resolution = resolution,
            SpacingX = data.size.x / (resolution - 1),
            SpacingZ = data.size.z / (resolution - 1),
            Depth = data.size.z
        };
    }

    // 데모 간격(약 0.73 × 0.71m)과 우리 간격(약 1.22m)이 달라 쌍선형 보간으로 읽는다
    private static float SampleDemoHeight(DemoHeights demo, float localX, float localZ)
    {
        int last = demo.Resolution - 1;
        float gridX = Mathf.Clamp(localX / demo.SpacingX, 0f, last);
        float gridZ = Mathf.Clamp(localZ / demo.SpacingZ, 0f, last);
        int x0 = Mathf.Min((int)gridX, last - 1);
        int z0 = Mathf.Min((int)gridZ, last - 1);
        float tx = gridX - x0;
        float tz = gridZ - z0;
        float south = Mathf.Lerp(demo.Heights[z0, x0], demo.Heights[z0, x0 + 1], tx);
        float north = Mathf.Lerp(demo.Heights[z0 + 1, x0], demo.Heights[z0 + 1, x0 + 1], tx);
        return Mathf.Lerp(south, north, tz);
    }

    // 이어 붙이는 폭을 MinSouthBlendWidth부터 넓혀 가며 폭마다 창을 고르고, 벽 예상 경사가 MaxBlendWallSlope 이하인 첫 폭을 쓴다.
    // 그런 폭이 없으면 벽 예상 경사가 가장 낮은 폭을 쓰고 경고한다. 호수가 띠와 겹치기 시작한 폭부터는 더 넓혀도 겹치므로 멈춘다
    private static WindowSelection SelectWindow(DemoHeights demo)
    {
        RowStats rows = CalculateRowStats(demo);
        Vector2 lakeRange = FindLakeWaterRangeZ(demo, out int lakeCellCount);
        var trials = new StringBuilder("[MapDemoTransplant] 이어 붙이는 폭 후보 (폭마다 고른 창, 남쪽 띠 평균, 오프셋, 벽 예상 경사)\n");
        WindowSelection best = default;
        bool isFound = false;
        for (float blendWidth = MinSouthBlendWidth; blendWidth <= MaxSouthBlendWidth; blendWidth += SouthBlendWidthStep)
        {
            WindowSelection selection = SelectWindow(rows, demo, lakeRange, lakeCellCount, blendWidth);
            if (!selection.HasLakeWindow)
            {
                trials.AppendLine($"  폭 {blendWidth:F0}m : 호수가 창 안, 남쪽 띠 밖에 모두 들어오는 창이 없다 (여기서 멈춤)");
                break;
            }

            DemoWindow window = selection.Window;
            bool isPass = window.EstimatedWallSlope <= MaxBlendWallSlope;
            trials.AppendLine($"  폭 {blendWidth:F0}m : 데모 z {window.StartZ:F0} ~ {window.StartZ + WindowDepth:F0}, 남쪽 띠 평균 {window.SouthBandMean:F1}m(데모), " +
                              $"오프셋 {window.HeightOffset:F2}m, 띠 평균 결과 높이 {window.SouthBandMean - window.HeightOffset:F1}m, 벽 예상 경사 {window.EstimatedWallSlope:F1}도{(isPass ? " (통과)" : "")}");
            if (isPass || !isFound || window.EstimatedWallSlope < best.Window.EstimatedWallSlope)
            {
                best = selection;
                isFound = true;
            }
            if (isPass) break;
        }

        if (!isFound)
        {
            Debug.LogWarning("[MapDemoTransplant] 데모 호수가 창 안(남쪽 띠 밖)에 모두 들어오는 후보가 없어 가장 좁은 폭에서 점수만으로 골랐다. 호수가 잘리거나 남쪽 띠에 걸린다");
            best = SelectWindow(rows, demo, lakeRange, lakeCellCount, MinSouthBlendWidth);
        }
        else if (best.Window.EstimatedWallSlope > MaxBlendWallSlope)
        {
            Debug.LogWarning($"[MapDemoTransplant] 벽 예상 경사가 {MaxBlendWallSlope}도 이하인 폭이 없어 가장 낮은 폭({best.Window.BlendWidth:F0}m, {best.Window.EstimatedWallSlope:F1}도)을 썼다");
        }
        best.BlendTrials = trials.ToString();
        return best;
    }

    // 한 이어 붙이는 폭에서 창 고르기. 기준 : 창 평균 높이 - 남쪽 이어 붙이는 띠의 평균 높이 (점수)가 가장 큰 곳.
    // 180도 회전이라 우리 남쪽 띠는 데모 창의 북쪽 끝이다. 남쪽 띠가 낮고 안쪽이 높을수록 "남쪽에서 산이 크게 솟는" 창이고, 이어 붙일 때 바뀌는 양도 적다.
    // 데모 호수의 물(수면보다 낮은 곳)이 창 안, 남쪽 띠 밖에 모두 들어오는 후보만 고른다. 없으면 점수만으로 고르고 HasLakeWindow = false
    private static WindowSelection SelectWindow(RowStats rows, DemoHeights demo, Vector2 lakeRange, int lakeCellCount, float blendWidth)
    {
        int count = Mathf.FloorToInt((demo.Depth - WindowDepth) / WindowSearchStep) + 1;
        var candidates = new WindowCandidate[count];
        int bestIndex = -1;
        int bestAnyIndex = 0;
        for (int i = 0; i < count; i++)
        {
            candidates[i] = CreateCandidate(rows, demo, i * WindowSearchStep, blendWidth, lakeRange, lakeCellCount);
            if (candidates[i].Score > candidates[bestAnyIndex].Score) bestAnyIndex = i;
            if (!candidates[i].HasLake) continue;
            if (bestIndex < 0 || candidates[i].Score > candidates[bestIndex].Score) bestIndex = i;
        }
        bool hasLakeWindow = bestIndex >= 0;
        if (!hasLakeWindow) bestIndex = bestAnyIndex;
        return new WindowSelection
        {
            Candidates = candidates,
            BestIndex = bestIndex,
            HasLakeWindow = hasLakeWindow,
            LakeRangeZ = lakeRange,
            LakeCellCount = lakeCellCount,
            Window = CreateWindow(candidates[bestIndex], blendWidth)
        };
    }

    // 창 폭이 데모 폭과 같아, 줄마다 미리 구해 두면 후보마다 줄 값만 모으면 된다
    private static RowStats CalculateRowStats(DemoHeights demo)
    {
        int resolution = demo.Resolution;
        var rows = new RowStats { Mean = new float[resolution], Max = new float[resolution], Min = new float[resolution] };
        for (int z = 0; z < resolution; z++)
        {
            float sum = 0f;
            float max = float.MinValue;
            float min = float.MaxValue;
            for (int x = 0; x < resolution; x++)
            {
                float height = demo.Heights[z, x];
                sum += height;
                max = Mathf.Max(max, height);
                min = Mathf.Min(min, height);
            }
            rows.Mean[z] = sum / resolution;
            rows.Max[z] = max;
            rows.Min[z] = min;
        }
        return rows;
    }

    // 데모 호수 물 평면 안에서 지형이 수면보다 낮은(물이 보이는) 곳의 데모 z 범위 (x = 최소, y = 최대)
    private static Vector2 FindLakeWaterRangeZ(DemoHeights demo, out int cellCount)
    {
        Vector3 lake = DemoLakePosition - DemoTerrainPosition;
        int xFrom = ToDemoIndex(lake.x - DemoLakeHalfSize, demo.SpacingX, demo.Resolution);
        int xTo = ToDemoIndex(lake.x + DemoLakeHalfSize, demo.SpacingX, demo.Resolution);
        int zFrom = ToDemoIndex(lake.z - DemoLakeHalfSize, demo.SpacingZ, demo.Resolution);
        int zTo = ToDemoIndex(lake.z + DemoLakeHalfSize, demo.SpacingZ, demo.Resolution);
        var range = new Vector2(float.MaxValue, float.MinValue);
        cellCount = 0;
        for (int z = zFrom; z <= zTo; z++)
        {
            for (int x = xFrom; x <= xTo; x++)
            {
                if (demo.Heights[z, x] >= lake.y) continue;
                cellCount++;
                range.x = Mathf.Min(range.x, z * demo.SpacingZ);
                range.y = Mathf.Max(range.y, z * demo.SpacingZ);
            }
        }
        return range;
    }

    private static int ToDemoIndex(float local, float spacing, int resolution)
    {
        return Mathf.Clamp(Mathf.RoundToInt(local / spacing), 0, resolution - 1);
    }

    // startZ는 창의 데모 남쪽 끝. 180도 회전이라 우리 남쪽 띠는 데모 z (startZ + 창 깊이 - 이어 붙이는 폭) ~ (startZ + 창 깊이)이다
    private static WindowCandidate CreateCandidate(RowStats rows, DemoHeights demo, float startZ, float blendWidth, Vector2 lakeRange, int lakeCellCount)
    {
        // 쌍선형 보간이 창 끝에서 바깥 줄도 읽으므로 끝 줄은 넉넉하게 넣는다
        int last = demo.Resolution - 1;
        int rowFrom = Mathf.Clamp(Mathf.FloorToInt(startZ / demo.SpacingZ), 0, last);
        int rowTo = Mathf.Clamp(Mathf.CeilToInt((startZ + WindowDepth) / demo.SpacingZ), 0, last);
        int bandFrom = Mathf.Clamp(Mathf.FloorToInt((startZ + WindowDepth - blendWidth) / demo.SpacingZ), 0, last);

        var candidate = new WindowCandidate { StartZ = startZ, Max = float.MinValue, Min = float.MaxValue };
        float sum = 0f;
        float bandSum = 0f;
        for (int z = rowFrom; z <= rowTo; z++)
        {
            sum += rows.Mean[z];
            if (z >= bandFrom) bandSum += rows.Mean[z];
            candidate.Max = Mathf.Max(candidate.Max, rows.Max[z]);
            candidate.Min = Mathf.Min(candidate.Min, rows.Min[z]);
        }
        candidate.Mean = sum / (rowTo - rowFrom + 1);
        candidate.SouthBandMean = bandSum / (rowTo - bandFrom + 1);
        candidate.Score = candidate.Mean - candidate.SouthBandMean;
        candidate.HasLake = lakeCellCount > 0 && lakeRange.x >= startZ && lakeRange.y <= startZ + WindowDepth - blendWidth;
        return candidate;
    }

    // 남쪽 띠 평균이 바탕 높이가 되도록 빼는 값이 목표. 띠 안에서 올리는 양과 내리는 양이 비슷해져 이어 붙일 때 바뀌는 양이 가장 적다.
    // 위 한계 두 개 : 최저점이 MinResultHeight 아래로 내려가지 않게, 호수 수면이 MinLakeSurfaceHeight 아래로 내려가지 않게.
    // 아래 한계 : 최고점이 MaxResultHeight를 넘지 않게
    private static DemoWindow CreateWindow(WindowCandidate candidate, float blendWidth)
    {
        float target = candidate.SouthBandMean - MapTerrainSetup.BaseHeight;
        float lower = candidate.Max - MaxResultHeight;
        float upperBottom = candidate.Min - MinResultHeight;
        float upperLake = DemoLakePosition.y - DemoTerrainPosition.y - MinLakeSurfaceHeight;
        float upper = Mathf.Min(upperBottom, upperLake);
        // 한계가 엇갈리면 Terrain 높이 한도(최고점)를 지키는 쪽을 고른다 (로그로 알린다)
        float offset = lower > upper ? lower : Mathf.Clamp(target, lower, upper);
        // 띠 평균 높이 차가 SmoothStep으로 이어질 때 가장 가파른 곳의 경사
        float bandRise = Mathf.Abs(candidate.SouthBandMean - offset - MapTerrainSetup.BaseHeight);
        float wallSlope = Mathf.Atan(SmoothStepMaxSlope * bandRise / blendWidth) * Mathf.Rad2Deg;
        return new DemoWindow
        {
            StartZ = candidate.StartZ,
            BlendWidth = blendWidth,
            SouthBandMean = candidate.SouthBandMean,
            HeightOffset = offset,
            OffsetTarget = target,
            OffsetLower = lower,
            OffsetUpperBottom = upperBottom,
            OffsetUpperLake = upperLake,
            EstimatedWallSlope = wallSlope
        };
    }

    // 높이맵은 꼭짓점 기준이라 끝 줄까지 포함한다 (남쪽 첫 줄을 바탕 높이로 고정한다)
    private static Vector2[,] CreateSamplePositions(Terrain terrain, RectInt range, float sampleSpacing)
    {
        Vector3 position = terrain.transform.position;
        var positions = new Vector2[range.height + 1, range.width + 1];
        for (int z = 0; z <= range.height; z++)
        {
            for (int x = 0; x <= range.width; x++)
                positions[z, x] = new Vector2(position.x + (range.x + x) * sampleSpacing, position.z + (range.y + z) * sampleSpacing);
        }
        return positions;
    }

    private static float[,] CalculateHeights(Vector2[,] positions, DemoHeights demo, DemoWindow window)
    {
        Rect gameWindow = GetGameWindowRect();
        float southZ = positions[0, 0].y;
        int depth = positions.GetLength(0);
        int width = positions.GetLength(1);
        var heights = new float[depth, width];
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                float height = SampleTransplant(positions[z, x], demo, window, gameWindow);
                // 남쪽 첫 줄에서 정확히 바탕 높이이고, 기울기도 0에서 시작해 중앙과 꺾임 없이 이어진다
                float blend = Mathf.SmoothStep(0f, 1f, (positions[z, x].y - southZ) / window.BlendWidth);
                heights[z, x] = Mathf.Lerp(MapTerrainSetup.BaseHeight, height, blend);
            }
        }
        return heights;
    }

    // 창 안은 데모 높이 그대로. 창 밖 동서는 가장자리에서 거울처럼 접어 읽어 높이가 끊기지 않게 하고,
    // EdgeFadeWidth에 걸쳐 바탕 위 높이를 OuterHillRatio까지 줄여 산맥이 낮은 언덕으로 잦아들게 한다.
    // 접기는 우리 창 기준으로 한 뒤 180도 돌려 데모 좌표로 바꾼다
    private static float SampleTransplant(Vector2 world, DemoHeights demo, DemoWindow window, Rect gameWindow)
    {
        float localX = world.x - gameWindow.xMin;
        float localZ = world.y - gameWindow.yMin;
        float outside = 0f;
        if (localX < 0f)
        {
            outside = -localX;
            localX = -localX;
        }
        else if (localX > WindowWidth)
        {
            outside = localX - WindowWidth;
            localX = WindowWidth * 2f - localX;
        }

        Vector2 demoLocal = GameLocalToDemo(localX, localZ, window);
        float height = SampleDemoHeight(demo, demoLocal.x, demoLocal.y) - window.HeightOffset;
        if (outside <= 0f) return height;

        float ratio = Mathf.Lerp(1f, OuterHillRatio, Mathf.SmoothStep(0f, 1f, outside / EdgeFadeWidth));
        return MapTerrainSetup.BaseHeight + (height - MapTerrainSetup.BaseHeight) * ratio;
    }

    // 기슭 경사 제한은 적용 여부와 상관없이 계산해 데모 대비 변경량을 로그로 남기고, ShouldLimitFoothillSlope일 때만 결과에 쓴다
    private static float[,] ApplyFoothillLimit(float[,] transplanted, Vector2[,] positions, DemoWindow window, float sampleSpacing)
    {
        bool[,] pins = CreatePins(transplanted, positions, window);
        float[,] limited = LimitFoothillSlope(transplanted, positions, pins, sampleSpacing);
        LogLimitResult(transplanted, limited, pins, sampleSpacing);
        return ShouldLimitFoothillSlope ? limited : transplanted;
    }

    // 경사 제한에서 높이를 바꾸지 않는 칸 : 남쪽 경계 줄, 데모 호수 물 평면 안에서 수면보다 낮은 곳 (호수 모양과 물가를 지킨다)
    private static bool[,] CreatePins(float[,] heights, Vector2[,] positions, DemoWindow window)
    {
        Vector3 lake = DemoToGame(DemoLakePosition, window);
        var lakeArea = new Rect(lake.x - DemoLakeHalfSize, lake.z - DemoLakeHalfSize, DemoLakeHalfSize * 2f, DemoLakeHalfSize * 2f);
        int depth = heights.GetLength(0);
        int width = heights.GetLength(1);
        var pins = new bool[depth, width];
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
                pins[z, x] = z == 0 || (lakeArea.Contains(positions[z, x]) && heights[z, x] < lake.y);
        }
        return pins;
    }

    // MapNorthShaper.LimitFoothillSlope와 같은 방식. 고정 칸에서 제한 기울기로 오르는 위 한계보다 높은 곳만 깎고, 나머지는 낮은 쪽을 메운다.
    // 높은 곳은 내려가지 않으므로 정상, 능선은 그대로 남고, 가파른 비탈 아래에 산자락이 붙는다
    private static float[,] LimitFoothillSlope(float[,] heights, Vector2[,] positions, bool[,] pins, float sampleSpacing)
    {
        float foothillTan = Mathf.Tan(FoothillMaxSlope * Mathf.Deg2Rad) / StencilSlopeFactor;
        // 제한 각도를 정할 높이. 깎인 뒤 높이보다 높게 잡히지 않도록 기슭 기울기로만 깎은 위 한계로 먼저 낮춘다
        float[,] limitBase = CreateCeiling(heights, pins, CreateFilled(heights, foothillTan), sampleSpacing, "기슭 기울기 위 한계");
        MinInPlace(limitBase, heights);
        float[,] limits = CreateSlopeLimits(limitBase, positions, sampleSpacing, foothillTan);

        float[,] limited = CreateCeiling(heights, pins, limits, sampleSpacing, "고정 칸 위 한계");
        MinInPlace(limited, heights);
        Relax(limited, limits, sampleSpacing, true, "메우기");
        return limited;
    }

    private static float[,] CreateFilled(float[,] source, float value)
    {
        var filled = new float[source.GetLength(0), source.GetLength(1)];
        for (int z = 0; z < filled.GetLength(0); z++)
        {
            for (int x = 0; x < filled.GetLength(1); x++)
                filled[z, x] = value;
        }
        return filled;
    }

    // 고정 칸 높이에서 제한 기울기로 올라가는 위 한계 (고정 칸이 아닌 곳은 무한대에서 시작해 내려온다)
    private static float[,] CreateCeiling(float[,] heights, bool[,] pins, float[,] limits, float sampleSpacing, string label)
    {
        var ceiling = new float[heights.GetLength(0), heights.GetLength(1)];
        for (int z = 0; z < ceiling.GetLength(0); z++)
        {
            for (int x = 0; x < ceiling.GetLength(1); x++)
                ceiling[z, x] = pins[z, x] ? heights[z, x] : float.PositiveInfinity;
        }
        Relax(ceiling, limits, sampleSpacing, false, label);
        return ceiling;
    }

    private static void MinInPlace(float[,] target, float[,] other)
    {
        for (int z = 0; z < target.GetLength(0); z++)
        {
            for (int x = 0; x < target.GetLength(1); x++)
                target[z, x] = Mathf.Min(target[z, x], other[z, x]);
        }
    }

    // 칸마다 이웃과의 허용 기울기(tan). 기슭과 그 바로 위는 기슭 제한, 그 위 LimitTransitionHeight 폭에서 사실상 제한 없음까지 커진다
    private static float[,] CreateSlopeLimits(float[,] limitBase, Vector2[,] positions, float sampleSpacing, float foothillTan)
    {
        Vector2 offset = CreateNoiseOffset(LimitNoiseSeed);
        float foothillAngle = Mathf.Atan(foothillTan) * Mathf.Rad2Deg;
        // 기슭 칸의 이웃도 모두 기슭 제한을 받도록, 제한이 풀리기 시작하는 높이를 이웃 하나만큼 위에 둔다
        float startMargin = foothillTan * sampleSpacing * Mathf.Sqrt(2f);
        var limits = new float[limitBase.GetLength(0), limitBase.GetLength(1)];
        for (int z = 0; z < limits.GetLength(0); z++)
        {
            for (int x = 0; x < limits.GetLength(1); x++)
            {
                Vector2 world = positions[z, x];
                float noise = Mathf.PerlinNoise(world.x / LimitJitterPeriod + offset.x, world.y / LimitJitterPeriod + offset.y);
                float start = FoothillMaxHeight + startMargin + Mathf.Clamp01(noise) * LimitJitter;
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, start + LimitTransitionHeight, limitBase[z, x]));
                limits[z, x] = Mathf.Tan(Mathf.Lerp(foothillAngle, NoLimitSlope, blend) * Mathf.Deg2Rad);
            }
        }
        return limits;
    }

    private static Vector2 CreateNoiseOffset(int seed)
    {
        var random = new System.Random(seed);
        float x = NoiseOffsetMin + (float)random.NextDouble() * (NoiseOffsetMax - NoiseOffsetMin);
        float z = NoiseOffsetMin + (float)random.NextDouble() * (NoiseOffsetMax - NoiseOffsetMin);
        return new Vector2(x, z);
    }

    // 이웃 사이 허용 높이 차를 넘는 곳을 고친다. isFill이면 낮은 쪽을 올리고, 아니면 높은 쪽을 내린다 (같은 입력이면 항상 같은 결과)
    private static void Relax(float[,] values, float[,] limits, float sampleSpacing, bool isFill, string label)
    {
        for (int pass = 1; pass <= MaxRelaxPasses; pass++)
        {
            float change = Mathf.Max(RelaxPass(values, limits, sampleSpacing, isFill, true), RelaxPass(values, limits, sampleSpacing, isFill, false));
            if (change <= RelaxTolerance) return;
        }
        Debug.LogWarning($"[MapDemoTransplant] 경사 제한 계산({label})이 {MaxRelaxPasses}회 안에 끝나지 않았다. 기슭 경사가 제한을 넘을 수 있다");
    }

    // 한 방향 패스에서 가장 크게 바뀐 높이를 돌려준다
    private static float RelaxPass(float[,] values, float[,] limits, float sampleSpacing, bool isFill, bool isForward)
    {
        int depth = values.GetLength(0);
        int width = values.GetLength(1);
        int sign = isForward ? 1 : -1;
        float maxChange = 0f;
        for (int i = 0; i < depth; i++)
        {
            int z = isForward ? i : depth - 1 - i;
            for (int j = 0; j < width; j++)
            {
                int x = isForward ? j : width - 1 - j;
                float value = values[z, x];
                for (int n = 0; n < RelaxNeighbors.Length; n++)
                {
                    int neighborX = x + RelaxNeighbors[n].x * sign;
                    int neighborZ = z + RelaxNeighbors[n].y * sign;
                    if (neighborX < 0 || neighborX >= width || neighborZ < 0 || neighborZ >= depth) continue;

                    float distance = RelaxNeighbors[n].x != 0 && RelaxNeighbors[n].y != 0 ? sampleSpacing * Mathf.Sqrt(2f) : sampleSpacing;
                    float allowed = Mathf.Min(limits[z, x], limits[neighborZ, neighborX]) * distance;
                    value = isFill
                        ? Mathf.Max(value, values[neighborZ, neighborX] - allowed)
                        : Mathf.Min(value, values[neighborZ, neighborX] + allowed);
                }
                if (value == values[z, x]) continue;

                // 무한대에서 처음 내려온 칸은 바뀐 양을 셀 수 없으므로 한 번 더 돌게 한다
                float change = float.IsInfinity(values[z, x]) ? float.MaxValue : Mathf.Abs(value - values[z, x]);
                maxChange = Mathf.Max(maxChange, change);
                values[z, x] = value;
            }
        }
        return maxChange;
    }

    // 창 후보, 고른 창, 높이 오프셋, 데모 호수 변환 위치는 2단계 이후와 검수에 쓰는 값이라 로그로 남긴다
    private static void LogSelection(WindowSelection selection)
    {
        Debug.Log(selection.BlendTrials);

        DemoWindow window = selection.Window;
        var builder = new StringBuilder($"[MapDemoTransplant] 창 후보 (이어 붙이는 폭 {window.BlendWidth:F0}m, 데모 z, 점수 = 창 평균 - 남쪽 띠 평균, 남쪽 띠 = 데모 창의 북쪽 끝)\n");
        for (int i = 0; i < selection.Candidates.Length; i++)
        {
            WindowCandidate candidate = selection.Candidates[i];
            builder.AppendLine($"{(i == selection.BestIndex ? "* " : "  ")}z {candidate.StartZ:F0} ~ {candidate.StartZ + WindowDepth:F0} : 점수 {candidate.Score:F1}, " +
                               $"평균 {candidate.Mean:F1}m, 남쪽 띠 평균 {candidate.SouthBandMean:F1}m, 최고 {candidate.Max:F1}m, 최저 {candidate.Min:F1}m, 호수 포함 {candidate.HasLake}");
        }
        Debug.Log(builder.ToString());

        Rect gameWindow = GetGameWindowRect();
        Debug.Log($"[MapDemoTransplant] 고른 창 : 데모 x {DemoWindowStartX:F0} ~ {DemoWindowStartX + WindowWidth:F0}, z {window.StartZ:F0} ~ {window.StartZ + WindowDepth:F0} " +
                  $"→ 우리 x {gameWindow.xMin:F0} ~ {gameWindow.xMax:F0}, z {gameWindow.yMin:F0} ~ {gameWindow.yMax:F0} (1:1, 180도 회전 : 데모 z {window.StartZ + WindowDepth:F0} → 우리 z {gameWindow.yMin:F0}, " +
                  $"데모 x {DemoWindowStartX + WindowWidth:F0} → 우리 x {gameWindow.xMin:F0}). 이유 : 호수 물이 창 안, 남쪽 띠 밖이고 점수가 가장 크다 (남쪽 띠 평균 {window.SouthBandMean:F1}m)");

        float offsetUpper = Mathf.Min(window.OffsetUpperBottom, window.OffsetUpperLake);
        Debug.Log($"[MapDemoTransplant] 높이 오프셋 {window.HeightOffset:F2}m (남쪽 띠 평균 기준 목표 {window.OffsetTarget:F2}m, 허용 범위 {window.OffsetLower:F2} ~ {offsetUpper:F2}m " +
                  $"[최고점 {MaxResultHeight}m 이하 ≥ {window.OffsetLower:F2}, 최저점 {MinResultHeight}m 이상 ≤ {window.OffsetUpperBottom:F2}, 호수 수면 {MinLakeSurfaceHeight}m 이상 ≤ {window.OffsetUpperLake:F2}]" +
                  $"{GetOffsetNote(window, offsetUpper)}). " +
                  $"남쪽 띠 평균 결과 높이 {window.SouthBandMean - window.HeightOffset:F1}m, 벽 예상 경사 {window.EstimatedWallSlope:F1}도");

        Vector3 lake = DemoToGame(DemoLakePosition, window);
        Debug.Log($"[MapDemoTransplant] 데모 호수 물 칸 {selection.LakeCellCount}개, 데모 z {selection.LakeRangeZ.x:F0} ~ {selection.LakeRangeZ.y:F0} " +
                  $"(우리 z {DemoZToGame(selection.LakeRangeZ.y, window):F0} ~ {DemoZToGame(selection.LakeRangeZ.x, window):F0}, 남쪽 띠 끝 우리 z {gameWindow.yMin + window.BlendWidth:F0}), " +
                  $"창 안 {IsInsideWindow(DemoLakePosition)}");
        Debug.Log($"[MapDemoTransplant] 데모 호수 {DemoLakePosition} → 우리 위치 ({lake.x:F2}, {lake.y:F2}, {lake.z:F2}), 수면 높이 {lake.y:F2}m, " +
                  $"물 평면 {DemoLakeHalfSize * 2f:F1} × {DemoLakeHalfSize * 2f:F1}m, 회전 yaw + 180도 (DemoRotationToGame)");
    }

    // 오프셋이 목표에서 벗어났다면 어느 한계 때문인지
    private static string GetOffsetNote(DemoWindow window, float offsetUpper)
    {
        if (window.OffsetLower > offsetUpper)
        {
            return window.OffsetLower > window.OffsetUpperLake
                ? $", 범위가 엇갈려 최고점 한도를 지켰고 호수 수면이 {MinLakeSurfaceHeight}m 아래다"
                : ", 범위가 엇갈려 최저점이 0m에서 잘린다";
        }
        if (window.OffsetTarget > offsetUpper)
            return window.OffsetUpperLake <= window.OffsetUpperBottom ? ", 호수 수면 한계에 걸림" : ", 최저점 한계에 걸림";
        if (window.OffsetTarget < window.OffsetLower) return ", 최고점 한계에 걸림";
        return "";
    }

    // 기슭 경사 제한의 데모 대비 변경량. 적용하지 않아도 남겨 사용자가 켤지 정하는 근거로 쓴다
    private static void LogLimitResult(float[,] before, float[,] after, bool[,] pins, float sampleSpacing)
    {
        float maxRaise = 0f;
        float maxCut = 0f;
        float maxPinChange = 0f;
        int changedCount = 0;
        for (int z = 0; z < before.GetLength(0); z++)
        {
            for (int x = 0; x < before.GetLength(1); x++)
            {
                float change = after[z, x] - before[z, x];
                maxRaise = Mathf.Max(maxRaise, change);
                maxCut = Mathf.Max(maxCut, -change);
                if (Mathf.Abs(change) > ChangeThreshold) changedCount++;
                if (pins[z, x]) maxPinChange = Mathf.Max(maxPinChange, Mathf.Abs(change));
            }
        }
        float changedHectare = changedCount * sampleSpacing * sampleSpacing / SquareMetersPerHectare;
        FoothillSlope original = CalculateFoothillSlope(before, sampleSpacing);
        FoothillSlope limited = CalculateFoothillSlope(after, sampleSpacing);
        Debug.Log($"[MapDemoTransplant] 기슭 경사 제한 적용 {ShouldLimitFoothillSlope} (높이 {FoothillMaxHeight}m 미만 최대 {FoothillMaxSlope}도). " +
                  $"적용하면 바뀌는 넓이 {changedHectare:F1}ha ({(float)changedCount / before.Length * 100f:F1}%), 최대 메움 +{maxRaise:F1}m, 최대 깎음 -{maxCut:F1}m, " +
                  $"고정 칸(남쪽 경계 줄, 호수 물) 최대 변화 {maxPinChange:F4}m (0이어야 함)");
        Debug.Log($"[MapDemoTransplant] 기슭 최대 경사 : 데모 그대로 {original.Max:F1}도 ({FoothillMaxSlope}도 초과 {original.SteepRatio * 100f:F2}%), " +
                  $"제한 후 {limited.Max:F1}도 (초과 {limited.SteepRatio * 100f:F3}%)");
    }

    private static FoothillSlope CalculateFoothillSlope(float[,] heights, float sampleSpacing)
    {
        var slope = new FoothillSlope();
        int foothillCount = 0;
        int steepCount = 0;
        for (int z = 1; z < heights.GetLength(0) - 1; z++)
        {
            for (int x = 1; x < heights.GetLength(1) - 1; x++)
            {
                if (heights[z, x] >= FoothillMaxHeight) continue;
                float value = GetSlope(heights, z, x, sampleSpacing);
                foothillCount++;
                slope.Max = Mathf.Max(slope.Max, value);
                if (value > FoothillMaxSlope + SlopeCheckTolerance) steepCount++;
            }
        }
        slope.SteepRatio = foothillCount > 0 ? (float)steepCount / foothillCount : 0f;
        return slope;
    }

    private static float GetSlope(float[,] heights, int z, int x, float sampleSpacing)
    {
        float gradientX = (heights[z, x + 1] - heights[z, x - 1]) / (2f * sampleSpacing);
        float gradientZ = (heights[z + 1, x] - heights[z - 1, x]) / (2f * sampleSpacing);
        return Mathf.Atan(Mathf.Sqrt(gradientX * gradientX + gradientZ * gradientZ)) * Mathf.Rad2Deg;
    }

    // 검수(최고점, 남쪽 경계 이어짐, 동서 블렌드, 호수 자리)에 쓰는 값이라 로그를 남긴다
    private static void LogResult(float[,] heights, Vector2[,] positions, DemoWindow window)
    {
        float sampleSpacing = positions[0, 1].x - positions[0, 0].x;
        float minHeight = float.MaxValue;
        var highest = Vector2Int.zero;
        float southDifference = 0f;
        for (int z = 0; z < heights.GetLength(0); z++)
        {
            for (int x = 0; x < heights.GetLength(1); x++)
            {
                minHeight = Mathf.Min(minHeight, heights[z, x]);
                if (heights[z, x] > heights[highest.y, highest.x]) highest = new Vector2Int(x, z);
                if (z == 0) southDifference = Mathf.Max(southDifference, Mathf.Abs(heights[z, x] - MapTerrainSetup.BaseHeight));
            }
        }
        Vector2 highestPosition = positions[highest.y, highest.x];
        Debug.Log($"[MapDemoTransplant] 높이 최저 {minHeight:F1}m, 최고 {heights[highest.y, highest.x]:F1}m, 최고점 ({highestPosition.x:F0}, {highestPosition.y:F0})");
        Debug.Log($"[MapDemoTransplant] 남쪽 경계 줄(z {positions[0, 0].y:F2})과 바탕 높이의 최대 차이 {southDifference:F4}m (0이어야 함), 이어 붙이는 폭 {window.BlendWidth:F0}m");
        LogBlendSlope(heights, positions, window, sampleSpacing);

        Rect gameWindow = GetGameWindowRect();
        int lastColumn = heights.GetLength(1) - 1;
        Debug.Log($"[MapDemoTransplant] 동서 블렌드 : 서쪽 x {gameWindow.xMin - EdgeFadeWidth:F0} ~ {gameWindow.xMin:F0}, 동쪽 x {gameWindow.xMax:F0} ~ {gameWindow.xMax + EdgeFadeWidth:F0} " +
                  $"(그 바깥은 바탕 위 높이의 {OuterHillRatio * 100f:F0}%). 맵 서쪽 끝 평균 {GetColumnAverage(heights, 0):F1}m, 동쪽 끝 평균 {GetColumnAverage(heights, lastColumn):F1}m");

        Vector3 lake = DemoToGame(DemoLakePosition, window);
        int lakeColumn = Mathf.Clamp(Mathf.RoundToInt((lake.x - positions[0, 0].x) / sampleSpacing), 0, lastColumn);
        int lakeRow = Mathf.Clamp(Mathf.RoundToInt((lake.z - positions[0, 0].y) / sampleSpacing), 0, heights.GetLength(0) - 1);
        Debug.Log($"[MapDemoTransplant] 호수 평면 중심 지형 높이 {heights[lakeRow, lakeColumn]:F2}m, 수면 {lake.y:F2}m (중심이 물 밖일 수 있다. 물 칸 범위는 위 로그)");
    }

    // 남쪽 이어 붙이는 구간(창 가로 범위 안)의 실제 경사. 벽 예상 경사는 띠 평균만 본 값이라, 데모 비탈이 더해진 실제 값을 함께 남긴다
    private static void LogBlendSlope(float[,] heights, Vector2[,] positions, DemoWindow window, float sampleSpacing)
    {
        Rect gameWindow = GetGameWindowRect();
        float southZ = positions[0, 0].y;
        float maxSlope = 0f;
        var steepest = Vector2.zero;
        int count = 0;
        int steepCount = 0;
        for (int z = 1; z < heights.GetLength(0) - 1 && positions[z, 0].y - southZ <= window.BlendWidth; z++)
        {
            for (int x = 1; x < heights.GetLength(1) - 1; x++)
            {
                if (positions[z, x].x < gameWindow.xMin || positions[z, x].x > gameWindow.xMax) continue;
                float slope = GetSlope(heights, z, x, sampleSpacing);
                count++;
                if (slope > MaxBlendWallSlope) steepCount++;
                if (slope <= maxSlope) continue;
                maxSlope = slope;
                steepest = positions[z, x];
            }
        }
        float steepRatio = count > 0 ? (float)steepCount / count : 0f;
        Debug.Log($"[MapDemoTransplant] 남쪽 이어 붙이는 구간(우리 z {southZ:F0} ~ {southZ + window.BlendWidth:F0}, x {gameWindow.xMin:F0} ~ {gameWindow.xMax:F0}) " +
                  $"최대 경사 {maxSlope:F1}도 ({steepest.x:F0}, {steepest.y:F0}), {MaxBlendWallSlope}도 초과 {steepRatio * 100f:F2}%, 벽 예상 경사 {window.EstimatedWallSlope:F1}도");
    }

    private static float GetColumnAverage(float[,] heights, int column)
    {
        float sum = 0f;
        for (int z = 0; z < heights.GetLength(0); z++)
            sum += heights[z, column];
        return sum / heights.GetLength(0);
    }

    private static float[,] ToNormalized(float[,] heights, float terrainHeight)
    {
        for (int z = 0; z < heights.GetLength(0); z++)
        {
            for (int x = 0; x < heights.GetLength(1); x++)
                heights[z, x] = Mathf.Clamp01(heights[z, x] / terrainHeight);
        }
        return heights;
    }
}
