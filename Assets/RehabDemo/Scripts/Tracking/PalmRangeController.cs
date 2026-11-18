using System;
using System.Collections.Generic;
using UnityEngine;

namespace RehabDemo
{
  public enum ForwardControlMode { ScreenVertical, PalmRange, ModelDepth }

  public sealed class PalmRangeController
  {
    private static readonly (int a, int b)[] Segments = { (0, 5), (0, 9), (0, 13), (0, 17), (5, 17), (5, 13) };
    private const int MinimumSamples = 15;
    private const int MaximumAgeMs = 250;
    private const float DeadZone = 0.01f;
    private const float MaximumSpeed = 4f;
    private readonly float[] reference = new float[Segments.Length];
    private readonly float[] lengths = new float[Segments.Length];
    private readonly float[] projected = new float[Segments.Length];
    private readonly float[] scratch = new float[Segments.Length];
    private readonly List<(float[] lengths, float[] projected)> retractFrames = new();
    private readonly List<float> samples = new();
    private readonly OneEuroFilter filter = new() { minCutoff = 2f, beta = 1f, dCutoff = 1f };
    private float firstAccepted = -1f, lastAccepted = -1f;
    public int CollectedSamples => Stage == 0 ? retractFrames.Count : samples.Count;
    public bool CanCapture => Valid && AgeMs <= MaximumAgeMs && Angle <= 45f;
    public string CaptureBlockReason { get; private set; } = "等待手掌";
    public string CalibrationFailure => calibrationIssue ?? "";
    public string TrackingStatus { get; private set; } = "等待手掌";
    private bool sideOn;
    private int recovery;
    private long lastFrame = -1;
    private float started, retract, retractMad, extend;
    private bool filtered;
    private string calibrationIssue;
    public string CalibrationNotice { get; private set; } = "本次播放尚未校准（校准仅在本次播放内有效）";

    public bool Calibrated { get; private set; }
    public bool Collecting { get; private set; }
    public bool Valid { get; private set; }
    public int Stage { get; private set; }
    public float Gain { get; set; } = 1f;
    public float Position { get; private set; } = 0.5f;
    public float Target { get; private set; } = 0.5f;
    public float Scale { get; private set; }
    public float RelativeDistance => Scale > 0 ? 1f / Scale : 0f;
    public float Angle { get; private set; }
    public int SegmentCount { get; private set; }
    public long AgeMs { get; private set; }
    public string Status { get; private set; } = "请采集收手位置";

    public void ResetMotion()
    {
      filter.Reset();
      filtered = false;
      recovery = 0;
      Valid = false;
    }

    public void CancelCapture()
    {
      if (!Collecting) return;
      Collecting = false;
      retractFrames.Clear();
      samples.Clear();
      Status = Stage == 0 ? "请采集收手位置" : "请采集伸手位置";
    }

    public void ResetCalibration(string reason = "已清除校准，请重新采集两个位置")
    {
      CalibrationNotice = reason;
      calibrationIssue = null;
      firstAccepted = lastAccepted = -1f;
      Stage = 0;
      Collecting = Calibrated = false;
      sideOn = false;
      lastFrame = -1;
      Array.Clear(reference, 0, reference.Length);
      retractFrames.Clear();
      samples.Clear();
      ResetMotion();
      Status = "请采集收手位置";
    }

    public void StartCapture(float now)
    {
      if (Collecting || Calibrated) return;
      calibrationIssue = null;
      started = now;
      firstAccepted = lastAccepted = -1f;
      samples.Clear();
      retractFrames.Clear();
      Collecting = true;
      Status = "请保持手不动（0/15）";
    }

    public void Tick(long nowMs, float now)
    {
      AgeMs = lastFrame < 0 ? -1 : nowMs - lastFrame;
      if (AgeMs > MaximumAgeMs) Pause("手部结果已过期");
      if (Collecting && lastAccepted >= 0f && now - lastAccepted > 1f)
      {
        retractFrames.Clear(); samples.Clear();
        firstAccepted = lastAccepted = -1f;
      }
      if (Collecting && now - started >= 10f)
      {
        Collecting = false;
        Status = calibrationIssue = "有效样本不足，请保持同一只手完整入镜、掌心朝向摄像头后重试";
      }
    }

    public void Pause(string reason)
    {
      ResetMotion();
      TrackingStatus = CaptureBlockReason = reason;
      Status = calibrationIssue ?? reason;
    }

    public void Update(RawHandFrame frame, int width, int height, long nowMs, float now, float currentPosition)
    {
      // A calibration session belongs to one physical hand; classifier labels are diagnostic only.
      if (frame.timestampMs <= lastFrame) return;
      var elapsed = lastFrame < 0 ? 0f : (frame.timestampMs - lastFrame) / 1000f;
      lastFrame = frame.timestampMs;
      AgeMs = nowMs - lastFrame;
      if (AgeMs > MaximumAgeMs)
      {
        Pause("手部结果已过期");
        return;
      }

      var begin = System.Diagnostics.Stopwatch.GetTimestamp();
      var measured = Measure(frame, width, height, out var distance, out var reason);
      GeometryMs = (System.Diagnostics.Stopwatch.GetTimestamp() - begin) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
      if (!measured)
      {
        Pause(reason);
        return;
      }
      Valid = true;
      TrackingStatus = "手掌追踪有效";
      CaptureBlockReason = Angle > 45f ? "校准要求倾角不超过45°" : "";
      if (Collecting)
      {
        if (firstAccepted < 0f) firstAccepted = now;
        lastAccepted = now;
        if (Stage == 0) retractFrames.Add(((float[])lengths.Clone(), (float[])projected.Clone()));
        else samples.Add(distance);
        var count = Stage == 0 ? retractFrames.Count : samples.Count;
        Status = $"请保持手不动（{count}/{MinimumSamples}）";
        if (now - firstAccepted >= 1f && count >= MinimumSamples) FinishCapture();
      }
      if (!Calibrated)
      {
        if (!Collecting)
          Status = calibrationIssue ?? (Stage == 0 ? "手掌有效：请采集收手位置（第 1/2 步）" : "收手已采集：请采集伸手位置（第 2/2 步）");
        return;
      }
      if (++recovery < 3)
      {
        Position = Mathf.Clamp01(currentPosition);
        Status = "等待手掌稳定";
        return;
      }
      recovery = 3;
      if (!filtered)
      {
        Position = Mathf.Clamp01(currentPosition);
        filter.Filter(Position, 0f);
        filtered = true;
      }
      var position = (retract - distance) / (retract - extend);
      Target = Mathf.Clamp01(0.5f + (position - 0.5f) * Mathf.Clamp(Gain, 0.5f, 3f));
      var dt = Mathf.Clamp(elapsed, 0f, 0.05f);
      var smoothed = filter.Filter(Target, dt);
      if (Mathf.Abs(smoothed - Position) > DeadZone)
        Position = Mathf.MoveTowards(Position, smoothed, MaximumSpeed * dt);
      Status = "手掌伸收控制中";
    }

    public double GeometryMs { get; private set; }
    public long FrameTimestamp => lastFrame;

    private bool Measure(RawHandFrame frame, int width, int height, out float distance, out string reason)
    {
      SegmentCount = 0;
      Scale = distance = 0f;
      var w = frame.world;
      var normal = Vector3.Cross(w[5] - w[0], w[17] - w[0]);
      var magnitude = normal.magnitude;
      Angle = magnitude > 1e-7f && Finite(magnitude)
        ? Mathf.Acos(Mathf.Clamp01(Mathf.Abs(normal.z) / magnitude)) * Mathf.Rad2Deg : 90f;
      sideOn = Angle > (sideOn ? 50f : 60f);
      if (sideOn || (Collecting && Angle > 45f))
      {
        reason = Collecting && Angle > 45f ? "校准要求倾角不超过45°" : "请让掌心或手背朝向摄像头";
        return false;
      }
      if (width <= 16 || height <= 16)
      {
        reason = "等待摄像头画面";
        return false;
      }
      for (var i = 0; i < Segments.Length; i++)
      {
        var (a, b) = Segments[i];
        var v = w[b] - w[a];
        var length = v.magnitude;
        var n = frame.normalized[b] - frame.normalized[a];
        var pixels = new Vector2(n.x * width, n.y * height).magnitude;
        var ratio = new Vector2(v.x, v.y).magnitude / length;
        var valid = Finite(length) && length > 1e-5f && Finite(ratio) && ratio >= 0.35f &&
                    Finite(pixels) && pixels >= 8f;
        lengths[i] = valid ? length : 0f;
        projected[i] = valid ? pixels / ratio : 0f;
      }
      reason = "有效手掌线段不足或尺度不一致";
      if (!Estimate(projected, lengths, out distance)) return false;
      reason = null;
      return true;
    }

    private bool Estimate(float[] spans, float[] frameLengths, out float distance)
    {
      SegmentCount = 0;
      Scale = distance = 0f;
      for (var i = 0; i < spans.Length; i++)
      {
        var length = Stage == 0 ? frameLengths[i] : reference[i];
        if (spans[i] > 0f && length > 0f) scratch[SegmentCount++] = spans[i] / length;
      }
      if (SegmentCount < 3) return false;
      var median = Median(scratch, SegmentCount);
      for (var i = 0; i < SegmentCount; i++) scratch[i] = Mathf.Abs(scratch[i] - median);
      if (!Finite(median) || median <= 0f || Median(scratch, SegmentCount) > 0.2f * median) return false;
      Scale = median;
      distance = 1f / median;
      return true;
    }

    private void FinishCapture()
    {
      if (Stage == 0)
      {
        for (var i = 0; i < reference.Length; i++)
        {
          var values = new List<float>();
          foreach (var frame in retractFrames)
            if (frame.lengths[i] > 0f) values.Add(frame.lengths[i]);
          reference[i] = values.Count > 0 ? Median(values.ToArray(), values.Count) : 0f;
        }
        Stage = 1;
        foreach (var frame in retractFrames)
          if (Estimate(frame.projected, frame.lengths, out var distance)) samples.Add(distance);
        if (samples.Count < MinimumSamples)
        {
          ResetCalibration();
          Status = calibrationIssue = "收手姿态变化过大，请重新采集";
          return;
        }
        (retract, retractMad) = SampleStatistics();
        Status = "请伸向摄像头，再采集伸手位置";
      }
      else
      {
        var (center, mad) = SampleStatistics();
        var difference = retract - center;
        if (difference <= Mathf.Max(6f * Mathf.Max(retractMad, mad), 0.10f * retract))
        {
          Collecting = false;
          Status = calibrationIssue = difference <= 0f ? "请将手伸得比收手时更靠近摄像头" : "两点区别太小或动作不稳，请重新采集伸手位置";
          return;
        }
        extend = center;
        Calibrated = true;
        CalibrationNotice = "两步校准已完成";
        ResetMotion();
        Status = "校准完成，等待手掌稳定";
      }
      Collecting = false;
      retractFrames.Clear();
      samples.Clear();
    }

    private (float median, float mad) SampleStatistics()
    {
      var values = samples.ToArray();
      var center = Median(values, values.Length);
      for (var i = 0; i < values.Length; i++) values[i] = Mathf.Abs(values[i] - center);
      return (center, Median(values, values.Length));
    }

    private static float Median(float[] values, int count)
    {
      Array.Sort(values, 0, count);
      var middle = count / 2;
      return count % 2 == 0 ? (values[middle - 1] + values[middle]) * 0.5f : values[middle];
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
  }
}
