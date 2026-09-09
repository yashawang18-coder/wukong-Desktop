# 吃饭、喝水素材：v5 接缝色彩修复候选

沿用已确认的中间档暖麦芽金。本次针对吃饭低头结束→循环开始的暖色跳变。
从相邻锚点计算一组统一的Lab色度残差，用连续smoothstep曲线分配到低头段。
基础调色映射未改变，也没有逐帧分别拟合；只修改eat-lower第2～6帧的金色毛发。
其余43张PNG（包含全部喝水帧及三张已确认校样）保持文件字节一致。

48张1024×1024 RGBA PNG。动作、位置、比例、Alpha、碗、食物、水及序列配置保持不变。
不缩放、不锐化、不模糊、不重绘、不超分；保留源图毛发纹理和真实分辨率。
原透明基线上的少量边缘杂色不属于本次接缝修复范围。

序列：吃饭103个槽位（12.875秒），喝水91个槽位（11.375秒），每槽125毫秒。
加载sequence-manifest.json指定的frames与duration_ms；不另加旧包uniform_scale_percent。
运行时启用标志仍为false，未替换正式素材，未提交、推送或运行Windows EXE。

review里的动态PNG为无损全彩APNG。eat-entry-before-after.png左右同步对比入口过程。
eat-seam-before-after-native.png：上排修复前，下排修复后；每排左低头末帧、右循环首帧。
all-48-native-contact.png包含全量独立帧。数值报告在QA/seam-repair.json。

复现：python3 tools/repair_eat_seam_v5.py --workspace /path/to/food-water-recolor-work
工作目录需保留v4全量素材与full-coat-v4-baseline。依赖Pillow、NumPy、SciPy。
