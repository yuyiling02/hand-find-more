using UnityEngine;

namespace RehabDemo
{
  /// <summary>握拳训练。目标物是手掌上方一个半透明的球，握拳时收缩，达标时变绿。</summary>
  public class FistExercise : ExerciseBase
  {
    [Tooltip("张开时球的直径（米）")]
    public float openDiameter = 0.13f;
    [Tooltip("握紧时球的直径（米）")]
    public float closedDiameter = 0.05f;
    [Tooltip("球心在手掌上方多高（米）")]
    public float heightAbovePalm = 0.07f;

    public override string DisplayName => "握拳";

    private static readonly Color _IdleColor = new Color(0.90f, 0.23f, 0.78f, 0.30f);
    private static readonly Color _DoneColor = new Color(0.34f, 0.82f, 0.39f, 0.40f);

    private GameObject _sphere;
    private Material _material;

    protected override float ComputeScore(Vector3[] joints) => HandMetrics.Fistness(joints, config.fistFullCurl);

    protected override float ComputeAngle(Vector3[] joints) => HandMetrics.AverageFingerFlex(joints);

    protected override void OnBegin()
    {
      if (_sphere == null)
      {
        _sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _sphere.name = "FistTarget";
        Destroy(_sphere.GetComponent<Collider>());
        _sphere.transform.SetParent(transform, false);
        _material = ProcTextures.CreateUnlitMaterial(_IdleColor);
        var meshRenderer = _sphere.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = _material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      }
      _sphere.SetActive(false);
    }

    protected override void OnEnd()
    {
      if (_sphere != null)
      {
        _sphere.SetActive(false);
      }
    }

    protected override void OnTick(HandPose pose)
    {
      _sphere.SetActive(pose.tracked);
      if (!pose.tracked)
      {
        return;
      }
      var j = pose.joints;
      var palm = (j[LandmarkIds.Wrist] + j[LandmarkIds.IndexMcp] + j[LandmarkIds.PinkyMcp]) / 3f;
      _sphere.transform.position = palm + Vector3.up * heightAbovePalm;
      _sphere.transform.localScale = Vector3.one * Mathf.Lerp(openDiameter, closedDiameter, Score);
      var target = ColorState == HandColorState.Green ? _DoneColor : _IdleColor;
      _material.color = Color.Lerp(_material.color, target, 1f - Mathf.Exp(-Time.deltaTime / 0.05f));
    }

    private void OnDestroy()
    {
      Destroy(_material);
    }
  }
}
