using UnityEngine;

namespace RehabDemo
{
  /// <summary>
  ///   微波炉日常交互训练：
  ///   ① 拉门把手开门 → ② 捏起桌上的杯子放进空心炉腔转盘 → ③ 推门/拉把手关门 → ④ 按右侧“开始”按钮 → 倒计时转盘旋转、腔内亮灯、“叮”一声完成。
  ///   同时支持 MediaPipe 单目手势（捏合/握拳抓取、食指点击/悬停、挥手推门）与鼠标点击拖拽调试。
  /// </summary>
  public class MicrowaveExercise : ExerciseBase
  {
    private enum CupState { OnTable, Held, InMicrowave, Returning }

    [Header("摆放位置（桌面中心为原点）")]
    public Vector3 microwavePosition = new Vector3(-0.12f, 0f, 0.07f);
    public float microwaveYaw = 10f;
    public Vector3 cupStartPosition = new Vector3(0.11f, 0f, -0.07f);

    [Header("交互手感参数")]
    [Tooltip("捏合或握拳达到该值即判定为抓握（比单纯捏合训练更宽松，方便移动手部）")]
    public float grabEnterScore = 0.58f;
    [Tooltip("抓握度低于该值并持续一小段时间才松手，防止手移动时中途掉杯子")]
    public float grabExitScore = 0.35f;
    [Tooltip("松手防抖时间（秒）")]
    public float releaseGraceTime = 0.12f;
    [Tooltip("门把手的屏幕射线判定半径（米）")]
    public float handleHitRadius = 0.058f;
    [Tooltip("杯子的屏幕射线抓取半径（米）")]
    public float cupGrabRadius = 0.062f;
    [Tooltip("炉腔转盘的磁吸捕获半径（米）")]
    public float cavitySnapRadius = 0.095f;
    [Tooltip("把杯子悬停在转盘磁吸区内多久自动落座（秒）")]
    public float autoDockTime = 0.50f;
    [Tooltip("开始按钮的判定半径（米）")]
    public float buttonHitRadius = 0.048f;
    [Tooltip("食指指向开始按钮停留多久自动触发（秒）")]
    public float buttonDwellTime = 0.42f;
    [Tooltip("微波加热倒计时时长（秒）")]
    public float heatingDuration = 3.0f;
    [Tooltip("Idle 空闲状态下是否也在桌面上显示并允许直接交互")]
    public bool activeInIdle = true;

    public override string DisplayName => "微波炉";

    public string CurrentStepSummary { get; private set; } = "① 拉开微波炉门";

    private MicrowaveOven _oven;
    private Camera _camera;

    // 门状态
    private float _doorOpenRatio;
    private float _doorTargetOpen;
    private float _doorVelocity;
    private bool _draggingDoor;
    private bool _doorDragClosing;
    private float _doorCloseLockTimer;
    private bool _doorLatched = true;
    private Vector3 _lastPalmTablePos;
    private bool _hasLastPalmPos;

    // 杯子状态
    private CupState _cupState = CupState.OnTable;
    private Vector3 _cupPosition;
    private Vector3 _cupVelocity;
    private Quaternion _cupRotation = Quaternion.identity;
    private float _cupScale = 1f;
    private bool _overTurntable;
    private float _turntableHoldTimer;
    private float _turntableGraceTimer;
    private float _cupPlacedLockTimer;
    private bool _hasReleasedSinceDock = true;
    private bool _pickedFromMicrowave;
    private bool _cavityMagnetArmed = true;

    // 按钮与加热状态
    private float _buttonDwellTimer;
    private bool _isHeating;
    private float _heatTimer;
    private int _lastWholeSecond = -1;
    private bool _heatedDone;
    private float _hintOverrideTimer;
    private string _hintOverrideText;

    // 手势抓握状态（含防抖）
    private bool _isGrabbing;
    private bool _justGrabbed;
    private float _releaseTimer;
    private float _currentGrabScore;
    private float _currentHandAngle;

    private static readonly Color _LedGreen = new Color32(0x57, 0xF2, 0x87, 0xFF);
    private static readonly Color _LedAmber = new Color32(0xFF, 0xC8, 0x2E, 0xFF);
    private static readonly Color _LedCyan = new Color32(0x5F, 0xD3, 0xD0, 0xFF);

    private void Start()
    {
      EnsureBuilt();
      ResetSequence(false);
    }

    private void EnsureBuilt()
    {
      if (_oven != null)
      {
        return;
      }
      _oven = gameObject.AddComponent<MicrowaveOven>();
      _oven.Build(transform, microwavePosition, microwaveYaw);
      _camera = Camera.main;
    }

    protected override float ComputeScore(Vector3[] joints)
    {
      // 用完成步骤数映射平滑进度，不走基类的静态保持计次，由步骤推进直接驱动进度
      return Mathf.Clamp(_currentGrabScore * 0.7f, 0f, 0.75f);
    }

    protected override float ComputeAngle(Vector3[] joints) => _currentHandAngle;

    protected override void OnBegin()
    {
      EnsureBuilt();
      _oven.SetVisible(true);
      // 如果上次已经做完，重新开始时复位一轮新的
      if (_heatedDone)
      {
        ResetSequence(false);
      }
      UpdateHudProgress();
    }

    protected override void OnEnd()
    {
      var sfx = GameFlow.Instance != null ? GameFlow.Instance.sfx : null;
      if (sfx != null)
      {
        sfx.SetMicrowaveHum(false);
      }
    }

    /// <summary>重置到初始状态（门关、杯子在桌上、未加热）。</summary>
    public void ResetSequence(bool playSound = true)
    {
      EnsureBuilt();
      _doorOpenRatio = 0f;
      _doorTargetOpen = 0f;
      _doorVelocity = 0f;
      _draggingDoor = false;
      _doorDragClosing = false;
      _doorCloseLockTimer = 0f;
      _doorLatched = true;

      _cupState = CupState.OnTable;
      _cupPosition = cupStartPosition;
      _cupVelocity = Vector3.zero;
      _cupRotation = Quaternion.identity;
      _cupScale = 1f;
      _overTurntable = false;
      _turntableHoldTimer = 0f;
      _turntableGraceTimer = 0f;
      _cupPlacedLockTimer = 0f;
      _hasReleasedSinceDock = true;
      _pickedFromMicrowave = false;
      _cavityMagnetArmed = true;

      _buttonDwellTimer = 0f;
      _isHeating = false;
      _heatTimer = heatingDuration;
      _lastWholeSecond = -1;
      _heatedDone = false;
      _hintOverrideTimer = 0f;

      _oven.CupRoot.SetParent(transform, true);
      _oven.CupRoot.position = _cupPosition;
      _oven.CupRoot.rotation = _cupRotation;
      _oven.CupRoot.localScale = Vector3.one;
      _oven.SetDoorOpenRatio(0f);
      _oven.SetSteamActive(false);

      var sfx = GameFlow.Instance != null ? GameFlow.Instance.sfx : null;
      if (sfx != null)
      {
        sfx.SetMicrowaveHum(false);
        if (playSound)
        {
          sfx.PlayClick();
        }
      }
      UpdateHudProgress();
    }

    private void LateUpdate()
    {
      EnsureBuilt();
      if (_camera == null)
      {
        _camera = Camera.main;
      }

      // 当切换到别的训练（捏合/握拳/屈指）时隐藏微波炉；在 Idle 或微波炉训练中保持可见并可操作
      var otherExerciseActive = Active != null && Active != this;
      var shouldShow = IsRunning || (activeInIdle && !otherExerciseActive);
      _oven.SetVisible(shouldShow);
      if (!shouldShow)
      {
        var sfx = GameFlow.Instance != null ? GameFlow.Instance.sfx : null;
        if (sfx != null)
        {
          sfx.SetMicrowaveHum(false);
        }
        return;
      }

      // 按 R 键可随时重置微波炉状态
      if (Input.GetKeyDown(KeyCode.R))
      {
        ResetSequence(true);
      }

      // 打开菜单选卡时暂停微波炉手势判定，避免选卡片时误触杯子
      if (GameFlow.Instance != null && GameFlow.Instance.State == GameFlow.FlowState.MenuOpen)
      {
        UpdateVisuals();
        return;
      }

      StepInteraction(HandFrameBus.Current);
    }

    private void StepInteraction(HandPose pose)
    {
      var sfx = GameFlow.Instance != null ? GameFlow.Instance.sfx : null;

      // 1. 解析手部（或鼠标）射线与抓握/点击状态
      var hasRay = false;
      var grabRay = default(Ray);
      var pointRay = default(Ray);
      var clickedThisFrame = false;

      var tracked = pose.tracked && pose.joints != null && _camera != null;
      if (tracked)
      {
        var j = pose.joints;
        var origin = _camera.transform.position;
        var grabPoint = (j[LandmarkIds.ThumbTip] + j[LandmarkIds.IndexTip]) * 0.5f;
        grabRay = new Ray(origin, (grabPoint - origin).normalized);
        if (!FingertipPointer.TryGetRay(out pointRay))
        {
          pointRay = new Ray(origin, (j[LandmarkIds.IndexTip] - origin).normalized);
        }
        hasRay = true;

        var pinchOpen = config != null ? config.pinchOpenRatio : 0.90f;
        var pinchClosed = config != null ? config.pinchClosedRatio : 0.25f;
        var fistCurl = config != null ? config.fistFullCurl : 0.85f;
        var pinch = Mathf.InverseLerp(pinchOpen, pinchClosed, HandMetrics.PinchRatio(j));
        var fist = HandMetrics.Fistness(j, fistCurl);
        _currentGrabScore = Mathf.Max(pinch, fist);
        _currentHandAngle = HandMetrics.PinchAngle(j);

        UpdateGrabHysteresis(_currentGrabScore);
        clickedThisFrame = FingertipPointer.ClickedThisFrame;
      }
      else
      {
        _currentGrabScore = 0f;
        // 鼠标兜底：没伸手或用鼠标点击拖拽时同样能完整走通流程
        if (_camera != null && (Input.GetMouseButton(0) || Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0) || Input.mousePresent))
        {
          grabRay = _camera.ScreenPointToRay(Input.mousePosition);
          pointRay = grabRay;
          hasRay = true;
          _justGrabbed = Input.GetMouseButtonDown(0);
          _isGrabbing = Input.GetMouseButton(0);
          clickedThisFrame = Input.GetMouseButtonDown(0);
        }
        else
        {
          _justGrabbed = false;
          _isGrabbing = false;
        }
      }

      // 2. 更新门、杯子、按钮与加热逻辑
      UpdateDoor(hasRay, grabRay, pointRay, clickedThisFrame, sfx);
      UpdateCup(hasRay, grabRay, pointRay, clickedThisFrame, sfx);
      UpdateStartButtonAndHeating(hasRay, pointRay, grabRay, clickedThisFrame, sfx);
      UpdateVisuals();
    }

    private void UpdateGrabHysteresis(float grabScore)
    {
      _justGrabbed = false;
      if (!_isGrabbing)
      {
        if (grabScore >= grabEnterScore)
        {
          _isGrabbing = true;
          _justGrabbed = true;
          _releaseTimer = 0f;
        }
      }
      else
      {
        if (grabScore < grabExitScore)
        {
          _releaseTimer += Time.deltaTime;
          if (_releaseTimer >= releaseGraceTime)
          {
            _isGrabbing = false;
            _releaseTimer = 0f;
          }
        }
        else
        {
          _releaseTimer = 0f;
        }
      }
    }

    private float GetCupRayDistance(Ray grabRay, Ray pointRay)
    {
      var cupCenter = _cupPosition + Vector3.up * 0.035f;
      var dist = Mathf.Min(DistanceRayToPoint(grabRay, cupCenter), DistanceRayToPoint(pointRay, cupCenter));
      if (_cupState == CupState.InMicrowave)
      {
        var entryCenter = _oven.CavityEntryWorld;
        var slotCenter = _oven.TurntableSurfaceWorld + Vector3.up * 0.032f;
        dist = Mathf.Min(dist, DistanceRayToPoint(grabRay, entryCenter));
        dist = Mathf.Min(dist, DistanceRayToPoint(pointRay, entryCenter));
        dist = Mathf.Min(dist, DistanceRayToPoint(grabRay, slotCenter));
        dist = Mathf.Min(dist, DistanceRayToPoint(pointRay, slotCenter));
      }
      return dist;
    }

    private bool IsCupReachable()
    {
      return _cupState == CupState.OnTable ||
             _cupState == CupState.Returning ||
             (_cupState == CupState.InMicrowave && _doorOpenRatio > 0.35f && !_isHeating &&
              _cupPlacedLockTimer <= 0f && _hasReleasedSinceDock);
    }

    private float GetEffectiveCupGrabRadius()
    {
      return _cupState == CupState.InMicrowave
        ? Mathf.Max(cupGrabRadius * 1.35f, cavitySnapRadius)
        : cupGrabRadius;
    }

    private void UpdateDoor(bool hasRay, Ray grabRay, Ray pointRay, bool clickedThisFrame, Sfx sfx)
    {
      var handleTop = _oven.HandleCenterWorld + Vector3.up * 0.055f;
      var handleBottom = _oven.HandleCenterWorld - Vector3.up * 0.055f;
      var handleDist = hasRay
        ? Mathf.Min(DistanceRayToSegment(grabRay, handleBottom, handleTop),
                    DistanceRayToSegment(pointRay, handleBottom, handleTop))
        : 99f;
      var doorPanelDist = hasRay
        ? Mathf.Min(DistanceRayToPoint(grabRay, _oven.DoorCenterWorld),
                    DistanceRayToPoint(pointRay, _oven.DoorCenterWorld))
        : 99f;
      var aimedHandle = hasRay && handleDist <= handleHitRadius;
      var aimedDoorPanel = hasRay && doorPanelDist <= handleHitRadius * 1.45f;

      // 当手明显对准可抓取的杯子（且比门把手更近）时，优先操作杯子，避免误关门/误抓门
      var cupDist = hasRay && IsCupReachable() ? GetCupRayDistance(grabRay, pointRay) : 99f;
      var prioritizingCup = cupDist <= GetEffectiveCupGrabRadius() && cupDist < handleDist;
      var aimedDoor = (aimedHandle || aimedDoorPanel) && _cupState != CupState.Held && !prioritizingCup;

      var handTablePos = hasRay ? RayAtHeight(grabRay, 0.11f) : Vector3.zero;
      var frameDelta = Vector3.zero;
      if (hasRay)
      {
        if (_hasLastPalmPos && Time.deltaTime > 1e-4f)
        {
          frameDelta = handTablePos - _lastPalmTablePos;
        }
        _lastPalmTablePos = handTablePos;
        _hasLastPalmPos = true;
      }
      else
      {
        _hasLastPalmPos = false;
      }

      if (_doorCloseLockTimer > 0f)
      {
        _doorCloseLockTimer -= Time.deltaTime;
      }

      // 捏住/握住把手开始拖拽：
      // - 门关着时（_doorOpenRatio < 0.18f），必须对准门把手且是新发起的一次捏合（_justGrabbed）
      // - 门开着时，对准把手或门板并产生新捏合即可拖拽
      var canStartDrag = _doorCloseLockTimer <= 0f && _cupState != CupState.Held &&
                         (_doorOpenRatio < 0.18f ? aimedHandle : aimedDoor);
      if (!_draggingDoor && _justGrabbed && canStartDrag)
      {
        _draggingDoor = true;
        _doorDragClosing = _doorOpenRatio >= 0.45f;
        _doorTargetOpen = _doorOpenRatio;
        EnsureExerciseStartedOnInteract();
      }

      var firmHold = _currentGrabScore >= grabEnterScore || Input.GetMouseButton(0);
      // 手是否仍然贴近门把手或门板（一旦手离开门的区域，立即与门脱离，门不再跟随手移动）
      var handNearDoor = hasRay && Mathf.Min(handleDist, doorPanelDist) <= handleHitRadius * 2.25f;

      if (_draggingDoor)
      {
        if (!_isGrabbing || !firmHold || !handNearDoor)
        {
          // 手松开或离开门：立即锁定在当前门角度，完全停止跟随
          _draggingDoor = false;
          _doorTargetOpen = _doorOpenRatio;
          _doorVelocity = 0f;
        }
        else
        {
          // 往左（-X）或往近处（-Z）拉是缓缓开门；往右（+X）或往远处（+Z）推是缓缓关门
          var pullDelta = (-frameDelta.x * 3.8f) + (-frameDelta.z * 2.5f);
          if (_doorDragClosing)
          {
            // 关门拖拽：只响应关门方向，防止手回撤把门带开
            if (pullDelta < 0f)
            {
              _doorTargetOpen = Mathf.Clamp01(_doorTargetOpen + pullDelta);
            }
            if (_doorTargetOpen <= 0.14f)
            {
              _doorTargetOpen = 0f;
              _draggingDoor = false;
              _doorCloseLockTimer = 0.65f;
            }
          }
          else
          {
            // 开门拖拽：只响应开门方向，手移开或往右去拿杯子时绝不把门带回；拉满到位自动脱离
            if (pullDelta > 0f)
            {
              _doorTargetOpen = Mathf.Clamp01(_doorTargetOpen + pullDelta);
            }
            if (_doorTargetOpen >= 0.96f)
            {
              _doorTargetOpen = 1f;
              _draggingDoor = false;
              _doorCloseLockTimer = 0.45f;
            }
          }
        }
      }

      // 真实门锁卡扣阻尼：未抓门且门已被推到接近门框（< 14%）时，缓闭阻尼器带动门慢慢合拢锁死；
      // 在其余角度（14% ~ 100%）手一旦离开门，门立即停留在当前位置，绝不继续乱动
      if (!_draggingDoor)
      {
        if (_doorTargetOpen > 0f && _doorTargetOpen < 0.14f)
        {
          _doorTargetOpen = Mathf.MoveTowards(_doorTargetOpen, 0f, 0.45f * Time.deltaTime);
          if (_doorTargetOpen <= 0.01f)
          {
            _doorTargetOpen = 0f;
            _doorCloseLockTimer = Mathf.Max(_doorCloseLockTimer, 0.55f);
          }
        }
        else
        {
          _doorTargetOpen = _doorOpenRatio;
          _doorVelocity = 0f;
        }
      }

      // 加热中途如果把门拉开了，自动暂停加热
      if (_isHeating && _doorOpenRatio > 0.20f)
      {
        _isHeating = false;
        if (sfx != null)
        {
          sfx.SetMicrowaveHum(false);
        }
      }

      // 仅在手抓着门或卡扣最后吸合时平滑推进；手离开后门静止不动
      if (_draggingDoor || (_doorTargetOpen < 0.14f && _doorOpenRatio > 0f))
      {
        var maxSpeed = _doorTargetOpen < _doorOpenRatio ? 0.68f : 0.85f;
        _doorOpenRatio = Mathf.SmoothDamp(_doorOpenRatio, _doorTargetOpen, ref _doorVelocity, 0.07f, maxSpeed);
      }
      _oven.SetDoorOpenRatio(_doorOpenRatio);

      // 根据真实门锁卡扣位置触发“咔哒”声（拉开脱扣时响开门声，缓缓推到底扣合时响关门声）
      if (_doorLatched && _doorOpenRatio > 0.08f)
      {
        _doorLatched = false;
        if (sfx != null)
        {
          sfx.PlayDoorOpen();
        }
        if (colorizer != null)
        {
          colorizer.Flash();
        }
      }
      else if (!_doorLatched && _doorOpenRatio < 0.025f && _doorTargetOpen < 0.05f)
      {
        _doorLatched = true;
        _doorOpenRatio = 0f;
        _doorTargetOpen = 0f;
        _doorVelocity = 0f;
        if (sfx != null)
        {
          sfx.PlayDoorClose();
        }
        if (colorizer != null)
        {
          colorizer.Flash();
        }
      }

      // 提示是否该操作门（第 1 步需要开门，第 3 步需要关门，或者加热完需要开门取杯）
      var shouldPromptDoor = (_cupState == CupState.OnTable && _doorOpenRatio < 0.5f) ||
                             (_cupState == CupState.InMicrowave && !_heatedDone && _doorOpenRatio >= 0.12f) ||
                             (_cupState == CupState.InMicrowave && _heatedDone && _doorOpenRatio < 0.5f);
      _oven.SetHandleFeedback(aimedDoor || _draggingDoor, shouldPromptDoor);
    }

    private void UpdateCup(bool hasRay, Ray grabRay, Ray pointRay, bool clickedThisFrame, Sfx sfx)
    {
      var canReachCup = IsCupReachable();
      var cupRayDist = hasRay ? GetCupRayDistance(grabRay, pointRay) : 99f;
      var aimedCup = hasRay && canReachCup && !_draggingDoor &&
                     cupRayDist <= GetEffectiveCupGrabRadius();

      switch (_cupState)
      {
        case CupState.OnTable:
          _oven.CupRoot.SetParent(transform, true);
          _cupPosition = Vector3.Lerp(_cupPosition, cupStartPosition, 1f - Mathf.Exp(-Time.deltaTime / 0.08f));
          _cupRotation = Quaternion.Slerp(_cupRotation, Quaternion.identity, 1f - Mathf.Exp(-Time.deltaTime / 0.08f));
          if (aimedCup && _justGrabbed)
          {
            BeginHoldCup(sfx, false);
          }
          break;

        case CupState.InMicrowave:
          // 杯子在转盘上时挂在 Turntable 下，转盘转动时杯子一起旋转
          if (_oven.CupRoot.parent != _oven.Turntable)
          {
            _oven.CupRoot.SetParent(_oven.Turntable, true);
          }
          _oven.CupRoot.localPosition = Vector3.Lerp(_oven.CupRoot.localPosition, new Vector3(0f, 0.005f, 0f), 1f - Mathf.Exp(-Time.deltaTime / 0.06f));
          _oven.CupRoot.localRotation = Quaternion.Slerp(_oven.CupRoot.localRotation, Quaternion.identity, 1f - Mathf.Exp(-Time.deltaTime / 0.06f));
          _cupPosition = _oven.CupRoot.position;
          _cupRotation = _oven.CupRoot.rotation;

          if (_cupPlacedLockTimer > 0f)
          {
            _cupPlacedLockTimer -= Time.deltaTime;
          }
          if (!_isGrabbing)
          {
            _hasReleasedSinceDock = true;
          }

          // 放置稳定且松开过手之后，再次对准杯子发起新的捏合抓握（_justGrabbed），即可随时把杯子从微波炉里拿出来
          if (_cupPlacedLockTimer <= 0f && _hasReleasedSinceDock && aimedCup && _justGrabbed)
          {
            _heatedDone = false;
            _oven.SetSteamActive(false);
            BeginHoldCup(sfx, true);
          }
          else if (!_isHeating && _doorOpenRatio <= 0.35f && hasRay && !_draggingDoor &&
                   cupRayDist <= cupGrabRadius && _justGrabbed)
          {
            _hintOverrideText = "请先拉开微波炉门，再取出杯子！";
            _hintOverrideTimer = 1.6f;
          }
          break;

        case CupState.Held:
          _oven.CupRoot.SetParent(transform, true);
          var carryPos = hasRay ? RayAtHeight(grabRay, 0.058f) : _cupPosition;
          carryPos.x = Mathf.Clamp(carryPos.x, -0.45f, 0.45f);
          carryPos.z = Mathf.Clamp(carryPos.z, -0.28f, 0.24f);

          // 炉腔磁吸判定：当门已打开且射线靠近转盘上方区域时，平滑把杯子吸进空心炉腔内
          var slotPos = _oven.TurntableSurfaceWorld;
          var slotCenter = slotPos + Vector3.up * 0.032f;
          var entryCenter = _oven.CavityEntryWorld;
          var distToSlot = hasRay ? Mathf.Min(DistanceRayToPoint(grabRay, slotCenter), DistanceRayToPoint(grabRay, entryCenter)) : 99f;

          // 如果是从微波炉里拿出来的，必须先把杯子移回桌面右侧区域后，才重新激活放入磁吸，避免取出中途被吸回转盘
          if (!_cavityMagnetArmed && distToSlot > cavitySnapRadius * 1.35f && _cupPosition.x > 0.02f)
          {
            _cavityMagnetArmed = true;
          }

          _overTurntable = _cavityMagnetArmed && _doorOpenRatio > 0.35f && distToSlot <= cavitySnapRadius;
          if (_overTurntable)
          {
            var magnet = Mathf.Lerp(0.60f, 0.90f, 1f - Mathf.Clamp01(distToSlot / cavitySnapRadius));
            carryPos = Vector3.Lerp(carryPos, slotPos + Vector3.up * 0.006f, magnet);
            _turntableHoldTimer += Time.deltaTime;
            _turntableGraceTimer = 0.38f;
          }
          else
          {
            _turntableHoldTimer = 0f;
            if (_turntableGraceTimer > 0f)
            {
              _turntableGraceTimer -= Time.deltaTime;
            }
          }

          var prevPos = _cupPosition;
          _cupPosition = Vector3.SmoothDamp(_cupPosition, carryPos, ref _cupVelocity, 0.055f);

          // 移动时带一点物理微倾感
          var horizVel = (_cupPosition - prevPos) / Mathf.Max(Time.deltaTime, 1e-4f);
          var tiltZ = Mathf.Clamp(-horizVel.x * 18f, -12f, 12f);
          var tiltX = Mathf.Clamp(horizVel.z * 18f, -12f, 12f);
          _cupRotation = Quaternion.Slerp(_cupRotation, Quaternion.Euler(tiltX, 0f, tiltZ), 1f - Mathf.Exp(-Time.deltaTime / 0.06f));

          var cupHorizDistToSlot = Vector2.Distance(new Vector2(_cupPosition.x, _cupPosition.z),
                                                    new Vector2(slotPos.x, slotPos.z));
          var cupNearTurntable = _cavityMagnetArmed && _doorOpenRatio > 0.35f &&
                                 cupHorizDistToSlot <= cavitySnapRadius * 1.15f;

          // 松手或悬停在转盘上达到 autoDockTime 即稳稳落座到转盘上（含松手张开手指时的宽限保护，防止一松手误判回桌面）
          if (!_isGrabbing || !hasRay || (_overTurntable && _turntableHoldTimer >= autoDockTime))
          {
            if (_overTurntable || _turntableGraceTimer > 0f || cupNearTurntable)
            {
              PlaceCupInMicrowave(sfx);
            }
            else
            {
              _cupState = CupState.Returning;
              _pickedFromMicrowave = false;
              _cavityMagnetArmed = true;
              if (sfx != null)
              {
                sfx.PlayCupClink();
              }
              UpdateHudProgress();
            }
          }
          break;

        case CupState.Returning:
          _oven.CupRoot.SetParent(transform, true);
          // 如果杯子还在炉腔内部，先沿炉腔正前方开口移出，再平滑回到桌面起始位置，避免穿模右侧控制面板
          var localInOven = _oven.Root.InverseTransformPoint(_cupPosition);
          var returnTarget = (localInOven.z > -0.135f && localInOven.x < 0.08f)
            ? _oven.Root.TransformPoint(new Vector3(-0.045f, 0.045f, -0.16f))
            : cupStartPosition;
          _cupPosition = Vector3.MoveTowards(_cupPosition, returnTarget, 1.15f * Time.deltaTime);
          _cupRotation = Quaternion.Slerp(_cupRotation, Quaternion.identity, 1f - Mathf.Exp(-Time.deltaTime / 0.06f));
          if (Vector3.Distance(_cupPosition, cupStartPosition) < 0.003f)
          {
            _cupPosition = cupStartPosition;
            _cupState = CupState.OnTable;
          }
          else if (aimedCup && _justGrabbed)
          {
            BeginHoldCup(sfx, false);
          }
          break;
      }

      if (_cupState != CupState.InMicrowave)
      {
        _oven.CupRoot.position = _cupPosition;
        _oven.CupRoot.rotation = _cupRotation;
      }

      var targetScale = (_cupState == CupState.OnTable || _cupState == CupState.InMicrowave) && aimedCup ? 1.10f : 1f;
      _cupScale = Mathf.Lerp(_cupScale, targetScale, 1f - Mathf.Exp(-Time.deltaTime / 0.06f));
      _oven.CupRoot.localScale = Vector3.one * _cupScale;

      var showRing = _doorOpenRatio > 0.35f && _cavityMagnetArmed &&
                     (_cupState == CupState.OnTable || _cupState == CupState.Held || _cupState == CupState.Returning);
      var dockProgress = autoDockTime > 0f ? Mathf.Clamp01(_turntableHoldTimer / autoDockTime) : 0f;
      _oven.SetCupAndTurntableFeedback(aimedCup, _cupState == CupState.Held, showRing, _overTurntable, dockProgress);
    }

    private void BeginHoldCup(Sfx sfx, bool fromMicrowave)
    {
      _cupState = CupState.Held;
      _cupVelocity = Vector3.zero;
      _overTurntable = false;
      _turntableHoldTimer = 0f;
      _turntableGraceTimer = 0f;
      _pickedFromMicrowave = fromMicrowave;
      _cavityMagnetArmed = !fromMicrowave;
      if (sfx != null)
      {
        sfx.PlayCupClink();
      }
      EnsureExerciseStartedOnInteract();
      UpdateHudProgress();
    }

    private void PlaceCupInMicrowave(Sfx sfx)
    {
      _cupState = CupState.InMicrowave;
      _overTurntable = false;
      _turntableHoldTimer = 0f;
      _turntableGraceTimer = 0f;
      _cupPlacedLockTimer = 0.70f;
      _hasReleasedSinceDock = !_isGrabbing;
      _pickedFromMicrowave = false;
      _cavityMagnetArmed = false;
      _oven.CupRoot.SetParent(_oven.Turntable, true);
      if (sfx != null)
      {
        sfx.PlayCupClink();
        sfx.PlayDing();
      }
      if (colorizer != null)
      {
        colorizer.Flash();
      }
      UpdateHudProgress();
    }

    private void UpdateStartButtonAndHeating(bool hasRay, Ray pointRay, Ray grabRay, bool clickedThisFrame, Sfx sfx)
    {
      if (_hintOverrideTimer > 0f)
      {
        _hintOverrideTimer -= Time.deltaTime;
      }

      var buttonReady = _cupState == CupState.InMicrowave && _doorOpenRatio < 0.12f && !_isHeating && !_heatedDone;
      var aimedButton = hasRay && _cupState != CupState.Held && !_draggingDoor &&
                        (DistanceRayToPoint(pointRay, _oven.StartButtonWorld) <= buttonHitRadius ||
                         DistanceRayToPoint(grabRay, _oven.StartButtonWorld) <= buttonHitRadius);

      if (aimedButton && !_isHeating)
      {
        _buttonDwellTimer += Time.deltaTime;
        var triggeredByDwell = buttonDwellTime > 0f && _buttonDwellTimer >= buttonDwellTime;
        if (clickedThisFrame || _justGrabbed || triggeredByDwell)
        {
          _buttonDwellTimer = 0f;
          OnStartButtonPressed(buttonReady, sfx);
        }
      }
      else
      {
        _buttonDwellTimer = 0f;
      }

      if (_isHeating)
      {
        _heatTimer -= Time.deltaTime;
        _oven.AdvanceTurntable(140f * Time.deltaTime);

        var sec = Mathf.CeilToInt(Mathf.Max(0f, _heatTimer));
        if (sec != _lastWholeSecond && sec > 0)
        {
          _lastWholeSecond = sec;
          if (sfx != null)
          {
            sfx.PlayClick();
          }
        }

        if (_heatTimer <= 0f)
        {
          CompleteHeating(sfx);
        }
      }

      var pressProgress = _isHeating
        ? 0.6f
        : (buttonDwellTime > 0f ? Mathf.Clamp01(_buttonDwellTimer / buttonDwellTime) : 0f);

      string mainDisplay;
      string subDisplay;
      Color displayColor;

      if (_isHeating)
      {
        var sec = Mathf.CeilToInt(Mathf.Max(0f, _heatTimer));
        mainDisplay = $"00:0{sec}";
        subDisplay = "HEATING";
        displayColor = _LedAmber;
      }
      else if (_heatedDone)
      {
        mainDisplay = "DONE";
        subDisplay = "HOT MILK";
        displayColor = _LedGreen;
      }
      else if (_doorOpenRatio >= 0.2f)
      {
        mainDisplay = "OPEN";
        subDisplay = _cupState == CupState.InMicrowave ? "CLOSE DOOR" : "PUT CUP IN";
        displayColor = _LedCyan;
      }
      else if (_cupState == CupState.InMicrowave)
      {
        var totalSec = Mathf.CeilToInt(heatingDuration);
        mainDisplay = $"00:0{totalSec}";
        subDisplay = "PRESS START";
        displayColor = _LedGreen;
      }
      else
      {
        mainDisplay = "00:00";
        subDisplay = "STANDBY";
        displayColor = _LedCyan;
      }

      _oven.UpdateControlPanel(mainDisplay, subDisplay, displayColor, buttonReady, aimedButton, pressProgress, _isHeating);
    }

    private void OnStartButtonPressed(bool buttonReady, Sfx sfx)
    {
      EnsureExerciseStartedOnInteract();
      if (buttonReady)
      {
        _isHeating = true;
        _heatTimer = heatingDuration;
        _lastWholeSecond = Mathf.CeilToInt(heatingDuration);
        if (sfx != null)
        {
          sfx.PlayClick();
          sfx.SetMicrowaveHum(true);
        }
        if (colorizer != null)
        {
          colorizer.Flash();
        }
        UpdateHudProgress();
        return;
      }

      // 还没关门或还没放杯子就按了按钮：给出友好的文字引导
      if (sfx != null)
      {
        sfx.PlayClick();
      }
      if (_cupState != CupState.InMicrowave)
      {
        _hintOverrideText = _doorOpenRatio < 0.4f ? "请先拉开炉门，把杯子放进微波炉！" : "请先把桌上的杯子放进微波炉转盘！";
        _hintOverrideTimer = 1.6f;
      }
      else if (_doorOpenRatio >= 0.12f)
      {
        _hintOverrideText = "请先关上微波炉门，再按“开始”！";
        _hintOverrideTimer = 1.6f;
      }
    }

    private void CompleteHeating(Sfx sfx)
    {
      _isHeating = false;
      _heatedDone = true;
      _oven.SetSteamActive(true);

      if (sfx != null)
      {
        sfx.SetMicrowaveHum(false);
        sfx.PlayMicrowaveDing();
      }
      if (colorizer != null)
      {
        colorizer.Flash();
      }
      if (GameFlow.Instance != null && GameFlow.Instance.celebrateFx != null)
      {
        GameFlow.Instance.celebrateFx.Play(_oven.Root.position + new Vector3(0f, 0.25f, 0f));
      }
      UpdateHudProgress();
      Debug.Log("[RehabDemo] 微波炉加热完成：“叮！”");
    }

    private void UpdateVisuals()
    {
      // 腔内灯：关门待机微光 0.12，开门照明 0.58，加热高亮 1.0，叮完成时呼吸闪烁
      float targetLight;
      var lightPulse = 0f;
      if (_isHeating)
      {
        targetLight = 1.0f;
        lightPulse = 0.15f * Mathf.Sin(Time.time * 12f);
      }
      else if (_heatedDone)
      {
        targetLight = 0.85f;
        lightPulse = 0.25f * Mathf.Sin(Time.time * 8f);
      }
      else
      {
        targetLight = Mathf.Lerp(0.12f, 0.65f, Mathf.Clamp01(_doorOpenRatio / 0.55f));
      }
      _oven.SetCavityLightLevel(targetLight, lightPulse);

      // 步骤提示横幅
      string banner;
      Color bannerColor;
      if (_hintOverrideTimer > 0f)
      {
        banner = _hintOverrideText;
        bannerColor = _LedAmber;
      }
      else if (_isHeating)
      {
        var sec = Mathf.CeilToInt(Mathf.Max(0f, _heatTimer));
        banner = $"加热中… 倒计时 {sec} 秒";
        bannerColor = _LedAmber;
      }
      else if (_heatedDone)
      {
        banner = "叮！加热完成！拉开炉门取出热杯子（或按 R 重置）";
        bannerColor = _LedGreen;
      }
      else if (_cupState != CupState.InMicrowave)
      {
        if (_cupState == CupState.Held && _pickedFromMicrowave && !_cavityMagnetArmed)
        {
          banner = "把杯子移出微波炉，松手即可放回桌面";
          bannerColor = _LedGreen;
        }
        else if (_doorOpenRatio < 0.35f)
        {
          banner = "① 捏住门把手向左缓缓拉开微波炉门";
          bannerColor = Color.white;
        }
        else if (_cupState == CupState.Held)
        {
          banner = _overTurntable ? "② 对准绿色转盘，松手即可放下杯子" : "② 把杯子移到微波炉空心腔内的转盘上";
          bannerColor = _overTurntable ? _LedGreen : _LedAmber;
        }
        else
        {
          banner = "② 捏住桌上的红色马克杯，放进微波炉转盘";
          bannerColor = Color.white;
        }
      }
      else if (_doorOpenRatio >= 0.12f)
      {
        banner = "③ 捏住把手或手推门板，向右缓缓关上炉门（也可捏杯取出）";
        bannerColor = Color.white;
      }
      else
      {
        banner = "④ 用食指指向右侧绿色“开始”按钮并点击";
        bannerColor = _LedGreen;
      }

      CurrentStepSummary = banner;
      _oven.UpdateBanner(banner, bannerColor, _camera);
      UpdateHudProgress();
    }

    private float ComputeStepProgress()
    {
      if (_heatedDone)
      {
        return 1f;
      }
      if (_isHeating)
      {
        return Mathf.Lerp(0.75f, 0.99f, 1f - Mathf.Clamp01(_heatTimer / Mathf.Max(0.1f, heatingDuration)));
      }
      if (_cupState == CupState.InMicrowave)
      {
        return Mathf.Lerp(0.75f, 0.50f, Mathf.Clamp01(_doorOpenRatio / 0.75f));
      }
      return Mathf.Lerp(0f, 0.25f, Mathf.Clamp01(_doorOpenRatio / 0.75f));
    }

    private void UpdateHudProgress()
    {
      if (IsRunning && GameFlow.Instance != null && GameFlow.Instance.hud != null)
      {
        GameFlow.Instance.hud.SetProgress(ComputeStepProgress());
      }
    }

    private void EnsureExerciseStartedOnInteract()
    {
      if (IsRunning || GameFlow.Instance == null || GameFlow.Instance.State != GameFlow.FlowState.Idle)
      {
        return;
      }
      var exercises = GameFlow.Instance.exercises;
      if (exercises == null)
      {
        return;
      }
      for (var i = 0; i < exercises.Length; i++)
      {
        if (exercises[i] == this)
        {
          GameFlow.Instance.StartExercise(i);
          break;
        }
      }
    }

    private static Vector3 RayAtHeight(Ray ray, float y)
    {
      if (Mathf.Abs(ray.direction.y) < 1e-4f)
      {
        return ray.origin + ray.direction * 0.8f;
      }
      var t = (y - ray.origin.y) / ray.direction.y;
      t = Mathf.Clamp(t, 0.1f, 2.5f);
      return ray.origin + ray.direction * t;
    }

    private static float DistanceRayToPoint(Ray ray, Vector3 point) =>
      Vector3.Cross(ray.direction, point - ray.origin).magnitude;

    private static float DistanceRayToSegment(Ray ray, Vector3 a, Vector3 b)
    {
      var ab = b - a;
      var best = float.MaxValue;
      for (var i = 0; i <= 4; i++)
      {
        var p = a + ab * (i / 4f);
        best = Mathf.Min(best, Vector3.Cross(ray.direction, p - ray.origin).magnitude);
      }
      return best;
    }
  }
}
