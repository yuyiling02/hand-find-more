namespace RehabDemo
{
  /// <summary>MediaPipe 手部 21 个关键点的标准编号。</summary>
  public static class LandmarkIds
  {
    public const int Count = 21;

    public const int Wrist = 0;
    public const int ThumbCmc = 1, ThumbMcp = 2, ThumbIp = 3, ThumbTip = 4;
    public const int IndexMcp = 5, IndexPip = 6, IndexDip = 7, IndexTip = 8;
    public const int MiddleMcp = 9, MiddlePip = 10, MiddleDip = 11, MiddleTip = 12;
    public const int RingMcp = 13, RingPip = 14, RingDip = 15, RingTip = 16;
    public const int PinkyMcp = 17, PinkyPip = 18, PinkyDip = 19, PinkyTip = 20;

    /// <summary>骨骼连线（起点, 终点）：五根手指 + 手掌轮廓。</summary>
    public static readonly int[,] Bones =
    {
      { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 4 },
      { 5, 6 }, { 6, 7 }, { 7, 8 },
      { 9, 10 }, { 10, 11 }, { 11, 12 },
      { 13, 14 }, { 14, 15 }, { 15, 16 },
      { 17, 18 }, { 18, 19 }, { 19, 20 },
      { 0, 5 }, { 5, 9 }, { 9, 13 }, { 13, 17 }, { 0, 17 },
    };
  }
}
