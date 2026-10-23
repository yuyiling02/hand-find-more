using System;
using UnityEngine;
using UnityEngine.UI;

namespace RehabDemo
{
  /// <summary>
  ///   平放在桌面上的按钮。食指指着它时边框高亮，做一次点击动作（或停留一段时间，见 <see cref="triggerMode" />）后触发，
  ///   按下时按钮凹陷作为反馈。
  ///   外观在运行时由代码生成。按钮平面是自身的 XZ 平面，<see cref="size" /> 为 (宽 x, 深 z)。
  /// </summary>
  public class DwellButton : MonoBehaviour
  {
    public enum TriggerMode { Click, Dwell }

    [Header("外观")]
    [Tooltip("宽、深（米）")]
    public Vector2 size = new Vector2(0.12f, 0.06f);
    public string label = "MENU";
    public int fontSize = 26;
    public Color fillColor = new Color32(0x2F, 0x7B, 0xE6, 0xFF);
    public Color textColor = Color.white;
    public Color highlightColor = new Color32(0xFF, 0xC8, 0x2E, 0xFF);

    [Header("交互")]
    [Tooltip("Click：食指指着按钮并做一次点击动作才触发；Dwell：指尖停留一段时间就触发")]
    public TriggerMode triggerMode = TriggerMode.Click;
    [Tooltip("Dwell 模式下指尖要停留多久才触发（秒）")]
    public float dwellTime = 0.35f;
    [Tooltip("触发后的冷却时间（秒）")]
    public float cooldown = 0.5f;
    [Tooltip("判定范围比外观多出的余量（米）")]
    public float hitMargin = 0.006f;
    public bool interactable = true;

    public event Action<DwellButton> Triggered;

    /// <summary>按钮内容的容器，单位是毫米，原点在按钮中心。可以往里加图标。</summary>
    public RectTransform Content { get; private set; }
    public Text LabelText { get; private set; }

    private Image _border;
    private float _timer;
    private float _cooldownTimer;
    private bool _armed = true;
    private float _highlight;
    private float _press;

    private void Awake()
    {
      // 世界空间 Canvas：1 个单位 = 1 毫米，朝上平放
      var visual = new GameObject("Visual", typeof(RectTransform));
      visual.transform.SetParent(transform, false);
      var canvas = visual.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.WorldSpace;
      visual.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
      var root = (RectTransform)visual.transform;
      root.sizeDelta = size * 1000f;
      root.localPosition = Vector3.zero;
      root.localRotation = Quaternion.Euler(90f, 0f, 0f);
      root.localScale = Vector3.one * 0.001f;

      _border = CreateImage("Border", root, highlightColor);
      _border.rectTransform.offsetMin = new Vector2(-6f, -6f);
      _border.rectTransform.offsetMax = new Vector2(6f, 6f);
      SetAlpha(_border, 0f);

      Content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
      Content.SetParent(root, false);
      Stretch(Content);

      CreateImage("Background", Content, fillColor);

      LabelText = new GameObject("Label").AddComponent<Text>();
      LabelText.transform.SetParent(Content, false);
      LabelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
      LabelText.text = label;
      LabelText.fontSize = fontSize;
      LabelText.fontStyle = FontStyle.Bold;
      LabelText.color = textColor;
      LabelText.alignment = TextAnchor.MiddleCenter;
      LabelText.horizontalOverflow = HorizontalWrapMode.Overflow;
      LabelText.verticalOverflow = VerticalWrapMode.Overflow;
      LabelText.raycastTarget = false;
      Stretch(LabelText.rectTransform);
    }

    private static Image CreateImage(string name, RectTransform parent, Color color)
    {
      var image = new GameObject(name).AddComponent<Image>();
      image.transform.SetParent(parent, false);
      image.sprite = ProcTextures.Rounded;
      image.type = Image.Type.Sliced;
      // 默认圆角半径约 24 毫米，对按钮来说太圆了，减半
      image.pixelsPerUnitMultiplier = 2f;
      image.color = color;
      image.raycastTarget = false;
      Stretch(image.rectTransform);
      return image;
    }

    private static void Stretch(RectTransform rect)
    {
      rect.anchorMin = Vector2.zero;
      rect.anchorMax = Vector2.one;
      rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void SetAlpha(Graphic graphic, float alpha)
    {
      var color = graphic.color;
      color.a = alpha;
      graphic.color = color;
    }

    private void Update()
    {
      _cooldownTimer -= Time.deltaTime;

      var pointed = interactable && IsPointed();
      float pressTarget;
      if (triggerMode == TriggerMode.Click)
      {
        _armed = true;
        if (pointed && _cooldownTimer <= 0f && FingertipPointer.ClickedThisFrame)
        {
          _cooldownTimer = cooldown;
          Triggered?.Invoke(this);
        }
        pressTarget = pointed && FingertipPointer.IsPressing ? 1f : 0f;
      }
      else
      {
        if (!pointed)
        {
          // 指尖离开后才能再次触发，避免手停在按钮上反复开关
          _timer = 0f;
          _armed = true;
        }
        else if (_armed && _cooldownTimer <= 0f)
        {
          _timer += Time.deltaTime;
          if (_timer >= dwellTime)
          {
            _timer = 0f;
            _armed = false;
            _cooldownTimer = cooldown;
            Triggered?.Invoke(this);
          }
        }
        pressTarget = dwellTime > 0f ? Mathf.Clamp01(_timer / dwellTime) : 0f;
      }

      _press = triggerMode == TriggerMode.Click ? Mathf.MoveTowards(_press, pressTarget, Time.deltaTime / 0.08f) : pressTarget;
      Content.localScale = Vector3.one * Mathf.Lerp(1f, 0.88f, _press);
      var targetHighlight = pointed && _armed && _cooldownTimer <= 0f ? 1f : 0f;
      _highlight = Mathf.MoveTowards(_highlight, targetHighlight, Time.deltaTime / 0.1f);
      SetAlpha(_border, _highlight);
    }

    private bool IsPointed()
    {
      if (!FingertipPointer.TryGetRay(out var ray))
      {
        return false;
      }
      var plane = new Plane(transform.up, transform.position);
      if (!plane.Raycast(ray, out var distance))
      {
        return false;
      }
      var offset = ray.GetPoint(distance) - transform.position;
      return Mathf.Abs(Vector3.Dot(offset, transform.right)) <= size.x * 0.5f + hitMargin &&
             Mathf.Abs(Vector3.Dot(offset, transform.forward)) <= size.y * 0.5f + hitMargin;
    }

    private void OnDisable()
    {
      _timer = 0f;
    }
  }
}
