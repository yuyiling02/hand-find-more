using UnityEngine;
using UnityEngine.UI;

namespace RehabDemo
{
  /// <summary>一段时间没检测到手时，在屏幕顶部提示用户把手放到摄像头前。</summary>
  public class HandLostHint : MonoBehaviour
  {
    public string message = "请把手放到摄像头前，保持光线充足";
    [Tooltip("手丢失多久后才显示提示（秒）")]
    public float delay = 1.5f;

    private GameObject _canvas;
    private float _lostTimer;

    private void Awake()
    {
      _canvas = new GameObject("HintCanvas");
      _canvas.transform.SetParent(transform, false);
      var canvas = _canvas.AddComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      var scaler = _canvas.AddComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      scaler.referenceResolution = new Vector2(1920f, 1080f);
      scaler.matchWidthOrHeight = 1f;

      var text = new GameObject("Text").AddComponent<Text>();
      text.transform.SetParent(_canvas.transform, false);
      text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
      text.text = message;
      text.fontSize = 42;
      text.fontStyle = FontStyle.Bold;
      text.color = Color.white;
      text.alignment = TextAnchor.MiddleCenter;
      text.horizontalOverflow = HorizontalWrapMode.Overflow;
      text.verticalOverflow = VerticalWrapMode.Overflow;
      text.raycastTarget = false;
      var rect = text.rectTransform;
      rect.anchorMin = new Vector2(0f, 1f);
      rect.anchorMax = new Vector2(1f, 1f);
      rect.pivot = new Vector2(0.5f, 1f);
      rect.anchoredPosition = new Vector2(0f, -60f);
      rect.sizeDelta = new Vector2(0f, 80f);

      var outline = text.gameObject.AddComponent<Outline>();
      outline.effectColor = new Color(0.1f, 0.12f, 0.15f, 0.9f);
      outline.effectDistance = new Vector2(2.5f, -2.5f);

      _canvas.SetActive(false);
    }

    private void Update()
    {
      _lostTimer = HandFrameBus.Current.tracked ? 0f : _lostTimer + Time.unscaledDeltaTime;
      var visible = _lostTimer >= delay;
      if (_canvas.activeSelf != visible)
      {
        _canvas.SetActive(visible);
      }
    }
  }
}
