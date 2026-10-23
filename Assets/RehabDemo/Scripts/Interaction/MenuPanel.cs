using System;
using UnityEngine;

namespace RehabDemo
{
  /// <summary>
  ///   训练项目菜单：在 MENU 按钮的远端竖排三张卡片，打开和收起时做缩放动画。
  ///   本对象的位置应与 MENU 按钮重合。
  /// </summary>
  public class MenuPanel : MonoBehaviour
  {
    public string[] titles = { "捏合", "握拳", "屈指", "微波炉" };
    [Tooltip("卡片的宽、深（米）")]
    public Vector2 cardSize = new Vector2(0.17f, 0.068f);
    [Tooltip("第一张卡片中心到 MENU 按钮中心的距离（米）")]
    public float firstOffset = 0.078f;
    [Tooltip("相邻卡片中心的间距（米）")]
    public float step = 0.078f;
    [Tooltip("弹出、收起动画时长（秒）")]
    public float animTime = 0.2f;

    /// <summary>参数是卡片序号（0 起）。</summary>
    public event Action<int> CardSelected;

    public bool IsOpen { get; private set; }

    private DwellButton[] _cards;
    private float _amount;

    private void Awake()
    {
      if (titles == null || titles.Length == 3)
      {
        titles = new[] { "捏合", "握拳", "屈指", "微波炉" };
      }
      _cards = new DwellButton[titles.Length];
      for (var i = 0; i < titles.Length; i++)
      {
        // 先设为未激活，填好参数再激活，这样组件的 Awake 能读到参数
        var go = new GameObject($"Card{i}_{titles[i]}");
        go.SetActive(false);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, firstOffset + step * i);
        go.transform.localScale = Vector3.zero;

        var button = go.AddComponent<DwellButton>();
        button.size = cardSize;
        button.label = titles[i];
        button.fontSize = 30;
        button.fillColor = Color.white;
        button.textColor = new Color32(0x22, 0x2A, 0x35, 0xFF);
        button.interactable = false;

        var card = go.AddComponent<ExerciseCard>();
        card.index = i;
        card.icon = (ExerciseIcon)Mathf.Min(i, 3);

        var index = i;
        button.Triggered += _ => CardSelected?.Invoke(index);
        go.SetActive(true);
        _cards[i] = button;
      }
    }

    public void Open() => IsOpen = true;

    public void Close() => IsOpen = false;

    private void Update()
    {
      var target = IsOpen ? 1f : 0f;
      _amount = animTime > 0f ? Mathf.MoveTowards(_amount, target, Time.deltaTime / animTime) : target;
      var scale = Vector3.one * Mathf.SmoothStep(0f, 1f, _amount);
      foreach (var card in _cards)
      {
        card.transform.localScale = scale;
        // 完全弹出后才能点
        card.interactable = IsOpen && _amount >= 1f;
      }
    }
  }
}
