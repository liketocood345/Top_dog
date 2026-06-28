using TopDog.Client;
using TopDog.Sim.Realtime;
using UnityEngine;
using UnityEngine.UIElements;
/*
 * ⚠️ 不要触动 — 实时交战宇宙背景（纯视觉层，不参与游戏逻辑/模拟）
 * 除非用户明确要求修改本背景功能，否则不要改动本文件及 CombatBackground* / CombatSpaceBackground* 链路。
 * ══ 设计手册嵌入 ══
 * 权威: docs/TACTICAL_VIEW.md §5.1 宇宙背景 · docs/CLIENT_GAME_SETTINGS.md §2.2
 * 本文件: CombatSpaceBackgroundCameraHost.cs — SG Cubemap 天空盒 → RT → UITK
 * 【机制要点】（对齐第二银河 SolarSystemCelestialLayerController + CameraTransformController）
 * · SG 六面 Cubemap → 内翻球/Skybox；Y/X 分层 orbit；背景随相机旋转
 * · UITK：Camera.Render → RenderTexture → art-viewport-bg
 * 【关联】CombatSpaceBackgroundPresenter · TacticalViewportCamera · CombatBackgroundCatalog
 * ══
 */

namespace TopDog.Client.Tactical;

/// <summary>SG-style cubemap skybox: Y/X camera rig renders to RT for UITK background slot.</summary>
public sealed class CombatSpaceBackgroundCameraHost : MonoBehaviour
{
    public const float BaseFieldOfView = BattlefieldSceneProxyService.TacticalEdgeBaseFovDeg;

    private const int BackgroundLayer = 30;
    private const float SkySphereRadius = 800f;
    private static readonly int TexId = Shader.PropertyToID("_Tex");

    private enum SkyRenderMode
    {
        None,
        SkyboxClear,
        InteriorSphere,
    }

    private Camera? _camera;
    private Transform? _yRotationRoot;
    private Transform? _xRotationRoot;
    private Transform? _skySphere;
    private MeshRenderer? _skyRenderer;
    private Skybox? _skyboxComponent;
    private RenderTexture? _renderTexture;
    private Material? _skyMaterial;
    private Cubemap? _activeCubemap;
    private Image? _rtImage;
    private VisualElement? _viewportHost;
    private VisualElement? _artSlot;
    private TacticalViewportCamera? _orbitSource;
    private SkyRenderMode _skyRenderMode;
    private bool _active;
    private bool _cameraReady;
    private bool _rtHasRenderedFrame;
    private bool _interiorFallbackTried;
    private string? _appliedSetId;
    private int _rtWidth;
    private int _rtHeight;

    public void Bind(VisualElement viewportHost, VisualElement artSlot, TacticalViewportCamera orbitSource)
    {
        _viewportHost = viewportHost;
        _artSlot = artSlot;
        _orbitSource = orbitSource;
        EnsureCameraRig();
        EnsureRtImage();
        _viewportHost.RegisterCallback<GeometryChangedEvent>(OnViewportGeometryChanged);
        _artSlot.AddToClassList("rtcombat-space-bg");
        _artSlot.pickingMode = PickingMode.Ignore;
        _artSlot.style.overflow = Overflow.Hidden;
        _artSlot.SendToBack();
    }

    public void SetActive(bool active)
    {
        _active = active;
        UpdateCameraEnabled();
        if (!active && _rtImage != null)
        {
            _rtImage.style.display = DisplayStyle.None;
        }
    }

    public void Refresh(string? setId)
    {
        if (string.IsNullOrEmpty(setId))
        {
            _appliedSetId = null;
            _cameraReady = false;
            _rtHasRenderedFrame = false;
            _interiorFallbackTried = false;
            ClearArtSlot();
            UpdateCameraEnabled();
            return;
        }

        EnsureCameraRig();
        EnsureRtImage();
        if (!setId.Equals(_appliedSetId, System.StringComparison.Ordinal))
        {
            _rtHasRenderedFrame = false;
            _interiorFallbackTried = false;
            var cubemap = CombatBackgroundCatalog.LoadCubemap(setId, mainPoolOnly: true);
            if (cubemap == null)
            {
                Debug.LogWarning("TopDog: combat sky cubemap missing for " + setId);
                _cameraReady = false;
                _appliedSetId = null;
                ClearArtSlot();
                return;
            }

            _cameraReady = ApplySkyMaterial(cubemap);
            if (!_cameraReady)
            {
                Debug.LogWarning("TopDog: combat skybox material unavailable for " + setId);
                _appliedSetId = null;
                ClearArtSlot();
                return;
            }

            _appliedSetId = setId;
            Debug.Log("TopDog: combat sky loaded " + setId + " (" + _skyRenderMode + ")");
        }

        EnsureRenderTexture();
        ApplyDisplayMode();
        UpdateCameraEnabled();
    }

    public float CurrentVerticalFovDeg => ClientGameSettings.CombatVerticalFovDeg;

    private void OnEnable()
    {
        ClientGameSettings.CombatBackgroundResolutionChanged += OnBackgroundResolutionChanged;
        ReconcileCameraRigAfterReload();
    }

    private void OnDisable()
    {
        ClientGameSettings.CombatBackgroundResolutionChanged -= OnBackgroundResolutionChanged;
        UpdateCameraEnabled();
    }

    private void OnDestroy()
    {
        if (_viewportHost != null)
        {
            _viewportHost.UnregisterCallback<GeometryChangedEvent>(OnViewportGeometryChanged);
        }

        if (_skyMaterial != null)
        {
            Destroy(_skyMaterial);
        }

        ReleaseRenderTexture();
    }

    private void LateUpdate()
    {
        if (!_active || _orbitSource == null || string.IsNullOrEmpty(_appliedSetId) || !_cameraReady || _camera == null)
        {
            return;
        }

        EnsureRenderTexture();
        if (_renderTexture == null)
        {
            return;
        }

        SyncOrbitAndZoom();
        if (_skyRenderMode == SkyRenderMode.InteriorSphere && _skySphere != null)
        {
            _skySphere.position = _camera.transform.position;
            _skySphere.rotation = Quaternion.identity;
        }
        else if (_skyRenderMode == SkyRenderMode.SkyboxClear && _skySphere != null)
        {
            _skySphere.position = _camera.transform.position;
            _skySphere.rotation = Quaternion.identity;
        }

        _camera.Render();

        if (!_interiorFallbackTried
            && _skyRenderMode == SkyRenderMode.InteriorSphere
            && _activeCubemap != null
            && _renderTexture != null
            && !RenderTextureHasSkyContent(_renderTexture))
        {
            _interiorFallbackTried = true;
            if (TryApplySkyboxClearMaterial(_activeCubemap))
            {
                _camera.Render();
            }
        }

        _rtHasRenderedFrame = true;
        if (_rtImage != null)
        {
            _rtImage.image = _renderTexture;
            _rtImage.MarkDirtyRepaint();
        }

        ApplyDisplayMode();
    }

    private void OnViewportGeometryChanged(GeometryChangedEvent _)
    {
        EnsureRenderTexture();
        ApplyDisplayMode();
    }

    private void EnsureRtImage()
    {
        if (_artSlot == null || _rtImage != null)
        {
            return;
        }

        _rtImage = new Image { name = "combat-bg-rt", pickingMode = PickingMode.Ignore };
        _rtImage.AddToClassList("rtcombat-space-bg-image");
        _rtImage.style.position = Position.Absolute;
        _rtImage.style.left = 0;
        _rtImage.style.right = 0;
        _rtImage.style.top = 0;
        _rtImage.style.bottom = 0;
        _rtImage.scaleMode = ScaleMode.StretchToFill;
        _artSlot.Add(_rtImage);
        _rtImage.SendToBack();
    }

    private bool IsCameraRigValid() =>
        _camera != null && _yRotationRoot != null && _xRotationRoot != null && _skyRenderer != null;

    private void EnsureCameraRig()
    {
        if (IsCameraRigValid())
        {
            EnsureSkyboxComponent();
            return;
        }

        var existing = transform.Find("CombatBackgroundWorld");
        if (existing != null)
        {
            Destroy(existing.gameObject);
        }

        _camera = null;
        _yRotationRoot = null;
        _xRotationRoot = null;
        _skySphere = null;
        _skyRenderer = null;
        _skyboxComponent = null;

        var root = new GameObject("CombatBackgroundWorld");
        root.transform.SetParent(transform, false);
        root.layer = BackgroundLayer;

        var yGo = new GameObject("YRotationRoot");
        yGo.transform.SetParent(root.transform, false);
        yGo.layer = BackgroundLayer;
        _yRotationRoot = yGo.transform;

        var xGo = new GameObject("XRotationRoot");
        xGo.transform.SetParent(_yRotationRoot, false);
        xGo.layer = BackgroundLayer;
        _xRotationRoot = xGo.transform;

        var camGo = new GameObject("CombatBackgroundCamera");
        camGo.transform.SetParent(_xRotationRoot, false);
        camGo.layer = BackgroundLayer;
        _camera = camGo.AddComponent<Camera>();
        _camera.orthographic = false;
        _camera.clearFlags = CameraClearFlags.Skybox;
        _camera.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 1f);
        _camera.cullingMask = 1 << BackgroundLayer;
        _camera.depth = 0;
        _camera.fieldOfView = BaseFieldOfView;
        _camera.nearClipPlane = 0.3f;
        _camera.farClipPlane = SkySphereRadius * 2f;
        _camera.enabled = false;
        _skyboxComponent = _camera.gameObject.AddComponent<Skybox>();

        var sphereGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphereGo.name = "CombatSkySphere";
        sphereGo.transform.SetParent(root.transform, false);
        sphereGo.layer = BackgroundLayer;
        Destroy(sphereGo.GetComponent<Collider>());
        _skySphere = sphereGo.transform;
        _skySphere.localScale = Vector3.one * (SkySphereRadius * 2f);
        _skyRenderer = sphereGo.GetComponent<MeshRenderer>();
        _skyRenderer.enabled = false;
    }

    private void ApplyDisplayMode()
    {
        if (_artSlot == null || _rtImage == null)
        {
            return;
        }

        var showRt = _cameraReady && _rtHasRenderedFrame && _renderTexture != null;
        _artSlot.style.backgroundImage = StyleKeyword.None;
        _artSlot.style.backgroundColor = new StyleColor(new Color(0.02f, 0.03f, 0.05f, 1f));
        _rtImage.style.display = showRt ? DisplayStyle.Flex : DisplayStyle.None;
        if (showRt)
        {
            _rtImage.image = _renderTexture;
        }
    }

    private void ReconcileCameraRigAfterReload()
    {
        if (IsCameraRigValid())
        {
            EnsureSkyboxComponent();
            return;
        }

        _camera = null;
        _yRotationRoot = null;
        _xRotationRoot = null;
        _skySphere = null;
        _skyRenderer = null;
        _skyboxComponent = null;
        _cameraReady = false;
        _rtHasRenderedFrame = false;
        _interiorFallbackTried = false;
        ReleaseRenderTexture();
    }

    private Skybox? EnsureSkyboxComponent()
    {
        if (_camera == null)
        {
            return null;
        }

        if (_skyboxComponent == null)
        {
            _skyboxComponent = _camera.GetComponent<Skybox>();
        }

        if (_skyboxComponent == null)
        {
            _skyboxComponent = _camera.gameObject.AddComponent<Skybox>();
        }

        return _skyboxComponent;
    }

    public void InvalidateAppliedSet()
    {
        _appliedSetId = null;
        _rtHasRenderedFrame = false;
        _interiorFallbackTried = false;
    }

    /// <summary>URP RT 探针全黑时回退到 Camera Skybox（内翻球在部分 URP 路径下 RT 为空）。</summary>
    private bool TryApplySkyboxClearMaterial(Cubemap cubemap)
    {
        var skyboxShader = Shader.Find("Skybox/Cubemap");
        if (skyboxShader == null || _camera == null)
        {
            return false;
        }

        if (_skyMaterial == null)
        {
            _skyMaterial = new Material(skyboxShader);
        }
        else
        {
            _skyMaterial.shader = skyboxShader;
        }

        _skyMaterial.SetTexture(TexId, cubemap);
        _activeCubemap = cubemap;

        var skybox = EnsureSkyboxComponent();
        if (skybox == null)
        {
            return false;
        }

        skybox.material = _skyMaterial;
        _camera.clearFlags = CameraClearFlags.Skybox;
        if (_skyRenderer != null)
        {
            _skyRenderer.enabled = false;
        }

        _skyRenderMode = SkyRenderMode.SkyboxClear;
        return true;
    }

    private bool ApplyInteriorSphereMaterial(Cubemap cubemap)
    {
        var interiorShader = Shader.Find("TopDog/CombatSkyboxInterior");
        if (interiorShader == null || _skyRenderer == null || _camera == null)
        {
            return false;
        }

        if (_skyMaterial == null)
        {
            _skyMaterial = new Material(interiorShader);
        }
        else
        {
            _skyMaterial.shader = interiorShader;
        }

        _skyMaterial.SetTexture(TexId, cubemap);
        _activeCubemap = cubemap;
        _skyRenderer.sharedMaterial = _skyMaterial;
        _skyRenderer.enabled = true;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _skyRenderMode = SkyRenderMode.InteriorSphere;
        return true;
    }

    /// <summary>SG 对齐里程碑：内翻球主路径（水平 yaw 正确），Skybox 作一次性回退。</summary>
    private bool ApplySkyMaterial(Cubemap cubemap)
    {
        if (ApplyInteriorSphereMaterial(cubemap))
        {
            return true;
        }

        return TryApplySkyboxClearMaterial(cubemap);
    }

    private static bool RenderTextureHasSkyContent(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var probe = new Texture2D(4, 4, TextureFormat.RGB24, false);
        var x = Mathf.Max(0, rt.width / 2 - 2);
        var y = Mathf.Max(0, rt.height / 2 - 2);
        probe.ReadPixels(new Rect(x, y, 4, 4), 0, 0);
        probe.Apply();
        RenderTexture.active = prev;
        var bright = false;
        foreach (var p in probe.GetPixels())
        {
            if (p.r > 0.12f || p.g > 0.12f || p.b > 0.14f)
            {
                bright = true;
                break;
            }
        }

        Destroy(probe);
        return bright;
    }

    private void EnsureRenderTexture()
    {
        if (!_cameraReady || _viewportHost == null || _artSlot == null || _camera == null)
        {
            return;
        }

        var bounds = _viewportHost.worldBound;
        var width = Mathf.RoundToInt(bounds.width);
        var height = Mathf.RoundToInt(bounds.height);
        var maxRes = ClientGameSettings.CombatBackgroundMaxResolution;
        if (width > 0 && height > 0)
        {
            var longest = Mathf.Max(width, height);
            if (longest > maxRes)
            {
                var scale = maxRes / (float)longest;
                width = Mathf.RoundToInt(width * scale);
                height = Mathf.RoundToInt(height * scale);
            }
        }

        width = Mathf.Clamp(width, 128, maxRes);
        height = Mathf.Clamp(height, 128, maxRes);
        if (width < 8 || height < 8)
        {
            UpdateCameraEnabled();
            return;
        }

        if (_renderTexture != null && _rtWidth == width && _rtHeight == height)
        {
            return;
        }

        ReleaseRenderTexture();
        _rtWidth = width;
        _rtHeight = height;
        _renderTexture = new RenderTexture(_rtWidth, _rtHeight, 24, RenderTextureFormat.ARGB32)
        {
            name = "CombatBackgroundRT",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
        };
        _renderTexture.Create();
        _camera.targetTexture = _renderTexture;
        _camera.aspect = _rtWidth / (float)_rtHeight;
        _rtHasRenderedFrame = false;
        _interiorFallbackTried = false;
        if (_rtImage != null)
        {
            _rtImage.image = _renderTexture;
        }
    }

    private void SyncOrbitAndZoom()
    {
        if (_camera == null || _orbitSource == null)
        {
            return;
        }

        var yawDeg = -_orbitSource.OrbitYawRad * Mathf.Rad2Deg;
        var pitchDeg = (Mathf.PI * 0.5f - _orbitSource.OrbitPitchRad) * Mathf.Rad2Deg;
        if (_yRotationRoot != null)
        {
            _yRotationRoot.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
        }

        if (_xRotationRoot != null)
        {
            _xRotationRoot.localRotation = Quaternion.Euler(-pitchDeg, 0f, 0f);
        }

        _camera.fieldOfView = CurrentVerticalFovDeg;
    }

    private void UpdateCameraEnabled()
    {
        if (_camera == null)
        {
            return;
        }

        _camera.enabled = false;
    }

    private void ClearArtSlot()
    {
        if (_artSlot == null)
        {
            return;
        }

        _artSlot.style.backgroundImage = StyleKeyword.None;
        _artSlot.style.backgroundColor = new StyleColor(new Color(0.03f, 0.04f, 0.06f, 1f));
        _rtHasRenderedFrame = false;
        if (_rtImage != null)
        {
            _rtImage.image = null;
            _rtImage.style.display = DisplayStyle.None;
        }
    }

    private void OnBackgroundResolutionChanged()
    {
        _rtHasRenderedFrame = false;
        _interiorFallbackTried = false;
        ReleaseRenderTexture();
    }

    private void ReleaseRenderTexture()
    {
        if (_camera != null)
        {
            _camera.targetTexture = null;
        }

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }

        _rtWidth = 0;
        _rtHeight = 0;
        _rtHasRenderedFrame = false;
    }
}
