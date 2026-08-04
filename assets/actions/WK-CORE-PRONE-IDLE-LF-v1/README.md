# WK-CORE-PRONE-IDLE-LF v1

- 当前状态：`runtime-candidate`
- 关键帧状态：`approved-keyframes`（主人于 2026-08-05 明确批准）
- 技术检查：通过（1024×1024、sRGB、透明 PNG、逐文件 SHA-256）
- 身份审核：通过，绑定 `wukong-current-adult-v1`
- 运行帧：12 帧，8 FPS，单循环 1500 ms
- 补帧方法：仅在相邻批准关键帧之间进行预乘 Alpha 的确定性插值
- 闭环：`frame-005.png` 与 `frame-001.png` 完全一致

## 运行规格

- 画布：1024×1024 RGBA / sRGB
- 地面基线：y=861 px
- 运行锚点：批准帧 001/002/003/004 原文件分别位于运行帧 001/004/007/010
- 重复闭环帧 005 不进入运行帧列表，避免循环处停顿
- 预览一：`previews/loop-actual-speed-v1.gif`
- 预览二：`previews/entry-loop-exit-seam-v1.gif`

## 使用限制

当前仍不得接入正式运行时。要提升为 `runtime-approved`，还需主人完成预览审核，并在真实桌宠渲染器中验证透明边缘、桌面比例、内存、方向和中断行为。

第二个预览只验证“进入锚点保持 → 当前循环 → 退出锚点保持”。由于仓库尚无站立、趴下或起身动作素材，它不代表相邻动作切换已经通过。
