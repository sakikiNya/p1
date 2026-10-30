using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Exercise the real prefab with a foreground wall. No AR subsystem or API is used.
public static class MarkerRenderValidation
{
    [Serializable]
    class Result
    {
        public string pipeline, shader;
        public int normalUnblockedOrangePixels, normalBlockedOrangePixels, overlayOrangePixels;
        public int animatedOrangePixels, renderQueue;
        public float cubeWidthMeters, baseWidthMeters, baseOpacity, planeOpacity, baseMeshHeight;
        public int planeGridChangedPixels, baseInteriorChangedPixels, baseInteriorPixels;
        public bool animationMoved, anchorUnchanged;
    }

    static Color32[] lastPixels;

    [MenuItem("Inspection/Validate Marker Rendering")]
    public static void Validate()
    {
        if (EditorApplication.isPlaying) throw new Exception("Stop Play mode before validating marker rendering.");
        if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
            throw new Exception("Run this regression check in the URP Android project.");
        ProjectSetup.Setup();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Marker.prefab");
        var scene = EditorSceneManager.NewPreviewScene();
        var texture = new RenderTexture(256,256,24,RenderTextureFormat.ARGB32);
        Material normal = null, wallMaterial = null;
        var previousTarget = RenderTexture.active;
        try
        {
            texture.Create();
            var marker = UnityEngine.Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(marker,scene);
            foreach (var child in marker.GetComponentsInChildren<Transform>()) child.gameObject.layer = 31;
            var visual = marker.GetComponent<InspectionMarkerVisual>();
            if (!visual || !visual.floatingVisual) throw new Exception("Animated visual is missing from prefab.");
            visual.Animate(0);
            var anchor = marker.transform.position;
            var visualPosition = visual.floatingVisual.localPosition;
            var visualRotation = visual.floatingVisual.localRotation;
            var renderers = marker.GetComponentsInChildren<Renderer>();
            var materials = new Material[renderers.Length][];
            for (var i=0;i<renderers.Length;i++) materials[i]=renderers[i].sharedMaterials;
            var markerMaterial = visual.floatingVisual.GetComponent<Renderer>().sharedMaterial;

            var cameraObject = new GameObject("Marker Validation Camera",typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera = cameraObject.GetComponent<Camera>();
            camera.scene=scene;
            camera.enabled=false;
            camera.cullingMask=1<<31;
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=Color.black;
            camera.orthographic=true;
            camera.orthographicSize=.5f;
            camera.aspect=1;
            camera.nearClipPlane=.01f;
            camera.farClipPlane=5;
            camera.transform.position=new Vector3(0,.6f,-1.4f);
            camera.transform.LookAt(new Vector3(0,.24f,0));
            var cameraData=camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing=false;
            cameraData.renderShadows=false;

            normal=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            normal.SetColor("_BaseColor",new Color(1,.35f,.05f));
            wallMaterial=new Material(normal);
            wallMaterial.SetColor("_BaseColor",new Color(.08f,.16f,.24f));
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name="Foreground occluder";
            wall.layer=31;
            SceneManager.MoveGameObjectToScene(wall,scene);
            wall.transform.position=new Vector3(0,.35f,-.7f);
            wall.transform.localScale=new Vector3(2,2,.04f);
            wall.GetComponent<Renderer>().sharedMaterial=wallMaterial;
            var result=new Result {
                pipeline=GraphicsSettings.currentRenderPipeline.name,
                shader=markerMaterial.shader.name,
                renderQueue=markerMaterial.renderQueue,
                cubeWidthMeters=visual.floatingVisual.localScale.x,
                baseWidthMeters=marker.transform.Find("Visibility Base").localScale.x
            };
            foreach (var renderer in renderers) renderer.sharedMaterial=normal;
            wall.SetActive(false);
            result.normalUnblockedOrangePixels=Render(camera,texture,"Builds/marker-normal-unblocked.png");
            wall.SetActive(true);
            result.normalBlockedOrangePixels=Render(camera,texture,"Builds/marker-normal-blocked.png");
            for (var i=0;i<renderers.Length;i++) renderers[i].sharedMaterials=materials[i];
            result.overlayOrangePixels=Render(camera,texture,"Builds/marker-overlay.png");
            visual.Animate(.25f);
            result.animationMoved=Vector3.Distance(visualPosition,visual.floatingVisual.localPosition)>.01f
                && Quaternion.Angle(visualRotation,visual.floatingVisual.localRotation)>1;
            result.anchorUnchanged=marker.transform.position==anchor;
            result.animatedOrangePixels=Render(camera,texture,"Builds/marker-overlay-animated.png");
            var baseRenderer = marker.transform.Find("Visibility Base").GetComponent<Renderer>();
            var planeMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Prefabs/Plane.mat");
            result.baseOpacity = baseRenderer.sharedMaterial.GetColor("_BaseColor").a;
            result.planeOpacity = planeMaterial.GetColor("_BaseColor").a;
            result.baseMeshHeight = baseRenderer.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;
            // Compare with the same camera background: grid holes must retain its pixels.
            wall.SetActive(false); marker.SetActive(false);
            camera.transform.SetPositionAndRotation(Vector3.up,Quaternion.Euler(90,0,0));
            camera.backgroundColor = new Color(.1f,.2f,.3f);
            Render(camera,texture,"Builds/grid-background.png");
            var baseline = lastPixels;
            var grid = GameObject.CreatePrimitive(PrimitiveType.Plane);
            SceneManager.MoveGameObjectToScene(grid,scene);
            grid.layer = 31; grid.transform.localScale = Vector3.one*.1f;
            grid.GetComponent<Renderer>().sharedMaterial = planeMaterial;
            Render(camera,texture,"Builds/grid-detected-plane.png");
            result.planeGridChangedPixels = ChangedPixels(baseline,lastPixels,0,out _);
            grid.SetActive(false);
            camera.orthographicSize = .07f;
            Render(camera,texture,"Builds/grid-base-background.png");
            baseline = lastPixels;
            marker.SetActive(true); visual.floatingVisual.gameObject.SetActive(false);
            Render(camera,texture,"Builds/grid-marker-base.png");
            result.baseInteriorChangedPixels = ChangedPixels(baseline,lastPixels,80,out result.baseInteriorPixels);
            File.WriteAllText("Builds/marker-grid-validation.json",JsonUtility.ToJson(result,true));
            if (result.normalUnblockedOrangePixels<100 || result.normalBlockedOrangePixels!=0
                || result.overlayOrangePixels<100 || result.animatedOrangePixels<100
                || result.renderQueue<4000 || Mathf.Abs(result.cubeWidthMeters-.07f)>.0001f
                || Mathf.Abs(result.baseWidthMeters-.12f)>.0001f
                || Mathf.Abs(result.baseOpacity-.1f)>.0001f || Mathf.Abs(result.planeOpacity-.1f)>.0001f
                || result.baseMeshHeight>.00001f || result.planeGridChangedPixels<100
                || result.planeGridChangedPixels>256*256*.45f || result.baseInteriorChangedPixels<30
                || result.baseInteriorChangedPixels>result.baseInteriorPixels*.5f
                || !result.animationMoved || !result.anchorUnchanged)
                throw new Exception("Small grid marker validation failed: "+JsonUtility.ToJson(result));
            Debug.Log("Small grid marker validation PASS: "+JsonUtility.ToJson(result));
        }
        finally
        {
            RenderTexture.active=previousTarget;
            if (normal) UnityEngine.Object.DestroyImmediate(normal);
            if (wallMaterial) UnityEngine.Object.DestroyImmediate(wallMaterial);
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static int Render(Camera camera,RenderTexture target,string path)
    {
        var request=new UniversalRenderPipeline.SingleCameraRequest { destination=target };
        if (!RenderPipeline.SupportsRenderRequest(camera,request)) throw new Exception("URP render request unavailable.");
        RenderPipeline.SubmitRenderRequest(camera,request);
        var readback=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        try
        {
            RenderTexture.active=target;
            readback.ReadPixels(new Rect(0,0,target.width,target.height),0,0);
            readback.Apply();
            var orange=0;
            lastPixels = readback.GetPixels32();
            foreach (var pixel in lastPixels)
                if (pixel.r>180 && pixel.g>40 && pixel.g<200 && pixel.b<100) orange++;
            Directory.CreateDirectory("Builds");
            File.WriteAllBytes(path,readback.EncodeToPNG());
            return orange;
        }
        finally { UnityEngine.Object.DestroyImmediate(readback); }
    }
    static int ChangedPixels(Color32[] before,Color32[] after,int radius,out int total)
    {
        int changed=0; total=0;
        for (int i=0;i<before.Length;i++)
        {
            int x=i%256-128, y=i/256-128;
            if (radius>0 && x*x+y*y>=radius*radius) continue;
            total++;
            if (Mathf.Abs(before[i].r-after[i].r)+Mathf.Abs(before[i].g-after[i].g)+Mathf.Abs(before[i].b-after[i].b)>2)
                changed++;
        }
        return changed;
    }

}
