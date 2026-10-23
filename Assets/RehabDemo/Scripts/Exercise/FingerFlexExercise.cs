using UnityEngine;

namespace RehabDemo
{
  /// <summary>
  ///   屈指训练：弯曲食指中间的关节（PIP）。目标物是关节旁的一段弧线，
  ///   从“伸直”方向画到目标角度，末端有个小球标出要弯到的位置。
  /// </summary>
  public class FingerFlexExercise : ExerciseBase
  {
    [Tooltip("弧线半径（米）")]
    public float arcRadius = 0.05f;
    public float lineWidth = 0.004f;

    public override string DisplayName => "屈指";

    private const int _Segments = 16;
    private static readonly Color _IdleColor = new Color(1f, 1f, 1f, 0.9f);

    private LineRenderer _arc;
    private GameObject _marker;
    private Material _material;
    private Vector3 _bendDirection;
    private readonly Vector3[] _points = new Vector3[_Segments + 1];

    protected override float ComputeScore(Vector3[] joints) =>
      Mathf.InverseLerp(config.flexStartAngle, config.flexTargetAngle, HandMetrics.IndexPipAngle(joints));

    protected override float ComputeAngle(Vector3[] joints) => HandMetrics.IndexPipAngle(joints);

    protected override void OnBegin()
    {
      if (_arc == null)
      {
        _material = ProcTextures.CreateUnlitMaterial(_IdleColor);

        var arcGo = new GameObject("FlexArc");
        arcGo.transform.SetParent(transform, false);
        _arc = arcGo.AddComponent<LineRenderer>();
        _arc.useWorldSpace = true;
        _arc.positionCount = _points.Length;
        _arc.widthMultiplier = lineWidth;
        _arc.numCapVertices = 4;
        _arc.sharedMaterial = _material;
        _arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _marker.name = "FlexTarget";
        Destroy(_marker.GetComponent<Collider>());
        _marker.transform.SetParent(transform, false);
        _marker.transform.localScale = Vector3.one * (lineWidth * 3.5f);
        _marker.GetComponent<MeshRenderer>().sharedMaterial = _material;
      }
      _bendDirection = Vector3.zero;
      SetVisible(false);
    }

    protected override void OnEnd() => SetVisible(false);

    private void SetVisible(bool visible)
    {
      if (_arc != null)
      {
        _arc.gameObject.SetActive(visible);
        _marker.SetActive(visible);
      }
    }

    protected override void OnTick(HandPose pose)
    {
      SetVisible(pose.tracked);
      if (!pose.tracked)
      {
        return;
      }

      var j = pose.joints;
      var pip = j[LandmarkIds.IndexPip];
      var straight = (pip - j[LandmarkIds.IndexMcp]).normalized;

      // 手指弯向哪一侧：取下一节指骨垂直于“伸直方向”的分量。手指接近伸直时这个分量不稳定，沿用上一次的
      var next = j[LandmarkIds.IndexDip] - pip;
      var perpendicular = next - Vector3.Dot(next, straight) * straight;
      if (HandMetrics.IndexPipAngle(j) > 8f && perpendicular.sqrMagnitude > 1e-8f)
      {
        _bendDirection = perpendicular.normalized;
      }
      else
      {
        var fallback = _bendDirection != Vector3.zero ? _bendDirection : Vector3.down;
        fallback -= Vector3.Dot(fallback, straight) * straight;
        _bendDirection = fallback.sqrMagnitude > 1e-8f ? fallback.normalized : Vector3.Cross(straight, Vector3.right).normalized;
      }

      var targetRadians = config.flexTargetAngle * Mathf.Deg2Rad;
      for (var i = 0; i <= _Segments; i++)
      {
        var angle = targetRadians * i / _Segments;
        _points[i] = pip + arcRadius * (Mathf.Cos(angle) * straight + Mathf.Sin(angle) * _bendDirection);
      }
      _arc.SetPositions(_points);
      _marker.transform.position = _points[_Segments];

      var target = ColorState == HandColorState.Green ? HandColorizer.GreenColor : _IdleColor;
      _material.color = Color.Lerp(_material.color, target, 1f - Mathf.Exp(-Time.deltaTime / 0.05f));
    }

    private void OnDestroy()
    {
      Destroy(_material);
    }
  }
}
