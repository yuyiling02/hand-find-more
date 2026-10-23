using System;
using System.Collections.Generic;
using UnityEngine;

namespace RehabDemo
{
  /// <summary>
  ///   带蒙皮的手：用现成的、已绑好骨骼的手模型，每帧把每节骨骼转到 MediaPipe 关节连线的方向。
  ///   骨骼长度保持模型原样：手腕放在追踪到的位置，其余骨骼的位置从手腕开始按模型自己的长度逐节推出来。
  ///   位置和旋转都按世界坐标直接设置，所以骨骼是父子嵌套的还是平级的都能用
  ///   （WebXR 的 generic-hand 模型就是平级的）。
  ///   骨骼对应表留空时会按骨骼名字里的关键词自动填写。
  /// </summary>
  public class SkinnedHandView : MonoBehaviour
  {
    /// <summary>一节骨骼对应 MediaPipe 的一段：从关节 from 指向关节 to。</summary>
    [Serializable]
    public class BoneLink
    {
      public Transform bone;
      [Tooltip("这节骨骼末端的骨骼（下一节或指尖），用来确定模型里这节骨骼的方向")]
      public Transform end;
      public int from;
      public int to;

      [NonSerialized] public bool valid;
      [NonSerialized] public Quaternion offset;
      // end 是指尖骨骼（不是下一节）时，它跟着这节骨骼一起转
      [NonSerialized] public Transform tip;
      [NonSerialized] public Quaternion tipLocalRotation;
      [NonSerialized] public Vector3 tipLocalPosition;
      // 这节骨骼接在哪一节后面（null = 接在手腕上），以及默认姿态下相对它的位置
      [NonSerialized] public BoneLink parent;
      [NonSerialized] public Vector3 localPosition;
      // 这一帧算出的世界坐标
      [NonSerialized] public Vector3 position;
      [NonSerialized] public Quaternion rotation;
    }

    [Serializable]
    public class Rig
    {
      [Tooltip("手模型的根对象")]
      public Transform root;
      [Tooltip("手腕骨骼，留空自动查找")]
      public Transform wrist;
      public List<BoneLink> links = new List<BoneLink>();

      [NonSerialized] public bool ready;
      [NonSerialized] public Quaternion wristOffset;
      [NonSerialized] public Vector3 initialScale;
      [NonSerialized] public float restPalmLength;
      [NonSerialized] public List<Follower> followers;
    }

    /// <summary>没有对应 MediaPipe 关节的骨骼（如四指的掌骨），相对手腕保持不动。</summary>
    public class Follower
    {
      public Transform bone;
      public Vector3 localPosition;
      public Quaternion localRotation;
    }

    public HandFrameBus bus;
    [Tooltip("留空则在自身或父对象上找")]
    public HandColorizer colorizer;
    [Tooltip("手的材质模板，运行时会复制一份再改颜色")]
    public Material material;
    public Rig left = new Rig();
    public Rig right = new Rig();
    [Tooltip("手出现、消失的动画时长（秒）")]
    public float fadeTime = 0.2f;

    [Header("手指姿态修正")]
    [Tooltip("把四指的方向限制在人手做得出来的范围内，避免追踪不准时模型扭曲")]
    public bool constrainFingers = true;
    [Tooltip("指根左右张开的最大角度")]
    public float maxSplayAngle = 25f;
    [Tooltip("中间关节（PIP）最大弯曲角度")]
    public float maxPipAngle = 115f;
    [Tooltip("指尖关节（DIP）最大弯曲角度")]
    public float maxDipAngle = 85f;
    [Tooltip("允许往手背方向反折的最大角度")]
    public float maxBackwardAngle = 5f;

    private readonly Vector3[] _directions = new Vector3[LandmarkIds.Count];
    private readonly Vector3[] _fingerAxis = new Vector3[4];
    private readonly float[] _pipAngle = new float[4];
    private readonly float[] _dipAngle = new float[4];
    private readonly bool[] _fingerValid = new bool[4];
    private float _flexSign;

    private static readonly string[][] _FingerKeywords =
    {
      new[] { "thumb" }, new[] { "index" }, new[] { "middle" }, new[] { "ring" }, new[] { "little", "pinky" },
    };
    private static readonly string[] _SegmentKeywords = { "metacarpal", "proximal", "intermediate", "distal", "tip" };

    private Material _materialInstance;
    private float _visibility;
    private readonly Vector3[] _scaled = new Vector3[LandmarkIds.Count];

    private void Awake()
    {
      _materialInstance = material != null
        ? new Material(material)
        : new Material(Shader.Find("Standard")) { color = HandColorizer.NeutralColor };

      Prepare(left, "左手");
      Prepare(right, "右手");

      if (colorizer == null)
      {
        colorizer = GetComponentInParent<HandColorizer>();
      }
      if (colorizer != null)
      {
        colorizer.Bind(_materialInstance);
      }
    }

    /// <summary>在模型还处于默认姿态时，记录每节骨骼“朝向”与“实际旋转”之间的差。</summary>
    private void Prepare(Rig rig, string label)
    {
      if (rig.root == null)
      {
        return;
      }
      if (rig.links.Count == 0)
      {
        AutoFill(rig);
      }
      // 从手腕往指尖的顺序设置，父骨骼先于子骨骼
      rig.links.Sort((a, b) => a.from.CompareTo(b.from));

      var indexBase = FindBone(rig, LandmarkIds.IndexMcp);
      var middleBase = FindBone(rig, LandmarkIds.MiddleMcp);
      var pinkyBase = FindBone(rig, LandmarkIds.PinkyMcp);
      if (rig.wrist == null || indexBase == null || middleBase == null || pinkyBase == null)
      {
        Debug.LogWarning($"[RehabDemo] {label}模型的骨骼没有识别出来（需要手腕和食指、中指、小指的指根骨骼），这只手不会显示。骨骼名字里应包含 wrist、index、middle、ring、little/pinky、thumb 等关键词。");
        rig.root.gameObject.SetActive(false);
        return;
      }

      var wristPosition = rig.wrist.position;
      var wristRotation = rig.wrist.rotation;
      var restNormal = Vector3.Cross(indexBase.position - wristPosition, pinkyBase.position - wristPosition).normalized;
      var restSide = (pinkyBase.position - indexBase.position).normalized;
      var restForward = middleBase.position - wristPosition;

      rig.wristOffset = Quaternion.Inverse(Quaternion.LookRotation(restForward, restNormal)) * wristRotation;
      rig.restPalmLength = restForward.magnitude;
      rig.initialScale = rig.root.localScale;

      var handled = new HashSet<Transform> { rig.wrist };
      foreach (var link in rig.links)
      {
        link.valid = false;
        link.tip = null;
        if (link.bone == null)
        {
          continue;
        }
        var end = link.end;
        if (end == null)
        {
          end = FindBone(rig, link.to);
        }
        if (end == null && link.bone.childCount > 0)
        {
          end = link.bone.GetChild(0);
        }
        if (end == null || (end.position - link.bone.position).sqrMagnitude < 1e-10f)
        {
          continue;
        }
        var restDirection = end.position - link.bone.position;
        link.offset = Quaternion.Inverse(Quaternion.LookRotation(restDirection, Hint(link, restNormal, restSide))) * link.bone.rotation;
        link.valid = true;
        handled.Add(link.bone);

        // 链表已按 from 排序，所以前一节（如果有）已经处理过
        link.parent = rig.links.Find(l => l.valid && l != link && l.to == link.from);
        var parentPosition = link.parent != null ? link.parent.bone.position : wristPosition;
        var parentRotation = link.parent != null ? link.parent.bone.rotation : wristRotation;
        link.localPosition = Quaternion.Inverse(parentRotation) * (link.bone.position - parentPosition);

        if (FindBone(rig, link.to) != end)
        {
          link.tip = end;
          link.tipLocalRotation = Quaternion.Inverse(link.bone.rotation) * end.rotation;
          link.tipLocalPosition = Quaternion.Inverse(link.bone.rotation) * (end.position - link.bone.position);
          handled.Add(end);
        }
      }

      rig.followers = new List<Follower>();
      foreach (var meshRenderer in rig.root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
      {
        foreach (var bone in meshRenderer.bones)
        {
          if (bone != null && handled.Add(bone))
          {
            rig.followers.Add(new Follower
            {
              bone = bone,
              localPosition = Quaternion.Inverse(wristRotation) * (bone.position - wristPosition),
              localRotation = Quaternion.Inverse(wristRotation) * bone.rotation,
            });
          }
        }

        var materials = meshRenderer.sharedMaterials;
        for (var i = 0; i < materials.Length; i++)
        {
          materials[i] = _materialInstance;
        }
        meshRenderer.sharedMaterials = materials;
        // 骨骼会被直接移到目标位置，离模型原点很远，按默认包围盒会被错误地剔除
        meshRenderer.updateWhenOffscreen = true;
      }

      rig.root.gameObject.SetActive(false);
      rig.ready = true;
    }

    /// <summary>
    ///   LookRotation 的参考方向。四指绕手掌的横向轴弯曲，这个方向在握拳时也始终垂直于手指；拇指用手掌法线。
    /// </summary>
    private static Vector3 Hint(BoneLink link, Vector3 normal, Vector3 side) => link.from < LandmarkIds.IndexMcp ? normal : side;

    private static Transform FindBone(Rig rig, int from)
    {
      foreach (var link in rig.links)
      {
        if (link.from == from && link.bone != null)
        {
          return link.bone;
        }
      }
      return null;
    }

    /// <summary>按名字关键词（thumb、index、middle、ring、little/pinky）自动填写骨骼对应表。</summary>
    public static void AutoFill(Rig rig)
    {
      rig.links.Clear();
      if (rig.root == null)
      {
        return;
      }
      var all = rig.root.GetComponentsInChildren<Transform>(true);

      if (rig.wrist == null)
      {
        rig.wrist = Array.Find(all, t => t.name.ToLowerInvariant().Contains("wrist"));
      }

      for (var finger = 0; finger < _FingerKeywords.Length; finger++)
      {
        // 同一根手指的骨骼，从指根到指尖排序：先按层级深度，深度相同（平级骨骼）再按名字里的节段关键词
        var chain = new List<Transform>();
        foreach (var t in all)
        {
          var name = t.name.ToLowerInvariant();
          if (t.GetComponent<Renderer>() == null && Array.Exists(_FingerKeywords[finger], k => name.Contains(k)))
          {
            chain.Add(t);
          }
        }
        chain.Sort((a, b) =>
        {
          var byDepth = Depth(a, rig.root).CompareTo(Depth(b, rig.root));
          return byDepth != 0 ? byDepth : SegmentRank(a).CompareTo(SegmentRank(b));
        });
        if (chain.Count == 0)
        {
          continue;
        }

        int firstJoint;
        if (finger == 0)
        {
          // 拇指：掌骨、近节、远节，对应 MediaPipe 的 1→2、2→3、3→4
          firstJoint = LandmarkIds.ThumbCmc;
        }
        else
        {
          // 四指：掌骨（如果有）没有对应的关节，随手腕一起动；近节、中节、远节对应 MCP→PIP、PIP→DIP、DIP→TIP
          if (chain[0].name.ToLowerInvariant().Contains("metacarpal") || chain.Count >= 5)
          {
            chain.RemoveAt(0);
          }
          firstJoint = LandmarkIds.IndexMcp + (finger - 1) * 4;
        }
        for (var k = 0; k < 3 && k < chain.Count; k++)
        {
          rig.links.Add(new BoneLink
          {
            bone = chain[k],
            end = k + 1 < chain.Count ? chain[k + 1] : null,
            from = firstJoint + k,
            to = firstJoint + k + 1,
          });
        }
      }

      if (rig.wrist == null)
      {
        var middle = FindBone(rig, LandmarkIds.MiddleMcp);
        rig.wrist = middle != null ? middle.parent : null;
      }
    }

    private static int SegmentRank(Transform t)
    {
      var name = t.name.ToLowerInvariant();
      var rank = Array.FindIndex(_SegmentKeywords, k => name.Contains(k));
      return rank < 0 ? _SegmentKeywords.Length : rank;
    }

    private static int Depth(Transform t, Transform root)
    {
      var depth = 0;
      while (t != null && t != root)
      {
        depth++;
        t = t.parent;
      }
      return depth;
    }

    [ContextMenu("Auto Fill Bones")]
    private void AutoFillFromMenu()
    {
      AutoFill(left);
      AutoFill(right);
    }

    private void Update()
    {
      var pose = HandFrameBus.Current;
      var tracked = pose.tracked && pose.joints != null;
      _visibility = fadeTime > 0f ? Mathf.MoveTowards(_visibility, tracked ? 1f : 0f, Time.deltaTime / fadeTime) : (tracked ? 1f : 0f);

      // 左右手各用各的模型；只配了一只时两种情况都用它
      var rig = pose.isLeft ? left : right;
      var other = pose.isLeft ? right : left;
      if (!rig.ready)
      {
        (rig, other) = (other, rig);
      }

      var visible = rig.ready && _visibility > 0.001f && pose.joints != null;
      SetActive(rig, visible);
      SetActive(other, false);
      if (!visible)
      {
        // “朝手心”的方向和左右手有关，手消失后重新判断
        _flexSign = 0f;
        return;
      }

      // 出现、消失时整只手朝手掌中心缩放
      var amount = Mathf.SmoothStep(0f, 1f, _visibility);
      var source = pose.joints;
      var center = (source[LandmarkIds.Wrist] + source[LandmarkIds.IndexMcp] + source[LandmarkIds.PinkyMcp]) / 3f;
      for (var i = 0; i < _scaled.Length; i++)
      {
        _scaled[i] = center + (source[i] - center) * amount;
      }
      var j = _scaled;

      var wristPosition = j[LandmarkIds.Wrist];
      var forward = j[LandmarkIds.MiddleMcp] - wristPosition;
      var normal = Vector3.Cross(j[LandmarkIds.IndexMcp] - wristPosition, j[LandmarkIds.PinkyMcp] - wristPosition).normalized;
      var side = (j[LandmarkIds.PinkyMcp] - j[LandmarkIds.IndexMcp]).normalized;
      if (forward.sqrMagnitude < 1e-10f || normal == Vector3.zero || side == Vector3.zero)
      {
        return;
      }

      // 整体缩放到手掌长度与追踪结果一致（影响蒙皮的粗细），再逐个摆放骨骼
      var ratio = forward.magnitude / rig.restPalmLength;
      rig.root.localScale = rig.initialScale * ratio;

      var wristRotation = Quaternion.LookRotation(forward, normal) * rig.wristOffset;
      rig.wrist.SetPositionAndRotation(wristPosition, wristRotation);

      foreach (var follower in rig.followers)
      {
        follower.bone.SetPositionAndRotation(wristPosition + wristRotation * (follower.localPosition * ratio), wristRotation * follower.localRotation);
      }

      for (var i = 0; i < LandmarkIds.Count - 1; i++)
      {
        _directions[i] = j[i + 1] - j[i];
      }
      if (constrainFingers)
      {
        ConstrainFingers(side);
      }

      foreach (var link in rig.links)
      {
        if (!link.valid)
        {
          continue;
        }
        // 方向取自追踪结果；位置按模型自己的骨骼长度从上一节推出来，这样蒙皮不会被拉伸或压扁
        var direction = link.to == link.from + 1 ? _directions[link.from] : j[link.to] - j[link.from];
        link.rotation = direction.sqrMagnitude > 1e-10f
          ? Quaternion.LookRotation(direction, Hint(link, normal, side)) * link.offset
          : link.bone.rotation;
        var parentPosition = link.parent != null ? link.parent.position : wristPosition;
        var parentRotation = link.parent != null ? link.parent.rotation : wristRotation;
        link.position = parentPosition + parentRotation * (link.localPosition * ratio);
        link.bone.SetPositionAndRotation(link.position, link.rotation);
        if (link.tip != null)
        {
          link.tip.SetPositionAndRotation(link.position + link.rotation * (link.tipLocalPosition * ratio), link.rotation * link.tipLocalRotation);
        }
      }
    }

    /// <summary>
    ///   把四指的方向修正到人手做得出来的范围内。手指被挡住或手侧对摄像头时，追踪结果会把指节估到
    ///   侧弯、反折等不可能的位置，直接照搬会让模型扭曲。
    ///   修正内容：指根左右张开的幅度有上限；中节和远节只能在这根手指自己的屈伸平面内弯，并且只能朝手心一侧弯、不超过上限。
    /// </summary>
    private void ConstrainFingers(Vector3 side)
    {
      var maxSplay = Mathf.Sin(maxSplayAngle * Mathf.Deg2Rad);
      var total = 0f;

      for (var finger = 0; finger < 4; finger++)
      {
        var mcp = LandmarkIds.IndexMcp + finger * 4;

        // 近节：保留屈伸，限制左右偏
        var proximal = _directions[mcp].normalized;
        var lateral = Mathf.Clamp(Vector3.Dot(proximal, side), -maxSplay, maxSplay);
        var inPlane = proximal - Vector3.Dot(proximal, side) * side;
        if (inPlane.sqrMagnitude > 1e-8f)
        {
          proximal = inPlane.normalized * Mathf.Sqrt(1f - lateral * lateral) + side * lateral;
        }

        // 这根手指的屈伸轴：垂直于近节的那条横向轴
        var axis = side - Vector3.Dot(side, proximal) * proximal;
        if (proximal == Vector3.zero || axis.sqrMagnitude < 1e-8f)
        {
          _fingerValid[finger] = false;
          continue;
        }
        axis.Normalize();

        var middle = Vector3.ProjectOnPlane(_directions[mcp + 1], axis);
        var distal = Vector3.ProjectOnPlane(_directions[mcp + 2], axis);
        _fingerValid[finger] = true;
        _fingerAxis[finger] = axis;
        _directions[mcp] = proximal;
        _pipAngle[finger] = middle.sqrMagnitude > 1e-10f ? Vector3.SignedAngle(proximal, middle, axis) : 0f;
        _dipAngle[finger] = middle.sqrMagnitude > 1e-10f && distal.sqrMagnitude > 1e-10f ? Vector3.SignedAngle(middle, distal, axis) : 0f;
        total += _pipAngle[finger] + _dipAngle[finger];
      }

      // 哪个旋转方向是“朝手心”：手指明显弯着的时候，以多数关节的方向为准并记住
      if (Mathf.Abs(total) > 60f)
      {
        _flexSign = Mathf.Sign(total);
      }

      for (var finger = 0; finger < 4; finger++)
      {
        if (!_fingerValid[finger])
        {
          continue;
        }
        var mcp = LandmarkIds.IndexMcp + finger * 4;
        var pip = ClampFlexion(_pipAngle[finger], maxPipAngle);
        var dip = ClampFlexion(_dipAngle[finger], maxDipAngle);
        _directions[mcp + 1] = Quaternion.AngleAxis(pip, _fingerAxis[finger]) * _directions[mcp];
        _directions[mcp + 2] = Quaternion.AngleAxis(dip, _fingerAxis[finger]) * _directions[mcp + 1];
      }
    }

    private float ClampFlexion(float angle, float max)
    {
      // 还不知道哪边是手心时，只限制幅度
      if (_flexSign == 0f)
      {
        return Mathf.Clamp(angle, -max, max);
      }
      return Mathf.Clamp(angle * _flexSign, -maxBackwardAngle, max) * _flexSign;
    }

    private static void SetActive(Rig rig, bool active)
    {
      if (rig.ready && rig.root.gameObject.activeSelf != active)
      {
        rig.root.gameObject.SetActive(active);
      }
    }

    private void OnDestroy()
    {
      Destroy(_materialInstance);
    }
  }
}
