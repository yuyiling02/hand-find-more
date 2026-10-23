using UnityEngine;
using UnityEngine.UI;

namespace RehabDemo
{
  public enum ExerciseIcon { Pinch, Fist, Flex }

  /// <summary>菜单里的训练项目卡片：在 <see cref="DwellButton" /> 上加一个红蓝图标，文字靠右。</summary>
  [RequireComponent(typeof(DwellButton))]
  public class ExerciseCard : MonoBehaviour
  {
    private static readonly Color _Red = new Color32(0xE8, 0x44, 0x3A, 0xFF);
    private static readonly Color _Blue = new Color32(0x2F, 0x7B, 0xE6, 0xFF);

    public int index;
    public ExerciseIcon icon;

    public DwellButton Button { get; private set; }

    private void Awake()
    {
      Button = GetComponent<DwellButton>();
    }

    // DwellButton 在 Awake 里生成外观，所以这里放在 Start
    private void Start()
    {
      var content = Button.Content;
      var width = content.rect.width;

      // 文字占右边 55%，图标放在左边
      Button.LabelText.rectTransform.anchorMin = new Vector2(0.45f, 0f);
      var center = new Vector2(-width * 0.26f, 0f);

      switch (icon)
      {
        case ExerciseIcon.Pinch:
          // 两个圆点靠拢
          AddShape(content, ProcTextures.Circle, _Red, center + new Vector2(-11f, 0f), new Vector2(22f, 22f), 0f);
          AddShape(content, ProcTextures.Circle, _Blue, center + new Vector2(11f, 0f), new Vector2(22f, 22f), 0f);
          break;
        case ExerciseIcon.Fist:
          // 实心大圆（拳头）加一个小圆（拇指）
          AddShape(content, ProcTextures.Circle, _Red, center, new Vector2(38f, 38f), 0f);
          AddShape(content, ProcTextures.Circle, _Blue, center + new Vector2(14f, -12f), new Vector2(16f, 16f), 0f);
          break;
        case ExerciseIcon.Flex:
          // 折线：一段水平、一段抬起 60°，关节处一个红点
          var joint = center + new Vector2(4f, -10f);
          var direction = new Vector2(Mathf.Cos(60f * Mathf.Deg2Rad), Mathf.Sin(60f * Mathf.Deg2Rad));
          AddShape(content, ProcTextures.Rounded, _Blue, joint + new Vector2(-14f, 0f), new Vector2(30f, 7f), 0f);
          AddShape(content, ProcTextures.Rounded, _Blue, joint + direction * 14f, new Vector2(30f, 7f), 60f);
          AddShape(content, ProcTextures.Circle, _Red, joint, new Vector2(13f, 13f), 0f);
          break;
      }
    }

    private static void AddShape(RectTransform parent, Sprite sprite, Color color, Vector2 position, Vector2 size, float rotation)
    {
      var image = new GameObject("Icon").AddComponent<Image>();
      image.transform.SetParent(parent, false);
      image.sprite = sprite;
      image.color = color;
      image.raycastTarget = false;
      var rect = image.rectTransform;
      rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
      rect.anchoredPosition = position;
      rect.sizeDelta = size;
      rect.localEulerAngles = new Vector3(0f, 0f, rotation);
    }
  }
}
