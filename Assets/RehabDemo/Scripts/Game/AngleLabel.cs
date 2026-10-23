using UnityEngine;
using UnityEngine.UI;

namespace RehabDemo
{
  /// <summary>训练时显示在手上方的实时角度（如 “45°”），始终朝向相机。</summary>
  public class AngleLabel : MonoBehaviour
  {
    [Tooltip("留空则使用 Main Camera")]
    public Camera viewCamera;
    [Tooltip("标签在手的最高点上方多高（米）")]
    public float heightAboveHand = 0.08f;
    [Tooltip("往远端偏多少（米），避免挡住手")]
    public float forwardOffset = 0.05f;
    public int fontSize = 44;

    private GameObject _visual;
    private Text _text;

    private void Awake()
    {
      // 世界空间 Canvas：1 个单位 = 1 毫米
      _visual = new GameObject("AngleCanvas", typeof(RectTransform));
      _visual.transform.SetParent(transform, false);
      var canvas = _visual.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.WorldSpace;
      _visual.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
      var root = (RectTransform)_visual.transform;
      root.sizeDelta = new Vector2(200f, 80f);
      root.localScale = Vector3.one * 0.001f;

      _text = new GameObject("Text").AddComponent<Text>();
      _text.transform.SetParent(root, false);
      _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
      _text.fontSize = fontSize;
      _text.fontStyle = FontStyle.Bold;
      _text.color = Color.white;
      _text.alignment = TextAnchor.MiddleCenter;
      _text.horizontalOverflow = HorizontalWrapMode.Overflow;
      _text.verticalOverflow = VerticalWrapMode.Overflow;
      _text.raycastTarget = false;
      var rect = _text.rectTransform;
      rect.anchorMin = Vector2.zero;
      rect.anchorMax = Vector2.one;
      rect.offsetMin = rect.offsetMax = Vector2.zero;

      var outline = _text.gameObject.AddComponent<Outline>();
      outline.effectColor = new Color(0.1f, 0.12f, 0.15f, 0.9f);
      outline.effectDistance = new Vector2(2.5f, -2.5f);

      _visual.SetActive(false);
    }

    // 放在 LateUpdate，保证拿到的是这一帧训练算出的角度
    private void LateUpdate()
    {
      if (_visual != null && _visual.activeSelf)
      {
        _visual.SetActive(false);
      }
    }
  }
}
