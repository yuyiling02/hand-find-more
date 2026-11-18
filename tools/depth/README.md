# 单目深度实验控制

在项目根目录运行 `tools/depth/setup.ps1 -Python <Python 3.10+ executable>`。
脚本创建独立环境、下载官方 Small 权重并校验 Hugging Face LFS SHA256。
源码固定到 `a561b849ebae10a6f5ef49e26c83cbbcd36c71bf`。
模型来源和校验值记录于 `models/provenance.json`，官方许可证保留在 `vendor/LICENSE`。

Unity 播放 RehabDemo 后，按 F1 展开面板，选择「模型深度」。
等待就绪，收手点击校准，按提示伸手后再点击校准。
左右移动由图像位置控制，伸手向桌面远端移动，收手向近端移动。
按钮可恢复原有控制或重新启动推理服务。

运行自动化测试使用 `.venv/Scripts/python.exe -m unittest discover -s tools/depth`，从项目根目录调用时请补全解释器路径。

当前进程默认使用 CUDA FP32、266 输入尺寸。摄像头只由 Unity 打开。
打包后的程序需要将整个 `tools/depth` 运行目录放在可执行文件旁，或使用编辑器运行。
关闭 Unity 会结束其启动的 Python 子进程。

人工验收需记录静止十秒、伸收十次、原地握拳翻掌、背景经过及各视角取放杯子的结果。
性能目标为五分钟运行深度更新至少 10 Hz、渲染至少 30 FPS、采集到位置更新 P95 不超过 150 ms。
调试面板显示图像入队、深度返回、前后目标、当前行程与桌面 Z。入队和返回持续增加、结果年龄在 250 毫秒内时，可以用伸手和收手比较前后目标与桌面 Z 的变化。桌面 Z 是虚拟场景坐标，手掌深度是模型的相对深度值。
