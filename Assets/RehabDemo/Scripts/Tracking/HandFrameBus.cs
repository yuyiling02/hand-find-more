using System;
using UnityEngine;

namespace RehabDemo
{
  /// <summary>
  ///   主线程每帧从 <see cref="HandTrackingRunner" /> 取最新结果，转成 Unity 世界坐标并做 One Euro 平滑。
  ///   其他模块通过 <see cref="Current" /> 或 <see cref="OnUpdated" /> 读取。
  /// </summary>
  [DefaultExecutionOrder(-100)]
  public class HandFrameBus : MonoBehaviour
  {
    public static HandPose Current;
    public static event Action<HandPose> OnUpdated;

    /// <summary>最新一帧的原始结果（未平滑），给调试面板用。</summary>
    public static RawHandFrame Raw { get; private set; }

    public HandTrackingRunner runner;

    [Tooltip("只接受哪只手，用于只训练患侧手")]
    public HandFilter handFilter = HandFilter.Any;

    [Header("形状")]
    [Tooltip("虚拟手相对真手的放大倍数")]
    public float handScale = 1.6f;
    [Tooltip("手指弯向反方向时切换")]
    public bool invertZ = false;
    [Tooltip("把竖着举在摄像头前的手转成趴在桌面上方")]
    public Vector3 cameraToTableEuler = new Vector3(70f, 0f, 0f);

    [Header("位置（桌面活动区域，单位米，桌面中心为原点）")]
    // 手掌中心的活动范围。食指尖在画面上比手掌中心更靠远端，所以整体偏向相机一侧，
    // 这样手在摄像头画面中间时，指尖正好落在桌面中部，够得到 MENU 和卡片
    public float areaXMin = -0.55f;
    public float areaXMax = 0.55f;
    public float areaZMin = -0.50f;
    public float areaZMax = 0.10f;
    [Tooltip("手离桌面的固定高度。单目摄像头的深度不可靠，所以不随深度变化")]
    public float hoverHeight = 0.12f;

    [Header("平滑（One Euro Filter）")]
    public float jointMinCutoff = 1.0f;
    public float positionMinCutoff = 0.6f;
    public float beta = 0.02f;
    public float dCutoff = 1.0f;

    [Header("丢失处理")]
    [Tooltip("连续这么久没检测到手就判定为丢失")]
    public float lostTimeout = 0.3f;

    private readonly RawHandFrame _raw = new RawHandFrame();
    private readonly Vector3[] _joints = new Vector3[LandmarkIds.Count];
    private readonly OneEuroFilter3[] _jointFilters = new OneEuroFilter3[LandmarkIds.Count];
    private readonly OneEuroFilter3 _positionFilter = new OneEuroFilter3();
    private int _sequence;
    private long _lastFilteredTimestampMs;
    private float _lastSeenTime = float.NegativeInfinity;
    private bool _filtersNeedReset = true;

    private void Awake()
    {
      if (runner == null)
      {
        runner = GetComponent<HandTrackingRunner>();
      }
      for (var i = 0; i < _jointFilters.Length; i++)
      {
        _jointFilters[i] = new OneEuroFilter3();
      }
      Current = new HandPose { tracked = false, isLeft = false, joints = _joints };
      Raw = _raw;
    }

    private void Update()
    {
      if (runner == null)
      {
        return;
      }

      if (runner.TryGetLatest(_raw, ref _sequence) && _raw.hasHand && Accepts(_raw.isLeft))
      {
        ApplyFrame();
        _lastSeenTime = Time.unscaledTime;
        Current.tracked = true;
        Current.isLeft = _raw.isLeft;
      }
      else if (Current.tracked && Time.unscaledTime - _lastSeenTime > lostTimeout)
      {
        Current.tracked = false;
        // 重新检测到时从新位置开始，避免手从旧位置“飞”过来
        _filtersNeedReset = true;
      }

      OnUpdated?.Invoke(Current);
    }

    private bool Accepts(bool isLeft)
    {
      switch (handFilter)
      {
        case HandFilter.LeftOnly: return isLeft;
        case HandFilter.RightOnly: return !isLeft;
        default: return true;
      }
    }

    private void ApplyFrame()
    {
      if (_filtersNeedReset)
      {
        _positionFilter.Reset();
        foreach (var filter in _jointFilters)
        {
          filter.Reset();
        }
        _filtersNeedReset = false;
        _lastFilteredTimestampMs = _raw.timestampMs;
      }
      var dt = (_raw.timestampMs - _lastFilteredTimestampMs) / 1000f;
      _lastFilteredTimestampMs = _raw.timestampMs;

      // 位置：手掌中心在图像里的位置 → 桌面活动区域
      var n = _raw.normalized;
      var palmN = (n[LandmarkIds.Wrist] + n[LandmarkIds.IndexMcp] + n[LandmarkIds.PinkyMcp]) / 3f;
      var position = new Vector3(
        Mathf.Lerp(areaXMin, areaXMax, palmN.x),
        hoverHeight,
        Mathf.Lerp(areaZMax, areaZMin, palmN.y));
      _positionFilter.SetParams(positionMinCutoff, beta, dCutoff);
      position = _positionFilter.Filter(position, dt);

      // 形状：world landmarks（米，y 朝下）→ Unity 局部坐标 → 转到桌面姿态
      var w = _raw.world;
      var palmLocal = (ToLocal(w[LandmarkIds.Wrist]) + ToLocal(w[LandmarkIds.IndexMcp]) + ToLocal(w[LandmarkIds.PinkyMcp])) / 3f;
      var rotation = Quaternion.Euler(cameraToTableEuler);
      for (var i = 0; i < LandmarkIds.Count; i++)
      {
        var offset = rotation * (ToLocal(w[i]) - palmLocal);
        _jointFilters[i].SetParams(jointMinCutoff, beta, dCutoff);
        _joints[i] = position + _jointFilters[i].Filter(offset, dt);
      }
    }

    private Vector3 ToLocal(Vector3 world) => new Vector3(world.x, -world.y, invertZ ? world.z : -world.z) * handScale;

    private void OnDestroy()
    {
      Current.tracked = false;
    }

    // 还没有虚拟手的时候（M1），在 Scene 视图里用小球看关节位置
    private void OnDrawGizmos()
    {
      Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
      var center = new Vector3((areaXMin + areaXMax) / 2f, hoverHeight, (areaZMin + areaZMax) / 2f);
      Gizmos.DrawWireCube(center, new Vector3(areaXMax - areaXMin, 0f, areaZMax - areaZMin));

      if (!Application.isPlaying || !Current.tracked || Current.joints == null)
      {
        return;
      }
      Gizmos.color = new Color(0.37f, 0.83f, 0.82f);
      for (var i = 0; i < LandmarkIds.Bones.GetLength(0); i++)
      {
        Gizmos.DrawLine(_joints[LandmarkIds.Bones[i, 0]], _joints[LandmarkIds.Bones[i, 1]]);
      }
      foreach (var joint in _joints)
      {
        Gizmos.DrawSphere(joint, 0.012f);
      }
    }
  }
}
