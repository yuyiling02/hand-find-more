using UnityEngine;

namespace RehabDemo
{
  /// <summary>
  ///   开发用调试面板，按 F1 切换。左下角显示摄像头画面并叠加 21 个关键点，旁边显示帧率和追踪状态。
  /// </summary>
  public class HandDebugPanel : MonoBehaviour
  {
    public HandTrackingRunner runner;
    private HandFrameBus _handBus;
    private bool _palmDetails;
    private Vector2 _controlScroll;
    private static readonly string[] ForwardModes = { "画面上下", "手掌伸收", "模型深度" };
    public bool visible = true;
    public int previewWidth = 200;
    public float infoWidth = 190f;

    private static readonly Color _BoneColor = new Color(1f, 1f, 1f, 0.9f);
    private static readonly Color _JointColor = new Color(0.2f, 1f, 0.3f);
    private static readonly Color _TipColor = new Color(1f, 0.3f, 0.3f);

    private float _renderFps;
    private GUIStyle _textStyle;
    private GUIStyle _buttonStyle;

    private void Awake()
    {
      _handBus = GetComponent<HandFrameBus>();
      if (runner == null)
      {
        runner = GetComponent<HandTrackingRunner>();
      }
    }

    private void Update()
    {
      if (Time.unscaledDeltaTime > 0f)
      {
        _renderFps = Mathf.Lerp(_renderFps, 1f / Time.unscaledDeltaTime, 0.05f);
      }
    }

    private void OnGUI()
    {
      var e = Event.current;
      if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F1)
      {
        visible = !visible;
        e.Use();
      }
      if (!visible || runner == null)
      {
        return;
      }

      _textStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true, wordWrap = true };
      _buttonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 11 };

      const float margin = 8f;
      var texture = runner.PreviewTexture;
      var aspect = texture != null && texture.width > 16 ? (float)texture.height / texture.width : 0.75f;
      var preview = new Rect(margin, UnityEngine.Screen.height - margin - previewWidth * aspect, previewWidth, previewWidth * aspect);

      GUI.Box(new Rect(preview.x - 2, preview.y - 2, preview.width + 4, preview.height + 4), GUIContent.none);
      if (texture != null && e.type == EventType.Repaint)
      {
        // 按送给 MediaPipe 的样子显示：水平镜像与 runner 一致，画面保持正立
        var uv = new Rect(0f, 0f, 1f, 1f);
        if (runner.mirrorX)
        {
          uv.x = 1f;
          uv.width = -1f;
        }
        if (runner.PreviewVerticallyMirrored)
        {
          uv.y = 1f;
          uv.height = -1f;
        }
        GUI.DrawTextureWithTexCoords(preview, texture, uv);
        DrawLandmarks(preview);
      }

      var raw = HandFrameBus.Raw;
      var depth = runner.Depth;
      if (depth != null && _handBus != null)
      {
        var panelWidth = Mathf.Min(460, Mathf.Max(120, UnityEngine.Screen.width - 106));
        var panel = new Rect(UnityEngine.Screen.width - panelWidth - 8, 8, panelWidth, Mathf.Min(350, UnityEngine.Screen.height - 16));
        var oldColor = GUI.color;
        GUI.color = new Color(0.12f, 0.12f, 0.12f, 1f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = oldColor;
        GUILayout.BeginArea(panel, GUI.skin.box);
        _controlScroll = GUILayout.BeginScrollView(_controlScroll);
        var mode = (ForwardControlMode)GUILayout.Toolbar((int)_handBus.ForwardMode, ForwardModes);
        _handBus.SetForwardMode(mode);
        if (mode == ForwardControlMode.PalmRange)
        {
          var palm = _handBus.PalmRange;
          var labelColor = GUI.contentColor;
          GUI.contentColor = palm.Calibrated ? Color.green : Color.yellow;
          GUILayout.Label(palm.Calibrated ? "已启用：手掌伸收控制前后" : "伸收待校准：前后位置保持不动");
          GUI.contentColor = labelColor;
          GUILayout.Label(palm.Status);
          GUILayout.Label($"单手会话 · 实际换手请重新校准 · 有效样本 {palm.CollectedSamples}/15");
          GUILayout.Label(palm.CanCapture ? "满足校准条件" : palm.CaptureBlockReason);
          if (!string.IsNullOrEmpty(palm.CalibrationFailure)) GUILayout.Label("采集失败：" + palm.CalibrationFailure);
          if (palm.Calibrated)
            GUILayout.Label($"伸收目标 {palm.Target:P0} · 当前行程 {palm.Position:P0}");
          else
          {
            GUILayout.Label(palm.CalibrationNotice);
            GUILayout.Label("保持画面高度：离摄像头较远 → 第1步；靠近摄像头 → 第2步");
          }
          GUILayout.BeginHorizontal();
          GUILayout.Label($"前后灵敏度 {palm.Gain:F1} 倍", GUILayout.Width(170));
          palm.Gain = GUILayout.HorizontalSlider(palm.Gain, 0.5f, 3f);
          GUILayout.EndHorizontal();
          GUILayout.BeginHorizontal();
          var enabledBefore = GUI.enabled;
          GUI.enabled = enabledBefore && !palm.Collecting && !palm.Calibrated;
          if (GUILayout.Button(palm.Collecting ? $"采集中 {palm.CollectedSamples}/15" : palm.Calibrated ? "伸收已启用" : palm.Stage == 0 ? "第 1/2 步：采集收手位置" : "第 2/2 步：采集伸手位置"))
            palm.StartCapture(Time.unscaledTime);
          GUI.enabled = enabledBefore;
          if (GUILayout.Button("重新校准")) palm.ResetCalibration();
          GUILayout.EndHorizontal();
          _palmDetails = GUILayout.Toggle(_palmDetails, "显示手掌诊断");
          if (_palmDetails)
          {
            GUILayout.Label($"有效 {palm.Valid} · 尺度 {palm.Scale:F1} · 线段 {palm.SegmentCount}/6 · 倾斜 {palm.Angle:F0}° · 结果年龄 {palm.AgeMs} ms");
            _handBus.LogPalmRange = GUILayout.Toggle(_handBus.LogPalmRange, "记录采集时间和前后行程");
          }
        }
        else if (mode == ForwardControlMode.ModelDepth)
        {
          GUILayout.Label(depth.PositionMode);
          GUILayout.Label(depth.Status);
          GUILayout.Label(depth.Calibrated ? "校准已完成 · 手掌高度固定" : "需要先采集收手位置，再采集伸手位置 · 手掌高度固定");
          GUILayout.Label($"前后目标 {depth.Target:P0} · 当前行程 {depth.Progress:P0} · {depth.UpdateHz:F1} Hz");
          GUILayout.Label($"图像入队 {depth.SubmittedFrames} · 深度返回 {depth.ReceivedFrames} · 结果年龄 {depth.ResultAgeMs:F0} ms");
          GUILayout.Label($"桌面 Z {_handBus.TableZ:F2} m");
          if (depth.Latest != null) GUILayout.Label($"手掌原始深度 {depth.Latest.depth:F3} · 有效 {depth.Latest.valid}");
          GUILayout.BeginHorizontal();
          GUILayout.Label($"前后灵敏度 {depth.movementSensitivity:F1} 倍", GUILayout.Width(170));
          depth.movementSensitivity = GUILayout.HorizontalSlider(depth.movementSensitivity, 1f, 3f);
          GUILayout.EndHorizontal();
          GUILayout.BeginHorizontal();
          if (!depth.Calibrated && !depth.CollectingCalibration && depth.Ready &&
              GUILayout.Button(depth.CalibrationButton)) depth.Calibrate();
          if (GUILayout.Button("重新校准")) depth.ResetCalibration();
          if (GUILayout.Button("重试服务")) depth.Retry();
          GUILayout.EndHorizontal();
        }
        var recording = _handBus.Recording;
        if (GUILayout.Button(recording.Active ? "停止并保存记录" : "记录 30 秒伸收诊断"))
        {
          if (recording.Active) recording.Stop(); else recording.Start();
        }
        if (!string.IsNullOrEmpty(recording.Status)) GUILayout.Label(recording.Status);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
      }
      var pose = HandFrameBus.Current;
      var hasRawHand = raw != null && raw.hasHand;

      var metrics = "pinch：—  fist：—";
      if (pose.tracked && pose.joints != null)
      {
        metrics = $"pinch：{HandMetrics.PinchRatio(pose.joints):F2}  fist：{HandMetrics.Fistness(pose.joints):F2}\n" +
          $"食指下压：{FingertipPointer.Curl:F0}°/{FingertipPointer.Baseline:F0}°{(FingertipPointer.IsPressing ? " <color=#FFC82E>按下</color>" : "")}";
      }
      var exercise = ExerciseBase.Active;
      string exerciseInfo;
      if (exercise is MicrowaveExercise mw)
      {
        exerciseInfo = $"训练：{mw.DisplayName}（R 重置）\n{mw.CurrentStepSummary}";
      }
      else if (exercise != null)
      {
        exerciseInfo = $"训练：{exercise.DisplayName}  {exercise.Reps} 次\nscore：{exercise.Score:F2}  角度：{exercise.AngleDegrees:F0}°";
      }
      else
      {
        exerciseInfo = "训练：自由模式（按 1/2 或点按钮）";
      }

      var infoText =
        $"<b>调试面板（F1 开关）</b>\n" +
        $"状态：{runner.Status}\n" +
        $"帧率：渲染 {_renderFps:F0} / 识别 {runner.DetectionFps:F1}\n" +
        $"追踪：{(pose.tracked ? "<color=#57D163>是</color>" : "<color=#E8443A>否</color>")}  " +
        $"{(hasRawHand ? (raw.isLeft ? "左手" : "右手") + $"（{raw.handednessScore:F2}）" : "")}\n" +
        $"{metrics}\n{exerciseInfo}";

      // 面板高度按文字自适应（至少与摄像头画面一样高），底部对齐
      var flow = GameFlow.Instance;
      const float buttonHeight = 20f;
      var textWidth = infoWidth - 12f;
      var textHeight = _textStyle.CalcHeight(new GUIContent(infoText), textWidth);
      var infoHeight = Mathf.Max(preview.height, textHeight + 8f + (flow != null ? buttonHeight + 6f : 0f));
      var info = new Rect(preview.xMax + margin, preview.yMax - infoHeight, infoWidth, infoHeight);
      GUI.Box(info, GUIContent.none);

      // 键盘收不到时（比如中文输入法拦截了数字键）可以直接用鼠标点
      if (flow != null)
      {
        string[] labels = { "捏合", "微波炉", "重置" };
        var button = new Rect(info.x + 4, info.yMax - buttonHeight - 4, (info.width - 8 - 4 * (labels.Length - 1)) / labels.Length, buttonHeight);
        for (var i = 0; i < labels.Length; i++)
        {
          if (GUI.Button(button, labels[i], _buttonStyle))
          {
            if (i < labels.Length - 1)
            {
              flow.StartExercise(i == 0 ? ExerciseIcon.Pinch : ExerciseIcon.Microwave);
            }
            else
            {
              if (ExerciseBase.Active is MicrowaveExercise activeMw)
              {
                activeMw.ResetSequence(true);
              }
              else
              {
                flow.ReturnToIdle();
              }
            }
          }
          button.x += button.width + 4;
        }
      }
      GUI.Label(new Rect(info.x + 6, info.y + 4, textWidth, textHeight), infoText, _textStyle);
    }

    private static void DrawLandmarks(Rect preview)
    {
      var raw = HandFrameBus.Raw;
      if (raw == null || !raw.hasHand)
      {
        return;
      }

      // 归一化图像坐标和 IMGUI 一样是左上角为原点、y 朝下
      Vector2 ToScreen(Vector3 n) => new Vector2(preview.x + n.x * preview.width, preview.y + n.y * preview.height);

      for (var i = 0; i < LandmarkIds.Bones.GetLength(0); i++)
      {
        DrawLine(ToScreen(raw.normalized[LandmarkIds.Bones[i, 0]]), ToScreen(raw.normalized[LandmarkIds.Bones[i, 1]]), _BoneColor, 2f);
      }
      for (var i = 0; i < LandmarkIds.Count; i++)
      {
        var isTip = i != 0 && i % 4 == 0;
        var p = ToScreen(raw.normalized[i]);
        var size = isTip ? 8f : 6f;
        var previous = GUI.color;
        GUI.color = isTip ? _TipColor : _JointColor;
        GUI.DrawTexture(new Rect(p.x - size / 2f, p.y - size / 2f, size, size), Texture2D.whiteTexture);
        GUI.color = previous;
      }
    }

    private static void DrawLine(Vector2 a, Vector2 b, Color color, float width)
    {
      var d = b - a;
      if (d.sqrMagnitude < 0.01f)
      {
        return;
      }
      var matrix = GUI.matrix;
      var previous = GUI.color;
      GUI.color = color;
      GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
      GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, d.magnitude, width), Texture2D.whiteTexture);
      GUI.matrix = matrix;
      GUI.color = previous;
    }
  }
}
