using System.Collections.Generic;
using UnityEngine;

namespace RehabDemo
{
  public enum HandColorState { Neutral, Red, Yellow, Green }

  /// <summary>控制虚拟手的颜色：青色（默认）、红（未达到）、黄（接近）、绿（达标），切换时平滑过渡。</summary>
  public class HandColorizer : MonoBehaviour
  {
    public static readonly Color NeutralColor = new Color32(0x5F, 0xD3, 0xD0, 0xFF);
    public static readonly Color RedColor = new Color32(0xE8, 0x44, 0x3A, 0xFF);
    public static readonly Color YellowColor = new Color32(0xF5, 0xC5, 0x18, 0xFF);
    public static readonly Color GreenColor = new Color32(0x57, 0xD1, 0x63, 0xFF);

    private static readonly int _EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Tooltip("颜色过渡时间（秒）")]
    public float transitionTime = 0.15f;
    [Tooltip("自发光强度，相对基色的倍数")]
    public float emission = 0.15f;
    [Tooltip("计次时白光闪一下的时长（秒）")]
    public float flashTime = 0.3f;

    public HandColorState State { get; private set; } = HandColorState.Neutral;

    private readonly List<Material> _materials = new List<Material>();
    private Color _from = NeutralColor;
    private Color _to = NeutralColor;
    private Color _current = NeutralColor;
    private float _t = 1f;
    private float _flash;

    /// <summary>由手的视图在创建材质后调用。胶囊手和蒙皮手各有一份材质，可以同时绑定。</summary>
    public void Bind(Material material)
    {
      if (material != null && !_materials.Contains(material))
      {
        _materials.Add(material);
      }
      Apply();
    }

    public void SetState(HandColorState state)
    {
      if (state == State)
      {
        return;
      }
      State = state;
      _from = _current;
      _to = ColorOf(state);
      _t = 0f;
    }

    /// <summary>闪一下白光，用于计次反馈。</summary>
    public void Flash() => _flash = 1f;

    public static Color ColorOf(HandColorState state)
    {
      switch (state)
      {
        case HandColorState.Red: return RedColor;
        case HandColorState.Yellow: return YellowColor;
        case HandColorState.Green: return GreenColor;
        default: return NeutralColor;
      }
    }

    private void Update()
    {
      if (_t >= 1f && _flash <= 0f)
      {
        return;
      }
      if (_t < 1f)
      {
        _t = transitionTime > 0f ? Mathf.Min(1f, _t + Time.deltaTime / transitionTime) : 1f;
        _current = Color.Lerp(_from, _to, _t);
      }
      if (_flash > 0f)
      {
        _flash = flashTime > 0f ? Mathf.Max(0f, _flash - Time.deltaTime / flashTime) : 0f;
      }
      Apply();
    }

    private void Apply()
    {
      var color = Color.Lerp(_current, Color.white, _flash * 0.85f);
      foreach (var material in _materials)
      {
        if (material == null)
        {
          continue;
        }
        material.color = color;
        material.SetColor(_EmissionColorId, color * (emission + _flash * 0.6f));
      }
    }
  }
}
