using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace RehabDemo.EditorTools
{
  /// <summary>菜单 Tools/RehabDemo/Build Scene：一键生成演示场景，不需要手动拖组件。</summary>
  public static class RehabSceneBuilder
  {
    private const string _ScenePath = "Assets/RehabDemo/Scenes/RehabDemo.unity";
    private const string _MaterialDir = "Assets/RehabDemo/Materials";
    private const string _ModelDir = "Assets/RehabDemo/Models";
    private const string _ConfigPath ="Assets/RehabDemo/ExerciseConfig.asset";

    [MenuItem("Tools/RehabDemo/Build Scene")]
    public static void BuildScene()
    {
      if (EditorApplication.isPlaying)
      {
        EditorUtility.DisplayDialog("RehabDemo", "请先停止 Play，再生成场景。", "好");
        return;
      }
      if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
      {
        return;
      }

      var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
      Directory.CreateDirectory(_MaterialDir);
      Directory.CreateDirectory(Path.GetDirectoryName(_ScenePath));
      AssetDatabase.Refresh();

      BuildEnvironment();
      BuildCameraAndLight();
      var bus = BuildTracking();
      var colorizer = BuildHand(bus);
      BuildGame(colorizer);

      EditorSceneManager.SaveScene(scene, _ScenePath);
      AssetDatabase.SaveAssets();
      Debug.Log($"[RehabDemo] 场景已生成：{_ScenePath}，现在可以点 Play。");
    }

    // 桌面上表面在 y = 0，桌面中心为原点
    private static void BuildEnvironment()
    {
      var root = new GameObject("Environment").transform;

      var tableMat = GetMaterial("Table", Hex("#F3E6C4"), 0.1f);
      var woodMat = GetMaterial("Wood", Hex("#D9B98A"), 0.15f);
      var wallMat = GetMaterial("Wall", Hex("#E9DFCF"), 0.05f);
      var floorMat = GetMaterial("Floor", Hex("#8C7B6B"), 0.05f);

      CreateBox("Table", root, new Vector3(0f, -0.02f, 0f), new Vector3(1.2f, 0.04f, 0.8f), tableMat);
      // 桌子远端的矮柜和墙，只做氛围
      CreateBox("Cabinet", root, new Vector3(0f, -0.375f, 0.50f), new Vector3(3f, 0.75f, 0.18f), woodMat);
      CreateBox("Wall", root, new Vector3(0f, 0f, 0.62f), new Vector3(4f, 1.5f, 0.05f), wallMat);
      CreateBox("Floor", root, new Vector3(0f, -0.775f, 0f), new Vector3(6f, 0.05f, 4f), floorMat);

      // 矮柜上的“书”：x 位置、宽、高、绕 z 轴的倾斜角
      var books = new[]
      {
        (x: -0.50f, w: 0.035f, h: 0.13f, tilt: 0f, color: "#D9534F"),
        (x: -0.46f, w: 0.030f, h: 0.11f, tilt: 0f, color: "#3B7DD8"),
        (x: -0.42f, w: 0.040f, h: 0.14f, tilt: 0f, color: "#F0AD4E"),
        (x: -0.37f, w: 0.030f, h: 0.12f, tilt: -12f, color: "#5CB85C"),
        (x: 0.05f, w: 0.035f, h: 0.12f, tilt: 0f, color: "#8E6BBF"),
        (x: 0.09f, w: 0.030f, h: 0.14f, tilt: 0f, color: "#E8793A"),
        (x: 0.36f, w: 0.040f, h: 0.11f, tilt: 10f, color: "#3B7DD8"),
        (x: 0.41f, w: 0.030f, h: 0.13f, tilt: 0f, color: "#D9534F"),
        (x: 0.45f, w: 0.035f, h: 0.12f, tilt: 0f, color: "#4FB8B0"),
      };
      for (var i = 0; i < books.Length; i++)
      {
        var b = books[i];
        var mat = GetMaterial("Book" + b.color.TrimStart('#'), Hex(b.color), 0.2f);
        var book = CreateBox($"Book{i}", root, new Vector3(b.x, b.h / 2f, 0.50f), new Vector3(b.w, b.h, 0.09f), mat);
        book.transform.rotation = Quaternion.Euler(0f, 0f, b.tilt);
      }
    }

    private static void BuildCameraAndLight()
    {
      var cameraGo = GameObject.Find("Main Camera");
      var camera = cameraGo.GetComponent<Camera>();
      cameraGo.transform.position = new Vector3(0f, 0.75f, -0.55f);
      cameraGo.transform.LookAt(new Vector3(0f, 0f, 0.05f));
      camera.fieldOfView = 45f;
      camera.nearClipPlane = 0.05f;
      camera.farClipPlane = 20f;
      camera.clearFlags = CameraClearFlags.SolidColor;
      camera.backgroundColor = Hex("#E9DFCF");

      var light = GameObject.Find("Directional Light").GetComponent<Light>();
      light.transform.rotation = Quaternion.Euler(55f, -25f, 0f);
      light.color = new Color(1f, 0.96f, 0.88f);
      light.intensity = 0.9f;
      light.shadows = LightShadows.Soft;
      light.shadowStrength = 0.45f;

      RenderSettings.ambientMode = AmbientMode.Flat;
      RenderSettings.ambientLight = new Color(0.32f, 0.31f, 0.29f);
    }

    private static HandFrameBus BuildTracking()
    {
      var go = new GameObject("HandTracking");
      var runner = go.AddComponent<HandTrackingRunner>();
      var bus = go.AddComponent<HandFrameBus>();
      var debugPanel = go.AddComponent<HandDebugPanel>();
      // 识别帧率在调试面板里能看到，不再每秒刷一行 Console
      runner.logDetectionFps = false;
      bus.runner = runner;
      debugPanel.runner = runner;
      return bus;
    }

    private static void BuildGame(HandColorizer colorizer)
    {
      // 已有的配置不覆盖，保留用户调过的参数
      var config = AssetDatabase.LoadAssetAtPath<ExerciseConfig>(_ConfigPath);
      if (config == null)
      {
        config = ScriptableObject.CreateInstance<ExerciseConfig>();
        AssetDatabase.CreateAsset(config, _ConfigPath);
      }

      var exercisesGo = new GameObject("Exercises");
      var exercises = new ExerciseBase[]
      {
        exercisesGo.AddComponent<PinchExercise>(),
        exercisesGo.AddComponent<FistExercise>(),
        exercisesGo.AddComponent<FingerFlexExercise>(),
      };
      foreach (var exercise in exercises)
      {
        exercise.config = config;
        exercise.colorizer = colorizer;
      }

      // MENU 按钮在桌面右侧，卡片从它的位置往远端排开
      var menuPosition = new Vector3(0.30f, 0.01f, 0.0f);
      var menuButtonGo = new GameObject("MenuButton");
      menuButtonGo.transform.position = menuPosition;
      var menuButton = menuButtonGo.AddComponent<DwellButton>();
      var menuPanelGo = new GameObject("MenuPanel");
      menuPanelGo.transform.position = menuPosition;
      var menuPanel = menuPanelGo.AddComponent<MenuPanel>();

      var flowGo = new GameObject("GameFlow");
      flowGo.AddComponent<FingertipPointer>();
      var flow = flowGo.AddComponent<GameFlow>();
      flow.hud = flowGo.AddComponent<ProgressHUD>();
      flow.celebrateFx = flowGo.AddComponent<CelebrateFx>();
      flow.sfx = flowGo.AddComponent<Sfx>();
      flow.colorizer = colorizer;
      flowGo.AddComponent<AngleLabel>();
      flowGo.AddComponent<HandLostHint>();
      flow.exercises = exercises;
      flow.menuButton = menuButton;
      flow.menuPanel = menuPanel;
    }

    private static HandColorizer BuildHand(HandFrameBus bus)
    {
      var handMat = GetMaterial("Hand", HandColorizer.NeutralColor, 0.4f);
      handMat.EnableKeyword("_EMISSION");
      handMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
      handMat.SetColor("_EmissionColor", HandColorizer.NeutralColor * 0.15f);
      EditorUtility.SetDirty(handMat);

      // 颜色控制放在父对象上，这样切换两种手时它始终在运行
      var root = new GameObject("HandVisual");
      var colorizer = root.AddComponent<HandColorizer>();
      var switcher = root.AddComponent<HandViewSwitcher>();

      var capsuleGo = new GameObject("CapsuleHand");
      capsuleGo.transform.SetParent(root.transform, false);
      var capsule = capsuleGo.AddComponent<CapsuleHandView>();
      capsule.bus = bus;
      capsule.colorizer = colorizer;
      capsule.material = handMat;
      switcher.capsuleHand = capsuleGo;
      // 带蒙皮的手是可选的：工程里找得到手模型才生成
      var leftModel = FindHandModel("left");
      var rightModel = FindHandModel("right");
      if (leftModel == null && rightModel == null)
      {
        Debug.Log($"[RehabDemo] 没有找到手模型（{_ModelDir} 下文件名同时包含 hand 和 left/right 的 .glb 或 .fbx），只生成胶囊手。");
        return colorizer;
      }

      var skinnedGo = new GameObject("SkinnedHand");
      skinnedGo.transform.SetParent(root.transform, false);
      var skinned = skinnedGo.AddComponent<SkinnedHandView>();
      skinned.bus = bus;
      skinned.colorizer = colorizer;
      skinned.material = handMat;
      skinned.left.root = InstantiateModel(leftModel, skinnedGo.transform);
      skinned.right.root = InstantiateModel(rightModel, skinnedGo.transform);
      SkinnedHandView.AutoFill(skinned.left);
      SkinnedHandView.AutoFill(skinned.right);
      switcher.skinnedHand = skinnedGo;
      switcher.mode = HandViewMode.Skinned;
      skinnedGo.SetActive(true);
      capsuleGo.SetActive(false);
      Debug.Log($"[RehabDemo] 已生成带蒙皮的手：左手 {skinned.left.links.Count} 节骨骼，右手 {skinned.right.links.Count} 节骨骼（正常应为各 15 节）。运行时按 H 可在两种手之间切换。");
      return colorizer;
    }

    /// <summary>找手模型：文件名同时包含 hand 和 left/right 的 .glb、.gltf 或 .fbx，优先用 RehabDemo/Models 下的。</summary>
    private static GameObject FindHandModel(string side)
    {
      GameObject fallback = null;
      foreach (var guid in AssetDatabase.FindAssets("hand t:GameObject", new[] { "Assets" }))
      {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if ((extension != ".glb" && extension != ".gltf" && extension != ".fbx") || !name.Contains("hand") || !name.Contains(side))
        {
          continue;
        }
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null || asset.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
        {
          continue;
        }
        if (path.StartsWith(_ModelDir))
        {
          return asset;
        }
        fallback ??= asset;
      }
      return fallback;
    }

    private static Transform InstantiateModel(GameObject model, Transform parent)
    {
      if (model == null)
      {
        return null;
      }
      var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
      instance.transform.SetParent(parent, false);
      return instance.transform;
    }

    private static GameObject CreateBox(string name, Transform parent, Vector3 center, Vector3 size, Material material)
    {
      var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
      go.name = name;
      Object.DestroyImmediate(go.GetComponent<Collider>());
      go.transform.SetParent(parent, false);
      go.transform.position = center;
      go.transform.localScale = size;
      go.GetComponent<MeshRenderer>().sharedMaterial = material;
      return go;
    }

    /// <summary>取得（没有就创建）Assets/RehabDemo/Materials 下的材质，重复生成场景时复用同一个文件。</summary>
    private static Material GetMaterial(string name, Color color, float smoothness)
    {
      var path = $"{_MaterialDir}/{name}.mat";
      var material = AssetDatabase.LoadAssetAtPath<Material>(path);
      if (material == null)
      {
        material = new Material(Shader.Find("Standard"));
        AssetDatabase.CreateAsset(material, path);
      }
      material.color = color;
      material.SetFloat("_Glossiness", smoothness);
      EditorUtility.SetDirty(material);
      return material;
    }

    private static Color Hex(string hex)
    {
      ColorUtility.TryParseHtmlString(hex, out var color);
      return color;
    }
  }
}
