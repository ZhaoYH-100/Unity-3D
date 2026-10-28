# Unity 3D 虚拟现场巡检

本仓库实现“无 AR 设备备选版”巡检题的非 Unity 部分：Go 后端、SQLite
持久化、React 管理端、自动化测试和工程文档。

当前请求明确暂不创建 Unity 工程，因此场景、射线放置和 Unity 表单仍待完成。
完整实施方案见 [DEVELOPMENT_PLAN.md](./DEVELOPMENT_PLAN.md)。

## 当前完成情况

| 部分 | 状态 | 说明 |
| --- | --- | --- |
| 本地工具链 | 已准备 | Go 1.27.3、Node 24.22.1、npm 11.19.1，安装在忽略提交的 `.tools/` |
| Go API | 已完成 | 健康检查、创建、列表、状态更新、严格校验、CORS、幂等键 |
| SQLite | 已完成 | WAL、busy timeout、单连接事务、重启持久化 |
| React 管理端 | 已完成 | 列表、加载/空/错误状态、状态更新、超时、Error Boundary |
| 自动化测试 | 已完成 | Go 测试、React 组件测试、真实 Chrome 端到端测试 |
| Unity 客户端 | 未开始 | 本轮按用户要求排除 |
| 演示视频与分享链接 | 未开始 | 需要 Unity 完成并选定发布渠道后处理 |

## 版本

| 工具/依赖 | 版本 |
| --- | --- |
| Go | 1.27.3 |
| Node | 24.22.1 |
| npm | 11.19.1 |
| React / React DOM | 19.3.1 |
| Vite | 8.3.5 |
| Vitest | 5.0.5 |
| Playwright | 1.64.1（使用本机 Chrome） |
| SQLite driver | `modernc.org/sqlite` 1.61.0 |
| UUID | `github.com/google/uuid` 1.6.0 |
| Unity（已选定，安装进行中 0/3） | Unity 6.3 LTS `6000.3.27f1` |

## 本地工具链

本机工具链位于 `.tools/`，不会提交到 Git。新环境可执行：

```powershell
.\scripts\install-toolchain.ps1
```

脚本从官方地址下载固定版本的 Go 和 Node，校验 SHA-256 后解压。
当前终端需要启用工具链时，执行：

```powershell
. .\scripts\use-toolchain.ps1
```

## 启动后端

```powershell
. .\scripts\use-toolchain.ps1
cd backend
go run ./cmd/server
```

默认地址：`http://127.0.0.1:8080`

健康检查：

```powershell
curl.exe http://127.0.0.1:8080/healthz
```

默认数据库为 `backend/data/issues.db`。修改 `DB_PATH` 可以切换数据库。
运行时数据库及 WAL/SHM 文件不会提交到 Git。

## 启动管理端

另开一个 PowerShell：

```powershell
. .\scripts\use-toolchain.ps1
cd web
npm ci
npm run dev
```

浏览器访问 Vite 输出的地址，默认是 `http://localhost:5173`。
如果 API 地址不同，设置 `VITE_API_BASE_URL`，示例见 `web/.env.example`。

## API

### `GET /healthz`

返回：

```json
{"status":"ok"}
```

### `POST /api/issues`

必须发送 `Idempotency-Key` UUID 请求头。请求体：

```json
{
  "title": "1 号机柜温度异常",
  "description": "机柜右侧温度明显偏高",
  "priority": "high",
  "position": {"x": 1.25, "y": 0.8, "z": 2.4}
}
```

首次创建返回 `201`。相同幂等键、相同载荷的重试返回 `200` 和原问题；
相同键但载荷不同返回 `409 IDEMPOTENCY_CONFLICT`。

### `GET /api/issues`

返回：

```json
{"items":[]}
```

结果按 `createdAt`、`id` 倒序稳定排列。

### `PATCH /api/issues/{id}/status`

请求体：

```json
{"status":"in_progress"}
```

合法状态：`open`、`in_progress`、`resolved`。

统一错误响应：

```json
{"error":{"code":"INVALID_ARGUMENT","message":"title is required"}}
```

## 配置

| 变量 | 默认值 | 说明 |
| --- | --- | --- |
| `PORT` | `8080` | Go 监听端口 |
| `DB_PATH` | `./data/issues.db` | SQLite 文件 |
| `CORS_ORIGINS` | `http://localhost:5173,http://127.0.0.1:5173` | 允许的浏览器来源 |
| `VITE_API_BASE_URL` | `http://127.0.0.1:8080` | React API 地址 |

后端只监听 `127.0.0.1`。CORS 允许 `GET`、`POST`、`PATCH`、`OPTIONS`，
允许 `Content-Type` 和 `Idempotency-Key` 请求头。

## 测试

后端：

```powershell
. .\scripts\use-toolchain.ps1
cd backend
go test ./...
```

前端：

```powershell
. .\scripts\use-toolchain.ps1
cd web
npm run typecheck
npm run lint
npm test
npm run build
npm run test:e2e
```

`npm run test:e2e` 会启动临时 Go 后端与 Vite 服务，使用本机 Chrome
验证状态修改、重新加载后的持久化以及后端失败提示。

当前 Windows 环境下 `go test -race` 的测试进程在启动时返回
`0xc0000139`，因此竞态检测仍属未验证项；普通并发测试已通过。

## AR 基础问答

这是无 AR 设备备选版，以下内容来自方案说明，未进行真机验证。

1. **真实 AR 的平面检测与点击放置通常需要哪些能力？**

   通常使用 AR Foundation 的 `ARSession`、`XROrigin`、`ARPlaneManager`、
   `ARRaycastManager` 和 `ARAnchorManager`。平面管理器提供检测平面，
   屏幕射线通过 raycast manager 命中平面，锚点用于稳定标记位置。

2. **为什么只保存 Unity 的 `x/y/z` 不能保证下次启动后仍在真实世界同一位置？**

   这些坐标依赖当次会话的跟踪原点。设备重启、跟踪丢失、重新建图或
   坐标原点变化后，相同数字可能对应不同真实位置。稳定恢复通常需要
   持久化锚点、世界地图重定位或云锚点。

3. **AR 初始化失败、设备不支持或 Tracking 异常时如何提示？**

   检查 `ARSession.state` 和 `notTrackingReason`，暂停放置功能，显示
   可理解的原因、重试入口，以及不支持设备时使用普通 3D 场景或稍后重试
   的降级说明。

## 已知限制

- Unity 6.3 LTS `6000.3.27f1` 已选定，但 Hub 最新截图显示 Editor 下载 0%、整体 0/3，默认 Editor 目录尚不存在；Unity 场景、射线放置、Unity 表单和两段演示视频尚未实现。
- 当前没有最终分享链接、访问码或有效期信息。
- `go test -race` 尚未跑通；具体原因和未验证项已在上方记录。
- 当前 API 没有鉴权，只应在本地开发环境使用。

AI 辅助过程记录见 [AI_LOG.md](./AI_LOG.md)。
