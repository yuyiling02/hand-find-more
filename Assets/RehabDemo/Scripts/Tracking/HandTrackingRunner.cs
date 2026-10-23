using System;
using System.Collections;
using System.Diagnostics;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Unity;
using Mediapipe.Unity.Experimental;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
// 示例程序集里还有一个 Mediapipe.Unity.RunningMode，这里明确用 Tasks API 的
using RunningMode = Mediapipe.Tasks.Vision.Core.RunningMode;

namespace RehabDemo
{
  /// <summary>
  ///   打开摄像头，把画面送给 MediaPipe HandLandmarker（CPU、LIVE_STREAM），
  ///   并把最新一帧结果保存在线程安全的缓冲里，供 <see cref="HandFrameBus" /> 在主线程读取。
  /// </summary>
  public class HandTrackingRunner : MonoBehaviour
  {
    private const string _ModelName = "hand_landmarker.bytes";

    [Header("摄像头")]
    public int deviceIndex = 0;
    public int requestWidth = 640;
    public int requestHeight = 480;
    public int requestFps = 30;

    [Header("图像方向（方向不对时在这里调）")]
    [Tooltip("水平镜像（自拍效果）：手往右移，画面里的手也往右移")]
    public bool mirrorX = true;
    [Tooltip("把 Unity 纹理（原点在左下）翻成 MediaPipe 需要的图像（原点在左上）。关键点上下颠倒时切换它")]
    public bool flipY = true;

    [Header("识别")]
    [Range(1, 2)] public int numHands = 1;
    [Range(0f, 1f)] public float minHandDetectionConfidence = 0.5f;
    [Range(0f, 1f)] public float minHandPresenceConfidence = 0.5f;
    [Range(0f, 1f)] public float minTrackingConfidence = 0.5f;

    [Header("调试")]
    public bool logDetectionFps = true;

    /// <summary>摄像头原始画面（未镜像），没启动时为 null。</summary>
    public Texture PreviewTexture => _webCam;
    public bool PreviewVerticallyMirrored => _webCam != null && _webCam.videoVerticallyMirrored;
    public float DetectionFps { get; private set; }
    public string Status { get; private set; } = "未启动";

    private HandLandmarker _landmarker;
    private WebCamTexture _webCam;
    private TextureFramePool _pool;
    private bool _hasNewCameraFrame;
    private readonly Stopwatch _stopwatch = new Stopwatch();
    private long _lastTimestampMs = -1;

    // 以下字段由回调线程写、主线程读，访问时必须持有 _lock
    private readonly object _lock = new object();
    private readonly RawHandFrame _latest = new RawHandFrame();
    private int _sequence;
    private int _resultCount;
    private string _callbackError;
    private volatile bool _mirrored;

    private float _fpsTimer;

    /// <summary>
    ///   取最新一帧结果。<paramref name="lastSequence" /> 之后有新结果时拷贝到 <paramref name="destination" /> 并返回 true。
    /// </summary>
    public bool TryGetLatest(RawHandFrame destination, ref int lastSequence)
    {
      lock (_lock)
      {
        if (_sequence == lastSequence)
        {
          return false;
        }
        lastSequence = _sequence;
        destination.CopyFrom(_latest);
        return true;
      }
    }

    private IEnumerator Start()
    {
      Status = "加载模型…";
#if UNITY_EDITOR
      IResourceManager resourceManager = new LocalResourceManager();
#else
      IResourceManager resourceManager = new StreamingAssetsResourceManager();
#endif
      yield return resourceManager.PrepareAssetAsync(_ModelName, _ModelName, false);

      var options = new HandLandmarkerOptions(
        new BaseOptions(BaseOptions.Delegate.CPU, modelAssetPath: _ModelName),
        runningMode: RunningMode.LIVE_STREAM,
        numHands: numHands,
        minHandDetectionConfidence: minHandDetectionConfidence,
        minHandPresenceConfidence: minHandPresenceConfidence,
        minTrackingConfidence: minTrackingConfidence,
        resultCallback: OnResult);
      _landmarker = HandLandmarker.CreateFromOptions(options, null);

      Status = "打开摄像头…";
      var devices = WebCamTexture.devices;
      if (devices == null || devices.Length == 0)
      {
        Status = "没有找到摄像头";
        Debug.LogError("[RehabDemo] 没有找到摄像头");
        yield break;
      }
      var device = devices[Mathf.Clamp(deviceIndex, 0, devices.Length - 1)];
      _webCam = new WebCamTexture(device.name, requestWidth, requestHeight, requestFps);
      _webCam.Play();

      // 摄像头真正出图之前 width 是 16
      var waitStart = Time.realtimeSinceStartup;
      yield return new WaitUntil(() => _webCam.width > 16 || Time.realtimeSinceStartup - waitStart > 10f);
      if (_webCam.width <= 16)
      {
        Status = "摄像头启动失败（可能被别的程序占用）";
        Debug.LogError($"[RehabDemo] 摄像头 {device.name} 启动失败，可能被别的程序占用");
        yield break;
      }
      Debug.Log($"[RehabDemo] 摄像头 {device.name} {_webCam.width}x{_webCam.height}");

      _pool = new TextureFramePool(_webCam.width, _webCam.height, TextureFormat.RGBA32, 10);
      var imageProcessingOptions = new ImageProcessingOptions(rotationDegrees: 0);
      _stopwatch.Start();
      Status = "运行中";

      AsyncGPUReadbackRequest req = default;
      var waitUntilReqDone = new WaitUntil(() => req.done);

      while (true)
      {
        if (!_hasNewCameraFrame || !_pool.TryGetTextureFrame(out var textureFrame))
        {
          yield return null;
          continue;
        }
        _hasNewCameraFrame = false;

        _mirrored = mirrorX;
        // 与示例 ImageSource.GetTransformationOptions() 一致：纹理本身没有上下颠倒时需要垂直翻转
        var flipVertically = flipY ^ _webCam.videoVerticallyMirrored;
        req = textureFrame.ReadTextureAsync(_webCam, mirrorX, flipVertically);
        yield return waitUntilReqDone;

        if (req.hasError)
        {
          Debug.LogWarning("[RehabDemo] 读取摄像头画面失败");
          textureFrame.Release();
          continue;
        }

        var image = textureFrame.BuildCPUImage();
        textureFrame.Release();
        _landmarker.DetectAsync(image, NextTimestampMs(), imageProcessingOptions);
      }
    }

    /// <summary>DetectAsync 的时间戳必须严格递增。</summary>
    private long NextTimestampMs()
    {
      _lastTimestampMs = Math.Max(_stopwatch.ElapsedMilliseconds, _lastTimestampMs + 1);
      return _lastTimestampMs;
    }

    private void Update()
    {
      if (_webCam != null && _webCam.didUpdateThisFrame)
      {
        _hasNewCameraFrame = true;
      }

      _fpsTimer += Time.unscaledDeltaTime;
      if (_fpsTimer >= 1f)
      {
        int count;
        string error;
        lock (_lock)
        {
          count = _resultCount;
          _resultCount = 0;
          error = _callbackError;
          _callbackError = null;
        }
        DetectionFps = count / _fpsTimer;
        _fpsTimer = 0f;

        if (error != null)
        {
          Debug.LogError($"[RehabDemo] 结果回调出错：{error}");
        }
        if (logDetectionFps && _landmarker != null && _stopwatch.IsRunning)
        {
          Debug.Log($"[RehabDemo] 识别帧率 {DetectionFps:F1} fps");
        }
      }
    }

    // ⚠️ 在 MediaPipe 的后台线程执行：不能调用任何 Unity API，只做加锁拷贝
    private void OnResult(HandLandmarkerResult result, Mediapipe.Image image, long timestamp)
    {
      try
      {
        lock (_lock)
        {
          _sequence++;
          _resultCount++;
          _latest.timestampMs = timestamp;
          _latest.hasHand = false;

          var handLandmarks = result.handLandmarks;
          var handWorldLandmarks = result.handWorldLandmarks;
          if (handLandmarks == null || handWorldLandmarks == null)
          {
            return;
          }

          // 多只手时取左右手分类置信度最高的一只
          var best = -1;
          var bestScore = -1f;
          var bestIsLeft = false;
          var handCount = Math.Min(handLandmarks.Count, handWorldLandmarks.Count);
          for (var i = 0; i < handCount; i++)
          {
            if (handLandmarks[i].landmarks == null || handLandmarks[i].landmarks.Count < LandmarkIds.Count ||
                handWorldLandmarks[i].landmarks == null || handWorldLandmarks[i].landmarks.Count < LandmarkIds.Count)
            {
              continue;
            }

            var score = 0f;
            var isLeft = false;
            var handedness = result.handedness;
            if (handedness != null && i < handedness.Count && handedness[i].categories != null && handedness[i].categories.Count > 0)
            {
              var category = handedness[i].categories[0];
              score = category.score;
              isLeft = category.categoryName == "Left";
            }
            if (score > bestScore)
            {
              best = i;
              bestScore = score;
              bestIsLeft = isLeft;
            }
          }
          if (best < 0)
          {
            return;
          }

          var normalized = handLandmarks[best].landmarks;
          var world = handWorldLandmarks[best].landmarks;
          for (var i = 0; i < LandmarkIds.Count; i++)
          {
            _latest.normalized[i] = new Vector3(normalized[i].x, normalized[i].y, normalized[i].z);
            _latest.world[i] = new Vector3(world[i].x, world[i].y, world[i].z);
          }
          // isLeft 指用户现实中的左手。实测：送入镜像图像时 MediaPipe 的标签与现实相反，所以镜像时取反
          _latest.isLeft = bestIsLeft ^ _mirrored;
          _latest.handednessScore = bestScore;
          _latest.hasHand = true;
        }
      }
      catch (Exception e)
      {
        // 异常不能抛回原生代码
        _callbackError = e.ToString();
      }
    }

    private void OnDestroy()
    {
      StopAllCoroutines();
      _landmarker?.Close();
      _landmarker = null;
      _pool?.Dispose();
      _pool = null;
      if (_webCam != null)
      {
        _webCam.Stop();
        Destroy(_webCam);
        _webCam = null;
      }
    }
  }
}
