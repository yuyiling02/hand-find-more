using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RehabDemo
{
  /// <summary>
  ///   用纯代码生成的风格化微波炉与马克杯：
  ///   机身、真正的空心炉腔、带半透明网点玻璃窗和把手的铰链门、可旋转转盘、腔内照明灯、
  ///   LED 倒计时显示屏、可按下的“开始”按钮，以及带牛奶液面和热气粒子的马克杯。
  /// </summary>
  public class MicrowaveOven : MonoBehaviour
  {
    public Transform Root { get; private set; }
    public Transform DoorPivot { get; private set; }
    public Transform HandleTransform { get; private set; }
    public Transform Turntable { get; private set; }
    public Transform StartButtonCap { get; private set; }
    public Transform CupRoot { get; private set; }

    /// <summary>转盘上表面的世界坐标（杯子放进去时的落点）。</summary>
    public Vector3 TurntableSurfaceWorld => Turntable.position + Turntable.up * 0.005f;
    /// <summary>炉腔入口中心的世界坐标。</summary>
    public Vector3 CavityEntryWorld => Root.TransformPoint(new Vector3(-0.045f, 0.095f, -0.105f));
    /// <summary>门把手中心的世界坐标（随门旋转实时变化）。</summary>
    public Vector3 HandleCenterWorld => HandleTransform.position;
    /// <summary>门面板中心的世界坐标（用于推门判定）。</summary>
    public Vector3 DoorCenterWorld => DoorPivot.TransformPoint(new Vector3(0.115f, 0f, -0.008f));
    /// <summary>“开始”按钮中心的世界坐标。</summary>
    public Vector3 StartButtonWorld => StartButtonCap.position;

    private readonly List<Material> _materials = new List<Material>();
    private Material _handleMat;
    private Material _cupBodyMat;
    private Material _cupHandleMat;
    private Material _lampMat;
    private Material _turntableRingMat;
    private Material _startBtnCapMat;

    private Light _cavityLight;
    private SpriteRenderer _turntableRing;
    private Text _displayMainText;
    private Text _displaySubText;
    private Image _startBorderImage;
    private Image _startFillImage;
    private Image _startBgImage;
    private Text _startLabelText;
    private Text _bannerText;
    private Image _bannerBg;
    private Transform _bannerTransform;
    private ParticleSystem _steamParticles;

    private static readonly int _EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private static readonly Color _ShellColor = new Color32(0xF6, 0xEF, 0xE2, 0xFF);
    private static readonly Color _TrimColor = new Color32(0x2B, 0x31, 0x3C, 0xFF);
    private static readonly Color _CavityColor = new Color32(0xD8, 0xDE, 0xE8, 0xFF);
    private static readonly Color _HandleIdleColor = new Color32(0xE0, 0xE5, 0xEC, 0xFF);
    private static readonly Color _HighlightGold = new Color32(0xFF, 0xC8, 0x2E, 0xFF);
    private static readonly Color _ReadyGreen = new Color32(0x57, 0xD1, 0x63, 0xFF);
    private static readonly Color _HeatOrange = new Color32(0xF5, 0x82, 0x28, 0xFF);
    private static readonly Color _CupBaseColor = new Color32(0xE8, 0x5D, 0x4A, 0xFF);
    private static readonly Color _CupCreamColor = new Color32(0xFF, 0xF8, 0xE8, 0xFF);
    private static readonly Color _LampColor = new Color(1f, 0.86f, 0.46f);

    private float _doorAngle;
    private float _turntableAngle;
    private float _lightAmount;
    private float _handleHighlight;
    private float _cupHighlight;
    private float _buttonPress;
    private float _buttonHighlight;

    public void Build(Transform parent, Vector3 microwavePosition, float microwaveYaw)
    {
      if (Root != null)
      {
        return;
      }

      Root = new GameObject("MicrowaveRoot").transform;
      Root.SetParent(parent, false);
      Root.localPosition = microwavePosition;
      Root.localRotation = Quaternion.Euler(0f, microwaveYaw, 0f);

      BuildBodyAndCavity();
      BuildTurntable();
      BuildCavityLight();
      BuildDoor();
      BuildControlPanel();
      BuildStepBanner();
      BuildCup(parent);
    }

    private void BuildBodyAndCavity()
    {
      var shellMat = CreateStandardMaterial(_ShellColor, 0.35f);
      var trimMat = CreateStandardMaterial(_TrimColor, 0.25f);
      var cavityMat = CreateStandardMaterial(_CavityColor, 0.40f);
      var darkSlotMat = CreateStandardMaterial(new Color32(0x16, 0x1A, 0x20, 0xFF), 0.1f);

      var body = new GameObject("Body").transform;
      body.SetParent(Root, false);

      // 微波炉整体宽 0.36m（x: -0.18 ~ +0.18），高 0.22m（y: 0.01 ~ 0.23），深 0.22m（z: -0.11 ~ +0.11）
      // 左侧空心炉腔开口范围：x ∈ [-0.155, +0.065]，y ∈ [0.032, 0.198]，z ∈ [-0.11, +0.085]
      // 四个橡胶脚垫
      var footPositions = new[]
      {
        new Vector3(-0.145f, 0.006f, -0.08f),
        new Vector3(0.145f, 0.006f, -0.08f),
        new Vector3(-0.145f, 0.006f, 0.08f),
        new Vector3(0.145f, 0.006f, 0.08f),
      };
      for (var i = 0; i < footPositions.Length; i++)
      {
        CreateCylinder($"Foot{i}", body, footPositions[i], new Vector3(0.026f, 0.006f, 0.026f), trimMat);
      }

      // 外壳五面（留出左前方的空心炉腔开口）
      CreateBox("BottomWall", body, new Vector3(0f, 0.021f, 0f), new Vector3(0.36f, 0.022f, 0.22f), shellMat);
      CreateBox("TopWall", body, new Vector3(0f, 0.214f, 0f), new Vector3(0.36f, 0.032f, 0.22f), shellMat);
      CreateBox("LeftWall", body, new Vector3(-0.168f, 0.117f, 0f), new Vector3(0.024f, 0.162f, 0.22f), shellMat);
      CreateBox("BackWall", body, new Vector3(-0.045f, 0.117f, 0.098f), new Vector3(0.222f, 0.162f, 0.024f), shellMat);
      // 右侧控制舱实体机身（x: +0.065 ~ +0.18）
      CreateBox("ControlHousing", body, new Vector3(0.1225f, 0.117f, 0f), new Vector3(0.115f, 0.162f, 0.22f), shellMat);

      // 顶部圆润装饰盖板，增加造型感
      CreateBox("TopCrown", body, new Vector3(0f, 0.232f, 0.004f), new Vector3(0.344f, 0.006f, 0.204f), shellMat);

      // 炉腔内胆（浅灰搪瓷反光壁，让腔内灯光打上去柔和明亮）
      var cavity = new GameObject("HollowCavity").transform;
      cavity.SetParent(body, false);
      CreateBox("CavityFloor", cavity, new Vector3(-0.045f, 0.033f, -0.005f), new Vector3(0.22f, 0.003f, 0.185f), cavityMat);
      CreateBox("CavityCeiling", cavity, new Vector3(-0.045f, 0.197f, -0.005f), new Vector3(0.22f, 0.003f, 0.185f), cavityMat);
      CreateBox("CavityLeft", cavity, new Vector3(-0.154f, 0.115f, -0.005f), new Vector3(0.003f, 0.166f, 0.185f), cavityMat);
      CreateBox("CavityRight", cavity, new Vector3(0.064f, 0.115f, -0.005f), new Vector3(0.003f, 0.166f, 0.185f), cavityMat);
      CreateBox("CavityBack", cavity, new Vector3(-0.045f, 0.115f, 0.085f), new Vector3(0.22f, 0.166f, 0.003f), cavityMat);

      // 后壁散热格栅条
      for (var i = 0; i < 4; i++)
      {
        CreateBox($"Vent{i}", cavity, new Vector3(-0.045f, 0.135f + i * 0.012f, 0.083f), new Vector3(0.11f, 0.004f, 0.002f), darkSlotMat);
      }

      // 前框深色密封圈与门锁孔
      CreateBox("SealTop", body, new Vector3(-0.045f, 0.199f, -0.109f), new Vector3(0.232f, 0.006f, 0.004f), trimMat);
      CreateBox("SealBottom", body, new Vector3(-0.045f, 0.031f, -0.109f), new Vector3(0.232f, 0.006f, 0.004f), trimMat);
      CreateBox("SealLeft", body, new Vector3(-0.158f, 0.115f, -0.109f), new Vector3(0.006f, 0.172f, 0.004f), trimMat);
      CreateBox("SealRight", body, new Vector3(0.068f, 0.115f, -0.109f), new Vector3(0.008f, 0.172f, 0.004f), trimMat);
      CreateBox("LatchSlotTop", body, new Vector3(0.068f, 0.145f, -0.110f), new Vector3(0.005f, 0.018f, 0.005f), darkSlotMat);
      CreateBox("LatchSlotBottom", body, new Vector3(0.068f, 0.085f, -0.110f), new Vector3(0.005f, 0.018f, 0.005f), darkSlotMat);
    }

    private void BuildTurntable()
    {
      var hubMat = CreateStandardMaterial(_TrimColor, 0.3f);
      var plateMat = CreateStandardMaterial(new Color32(0xFA, 0xF8, 0xF2, 0xFF), 0.65f);
      var ribMat = CreateStandardMaterial(new Color32(0xD6, 0xDC, 0xE5, 0xFF), 0.5f);

      Turntable = new GameObject("Turntable").transform;
      Turntable.SetParent(Root, false);
      Turntable.localPosition = new Vector3(-0.045f, 0.038f, -0.015f);

      // 底部滚轮环 + 玻璃圆盘 + 三条防滑筋（旋转时一目了然）
      CreateCylinder("RollerHub", Turntable, new Vector3(0f, -0.002f, 0f), new Vector3(0.036f, 0.003f, 0.036f), hubMat);
      CreateCylinder("GlassTray", Turntable, new Vector3(0f, 0.002f, 0f), new Vector3(0.156f, 0.003f, 0.156f), plateMat);
      CreateCylinder("TrayCenter", Turntable, new Vector3(0f, 0.005f, 0f), new Vector3(0.022f, 0.0015f, 0.022f), ribMat);

      for (var i = 0; i < 3; i++)
      {
        var rib = CreateBox($"Rib{i}", Turntable, Vector3.zero, new Vector3(0.056f, 0.0025f, 0.005f), ribMat);
        var angle = i * 120f;
        var rad = angle * Mathf.Deg2Rad;
        rib.transform.localPosition = new Vector3(Mathf.Cos(rad) * 0.038f, 0.005f, Mathf.Sin(rad) * 0.038f);
        rib.transform.localRotation = Quaternion.Euler(0f, -angle, 0f);
      }

      // 转盘上的放置提示光圈
      var ringGo = new GameObject("TargetRing");
      ringGo.transform.SetParent(Turntable, false);
      ringGo.transform.localPosition = new Vector3(0f, 0.006f, 0f);
      ringGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
      ringGo.transform.localScale = Vector3.one * 0.12f;
      _turntableRing = ringGo.AddComponent<SpriteRenderer>();
      _turntableRing.sprite = ProcTextures.Ring;
      _turntableRingMat = ProcTextures.CreateUnlitMaterial(new Color(1f, 0.8f, 0.2f, 0f));
      _materials.Add(_turntableRingMat);
      _turntableRing.sharedMaterial = _turntableRingMat;
      _turntableRing.shadowCastingMode = ShadowCastingMode.Off;
    }

    private void BuildCavityLight()
    {
      _lampMat = CreateStandardMaterial(_LampColor, 0.5f, true);
      var lampGo = CreateBox("CavityLampMesh", Root, new Vector3(-0.045f, 0.193f, -0.02f), new Vector3(0.075f, 0.006f, 0.024f), _lampMat);
      lampGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

      var lightGo = new GameObject("CavityPointLight");
      lightGo.transform.SetParent(Root, false);
      lightGo.transform.localPosition = new Vector3(-0.045f, 0.175f, -0.02f);
      _cavityLight = lightGo.AddComponent<Light>();
      _cavityLight.type = LightType.Point;
      _cavityLight.color = _LampColor;
      _cavityLight.range = 0.48f;
      _cavityLight.intensity = 0.2f;
      _cavityLight.shadows = LightShadows.None;
    }

    private void BuildDoor()
    {
      var doorMat = CreateStandardMaterial(_ShellColor, 0.4f);
      var bezelMat = CreateStandardMaterial(_TrimColor, 0.3f);
      _handleMat = CreateStandardMaterial(_HandleIdleColor, 0.65f, true);

      // 铰链在左前角：localEulerAngles.y 从 0°（关）转到 +105°（朝左前方拉开）
      DoorPivot = new GameObject("DoorPivot").transform;
      DoorPivot.SetParent(Root, false);
      DoorPivot.localPosition = new Vector3(-0.162f, 0.115f, -0.112f);

      // 门总宽 0.232m（从铰链 x=0 到 x=+0.232），高 0.176m（y: -0.088 ~ +0.088），厚 0.016m
      // 掏空中间区域做真正的透明观察窗：窗口 x ∈ [0.028, 0.196]，y ∈ [-0.056, +0.056]
      CreateBox("DoorTopBar", DoorPivot, new Vector3(0.116f, 0.072f, -0.008f), new Vector3(0.232f, 0.032f, 0.016f), doorMat);
      CreateBox("DoorBottomBar", DoorPivot, new Vector3(0.116f, -0.072f, -0.008f), new Vector3(0.232f, 0.032f, 0.016f), doorMat);
      CreateBox("DoorLeftBar", DoorPivot, new Vector3(0.014f, 0f, -0.008f), new Vector3(0.028f, 0.112f, 0.016f), doorMat);
      CreateBox("DoorRightBar", DoorPivot, new Vector3(0.214f, 0f, -0.008f), new Vector3(0.036f, 0.112f, 0.016f), doorMat);

      // 窗口内侧深色嵌框
      CreateBox("WindowBezelTop", DoorPivot, new Vector3(0.112f, 0.056f, -0.009f), new Vector3(0.176f, 0.006f, 0.018f), bezelMat);
      CreateBox("WindowBezelBottom", DoorPivot, new Vector3(0.112f, -0.056f, -0.009f), new Vector3(0.176f, 0.006f, 0.018f), bezelMat);
      CreateBox("WindowBezelLeft", DoorPivot, new Vector3(0.026f, 0f, -0.009f), new Vector3(0.006f, 0.116f, 0.018f), bezelMat);
      CreateBox("WindowBezelRight", DoorPivot, new Vector3(0.198f, 0f, -0.009f), new Vector3(0.006f, 0.116f, 0.018f), bezelMat);

      // 半透明防辐射网点玻璃窗（正反都可透视炉腔）
      var glassMat = ProcTextures.CreateUnlitMaterial(Color.white);
      glassMat.mainTexture = ProcTextures.WindowGrid;
      _materials.Add(glassMat);
      var glass = CreateBox("GlassWindow", DoorPivot, new Vector3(0.112f, 0f, -0.008f), new Vector3(0.168f, 0.108f, 0.002f), glassMat);
      glass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

      // 门背面的双卡扣钩（开门时能看见真实的门钩细节）
      CreateBox("LatchHookTop", DoorPivot, new Vector3(0.222f, 0.030f, 0.006f), new Vector3(0.004f, 0.012f, 0.014f), bezelMat);
      CreateBox("LatchHookBottom", DoorPivot, new Vector3(0.222f, -0.030f, 0.006f), new Vector3(0.004f, 0.012f, 0.014f), bezelMat);

      // 门把手（右侧竖向拉手，对准或等待操作时会高亮提示）
      HandleTransform = new GameObject("DoorHandle").transform;
      HandleTransform.SetParent(DoorPivot, false);
      HandleTransform.localPosition = new Vector3(0.214f, 0f, -0.028f);

      CreateBox("MountTop", HandleTransform, new Vector3(0f, 0.046f, 0.008f), new Vector3(0.012f, 0.012f, 0.018f), _handleMat);
      CreateBox("MountBottom", HandleTransform, new Vector3(0f, -0.046f, 0.008f), new Vector3(0.012f, 0.012f, 0.018f), _handleMat);
      CreateCylinder("GripBar", HandleTransform, new Vector3(0f, 0f, 0f), new Vector3(0.015f, 0.056f, 0.015f), _handleMat);
    }

    private void BuildControlPanel()
    {
      var panelMat = CreateStandardMaterial(_TrimColor, 0.35f);
      var screenMat = CreateStandardMaterial(new Color32(0x10, 0x16, 0x1E, 0xFF), 0.7f);
      var decoMat = CreateStandardMaterial(new Color32(0x46, 0x50, 0x60, 0xFF), 0.3f);
      _startBtnCapMat = CreateStandardMaterial(_ReadyGreen, 0.45f, true);

      var panel = new GameObject("ControlPanel").transform;
      panel.SetParent(Root, false);
      panel.localPosition = new Vector3(0.124f, 0.115f, -0.111f);

      // 黑色控制面板底板
      CreateBox("PanelFace", panel, new Vector3(0f, 0f, 0f), new Vector3(0.098f, 0.172f, 0.004f), panelMat);
      // 上方 LED 倒计时屏幕框
      CreateBox("DisplayScreen", panel, new Vector3(0f, 0.048f, -0.002f), new Vector3(0.082f, 0.048f, 0.004f), screenMat);

      // 中间两颗装饰快捷小键（火力 / +30s）
      CreateBox("DecoBtnLeft", panel, new Vector3(-0.022f, 0.006f, -0.003f), new Vector3(0.036f, 0.016f, 0.005f), decoMat);
      CreateBox("DecoBtnRight", panel, new Vector3(0.022f, 0.006f, -0.003f), new Vector3(0.036f, 0.016f, 0.005f), decoMat);

      // 下方大号实体“开始”按钮帽（按下时沿 +Z 缩进面板）
      StartButtonCap = CreateBox("StartButtonCap", panel, new Vector3(0f, -0.044f, -0.006f), new Vector3(0.082f, 0.054f, 0.010f), _startBtnCapMat).transform;

      // 控制面板世界空间 Canvas（显示倒计时数字与按钮文字）
      var canvasGo = new GameObject("PanelCanvas", typeof(RectTransform));
      canvasGo.transform.SetParent(panel, false);
      var canvas = canvasGo.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.WorldSpace;
      canvasGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
      var rootRect = (RectTransform)canvasGo.transform;
      rootRect.sizeDelta = new Vector2(98f, 172f);
      rootRect.localPosition = new Vector3(0f, 0f, -0.0125f);
      rootRect.localRotation = Quaternion.identity;
      rootRect.localScale = Vector3.one * 0.001f;

      var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

      // LED 主倒计时数字
      _displayMainText = CreateUIText("CountdownText", rootRect, font, "00:03", 25, new Color32(0x57, 0xF2, 0x87, 0xFF));
      _displayMainText.rectTransform.anchoredPosition = new Vector2(0f, 52f);
      _displayMainText.rectTransform.sizeDelta = new Vector2(82f, 34f);

      // LED 小状态字
      _displaySubText = CreateUIText("StatusText", rootRect, font, "DOOR CLOSED", 9, new Color32(0x8B, 0xB8, 0x9E, 0xFF));
      _displaySubText.rectTransform.anchoredPosition = new Vector2(0f, 33f);
      _displaySubText.rectTransform.sizeDelta = new Vector2(82f, 14f);

      // 装饰小键文字
      var deco1 = CreateUIText("DecoLabel1", rootRect, font, "高火", 9, new Color(1f, 1f, 1f, 0.7f));
      deco1.rectTransform.anchoredPosition = new Vector2(-22f, 6f);
      deco1.rectTransform.sizeDelta = new Vector2(36f, 16f);
      var deco2 = CreateUIText("DecoLabel2", rootRect, font, "+3秒", 9, new Color(1f, 1f, 1f, 0.7f));
      deco2.rectTransform.anchoredPosition = new Vector2(22f, 6f);
      deco2.rectTransform.sizeDelta = new Vector2(36f, 16f);

      // “开始”按钮 UI 层（随按钮帽一起下压）
      var btnRect = new GameObject("StartBtnUI", typeof(RectTransform)).GetComponent<RectTransform>();
      btnRect.SetParent(rootRect, false);
      btnRect.anchoredPosition = new Vector2(0f, -44f);
      btnRect.sizeDelta = new Vector2(82f, 54f);

      _startBorderImage = CreateRoundedUIImage("Border", btnRect, _HighlightGold);
      _startBorderImage.rectTransform.offsetMin = new Vector2(-4f, -4f);
      _startBorderImage.rectTransform.offsetMax = new Vector2(4f, 4f);

      _startBgImage = CreateRoundedUIImage("Bg", btnRect, _ReadyGreen);

      _startFillImage = CreateRoundedUIImage("DwellFill", btnRect, new Color(1f, 1f, 1f, 0.35f));
      _startFillImage.rectTransform.anchorMax = new Vector2(0f, 1f);

      _startLabelText = CreateUIText("StartLabel", btnRect, font, "开始", 22, Color.white);
      _startLabelText.rectTransform.anchoredPosition = Vector2.zero;
      _startLabelText.rectTransform.sizeDelta = new Vector2(82f, 54f);
    }

    private void BuildStepBanner()
    {
      _bannerTransform = new GameObject("StepBanner", typeof(RectTransform)).transform;
      _bannerTransform.SetParent(Root, false);
      _bannerTransform.localPosition = new Vector3(0f, 0.285f, -0.02f);

      var canvas = _bannerTransform.gameObject.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.WorldSpace;
      _bannerTransform.gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
      var rect = (RectTransform)_bannerTransform;
      rect.sizeDelta = new Vector2(380f, 54f);
      rect.localScale = Vector3.one * 0.001f;

      _bannerBg = CreateRoundedUIImage("BannerBg", rect, new Color(0.14f, 0.17f, 0.22f, 0.88f));
      var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
      _bannerText = CreateUIText("BannerText", rect, font, "① 拉把手打开微波炉门", 24, Color.white);
      _bannerText.rectTransform.sizeDelta = new Vector2(360f, 50f);
    }

    private void BuildCup(Transform parent)
    {
      _cupBodyMat = CreateStandardMaterial(_CupBaseColor, 0.55f, true);
      _cupHandleMat = CreateStandardMaterial(_CupCreamColor, 0.50f, true);
      var innerMat = CreateStandardMaterial(new Color32(0x4A, 0x2C, 0x24, 0xFF), 0.2f);
      var milkMat = CreateStandardMaterial(_CupCreamColor, 0.75f, true);

      CupRoot = new GameObject("MugCup").transform;
      CupRoot.SetParent(parent, false);

      // 杯底原点在 y = 0，总高约 0.068m，外径 0.056m
      CreateCylinder("CupBase", CupRoot, new Vector3(0f, 0.004f, 0f), new Vector3(0.048f, 0.004f, 0.048f), _cupHandleMat);
      CreateCylinder("CupBody", CupRoot, new Vector3(0f, 0.034f, 0f), new Vector3(0.056f, 0.030f, 0.056f), _cupBodyMat);
      CreateCylinder("CupRim", CupRoot, new Vector3(0f, 0.064f, 0f), new Vector3(0.059f, 0.003f, 0.059f), _cupHandleMat);
      CreateCylinder("CupInner", CupRoot, new Vector3(0f, 0.0655f, 0f), new Vector3(0.050f, 0.002f, 0.050f), innerMat);
      CreateCylinder("MilkSurface", CupRoot, new Vector3(0f, 0.0665f, 0f), new Vector3(0.046f, 0.0015f, 0.046f), milkMat);

      // 右侧 C 形杯耳（三段式圆角把手）
      var handleRoot = new GameObject("CupHandle").transform;
      handleRoot.SetParent(CupRoot, false);
      handleRoot.localPosition = new Vector3(0.028f, 0.035f, 0f);
      CreateBox("EarTop", handleRoot, new Vector3(0.009f, 0.015f, 0f), new Vector3(0.018f, 0.007f, 0.008f), _cupHandleMat);
      CreateBox("EarBottom", handleRoot, new Vector3(0.009f, -0.015f, 0f), new Vector3(0.018f, 0.007f, 0.008f), _cupHandleMat);
      CreateBox("EarSide", handleRoot, new Vector3(0.016f, 0f, 0f), new Vector3(0.007f, 0.034f, 0.008f), _cupHandleMat);

      // 加热完成后的袅袅热气粒子
      var steamGo = new GameObject("CupSteam");
      steamGo.SetActive(false);
      steamGo.transform.SetParent(CupRoot, false);
      steamGo.transform.localPosition = new Vector3(0f, 0.072f, 0f);
      _steamParticles = steamGo.AddComponent<ParticleSystem>();

      var main = _steamParticles.main;
      main.playOnAwake = false;
      main.loop = true;
      main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
      main.startSpeed = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
      main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.036f);
      main.simulationSpace = ParticleSystemSimulationSpace.World;
      main.maxParticles = 30;

      var emission = _steamParticles.emission;
      emission.rateOverTime = 8f;

      var shape = _steamParticles.shape;
      shape.shapeType = ParticleSystemShapeType.Circle;
      shape.radius = 0.014f;
      shape.rotation = new Vector3(-90f, 0f, 0f);

      var col = _steamParticles.colorOverLifetime;
      col.enabled = true;
      var grad = new Gradient();
      grad.SetKeys(
        new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
        new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.42f, 0.25f), new GradientAlphaKey(0f, 1f) });
      col.color = grad;

      var sizeOverLifetime = _steamParticles.sizeOverLifetime;
      sizeOverLifetime.enabled = true;
      sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));

      var steamMat = ProcTextures.CreateUnlitMaterial(Color.white);
      steamMat.mainTexture = ProcTextures.Steam;
      _materials.Add(steamMat);
      var steamRenderer = steamGo.GetComponent<ParticleSystemRenderer>();
      steamRenderer.sharedMaterial = steamMat;
      steamRenderer.shadowCastingMode = ShadowCastingMode.Off;
      steamGo.SetActive(true);
    }

    public void SetVisible(bool visible)
    {
      if (Root != null && Root.gameObject.activeSelf != visible)
      {
        Root.gameObject.SetActive(visible);
      }
      if (CupRoot != null && CupRoot.gameObject.activeSelf != visible)
      {
        CupRoot.gameObject.SetActive(visible);
      }
    }

    /// <summary>设置门开合比例（0 = 完全关闭，1 = 打开到 105°）。</summary>
    public void SetDoorOpenRatio(float ratio)
    {
      _doorAngle = Mathf.Clamp01(ratio) * 105f;
      if (DoorPivot != null)
      {
        DoorPivot.localRotation = Quaternion.Euler(0f, _doorAngle, 0f);
      }
    }

    /// <summary>旋转转盘（度/秒）。</summary>
    public void AdvanceTurntable(float degrees)
    {
      _turntableAngle = (_turntableAngle + degrees) % 360f;
      if (Turntable != null)
      {
        Turntable.localRotation = Quaternion.Euler(0f, _turntableAngle, 0f);
      }
    }

    /// <summary>更新腔内灯亮度（0 = 待机微光，0.5 = 开门照明，1 = 加热高亮）。</summary>
    public void SetCavityLightLevel(float targetLevel, float pulse = 0f)
    {
      _lightAmount = Mathf.Lerp(_lightAmount, targetLevel, 1f - Mathf.Exp(-Time.deltaTime / 0.08f));
      var effective = Mathf.Max(0.08f, _lightAmount + pulse * 0.35f);
      if (_cavityLight != null)
      {
        _cavityLight.intensity = effective * 2.2f;
      }
      if (_lampMat != null)
      {
        _lampMat.color = Color.Lerp(new Color(0.55f, 0.52f, 0.45f), Color.white, Mathf.Clamp01(effective));
        _lampMat.SetColor(_EmissionColorId, _LampColor * (effective * 1.6f));
      }
    }

    /// <summary>更新门把手的高亮提示。</summary>
    public void SetHandleFeedback(bool aimed, bool pulsePrompt)
    {
      var target = aimed ? 1f : (pulsePrompt ? 0.45f + 0.35f * Mathf.Sin(Time.time * 5.5f) : 0f);
      _handleHighlight = Mathf.Lerp(_handleHighlight, target, 1f - Mathf.Exp(-Time.deltaTime / 0.06f));
      var color = Color.Lerp(_HandleIdleColor, aimed ? _ReadyGreen : _HighlightGold, _handleHighlight);
      _handleMat.color = color;
      _handleMat.SetColor(_EmissionColorId, color * (_handleHighlight * 0.45f));
      if (HandleTransform != null)
      {
        var scale = 1f + (aimed ? 0.10f : _handleHighlight * 0.04f);
        HandleTransform.localScale = Vector3.Lerp(HandleTransform.localScale, new Vector3(scale, 1f, scale), 1f - Mathf.Exp(-Time.deltaTime / 0.06f));
      }
    }

    /// <summary>更新杯子与转盘落点圈的视觉反馈。</summary>
    public void SetCupAndTurntableFeedback(bool cupAimed, bool cupHeld, bool showTurntableRing, bool overTurntable, float dockProgress)
    {
      var targetCup = cupHeld ? 1f : (cupAimed ? 0.7f : 0f);
      _cupHighlight = Mathf.Lerp(_cupHighlight, targetCup, 1f - Mathf.Exp(-Time.deltaTime / 0.06f));
      var bodyColor = Color.Lerp(_CupBaseColor, cupHeld ? _ReadyGreen : _HighlightGold, _cupHighlight * 0.45f);
      _cupBodyMat.color = bodyColor;
      _cupBodyMat.SetColor(_EmissionColorId, bodyColor * (_cupHighlight * 0.3f));

      if (_turntableRing != null)
      {
        if (!showTurntableRing)
        {
          _turntableRingMat.color = new Color(1f, 1f, 1f, 0f);
        }
        else
        {
          var pulse = 0.55f + 0.25f * Mathf.Sin(Time.time * 6f);
          var ringColor = overTurntable ? _ReadyGreen : _HighlightGold;
          ringColor.a = overTurntable ? 0.92f : pulse;
          _turntableRingMat.color = ringColor;
          var ringScale = 0.12f * (overTurntable ? Mathf.Lerp(1.12f, 0.92f, dockProgress) : 1f);
          _turntableRing.transform.localScale = Vector3.one * ringScale;
        }
      }
    }

    /// <summary>更新右侧控制面板的倒计时与“开始”按钮状态。</summary>
    public void UpdateControlPanel(string mainDisplay, string subDisplay, Color displayColor, bool buttonReady, bool buttonAimed, float buttonPressProgress, bool isHeating)
    {
      if (_displayMainText != null)
      {
        _displayMainText.text = mainDisplay;
        _displayMainText.color = displayColor;
      }
      if (_displaySubText != null)
      {
        _displaySubText.text = subDisplay;
        _displaySubText.color = Color.Lerp(displayColor, Color.white, 0.35f);
      }

      var targetPress = Mathf.Clamp01(buttonPressProgress);
      _buttonPress = Mathf.MoveTowards(_buttonPress, targetPress, Time.deltaTime / 0.06f);
      if (StartButtonCap != null)
      {
        StartButtonCap.localPosition = new Vector3(0f, -0.044f, Mathf.Lerp(-0.006f, -0.0015f, _buttonPress));
      }

      var pulse = buttonReady ? 0.5f + 0.5f * Mathf.Sin(Time.time * 6f) : 0f;
      var targetHighlight = buttonAimed ? 1f : pulse * 0.7f;
      _buttonHighlight = Mathf.MoveTowards(_buttonHighlight, targetHighlight, Time.deltaTime / 0.08f);

      var baseBtnColor = isHeating
        ? _HeatOrange
        : (buttonReady ? _ReadyGreen : new Color32(0x3A, 0x43, 0x52, 0xFF));

      if (_startBtnCapMat != null)
      {
        _startBtnCapMat.color = baseBtnColor;
        _startBtnCapMat.SetColor(_EmissionColorId, baseBtnColor * (buttonReady || isHeating ? 0.35f : 0.05f));
      }
      if (_startBgImage != null)
      {
        _startBgImage.color = baseBtnColor;
      }
      if (_startBorderImage != null)
      {
        var borderColor = _HighlightGold;
        borderColor.a = _buttonHighlight;
        _startBorderImage.color = borderColor;
      }
      if (_startFillImage != null)
      {
        _startFillImage.rectTransform.anchorMax = new Vector2(_buttonPress, 1f);
      }
      if (_startLabelText != null)
      {
        _startLabelText.text = isHeating ? "加热中" : "开始";
        _startLabelText.color = buttonReady || isHeating ? Color.white : new Color(1f, 1f, 1f, 0.55f);
      }
    }

    /// <summary>更新微波炉上方的步骤提示横幅，并始终朝向主相机。</summary>
    public void UpdateBanner(string text, Color accentColor, Camera viewCamera)
    {
      if (_bannerTransform == null)
      {
        return;
      }
      if (viewCamera != null)
      {
        _bannerTransform.rotation = viewCamera.transform.rotation;
      }
      if (_bannerText != null && _bannerText.text != text)
      {
        _bannerText.text = text;
      }
      if (_bannerText != null)
      {
        _bannerText.color = accentColor;
      }
    }

    /// <summary>开关马克杯上方的热气粒子。</summary>
    public void SetSteamActive(bool active)
    {
      if (_steamParticles == null)
      {
        return;
      }
      if (active && !_steamParticles.isPlaying)
      {
        _steamParticles.Play();
      }
      else if (!active && _steamParticles.isPlaying)
      {
        _steamParticles.Stop();
      }
    }

    private Material CreateStandardMaterial(Color color, float smoothness, bool emissive = false)
    {
      var mat = new Material(Shader.Find("Standard")) { color = color };
      mat.SetFloat("_Glossiness", smoothness);
      if (emissive)
      {
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        mat.SetColor("_EmissionColor", color * 0.1f);
      }
      _materials.Add(mat);
      return mat;
    }

    private static GameObject CreateBox(string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
    {
      var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
      go.name = name;
      Object.Destroy(go.GetComponent<Collider>());
      go.transform.SetParent(parent, false);
      go.transform.localPosition = localPos;
      go.transform.localScale = localScale;
      go.GetComponent<MeshRenderer>().sharedMaterial = mat;
      return go;
    }

    private static GameObject CreateCylinder(string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
    {
      var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
      go.name = name;
      Object.Destroy(go.GetComponent<Collider>());
      go.transform.SetParent(parent, false);
      go.transform.localPosition = localPos;
      go.transform.localScale = localScale;
      go.GetComponent<MeshRenderer>().sharedMaterial = mat;
      return go;
    }

    private static Image CreateRoundedUIImage(string name, RectTransform parent, Color color)
    {
      var img = new GameObject(name).AddComponent<Image>();
      img.transform.SetParent(parent, false);
      img.sprite = ProcTextures.Rounded;
      img.type = Image.Type.Sliced;
      img.pixelsPerUnitMultiplier = 2.5f;
      img.color = color;
      img.raycastTarget = false;
      var rect = img.rectTransform;
      rect.anchorMin = Vector2.zero;
      rect.anchorMax = Vector2.one;
      rect.offsetMin = rect.offsetMax = Vector2.zero;
      return img;
    }

    private static Text CreateUIText(string name, RectTransform parent, Font font, string content, int fontSize, Color color)
    {
      var txt = new GameObject(name).AddComponent<Text>();
      txt.transform.SetParent(parent, false);
      txt.font = font;
      txt.text = content;
      txt.fontSize = fontSize;
      txt.fontStyle = FontStyle.Bold;
      txt.color = color;
      txt.alignment = TextAnchor.MiddleCenter;
      txt.horizontalOverflow = HorizontalWrapMode.Overflow;
      txt.verticalOverflow = VerticalWrapMode.Overflow;
      txt.raycastTarget = false;
      var rect = txt.rectTransform;
      rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
      return txt;
    }

    private void OnDestroy()
    {
      foreach (var mat in _materials)
      {
        if (mat != null)
        {
          Destroy(mat);
        }
      }
      _materials.Clear();
      if (Root != null)
      {
        Destroy(Root.gameObject);
      }
      if (CupRoot != null)
      {
        Destroy(CupRoot.gameObject);
      }
    }
  }
}
