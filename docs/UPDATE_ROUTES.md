# GitHub 更新线路与可用性测试

当前源码版本：1.8.0-alpha3（未打包）。已发布安装包：v1.8.0-alpha2。

## 默认目标

- 版本查询：`https://api.github.com/repos/friends-xiaohuli/HongxueAssistant/releases/latest`。
- 下载测试：从本仓库最新 Release 自动解析 Windows x64 ZIP，目前为 `https://github.com/friends-xiaohuli/HongxueAssistant/releases/download/v1.8.0-alpha2/HongxueAssistant-v1.8.0-alpha2-win-x64.zip`。
- 所有线路测试同一份安装包，版本接口失败的代理也会尝试这个真实下载地址。
- 已移除 ghproxy.net，默认保留 13 条线路。不再使用其他项目的公共下载样本。
- 无发布版本、没有 Windows x64 包或无法确定下载地址时，下载列显示“未测试”，不会以其他资源代替。

设置页点击“测试更新线路”，最多并发 4 条、单请求 6 秒、总计 45 秒，支持取消。下载测试只读取 ZIP 文件头，不下载完整安装包或运行程序。实际更新仍须验证 SHA256 校验清单。

## 当前实测

2026-11-15 03:05（Asia/Shanghai），正常 HTTPS 验证、无认证、当前主机网络。13 条线路约 8.2 秒完成，版本接口可用 8 条，实际安装包 ZIP 文件头可用 4 条。可用状态只代表本次测量。

| 线路 | 本仓库版本接口 | 本仓库实际安装包 |
| --- | --- | --- |
| GitHub 直连 | 可用（发布 JSON 已验证） | 超时 |
| github.xxlab.tech | HTTP 403 | 可用（本仓库安装包 ZIP 文件头已验证） |
| cors.isteed.cc | HTTP 403 | 可用（本仓库安装包 ZIP 文件头已验证） |
| ghfast.top | HTTP 403 | 可用（本仓库安装包 ZIP 文件头已验证） |
| github.chenc.dev | 可用（发布 JSON 已验证） | 可用（本仓库安装包 ZIP 文件头已验证） |
| gh-proxy.com | 可用（发布 JSON 已验证） | HTTP 404 |
| gh-proxy.org | 可用（发布 JSON 已验证） | HTTP 404 |
| fastgit.cc | 返回的不是发布 JSON（可能是代理网页） | HTTP 404 |
| gh.noki.icu | 可用（发布 JSON 已验证） | HTTP 404 |
| githubproxy.gohj99.site | HTTP 402 | HTTP 404 |
| GH-Proxy CDN（同服务节点） | 可用（发布 JSON 已验证） | HTTP 404 |
| GH-Proxy 香港（同服务节点） | 可用（发布 JSON 已验证） | HTTP 404 |
| GH-Proxy EdgeOne（同服务节点） | 可用（发布 JSON 已验证） | HTTP 404 |

## 发布校验文件

安装包的完整性验证依赖同一个 Release 中的 `SHA256SUMS.txt`。初次核对 alpha2 时只有 ZIP；最终复核已确认校验清单补齐。本机 `publish/SHA256SUMS.txt` 保留对应记录。下载线路测试仅检查文件头，实际自动更新仍须完整下载并通过 SHA256 校验。

## 历史研究记录

以下为 alpha2 发布前的研究快照，包含曾使用的其他项目公共样本和旧线路；它们不再代表当前默认配置。


日期：2026-11-14（Asia/Shanghai）。这是当前主机网络的测量快照，不能保证其他网络或未来时刻可用。

已检查原有 10 个代理和 GitHub 直连，并补充 3 个通过真实路径测试的 GH-Proxy 节点。新增节点属于同一服务的不同线路，不能当作三家独立服务。

## 程序内测试

在设置页点击“测试更新线路”，查看每条线路的“版本接口”和“安装包下载／公共下载样本”结果。最多并发 4 条线路、单请求 6 秒、测试总时限 45 秒；可随时取消。

程序优先直连，连接失败后尝试代理。对版本 JSON 校验结构，对本仓库资源校验来源路径；下载前检查 ZIP 文件头，完整包仍须通过本次 Release 的 SHA256 清单校验。不会执行诊断用公共样本。

对当前仓库返回的 JSON 404，需要同线路确认仓库名称后才显示“暂无发布版本”。如果接口与下载支持范围不同，两列独立显示。以下研究快照使用的请求时限与程序诊断略有不同，实际使用时以本机测试为准。

## 测试对象与边界

- 公共 API 对照：`https://api.github.com/repos/cli/cli/releases/latest`；测得标签 `v2.104.0`，发布时间 2026-11-12。
- 实际更新源：`https://api.github.com/repos/friends-xiaohuli/HongxueAssistant/releases/latest`；仓库存在，当日暂无 Release。规范 JSON 404 须结合同线路仓库 `full_name` 校验，不能把它直接当网络故障。
- 小型公开下载样本：`https://github.com/benbuck/rbtray/releases/download/v4.14/x64.zip`，官方 API 列示 95,252 字节。仅发起 Range `bytes=0-3`，以 ResponseHeadersRead 流式读取四字节后释放。
- 不安装、不执行样本；样本不作为 HongxueAssistant 更新包。四字节 `504B0304` 仅验证返回 ZIP 形状，不能证明整个文件完整或可信。
- 使用 Windows/.NET 的正常 HTTPS 证书验证，不跳过证书；NoProxy、无认证/Token。连接超时 4–5 秒、单次 7–8 秒，主体矩阵并发 8、全局 65 秒；主体矩阵约 16.2 秒完成。
- 不测试站点首页来推断线路能力。API 请求与 ZIP 下载分别判定；HTML 错误页、挑战页面、假 200 与错误签名不会判为可用。

## 应用内集成诊断快照

2026-11-14 23:38（Asia/Shanghai），使用最终构建中的诊断服务、正常 HTTPS 验证和无认证直连网络测试全部 14 条线路，约 19.9 秒完成：版本接口可访问 7 条，公共 ZIP 文件头可读取 11 条。当前仓库暂无 Release，所有下载测试均为公共样本。

| 线路 | 本仓库版本接口 | 公共下载样本 |
| --- | --- | --- |
| GitHub 直连 | 可访问，仓库暂无发布版本 | 超时 |
| github.xxlab.tech | HTTP 403 | 超时 |
| cors.isteed.cc | HTTP 403 | 可用（仅验证 ZIP 文件头） |
| ghfast.top | HTTP 403 | 可用（仅验证 ZIP 文件头） |
| github.chenc.dev | 可访问，仓库暂无发布版本 | 可用（仅验证 ZIP 文件头） |
| gh-proxy.com | 可访问，仓库暂无发布版本 | 可用（仅验证 ZIP 文件头） |
| gh-proxy.org | 可访问，仓库暂无发布版本 | 可用（仅验证 ZIP 文件头） |
| fastgit.cc | 返回的不是发布 JSON（可能是代理网页） | 可用（仅验证 ZIP 文件头） |
| gh.noki.icu | 可访问，仓库暂无发布版本 | 可用（仅验证 ZIP 文件头） |
| githubproxy.gohj99.site | 超时 | 可用（仅验证 ZIP 文件头） |
| GH-Proxy CDN（同服务节点） | HTTP 403 | 可用（仅验证 ZIP 文件头） |
| GH-Proxy 香港（同服务节点） | 可访问，仓库暂无发布版本 | 可用（仅验证 ZIP 文件头） |
| GH-Proxy EdgeOne（同服务节点） | 可访问，仓库暂无发布版本 | 可用（仅验证 ZIP 文件头） |

与稍早研究快照不同，CDN 节点此次版本接口返回 403，说明线路状态会变化；保留独立诊断结果，不把一时通过当作永久可用。下表研究快照也保留，便于对照。

## 原有线路与新增节点

| 线路 | 公共 Release API | 本项目 latest | 公共 ZIP 下载 | API/ZIP 耗时 |
| --- | --- | --- | --- | --- |
| GitHub direct | 200 / Release JSON | 404 / GitHub暂无发布 | 连接/请求超时 | 1214/5002 ms |
| github.xxlab.tech | 403 / 非Release内容 | 403 / 非Release内容 | 连接/请求超时 | 299/8001 ms |
| cors.isteed.cc | 403 / 非Release内容 | 403 / 非Release内容 | 206 / ZIP文件头有效 | 1305/3714 ms |
| ghfast.top | 403 / 非Release内容 | 403 / 非Release内容 | 206 / ZIP文件头有效 | 1381/488 ms |
| github.chenc.dev | 200 / Release JSON | 404 / GitHub暂无发布 | 206 / ZIP文件头有效 | 7491/2825 ms |
| gh-proxy.com | 200 / Release JSON | 404 / GitHub暂无发布 | 206 / ZIP文件头有效 | 1410/231 ms |
| gh-proxy.org | 200 / Release JSON | 404 / GitHub暂无发布 | 206 / ZIP文件头有效 | 1168/598 ms |
| fastgit.cc | 404 / 非Release内容 | 404 / 非Release内容 | 206 / ZIP文件头有效 | 1680/1577 ms |
| gh.noki.icu | 200 / Release JSON | 404 / GitHub暂无发布 | 206 / ZIP文件头有效 | 1943/527 ms |
| githubproxy.gohj99.site | 200 / Release JSON | 404 / JSON，需校验仓库 | 206 / ZIP文件头有效 | 4902/7855 ms |
| cdn.gh-proxy.org（新增） | 200 / Release JSON | 404 / GitHub暂无发布 | 206 / ZIP文件头有效 | 797/207 ms |
| edgeone.gh-proxy.org（新增） | 200 / Release JSON | 404 / GitHub暂无发布 | 206 / ZIP文件头有效 | 796/151 ms |
| hk.gh-proxy.org（新增） | 200 / Release JSON | 404 / GitHub暂无发布 | 206 / ZIP文件头有效 | 797/513 ms |

`githubproxy.gohj99.site` 还会重写 JSON 正文里的 GitHub 域名。其 404 中 `documentation_url` 被改为 `docs.github.gohj99.site`；同线路仓库 API 返回的 `full_name` 已核实为 `friends-xiaohuli/HongxueAssistant`。即使 JSON 形状有效，实际更新解析仍须保留官方仓库 URL/资产校验，不能为迁就该节点而放宽到任意域名。

初次 Python 探测对本机 GitHub 证书链有差异，直连 API 出现 Python CA 错误；随后 Windows/.NET 正常验证得到 200。因此以 `endpoint-probes-windows.json` 为应用行为参考，保留 Python 结果仅作诊断，不能据此称 GitHub 证书失效。

## 不建议默认加入的候选

| 候选 | 原因 |
| --- | --- |
| github.akams.cn | 是节点聚合网站；按通用前缀拼接的 API/下载路径均 404，不能当普通代理。 |
| ghproxy.cc | HTTPS 证书域名不匹配，保持拒绝，不绕过验证。 |
| gh.ddlc.top | API、下载返回 429，当前限流。 |
| gh.llkk.cc | 当前连接超时。 |
| gh-proxy.net | 当前重定向/连接未通过；不作默认可用节点。 |
| ghproxy.homeboyc.cn | API 与下载均 403，正文 blocked。 |

## 使用方式与来源

GH-Proxy 官方文档明确采用“代理域名/完整 GitHub URL”拼接，列出 Release Assets 和 `api.github.com` 支持。API 示例直接使用 `https://gh-proxy.org/https://api.github.com/repos/cli/cli/releases/latest`。

- GH-Proxy 使用说明：<https://gh-proxy.com/docs/github-accelerator/>。
- GH-Proxy 站点自述：<https://gh-proxy.org/>。
- 额外节点接入：`https://cdn.gh-proxy.org/`、`https://hk.gh-proxy.org/`、`https://edgeone.gh-proxy.org/`，均已直接请求验证，不依赖 .com 重定向。
- 聚合站自述：<https://github.akams.cn/>，宣称汇总 API/Release/Raw 等源，但实测不能把它当统一前缀。
- GHProxy 地址发布页：<https://ghproxy.link/>，是地址说明页，不是下载代理节点。
- 公开下载样本项目：<https://github.com/benbuck/rbtray>，只读查询该项目最新 Release API 确认资产；不使用二次转载下载链接。

搜索发现的第三方清单仅用于发现候选；可用结论来自本次官方/站点自身路径请求。没有假定站点关于速度、隐私或不篡改的宣传已被验证。


程序不会自动刷新第三方节点清单，也不会转发账号认证信息。新增节点应先核对服务自身接入说明，再用真实 API 与下载路径进行测量。
