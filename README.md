<div align="center">
  <img src="docs/images/hongxue-logo-large.png" width="280" alt="鸿雪画框图标"/>
  <h1>HongxueAssistant</h1>
  <p>鸿雪·卫戍助手 · 本地增强版</p>
  <p>明日方舟·卫戍协议 辅助工具<br/>基于 Moonfair/ASP_Assistant 修改，由 MaaFramework 提供图像识别能力。</p>
  <p>
    <img src="https://img.shields.io/badge/platform-Windows-blue?style=flat-square" alt="Windows"/>
    <img src="https://img.shields.io/badge/.NET-8.0-purple?style=flat-square" alt=".NET 8"/>
    <img src="https://img.shields.io/badge/game-卫戍协议·规程-orange?style=flat-square" alt="卫戍协议·规程"/>
    <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-green?style=flat-square" alt="Apache 2.0"/></a>
  </p>
  <p><a href="https://github.com/Moonfair/ASP_Assistant">原项目</a> · <a href="https://github.com/friends-xiaohuli/HongxueAssistant/issues">问题反馈</a></p>
</div>

---

> **项目来源**：本项目是在 [Moonfair/ASP_Assistant](https://github.com/Moonfair/ASP_Assistant) 基础上修改的独立衍生版本，保留原项目 Apache License 2.0 协议与归属声明。感谢 Moonfair 及上游贡献者。本仓库不是原作者发布的版本。
>
> 上游基线：[`93b2ba8`](https://github.com/Moonfair/ASP_Assistant/commit/93b2ba8514f6591a67e4cbf5a6169dee7d065bf6)。改动范围见各文件头部声明及发布说明。

本分支以 [bingyunhe/hongxue-stronghold-companion v1.9.0](https://github.com/bingyunhe/hongxue-stronghold-companion/releases/tag/v1.9.0) 为基底，保留独立真实回合阵容、完整攻略、当前本地配置及本仓库更新线路。源码版本为 `1.9.0-alpha1`；日常同步只编译、测试，需要交付时才生成包。详细行为差异见 [1.9.0 同步审计](docs/UPSTREAM_1.9.0_AUDIT.md)。

## 目录

- [简介](#简介)
- [快速开始](#快速开始)
- [截图](#截图)
- [功能](#功能)
  - [识别](#识别)
  - [图鉴](#图鉴)
  - [追踪与阵容](#追踪与阵容)
  - [规划](#规划)
  - [设置与窗口](#设置与窗口)
- [连接 MuMu / ADB](#连接-mumu--adb)
- [当前数据与限制](#当前数据与限制)
- [开发](#开发)
- [更新游戏数据](#更新游戏数据)
- [数据与项目结构](#数据与项目结构)
- [开发依赖与致谢](#开发依赖与致谢)
- [许可证](#许可证)

## 简介

鸿雪·卫戍助手是《明日方舟》卫戍协议的侧边辅助工具，读取游戏画面，帮助录入本局信息、标记商店追踪目标，并提供干员和装备查询、阵容管理、阵容规划与站位参考。支持 PC 客户端和 MuMu、雷电 / ADB 连接。

项目原名 ASP Assistant · Arknights。程序文件为 `HongxueAssistant.exe`；v1.9.0 起配置保存在 `%APPDATA%\HongxueAssistant`，启动时自动合并旧项目配置，无需手动搬迁阵容。

| 页面 | 内容 |
| --- | --- |
| 图鉴 | 在干员 / 装备间切换；按等阶、盟约、词条或装备功能筛选，查看普通与精锐详情 |
| 追踪 | 购买清单、获取进度、干员状态与地图模拟；商店中标记追踪目标 |
| 阵容 | 我的阵容 / 规划；目标盟约筛选、本局推荐、受禁用影响后的修补与分阶段编辑 |
| 对局信息 | 本局 Ban 盟约和禁用干员、截屏录入、敌方领袖与特训敌人资料 |

本文和截图以当前开发源码为准，可下载版本的内容以[发布页说明](https://github.com/friends-xiaohuli/HongxueAssistant/releases/latest)为准。

## 快速开始

1. 从 [Releases](https://github.com/friends-xiaohuli/HongxueAssistant/releases/latest) 的 Assets 下载以 `-win-x64.zip` 结尾的 Windows 包；`Source code` 是源码。
2. 右键“全部解压”到独立目录，保留全部文件及 `data`、`runtimes` 等文件夹。
3. 进入解压后的应用目录，双击 `HongxueAssistant.exe`。若 ZIP 内有同名顶层文件夹，请先打开它。独立运行包带有 .NET、识别模型和所需运行库。
4. 首次启动时选择与游戏一致的商店主题；也可在设置中开启自动主题。需要时配置截屏录入和隐藏助手快捷键。
5. 模拟器用户在设置的“游戏连接…”中选择 MuMu / ADB，再选择对应实例、窗口及游戏屏幕，见[连接说明](#连接-mumu--adb)。

**运行要求**：Windows 10 / 11 x64，明日方舟 PC 版，或 MuMu / 雷电中的 Android 版。源码构建的非独立版本需要 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) 和 VC++ 2015–2022 x64 Runtime。

**更新**：正式发行版检查本仓库 Releases，检测到新版后由用户选择下载；校验并暂存后，再由用户选择是否重启更新。手动更新时先退出旧程序，再解压新版；个人追踪、阵容和设置保存在本机用户数据目录。

**校验下载**：发布页提供 `SHA256SUMS.txt`，可用 PowerShell `Get-FileHash` 计算所下载 ZIP 的 SHA256 并核对。`PACKAGE-MANIFEST.txt` 记录版本、资源数量与 ZIP 哈希。各版本变化见[发布说明](https://github.com/friends-xiaohuli/HongxueAssistant/releases/latest)。

## 截图

以下截图由当前开发源码使用公开游戏数据和示例阵容直接渲染，不含个人游戏记录。三个主题可分别选择深色或浅色。

| 图鉴 · 干员 | 图鉴 · 装备 | 追踪 · 购买清单 |
| :---: | :---: | :---: |
| ![终端深色干员图鉴](docs/images/operators.png) | ![可露希尔浅色装备图鉴](docs/images/equipment.png) | ![购买清单、终端等级与回合资金](docs/images/tracking.png) |

| 地图模拟 | 我的阵容 | 对局信息 |
| :---: | :---: | :---: |
| ![使用手动选择的干员池生成站位参考](docs/images/deploy.png) | ![目标盟约、本局推荐与阵容入口](docs/images/lineups.png) | ![本局禁用和敌方领袖信息](docs/images/run-info.png) |

| 规划 · 目标 | 规划 · 结果 |
| :---: | :---: |
| ![选择目标盟约和固定干员](docs/images/planner-targets.png) | ![按人数和转职次数查看候选阵容](docs/images/planner-results.png) |

![四阶段阵容编辑、装备与候选、目标盟约统计](docs/images/lineup-editor.png)

<details>
<summary>查看浅色干员图鉴、悬停详情与森空岛导入</summary>

<p align="center">
  <img src="docs/images/operators-light.png" width="300" alt="浅色干员图鉴"/>
  <img src="docs/images/operator-details.png" width="360" alt="普通与精锐干员详情"/>
</p>

![森空岛攻略入口、导入教程和示例按钮](docs/images/import-guide.png)

</details>

## 功能

### 识别

**本局信息与 Ban 位**

- 打开开局信息页后自动录入核心盟约、附加盟约、禁用状态和敌人；也可在“对局信息”页点击“截屏录入”，或使用 `Ctrl+Shift+/`。局内重新打开的信息页同样支持手动补录。
- 快捷键中的 `/` 是主键盘上与 `?` 共用的按键。请先打开游戏信息页，再触发录入。
- 开局盟约面板显示不全时，最多自动下滚三次，完整后应用；局内信息页不自动滚动。PC 模式发送滚动前会检查并尝试取得游戏前台。
- 重试失败保留旧结果；盟约和敌人分别校验，敌人漏读不影响完整盟约的录入。任务执行期间显示状态并禁用重复录入。
- 缩放、最小化、失焦或标题漏读不会重复触发新局；确认卫戍主菜单或局内画面后，允许识别下一次开局。确认新局时清除本局获取和规划进度，保留追踪目标、阵容关联及个人设置。
- 图鉴与规划提供“只看本局”复选框；对局信息顶部可清除录入或本局 Ban。清除录入保留手动禁用；清除 Ban 位清空全部禁用。

**敌人和首领血量**

- 识别信息页右侧敌方领袖及特训敌人，支持名称断行、相邻类型合并与去重；有资料时展示详情，未收录时保留原识别文字。
- 首领资料包含单人极速模拟、规程等级 1–20 和多人四档血量；有隐藏形态的另列隐藏形态血量。匹配当前模式或规程等级，其他难度折叠显示。
- 在“对局信息”页可单独重试敌人识别；手动识别不点击或切换游戏页面。

**商店和地图**

- 支持终端、乌萨斯齐心团和可露希尔商店主题；对追踪中的干员和装备显示绿色标记，支持灰色、冻结与扫光等状态。
- 读取回合、模式、资金、升级价和终端等级。两次一致后更新，读不清时保留上次确认值；已满级时显示满级。`??` 是隐藏 Boss 回合，不猜填数字。
- 自动主题需要连续两次可靠结果后切换，未知画面保留原主题；也可手动选择。
- 地图模拟识别当前地图和商店收起 / 展开镜头，也可手动选图；暂时看不到棋盘时保留上次地图。

### 图鉴

干员和装备共用“图鉴”页，通过顶部“干员 / 装备”切换，保留各自筛选状态。

- **干员**：名称及拼音首字母搜索；按等阶、核心盟约、附加盟约、词条类型、触发时机和本局状态筛选。等阶可多选，盟约行可选择满足任一或全部条件。
- 列表展示头像、名称和盟约，悬停查看普通与精锐信息；可手动追踪、禁用，或通过自选干员入口选择候选。
- 筛选“重置”恢复搜索和筛选，保留本局盟约高亮；禁用与录入操作集中在对局信息页。
- **装备**：按等阶、功能和普通 / 转职分类筛选，悬停查看效果及普通 / 精锐数值；支持追踪和名称、拼音首字母搜索。

### 追踪与阵容

- 购买清单按最终 / 过渡干员分组，装备一行一件；支持获取、精锐、同名数量及完成进度。标记获取保留原需求，删除追踪会撤销对应需求。
- 设置中开启“同时追踪多个队伍”后，阵容需求合并并保留来源；未开启时，追踪下一阵容会替换列表。已获取装备可从图鉴重新点星追踪。
- “干员状态”统一管理实际拥有池，搜索、添加、获取与精锐状态供地图模拟使用。
- “自动标记购买（实验性）”默认关闭，可在设置的“追踪与侧栏”中开启。购买前需先稳定识别卡名，再依据卡位空缺与金币下降确认；仅自动处理已追踪装备，不推测精锐或同名数量，识别不足时可手动标记。
- 阵容支持名称 / 干员搜索、目标盟约筛选、来源链接和分享，按全部 / 本局可用 / 待确认 / 不可用切换。待确认页显示可折叠的本局推荐；“修补”固定未被禁的干员，按原目标搜索替补，可另存或覆盖原阵容。
- 编辑器分前期、中期、后期、挂件四阶段。干员支持标签、精锐及复制到其他阶段；装备、核心装备和候选可分别配置，候选支持优先标记。
- 每套阵容可设置目标盟约及Ⅰ / Ⅱ档位，实时显示满足情况；旧阵容缺少目标时按后期配置推算。策略支持多选，最多九项。
- 目标选择器与规划目标隐藏“绝技”；对局信息仍可识别和显示该盟约。

**森空岛攻略导入**

1. 阵容页点击“导入”，通过攻略站入口选择攻略，复制详情网址。
2. 粘贴后导入预览；也可使用“复制示例并填入”体验。
3. 在编辑器核对干员、精锐、策略及阶段，点击“保存”后使用。

超过九人的攻略按本数划分前期（一本、二本）、中期（三本、四本）和后期（五本、六本）；九人以内保留为后期阵容。装备和回合说明请核对来源攻略，导入不会自动开启追踪。也支持 dpaste.com 分享链接和阵容 JSON。

### 规划

**阵容规划**

- 在阵容页切换“我的阵容 / 规划”，选择核心与附加盟约的Ⅰ / Ⅱ档目标，按 8 / 9 人和转职次数搜索，可固定必须加入的干员。
- 普通策略支持 0–2 次转职，变形同构体策略支持至多三次；暴躁铁皮按普通 / 精锐规则计算转职上限。以界面可选项为准。
- 规划与本局禁用联动；被禁的附加盟约可由原生干员满足，但不能作为转职目标。结果可保存为阵容或加入追踪，并可查看道中路线。
- 被 Ban 的必带干员保留并显示暂停状态，暂不计入规划，禁用解除后自动恢复。
- 涉及叠层、精锐等额外条件的Ⅱ档会标注，人数满足之外仍需自行确认。达到搜索上限会提示；没有候选不等于已证明无解。
- 感谢 CarlosDJY（Jiayu Ding）通过 [PR #1](https://github.com/bingyunhe/hongxue-stronghold-companion/pull/1) 及后续贡献提供阵容规划等功能。

**地图模拟与站位参考（实验）**

- 在追踪页切换“地图模拟”，从手动选择的实际拥有池挑选 8 / 9 人，参考路线拦截、远近排布、治疗覆盖、阻挡、地块与位置叠层特质生成格子及朝向。
- 小棋盘显示头像，可拖动换位、固定位置并重新规划；支持同名干员重复上场，盟约按不同干员计数。
- 地图识别支持 10 张地图和两种镜头。**没有实现自动识别场上、整备区干员**，干员池由用户管理。
- 游戏内显示默认关闭，开启后显示格框、朝向与名牌。打分权重是估计值，结果仅供参考。

### 设置与窗口

- 标题栏设置集中管理主题、深浅色、多队伍追踪、游戏连接和快捷键。外观可跟随模式主题，也可独立选择。
- 支持按游戏焦点置顶、拖动吸附、缩放、自由位置及跟随游戏窗口；可配置隐藏助手和快速识别快捷键，隐藏到托盘后追踪识别继续。
- PC 截图在游戏被遮挡或不在前台时暂停；截图期间短暂隐藏辅助及覆盖层。游戏最小化时隐藏标记，恢复后恢复联动收起的辅助，保留用户手动最小化状态。
- 开发构建跳过更新检查；正式版校验 SHA256 后更新，替换或启动失败尝试回滚并提示。
- 个人配置保存在 `%APPDATA%\HongxueAssistant`。启动时自动合并旧目录中本项目使用的设置、追踪、阵容及排序，新目录数据优先，旧目录只读保留。新增和修改只写新目录；删除阵容后不会由旧副本重新补回。合并先备份，失败会提示并停止启动；原版专用设置和日志不迁入。

## 连接 MuMu / ADB


1. 在 MuMu 中打开明日方舟，将模拟器画面设置为横屏 16∶9，支持 1920×1080 和 1280×720（240 DPI，平板模式）。
2. 打开辅助侧栏的设置，点击“游戏连接…”，选择“MuMu / ADB”。
3. 点击“发现设备”并选择目标实例，也可以手动填写模拟器的 `adb.exe` 路径和连接地址。
4. 选择与该设备对应的 MuMu 游戏窗口，点击“测试并应用”。多开时需核对实例与窗口。
5. 选择当前游戏的商店主题并添加追踪目标。绿色标记会随 MuMu 内部游戏画面移动和缩放。

ADB 模式使用 MaaFramework 读取游戏截图，游戏被其他窗口遮挡时仍可识别。覆盖层在游戏或辅助侧栏处于前台时显示，最小化时隐藏。截图失败时清除旧结果并每 5 秒重试。关闭后重新打开的模拟器窗口可在“游戏连接…”中重新选择。连接设置保存在本机用户数据目录，原有安装默认继续使用 PC 截图。

当前实测范围为 MuMu 12 的 1920×1080 终端主题商店，已读出四格商品并验证坐标。ADB 模式拒绝竖屏与非 16∶9 画面。其他商店等级、主题和模拟器版本仍需更多实机验证。

开发验证可通过 `HONGXUEASSISTANT_SETTINGS_DIR`（兼容旧 `ASPASSISTANT_SETTINGS_DIR`）环境变量指定独立设置目录，使用 `dotnet HongxueAssistant.dll` 启动已构建的托管入口。

## 当前数据与限制

| 项目 | 范围 |
| --- | --- |
| 赛季 | 卫戍协议·规程·序列 I，`act3autochess` / `V3_1` |
| 干员条目 / 装备 / 盟约 | 143（含策略专属） / 70 / 26；另有75名自选候选独立管理 |
| PC 画面 | 原布局以 16:9 为基础；已离线验证 4K/UI90 及约 2560×1600/UI90 样本，实际识别仍受赛季布局、分辨率与缩放影响 |
| ADB 画面 | 横屏 16:9；其他比例会拒绝应用，不因 PC 样本适配而放宽 |
| Ban 与商店 | 支持不完整开局面板补读和多主题商店；异常画面可能漏读，需要核对录入结果 |
| 敌人资料 | 匹配已收录名称；未收录时显示原文字，不用相似名称猜测替代 |
| 地图模拟 | 自动识别地图，使用手动管理的干员池；规划权重及地块提示仅供参考 |
| 个人数据 | 保存在本机用户数据目录，更新前请退出程序并保留配置 |

## 开发


安装 .NET 8 SDK 后，在 PowerShell 中运行：

```powershell
git clone https://github.com/friends-xiaohuli/HongxueAssistant.git
cd HongxueAssistant
dotnet restore HongxueAssistant.sln
dotnet test src/HongxueAssistant.Tests/HongxueAssistant.Tests.csproj -c Release --filter "Category!=LocalScreenshot"
dotnet run --project src/HongxueAssistant.App
```

仓库已包含当前 JSON 数据、图标和 OCR 模型，首次运行不需要重新抓取数据。

开发与提交流程见 [贡献指南](CONTRIBUTING.md)。公开测试排除依赖本地私有截图的 `LocalScreenshot` 用例。

```powershell
# 生成需要 .NET Desktop Runtime 的本地版本
dotnet publish src/HongxueAssistant.App/HongxueAssistant.App.csproj -c Release --self-contained false -o publish/local

# 生成 Windows x64 独立版本
dotnet publish src/HongxueAssistant.App/HongxueAssistant.App.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

维护者须先确认版本号和完整发布文案，再推送 `v` 开头的版本标签，触发 `.github/workflows/release.yml`。该流程先运行测试，再构建独立运行包、打包运行库与许可证，生成校验文件并发布到 GitHub Releases。

## 更新游戏数据

脚本使用 Python 3.10+；处理图片需要 Pillow。

```powershell
python -m pip install Pillow
cd data
# 只更新本赛季干员、装备、盟约，保留首领和敌人数据
python fetch_spdatabase.py --skip-bosses --skip-enemies
```

已有图标会保留，只下载缺失图标。大量下载可能耗时，建议在游戏空闲时运行。

盟约图标（`data/icons/covenants/`）来自 PRTS 的“卫戍协议：盟约_icon_*Ship”系列。首字母索引随 `data/search_keys.json` 提供；更新数据后可用 `data/build_search_keys.py` 重建（该维护脚本需要 pypinyin，正常构建和运行无需安装）。

| 命令 | 功能 |
| --- | --- |
| `python fetch_spdatabase.py --skip-bosses --skip-enemies` | 更新当前干员、装备和盟约数据 |
| `python fetch_spdatabase.py --download-skins` | 下载皮肤头像并重建映射 |
| `python fetch_spdatabase.py --apply-mask` | 为已有皮肤头像补白色背景 |
| `python fetch_spdatabase.py --skip-icons` | 跳过图标下载 |

数据脚本的输出格式和来源沿用原项目，并增加当前赛季读取逻辑；更新数据不会自动完成识别区域适配。

## 数据与项目结构

```text
├── data/
│   ├── operators.json           # 干员、盟约与词条
│   ├── equipment.json           # 装备与普通 / 精锐效果
│   ├── covenants.json           # 盟约与核心转职装备映射
│   ├── skin_avatar_map.json     # 皮肤头像到干员名的映射
│   ├── garrison_maps.json       # 站位地图：可部署格、敌人路线、特殊地块与镜头
│   ├── icons/                  # 干员、装备、敌人等图标
│   ├── model/                  # OCR 模型
│   └── fetch_spdatabase.py      # 数据与图片获取脚本
├── src/
│   ├── HongxueAssistant.App/        # WPF 界面
│   ├── HongxueAssistant.Core/       # 业务逻辑与数据层
│   └── HongxueAssistant.Tests/      # 数据、视图模型和 WPF 回归测试
├── docs/images/                # 无个人信息的界面截图
├── LICENSE                     # Apache 官方标准 License 2.0 正文
├── LICENSE.upstream            # 保留上游许可原文及版权声明
├── NOTICE                      # 上游与第三方归属
```

个人追踪、阵容、窗口及主题设置存放在本机用户数据目录，不随源码分发。不要把个人配置、未经脱敏的游戏截图、日志、密钥或构建缓存提交到仓库。

## 开发依赖与致谢

- [Moonfair/ASP_Assistant](https://github.com/Moonfair/ASP_Assistant)：本项目的上游代码与设计基础。
- [MaaFramework](https://github.com/MaaXYZ/MaaFramework)：OCR、模板与特征匹配。
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)：MVVM 基础设施。
- [Noto Sans SC](https://fonts.google.com/noto/specimen/Noto+Sans+SC)：内置界面字体（子集），SIL Open Font License 1.1，许可文本随字体文件分发。
- [PRTS Wiki](https://prts.wiki/) 与 [SPDatabase](https://github.com/Yanstory/SPDatabase)：游戏数据和图片来源，具体接口保留在数据脚本中。
- xUnit、Moq、FluentAssertions 等测试依赖遵循各自许可证。

## 许可证

本项目的修改部分以 **[Apache License 2.0](LICENSE)** 开源。`LICENSE` 采用 [Apache 官方标准正文](https://www.apache.org/licenses/LICENSE-2.0.txt)；上游仓库原许可文本的部分措辞与标准正文不同，已逐字保留于 [LICENSE.upstream](LICENSE.upstream)，其中的原始版权声明及上游许可条件继续保留。

分发源码或构建产物时，请一并保留 `LICENSE`、`LICENSE.upstream`、`NOTICE` 以及相关文件中的版权和变更声明。

软件许可证不改变《明日方舟》游戏数据、名称、图像和其他第三方资源的权利归属；各依赖遵循其各自的许可证。本项目仅供学习和个人使用，与 Hypergryph / 鹰角网络无关。
