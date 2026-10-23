using UnityEngine;

namespace RehabDemo
{
  /// <summary>训练参数，集中放在一个资源里，方便以后按患者调整。</summary>
  [CreateAssetMenu(fileName = "ExerciseConfig", menuName = "RehabDemo/Exercise Config")]
  public class ExerciseConfig : ScriptableObject
  {
    [Header("次数")]
    [Tooltip("达标（绿色）要保持多少秒才计一次")]
    public float holdTime = 0.8f;
    [Tooltip("完成多少次算 100%")]
    public int targetReps = 8;

    [Header("颜色阈值（score 0~1）")]
    [Tooltip("达到这个值变绿")]
    public float greenEnter = 0.90f;
    [Tooltip("已经是绿色时，降到这个值以下才退回黄色（防止临界闪烁）")]
    public float greenExit = 0.80f;
    [Tooltip("低于这个值是红色")]
    public float yellowThreshold = 0.50f;
    [Tooltip("计过一次后，要放松到这个值以下才能计下一次")]
    public float relaxThreshold = 0.30f;
    [Tooltip("score 的平滑时间（秒），越大越稳但越迟钝")]
    public float scoreSmoothTime = 0.05f;

    [Header("捏合")]
    [Tooltip("score = 0 时的 pinchRatio（张开）")]
    public float pinchOpenRatio = 0.90f;
    [Tooltip("score = 1 时的 pinchRatio（捏紧）")]
    public float pinchClosedRatio = 0.25f;

    [Header("握拳")]
    [Tooltip("四指平均卷曲值达到多少算完全握拳")]
    public float fistFullCurl = 0.85f;

    [Header("屈指")]
    public float flexStartAngle = 10f;
    public float flexTargetAngle = 60f;
  }
}
