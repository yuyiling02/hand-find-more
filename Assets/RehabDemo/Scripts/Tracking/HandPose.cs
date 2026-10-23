using UnityEngine;

namespace RehabDemo
{
  public enum HandFilter { Any, LeftOnly, RightOnly }

  /// <summary>平滑后的手部姿态，由 <see cref="HandFrameBus" /> 每帧更新。</summary>
  public struct HandPose
  {
    public bool tracked;
    public bool isLeft;
    /// <summary>21 个关节的世界坐标。数组由 HandFrameBus 持有并原地更新，需要保留历史请自行拷贝。</summary>
    public Vector3[] joints;
  }

  /// <summary>MediaPipe 的一帧原始结果（已从回调线程深拷贝出来）。</summary>
  public class RawHandFrame
  {
    public bool hasHand;
    public bool isLeft;
    public float handednessScore;
    public long timestampMs;
    /// <summary>图像归一化坐标，x、y∈[0,1]，y 轴朝下。</summary>
    public readonly Vector3[] normalized = new Vector3[LandmarkIds.Count];
    /// <summary>以米为单位，原点在手的几何中心，y 轴朝下。</summary>
    public readonly Vector3[] world = new Vector3[LandmarkIds.Count];

    public void CopyFrom(RawHandFrame src)
    {
      hasHand = src.hasHand;
      isLeft = src.isLeft;
      handednessScore = src.handednessScore;
      timestampMs = src.timestampMs;
      System.Array.Copy(src.normalized, normalized, LandmarkIds.Count);
      System.Array.Copy(src.world, world, LandmarkIds.Count);
    }
  }
}
