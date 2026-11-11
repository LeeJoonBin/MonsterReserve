using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// 메뉴 10 : 색감(후처리), 안개, 하늘, 환경광, 해를 데모 씬 (DemoScene_V2.0.unity) 설정으로 맞춘다.
// 안개 값은 이 스크립트 한 곳에서만 정한다 (메뉴 1은 안개를 건드리지 않는다).
// 여러 번 실행해도 Global Volume, 해가 중복 생성되지 않는다.
public static class MapAtmosphereSetup
{
    // 데모 원본 프로필은 고치지 않고, 처음 한 번만 우리 폴더로 복제해 그 복제본을 쓴다.
    private const string DemoVolumeProfilePath = "Assets/Imports/Polyart/PolyartStudio/DreamscapeMountains/Scenes/Mountains_URP_PostProcess.asset";
    private const string GameVolumeProfilePath = "Assets/Settings/World/GamePostProcess.asset";
    private const string SkyboxPath = "Assets/Imports/Polyart/PolyartStudio/SharedResources/Materials/SkyboxLiteWarm_Custom.mat";

    private const string VolumeName = "Global Volume";
    private const string SunName = "Directional Light";

    // 안개 (데모 RenderSettings). Exp2는 시작·끝 거리를 쓰지 않고 밀도로만 정해진다.
    private const FogMode DemoFogMode = FogMode.ExponentialSquared;
    private const float FogDensity = 0.002f;
    private static readonly Color FogColor = new Color(0.16862744f, 0.5679085f, 0.7529412f);

    // 환경광, 반사 (데모 RenderSettings). 하늘 모드라 하늘·적도·지면 색은 쓰지 않는다.
    private const float AmbientIntensity = 1f;
    private const int ReflectionResolution = 128;
    private const float ReflectionIntensity = 1f;
    private const int ReflectionBounces = 1;

    // 해 (데모 "Directional Light"). 부모 회전이 0이라 로컬 회전이 곧 월드 회전이다.
    private static readonly Vector3 SunEuler = new Vector3(37.7f, 45.49f, 0f);
    private static readonly Color SunColor = new Color(0.990566f, 0.87147474f, 0.54668033f);
    private const float SunIntensity = 2f;
    private const float SunShadowStrength = 0.715f;

    // 안개 투과율 확인 거리. 이 비율만 남으면 사실상 보이지 않는다고 본다.
    private static readonly float[] FogTransmittances = { 0.5f, 0.1f, 0.01f };

    [MenuItem("Tools/Map/10. 조명·안개·하늘 (데모 설정)")]
    private static void Run()
    {
        var skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
        if (skybox == null)
        {
            Debug.LogError($"[MapAtmosphereSetup] 데모 하늘 머티리얼을 찾지 못했습니다 : {SkyboxPath}");
            return;
        }

        VolumeProfile profile = GetGameProfile();
        if (profile == null) return;

        ApplyVolume(profile);
        Light sun = ApplySun();
        ApplyFog();
        ApplySkyAndEnvironment(skybox, sun);
        ApplyCamera();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        LogFogDistances();
    }

    private static GameObject FindRoot(string objectName)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == objectName) return root;
        }
        return null;
    }

    // 우리 프로필이 없을 때만 데모 프로필을 복제한다. 이미 있으면 사용자가 조정한 값을 덮어쓰지 않도록 그대로 쓴다.
    // CopyAsset은 파일째 복사하므로 하위 에셋인 VolumeComponent(효과들)도 함께 복제된다.
    private static VolumeProfile GetGameProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GameVolumeProfilePath);
        if (profile == null)
        {
            if (!AssetDatabase.CopyAsset(DemoVolumeProfilePath, GameVolumeProfilePath))
            {
                Debug.LogError($"[MapAtmosphereSetup] 데모 프로필 복제 실패 : {DemoVolumeProfilePath} -> {GameVolumeProfilePath}");
                return null;
            }
            profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GameVolumeProfilePath);
            Debug.Log($"[MapAtmosphereSetup] 데모 프로필을 복제했습니다 : {GameVolumeProfilePath}");
        }
        else
        {
            Debug.Log($"[MapAtmosphereSetup] 기존 프로필을 그대로 씁니다 (덮어쓰지 않음) : {GameVolumeProfilePath}");
        }

        DisableDepthOfField(profile);
        LogProfileComponents(profile);
        return profile;
    }

    // 저격 게임이라 먼 대상이 흐려지면 안 되므로 피사계 심도는 항상 끈다
    private static void DisableDepthOfField(VolumeProfile profile)
    {
        if (!profile.TryGet(out DepthOfField depthOfField))
        {
            Debug.Log("[MapAtmosphereSetup] 프로필에 피사계 심도가 없습니다.");
            return;
        }

        if (depthOfField.active)
        {
            depthOfField.active = false;
            EditorUtility.SetDirty(depthOfField);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("[MapAtmosphereSetup] 피사계 심도가 켜져 있어 껐습니다.");
        }
        else
        {
            Debug.Log("[MapAtmosphereSetup] 피사계 심도는 이미 꺼져 있습니다.");
        }
    }

    private static void LogProfileComponents(VolumeProfile profile)
    {
        foreach (VolumeComponent component in profile.components)
        {
            Debug.Log($"[MapAtmosphereSetup] 효과 {component.GetType().Name} : {(component.active ? "켜짐" : "꺼짐")}");
        }
    }

    // 전역 볼륨. sharedProfile로 연결해야 인스턴스를 만들지 않고 에셋을 그대로 참조한다.
    private static void ApplyVolume(VolumeProfile profile)
    {
        GameObject volumeObject = FindRoot(VolumeName);
        if (volumeObject == null) volumeObject = new GameObject(VolumeName);

        Volume volume = volumeObject.GetComponent<Volume>();
        if (volume == null) volume = volumeObject.AddComponent<Volume>();

        volume.isGlobal = true;
        volume.priority = 0f;
        volume.weight = 1f;
        volume.sharedProfile = profile;
        EditorUtility.SetDirty(volume);
        Debug.Log($"[MapAtmosphereSetup] 볼륨 '{VolumeName}' : 전역, 우선순위 0, 가중치 1, 프로필 {AssetDatabase.GetAssetPath(profile)}");
    }

    // 데모의 역광 (Rimlight)은 데모 씬에서 꺼져 있어 만들지 않는다
    private static Light ApplySun()
    {
        GameObject sunObject = FindRoot(SunName);
        if (sunObject == null) sunObject = new GameObject(SunName);

        Light sun = sunObject.GetComponent<Light>();
        if (sun == null) sun = sunObject.AddComponent<Light>();

        sun.type = LightType.Directional;
        sunObject.transform.rotation = Quaternion.Euler(SunEuler);
        sun.useColorTemperature = false;
        sun.color = SunColor;
        sun.intensity = SunIntensity;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = SunShadowStrength;
        EditorUtility.SetDirty(sun);
        EditorUtility.SetDirty(sunObject.transform);
        Debug.Log($"[MapAtmosphereSetup] 해 : 회전 {SunEuler}, 색 {SunColor}, 세기 {SunIntensity}, 부드러운 그림자 세기 {SunShadowStrength}");
        return sun;
    }

    private static void ApplyFog()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = DemoFogMode;
        RenderSettings.fogDensity = FogDensity;
        RenderSettings.fogColor = FogColor;
        Debug.Log($"[MapAtmosphereSetup] 안개 : {DemoFogMode}, 밀도 {FogDensity}, 색 {FogColor}");
    }

    private static void ApplySkyAndEnvironment(Material skybox, Light sun)
    {
        RenderSettings.skybox = skybox;
        // 하늘의 해 원반 위치를 해 방향과 맞춘다
        RenderSettings.sun = sun;

        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = AmbientIntensity;

        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.defaultReflectionResolution = ReflectionResolution;
        RenderSettings.reflectionIntensity = ReflectionIntensity;
        RenderSettings.reflectionBounces = ReflectionBounces;

        // 하늘을 바꾼 뒤 환경광과 반사를 새 하늘 기준으로 다시 계산한다
        DynamicGI.UpdateEnvironment();
        Debug.Log($"[MapAtmosphereSetup] 하늘 {skybox.name}, 환경광 Skybox 세기 {AmbientIntensity}, " +
                  $"반사 Skybox 해상도 {ReflectionResolution} 세기 {ReflectionIntensity} 반사 횟수 {ReflectionBounces}");
    }

    // 카메라는 플레이어 담당일 수 있어 새로 만들지 않고, 씬의 Main Camera 설정만 바꾼다. Far Clip은 바꾸지 않는다.
    private static void ApplyCamera()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            Debug.LogError("[MapAtmosphereSetup] MainCamera 태그가 붙은 카메라가 없어 후처리를 켜지 못했습니다.");
            return;
        }

        UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        // 데모 카메라와 같은 FXAA. 후처리가 켜져야 적용된다
        cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        EditorUtility.SetDirty(cameraData);
        Debug.Log($"[MapAtmosphereSetup] 카메라 '{camera.name}' : 후처리 켬, 안티에일리어싱 FXAA, Far Clip {camera.farClipPlane}m (변경 없음)");
    }

    // 성능 기준 대조용 (Far Clip, 나무 그리는 거리와 비교). 밀도를 바꿀 때마다 확인할 값이라 남겨 둔다.
    // Exp2 투과율 = exp(-(밀도 × 거리)²) 이므로 거리 = sqrt(-ln(투과율)) / 밀도
    private static void LogFogDistances()
    {
        foreach (float transmittance in FogTransmittances)
        {
            float distance = Mathf.Sqrt(-Mathf.Log(transmittance)) / FogDensity;
            Debug.Log($"[MapAtmosphereSetup] 안개 투과율 {transmittance * 100f:F0}% 거리 : {distance:F0}m");
        }

        Camera camera = Camera.main;
        if (camera == null) return;
        float farClip = camera.farClipPlane;
        float remaining = Mathf.Exp(-Mathf.Pow(FogDensity * farClip, 2f));
        Debug.Log($"[MapAtmosphereSetup] Far Clip {farClip}m 에서 남는 투과율 : {remaining * 100f:F2}%");
    }
}
