using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 메뉴 4 : 중앙 지역(B2~C3)의 바닥 식생(Terrain 디테일)과 지면 레이어를 칠한다.
// 영역 구분은 MapTreeScatter.GetFieldZone을 그대로 써서 나무 분포와 맞춘다.
// 중앙 지역만 덮어쓰므로 여러 번 실행해도 결과가 같고, 바깥 구역은 그대로 남는다.
public static class MapDetailPainter
{
    private enum DetailGroup
    {
        Grass = 0,
        Fern = 1,
        Leaf = 2,
        Flower = 3
    }

    // 한 번 실행하는 동안 함께 쓰는 값 묶음
    private class PaintContext
    {
        public Dictionary<int, int[,]> Layers;
        public int[][] GroupPrototypeIndices;
        public System.Random Random;
    }

    private const int Seed = 20261109;
    private const string FoliageFolder = "Assets/Imports/Polyart/PolyartStudio/DreamscapeMountains/Prefabs/Foliage/";

    // 그룹별 prefab과 크기 범위. 순서는 DetailGroup (풀, 고사리, 낙엽, 꽃)
    // 고사리는 원본 폭이 3m라 크기를 줄인다
    private static readonly string[][] GroupPrefabNames =
    {
        new[] { "Prefab_Terrain_Grass_Medium01", "Prefab_Terrain_Grass_Medium02", "Prefab_Terrain_Grass_Medium03", "Prefab_Terrain_Grass_Small01" },
        new[] { "Prefab_Terrain_Fern_02", "Prefab_Terrain_Fern_03" },
        new[] { "Prefab_Terrain_Groundleaf_01" },
        new[] { "Prefab_FlowerGroup_White_Detail", "Prefab_FlowerGroup_Yellow_Detail", "Prefab_FlowerSingle_Purple_Detail", "Prefab_FlowerSingle_Red_Detail" }
    };
    // 그룹 안에서 각 prefab이 뽑히는 비중. GroupPrefabNames와 같은 순서이며, 그룹마다 합이 1이 아니어도 된다
    // 풀 : 연한 풀(Small01)이 너무 눈에 띄어 15%로 줄이고, 나머지를 Medium 3종에 고르게 나눴다
    private static readonly float[][] GroupPrefabWeights =
    {
        new[] { 0.85f / 3f, 0.85f / 3f, 0.85f / 3f, 0.15f },
        new[] { 1f, 1f },
        new[] { 1f },
        new[] { 1f, 1f, 1f, 1f }
    };
    private static readonly float[] GroupScaleMin = { 0.8f, 0.4f, 0.5f, 0.8f };
    private static readonly float[] GroupScaleMax = { 1.1f, 0.7f, 0.8f, 1.1f };

    // 디테일 칸(약 2m) 하나에 놓는 개수
    private const int ForestFernMax = 1;
    private const int ForestLeafCount = 1;
    private const int SparseGrassMin = 1;
    private const int SparseGrassMax = 2;
    private const float SparseFernChance = 0.1f;
    private const int OpenGrassMin = 2;
    private const int OpenGrassMax = 3;
    private const int FlowerCountMin = 1;
    private const int FlowerCountMax = 2;

    // 꽃은 노이즈가 높은 곳에만 무리 지어 핀다
    private const float FlowerNoisePeriod = 30f;
    private const float FlowerNoiseThreshold = 0.65f;
    private static readonly Vector2 FlowerNoiseOffset = new Vector2(523.9f, 187.1f);

    // 이 경사부터 MapTreeScatter.MaxSlope까지 돌 지면으로 섞는다
    private const float StoneBlendStartSlope = 25f;

    [MenuItem("Tools/Map/4. 중앙 바닥 식생 칠하기")]
    private static void Run()
    {
        Terrain terrain = MapTerrainSetup.FindSceneTerrain();
        if (terrain == null) return;

        TerrainData data = terrain.terrainData;
        if (data.detailScatterMode != DetailScatterMode.InstanceCountMode)
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);

        Rect center = MapGrid.GetRangeBounds(MapTerrainSetup.CenterFromCell, MapTerrainSetup.CenterToCell);
        int[][] groupPrototypeIndices = RegisterPrototypes(data);
        PaintAlphamaps(terrain, center);
        Dictionary<int, int[,]> layers = PaintDetails(terrain, center, groupPrototypeIndices);

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        LogResult(data, layers);
    }

    // 이미 등록된 prefab은 그 번호를 쓰고, 없으면 뒤에 추가한다. 크기와 그리기 설정은 매번 위 표대로 다시 넣는다
    private static int[][] RegisterPrototypes(TerrainData data)
    {
        var prototypes = new List<DetailPrototype>(data.detailPrototypes);
        var groupPrototypeIndices = new int[GroupPrefabNames.Length][];
        for (int group = 0; group < GroupPrefabNames.Length; group++)
        {
            string[] names = GroupPrefabNames[group];
            groupPrototypeIndices[group] = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoliageFolder + names[i] + ".prefab");
                int index = prototypes.FindIndex(prototype => prototype.prototype == prefab);
                if (index < 0)
                {
                    prototypes.Add(new DetailPrototype());
                    index = prototypes.Count - 1;
                }
                ConfigurePrototype(prototypes[index], prefab, group);
                groupPrototypeIndices[group][i] = index;
            }
        }
        data.detailPrototypes = prototypes.ToArray();
        return groupPrototypeIndices;
    }

    private static void ConfigurePrototype(DetailPrototype prototype, GameObject prefab, int group)
    {
        prototype.prototype = prefab;
        prototype.usePrototypeMesh = true;
        prototype.renderMode = DetailRenderMode.VertexLit;
        prototype.useInstancing = true;
        prototype.minWidth = GroupScaleMin[group];
        prototype.maxWidth = GroupScaleMax[group];
        prototype.minHeight = GroupScaleMin[group];
        prototype.maxHeight = GroupScaleMax[group];
    }

    // 지면 레이어 : 기본 풀, 숲은 숲 바닥, 경사면은 돌로 섞고, 거점 반경은 흙
    private static void PaintAlphamaps(Terrain terrain, Rect center)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.alphamapResolution;
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, center, resolution);
        var weights = new float[range.height, range.width, data.alphamapLayers];

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                Vector2 world = GetCellWorldPosition(terrain, range.x + x, range.y + z, resolution);
                if (world.magnitude < MapHeightShaper.BaseFlatRadius)
                {
                    weights[z, x, MapTerrainSetup.DirtLayer] = 1f;
                    continue;
                }

                int groundLayer = MapTreeScatter.GetFieldZone(world) == MapTreeScatter.FieldZone.Forest
                    ? MapTerrainSetup.ForestGroundLayer
                    : MapTerrainSetup.GrassLayer;
                float slope = data.GetSteepness((range.x + x + 0.5f) / resolution, (range.y + z + 0.5f) / resolution);
                float stoneWeight = Mathf.InverseLerp(StoneBlendStartSlope, MapTreeScatter.MaxSlope, slope);
                weights[z, x, groundLayer] = 1f - stoneWeight;
                weights[z, x, MapTerrainSetup.StoneLayer] += stoneWeight;
            }
        }
        data.SetAlphamaps(range.x, range.y, weights);
    }

    private static Dictionary<int, int[,]> PaintDetails(Terrain terrain, Rect center, int[][] groupPrototypeIndices)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.detailResolution;
        RectInt range = MapTerrainSetup.ToIndexRange(terrain, center, resolution);
        var context = new PaintContext
        {
            Layers = groupPrototypeIndices.SelectMany(indices => indices)
                .ToDictionary(index => index, index => new int[range.height, range.width]),
            GroupPrototypeIndices = groupPrototypeIndices,
            Random = new System.Random(Seed)
        };

        for (int z = 0; z < range.height; z++)
        {
            for (int x = 0; x < range.width; x++)
            {
                Vector2 world = GetCellWorldPosition(terrain, range.x + x, range.y + z, resolution);
                if (world.magnitude < MapTreeScatter.BaseClearRadius) continue;

                float slope = data.GetSteepness((range.x + x + 0.5f) / resolution, (range.y + z + 0.5f) / resolution);
                if (slope >= MapTreeScatter.MaxSlope) continue;

                PaintDetailCell(context, x, z, world);
            }
        }

        foreach (KeyValuePair<int, int[,]> layer in context.Layers)
            data.SetDetailLayer(range.x, range.y, layer.Key, layer.Value);
        return context.Layers;
    }

    private static void PaintDetailCell(PaintContext context, int x, int z, Vector2 world)
    {
        System.Random random = context.Random;
        switch (MapTreeScatter.GetFieldZone(world))
        {
            case MapTreeScatter.FieldZone.Forest:
                AddDetail(context, x, z, DetailGroup.Fern, random.Next(0, ForestFernMax + 1));
                AddDetail(context, x, z, DetailGroup.Leaf, ForestLeafCount);
                break;
            case MapTreeScatter.FieldZone.Sparse:
                AddDetail(context, x, z, DetailGroup.Grass, random.Next(SparseGrassMin, SparseGrassMax + 1));
                if (random.NextDouble() < SparseFernChance) AddDetail(context, x, z, DetailGroup.Fern, 1);
                break;
            case MapTreeScatter.FieldZone.Open:
                AddDetail(context, x, z, DetailGroup.Grass, random.Next(OpenGrassMin, OpenGrassMax + 1));
                if (SampleFlowerNoise(world) > FlowerNoiseThreshold)
                    AddDetail(context, x, z, DetailGroup.Flower, random.Next(FlowerCountMin, FlowerCountMax + 1));
                break;
        }
    }

    // 그룹 안에서 prefab 하나를 비중대로 골라 그 칸의 개수를 더한다
    private static void AddDetail(PaintContext context, int x, int z, DetailGroup group, int count)
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

    // 알파맵, 디테일 칸 중심의 월드 XZ 좌표
    private static Vector2 GetCellWorldPosition(Terrain terrain, int indexX, int indexZ, int resolution)
    {
        Vector3 position = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        return new Vector2(
            position.x + (indexX + 0.5f) * size.x / resolution,
            position.z + (indexZ + 0.5f) * size.z / resolution);
    }

    private static void LogResult(TerrainData data, Dictionary<int, int[,]> layers)
    {
        IEnumerable<string> lines = layers.Select(layer =>
            $"{data.detailPrototypes[layer.Key].prototype.name}: {layer.Value.Cast<int>().Sum()}");
        Debug.Log("[MapDetailPainter] 중앙 바닥 식생 인스턴스 수\n" + string.Join("\n", lines));
    }
}
