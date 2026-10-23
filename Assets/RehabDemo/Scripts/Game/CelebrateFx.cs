using UnityEngine;

namespace RehabDemo
{
  /// <summary>完成训练时在手的位置爆出一把彩色星星。粒子系统在运行时由代码生成。</summary>
  public class CelebrateFx : MonoBehaviour
  {
    public int starCount = 60;
    public float lifetime = 1.2f;
    public float gravity = 0.3f;
    [Tooltip("星星大小范围（米）")]
    public Vector2 sizeRange = new Vector2(0.025f, 0.05f);
    [Tooltip("初速度范围（米/秒）")]
    public Vector2 speedRange = new Vector2(0.25f, 0.8f);

    private ParticleSystem _particles;
    private Material _material;

    private void Awake()
    {
      // 先设为未激活再配置，避免粒子系统一加上就按默认参数开始播放
      var go = new GameObject("Stars");
      go.SetActive(false);
      go.transform.SetParent(transform, false);
      _particles = go.AddComponent<ParticleSystem>();

      var main = _particles.main;
      main.playOnAwake = false;
      main.loop = false;
      main.duration = lifetime;
      main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
      main.startSpeed = new ParticleSystem.MinMaxCurve(speedRange.x, speedRange.y);
      main.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
      main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
      main.gravityModifier = gravity;
      main.simulationSpace = ParticleSystemSimulationSpace.World;
      main.maxParticles = Mathf.Max(starCount * 2, 100);

      // 品红、黄、青、绿四种颜色里随机取
      var gradient = new Gradient { mode = GradientMode.Fixed };
      gradient.SetKeys(
        new[]
        {
          new GradientColorKey(new Color32(0xE6, 0x3B, 0xC8, 0xFF), 0.25f),
          new GradientColorKey(new Color32(0xF5, 0xC5, 0x18, 0xFF), 0.50f),
          new GradientColorKey(new Color32(0x5F, 0xD3, 0xD0, 0xFF), 0.75f),
          new GradientColorKey(new Color32(0x57, 0xD1, 0x63, 0xFF), 1.00f),
        },
        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
      main.startColor = new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };

      var emission = _particles.emission;
      emission.enabled = false;

      var shape = _particles.shape;
      shape.shapeType = ParticleSystemShapeType.Sphere;
      shape.radius = 0.02f;

      // 快消失时淡出
      var colorOverLifetime = _particles.colorOverLifetime;
      colorOverLifetime.enabled = true;
      var fade = new Gradient();
      fade.SetKeys(
        new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
      colorOverLifetime.color = fade;

      var rotation = _particles.rotationOverLifetime;
      rotation.enabled = true;
      rotation.z = new ParticleSystem.MinMaxCurve(-3f, 3f);

      _material = ProcTextures.CreateUnlitMaterial(Color.white);
      _material.mainTexture = ProcTextures.Star;
      var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
      particleRenderer.sharedMaterial = _material;
      particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
      particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

      go.SetActive(true);
    }

    public void Play(Vector3 position)
    {
      _particles.transform.position = position;
      _particles.Play();
      _particles.Emit(starCount);
    }

    private void OnDestroy()
    {
      Destroy(_material);
    }
  }
}
