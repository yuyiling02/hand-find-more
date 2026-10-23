using UnityEngine;

namespace RehabDemo
{
  /// <summary>
  ///   总流程：Idle → MenuOpen → Exercise → Celebrate → Idle。
  ///   调试快捷键：数字键 1/2/3 直接开始对应的训练，Esc 回到 Idle。
  /// </summary>
  public class GameFlow : MonoBehaviour
  {
    public enum FlowState { Idle, MenuOpen, Exercise, Celebrate }

    public ExerciseBase[] exercises;
    public ProgressHUD hud;
    public DwellButton menuButton;
    public MenuPanel menuPanel;
    public CelebrateFx celebrateFx;
    public HandColorizer colorizer;
    public Sfx sfx;
    [Tooltip("完成后 “Progress 100%” 停留多久（秒）")]
    public float celebrateTime = 1.5f;

    public static GameFlow Instance { get; private set; }

    public FlowState State { get; private set; } = FlowState.Idle;

    private ExerciseBase _current;
    private float _celebrateTimer;

    private void Awake()
    {
      Instance = this;
      EnsureMicrowaveExercise();
    }

    private void EnsureMicrowaveExercise()
    {
      if (exercises == null || exercises.Length == 0)
      {
        return;
      }
      foreach (var ex in exercises)
      {
        if (ex is MicrowaveExercise)
        {
          return;
        }
      }
      var host = exercises[0] != null ? exercises[0].gameObject : gameObject;
      var mw = host.GetComponent<MicrowaveExercise>() ?? host.AddComponent<MicrowaveExercise>();
      mw.config = exercises[0] != null ? exercises[0].config : null;
      mw.colorizer = colorizer;
      var updated = new ExerciseBase[exercises.Length + 1];
      System.Array.Copy(exercises, updated, exercises.Length);
      updated[exercises.Length] = mw;
      exercises = updated;
    }

    private void OnEnable()
    {
      if (menuButton != null)
      {
        menuButton.Triggered += OnMenuButton;
      }
      if (menuPanel != null)
      {
        menuPanel.CardSelected += OnCardSelected;
      }
    }

    private void OnDisable()
    {
      if (menuButton != null)
      {
        menuButton.Triggered -= OnMenuButton;
      }
      if (menuPanel != null)
      {
        menuPanel.CardSelected -= OnCardSelected;
      }
    }

    private void OnCardSelected(int index)
    {
      if (sfx != null)
      {
        sfx.PlayClick();
      }
      StartExercise(index);
    }

    private void OnMenuButton(DwellButton button)
    {
      if (sfx != null)
      {
        sfx.PlayClick();
      }
      switch (State)
      {
        case FlowState.Idle:
          OpenMenu();
          break;
        case FlowState.MenuOpen:
          ReturnToIdle();
          break;
        case FlowState.Exercise:
          // 训练中再按一次：中断训练，回到菜单
          OpenMenu();
          break;
      }
    }

    public void OpenMenu()
    {
      ReturnToIdle();
      State = FlowState.MenuOpen;
      if (menuPanel != null)
      {
        menuPanel.Open();
      }
    }

    public void StartExercise(int index)
    {
      if (exercises == null || index < 0 || index >= exercises.Length || exercises[index] == null)
      {
        return;
      }
      ReturnToIdle();

      _current = exercises[index];
      _current.RepCounted += OnRepCounted;
      _current.Completed += OnCompleted;
      _current.Begin();
      State = FlowState.Exercise;
      if (hud != null)
      {
        hud.Show(0f);
      }
      Debug.Log($"[RehabDemo] 开始训练：{_current.DisplayName}");
    }

    /// <summary>中断当前训练、收起菜单，回到 Idle。</summary>
    public void ReturnToIdle()
    {
      if (_current != null)
      {
        _current.RepCounted -= OnRepCounted;
        _current.Completed -= OnCompleted;
        _current.End();
        _current = null;
      }
      if (menuPanel != null)
      {
        menuPanel.Close();
      }
      if (hud != null)
      {
        hud.Hide();
      }
      State = FlowState.Idle;
    }

    private void OnRepCounted(ExerciseBase exercise)
    {
      if (hud != null)
      {
        hud.SetProgress(exercise.Progress);
      }
      if (colorizer != null)
      {
        colorizer.Flash();
      }
      // 最后一次由完成音效代替
      if (sfx != null && exercise.Progress < 1f)
      {
        sfx.PlayDing();
      }
      Debug.Log($"[RehabDemo] {exercise.DisplayName} 第 {exercise.Reps} 次，进度 {exercise.Progress:P0}");
    }

    private void OnCompleted(ExerciseBase exercise)
    {
      // 训练会在这个回调之后自行结束，这里只解除订阅
      exercise.RepCounted -= OnRepCounted;
      exercise.Completed -= OnCompleted;
      _current = null;
      State = FlowState.Celebrate;
      _celebrateTimer = celebrateTime;

      var joints = HandFrameBus.Current.joints;
      if (celebrateFx != null && joints != null)
      {
        celebrateFx.Play((joints[LandmarkIds.Wrist] + joints[LandmarkIds.IndexMcp] + joints[LandmarkIds.PinkyMcp]) / 3f);
      }
      if (sfx != null)
      {
        sfx.PlayFanfare();
      }
      Debug.Log($"[RehabDemo] {exercise.DisplayName} 完成");
    }

    private void Update()
    {
      // 庆祝期间 MENU 按钮不响应
      if (menuButton != null)
      {
        menuButton.interactable = State != FlowState.Celebrate;
      }

      if (State != FlowState.Celebrate)
      {
        return;
      }
      _celebrateTimer -= Time.deltaTime;
      if (_celebrateTimer <= 0f)
      {
        ReturnToIdle();
      }
    }

    private void OnGUI()
    {
      var e = Event.current;
      if (e.type != EventType.KeyDown)
      {
        return;
      }
      // 同一次按键可能以 keyCode 或 character 的形式到达（取决于输入法），两种都认
      if (e.keyCode == KeyCode.Escape)
      {
        ReturnToIdle();
        e.Use();
        return;
      }
      var index = -1;
      if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha4)
      {
        index = e.keyCode - KeyCode.Alpha1;
      }
      else if (e.keyCode >= KeyCode.Keypad1 && e.keyCode <= KeyCode.Keypad4)
      {
        index = e.keyCode - KeyCode.Keypad1;
      }
      else if (e.character >= '1' && e.character <= '4')
      {
        index = e.character - '1';
      }
      // 已经在做这一项时不重新开始，避免一次按键触发两次
      if (index >= 0 && !(State == FlowState.Exercise && _current == GetExercise(index)))
      {
        StartExercise(index);
        e.Use();
      }
    }

    private ExerciseBase GetExercise(int index) => exercises != null && index < exercises.Length ? exercises[index] : null;
  }
}
