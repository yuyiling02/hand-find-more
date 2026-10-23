using UnityEngine;

namespace RehabDemo
{
  /// <summary>
  ///   动作判定指标。全部用比值和角度，与手离摄像头的远近无关。传入的是平滑后的 21 个关节坐标。
  /// </summary>
  public static class HandMetrics
  {
    /// <summary>手腕到中指根部的距离，作为归一化基准。</summary>
    public static float PalmSize(Vector3[] j) => Vector3.Distance(j[LandmarkIds.Wrist], j[LandmarkIds.MiddleMcp]);

    /// <summary>拇指尖到食指尖的距离 / 手掌大小。张开约 0.9 以上，捏紧约 0.15。</summary>
    public static float PinchRatio(Vector3[] j)
    {
      var palm = PalmSize(j);
      return palm > 1e-5f ? Vector3.Distance(j[LandmarkIds.ThumbTip], j[LandmarkIds.IndexTip]) / palm : 1f;
    }

    /// <summary>关节 b 处的屈曲角，0° = 伸直。</summary>
    public static float Flex(Vector3[] j, int a, int b, int c) => Vector3.Angle(j[b] - j[a], j[c] - j[b]);

    /// <summary>食指到小指（finger = 0~3）的卷曲程度，约 0~1。</summary>
    public static float FingerCurl(Vector3[] j, int finger)
    {
      var mcp = LandmarkIds.IndexMcp + finger * 4;
      return (Flex(j, LandmarkIds.Wrist, mcp, mcp + 1) + Flex(j, mcp, mcp + 1, mcp + 2)) / 180f;
    }

    /// <summary>握拳程度 0~1。<paramref name="fullCurl" /> 是算作完全握拳的平均卷曲值。</summary>
    public static float Fistness(Vector3[] j, float fullCurl = 0.85f)
    {
      var sum = 0f;
      for (var f = 0; f < 4; f++)
      {
        sum += FingerCurl(j, f);
      }
      return Mathf.Clamp01(sum / 4f / fullCurl);
    }

    /// <summary>拇指与食指张开的角度。</summary>
    public static float PinchAngle(Vector3[] j) =>
      Vector3.Angle(j[LandmarkIds.ThumbTip] - j[LandmarkIds.ThumbMcp], j[LandmarkIds.IndexTip] - j[LandmarkIds.IndexMcp]);

    /// <summary>四指 MCP、PIP 屈曲角的平均值。</summary>
    public static float AverageFingerFlex(Vector3[] j)
    {
      var sum = 0f;
      for (var f = 0; f < 4; f++)
      {
        var mcp = LandmarkIds.IndexMcp + f * 4;
        sum += Flex(j, LandmarkIds.Wrist, mcp, mcp + 1) + Flex(j, mcp, mcp + 1, mcp + 2);
      }
      return sum / 8f;
    }

    /// <summary>
    ///   食指整体相对手掌压下去的角度：指根到指尖的连线与手掌方向的夹角，0° = 与手掌成一直线。
    ///   只动指根关节轻轻点一下也能反映出来，用于点击手势。
    /// </summary>
    public static float IndexTapAngle(Vector3[] j) =>
      Vector3.Angle(j[LandmarkIds.IndexTip] - j[LandmarkIds.IndexMcp], j[LandmarkIds.IndexMcp] - j[LandmarkIds.Wrist]);

    /// <summary>食指 PIP 关节的屈曲角。</summary>
    public static float IndexPipAngle(Vector3[] j) => Flex(j, LandmarkIds.IndexMcp, LandmarkIds.IndexPip, LandmarkIds.IndexDip);
  }
}
