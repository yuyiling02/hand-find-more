using UnityEngine;

namespace RehabDemo
{
  /// <summary>
  ///   胶囊手：21 个球（关节）+ 圆柱（骨骼）+ 一片手掌网格，全部由代码生成，不需要模型资源。
  /// </summary>
  public class CapsuleHandView : MonoBehaviour
  {
    public HandFrameBus bus;
    [Tooltip("留空则在自身或父对象上找")]
    public HandColorizer colorizer;
    [Tooltip("手的材质模板，运行时会复制一份再改颜色")]
    public Material material;

    [Header("尺寸（米，会再乘以 HandFrameBus.handScale）")]
    public float jointRadius = 0.012f;
    public float tipRadius = 0.010f;
    public float boneRadius = 0.009f;
    [Tooltip("手出现、消失的动画时长（秒）")]
    public float fadeTime = 0.2f;

    // 手掌轮廓，按顺序围成一圈
    private static readonly int[] _PalmLoop = { 0, 1, 5, 9, 13, 17 };

    private Material _materialInstance;
    private GameObject _root;
    private readonly Transform[] _joints = new Transform[LandmarkIds.Count];
    private Transform[] _bones;
    private Mesh _palmMesh;
    private readonly Vector3[] _palmVertices = new Vector3[_PalmLoop.Length * 2];
    private readonly Vector3[] _scaled = new Vector3[LandmarkIds.Count];
    private float _visibility;

    private void Awake()
    {
      if (material != null)
      {
        _materialInstance = new Material(material);
      }
      else
      {
        _materialInstance = new Material(Shader.Find("Standard")) { color = HandColorizer.NeutralColor };
      }

      _root = new GameObject("Hand");
      _root.transform.SetParent(transform, false);

      for (var i = 0; i < _joints.Length; i++)
      {
        _joints[i] = CreatePart(PrimitiveType.Sphere, $"Joint{i}");
      }
      _bones = new Transform[LandmarkIds.Bones.GetLength(0)];
      for (var i = 0; i < _bones.Length; i++)
      {
        _bones[i] = CreatePart(PrimitiveType.Cylinder, $"Bone{LandmarkIds.Bones[i, 0]}-{LandmarkIds.Bones[i, 1]}");
      }
      CreatePalm();

      if (colorizer == null)
      {
        colorizer = GetComponentInParent<HandColorizer>();
      }
      if (colorizer != null)
      {
        colorizer.Bind(_materialInstance);
      }
      _root.SetActive(false);
    }

    private Transform CreatePart(PrimitiveType type, string partName)
    {
      var go = GameObject.CreatePrimitive(type);
      go.name = partName;
      Destroy(go.GetComponent<Collider>());
      go.transform.SetParent(_root.transform, false);
      go.GetComponent<MeshRenderer>().sharedMaterial = _materialInstance;
      return go.transform;
    }

    private void CreatePalm()
    {
      var go = new GameObject("Palm");
      go.transform.SetParent(_root.transform, false);

      // 正反两面各用一套顶点，这样两面的法线都正确
      var n = _PalmLoop.Length;
      var triangles = new int[(n - 2) * 3 * 2];
      var t = 0;
      for (var i = 1; i < n - 1; i++)
      {
        triangles[t++] = 0;
        triangles[t++] = i;
        triangles[t++] = i + 1;
        triangles[t++] = n;
        triangles[t++] = n + i + 1;
        triangles[t++] = n + i;
      }

      _palmMesh = new Mesh { name = "Palm" };
      _palmMesh.MarkDynamic();
      _palmMesh.vertices = _palmVertices;
      _palmMesh.triangles = triangles;

      go.AddComponent<MeshFilter>().sharedMesh = _palmMesh;
      go.AddComponent<MeshRenderer>().sharedMaterial = _materialInstance;
    }

    private void Update()
    {
      var pose = HandFrameBus.Current;
      var tracked = pose.tracked && pose.joints != null;
      _visibility = fadeTime > 0f ? Mathf.MoveTowards(_visibility, tracked ? 1f : 0f, Time.deltaTime / fadeTime) : (tracked ? 1f : 0f);
      var visible = _visibility > 0.001f && pose.joints != null;
      if (_root.activeSelf != visible)
      {
        _root.SetActive(visible);
      }
      if (!visible)
      {
        return;
      }

      // 出现、消失时整只手朝手掌中心缩放。手丢失后关节数组保持最后的姿态，正好用来做消失动画
      var amount = Mathf.SmoothStep(0f, 1f, _visibility);
      var source = pose.joints;
      var center = (source[LandmarkIds.Wrist] + source[LandmarkIds.IndexMcp] + source[LandmarkIds.PinkyMcp]) / 3f;
      for (var i = 0; i < _scaled.Length; i++)
      {
        _scaled[i] = center + (source[i] - center) * amount;
      }

      var scale = (bus != null ? bus.handScale : 1.6f) * amount;
      var j = _scaled;

      for (var i = 0; i < _joints.Length; i++)
      {
        var isTip = i != 0 && i % 4 == 0;
        _joints[i].position = j[i];
        _joints[i].localScale = Vector3.one * ((isTip ? tipRadius : jointRadius) * 2f * scale);
      }

      var diameter = boneRadius * 2f * scale;
      for (var i = 0; i < _bones.Length; i++)
      {
        var a = j[LandmarkIds.Bones[i, 0]];
        var b = j[LandmarkIds.Bones[i, 1]];
        var d = b - a;
        var length = d.magnitude;
        _bones[i].position = (a + b) * 0.5f;
        if (length > 1e-5f)
        {
          _bones[i].rotation = Quaternion.FromToRotation(Vector3.up, d / length);
        }
        // Unity 自带圆柱高 2、直径 1
        _bones[i].localScale = new Vector3(diameter, length * 0.5f, diameter);
      }

      var n = _PalmLoop.Length;
      for (var i = 0; i < n; i++)
      {
        _palmVertices[i] = _palmVertices[n + i] = j[_PalmLoop[i]];
      }
      _palmMesh.vertices = _palmVertices;
      _palmMesh.RecalculateNormals();
      _palmMesh.RecalculateBounds();
    }

    private void OnDestroy()
    {
      Destroy(_materialInstance);
      Destroy(_palmMesh);
    }
  }
}
