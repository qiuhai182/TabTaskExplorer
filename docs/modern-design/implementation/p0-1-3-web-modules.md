# P0-1 第 3 步：前端模块化

状态：已实测通过（2026-11-04）
日期：2026-11-03

## 1. 拆分结果

原先 `WebPageHtml.cs` 内的 `<style>` 与 `<script>` 已拆成独立静态资源：

| 资源 | 职责 |
| --- | --- |
| `Assets/web/index.html` | 页面结构（与原页面完全一致） |
| `Assets/web/app.css` | 全部样式（含深浅主题） |
| `Assets/web/core.js` | 常量、图标路径、工具函数、会话/API、导航动作 |
| `Assets/web/views.js` | 标签条、导航、文件列表、表格、磁贴、磁盘视图 |
| `Assets/web/panels.js` | 目录/文件比对、进程、任务看板、连接管理、启动项 |
| `Assets/web/actions.js` | 选择、右键菜单、地址栏、批量改名、编码转换、只读/可写编辑器 |
| `Assets/web/ide.js` | IDE 资源树、编辑器标签、查找替换、快速打开 |
| `Assets/web/boot.js` | 快捷键、事件挂接、页面启动 |

加载顺序固定为：

```text
core → views → panels → actions → ide → boot
```

采用经典脚本（非 ES module），因为原单文件脚本大量使用顶层 `let`/`const` 与跨函数全局状态；直接拆成同名脚本可保持行为不变，避免引入 import/export 的全局重构风险。后续如需 ES module，可在编译验证通过后逐步迁移。

## 2. 服务端接入

| 文件 | 改动 |
| --- | --- |
| `src/TabTaskExplorer.App/Services/WebStaticAssets.cs` | 新增：从嵌入资源加载 index/css/六个 JS 模块；计算内容哈希版本；白名单读取 |
| `src/TabTaskExplorer.App/Services/WebServiceHost.cs` | `/` 优先返回外部资源入口页；新增 `/assets/web/*` 路由；`/api/version` 返回版本与能力 |
| `src/TabTaskExplorer.App/TabTaskExplorer.App.csproj` | 新增 8 个 `EmbeddedResource` 条目，使用固定 `LogicalName`（`web.index.html`、`web.app.css`、`web.core.js` 等） |

### 缓存与版本

- 入口页中的样式/脚本 URL 带 `?v=__VERSION__`。
- 版本号 = 全部资源内容 SHA-256 的前 10 位。
- 查询串版本匹配时返回：
  `Cache-Control: public, max-age=31536000, immutable`
- 版本不匹配或直接访问时返回 `no-store`，避免浏览器缓存旧页面导致新功能不出现。

### 端口占位符

`core.js` 中的 `PORT_LABEL = '127.0.0.1:__PORT__'` 在服务端读取资源时替换为实际端口；样式与 HTML 不依赖端口。

## 3. 回退与兼容

为保证无构建验证时仍可运行：

1. `/` 路由先尝试 `WebStaticAssets.TryBuildPage`。
2. 只要 8 个资源中任意一个缺失，`IsAvailable = false`，服务端回退到 `WebPageHtml.Content` 的内联页面。
3. 内联页面仍保留完整 CSS/JS，并且已同步第 1、2 步的会话令牌与 SSE 改动。
4. 待 `dotnet build` 与浏览器验证通过后，可删除 `WebPageHtml.Content` 常量及其内联内容，减少重复维护。

> 注意：当前处于过渡期，修改前端行为时必须同时修改 `Assets/web` 与 `WebPageHtml.cs` 内联副本；验证通过后删除内联副本即可消除重复。

## 4. 回退方式

1. 删除 `Assets/web` 目录与 `WebStaticAssets.cs`。
2. 删除 csproj 中 8 个 `EmbeddedResource` 条目。
3. 将 `WebServiceHost.cs` 的 `/` 路由恢复为仅返回 `WebPageHtml.Content`；删除 `/assets/web/*` 路由与 `/api/version` 中的静态资源字段。
4. 内联页面未删除，因此上述回退后功能与拆分前一致。

## 5. 待验证清单（沙箱恢复后执行）

- [ ] `dotnet build` 0 错误；确认 8 个嵌入资源均被打包（发布产物中无需额外文件）。
- [ ] 浏览器强制刷新后加载新入口页，六个脚本按顺序执行无报错。
- [ ] `/api/version` 返回 `staticAssets=true` 与 10 位版本号。
- [ ] 样式、图标、深浅主题、文件列表、IDE、右键菜单、批量改名、编码转换全部正常。
- [ ] 修改任一静态资源后重新构建，版本号变化，浏览器不再使用旧缓存。
- [ ] 删除一个嵌入资源后，服务端能回退到内联页面（仅用于验证回退路径）。
