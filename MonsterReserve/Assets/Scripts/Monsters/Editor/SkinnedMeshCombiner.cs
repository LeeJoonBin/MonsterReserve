using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 몬스터 프리팹의 SkinnedMeshRenderer 여러 개를 같은 뼈대를 쓰는 하나로 합친다 (드로우 콜, 스키닝 비용 줄이기).
// 원본 렌더러는 지우지 않고 끄기만 하므로, 장비 구성을 바꾼 뒤 다시 실행하면 다시 합쳐진다.
// 조건 : 합칠 렌더러가 모두 같은 머티리얼 하나를 써야 한다 (다르면 멈추고 알린다)
public static class SkinnedMeshCombiner
{
    private const string CombinedName = "CombinedBody";
    private const string MeshFolder = "Assets/Prefabs/Monsters/Meshes";
    // 애니메이션으로 팔다리가 bounds 밖으로 나가 잘려 보이지 않게 넉넉히 늘린다
    private const float BoundsPadding = 1.2f;

    [MenuItem("Tools/Monsters/Combine Skinned Meshes")]
    private static void CombineSelected()
    {
        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
        {
            EditorUtility.DisplayDialog("Combine Skinned Meshes", "Project 창에서 몬스터 프리팹을 선택한 뒤 실행하세요.", "OK");
            return;
        }
        Debug.Log(Combine(path));
    }

    public static string Combine(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform oldCombined = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == CombinedName);
            if (oldCombined != null) Object.DestroyImmediate(oldCombined.gameObject);

            // 꺼진 GameObject (쓰지 않는 장비)는 빼고, 렌더러가 꺼진 것 (이전에 합치며 끈 것)은 포함한다
            SkinnedMeshRenderer[] sources = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(s => s.gameObject.activeInHierarchy && s.sharedMesh != null).ToArray();
            if (sources.Length < 2) return prefabPath + " : 합칠 렌더러가 " + sources.Length + "개뿐이다";

            Material material = sources[0].sharedMaterial;
            if (sources.Any(s => s.sharedMaterials.Length != 1 || s.sharedMaterial != material))
                return prefabPath + " : 머티리얼이 하나가 아니라 합치지 않았다";

            // 합친 메시는 원본들의 공통 부모 공간에 둔다
            Transform space = sources[0].transform.parent;
            Matrix4x4 worldToSpace = space.worldToLocalMatrix;

            var bones = new List<Transform>();
            var boneIndex = new Dictionary<Transform, int>();
            var bindposes = new List<Matrix4x4>();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uvs = new List<Vector2>();
            var weights = new List<BoneWeight>();
            var triangles = new List<int>();
            float maxBindposeError = 0f;

            foreach (SkinnedMeshRenderer smr in sources)
            {
                Mesh mesh = smr.sharedMesh;
                // 원본 메시 공간 → 합친 공간
                Matrix4x4 toSpace = worldToSpace * smr.transform.localToWorldMatrix;
                Matrix4x4 fromSpace = toSpace.inverse;

                Vector3[] v = mesh.vertices;
                Vector3[] n = mesh.normals;
                BakeBlendShapes(smr, mesh, v, n);

                int offset = vertices.Count;
                Vector4[] t = mesh.tangents;
                Vector2[] uv = mesh.uv;
                for (int i = 0; i < v.Length; i++)
                {
                    vertices.Add(toSpace.MultiplyPoint3x4(v[i]));
                    normals.Add(n.Length > 0 ? toSpace.MultiplyVector(n[i]).normalized : Vector3.up);
                    if (t.Length > 0)
                    {
                        Vector3 td = toSpace.MultiplyVector(t[i]).normalized;
                        tangents.Add(new Vector4(td.x, td.y, td.z, t[i].w));
                    }
                    else tangents.Add(new Vector4(1f, 0f, 0f, 1f));
                    uvs.Add(uv.Length > 0 ? uv[i] : Vector2.zero);
                }

                // 뼈 번호를 합친 목록 기준으로 다시 매긴다
                Transform[] smrBones = smr.bones;
                Matrix4x4[] smrBind = mesh.bindposes;
                var remap = new int[smrBones.Length];
                for (int b = 0; b < smrBones.Length; b++)
                {
                    Matrix4x4 bind = smrBind[b] * fromSpace;
                    if (!boneIndex.TryGetValue(smrBones[b], out int index))
                    {
                        index = bones.Count;
                        boneIndex[smrBones[b]] = index;
                        bones.Add(smrBones[b]);
                        bindposes.Add(bind);
                    }
                    else maxBindposeError = Mathf.Max(maxBindposeError, MaxDiff(bindposes[index], bind));
                    remap[b] = index;
                }

                foreach (BoneWeight w in mesh.boneWeights)
                {
                    weights.Add(new BoneWeight
                    {
                        boneIndex0 = remap[w.boneIndex0], weight0 = w.weight0,
                        boneIndex1 = remap[w.boneIndex1], weight1 = w.weight1,
                        boneIndex2 = remap[w.boneIndex2], weight2 = w.weight2,
                        boneIndex3 = remap[w.boneIndex3], weight3 = w.weight3,
                    });
                }

                for (int s = 0; s < mesh.subMeshCount; s++)
                    triangles.AddRange(mesh.GetTriangles(s).Select(i => i + offset));
            }

            var combined = new Mesh { name = Path.GetFileNameWithoutExtension(prefabPath) + "_Combined" };
            if (vertices.Count > 65535) combined.indexFormat = IndexFormat.UInt32;
            combined.SetVertices(vertices);
            combined.SetNormals(normals);
            combined.SetTangents(tangents);
            combined.SetUVs(0, uvs);
            combined.boneWeights = weights.ToArray();
            combined.bindposes = bindposes.ToArray();
            combined.SetTriangles(triangles, 0);
            combined.RecalculateBounds();

            if (!AssetDatabase.IsValidFolder(MeshFolder)) AssetDatabase.CreateFolder("Assets/Prefabs/Monsters", "Meshes");
            string meshPath = MeshFolder + "/" + combined.name + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing != null) { EditorUtility.CopySerialized(combined, existing); combined = existing; }
            else AssetDatabase.CreateAsset(combined, meshPath);

            var go = new GameObject(CombinedName);
            go.layer = sources[0].gameObject.layer;
            go.transform.SetParent(space, false);
            var target = go.AddComponent<SkinnedMeshRenderer>();
            target.sharedMesh = combined;
            target.sharedMaterial = material;
            target.bones = bones.ToArray();
            target.rootBone = sources[0].rootBone;
            target.shadowCastingMode = sources[0].shadowCastingMode;
            target.receiveShadows = sources[0].receiveShadows;
            target.quality = SkinQuality.Auto;
            target.updateWhenOffscreen = false;
            target.localBounds = BoundsInRootBone(combined.bounds, space, target.rootBone);

            foreach (SkinnedMeshRenderer smr in sources) smr.enabled = false;

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            AssetDatabase.SaveAssets();
            return prefabPath + " : 렌더러 " + sources.Length + "개 → 1개, 정점 " + vertices.Count + ", 삼각형 " + triangles.Count / 3
                + ", 뼈 " + bones.Count + ", bindpose 최대 차이 " + maxBindposeError.ToString("0.000000") + ", 메시 " + meshPath;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 블렌드 셰이프 값이 들어 있는 조각은 지금 모양을 정점에 미리 반영한다 (합친 메시에는 블렌드 셰이프가 없다)
    private static void BakeBlendShapes(SkinnedMeshRenderer smr, Mesh mesh, Vector3[] v, Vector3[] n)
    {
        var dv = new Vector3[v.Length];
        var dn = new Vector3[v.Length];
        var dt = new Vector3[v.Length];
        for (int s = 0; s < mesh.blendShapeCount; s++)
        {
            float weight = smr.GetBlendShapeWeight(s);
            if (weight <= 0f) continue;
            int last = mesh.GetBlendShapeFrameCount(s) - 1;
            float ratio = weight / mesh.GetBlendShapeFrameWeight(s, last);
            mesh.GetBlendShapeFrameVertices(s, last, dv, dn, dt);
            for (int i = 0; i < v.Length; i++)
            {
                v[i] += dv[i] * ratio;
                if (n.Length > 0) n[i] += dn[i] * ratio;
            }
        }
    }

    private static Bounds BoundsInRootBone(Bounds b, Transform space, Transform rootBone)
    {
        Matrix4x4 m = rootBone.worldToLocalMatrix * space.localToWorldMatrix;
        Vector3 c = b.center, e = b.extents;
        var result = new Bounds(m.MultiplyPoint3x4(c), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            result.Encapsulate(m.MultiplyPoint3x4(c + corner));
        }
        result.extents *= BoundsPadding;
        return result;
    }

    private static float MaxDiff(Matrix4x4 a, Matrix4x4 b)
    {
        float max = 0f;
        for (int i = 0; i < 16; i++) max = Mathf.Max(max, Mathf.Abs(a[i] - b[i]));
        return max;
    }
}
