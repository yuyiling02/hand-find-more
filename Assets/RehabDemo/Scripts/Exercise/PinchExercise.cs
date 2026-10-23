using UnityEngine;

namespace RehabDemo
{
  /// <summary>
  ///   捏合训练：小球放在桌面上，用拇指和食指对准它捏合就能把它拿起来，保持一会儿计一次，松开后小球落回桌面。
  ///   “对准”和按钮一样按画面判断（虚拟手悬在桌面上方，够不到桌面上的东西）：
  ///   从相机穿过两指中点的射线经过小球附近就算对准。
  /// </summary>
  public class PinchExercise : ExerciseBase
  {
    public static readonly Color BallColor = new Color32(0xE6, 0x3B, 0xC8, 0xFF);

    private enum BallState { Resting, Held, Falling }

    [Tooltip("小球半径（米）")]
    public float ballRadius = 0.022f;
    [Tooltip("小球一开始放在桌面的哪里（x, z）")]
    public Vector2 startPosition = new Vector2(0f, 0.12f);
    [Tooltip("两指中点离小球多近算对准（米，按画面判断）")]
    public float grabRadius = 0.03f;
    [Tooltip("捏合度低于这个值就松手")]
    public float releaseScore = 0.7f;
    [Tooltip("没对准小球时空捏，完成度最多到这个值（只会到黄色）")]
    public float missScoreCap = 0.75f;
    [Tooltip("小球可以落在桌面的范围（半宽 x, 半深 z）")]
    public Vector2 tableHalfSize = new Vector2(0.55f, 0.35f);

    public override string DisplayName => "捏合";

    private GameObject _ball;
    private Material _ballMaterial;
    private BallState _state;
    private Vector3 _position;
    private float _fallSpeed;
    private Vector3 _grabOffset;
    private Quaternion _palmRotation = Quaternion.identity;
    private float _sizeFactor = 1f;
    private bool _aimed;
    private Camera _camera;

    protected override float ComputeScore(Vector3[] joints)
    {
      var pinch = Mathf.InverseLerp(config.pinchOpenRatio, config.pinchClosedRatio, HandMetrics.PinchRatio(joints));
      // 只有对准或已经拿着小球时，捏合才算数
      return _aimed || _state == BallState.Held ? pinch : Mathf.Min(pinch, missScoreCap);
    }

    protected override float ComputeAngle(Vector3[] joints) => HandMetrics.PinchAngle(joints);

    protected override void OnBegin()
    {
      if (_ball == null)
      {
        _ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _ball.name = "PinchBall";
        Destroy(_ball.GetComponent<Collider>());
        _ball.transform.SetParent(transform, false);
        _ballMaterial = new Material(Shader.Find("Standard")) { color = BallColor };
        _ballMaterial.SetFloat("_Glossiness", 0.6f);
        _ball.GetComponent<MeshRenderer>().sharedMaterial = _ballMaterial;
      }
      _camera = Camera.main;
      _state = BallState.Resting;
      _aimed = false;
      _sizeFactor = 1f;
      _position = new Vector3(startPosition.x, ballRadius, startPosition.y);
      _ballMaterial.color = BallColor;
      _ball.transform.position = _position;
      _ball.transform.localScale = Vector3.one * (ballRadius * 2f);
      _ball.SetActive(true);
    }

    protected override void OnEnd()
    {
      if (_ball != null)
      {
        _ball.SetActive(false);
      }
    }

    protected override void OnTick(HandPose pose)
    {
      var tracked = pose.tracked && pose.joints != null;
      var pinchPoint = Vector3.zero;
      var pinch = 0f;
      _aimed = false;
      if (tracked)
      {
        var j = pose.joints;
        pinchPoint = (j[LandmarkIds.ThumbTip] + j[LandmarkIds.IndexTip]) * 0.5f;
        pinch = Mathf.InverseLerp(config.pinchOpenRatio, config.pinchClosedRatio, HandMetrics.PinchRatio(j));
        if (_camera != null && _state == BallState.Resting)
        {
          // 小球中心到“相机 → 两指中点”这条射线的距离
          var origin = _camera.transform.position;
          var direction = (pinchPoint - origin).normalized;
          _aimed = Vector3.Cross(direction, _position - origin).magnitude <= grabRadius;
        }
      }

      switch (_state)
      {
        case BallState.Resting:
          if (_aimed && pinch >= config.greenEnter)
          {
            _state = BallState.Held;
            // 把小球沿着“相机 → 小球”的视线拉到两指所在的深度：画面上它的位置不变（没有吸附的跳动），
            // 但在 3D 里它真的到了手指之间，之后手怎么翻转它都跟着转
            var origin = _camera.transform.position;
            var toBall = _position - origin;
            var depth = Vector3.Dot(pinchPoint - origin, toBall.normalized);
            if (depth > 0.05f && toBall.magnitude > 0.05f)
            {
              // 离相机近了看起来会变大，先按比例缩小，再慢慢恢复原大小
              _sizeFactor = depth / toBall.magnitude;
              _position = origin + toBall.normalized * depth;
            }
            // 记住小球在“手掌坐标系”里的位置
            _palmRotation = PalmRotation(pose.joints, _palmRotation);
            _grabOffset = Quaternion.Inverse(_palmRotation) * (_position - pinchPoint);
          }
          break;
        case BallState.Held:
          if (!tracked || pinch < releaseScore)
          {
            _state = BallState.Falling;
            _fallSpeed = 0f;
          }
          else
          {
            // 被夹住后跟着手走、跟着手转，和手的相对位置保持捏住时的样子
            _palmRotation = PalmRotation(pose.joints, _palmRotation);
            _position = pinchPoint + _palmRotation * _grabOffset;
            _sizeFactor = Mathf.MoveTowards(_sizeFactor, 1f, Time.deltaTime / 0.6f);
          }
          break;
        case BallState.Falling:
          _fallSpeed += 3f * Time.deltaTime;
          _position.y -= _fallSpeed * Time.deltaTime;
          if (_position.y <= ballRadius)
          {
            // 落回桌面，下一次从落点再捏起来
            _position.x = Mathf.Clamp(_position.x, -tableHalfSize.x, tableHalfSize.x);
            _position.z = Mathf.Clamp(_position.z, -tableHalfSize.y, tableHalfSize.y);
            _position.y = ballRadius;
            _sizeFactor = 1f;
            _state = BallState.Resting;
          }
          break;
      }

      _ball.transform.position = _position;
      // 对准时放大一点作为提示，拿起来后变绿
      var scale = (_state == BallState.Resting && _aimed ? 1.2f : 1f) * _sizeFactor;
      _ball.transform.localScale = Vector3.Lerp(_ball.transform.localScale, Vector3.one * (ballRadius * 2f * scale), 1f - Mathf.Exp(-Time.deltaTime / 0.06f));
      var target = _state == BallState.Held ? HandColorizer.GreenColor : BallColor;
      _ballMaterial.color = Color.Lerp(_ballMaterial.color, target, 1f - Mathf.Exp(-Time.deltaTime / 0.05f));
    }

    /// <summary>手掌的朝向（手腕指向中指根部为前，手掌法线为上）。算不出来时沿用上一次的。</summary>
    private static Quaternion PalmRotation(Vector3[] j, Quaternion fallback)
    {
      var forward = j[LandmarkIds.MiddleMcp] - j[LandmarkIds.Wrist];
      var normal = Vector3.Cross(j[LandmarkIds.IndexMcp] - j[LandmarkIds.Wrist], j[LandmarkIds.PinkyMcp] - j[LandmarkIds.Wrist]);
      return forward.sqrMagnitude > 1e-10f && normal.sqrMagnitude > 1e-10f ? Quaternion.LookRotation(forward, normal) : fallback;
    }

    private void OnDestroy()
    {
      Destroy(_ballMaterial);
    }
  }
}
