using UnityEngine;

namespace RehabDemo
{
  /// <summary>One Euro Filter：静止时强平滑，快速移动时自动放宽，兼顾稳定和跟手。</summary>
  public class OneEuroFilter
  {
    public float minCutoff = 1.0f;
    public float beta = 0.02f;
    public float dCutoff = 1.0f;

    private bool _initialized;
    private float _x;
    private float _dx;

    public void Reset() => _initialized = false;

    public float Filter(float x, float dt)
    {
      if (!_initialized)
      {
        _initialized = true;
        _x = x;
        _dx = 0f;
        return x;
      }
      if (dt <= 0f)
      {
        return _x;
      }

      var dx = (x - _x) / dt;
      _dx = Mathf.Lerp(_dx, dx, Alpha(dCutoff, dt));
      var cutoff = minCutoff + beta * Mathf.Abs(_dx);
      _x = Mathf.Lerp(_x, x, Alpha(cutoff, dt));
      return _x;
    }

    private static float Alpha(float cutoff, float dt)
    {
      var tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 1e-4f));
      return 1f / (1f + tau / dt);
    }
  }

  public class OneEuroFilter3
  {
    private readonly OneEuroFilter _x = new OneEuroFilter();
    private readonly OneEuroFilter _y = new OneEuroFilter();
    private readonly OneEuroFilter _z = new OneEuroFilter();

    public void SetParams(float minCutoff, float beta, float dCutoff)
    {
      _x.minCutoff = _y.minCutoff = _z.minCutoff = minCutoff;
      _x.beta = _y.beta = _z.beta = beta;
      _x.dCutoff = _y.dCutoff = _z.dCutoff = dCutoff;
    }

    public void Reset()
    {
      _x.Reset();
      _y.Reset();
      _z.Reset();
    }

    public Vector3 Filter(Vector3 v, float dt) => new Vector3(_x.Filter(v.x, dt), _y.Filter(v.y, dt), _z.Filter(v.z, dt));
  }
}
