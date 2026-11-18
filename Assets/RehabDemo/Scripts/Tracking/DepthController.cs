using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

namespace RehabDemo
{
  [Serializable]
  public class DepthSample
  {
    public long timestampMs;
    public bool isLeft, valid;
    public float depth, mad, inferenceMs;
    public string reason;
  }

  public class DepthController : MonoBehaviour
  {
    [Serializable] private class Lifecycle { public string state; public int port; }
    [Serializable] private class Request
    {
      public long timestampMs;
      public int width, height, channels;
      public bool isLeft;
      public Vector3[] landmarks;
    }
    private class Frame
    {
      public long timestamp;
      public int width, height, channels;
      public byte[] pixels;
      public RawHandFrame hand;
    }

    public bool depthEnabled;
    [Range(1f, 3f)]
    [Tooltip("以校准中点为中心放大前后动作，数值越大，小幅伸收的移动越明显")]
    public float movementSensitivity = 2f;
    public bool logDiagnostics;
    public string Status { get; private set; } = "原有控制";
    public bool Ready { get; private set; }
    public bool Calibrated { get; private set; }
    public bool CollectingCalibration => collecting;
    public string CalibrationButton => stage == 0 ? "采集收手位置" : "采集伸手位置";
    public string PositionMode => !depthEnabled ? "模型深度未启用" :
      !Calibrated ? "校准准备中，前后位置保持不动" : "伸手／收手控制前后";
    public float Progress { get; private set; } = 0.5f;
    public float Target { get; private set; } = 0.5f;
    public long SubmittedFrames => Interlocked.Read(ref submittedFrames);
    public long ReceivedFrames { get; private set; }
    public DepthSample Latest { get; private set; }
    public float UpdateHz { get; private set; }
    public float ResultAgeMs => Latest == null ? float.PositiveInfinity : runner.ClockMs - Latest.timestampMs;
    private HandTrackingRunner runner;
    private readonly object gate = new object();
    private readonly Queue<string> messages = new Queue<string>();
    private readonly AutoResetEvent wake = new AutoResetEvent(false);
    private readonly List<float> calibration = new List<float>();
    private Frame pending;
    private Process process;
    private TcpClient client;
    private Thread worker;
    private volatile bool stopping;
    private volatile bool acceptingFrames;
    private bool wasEnabled, collecting, calibrationLeft;
    private int stage, restartCount;
    private float started, retract, retractMad, lastValid = float.NegativeInfinity;
    private long lastCapture = -1000, consumed, submittedFrames;
    private bool initializedPosition;
    private readonly OneEuroFilter travelFilter = new OneEuroFilter { minCutoff = 2f, beta = 1f, dCutoff = 1f };
    private string root;
    private string cameraConfiguration;
    private long previousSample;

    private void Awake()
    {
      runner = GetComponent<HandTrackingRunner>();
      root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "tools", "depth"));
    }

    // Called by MediaPipe with the image that produced these landmarks.
    public void Submit(Mediapipe.Image image, RawHandFrame hand)
    {
      if (!acceptingFrames || hand.timestampMs - lastCapture < 67) return;
      var width = image.Width();
      var height = image.Height();
      var channels = image.Channels();
      var step = image.Step();
      var rowBytes = width * channels;
      if ((channels != 3 && channels != 4) || step < rowBytes) return;
      var frame = new Frame { timestamp = hand.timestampMs, width = width, height = height, channels = channels,
        pixels = new byte[rowBytes * height], hand = new RawHandFrame() };
      using (var pixels = new Mediapipe.PixelWriteLock(image))
      {
        var source = pixels.Pixels();
        if (step == rowBytes) Marshal.Copy(source, frame.pixels, 0, frame.pixels.Length);
        else for (var row = 0; row < height; row++)
          Marshal.Copy(IntPtr.Add(source, row * step), frame.pixels, row * rowBytes, rowBytes);
      }
      frame.hand.CopyFrom(hand);
      lock (gate)
      {
        if (!acceptingFrames) return;
        lastCapture = hand.timestampMs;
        pending = frame;
        Interlocked.Increment(ref submittedFrames);
        wake.Set();
      }
    }

    private void Post(string message) { lock (gate) messages.Enqueue(message); }

    public void Retry()
    {
      StopWorker();
      restartCount = 0;
      StartWorker();
    }

    private void StartWorker()
    {
      stopping = false;
      Ready = false;
      Status = "加载深度模型…";
      var python = Path.Combine(root, ".venv", "Scripts", "python.exe");
      if (!File.Exists(python)) { Status = "请先运行 tools/depth/setup.ps1"; return; }
      try
      {
        process = new Process { StartInfo = new ProcessStartInfo(python, "-u \"" + Path.Combine(root, "server.py") + "\"")
        { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
          RedirectStandardOutput = true, RedirectStandardError = true } };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) Post(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Post("错误 " + e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
      }
      catch (Exception error) { Status = error.Message; }
    }

    private void Communicate(int port)
    {
      try
      {
        client = new TcpClient();
        client.Connect("127.0.0.1", port);
        using (var stream = client.GetStream())
        using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
          stream.ReadTimeout = 10000;
          stream.WriteTimeout = 10000;
          while (!stopping)
          {
            wake.WaitOne(100);
            Frame frame;
            lock (gate) { frame = pending; pending = null; }
            if (frame == null || runner.ClockMs - frame.timestamp > 250) continue;
            var request = new Request { timestampMs = frame.timestamp, width = frame.width,
              height = frame.height, channels = frame.channels, isLeft = frame.hand.isLeft, landmarks = frame.hand.normalized };
            var header = Encoding.UTF8.GetBytes(JsonUtility.ToJson(request));
            writer.Write(header.Length);
            writer.Write(header);
            writer.Write(frame.pixels);
            writer.Flush();
            var count = reader.ReadInt32();
            if (count < 1 || count > 65536) throw new IOException("Invalid depth response length");
            var response = reader.ReadBytes(count);
            if (response.Length != count) throw new EndOfStreamException();
            Post(Encoding.UTF8.GetString(response));
          }
        }
      }
      catch (Exception error) { if (!stopping) Post("连接失败 " + error.Message); }
    }

    private void Update()
    {
      var handBus = GetComponent<HandFrameBus>();
      var configuration = runner.deviceIndex + ":" + runner.mirrorX + ":" + runner.flipY + ":" + handBus?.handFilter;
      if (cameraConfiguration != null && cameraConfiguration != configuration) ResetCalibration();
      cameraConfiguration = configuration;
      if (depthEnabled != wasEnabled)
      {
        wasEnabled = depthEnabled;
        if (depthEnabled) StartWorker(); else { StopWorker(); Status = "原有控制"; }
      }
      lock (gate)
      {
        while (messages.Count > 0)
        {
          var message = messages.Dequeue();
          if (!message.StartsWith("{")) { Status = message; continue; }
          var lifecycle = JsonUtility.FromJson<Lifecycle>(message);
          if (lifecycle.state == "ready")
          {
            Ready = true;
            acceptingFrames = true;
            Status = "就绪，请校准";
            worker = new Thread(() => Communicate(lifecycle.port)) { IsBackground = true };
            worker.Start();
          }
          else if (!string.IsNullOrEmpty(lifecycle.state)) Status = lifecycle.state == "warming" ? "模型预热…" : "加载模型…";
          else
          {
            Latest = JsonUtility.FromJson<DepthSample>(message);
            ReceivedFrames++;
          }
        }
      }
      if (process != null && process.HasExited && Ready && restartCount == 0)
      {
        StopWorker();
        restartCount++;
        StartWorker();
      }
      else if (process != null && process.HasExited)
      {
        Ready = false;
        acceptingFrames = false;
        Status = "推理服务已退出，请查看错误并重试";
      }
      if (collecting && Time.unscaledTime - started > 5f)
      {
        collecting = false;
        Status = "有效样本不足。请保持手掌完整可见，然后重新采集当前位置";
      }
      if (Latest == null || Latest.timestampMs <= consumed) return;
      consumed = Latest.timestampMs;
      if (logDiagnostics)
        UnityEngine.Debug.Log($"[Depth] timestamp={Latest.timestampMs} ageMs={ResultAgeMs:F1} inferenceMs={Latest.inferenceMs:F1} valid={Latest.valid} depth={Latest.depth} mad={Latest.mad}");
      if (!Latest.valid || ResultAgeMs > 250) { Status = string.IsNullOrEmpty(Latest.reason) ? "深度结果过期" : Latest.reason; return; }
      var bus = GetComponent<HandFrameBus>();
      if (bus != null && ((bus.handFilter == HandFilter.LeftOnly && !Latest.isLeft) ||
          (bus.handFilter == HandFilter.RightOnly && Latest.isLeft))) return;
      if ((Calibrated || collecting || stage == 1) && calibrationLeft != Latest.isLeft) ResetCalibration();
      if (collecting)
      {
        calibration.Add(Latest.depth);
        if (Time.unscaledTime - started >= 1 && calibration.Count >= 10) FinishCalibration();
        else Status = $"{CalibrationButton}中，请保持手不动（{calibration.Count}/10）";
      }
      if (previousSample > 0 && Latest.timestampMs > previousSample)
        UpdateHz = 1000f / (Latest.timestampMs - previousSample);
      previousSample = Latest.timestampMs;
      if (Calibrated) { lastValid = Time.unscaledTime; Status = "深度控制中"; }
    }

    public void ResetMotion()
    {
      initializedPosition = false;
      travelFilter.Reset();
    }

    public void ResetCalibration()
    {
      Calibrated = false;
      ResetMotion();
      collecting = false;
      stage = 0;
      calibration.Clear();
      Status = "请收手并点击校准";
    }

    public void Calibrate()
    {
      if (collecting) return;
      if (!Ready || Latest == null || !Latest.valid || ResultAgeMs > 250)
      {
        if (Ready) Status = "暂无有效手掌深度，请将手掌完整放入画面后重试";
        return;
      }
      if (Calibrated) ResetCalibration();
      collecting = true;
      calibrationLeft = Latest.isLeft;
      calibration.Clear();
      started = Time.unscaledTime;
      Status = stage == 0 ? "正在采集收手位置" : "正在采集伸手位置";
    }

    private static float Median(List<float> values)
    {
      var sorted = values.ToArray();
      Array.Sort(sorted);
      var middle = sorted.Length / 2;
      return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) * 0.5f : sorted[middle];
    }
    private float extend;
    private void FinishCalibration()
    {
      collecting = false;
      var center = Median(calibration);
      var deviations = calibration.ConvertAll(value => Mathf.Abs(value - center));
      var mad = Median(deviations);
      if (stage == 0)
      {
        retract = center;
        retractMad = mad;
        stage = 1;
        Status = "请伸手并再次点击校准";
      }
      else if (Mathf.Abs(center - retract) <= Mathf.Max(0.00001f, 6 * Mathf.Max(mad, retractMad)))
      {
        ResetCalibration();
        Status = "两点深度差不足。请沿摄像头方向拉开收手与伸手距离，再重新校准";
      }
      else { extend = center; Calibrated = true; Status = "校准完成"; }
    }

    public float MapZ(float current, float min, float max, float dt)
    {
      if (!depthEnabled || !Calibrated || Latest == null) return current;
      if (!Latest.valid || ResultAgeMs > 250 || Latest.isLeft != calibrationLeft || Time.unscaledTime - lastValid > 1)
      {
        ResetMotion();
        Status = ResultAgeMs > 250
          ? $"深度暂停，结果已过期 {ResultAgeMs:F0} ms"
          : "深度暂停，等待校准手的有效结果";
        return current;
      }
      if (!initializedPosition)
      {
        Progress = Mathf.InverseLerp(min, max, current);
        travelFilter.Filter(Progress, 0f);
        initializedPosition = true;
      }
      var calibratedPosition = (Latest.depth - retract) / (extend - retract);
      Target = Mathf.Clamp01(0.5f + (calibratedPosition - 0.5f) * movementSensitivity);
      var step = Mathf.Clamp(dt, 0f, 0.05f);
      var filtered = travelFilter.Filter(Target, step);
      if (Mathf.Abs(filtered - Progress) > 0.01f)
        Progress = Mathf.MoveTowards(Progress, filtered, 5f * step);
      return Mathf.Lerp(min, max, Progress);
    }

    private void StopWorker()
    {
      stopping = true;
      acceptingFrames = false;
      Ready = false;
      client?.Close();
      wake.Set();
      if (process != null)
      {
        try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
        process.Dispose();
        process = null;
      }
      worker?.Join(1000);
      worker = null;
      lock (gate)
      {
        pending = null;
        messages.Clear();
        Interlocked.Exchange(ref submittedFrames, 0);
      }
      ReceivedFrames = 0;
      Latest = null;
      ResetCalibration();
    }
    private void OnDestroy() { StopWorker(); wake.Dispose(); }
  }
}
