using UnityEngine;

namespace RehabDemo
{
  /// <summary>
  ///   开发用调试面板，按 F1 切换。左下角显示摄像头画面并叠加 21 个关键点，旁边显示帧率和追踪状态。
  /// </summary>
  public class HandDebugPanel : MonoBehaviour
  {
    public HandTrackingRunner runner;
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
