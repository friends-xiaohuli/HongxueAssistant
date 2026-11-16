# 本地增强与后续同步

当前上游基底：`v1.9.0`，提交 `13e97c49620d6b99b4d29e7811464f063f5e86cd`。
本地源码版本：`1.9.0-alpha2`。行为对比和保留理由见 [同步审计](UPSTREAM_1.9.0_AUDIT.md)。

## 同步原则

- 常规功能、界面、Ban、吸附与模拟器连接优先采用上游实现。窗口焦点按本地需求补强：从其他软件直接点回助手或所属弹窗也可恢复置顶，切走后沉底。
- 配置固定为 `%APPDATA%\HongxueAssistant`，使用上游的安全迁移、备份、缺项合并和阵容删除标记保护。
- 保留完整森空岛攻略及作者真实回合独立编辑、复制、撤销、保存、分享、修补与追踪，兼容上游候选装备和目标盟约。
- 保留已由用户图片验证的完整盟约池补扫、同屏滚动、切屏丢弃旧 OCR 和隐藏 owner 置顶修复。
- 更新检查、下载与线路诊断固定使用本仓库 Releases；13 条线路测试同一个实际安装包。

## Git

- origin: https://github.com/friends-xiaohuli/HongxueAssistant.git
- upstream: https://github.com/bingyunhe/hongxue-stronghold-companion.git
- upstream 推送 URL 为 `DISABLED`。保留真实上游祖先，后续可继续 fetch / merge。普通同步不强推、不重写历史。

```powershell
git switch main
git status --short
git fetch upstream
git log --oneline HEAD..upstream/main
git merge upstream/main
dotnet test src/HongxueAssistant.Tests/HongxueAssistant.Tests.csproj -c Release --filter "Category!=LocalScreenshot"
python -m unittest discover -s data/tests -v
git push origin main
```

解决冲突时检查本地保留项与上游新数据字段的交叉兼容；统一 namespace / 项目目录为 HongxueAssistant，保留来源归属声明。

## 版本与交付

日常修改只编译和测试；用户明确要求打包后才执行 publish。版本取所用上游版本加 `-alphaN`，更换上游基底后从 `alpha1` 开始；同一基底依次递增 `alpha2`、`alpha3`。更新程序按数字比较编号，`alpha10` 高于 `alpha2`，兼容旧 `local.N`。

以当前基底打包时：

```powershell
dotnet publish src/HongxueAssistant.App/HongxueAssistant.App.csproj -c Release -r win-x64 --self-contained true -p:Version=1.9.0-alpha2 -o publish/HongxueAssistant-v1.9.0-alpha2-win-x64
./tools/Verify-Package.ps1 -PackageDirectory publish/HongxueAssistant-v1.9.0-alpha2-win-x64
```

ZIP 与顶层文件夹同名，实际程序为 `HongxueAssistant.exe`。保留 data、runtimes、MaaAgentBinary、VC++ 运行库、LICENSE、LICENSE.upstream、NOTICE。
只有明确推送 `v*` 发布标签才触发 Release。alphaN 是本仓库增强编号；发布为普通 Release 才会进入 `/releases/latest`，GitHub 的预发布不会进入该接口。

## 配置与验证

- 主配置为 `%APPDATA%\HongxueAssistant`；只从旧 `%APPDATA%\ASPAssistant` 补入受支持的缺失设置和阵容，不覆盖已有值，也不自动切换到上游的 HongxueCompanion。
- `HONGXUEASSISTANT_SETTINGS_DIR` 优先，兼容 `ASPASSISTANT_SETTINGS_DIR`；单实例旧标识仅用于兼容。
- `.tools`、`publish`、`bin`、`obj` 和用户配置不提交。所有迁移测试使用隔离临时目录。
- `Category=LocalScreenshot` 测试依赖未公开样本，不进入公共 CI；合成滚动图与 Win32 测试不能代替真实 MuMu / 雷电或 PC 游戏验证。
