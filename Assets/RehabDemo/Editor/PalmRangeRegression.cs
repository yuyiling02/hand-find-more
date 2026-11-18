using System;
using UnityEditor;
using UnityEngine;

namespace RehabDemo
{
  public static class PalmRangeRegression
  {
    [MenuItem("Rehab Demo/Validate Palm Range")]
    public static void Run()
    {
      var axes = new OneEuroFilter3();
      axes.SetParams(0.6f, 0.02f, 1f);
      axes.Filter(Vector3.zero, 0f);
      var controlled = axes.Filter(Vector3.one, 1f / 30f, preserveZ: true);
      Check(controlled.z == 1f && controlled.x < 0.5f, "Controlled Z bypasses position smoothing, X still smooths");
      var heldZ = axes.Filter(new Vector3(0, 0, controlled.z), 1f / 30f, preserveZ: true);
      Check(heldZ.z == controlled.z, "Invalid depth can hold exact previous Z");
      axes.Reset();
      axes.Filter(Vector3.zero, 0f);
      Check(axes.Filter(Vector3.one, 1f / 30f).z < 0.5f, "Screen vertical retains original smoothing");
      var palm = new PalmRangeController();
      var frame = new RawHandFrame { hasHand = true };
      var ids = new[] { 0, 5, 9, 13, 17 };
      var points = new[] { Vector3.zero, new Vector3(-0.04f, 0.08f), new Vector3(-0.012f, 0.09f),
        new Vector3(0.016f, 0.085f), new Vector3(0.045f, 0.07f) };
      float Now() => frame.timestampMs / 1000f;
      void Feed(float scale, float angle = 0, int width = 640, bool mirror = false, long age = 0)
      {
        frame.timestampMs += 50;
        var radians = angle * Mathf.Deg2Rad;
        for (var i = 0; i < ids.Length; i++)
        {
          var p = points[i];
          var w = new Vector3(p.x * Mathf.Cos(radians), p.y, p.x * Mathf.Sin(radians));
          frame.world[ids[i]] = w;
          frame.normalized[ids[i]] = new Vector3((mirror ? 0.38f : 0.5f) + (mirror ? -1 : 1) * w.x * scale / width,
            0.4f + w.y * scale / 480f);
        }
        palm.Update(frame, width, 480, frame.timestampMs + age, Now(), palm.Position);
      }
      void Hold(float scale) { for (var i = 0; i < 22; i++) Feed(scale); }

      palm.StartCapture(Now());
      Hold(600);
      Check(palm.Stage == 1, "Retraction capture");
      var distance = palm.RelativeDistance;
      Feed(600, 40, 960, true);
      Check(Mathf.Abs(palm.RelativeDistance / distance - 1) < 0.001f, "Tilt, translation, mirror and aspect");
      Feed(600, 65);
      Check(!palm.Valid, "Side-on rejection");
      Feed(600, 55);
      Check(!palm.Valid, "Side-on hysteresis");
      Feed(600, 45);
      Check(palm.Valid, "Side-on recovery");
      Check(palm.Status.Contains("第 2/2 步"), "Recovered pre-calibration status clears stale pose warning");
      palm.StartCapture(Now());
      Hold(590);
      Check(!palm.Calibrated && palm.Status.Contains("更靠近"), "Reversed endpoint");
      Feed(600);
      Check(palm.Status.Contains("更靠近"), "Calibration failure remains visible on next valid frame");
      palm.StartCapture(Now());
      Hold(615);
      Check(!palm.Calibrated && palm.Status.Contains("太小"), "Small endpoint");
      palm.StartCapture(Now());
      Hold(850);
      Check(palm.Calibrated, "Extension capture");
      palm.ResetMotion();
      var before = palm.Position;
      Feed(850);
      Feed(850);
      Feed(850);
      Check(palm.Position > before && palm.Position - before <= 0.201f, "Direction and velocity limit");
      Hold(850);
      Check(palm.Position > 0.9f, "Full extension");
      Hold(600);
      Check(palm.Position < 0.1f, "Full retraction");
      var held = palm.Position;
      palm.Update(frame, 640, 480, frame.timestampMs, Now(), held);
      Check(palm.Position == held, "Duplicate timestamp");
      palm.Pause("等待追踪手掌");
      Feed(850, age: 300);
      Check(!palm.Valid && palm.Position == held, "Stale frame held");
      Feed(850);
      Feed(850);
      Check(palm.Position == held, "Recovery waits");
      Feed(850);
      Check(palm.Position > held, "Third valid frame resumes");
      Hold(700);
      var distanceBeforeTranslation = palm.RelativeDistance;
      var targetBeforeTranslation = palm.Target;
      for (var i = 0; i < frame.normalized.Length; i++) frame.normalized[i].y += 0.15f;
      frame.timestampMs += 50;
      palm.Update(frame, 640, 480, frame.timestampMs, Now(), palm.Position);
      Check(Mathf.Abs(palm.RelativeDistance - distanceBeforeTranslation) < 0.000001f && Mathf.Abs(palm.Target - targetBeforeTranslation) < 0.0001f,
        "Image vertical translation cannot drive distance target");
      palm.Gain = 3f;
      Feed(1200);
      Check(palm.Target == 1f, "Gain and clamp");
      palm.ResetMotion();
      Check(palm.Calibrated && !palm.Valid, "Mode switch resets filter");
      frame.isLeft = true;
      Feed(850);
      Check(palm.Calibrated, "Classifier label change preserves calibration");
      palm.ResetCalibration();
      Check(!palm.Calibrated && palm.Stage == 0, "Explicit reset clears calibration");
      palm.StartCapture(Now());
      palm.Tick(frame.timestampMs + 10000, Now() + 10.01f);
      Check(!palm.Collecting && palm.Status.Contains("不足"), "Calibration timeout");
      palm.Pause("等待追踪手掌");
      Check(palm.CalibrationFailure.Contains("不足") && palm.Status.Contains("不足"), "Timeout survives tracking pause");
      palm.ResetCalibration();
      palm.StartCapture(Now());
      for (var i = 0; i < 10; i++) Feed(600);
      var count = palm.CollectedSamples;
      frame.isLeft = !frame.isLeft;
      Feed(600);
      Check(palm.Collecting && palm.CollectedSamples > count, "One label flip preserves capture samples");
      Hold(600);
      Check(palm.Stage == 1, "Capture completes after label flip");
      palm.StartCapture(Now());
      Feed(850, 50);
      Check(!palm.Valid && palm.CaptureBlockReason.Contains("45"), "Capture rejects 50 degree pose explicitly");
      Hold(850);
      Check(palm.Calibrated, "Capture resumes after pose correction");
      palm.ResetCalibration();
      palm.StartCapture(Now());
      for (var i = 0; i < 10; i++) Feed(600);
      palm.Tick(frame.timestampMs + 1100, Now() + 1.1f);
      Check(palm.Collecting && palm.CollectedSamples == 0, "Long gap clears batch without cancelling capture");
      Debug.Log("[PalmRange] Synthetic regression passed");
    }

    private static void Check(bool condition, string scenario)
    {
      if (!condition) throw new InvalidOperationException(scenario);
    }
  }
}
