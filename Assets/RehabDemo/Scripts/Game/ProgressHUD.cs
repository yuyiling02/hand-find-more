using UnityEngine;
using UnityEngine.UI;

namespace RehabDemo
{
  /// <summary>左上角的绿色圆角标签 “Progress xx%”。界面在运行时由代码生成，不需要在场景里搭 Canvas。</summary>
  public class ProgressHUD : MonoBehaviour
  {
    public Color badgeColor = new Color32(0x9B, 0xE2, 0x2D, 0xFF);
    [Tooltip("标签的倾斜角度")]
    public float tiltDegrees = -8f;
    [Tooltip("数字变化时放大回弹的时长（秒）")]
    public float bounceTime = 0.15f;

    private GameObject _canvas;
    private Text _percentText;
    private Texture2D _badgeTexture;
    private int _shownPercent = -1;
    private float _bounce = 1f;

    private void Awake()
    {
      _canvas = new GameObject("ProgressCanvas");
      _canvas.transform.SetParent(transform, false);
      var canvas = _canvas.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      var scaler = _canvas.AddComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      scaler.referenceResolution = new Vector2(1920f, 1080f);
      scaler.matchWidthOrHeight = 1f;

      var badge = new GameObject("Badge").AddComponent<Image>();
      badge.transform.SetParent(_canvas.transform, false);
      badge.sprite = CreateRoundedSprite(64, 22);
      badge.type = Image.Type.Sliced;
      badge.color = badgeColor;
      badge.raycastTarget = false;
      var rect = badge.rectTransform;
      rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
      rect.anchoredPosition = new Vector2(50f, -50f);
      rect.sizeDelta = new Vector2(330f, 170f);
      rect.localEulerAngles = new Vector3(0f, 0f, tiltDegrees);

      var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
      CreateText("Title", rect, font, "Progress", 46, new Vector2(0f, 0.5f), new Vector2(1f, 1f));
      _percentText = CreateText("Percent", rect, font, "0%", 68, new Vector2(0f, 0f), new Vector2(1f, 0.58f));

      _canvas.SetActive(false);
    }

    private static Text CreateText(string name, RectTransform parent, Font font, string content, int size, Vector2 anchorMin, Vector2 anchorMax)
    {
      var text = new GameObject(name).AddComponent<Text>();
      text.transform.SetParent(parent, false);
      text.font = font;
      text.text = content;
      text.fontSize = size;
      text.fontStyle = FontStyle.Bold;
      text.color = Color.black;
      text.alignment = TextAnchor.MiddleCenter;
      text.horizontalOverflow = HorizontalWrapMode.Overflow;
      text.verticalOverflow = VerticalWrapMode.Overflow;
      text.raycastTarget = false;
      var rect = text.rectTransform;
      rect.anchorMin = anchorMin;
      rect.anchorMax = anchorMax;
      rect.offsetMin = rect.offsetMax = Vector2.zero;
      return text;
    }

    /// <summary>程序生成一张白色圆角矩形，九宫格拉伸后四个角不变形。</summary>
    private Sprite CreateRoundedSprite(int size, int radius)
    {
      _badgeTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
      var pixels = new Color32[size * size];
      for (var y = 0; y < size; y++)
      {
        for (var x = 0; x < size; x++)
        {
          // 到圆角矩形边缘的距离，用来做 1 像素的抗锯齿
          var dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius), 0f);
          var dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius), 0f);
          var alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
          pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
        }
      }
      _badgeTexture.SetPixels32(pixels);
      _badgeTexture.Apply();
      var border = radius + 2;
      return Sprite.Create(_badgeTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }

    public void Show(float progress)
    {
      _canvas.SetActive(true);
      _shownPercent = -1;
      SetProgress(progress);
      _bounce = 1f;
      _percentText.transform.localScale = Vector3.one;
    }

    public void Hide() => _canvas.SetActive(false);

    public void SetProgress(float progress)
    {
      var percent = Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f);
      if (percent == _shownPercent)
      {
        return;
      }
      _shownPercent = percent;
      _percentText.text = percent + "%";
      _bounce = 0f;
    }

    private void Update()
    {
      if (_bounce >= 1f)
      {
        return;
      }
      _bounce = bounceTime > 0f ? Mathf.Min(1f, _bounce + Time.deltaTime / bounceTime) : 1f;
      // 0 → 1 的过程中先放大再回到原大小
      _percentText.transform.localScale = Vector3.one * (1f + 0.3f * Mathf.Sin(_bounce * Mathf.PI));
    }

    private void OnDestroy()
    {
      Destroy(_badgeTexture);
    }
  }
}
