# 本地增强与后续同步

基底 v1.8.0: 11be6018c8ebdf0085ef0aabc93f9ceef3212dfa。
保留目标仓库后续测试/文档修复至 a0dfcfb125bed8309017c538fdbea4337c4e8428。

## 增强

- MuMu 官服/B服、逻辑屏与物理截图编号分离；当前操作游戏页跟随/手动选屏。
- 自动滚动绑定同一屏幕，切屏拒绝输入及旧 OCR。
- 已知模式完整池校验、45 秒同来源补扫、回顶部确认，未知池仅预览。
- 普通 Ban 保留上游全部盟约禁用规则，明确二选一页补充核心组判定，手动覆盖保留。
- 被 Ban 必带留在栏中但不计入，解除后恢复；主动取消/清空仍移除。
- 默认不置顶，游戏聚焦后置顶，助手及所属窗口保持；其他应用聚焦取消并沉底。
- 释放拖动时 12 DIP 内吸附，调整大小不吸附，两种窗口模式可拖动。
- 新版商店算法优先，ADB 无结果时使用校准 MuMu 兜底。
- 保留新版规划、路线、自选、策略棋子、部署、托盘及追踪来源/已获取。

## Git

origin: https://github.com/friends-xiaohuli/HongxueAssistant.git
upstream: https://github.com/bingyunhe/hongxue-stronghold-companion.git

main 为增强主线。旧 codex/sync-bing-1.7.1 与独立 bundle 备份保留。
后续同步：

    git switch main
    git status --short
    git fetch upstream --tags
    git log --oneline HEAD..upstream/main
    git merge upstream/main
    dotnet test src/HongxueAssistant.Tests/HongxueAssistant.Tests.csproj -c Release --filter "Category!=LocalScreenshot"
    git push origin main

按功能解决冲突，先检查上游已有修复，不整块覆盖本地连接、规划及窗口补丁。不强推、不重写历史。

## 构建

    dotnet publish src/HongxueAssistant.App/HongxueAssistant.App.csproj -c Release -r win-x64 --self-contained true -p:Version=1.8.0-local.1 -o publish/win-x64

保留 data、runtimes、MaaAgentBinary、VC++ 运行库、LICENSE、LICENSE.upstream、NOTICE。
.tools、publish、bin、obj 和用户设置不提交。更新检查使用本仓库 Releases，上游更新走 Git。
测试区分公共/本地样本，合成滚动图不算实机证据，未连接模拟器时不声明实机验证。

## 更名兼容

解决方案 HongxueAssistant.sln；项目为 HongxueAssistant.App/Core/Tests；实际 EXE 和应用程序集为 HongxueAssistant.exe / HongxueAssistant.dll。
默认用户目录改为 HongxueAssistant：首次启动在暂存目录完整复制旧 ASPAssistant 配置和阵容后一次性切换，旧目录保留且不覆盖已存在的新目录。
旧环境变量与旧单实例锁仅用于升级兼容，不再作为显示名称。检查更新为 friends-xiaohuli/HongxueAssistant 的 Releases。

## 发布检查

    ./tools/Verify-Package.ps1 -PackageDirectory publish/HongxueAssistant

压缩包以 HongxueAssistant 开头并包含单独的 HongxueAssistant 文件夹。
本地 Release 标签 v1.8.0-local.1、v1.8.0-local.2 按修订号比较；默认更新接口为本仓库 /releases/latest。
旧目录/环境变量/单实例名称仅在升级兼容代码中保留，未更改原库 URL 的来源归属说明。
