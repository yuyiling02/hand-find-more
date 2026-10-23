using UnityEngine;

namespace RehabDemo
{
  /// <summary>
  ///   食指尖“指针”，负责两件事：
  ///   1. 指向：因为单目摄像头测不准手的高度，这里不做真实的 3D 碰撞，而是从相机穿过食指尖发一条射线，
  ///      屏幕上指尖盖住哪个按钮，就算指着哪个按钮。
  ///   2. 点击：食指快速弯一下（像点鼠标）算一次点击。用未平滑的原始关键点判断，否则快速动作会被平滑掉。
  /// </summary>
  [DefaultExecutionOrder(-50)]
  public class FingertipPointer : MonoBehaviour
  {
    [Tooltip("留空则使用 Main Camera")]
    public Camera viewCamera;

    [Header("点击手势（数值是食指相对手掌压下去的角度，单位度）")]
    [Tooltip("比平时多压下这么多度算按下。调小更灵敏，调大更不容易误触")]
    public float pressDelta = 9f;
    [Tooltip("回落到比平时只多这么多度算松开")]
    public float releaseDelta = 4f;
    [Tooltip("“平时的角度”跟随当前值的时间（秒）。慢慢弯手指不算点击，只有比这更快的才算")]
    public float baselineTime = 0.5f;
    [Tooltip("对角度做的轻微平滑，0 = 不平滑，越接近 1 越稳但越迟钝")]
    [Range(0f, 0.9f)] public float smoothing = 0.4f;
    [Tooltip("按下超过这么久还没松开就自动复位（秒）")]
    public float maxPressTime = 1f;

    /// <summary>这一帧刚发生了一次点击（按下的瞬间）。</summary>
    public static bool ClickedThisFrame { get; private set; }
    /// <summary>食指正处于按下状态。</summary>
    public static bool IsPressing { get; private set; }
    /// <summary>食指当前压下去的角度（度），给调试面板用。</summary>
    public static float Curl { get; private set; }
    /// <summary>食指平时的角度（慢速跟随），给调试面板用。</summary>
    public static float Baseline { get; private set; }

    private static bool _hasRay;
    private static Ray _ray;

    private bool _hasBaseline;
    private long _lastTimestampMs;
    private float _pressTimer;

    public static bool TryGetRay(out Ray ray)
    {
      ray = _ray;
      return _hasRay;
    }

    private void Update()
    {
      ClickedThisFrame = false;

      if (viewCamera == null)
      {
        viewCamera = Camera.main;
      }
      var pose = HandFrameBus.Current;
      if (viewCamera == null || !pose.tracked || pose.joints == null)
      {
        ResetState();
        return;
      }

      // 原始结果大约每秒 30 帧，每来一帧新的才更新一次点击状态
      var raw = HandFrameBus.Raw;
      if (raw != null && raw.hasHand && raw.timestampMs != _lastTimestampMs)
      {
        var dt = Mathf.Clamp((raw.timestampMs - _lastTimestampMs) / 1000f, 0f, 0.2f);
        _lastTimestampMs = raw.timestampMs;
        UpdateClick(HandMetrics.IndexTapAngle(raw.world), dt);
      }

      // 弯手指时指尖会跟着移动，所以按下过程中保持按下之前指着的位置
      var steady = !IsPressing && Curl - Baseline < releaseDelta;
      if (steady || !_hasRay)
      {
        var origin = viewCamera.transform.position;
        _ray = new Ray(origin, pose.joints[LandmarkIds.IndexTip] - origin);
        _hasRay = true;
      }
    }

    private void UpdateClick(float curl, float dt)
    {
      if (!_hasBaseline)
      {
        Curl = curl;
        Baseline = curl;
        _hasBaseline = true;
      }
      // 原始角度每帧有几度的抖动，轻微平滑一下再比较
      curl = Mathf.Lerp(curl, Curl, smoothing);
      Curl = curl;

      var delta = curl - Baseline;
      if (!IsPressing)
      {
        if (delta > pressDelta)
        {
          IsPressing = true;
          ClickedThisFrame = true;
          _pressTimer = 0f;
        }
        else
        {
          Baseline = Mathf.Lerp(Baseline, curl, baselineTime > 0f ? 1f - Mathf.Exp(-dt / baselineTime) : 1f);
        }
      }
      else
      {
        _pressTimer += dt;
        if (delta < releaseDelta)
        {
          IsPressing = false;
        }
        else if (_pressTimer > maxPressTime)
        {
          // 手指一直弯着（比如握拳了），把现在的姿势当作新的“平时”
          IsPressing = false;
          Baseline = curl;
        }
      }
    }

    private void ResetState()
    {
      _hasRay = false;
      _hasBaseline = false;
      IsPressing = false;
    }

    private void OnDisable()
    {
      ClickedThisFrame = false;
      ResetState();
    }
  }
}
