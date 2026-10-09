# 行为策略归一与便携相册

## 范围与边界

本轮在现有 `BehaviorRequest -> eligibility -> arbitration -> lifecycle -> PetStateReducer` 上归一策略，不重绘素材，不改变 PNG、manifest 审批字段、模型权限或现有播放序列。开始时已有的面板、姿态驻留和迁移文档修改保留。

## 六项实现

1. **唯一行为定义表**：`DesktopBehaviorDefinitionCatalog` 以准确的 BehaviorId 定义语义类别、Episode、来源、效果和期望素材批次。能力目录、Episode allowlist、偏好投影和 Outcome 配置从它生成。未知 ID 或同名旧批次不能靠名称包含 `idle`、`eat` 等字样获得运行资格。资产 loader 仍负责实际文件与审批；定义表只能收紧，不能批准资产。
2. **持久策略**：`WukongData/agent/autonomy-policy.json` 是运行时与面板共用的配置。它包含 Stand/Sit/Prone 最短/最长驻留、待机偏好、决策延迟，支持 Episode 局部覆盖、类别权重/冷却、单行为倍率、微事件冷却、口令和发言参数。旧 `autonomous-behavior-preferences.json` 只用于兼容读取；旧接口写入新策略，不再维护第二份权威配置。
3. **显式结算**：每个可执行的 Normal 动作必须有明确的 `BehaviorOutcomeProfile`。原有行为专属数值保留，兜风等旧 fallback 分支的效果及准确终态也已显式登记。不再为未知动作猜姿态或套用通用能量扣减。稳定 Idle 不生成收益；重复/迟到完成不结算；预览不写正式状态、关系和记忆。
4. **单一自主选择器**：删除旧 `ChooseAutonomousBehavior` 及其随机候选构造。正式 Episode 仅由 `BehaviorDecisionEngine` 选择；无合格候选、基础设施异常或只开 Shadow 时保持兼容 Idle，不再偷偷回退另一套随机决策。Shadow 可继续计算诊断，但不提交实际动作。
5. **可调打扰和服从阈值**：主动发言开关、频率、跨午夜安静时段、八小时预算、未回应退避与口令阈值集中配置。资产/来源/姿态/忙碌检查仍先于意愿计算。参数不能解锁素材或授权模型调用。睡眠、正在聊天、高压力等抑制保留。
6. **门禁真相审计**：开发者新增“策略与素材门禁”，分别展示实际加载批次、视觉批准、运行批准、真实路由、结算来源和异常。当前旧巡逻 v1 已被原代码排除，v8 是正式巡逻；不能把历史旧文件误报为运行绑定。魔法与金币当前仍是既有 PrototypePreview 路由，本轮如实展示而不擅自晋级。金币路径只允许规范 `petrificus_coin/v19/`，越界和旧重复路径失败关闭。

## 面板与调整方法

- 主人档案的日常偏好下展开“生活节奏与打扰频率”，调整三种姿态停留、待机偏好、主动发言和安静时段，然后保存。
- 开发者的“策略与素材门禁”查看生效值、实际素材入口与诊断。它不提供跳过审批按钮。
- 高级参数可在关闭应用后编辑 `WukongData/agent/autonomy-policy.json`。只接受 schema 1 和有限的合法数值。无效或未来 schema 文件保留原样，应用使用安全默认值并显示诊断；不会自动覆盖原文件。
- 最长驻留不是强制计时硬切。只有存在已批准、姿态/视角兼容的退出动作时，才移除当前 Idle 候选；没有安全出口时继续等待。
- 驻留时钟依据正式姿态变化及入睡/醒来边界重置，不因同姿态 Idle 刷新或微表情结束而重新计时；动作自己的冷却和生命周期计时保持独立。
- 倍率是效用评分的一部分，不是固定播放百分比。精力、压力、Episode、记忆、关系、冷却和资产门禁仍共同作用。
- Episode 覆盖优先于普通姿态配置；面板保存普通配置时保留高级覆盖，不静默删除它们。
- `LegacyFallbackOnInfrastructureFailure` 仅保留旧接口兼容，不再启用第二选择器；清空 `AuthoritativeEpisodes` 会保持兼容 Idle，而不是恢复旧随机算法。

## 随 EXE 附带相册

整个发布文件夹一起分发，不能只发送启动器 EXE。推荐的首次初始化布局：

```text
Wukong.Desktop.exe
Wukong.Desktop.dll
WukongAssets/                    # 发布流程生成的动作素材
WukongDefaults/
  profile/album-root.txt         # 内容：albums
  agent/autonomy-policy.json
  albums/
    江边散步/
      album.md
      images/photo-001.jpg
```

`WukongDefaults/albums/` 是可选的本地附赠内容，不提交私人相册。首次启动仅复制缺少的文件到可写 `WukongData/albums/`，记录 `.bundled-albums-v1-imported` 后不再重复导入，因此接收者删除照片后不会被下次启动恢复。

也可以直接把整理好的相册放进 `WukongData/albums/`，不需要首次复制。`WukongData/profile/album-root.txt` 内容为 `albums`，相对于 **WukongData** 解析，而不是进程工作目录。相册 Markdown 内的图片也应使用相对于子相册目录的链接。

相册面板和供对话/记忆使用的索引共用 `PortableAlbumBinding`。保留接收者现有有效外部目录绑定；发送者失效的绝对路径回退到随包相册。相对路径禁止逃出 WukongData。显式 `WUKONG_ALBUM_ROOT` 环境变量仍优先。程序目录不可写时，数据会按现有机制落到用户数据目录，随包相册仍可完成初始化。

分发前不要携带自己的 API 密钥、会话历史、长期记忆或真实用户状态。本轮只添加机制和默认相对绑定，没有复制或上传私人相册、修改原相册目录。

## 验证入口

```powershell
dotnet build Wukong.sln --configuration Release
python tools/validate_contracts.py
python -m unittest discover -s tests -v
dotnet tests/Wukong.Desktop.Tests/bin/Release/net8.0-windows/Wukong.Desktop.Tests.dll --policy-refinement-selftest
dotnet tests/Wukong.Desktop.Tests/bin/Release/net8.0-windows/Wukong.Desktop.Tests.dll --list-tests
# 每个 --test 使用上面列出的精确名称，可以独立进程验证 WPF 测试。
git diff --check
```

专项包含 10,000 次确定性决策、30 分钟虚拟连续性、重复/迟到/失败结算、预览隔离、未知动作和旧批次拒绝、配置迁移/往返/无效文件保留、发言和口令参数、相册首次导入/搬迁/外部绑定/删除后不恢复。虚拟时间与启动存活检查不等于 Windows 长时间视觉验收；主人仍需观察驻留节奏、过渡自然程度、面板大小以及打扰频率。

全量桌面测试可逐进程执行，防止 WPF 单例/线程资源影响下一项，也防止进程提前退出被误判为通过：

```powershell
$dll = 'tests/Wukong.Desktop.Tests/bin/Release/net8.0-windows/Wukong.Desktop.Tests.dll'
foreach ($name in (& dotnet $dll --list-tests)) {
    $output = @(& dotnet $dll --test $name 2>&1)
    if ($LASTEXITCODE -ne 0 -or !($output -match '^1/1 tests passed\.$')) {
        throw "Test did not complete successfully: $name"
    }
}
```

## 本轮文件范围

- Application：`BehaviorAgentFoundation.cs`、`InitiativeSpeechDecision.cs`、新增 `AutonomyPolicyProfile.cs`。
- Infrastructure：`AgentLocalStores.cs`、`PortableDataLayout.cs`、新增 `FileAutonomyPolicyStore.cs`、`PortableAlbumBinding.cs`。
- Desktop：`DesktopBehaviorCapabilityCatalog.cs`、`DesktopPetRuntime.cs`、`DesktopAgentRuntime.cs`、`MainWindow.xaml.cs`、`ControlPanelWindow.xaml`、`ControlPanelWindow.xaml.cs`，新增 `ControlPanelWindow.Policy.cs`、`RuntimeAssetAudit.cs`。
- 测试：Desktop 的 `Program.cs`、`BehaviorAgentRolloutTests.cs`、`BaseAssetExecutionTests.cs`、新增 `PolicyRefinementTests.cs`；Python 的 `test_patrol_walk_candidate_v1.py` 更新为统一定义表断言，原始像素/哈希断言保留。
- 默认配置：`config/defaults/agent/autonomy-policy.json`、`config/defaults/profile/album-root.txt`。
- 文档：本文、`CURRENT_STATE.md`、`DECISIONS.md`。
- 开始时已有的 `AGENTS.md`、`skills/`、迁移说明及其他重叠文件中的用户改动完整保留；它们不属于本轮新写的功能。`.asset-staging/` 未修改，未纳入提交。

## 2026-10-08 最终本地验证

- 分支：`codex/project-skills-foundation`；HEAD：`f05b16c76abeaed270e7c11127f4b75f5d516e70`。本轮未提交、未推送、未改 main。
- Release solution build：通过，6 个既有警告；win-x64 自包含文件夹发布通过，4 个既有 Desktop 警告。不生成安装包。
- C#：Domain 5/5、Contracts 5/5、Application 58/58、Infrastructure 23/23、Desktop 134/134。桌面测试均独立进程完成且检查成功终态，不把提前退出当作成功。
- Python：95/95；契约校验：0 errors / 9 known gaps；`git diff --check` 通过。
- 固定种子 10,000 次决策、30 分钟虚拟连续运行、同坐姿 Idle 刷新后累计驻留上限、配置迁移与相册搬迁专项通过。
- 发布素材：1,696 个文件，包括 1,483 张 PNG，逐文件与源码 SHA 一致。资产和 manifest 无本轮 diff，旧巡逻 v1、已弃用侧趴拼接 v5、睡眠 v10 不在发布包中。
- EXE：`.publish-check/behavior-policy-portable-albums-v1-20261008/Wukong.Desktop.exe`。
- EXE SHA256：`9264563b02b1291f6c98a95ad4ce450b30455073a1defcc9308253df85bdafac`。
- 应用 DLL SHA256：`28837f97af7b1b39d262e1d490a31533914ea855e644284e544bbb4c3a30dde0`。文件夹发布应整体分发，不能只分发 EXE 启动器。
- 最终受控启动：从发布目录启动，PID 44128 存活 7 秒；透明窗口未响应 `CloseMainWindow` 正常退出请求，随后只按本次确切 PID 终止；无残留。**这不是正常退出路径或长时间视觉验收通过的证明**。
- 主人面板 WPF 截图复核通过：`.publish-check/behavior-policy-verification/screenshots-final/profile-autonomy-policy.png`。列宽截断已修复。全页截图首次进入开发者登录时停住，未绕过认证；改为仅截主人页后正常完成。开发者页实际登录后的展示、长期驻留节奏、主动发言体验仍需人工复验。
- 日志、每项桌面测试、发布哈希和受控启动记录：`.publish-check/behavior-policy-verification/`。这些是本地审阅产物，不提交私人配置或相册。
