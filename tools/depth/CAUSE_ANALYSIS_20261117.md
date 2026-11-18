# 前后位移无效：原因分析（2026-11-17）

本轮仅分析；未修改控制、模型或渲染代码。工作区原有未提交改动保留。

## 确定事实

1. `HandFrameBus.cs:159-182` 默认用图像掌心 Y 映射桌面 Z；只有选中相应模式且完成校准才覆盖。场景范围为 -0.50 至 0.10，因此未启用伸收时 `Z = 0.10 - 0.60 * palmY`。掌心保持画面 y=0.5 时，Z 始终为 -0.20，不使用相机距离。
2. `HandFrameBus.cs:27-35` 的三模式互斥。选择 PalmRange 会关闭 DepthController，未做几何与模型深度融合。模式和端点校准没有持久化，新运行实例默认 ScreenVertical。
3. 重新统计项目现存三份原始录制，三份均为 PalmRange 且全程 calibrated=0。最长录制 `forward-20261117-031658-575.csv`：4298 行、30006 ms、901 个新手帧；target 和 controller_position 全程 0.5，depth_valid 全程 0；table_z 范围 -0.426878 至 -0.130451。871 个新且 tracked 的记录中，table_position 与 1-palm_y 相关系数 0.8952。这与画面上下回退路径一致。
4. 以上录制为 11 月 17 日凌晨已有记录，不能证明用户最新一次测试的模式、校准或有效性；也不能从 calibrated=0 判断究竟从未完成校准还是中途被清除。旧版本 status 存在过时提示问题，不用它推断本次姿态。
5. `HandFrameBus.cs:188-195` 把 world landmarks 减去 palmLocal，仅保留相对手形；`207-210` 再加基准位置。MediaPipe world landmarks 原点本就在手的几何中心，不是相机；有 world.z 不等于得到手到相机距离。
6. `SkinnedHandView.cs:375,389` 使用总线腕关节世界位置，`CapsuleHandView.cs:139` 使用关节世界位置。未发现末端强行清零 Z。固定的是手掌高度 Y，而不是桌面前后 Z。

## 模型接入的结构性缺口

`models/provenance.json` 确认模型是 Depth-Anything-V2-Small 相对深度权重。`server.py:79-80` 每帧独立预测整图并取掌心中位数。`DepthController.cs:332-333` 用 `(当前值-收手值)/(伸手值-收手值)` 映射行程。这依赖后续帧输出与校准帧尺度可比，但现有实现没有稳定参考物、跨帧尺度对齐或时序模型。

这是工程风险，不是本次已实测出的深度漂移。相对深度仍可能用于相对伸收，不必先得到厘米值；关键是要验证重复动作时的方向、幅度和跨帧稳定性。不能声称只换 metric 模型或加滤波就一定解决。

本地模型 `infer_image()` 直接返回 raw depth，没有每帧输出 min-max 归一化，不能将问题归因为这类归一化。

## 会冻结或退出控制的条件（待本次录制确认）

- 模型结果无效、年龄超过 250 ms、手别不符或超过一秒无有效结果：MapZ 返回当前位置（DepthController.cs:317-324）。
- 有效结果的左右手标签改变：模型校准会被清除（DepthController.cs:241）；PalmRange 同样有换手重置。
- PalmRange 侧掌、过期或几何质量不满足阈值：已校准时保持上一次 Z。采集要求倾角不超过 45°，运行时超过 60°暂停、低于 50°恢复。
- 两倍增益会令校准进度前后各四分之一范围进入端点饱和。它不能解释整段未校准记录，增加增益也不会让回退路径开始使用深度。

## 运行状态与验证边界

- 活跃 Unity 项目路径与此工作区一致；本轮检查时未见 Python 深度服务进程。这只描述检查时刻，不证明之前没有运行。
- Unity 窗口最小化，未读取到当前 F1 模式；没有修改窗口内的模式或重新校准。
- 独立检查确认回调图像、关键点和 timestamp 由同一回调提交，未发现明显代码级帧错配。
- 本轮未进行真实摄像头伸收验收；未改业务代码，未重跑无关编译测试。

## 最短定位流程

在本次使用的模式完成两端校准，确认界面显示已启用，记录 30 秒（静止后保持画面高度伸收）。首先核对 effective_mode/calibrated；再比较 raw depth 或 palm_q、target、table_z。输入不随动作变化则查估计信号；输入变化而 target 不动则查校准/有效性/限幅；target 变化而 table_z 不动才查控制输出。本轮原始统计保存于 `Logs/ForwardControl/analysis-20261117.json`。

官方定义核对：
- MediaPipe 坐标原点：https://developers.google.com/edge/mediapipe/solutions/vision/hand_landmarker
- Depth Anything V2 相对深度与视频时序说明：https://github.com/DepthAnything/Depth-Anything-V2

## 第一阶段无法完成：实时状态与独立复现

用户补充无论靠近、远离或静止都不能进入第 2 步。本轮打开 Unity 当前窗口后，现场显示识别约 30 FPS、校准通知为『检测到左右手切换，校准已清除』。这确认标签切换重置分支确实执行过。30 FPS 包括无手结果；当前观察时用户没有举手，因此不把此刻结果年龄大当作摄像头故障证据。

使用当前 PalmRangeController 业务源码与实际 Unity 数学 DLL 的独立探针（tools/depth/.bootstrap/CalibrationGateProbe.cs）复现：
- 正常姿态采集 10 帧：Stage=0，Collecting=True，samples=10。
- 仅一帧 isLeft 变化：Stage=0，Collecting=False，samples=0，通知变为左右手切换清除。
- 下一帧恢复原标签，随后稳定 5 秒：仍未采集、仍是第 1 步。
- 0°/40° 对照可以完成第 1 步；50° 采集前有效，但采集时被 45° 门槛拒绝全部样本。
- 无手超时先生成『有效样本不足』，随后同帧 Pause 又覆盖成『等待追踪手掌』，失败原因会丢失。

具体入口 PalmRangeController.cs:109 在年龄、几何有效性之前按单帧手别 ResetCalibration。63-75 行清空状态且取消 Collecting，恢复后不会自动继续。Runner 271-301 保存 handednessScore 但不以置信度/连续帧确认身份。现场是否为单手误分类还是实际另一只手入镜仍待用户确认，但重置路径及其后果已经验证。没有证据表明 UI 必然误触重新校准；未改业务代码。


### 用户确认补充
用户明确确认测试时始终只有同一只手。因此现场『左右手切换』不是实际换手操作，而是同一只手的分类标签变化被当前状态机当作换手；该重置分支是本轮已定位的第一阶段校准阻断原因。先前待区分单手/换手这一项已澄清。
