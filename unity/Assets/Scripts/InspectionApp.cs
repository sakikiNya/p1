using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;

[Serializable] public class IssuePosition { public float x, y, z; }
[Serializable] public class CreateIssue { public string title, description, priority; public IssuePosition position; }
[Serializable] public class IssueResponse { public string id, title, status; }
[Serializable] public class ApiError { public string code, message; }
[Serializable] public class ErrorResponse { public ApiError error; }

public class InspectionApp : MonoBehaviour
{
    public GameObject markerPrefab;
    public GameObject planePrefab;
    ARRaycastManager raycasts;
    ARPlaneManager planes;
    GameObject marker;
    InputField api, title, description;
    Dropdown priority;
    Button submit;
    Text feedback, tracking, coordinate;
    bool sending;
    Font font;
    readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
    readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    static readonly string[] Priorities = { "low", "medium", "high" };

    void Awake()
    {
        Application.targetFrameRate = 60;
        // Same AR Foundation scene graph is used by the Editor simulation and Android ARCore.
        var sessionObject = new GameObject("AR Session");
        sessionObject.AddComponent<ARSession>();
        sessionObject.AddComponent<ARInputManager>();
        var originObject = new GameObject("XR Origin");
        var origin = originObject.AddComponent<XROrigin>();
        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(originObject.transform, false);
        origin.CameraFloorOffsetObject = offset;
        var cameraObject = new GameObject("AR Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(offset.transform, false);
        var camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = .05f; camera.farClipPlane = 100f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<ARCameraManager>();
        cameraObject.AddComponent<ARCameraBackground>();
        var pose = cameraObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        pose.positionInput = new InputActionProperty(new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
        pose.rotationInput = new InputActionProperty(new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
        origin.Camera = camera;
        planes = originObject.AddComponent<ARPlaneManager>();
        planes.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        planes.planePrefab = planePrefab;
        raycasts = originObject.AddComponent<ARRaycastManager>();
        var lightObject = new GameObject("Light");
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1;
        lightObject.transform.rotation = Quaternion.Euler(45, 20, 0);
        BuildUI();
    }
    void OnEnable() { ARSession.stateChanged += SessionChanged; }
    void OnDisable() { ARSession.stateChanged -= SessionChanged; }
    void SessionChanged(ARSessionStateChangedEventArgs e)
    {
        if (!tracking) return;
        tracking.text = "AR: " + e.state + (e.state == ARSessionState.Unsupported ? " / 此设备不支持 AR" : " / 缓慢移动视角，寻找水平面");
    }
    bool OverUI(Vector2 point)
    {
        if (!EventSystem.current) return false;
        uiHits.Clear();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, uiHits);
        return uiHits.Count > 0;
    }
    void Update()
    {
        if (sending) return;
        Vector2 point;
        if (Input.touchCount > 0)
        {
            var touch = Input.GetTouch(0);
            if (touch.phase != TouchPhase.Began) return;
            point = touch.position;
        }
#if UNITY_EDITOR
        else if (Input.GetMouseButtonDown(0)) point = Input.mousePosition;
#endif
        else return;
        if (OverUI(point) || !raycasts.Raycast(point, hits, TrackableType.PlaneWithinPolygon)) return;
        var hit = hits[0];
        var plane = planes.GetPlane(hit.trackableId);
        if (plane == null || (plane.alignment != PlaneAlignment.HorizontalUp && plane.alignment != PlaneAlignment.HorizontalDown)) return;
        if (!marker) marker = Instantiate(markerPrefab);
        marker.transform.SetPositionAndRotation(hit.pose.position, hit.pose.rotation);
        var p = marker.transform.position;
        coordinate.text = $"位置（米） X {p.x:F3} / Y {p.y:F3} / Z {p.z:F3}";
        feedback.text = "标记已放置；再次点击平面可移动。";
    }
    public void Submit()
    {
        if (sending) return;
        if (!marker) { feedback.text = "请先点击检测到的水平面放置标记。"; return; }
        if (string.IsNullOrWhiteSpace(title.text)) { feedback.text = "标题不能为空。"; return; }
        if (!Uri.TryCreate(api.text.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) { feedback.text = "请填写有效的 HTTP/HTTPS 后端地址。"; return; }
        StartCoroutine(Send());
    }
    IEnumerator Send()
    {
        sending = true; SetInteractable(false); feedback.text = "正在上报…";
        var baseUrl = api.text.Trim().TrimEnd('/');
        PlayerPrefs.SetString("apiBaseUrl", baseUrl); PlayerPrefs.Save();
        var p = marker.transform.position;
        var payload = new CreateIssue { title = title.text.Trim(), description = description.text, priority = Priorities[priority.value], position = new IssuePosition { x = p.x, y = p.y, z = p.z } };
        using (var request = new UnityWebRequest(baseUrl + "/issues", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json"); request.timeout = 15;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                IssueResponse issue = null;
                try { issue = JsonUtility.FromJson<IssueResponse>(request.downloadHandler.text); } catch (Exception) { }
                if (issue != null && !string.IsNullOrEmpty(issue.id))
                {
                    feedback.text = "上报成功，编号：" + issue.id;
                    Destroy(marker); marker = null; title.text = ""; description.text = ""; priority.value = 1;
                    coordinate.text = "尚未放置标记";
                }
                else feedback.text = "服务器响应无效，请先在管理端确认是否已创建，避免重复提交。";
            }
            else
            {
                var reason = request.error;
                try { var error = JsonUtility.FromJson<ErrorResponse>(request.downloadHandler.text); if (error?.error != null) reason = error.error.message; } catch (Exception) { }
                feedback.text = "上报失败：" + reason + "。表单和标记已保留；网络异常时请先查管理端再重试。";
            }
        }
        sending = false; SetInteractable(true);
    }
    void SetInteractable(bool enabled)
    {
        api.interactable = title.interactable = description.interactable = priority.interactable = submit.interactable = enabled;
    }
    Text Label(Transform parent, string text, Vector2 pos, Vector2 size, int fontSize = 23)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = pos; rt.sizeDelta = size;
        var label = go.GetComponent<Text>(); label.font = font; label.fontSize = fontSize; label.text = text; label.color = Color.white; label.raycastTarget = false; return label;
    }
    GameObject Box(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = pos; rt.sizeDelta = size;
        go.GetComponent<Image>().color = color; return go;
    }
    InputField Field(Transform parent, string placeholder, Vector2 pos, Vector2 size, bool multiline = false)
    {
        var go = Box(parent, placeholder, pos, size, new Color(.94f,.97f,.94f));
        var input = go.AddComponent<InputField>();
        var text = Label(go.transform, "", new Vector2(12,-8), size-new Vector2(24,16), 24); text.color = Color.black;
        var hint = Label(go.transform, placeholder, new Vector2(12,-8), size-new Vector2(24,16), 24); hint.color = Color.gray;
        input.textComponent = text; input.placeholder = hint; input.targetGraphic = go.GetComponent<Image>();
        input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
        return input;
    }
    void BuildUI()
    {
        font = Font.CreateDynamicFontFromOSFont(new[] { "Noto Sans CJK SC", "Noto Sans CJK JP", "Microsoft YaHei", "Droid Sans Fallback", "Arial" }, 24);
        if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(720,1280); scaler.matchWidthOrHeight = 0;
        var header = Box(canvasObject.transform, "Header", Vector2.zero, new Vector2(720,150), new Color(.05f,.18f,.14f,.92f));
#if UNITY_EDITOR
        Label(header.transform,"园区巡检 · Editor 模拟验证",new Vector2(20,-16),new Vector2(680,40));
#else
        Label(header.transform,"园区巡检 · AR 上报",new Vector2(20,-16),new Vector2(680,40));
#endif
        tracking = Label(header.transform,"AR 初始化中…",new Vector2(20,-60),new Vector2(680,70),20);
        var panel = Box(canvasObject.transform, "Form", Vector2.zero, new Vector2(720,565),new Color(.05f,.18f,.14f,.95f));
        var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0,0); rect.pivot = Vector2.zero;
        api = Field(panel.transform,"Go API 地址，如 https://域名/api",new Vector2(20,-15),new Vector2(680,52));
        var config = Resources.Load<TextAsset>("api-config");
        api.text = PlayerPrefs.GetString("apiBaseUrl",config ? config.text.Trim() : "http://127.0.0.1:8080");
        title = Field(panel.transform,"问题标题（必填）",new Vector2(20,-80),new Vector2(680,52)); title.characterLimit = 200;
        description = Field(panel.transform,"描述（可选）",new Vector2(20,-145),new Vector2(680,80),true); description.characterLimit = 2000;
        Label(panel.transform,"优先级",new Vector2(20,-242),new Vector2(120,40));
        // Copy the built-in Dropdown template created in the Editor; it includes a scrolling options list.
        var dropdownPrefab = Resources.Load<GameObject>("PriorityDropdown");
        var dropdownObject = Instantiate(dropdownPrefab,panel.transform,false);
        var dropRect = dropdownObject.GetComponent<RectTransform>(); dropRect.anchorMin = dropRect.anchorMax = new Vector2(0,1); dropRect.pivot = new Vector2(0,1); dropRect.anchoredPosition = new Vector2(150,-238); dropRect.sizeDelta = new Vector2(550,50);
        priority = dropdownObject.GetComponent<Dropdown>(); priority.ClearOptions(); priority.AddOptions(new List<string>{"低 · low","中 · medium","高 · high"}); priority.value = 1;
        foreach (var label in dropdownObject.GetComponentsInChildren<Text>(true)) { label.font = font; label.fontSize = 22; }
        coordinate = Label(panel.transform,"尚未放置标记",new Vector2(20,-305),new Vector2(680,34),20);
        var button = Box(panel.transform,"Submit",new Vector2(20,-350),new Vector2(680,60),new Color(.16f,.5f,.34f));
        submit = button.AddComponent<Button>(); submit.targetGraphic = button.GetComponent<Image>(); submit.onClick.AddListener(Submit);
        var buttonText = Label(button.transform,"提交问题",Vector2.zero,new Vector2(680,60),26); buttonText.alignment = TextAnchor.MiddleCenter;
        feedback = Label(panel.transform,"移动视角识别平面，在表单外点击放置标记。",new Vector2(20,-430),new Vector2(680,120),22);
    }
}
