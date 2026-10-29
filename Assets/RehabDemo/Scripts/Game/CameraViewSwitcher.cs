using System;
using UnityEngine;

namespace RehabDemo
{
  /// <summary>
  ///   多视角切换：正面 / 俯视 / 左侧 / 右侧 / 微波炉特写。
  ///   屏幕左侧有一列视角按钮，虚拟手食指指着按钮后“点一下”（或停留约 1 秒）即可切换，也支持鼠标点击和 V 键循环。
  ///   切换视角后，虚拟手的活动区域会按屏幕重新映射（见 <see cref="Remap" />），
  ///   保证在任何视角下“真手往右 = 屏幕上往右”，并且手能覆盖到的屏幕范围和默认视角一致。
  /// </summary>
  [DefaultExecutionOrder(-200)]
  public class CameraViewSwitcher : MonoBehaviour
  {
    [Serializable]
    public class ViewPreset
    {
      public string name;
      public Vector3 position;
      public Vector3 lookAt;
      public float fieldOfView = 45f;
    }

    [Tooltip("留空则使用 Main Camera")]
    public Camera viewCamera;
    [Tooltip("视角切换动画时长（秒）")]
    public float transitionTime = 0.6f;
    [Tooltip("食指停留在按钮上多久自动切换（秒），0 = 只认点击手势")]
    public float dwellTime = 1.0f;
    [Tooltip("第 0 个视角为默认正面视角（正对桌面中央的微波炉）")]
    public ViewPreset[] presets =
    {
      new ViewPreset { name = "正面", position = new Vector3(0f, 0.42f, -0.55f), lookAt = new Vector3(0f, 0.08f, 0.08f), fieldOfView = 45f },
      new ViewPreset { name = "俯视", position = new Vector3(0f, 0.95f, -0.38f), lookAt = new Vector3(0f, 0.02f, 0.02f), fieldOfView = 45f },
      new ViewPreset { name = "左侧", position = new Vector3(-0.42f, 0.52f, -0.46f), lookAt = new Vector3(0f, 0.06f, 0.06f), fieldOfView = 45f },
      new ViewPreset { name = "右侧", position = new Vector3(0.42f, 0.52f, -0.46f), lookAt = new Vector3(0f, 0.06f, 0.06f), fieldOfView = 45f },
      new ViewPreset { name = "特写", position = new Vector3(0f, 0.34f, -0.36f), lookAt = new Vector3(0f, 0.09f, 0.08f), fieldOfView = 42f },
    };

    public static CameraViewSwitcher Instance { get; private set; }

    /// <summary>相机正在两个视角之间过渡。</summary>
    public static bool IsTransitioning => Instance != null && Instance._transitionT < 1f;

    public int CurrentIndex { get; private set; }

    // 默认视角（手部映射的基准）
    private Vector3 _defaultPos;
    private Quaternion _defaultRot = Quaternion.identity;
    private float _defaultFov = 45f;
    private bool _hasDefault;

    // 过渡动画
    private Vector3 _fromPos;
    private Quaternion _fromRot;
    private float _fromFov;
    private float _transitionT = 1f;

    // 按钮交互
    private int _hoverIndex = -1;
    private float _dwellTimer;
    private bool _dwellArmed = true;
    private bool _hasFingerScreen;
    private Vector2 _fingerScreen; // GUI 坐标（左上为原点）
    private GUIStyle _buttonStyle;
    private GUIStyle _titleStyle;

    private void Awake()
    {
      Instance = this;
      CaptureDefault();
    }

    private void OnDestroy()
    {
      if (Instance == this)
      {
        Instance = null;
      }
    }

    private void CaptureDefault()
    {
      if (viewCamera == null)
      {
        viewCamera = Camera.main;
      }
      if (viewCamera == null || _hasDefault)
      {
        return;
      }
      var t = viewCamera.transform;
      if (presets != null && presets.Length > 0 && presets[0].position.sqrMagnitude > 1e-4f)
      {
        GetPresetPose(0, out var pos, out var rot, out var fov);
        t.position = pos;
        t.rotation = rot;
        viewCamera.fieldOfView = fov;
      }
      _defaultPos = t.position;
      _defaultRot = t.rotation;
      _defaultFov = viewCamera.fieldOfView;
      _hasDefault = true;
      if (presets != null && presets.Length > 0 && presets[0].position.sqrMagnitude <= 1e-4f)
      {
        presets[0].position = _defaultPos;
        presets[0].lookAt = _defaultPos + t.forward;
        presets[0].fieldOfView = _defaultFov;
      }
    }

    /// <summary>切换到第 index 个视角（带过渡动画）。</summary>
    public void SwitchTo(int index)
    {
      CaptureDefault();
      if (viewCamera == null || presets == null || presets.Length == 0)
      {
        return;
      }
      index = (index % presets.Length + presets.Length) % presets.Length;
      if (index == CurrentIndex && _transitionT >= 1f)
      {
        return;
      }
      var t = viewCamera.transform;
      _fromPos = t.position;
      _fromRot = t.rotation;
      _fromFov = viewCamera.fieldOfView;
      CurrentIndex = index;
      _transitionT = transitionTime > 0f ? 0f : 1f;
      if (_transitionT >= 1f)
      {
        ApplyPose(1f);
      }

      var sfx = GameFlow.Instance != null ? GameFlow.Instance.sfx : null;
      if (sfx != null)
      {
        sfx.PlayClick();
      }
    }

    private void GetPresetPose(int index, out Vector3 pos, out Quaternion rot, out float fov)
    {
      if (index == 0 && (presets == null || presets.Length == 0 || presets[0].position.sqrMagnitude <= 1e-4f))
      {
        pos = _defaultPos;
        rot = _defaultRot;
        fov = _defaultFov;
        return;
      }
      var p = presets[index];
      pos = p.position;
      var dir = p.lookAt - p.position;
      rot = dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir, Vector3.up) : Quaternion.identity;
      fov = p.fieldOfView;
    }

    private void ApplyPose(float t)
    {
      GetPresetPose(CurrentIndex, out var pos, out var rot, out var fov);
      var s = Mathf.SmoothStep(0f, 1f, t);
      var camT = viewCamera.transform;
      camT.position = Vector3.Lerp(_fromPos, pos, s);
      camT.rotation = Quaternion.Slerp(_fromRot, rot, s);
      viewCamera.fieldOfView = Mathf.Lerp(_fromFov, fov, s);
    }

    private void Update()
    {
      CaptureDefault();
      if (viewCamera == null)
      {
        return;
      }

      if (_transitionT < 1f)
      {
        _transitionT = Mathf.Min(1f, _transitionT + Time.deltaTime / Mathf.Max(0.01f, transitionTime));
        ApplyPose(_transitionT);
      }

      if (Input.GetKeyDown(KeyCode.V))
      {
        SwitchTo(CurrentIndex + 1);
      }

      UpdateHandInput();
    }

    // ---------------- 屏幕按钮 ----------------

    private Rect GetButtonRect(int i, out float scale)
    {
      // 按钮放在屏幕左侧中上部：这个区域在任何视角下虚拟手的食指都够得到（映射是按屏幕做的）
      float h = UnityEngine.Screen.height;
      scale = Mathf.Clamp(h / 1080f, 0.45f, 1.5f);
      var width = 120f * scale;
      var height = 54f * scale;
      var gap = 10f * scale;
      var x = Mathf.Max(8f, UnityEngine.Screen.width * 0.015f);
      var top = h * 0.28f;
      return new Rect(x, top + i * (height + gap), width, height);
    }

    private void UpdateHandInput()
    {
      _hasFingerScreen = false;
      var newHover = -1;
      if (_transitionT >= 1f && presets != null && FingertipPointer.TryGetRay(out var ray))
      {
        var screen = viewCamera.WorldToScreenPoint(ray.origin + ray.direction.normalized * 0.5f);
        if (screen.z > 0f)
        {
          _fingerScreen = new Vector2(screen.x, UnityEngine.Screen.height - screen.y);
          _hasFingerScreen = true;
          for (var i = 0; i < presets.Length; i++)
          {
            var rect = GetButtonRect(i, out var scale);
            var margin = 8f * scale;
            var hit = new Rect(rect.x - margin, rect.y - margin * 0.5f, rect.width + margin * 2f, rect.height + margin);
            if (hit.Contains(_fingerScreen))
            {
              newHover = i;
              break;
            }
          }
        }
      }

      if (newHover != _hoverIndex)
      {
        _hoverIndex = newHover;
        _dwellTimer = 0f;
        _dwellArmed = true;
      }
      if (_hoverIndex < 0)
      {
        return;
      }

      if (FingertipPointer.ClickedThisFrame)
      {
        _dwellArmed = false;
        _dwellTimer = 0f;
        SwitchTo(_hoverIndex);
        return;
      }
      if (dwellTime > 0f && _dwellArmed && _hoverIndex != CurrentIndex)
      {
        _dwellTimer += Time.deltaTime;
        if (_dwellTimer >= dwellTime)
        {
          // 指尖离开按钮后才能再次停留触发
          _dwellArmed = false;
          _dwellTimer = 0f;
          SwitchTo(_hoverIndex);
        }
      }
    }

    private void OnGUI()
    {
      if (presets == null || presets.Length == 0)
      {
        return;
      }
      var first = GetButtonRect(0, out var scale);
      _buttonStyle ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
      _titleStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerLeft };
      _buttonStyle.fontSize = Mathf.RoundToInt(20f * scale);
      _titleStyle.fontSize = Mathf.RoundToInt(16f * scale);
      _titleStyle.normal.textColor = new Color(0.25f, 0.22f, 0.2f);

      GUI.Label(new Rect(first.x + 2f, first.y - 26f * scale, first.width * 2f, 24f * scale), "视角（V）", _titleStyle);

      var previousColor = GUI.backgroundColor;
      var previousContent = GUI.contentColor;
      for (var i = 0; i < presets.Length; i++)
      {
        var rect = GetButtonRect(i, out _);
        var selected = i == CurrentIndex;
        var hovered = i == _hoverIndex;

        if (hovered)
        {
          // 高亮描边
          var pad = 4f * scale;
          DrawRect(new Rect(rect.x - pad, rect.y - pad, rect.width + pad * 2f, rect.height + pad * 2f), new Color(1f, 0.78f, 0.18f, 0.95f));
        }
        GUI.backgroundColor = selected ? new Color(0.18f, 0.48f, 0.90f) : new Color(0.85f, 0.85f, 0.85f);
        GUI.contentColor = Color.white;
        if (GUI.Button(rect, presets[i].name, _buttonStyle))
        {
          SwitchTo(i);
        }

        // 停留进度条
        if (hovered && dwellTime > 0f && _dwellArmed && i != CurrentIndex && _dwellTimer > 0f)
        {
          var progress = Mathf.Clamp01(_dwellTimer / dwellTime);
          DrawRect(new Rect(rect.x + 3f, rect.yMax - 6f * scale, (rect.width - 6f) * progress, 4f * scale), new Color(1f, 0.78f, 0.18f));
        }
      }
      GUI.backgroundColor = previousColor;
      GUI.contentColor = previousContent;

      // 食指靠近按钮区域时画一个小光标，方便对准
      if (_hasFingerScreen && _fingerScreen.x < first.xMax + 120f * scale)
      {
        var r = 7f * scale + 3f;
        DrawRect(new Rect(_fingerScreen.x - r, _fingerScreen.y - r, r * 2f, r * 2f), new Color(1f, 1f, 1f, 0.85f));
        var inner = r * 0.55f;
        DrawRect(new Rect(_fingerScreen.x - inner, _fingerScreen.y - inner, inner * 2f, inner * 2f), new Color(0.95f, 0.3f, 0.3f, 0.95f));
      }
    }

    private static void DrawRect(Rect rect, Color color)
    {
      var previous = GUI.color;
      GUI.color = color;
      GUI.DrawTexture(rect, Texture2D.whiteTexture);
      GUI.color = previous;
    }

    // ---------------- 手部映射 ----------------

    /// <summary>
    ///   把“默认视角下”的手掌世界坐标换算到当前视角：先求它在默认相机画面中的位置，
    ///   再从当前相机穿过同一个画面位置打射线，落到同样的悬停高度上。
    ///   同时返回手形需要绕 Y 轴补偿的旋转，让手的朝向跟着视角转。默认视角下完全不变。
    /// </summary>
    public static void Remap(Vector3 basePosition, out Vector3 position, out Quaternion handRotation)
    {
      position = basePosition;
      handRotation = Quaternion.identity;
      var self = Instance;
      if (self == null || !self.isActiveAndEnabled || !self._hasDefault || self.viewCamera == null)
      {
        return;
      }
      if (self.CurrentIndex == 0 && self._transitionT >= 1f)
      {
        return;
      }

      var cam = self.viewCamera;
      var tanV = Mathf.Tan(self._defaultFov * 0.5f * Mathf.Deg2Rad);
      var tanH = tanV * cam.aspect;
      var local = Quaternion.Inverse(self._defaultRot) * (basePosition - self._defaultPos);
      var depth = Mathf.Max(local.z, 0.02f);
      var viewport = new Vector3(0.5f + local.x / (depth * tanH) * 0.5f, 0.5f + local.y / (depth * tanV) * 0.5f, 0f);

      var ray = cam.ViewportPointToRay(viewport);
      if (ray.direction.y < -0.02f)
      {
        var distance = Mathf.Min((basePosition.y - ray.origin.y) / ray.direction.y, 4f);
        position = ray.GetPoint(distance);
        position.y = basePosition.y;
      }

      var yaw = Mathf.DeltaAngle(self._defaultRot.eulerAngles.y, cam.transform.eulerAngles.y);
      handRotation = Quaternion.Euler(0f, yaw, 0f);
    }
  }
}
