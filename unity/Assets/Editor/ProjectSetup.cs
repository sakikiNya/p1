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
public static class ProjectSetup
{
    const string ScenePath = "Assets/Scenes/Inspection.unity";
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
        // Use the Input System for touch, UI and XR Simulation.
        var player = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        var input = player.FindProperty("activeInputHandler"); if (input != null) { input.intValue = 1; player.ApplyModifiedProperties(); }
        var gradleSettings = player.FindProperty("useCustomGradleSettingsTemplate"); if (gradleSettings != null) { gradleSettings.boolValue = true; player.ApplyModifiedProperties(); }
        if (!File.Exists("Assets/Resources/PriorityDropdown.prefab"))
        {
            var dropdown = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            PrefabUtility.SaveAsPrefabAsset(dropdown, "Assets/Resources/PriorityDropdown.prefab"); UnityEngine.Object.DestroyImmediate(dropdown);
        }
        // Upgrade existing generated materials too: old prefabs survive Setup reruns.
        // Standard/Unlit-Color passes are incompatible with the template's URP renderer.
        var markerMaterial = EnsureMarkerMaterial("Assets/Prefabs/Marker.mat", new Color(1,.35f,.05f), 4000);
        var planeMaterial = EnsureGridMaterial("Assets/Prefabs/Plane.mat", new Color(.18f,.65f,.43f,.1f), .1f, .002f, 3000, CompareFunction.LessEqual);
        var marker = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Marker.prefab");
        if (!marker)
        {
            var root = new GameObject("Marker");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(root.transform,false);
            cube.transform.localPosition = new Vector3(0,.05f,0); cube.transform.localScale = Vector3.one*.1f;
            cube.GetComponent<Renderer>().sharedMaterial = markerMaterial;
            marker = PrefabUtility.SaveAsPrefabAsset(root,"Assets/Prefabs/Marker.prefab"); UnityEngine.Object.DestroyImmediate(root);
        }
        UpgradeMarkerVisual(markerMaterial);
        var plane = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Plane.prefab");
        if (!plane)
        {
            var root = new GameObject("Detected Horizontal Plane",typeof(ARPlane),typeof(MeshFilter),typeof(MeshRenderer),typeof(ARPlaneMeshVisualizer));
            root.GetComponent<MeshRenderer>().sharedMaterial = planeMaterial;
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
    static Material EnsureMarkerMaterial(string path, Color color, int queue)
    {
        var shader = Shader.Find("Inspection/MarkerOverlay");
        if (!shader) throw new Exception("Marker overlay shader is missing.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.shaderKeywords = Array.Empty<string>();
        material.SetColor("_BaseColor", color);
        material.renderQueue = queue;
        EditorUtility.SetDirty(material);
        return material;
    }
    static void UpgradeMarkerVisual(Material material)
    {
        var baseMaterial = EnsureGridMaterial("Assets/Prefabs/MarkerBase.mat", new Color(.05f,.9f,1,.1f), .015f, .0005f, 3999, CompareFunction.Always);
        const string path = "Assets/Prefabs/Marker.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var cube = root.transform.Find("Cube");
            if (!cube) throw new Exception("Generated Marker prefab is missing its Cube child.");
            cube.localScale = Vector3.one * .07f;
            cube.localPosition = new Vector3(0,.075f,0);
            cube.localRotation = Quaternion.Euler(15,0,15);
            cube.GetComponent<Renderer>().sharedMaterial = material;
            var visual = root.GetComponent<InspectionMarkerVisual>();
            if (!visual) visual = root.AddComponent<InspectionMarkerVisual>();
            visual.floatingVisual = cube;
            var markerBase = root.transform.Find("Visibility Base");
            if (!markerBase)
            {
                var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                markerBase = cylinder.transform;
                markerBase.name = "Visibility Base";
                markerBase.SetParent(root.transform, false);
                UnityEngine.Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            }
            markerBase.GetComponent<MeshFilter>().sharedMesh = MarkerBaseDisk();
            markerBase.localPosition = new Vector3(0,.00375f,0);
            markerBase.localScale = new Vector3(.12f,1,.12f);
            markerBase.GetComponent<Renderer>().sharedMaterial = baseMaterial;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.allowOcclusionWhenDynamic = false;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static Material EnsureGridMaterial(string path, Color color, float spacing, float width, int queue, CompareFunction depthTest)
    {
        var shader = Shader.Find("Inspection/TransparentGrid");
        if (!shader) throw new Exception("Transparent grid shader is missing.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material,path);
        }
        material.shader = shader;
        material.shaderKeywords = Array.Empty<string>();
        material.SetColor("_BaseColor",color);
        material.SetFloat("_GridSpacing",spacing);
        material.SetFloat("_LineWidth",width);
        material.SetFloat("_ZTest",(float)depthTest);
        material.renderQueue = queue;
        EditorUtility.SetDirty(material);
        return material;
    }
    static Mesh MarkerBaseDisk()
    {
        const string path = "Assets/Prefabs/MarkerBaseDisk.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh) return mesh;
        const int segments = 64;
        var vertices = new Vector3[segments+1];
        var triangles = new int[segments*3];
        for (int i=0;i<segments;i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            vertices[i+1] = new Vector3(Mathf.Cos(angle)*.5f,0,Mathf.Sin(angle)*.5f);
            triangles[i*3] = 0;
            triangles[i*3+1] = (i+1)%segments+1;
            triangles[i*3+2] = i+1;
        }
        mesh = new Mesh { name = "Marker Base Single Surface", vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh,path);
        return mesh;
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
    [MenuItem("Inspection/Build x86_64 Simulation APK")]
    public static void BuildSimulation()
    {
        Setup();
        SimulationMaterial("SimulationGround", new Color(.22f,.43f,.39f));
        SimulationMaterial("SimulationGrid", new Color(.4f,.65f,.59f));
        SimulationMaterial("SimulationMarker", new Color(1,.35f,.05f));
        EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.k_SettingsKey, out var targets);
        var settings = targets.SettingsForBuildTarget(BuildTargetGroup.Android);
        var originalInit = settings.InitManagerOnStart;
        var originalArch = PlayerSettings.Android.targetArchitectures;
        var originalId = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
        var originalName = PlayerSettings.productName;
        try
        {
            settings.InitManagerOnStart = false;
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.X86_64;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "cn.inspection.prototype.simulation");
            PlayerSettings.productName = "Inspection Simulation";
            Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[]{ScenePath}, locationPathName = "Builds/inspection-x86-simulation.apk",
                target = BuildTarget.Android, options = BuildOptions.Development,
                extraScriptingDefines = new[]{"INSPECTION_SIMULATION"}
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Simulation build failed: " + report.summary.result);
        }
        finally
        {
            settings.InitManagerOnStart = originalInit;
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
            PlayerSettings.Android.targetArchitectures = originalArch;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, originalId);
            PlayerSettings.productName = originalName;
            AssetDatabase.SaveAssets();
        }
    }
    static void SimulationMaterial(string name, Color color)
    {
        EnsureUnlitMaterial("Assets/Resources/" + name + ".mat", color);
    }
    static Material EnsureUnlitMaterial(string path, Color color)
    {
        var shaderName = GraphicsSettings.currentRenderPipeline ? "Universal Render Pipeline/Unlit" : "Unlit/Color";
        var shader = Shader.Find(shaderName);
        if (!shader) throw new Exception("Required marker shader not found: " + shaderName);
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.shaderKeywords = Array.Empty<string>();
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1);
        material.renderQueue = -1;
        EditorUtility.SetDirty(material);
        return material;
    }

}
