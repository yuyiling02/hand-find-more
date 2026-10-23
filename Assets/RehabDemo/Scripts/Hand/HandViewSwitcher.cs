using UnityEngine;

namespace RehabDemo
{
  public enum HandViewMode { Capsule, Skinned }

  /// <summary>在胶囊手和带蒙皮的手之间切换。可以在 Inspector 里改 Mode，运行时也可以按 H 键切换。</summary>
  public class HandViewSwitcher : MonoBehaviour
  {
    public HandViewMode mode = HandViewMode.Capsule;
    public GameObject capsuleHand;
    public GameObject skinnedHand;

    private void OnEnable() => Apply();

    private void Update() => Apply();

    private void Apply()
    {
      // 没有蒙皮手模型时只能用胶囊手
      var useSkinned = mode == HandViewMode.Skinned && skinnedHand != null;
      if (capsuleHand != null && capsuleHand.activeSelf == useSkinned)
      {
        capsuleHand.SetActive(!useSkinned);
      }
      if (skinnedHand != null && skinnedHand.activeSelf != useSkinned)
      {
        skinnedHand.SetActive(useSkinned);
      }
    }

    private void OnGUI()
    {
      var e = Event.current;
      if (e.type == EventType.KeyDown && e.keyCode == KeyCode.H)
      {
        mode = mode == HandViewMode.Capsule ? HandViewMode.Skinned : HandViewMode.Capsule;
        e.Use();
      }
    }
  }
}
