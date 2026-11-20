using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 메뉴 28 : 북쪽 줄 서식지 3곳 (북, 북동, 북서)의 위치 표시를 두고, 서식지 8곳의 몬스터 스포너를 아래 표대로 맞춘다 (이슈 #35).
// - 서식지 : North/Habitat, NorthEast/Habitat, NorthWest/Habitat을 HabitatCenters의 지면 높이에 둔다.
//   북쪽 줄 서식지 3곳의 위치는 이 스크립트가 단일출처이다 (NavMesh·경사 검색으로 고른 자리, 사용자 승인 임시값).
// - 스포너 : MonsterSpawners 아래 NorthSpawner, NorthEastSpawner, NorthWestSpawner를 WestSpawner 값을 복사해 서식지 중심에 만들고,
//   스포너 8개의 몬스터 prefab과 마리 수를 SpawnerSettings 표대로 넣는다. 반경, 재생성 시간 등 나머지 값은 건드리지 않는다.
// 다시 실행하면 이 스크립트가 만든 Habitat 3개와 스포너 3개만 지우고 다시 만든 뒤, 표를 다시 적용하고 씬을 저장한다.
// 기존 스포너 5개는 위치를 옮기지 않는다 (각 구역 배치 메뉴의 서식지 위치를 따른다).
public static class MapNorthHabitats
{
    private const string GameSceneName = "Game";
    private const string HabitatName = "Habitat";
    private const string SpawnerRootName = "MonsterSpawners";
    // 새 스포너가 값을 복사해 올 스포너
    private const string TemplateSpawnerName = "WestSpawner";
    private const string MonsterPrefabFolder = "Assets/Prefabs/Monsters/";
    private const string PrefabExtension = ".prefab";
    private const string PrefabFieldName = "_monsterPrefab";
    private const string CountFieldName = "_count";
    private const string LogTag = "[MapNorthHabitats]";
    private const float FullTurnDegrees = 360f;

    // 이 스크립트가 만드는 서식지. 구역 부모 이름, 스포너 이름, 중심 (x, z)
    private static readonly HabitatSpec[] HabitatCenters =
    {
        new HabitatSpec("North", "NorthSpawner", new Vector2(-75f, 400f)),
        new HabitatSpec("NorthEast", "NorthEastSpawner", new Vector2(1100f, 850f)),
        new HabitatSpec("NorthWest", "NorthWestSpawner", new Vector2(-750f, 1000f))
    };

    // 스포너별 몬스터와 마리 수 (사용자 결정 임시값). 몬스터는 MonsterPrefabFolder 안 prefab 이름
    private static readonly SpawnerSetting[] SpawnerSettings =
    {
        new SpawnerSetting("WestSpawner", "Goblin", 2),
        new SpawnerSetting("SouthWestSpawner", "Goblin", 2),
        new SpawnerSetting("NorthEastSpawner", "Goblin", 2),
        new SpawnerSetting("EastSpawner", "MushroomMonster", 2),
        new SpawnerSetting("NorthSpawner", "MushroomMonster", 2),
        new SpawnerSetting("SouthSpawner", "SkeletonWarrior", 3),
        new SpawnerSetting("SouthEastSpawner", "Fishman", 2),
        new SpawnerSetting("NorthWestSpawner", "RockMonster", 1)
    };

    private readonly struct HabitatSpec
    {
        public readonly string RegionName;
        public readonly string SpawnerName;
        public readonly Vector2 Center;

        public HabitatSpec(string regionName, string spawnerName, Vector2 center)
        {
            RegionName = regionName;
            SpawnerName = spawnerName;
            Center = center;
        }
    }

    private readonly struct SpawnerSetting
    {
        public readonly string SpawnerName;
        public readonly string PrefabName;
        public readonly int Count;

        public SpawnerSetting(string spawnerName, string prefabName, int count)
        {
            SpawnerName = spawnerName;
            PrefabName = prefabName;
            Count = count;
        }
    }

    [MenuItem("Tools/Map/28. 북쪽 서식지 3곳과 몬스터 스포너 배치")]
    private static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != GameSceneName)
        {
            Debug.LogError($"{LogTag} {GameSceneName} 씬을 활성 씬으로 두고 실행하세요. (현재 활성 씬 : {scene.name})");
            return;
        }

        GameObject spawnerRoot = FindRoot(scene, SpawnerRootName);
        if (spawnerRoot == null)
        {
            Debug.LogError($"{LogTag} 씬 루트에 {SpawnerRootName}가 없습니다.");
            return;
        }

        MonsterSpawner template = FindSpawner(spawnerRoot.transform, TemplateSpawnerName);
        if (template == null) return;

        // 높이와 prefab을 모두 확인한 뒤에 씬을 바꾼다 (중간에 멈춰 반쯤 바뀐 씬이 남지 않게)
        Vector3[] positions = HabitatCenters.Select(spec => GetGroundPosition(spec.Center)).ToArray();
        if (positions.Any(position => float.IsNaN(position.y))) return;
        Monster[] prefabs = SpawnerSettings.Select(setting => LoadMonsterPrefab(setting.PrefabName)).ToArray();
        if (prefabs.Any(prefab => prefab == null)) return;

        for (int i = 0; i < HabitatCenters.Length; i++)
        {
            PlaceHabitat(scene, HabitatCenters[i], positions[i]);
            CreateSpawner(spawnerRoot.transform, template, HabitatCenters[i].SpawnerName, positions[i]);
        }

        for (int i = 0; i < SpawnerSettings.Length; i++)
        {
            MonsterSpawner spawner = FindSpawner(spawnerRoot.transform, SpawnerSettings[i].SpawnerName);
            if (spawner == null) continue;
            ApplySetting(spawner, prefabs[i], SpawnerSettings[i].Count);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        return scene.GetRootGameObjects().FirstOrDefault(root => root.name == name);
    }

    private static MonsterSpawner FindSpawner(Transform spawnerRoot, string name)
    {
        Transform child = spawnerRoot.Find(name);
        MonsterSpawner spawner = child != null ? child.GetComponent<MonsterSpawner>() : null;
        if (spawner == null) Debug.LogError($"{LogTag} {SpawnerRootName}/{name}에 MonsterSpawner가 없습니다.");
        return spawner;
    }

    // 지형이 여러 개라 점을 덮는 Terrain에서 높이를 잰다. 덮는 Terrain이 없으면 y가 NaN
    private static Vector3 GetGroundPosition(Vector2 center)
    {
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            Vector3 origin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            bool isInside = center.x >= origin.x && center.x <= origin.x + size.x &&
                            center.y >= origin.z && center.y <= origin.z + size.z;
            if (!isInside) continue;

            var position = new Vector3(center.x, 0f, center.y);
            // SampleHeight는 Terrain 기준 높이라 Terrain의 y를 더한다
            position.y = terrain.SampleHeight(position) + origin.y;
            return position;
        }

        Debug.LogError($"{LogTag} ({center.x}, {center.y})를 덮는 Terrain이 없습니다.");
        return new Vector3(center.x, float.NaN, center.y);
    }

    private static Monster LoadMonsterPrefab(string prefabName)
    {
        string path = MonsterPrefabFolder + prefabName + PrefabExtension;
        Monster prefab = AssetDatabase.LoadAssetAtPath<Monster>(path);
        if (prefab == null) Debug.LogError($"{LogTag} {path}가 없거나 루트에 Monster 컴포넌트가 없습니다.");
        return prefab;
    }

    // 구역 부모가 없으면 만들고, 이 스크립트가 만든 Habitat만 지운 뒤 다시 만든다
    private static void PlaceHabitat(Scene scene, HabitatSpec spec, Vector3 position)
    {
        GameObject region = FindRoot(scene, spec.RegionName);
        if (region == null)
        {
            region = new GameObject(spec.RegionName);
            Undo.RegisterCreatedObjectUndo(region, $"Create {spec.RegionName}");
        }

        Transform old = region.transform.Find(HabitatName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var habitat = new GameObject(HabitatName);
        Undo.RegisterCreatedObjectUndo(habitat, $"Create {spec.RegionName}/{HabitatName}");
        habitat.transform.SetParent(region.transform, false);
        habitat.transform.position = position;

        // 거점(원점) 기준 방위. 북 0°, 동 90°. 서식지 방향을 MapLayout.md 방향 표와 대조하려고 로그를 남겨 둔다
        float bearing = Mathf.Repeat(Mathf.Atan2(spec.Center.x, spec.Center.y) * Mathf.Rad2Deg, FullTurnDegrees);
        Debug.Log($"{LogTag} {spec.RegionName}/{HabitatName} 위치 {position}, 거점 기준 방위 {bearing:F1}°");
    }

    // 같은 이름 스포너가 있으면 지우고, 템플릿 스포너 값을 복사한 새 스포너를 서식지 중심에 만든다
    private static void CreateSpawner(Transform spawnerRoot, MonsterSpawner template, string name, Vector3 position)
    {
        Transform old = spawnerRoot.Find(name);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var spawnerObject = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(spawnerObject, $"Create {name}");
        spawnerObject.transform.SetParent(spawnerRoot, false);
        spawnerObject.transform.position = position;

        MonsterSpawner spawner = spawnerObject.AddComponent<MonsterSpawner>();
        EditorUtility.CopySerialized(template, spawner);
    }

    private static void ApplySetting(MonsterSpawner spawner, Monster prefab, int count)
    {
        var serialized = new SerializedObject(spawner);
        serialized.FindProperty(PrefabFieldName).objectReferenceValue = prefab;
        serialized.FindProperty(CountFieldName).intValue = count;
        // Undo 기록도 함께 남는다
        serialized.ApplyModifiedProperties();

        // 스포너 결과 확인용. 메뉴를 실행할 때마다 표와 대조할 값이라 남겨 둔다
        Debug.Log($"{LogTag} {spawner.name} : {prefab.name} {count}마리, 위치 {spawner.transform.position}");
    }
}
