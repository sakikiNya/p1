using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
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
    Dropdown priority, apiMode;
    string defaultApiBaseUrl;
    RectTransform formRect, formBody;
    const string ApiModeKey = "apiAddressMode";
    const string CustomApiKey = "customApiBaseUrl";
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
#if INSPECTION_SIMULATION
        BuildSimulation();
#else
        // Same AR Foundation scene graph is used by the Editor simulation and Android ARCore.
        var sessionObject = new GameObject("AR Session");
        sessionObject.AddComponent<ARSession>();
        sessionObject.AddComponent<ARInputManager>();
        var originObject = new GameObject("XR Origin");
        originObject.SetActive(false);
        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(originObject.transform, false);
        var cameraObject = new GameObject("AR Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(offset.transform, false);
        var camera = cameraObject.AddComponent<Camera>();
        var origin = originObject.AddComponent<XROrigin>();
        origin.CameraFloorOffsetObject = offset;
        origin.Camera = camera;
        camera.nearClipPlane = .05f; camera.farClipPlane = 100f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<ARCameraManager>();
        cameraObject.AddComponent<ARCameraBackground>();
        var pose = cameraObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        pose.positionInput = new InputActionProperty(new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
        pose.rotationInput = new InputActionProperty(new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
        planes = originObject.AddComponent<ARPlaneManager>();
        planes.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        planes.planePrefab = planePrefab;
        raycasts = originObject.AddComponent<ARRaycastManager>();
        originObject.SetActive(true);
#endif
        var lightObject = new GameObject("Light");
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1;
        lightObject.transform.rotation = Quaternion.Euler(45, 20, 0);
        BuildUI();
#if INSPECTION_SIMULATION
        tracking.text = "虚拟水平面 · 坐标用于流程测试，不代表真实环境";
        feedback.text = "点击上方虚拟地面放置标记，再填写表单上报测试数据。";
#endif
    }
#if INSPECTION_SIMULATION
    void BuildSimulation()
    {
        var cameraObject = new GameObject("Simulation Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.11f,.16f,.22f);
        camera.transform.position = new Vector3(0, 3, -3);
        camera.transform.LookAt(new Vector3(0, 0, 1));
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Virtual Test Plane";
        ground.transform.localScale = Vector3.one * 2;
        ground.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("SimulationGround");
        for (int i = -8; i <= 8; i++)
        {
            SimulationGridLine(new Vector3(i,.002f,0), new Vector3(.015f,.003f,16));
            SimulationGridLine(new Vector3(0,.002f,i), new Vector3(16,.003f,.015f));
        }
    }
    void SimulationGridLine(Vector3 position, Vector3 scale)
    {
        var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        line.name = "Grid"; line.transform.position = position; line.transform.localScale = scale;
        line.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("SimulationGrid");
        Destroy(line.GetComponent<Collider>());
    }
#endif
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
#if INSPECTION_SIMULATION
        return; // Simulation placement is routed through the UI pointer event system.
#else
        if (sending) return;
        Vector2 point;
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            point = Touchscreen.current.primaryTouch.position.ReadValue();
#if UNITY_EDITOR
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            point = Mouse.current.position.ReadValue();
#endif
        else return;
        if (OverUI(point)) return;
        PlaceAt(point);
#endif
    }
    public void PlaceAt(Vector2 point)
    {
        if (sending) return;
#if INSPECTION_SIMULATION
        var ray = Camera.main.ScreenPointToRay(point);
        if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var distance)) return;
        var pose = new Pose(ray.GetPoint(distance), Quaternion.identity);
        if (Mathf.Abs(pose.position.x) > 10 || Mathf.Abs(pose.position.z) > 10) return;
#else
        if (!raycasts.Raycast(point, hits, TrackableType.PlaneWithinPolygon)) return;
        var hit = hits[0];
        var plane = planes.GetPlane(hit.trackableId);
        if (plane == null || (plane.alignment != PlaneAlignment.HorizontalUp && plane.alignment != PlaneAlignment.HorizontalDown)) return;
        var pose = hit.pose;
#endif
        if (!marker) marker = Instantiate(markerPrefab);
#if INSPECTION_SIMULATION
        foreach (var renderer in marker.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = Resources.Load<Material>("SimulationMarker");
#endif
        marker.transform.SetPositionAndRotation(pose.position, pose.rotation);
        var p = marker.transform.position;
        coordinate.text = $"位置（米） X {p.x:F3} / Y {p.y:F3} / Z {p.z:F3}";
        feedback.text = "标记已放置；再次点击平面可移动。";
    }
    public void Submit()
    {
        if (sending) return;
        if (!marker) { feedback.text = "请先点击检测到的水平面放置标记。"; return; }
        if (string.IsNullOrWhiteSpace(title.text)) { feedback.text = "标题不能为空。"; return; }
        if (!Uri.TryCreate(SelectedApiBaseUrl(), UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) { feedback.text = "请填写有效的 HTTP/HTTPS 后端地址。"; return; }
        StartCoroutine(Send());
    }
    IEnumerator Send()
    {
        sending = true; SetInteractable(false); feedback.text = "正在上报…";
        var baseUrl = SelectedApiBaseUrl();
        SaveApiSelection();
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
        apiMode.interactable = api.interactable = title.interactable = description.interactable = priority.interactable = submit.interactable = enabled;
    }
    string SelectedApiBaseUrl()
    {
        return (apiMode.value == 0 ? defaultApiBaseUrl : api.text).Trim().TrimEnd('/');
    }
    void SaveApiSelection()
    {
        PlayerPrefs.SetInt(ApiModeKey, apiMode.value);
        PlayerPrefs.SetString(CustomApiKey, api.text.Trim());
        PlayerPrefs.Save();
    }
    void ApplyApiMode()
    {
        var custom = apiMode.value == 1;
        api.gameObject.SetActive(custom);
        formRect.sizeDelta = new Vector2(720, custom ? 630 : 565);
        formBody.anchoredPosition = new Vector2(0, custom ? -145 : -80);
    }
    Dropdown DropdownField(Transform parent, string name, Vector2 pos, Vector2 size, List<string> options)
    {
        var go = Instantiate(Resources.Load<GameObject>("PriorityDropdown"), parent, false);
        go.name = name;
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0,1); rect.pivot = new Vector2(0,1);
        rect.anchoredPosition = pos; rect.sizeDelta = size;
        var dropdown = go.GetComponent<Dropdown>(); dropdown.ClearOptions(); dropdown.AddOptions(options);
        foreach (var label in go.GetComponentsInChildren<Text>(true)) { label.font = font; label.fontSize = 22; }
        // The built-in template has 20px rows, which clips the larger Chinese font.
        var item = dropdown.itemText.transform.parent.GetComponent<RectTransform>();
        item.sizeDelta = new Vector2(item.sizeDelta.x, 52);
        var content = item.parent.GetComponent<RectTransform>();
        content.sizeDelta = new Vector2(content.sizeDelta.x, 60);
        dropdown.itemText.rectTransform.offsetMin = new Vector2(30, 4);
        dropdown.itemText.rectTransform.offsetMax = new Vector2(-10, -4);
        dropdown.template.sizeDelta = new Vector2(0, Mathf.Min(180, options.Count * 52 + 8));
        var oldArrow = go.transform.Find("Arrow");
        if (oldArrow) oldArrow.gameObject.SetActive(false);
        var arrow = Label(go.transform, "▼", new Vector2(size.x - 38, -12), new Vector2(28, 28), 20);
        arrow.color = new Color(.2f,.2f,.2f);
        return dropdown;
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
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(720,1280); scaler.matchWidthOrHeight = 0;
#if INSPECTION_SIMULATION
        var surface = Box(canvasObject.transform,"Simulation Placement Surface",Vector2.zero,Vector2.zero,Color.clear);
        var surfaceRect = surface.GetComponent<RectTransform>();
        surfaceRect.anchorMin = Vector2.zero; surfaceRect.anchorMax = Vector2.one;
        surfaceRect.offsetMin = surfaceRect.offsetMax = Vector2.zero;
        surface.AddComponent<SimulationPlacementSurface>().app = this;
#endif
        var header = Box(canvasObject.transform, "Header", Vector2.zero, new Vector2(720,150), new Color(.05f,.18f,.14f,.92f));
#if INSPECTION_SIMULATION
        Label(header.transform,"园区巡检 · x86_64 模拟测试",new Vector2(20,-16),new Vector2(680,40));
#elif UNITY_EDITOR
        Label(header.transform,"园区巡检 · Editor 模拟验证",new Vector2(20,-16),new Vector2(680,40));
#else
        Label(header.transform,"园区巡检 · AR 上报",new Vector2(20,-16),new Vector2(680,40));
#endif
        tracking = Label(header.transform,"AR 初始化中…",new Vector2(20,-60),new Vector2(680,70),20);
        var panel = Box(canvasObject.transform, "Form", Vector2.zero, new Vector2(720,565),new Color(.05f,.18f,.14f,.95f));
        formRect = panel.GetComponent<RectTransform>(); formRect.anchorMin = formRect.anchorMax = new Vector2(0,0); formRect.pivot = Vector2.zero;
        Label(panel.transform,"API 地址",new Vector2(20,-23),new Vector2(140,40));
        apiMode = DropdownField(panel.transform,"API Address Mode",new Vector2(170,-15),new Vector2(530,52),new List<string>{"默认","自定义"});
        api = Field(panel.transform,"自定义 API 地址，如 https://域名/api",new Vector2(20,-80),new Vector2(680,52));
        api.contentType = InputField.ContentType.Standard;
        api.keyboardType = TouchScreenKeyboardType.URL;
        var config = Resources.Load<TextAsset>("api-config");
        defaultApiBaseUrl = config ? config.text.Trim() : "http://127.0.0.1:8080";
        // Keep the built-in endpoint separate from editable preferences. Existing addresses
        // remain available in Custom, while the first launch of this UI selects Default.
        api.text = PlayerPrefs.GetString(CustomApiKey, PlayerPrefs.GetString("apiBaseUrl", ""));
        apiMode.SetValueWithoutNotify(PlayerPrefs.GetInt(ApiModeKey, 0) == 1 ? 1 : 0);
        var body = new GameObject("Form Body", typeof(RectTransform)); body.transform.SetParent(panel.transform, false);
        formBody = body.GetComponent<RectTransform>(); formBody.anchorMin = formBody.anchorMax = new Vector2(0,1); formBody.pivot = new Vector2(0,1); formBody.sizeDelta = new Vector2(720,485);
        title = Field(body.transform,"问题标题（必填）",new Vector2(20,0),new Vector2(680,52)); title.characterLimit = 200;
        description = Field(body.transform,"描述（可选）",new Vector2(20,-65),new Vector2(680,80),true); description.characterLimit = 2000;
        Label(body.transform,"优先级",new Vector2(20,-162),new Vector2(120,40));
        priority = DropdownField(body.transform,"Priority",new Vector2(150,-158),new Vector2(550,50),new List<string>{"低 · low","中 · medium","高 · high"}); priority.value = 1;
        coordinate = Label(body.transform,"尚未放置标记",new Vector2(20,-225),new Vector2(680,34),20);
        var button = Box(body.transform,"Submit",new Vector2(20,-270),new Vector2(680,60),new Color(.16f,.5f,.34f));
        submit = button.AddComponent<Button>(); submit.targetGraphic = button.GetComponent<Image>(); submit.onClick.AddListener(Submit);
        var buttonText = Label(button.transform,"提交问题",Vector2.zero,new Vector2(680,60),26); buttonText.alignment = TextAnchor.MiddleCenter;
        feedback = Label(body.transform,"移动视角识别平面，在表单外点击放置标记。",new Vector2(20,-350),new Vector2(680,120),22);
        ApplyApiMode();
        apiMode.onValueChanged.AddListener(_ => { ApplyApiMode(); SaveApiSelection(); });
        api.onEndEdit.AddListener(_ => SaveApiSelection());
    }
}
#if INSPECTION_SIMULATION
public class SimulationPlacementSurface : MonoBehaviour, IPointerClickHandler
{
    public InspectionApp app;
    public void OnPointerClick(PointerEventData data) { app.PlaceAt(data.position); }
}
#endif
