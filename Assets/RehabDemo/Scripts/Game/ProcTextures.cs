using UnityEngine;

namespace RehabDemo
{
  /// <summary>程序生成的贴图和材质，省去美术资源。</summary>
  public static class ProcTextures
  {
    private static Sprite _rounded;
    private static Sprite _circle;
    private static Texture2D _star;

    /// <summary>白色圆角矩形，九宫格拉伸后四个角不变形。</summary>
    public static Sprite Rounded
    {
      get
      {
        if (_rounded == null)
        {
          _rounded = CreateRoundedSprite(64, 22);
        }
        return _rounded;
      }
    }

    /// <summary>白色实心圆。</summary>
    public static Sprite Circle
    {
      get
      {
        if (_circle == null)
        {
          _circle = CreateRoundedSprite(64, 32);
        }
        return _circle;
      }
    }

    /// <summary>白色五角星，给粒子用。</summary>
    public static Texture2D Star
    {
      get
      {
        if (_star == null)
        {
          _star = CreateStarTexture(64);
        }
        return _star;
      }
    }

    /// <summary>不受光照影响、支持半透明的纯色材质。用完由调用者 Destroy。</summary>
    public static Material CreateUnlitMaterial(Color color) => new Material(Shader.Find("Sprites/Default")) { color = color };

    private static Sprite CreateRoundedSprite(int size, int radius)
    {
      var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
      var pixels = new Color32[size * size];
      for (var y = 0; y < size; y++)
      {
        for (var x = 0; x < size; x++)
        {
          // 到圆角边缘的距离，用来做 1 像素的抗锯齿
          var dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius), 0f);
          var dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius), 0f);
          var alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
          pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
        }
      }
      texture.SetPixels32(pixels);
      texture.Apply();
      var border = Mathf.Min(radius + 2, size / 2 - 1);
      return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }

    private static Texture2D CreateStarTexture(int size)
    {
      // 十个顶点，外圈和内圈交替
      var polygon = new Vector2[10];
      for (var i = 0; i < 10; i++)
      {
        var angle = Mathf.PI / 2f + i * Mathf.PI / 5f;
        var r = i % 2 == 0 ? 0.95f : 0.42f;
        polygon[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
      }

      var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
      var pixels = new Color32[size * size];
      const int samples = 3;
      for (var y = 0; y < size; y++)
      {
        for (var x = 0; x < size; x++)
        {
          var inside = 0;
          for (var sy = 0; sy < samples; sy++)
          {
            for (var sx = 0; sx < samples; sx++)
            {
              var p = new Vector2((x + (sx + 0.5f) / samples) / size, (y + (sy + 0.5f) / samples) / size) * 2f - Vector2.one;
              if (IsInside(polygon, p))
              {
                inside++;
              }
            }
          }
          pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * inside / (samples * samples)));
        }
      }
      texture.SetPixels32(pixels);
      texture.Apply();
      return texture;
    }

    private static bool IsInside(Vector2[] polygon, Vector2 p)
    {
      var inside = false;
      for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
      {
        if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
            p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
        {
          inside = !inside;
        }
      }
      return inside;
    }
  }
}
