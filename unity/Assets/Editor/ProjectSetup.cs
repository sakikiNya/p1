using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

// Generates serialized Unity assets with the installed Editor instead of hand-writing
// version-dependent scene/prefab YAML. Safe to rerun; existing scenes are preserved.
[InitializeOnLoad]
public static class ProjectSetup
{
    const string ScenePath = "Assets/Scenes/Inspection.unity";
    static ProjectSetup()
    {
        EditorApplication.delayCall += () => {
            if (!Application.isBatchMode && !File.Exists(ScenePath)) Setup();
        };
    }
    [MenuItem("Inspection/Setup Project")]
    public static void Setup()
    {
        Directory.CreateDirectory("Assets/Scenes"); Directory.CreateDirectory("Assets/Prefabs");
        Directory.CreateDirectory("Assets/Resources"); Directory.CreateDirectory("Assets/XR"); AssetDatabase.Refresh();
        ConfigureXR();
        var arcore = UnityEditor.XR.ARCore.ARCoreSettings.currentSettings;
        if (!arcore)
        {
            arcore = ScriptableObject.CreateInstance<UnityEditor.XR.ARCore.ARCoreSettings>();
            AssetDatabase.CreateAsset(arcore,"Assets/XR/ARCoreSettings.asset");
            UnityEditor.XR.ARCore.ARCoreSettings.currentSettings = arcore;
        }
        arcore.requirement = UnityEditor.XR.ARCore.ARCoreSettings.Requirement.Required;
        arcore.depth = UnityEditor.XR.ARCore.ARCoreSettings.Requirement.Optional;
        PlayerSettings.companyName = "Inspection"; PlayerSettings.productName = "Park Inspection";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "cn.inspection.prototype");
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.stripEngineCode = false;
        // Both: legacy touch/UI plus Input System required by XR Simulation.
        var player = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        var input = player.FindProperty("activeInputHandler"); if (input != null) { input.intValue = 2; player.ApplyModifiedProperties(); }
        if (!File.Exists("Assets/Resources/PriorityDropdown.prefab"))
        {
            var dropdown = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            PrefabUtility.SaveAsPrefabAsset(dropdown, "Assets/Resources/PriorityDropdown.prefab"); UnityEngine.Object.DestroyImmediate(dropdown);
        }
        var marker = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Marker.prefab");
        if (!marker)
        {
            var root = new GameObject("Marker");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(root.transform,false);
            cube.transform.localPosition = new Vector3(0,.05f,0); cube.transform.localScale = Vector3.one*.1f;
            var material = new Material(Shader.Find("Standard")); material.color = new Color(1,.35f,.05f);
            AssetDatabase.CreateAsset(material,"Assets/Prefabs/Marker.mat"); cube.GetComponent<Renderer>().sharedMaterial = material;
            marker = PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/Marker.prefab"); UnityEngine.Object.DestroyImmediate(root);
        }
        var plane = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Plane.prefab");
        if (!plane)
        {
            var root = new GameObject("Detected Horizontal Plane",typeof(ARPlane),typeof(MeshFilter),typeof(MeshRenderer),typeof(ARPlaneMeshVisualizer));
            var material = new Material(Shader.Find("Unlit/Color")); material.color = new Color(.18f,.65f,.43f);
            AssetDatabase.CreateAsset(material,"Assets/Prefabs/Plane.mat"); root.GetComponent<MeshRenderer>().sharedMaterial = material;
            plane = PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/Plane.prefab"); UnityEngine.Object.DestroyImmediate(root);
        }
        if (!File.Exists(ScenePath))
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var app = new GameObject("Inspection App").AddComponent<InspectionApp>(); app.markerPrefab = marker; app.planePrefab = plane;
            EditorSceneManager.SaveScene(scene,ScenePath);
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath,true) };
        AssetDatabase.SaveAssets();
        Debug.Log("Inspection setup complete. Open Assets/Scenes/Inspection.unity. Check XR Simulation environment and XR Project Validation before building.");
    }
    static void ConfigureXR()
    {
        if (!EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.k_SettingsKey,out var targets))
        {
            targets = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(targets,"Assets/XR/XRSettings.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey,targets,true);
        }
        ConfigureTarget(targets,BuildTargetGroup.Standalone,"UnityEngine.XR.Simulation.SimulationLoader");
        ConfigureTarget(targets,BuildTargetGroup.Android,"UnityEngine.XR.ARCore.ARCoreLoader");
    }
    static void ConfigureTarget(XRGeneralSettingsPerBuildTarget targets, BuildTargetGroup group, string loader)
    {
        if (!targets.HasSettingsForBuildTarget(group)) targets.CreateDefaultSettingsForBuildTarget(group);
        if (!targets.HasManagerSettingsForBuildTarget(group)) targets.CreateDefaultManagerSettingsForBuildTarget(group);
        var settings = targets.SettingsForBuildTarget(group); settings.InitManagerOnStart = true;
        if (!XRPackageMetadataStore.AssignLoader(settings.Manager,loader,group)) throw new Exception("无法启用 XR Loader: " + loader);
        EditorUtility.SetDirty(settings); EditorUtility.SetDirty(settings.Manager); EditorUtility.SetDirty(targets);
    }
    [MenuItem("Inspection/Build Android Development APK")]
    public static void BuildAndroid()
    {
        Setup(); Directory.CreateDirectory("Builds");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[]{ScenePath}, locationPathName="Builds/inspection-dev.apk", target=BuildTarget.Android, options=BuildOptions.Development });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Android build failed: " + report.summary.result);
    }
}
