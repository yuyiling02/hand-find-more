using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RehabDemo
{
  // Buffer numeric samples in memory. No camera images and no per-frame disk writes.
  public sealed class ForwardControlRecording
  {
    private readonly StringBuilder rows = new StringBuilder();
    private float started;
    public bool Active { get; private set; }
    public string Status { get; private set; } = "";
    public string SavedPath { get; private set; }

    public void Start()
    {
      if (Active) return;
      rows.Clear();
      rows.AppendLine("update_ms,hand_ms,new_hand,tracked,mode,calibrated,palm_y,palm_span_px,palm_q,palm_angle,palm_segments,palm_valid,depth_ms,depth,depth_valid,depth_age_ms,target,controller_position,table_position,table_z,status,effective_mode,calibration_stage,gain,calibration_notice,raw_has_hand,raw_is_left,handedness_score,collecting,collected_samples,capture_block_reason,calibration_failure");
      started = Time.unscaledTime;
      Active = true;
      Status = "正在记录 30 秒：静止 5 秒，再保持画面高度伸收 5 次";
    }

    public void Record(HandFrameBus bus, RawHandFrame raw, bool newFrame)
    {
      if (!Active) return;
      var depth = bus.runner.Depth;
      var palm = bus.PalmRange;
      var mode = bus.ForwardMode;
      var model = mode == ForwardControlMode.ModelDepth;
      var geometry = mode == ForwardControlMode.PalmRange;
      var calibrated = model ? depth.Calibrated : geometry && palm.Calibrated;
      var palmY = (raw.normalized[0].y + raw.normalized[5].y + raw.normalized[17].y) / 3f;
      var texture = bus.runner.PreviewTexture;
      var span = raw.normalized[9] - raw.normalized[0];
      var pixels = texture == null ? 0f : new Vector2(span.x * texture.width, span.y * texture.height).magnitude;
      var output = Mathf.InverseLerp(bus.areaZMin, bus.areaZMax, bus.TableZ);
      var target = model ? depth.Target : geometry ? palm.Target : 1f - palmY;
      var position = model ? depth.Progress : geometry ? palm.Position : output;
      var status = model ? depth.Status : geometry ? palm.Status : "画面上下";
      rows.AppendLine(string.Join(",", new[] {
        N(bus.runner.ClockMs), N(raw.timestampMs), N(newFrame ? 1 : 0), N(HandFrameBus.Current.tracked ? 1 : 0), mode.ToString(), N(calibrated ? 1 : 0),
        N(palmY), N(pixels), N(palm.RelativeDistance), N(palm.Angle), N(palm.SegmentCount), N(palm.Valid ? 1 : 0),
        N(depth.Latest?.timestampMs ?? -1), N(depth.Latest?.depth ?? float.NaN), N(depth.Latest?.valid == true ? 1 : 0), N(depth.ResultAgeMs),
        N(target), N(position), N(output), N(bus.TableZ), "\"" + status.Replace("\"", "\"\"") + "\"",
        (mode == ForwardControlMode.ScreenVertical ? mode.ToString() : calibrated ? mode.ToString() : "Hold"), N(palm.Stage), N(model ? depth.movementSensitivity : geometry ? palm.Gain : 1f),
        "\"" + (geometry ? palm.CalibrationNotice : "").Replace("\"", "\"\"") + "\"", N(raw.hasHand ? 1 : 0), N(raw.isLeft ? 1 : 0), N(raw.handednessScore), N(palm.Collecting ? 1 : 0), N(palm.CollectedSamples),
        "\"" + palm.CaptureBlockReason.Replace("\"", "\"\"") + "\"", "\"" + palm.CalibrationFailure.Replace("\"", "\"\"") + "\"" }));
      if (Time.unscaledTime - started >= 30f) Stop();
    }

    private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    public void Stop()
    {
      if (!Active) return;
      Active = false;
      try
      {
#if UNITY_EDITOR
        var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "ForwardControl"));
#else
        var directory = Path.Combine(Application.persistentDataPath, "ForwardControl");
#endif
        Directory.CreateDirectory(directory);
        SavedPath = Path.Combine(directory, "forward-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".csv");
        File.WriteAllText(SavedPath, rows.ToString(), new UTF8Encoding(true));
        Status = "记录已保存：" + Path.GetFileName(SavedPath);
        Debug.Log("[ForwardControl] Saved " + SavedPath);
      }
      catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
      {
        Status = "保存记录失败：" + error.Message;
      }
      rows.Clear();
    }
  }
}
