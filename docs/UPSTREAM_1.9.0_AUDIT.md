# 上游 1.9.0 同步与行为审计

审计日期：2026-11-16。上游代码基底为 `v1.9.0` / `13e97c49620d6b99b4d29e7811464f063f5e86cd`，通过真实 Git merge 保留原始历史。同步时 `upstream/main` 为 `77b583df0f5615af36cae0d1aa79629b1c6d57e2`，文件树与该标签一致。
同步前本地为 `14f6be3` / `1.8.0-alpha4`；本次源码为 `1.9.0-alpha1`。常规规则优先采用上游，以下列明保留项、差异和已发现的问题。

## 功能核对

| 范围 | 上游 1.9.0 与旧本地的区别 | 本次处理与代码 |
| --- | --- | --- |
| MuMu / 雷电选屏 | 上游已支持两类模拟器、官服／B服、当前焦点屏优先及手动屏幕选择；旧本地只支持 MuMu，解析语法较窄 | 采用上游统一解析与实例发现；同包多个屏幕无焦点／无明确选择时拒绝猜测。[选屏](../src/HongxueAssistant.Core/Services/AdbGameScreens.cs#L67)、[窗口关联](../src/HongxueAssistant.Core/Services/GameConnectionService.cs#L54) |
| 当前操作页与桌面窗口 | 同一已选模拟器进程内，Android 焦点与前台游戏页关联；不会跨进程改绑另一实例 | 采用上游。只有 Android 目标确认后才关联同进程前台模拟器窗口；模拟器关闭后不静默改绑同名实例。[Maa 截图](../src/HongxueAssistant.Core/Services/GameConnectionService.cs#L111) |
| 物理截图编号 | 上游和旧本地都区分逻辑与物理编号；上游允许显示缺少物理编号的条目，但在实际截图前明确报错 | 采用上游 Maa ADB。`screencap -d` 使用物理编号，`input -d` 使用逻辑编号；非零逻辑屏缺物理编号不回退默认屏。[截图与输入编号](../src/HongxueAssistant.Core/Services/AdbGameScreens.cs#L84) |
| 默认屏滚动 | 旧本地直接发 `input -d 0 swipe`；上游对默认屏省略 `-d`，兼容 Android 9 | 采用上游命令；发手势前同时核对包名、逻辑编号和物理编号。[同屏滚动](../src/HongxueAssistant.Core/Services/GameConnectionService.cs#L259) |
| 切屏后的旧识别 | 两边都有版本号；上游主要在采集时更新，本地会在结果发布前再检查 | 采用上游主体，补回发布前重查与滚动拒绝时立即作废旧版本；异常发现不发手势。[旧结果校验](../src/HongxueAssistant.Core/Services/GameConnectionService.cs#L229) |
| 置顶 / 聚焦 | 两边均由游戏聚焦触发；助手或其所属对话框只能延续已经置顶的状态。旧本地允许非最小化状态，上游要求 Normal | 采用上游条件：失焦后再单独点击助手不会强制置顶。[焦点规则](../src/HongxueAssistant.App/Windows/SidePanelWindow.xaml.cs#L637) |
| 沉底与原生置顶 | 两边由置顶切到失焦时均用 HWND_BOTTOM；状态已一致时不会每帧重排层级。上游未保留隐藏 owner 的层级，原生测试复现 WPF Topmost=true 但 WS_EX_TOPMOST=false | 修复抬升时的 `SWP_NOOWNERZORDER`，不改变上游触发规则；游戏丢失立即清理状态。[原生层级](../src/HongxueAssistant.Core/Interop/User32.cs#L43) |
| 轻微吸附 | 两边均为约 12 DIP、拖动松手生效、调整大小不吸附。旧本地允许另一轴近似相邻，上游要求实际重叠，并约束工作区 | 采用上游吸附；动态真实回合按钮保持换行，手动阶段仍使用上游横排。[吸附](../src/HongxueAssistant.Core/Services/WindowGeometry.cs#L47) |
| 核心 / 附加 Ban | 旧本地普通页把两类盟约合并，可能让核心已禁的干员继续可用；分组页还有额外分支 | 统一上游：有核心则全部核心被禁时不可用；无核心才看全部附加盟约。分组元信息不改变规则。[Ban 判定](../src/HongxueAssistant.Core/Services/CovenantBanRules.cs#L19) |
| 被 Ban 的必带 | 上游已实现保留选择、停用、不计入请求，解除后恢复 | 直接采用上游，取消或清空才删除；[有效必带](../src/HongxueAssistant.Core/ViewModels/LineupPlannerViewModel.cs#L617)、[规划请求](../src/HongxueAssistant.Core/ViewModels/LineupPlannerViewModel.cs#L1047) |
| 必带选择与人数上限 | 旧本地隐藏／拒绝新增被 Ban 项，按有效人数限额；上游允许预留被 Ban 项，但保存名单总数最多 9 人 | 按上游，因此 9 个保存项中即使部分停用，也需主动移除一个才能新增第 10 项。停用项仍不计入规划。[名单上限](../src/HongxueAssistant.Core/ViewModels/LineupPlannerViewModel.cs#L698) |
| 手动 Ban 重扫 | 旧本地保留手动覆盖，上游成功识别直接替换名单 | 按上游。`BanViewModel.SetBans` 的替换行为与 `ClearRunBans` 保留手动项的行为分别验证 |
| 商店识别 | 旧本地保留了一套 MuMu 专用兜底；上游已有新版识别、UI90 适配和经济／购买状态处理 | 采用上游 ShopReader，移除未使用的旧兜底，避免两套识别规则漂移 |

## 保留的本地功能

- **配置**：始终为 `%APPDATA%\HongxueAssistant`。采用上游暂存、校验、备份和缺项合并；仅从旧 ASPAssistant 补入受支持内容，已有值优先，已删除阵容不复活。不自动读取 HongxueCompanion。[配置目录](../src/HongxueAssistant.Core/Data/UserSettingsDirectory.cs#L9)。
- **真实回合阵容**：保存作者真实回合区间、独立槽位及不可变来源快照；原文装备无所属关系时保留在参考区，不猜测配装。旧仅含 Guide 的阵容可恢复回合，已明确保存的空回合不会被补满。
- **新字段兼容**：真实回合深拷贝包含候选装备和优先级；撤销同时恢复目标盟约。回合可复制到此前选中的后期，修复阶段类型相同而误拒绝复制的问题。[回合深拷贝](../src/HongxueAssistant.Core/Models/LineupRoundStage.cs#L54)、[回合复制](../src/HongxueAssistant.Core/ViewModels/LineupEditorViewModel.cs#L411)。
- **保存 / 分享 / 修补 / 追踪**：真实回合、目标盟约、候选装备同时保留；修改回合候选优先级或目标会使分享缓存失效。阵容修补保留回合，追踪包括只在过渡回合出现的干员；已获取干员的待买装备继续保留。不把终端等级当真实回合。[分享缓存](../src/HongxueAssistant.Core/Services/LineupShareService.cs#L51)。
- **识别完整性**：继续按已知模式池做同来源补扫，45 秒有效，并要求回到顶部确认；未读全或来源切换时保留旧 Ban。上游按可见条目判断完整，直接覆盖会回退用户图片已修复的隐藏末行问题。未知模式池只预览，因此新赛季池变化需要补充 `data/information_pools.json`。[完整池补扫](../src/HongxueAssistant.Core/Services/InformationReadSession.cs#L36)。
- **部分识别预览**：补齐对局信息页与新版 ViewModel 的绑定、清理和事件订阅；不完整读取可显示原因与已见条目，保留旧结果。
- **更新**：更新检查和 13 条线路诊断继续使用 `friends-xiaohuli/HongxueAssistant` 的实际 Release 安装包，保留直连优先、代理回退、SHA256 和 alpha 数字版本比较。[更新源](../src/HongxueAssistant.Core/Services/UpdateService.cs#L35)。

## 验证与边界

- 使用隔离目录做配置迁移、保存／重开、分享、修补和追踪回归。
- Win32 测试核对 WPF 属性、原生置顶位、所属对话框、失焦沉底、拖动吸附及调整大小；六种主题组合验证新编辑器和真实回合。
- ADB 使用假设备验证逻辑／物理映射、切屏、拒绝跨屏滚动和发现异常；未向真实模拟器／游戏发送输入。
- 原始用户图片及合成滚动图用于 OCR 回归；合成帧不作为实机滚动成功的证据。
- 自动关联限于已选择的 ADB 设备与同一模拟器进程；手动锁屏时仍应选择对应的桌面窗口。跨进程切换不会自动改绑，以免混用其他实例。
- 本轮只更新源码和编译结果，不生成新 ZIP，不改写已发布 alpha2 包，不创建发布标签。最终测试记录位于本地 `.tools/sync-upstream-1.9-20261116/`。

## 回归结果

- 完整公开 .NET 回归：1091 通过、0 失败、1 个上游原有跳过（共 1092 项）。
- 回合、选屏、原生层级及复制专项：41 通过。
- 最后补齐部分识别预览后，重新编译并运行受影响的 33 项（含新增预览测试、六种主题、首领难度、回合保存和原生置顶）：全部通过。
- Python 数据回归：33 通过。
- Release 编译无新增警告或错误。未向真实 PC 游戏、MuMu 或雷电发送输入。
- 已发布 alpha2 ZIP SHA256 仍为 `06a80662fdc9df93e0f37150a5cbf1043d8268c37530cdc6b5a2d3c502bb10b3`；本轮不打包。
