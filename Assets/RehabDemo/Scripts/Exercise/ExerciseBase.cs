using System;
using UnityEngine;

namespace RehabDemo
{
  /// <summary>
  ///   训练项目的公共逻辑：把 score 换算成红、黄、绿（带迟滞），达标保持一段时间计一次，放松后才能计下一次。
  ///   子类只需要实现 <see cref="ComputeScore" />。
  /// </summary>
  public abstract class ExerciseBase : MonoBehaviour
  {
    private enum RepState { Waiting, Counted }

    /// <summary>当前正在进行的训练，没有时为 null。</summary>
    public static ExerciseBase Active { get; private set; }

    public ExerciseConfig config;
    public HandColorizer colorizer;

    public abstract string DisplayName { get; }
    public bool IsRunning { get; private set; }
    /// <summary>平滑后的完成度 0~1。</summary>
    public float Score { get; private set; }
    public int Reps { get; private set; }
    public float Progress => config != null && config.targetReps > 0 ? Mathf.Clamp01((float)Reps / config.targetReps) : 0f;
    public HandColorState ColorState { get; private set; }
    /// <summary>显示在手上方的角度（度）。</summary>
    public float AngleDegrees { get; private set; }

    public event Action<ExerciseBase> RepCounted;
    public event Action<ExerciseBase> Completed;

    private RepState _repState;
    private float _holdTimer;

    /// <summary>把关节坐标换算成完成度 0~1（不需要平滑，基类会处理）。</summary>
    protected abstract float ComputeScore(Vector3[] joints);
    protected abstract float ComputeAngle(Vector3[] joints);
    protected virtual void OnBegin() { }
    protected virtual void OnEnd() { }
    /// <summary>训练进行中每帧调用，用来更新训练目标物。</summary>
    protected virtual void OnTick(HandPose pose) { }

    public void Begin()
    {
      if (Active != null && Active != this)
      {
        Active.End();
      }
      Active = this;
      IsRunning = true;
      Reps = 0;
      Score = 0f;
      _holdTimer = 0f;
      _repState = RepState.Waiting;
      ColorState = HandColorState.Red;
      OnBegin();
    }

    public void End()
    {
      if (!IsRunning)
      {
        return;
      }
      IsRunning = false;
      if (Active == this)
      {
        Active = null;
      }
      if (colorizer != null)
      {
        colorizer.SetState(HandColorState.Neutral);
      }
      OnEnd();
    }

    private void Update()
    {
      if (!IsRunning || config == null)
      {
        return;
      }

      var pose = HandFrameBus.Current;
      if (!pose.tracked || pose.joints == null)
      {
        // 手丢失：暂停计时，颜色回到默认，不清零
        _holdTimer = 0f;
        if (colorizer != null)
        {
          colorizer.SetState(HandColorState.Neutral);
        }
        OnTick(pose);
        return;
      }

      var raw = Mathf.Clamp01(ComputeScore(pose.joints));
      var blend = config.scoreSmoothTime > 0f ? 1f - Mathf.Exp(-Time.deltaTime / config.scoreSmoothTime) : 1f;
      Score = Mathf.Lerp(Score, raw, blend);
      AngleDegrees = ComputeAngle(pose.joints);

      // 颜色，带迟滞
      if (Score >= config.greenEnter || (ColorState == HandColorState.Green && Score >= config.greenExit))
      {
        ColorState = HandColorState.Green;
      }
      else
      {
        ColorState = Score >= config.yellowThreshold ? HandColorState.Yellow : HandColorState.Red;
      }
      if (colorizer != null)
      {
        colorizer.SetState(ColorState);
      }

      // 次数
      if (_repState == RepState.Waiting)
      {
        _holdTimer = ColorState == HandColorState.Green ? _holdTimer + Time.deltaTime : 0f;
        if (_holdTimer >= config.holdTime)
        {
          _holdTimer = 0f;
          _repState = RepState.Counted;
          Reps++;
          RepCounted?.Invoke(this);
        }
      }
      else if (Score < config.relaxThreshold)
      {
        _repState = RepState.Waiting;
      }

      OnTick(pose);

      if (Progress >= 1f)
      {
        // 先通知再结束，这样监听者还能读到最终的次数
        Completed?.Invoke(this);
        End();
      }
    }

    private void OnDisable()
    {
      End();
    }
  }
}
