# Unity 3D 虚拟现场巡检

无 AR 硬件条件下的巡检演示：Unity 负责 3D 现场和问题标记，Go + SQLite
负责问题 API 与持久化，React 负责管理端。三端共用同一套问题数据。

## 1. 使用版本

| 组件 | 版本 |
| --- | --- |
| Unity | `6000.0.30f1` (`62b05ba0686a`) |
| Unity Test Framework | `1.4.5` |
| Go | `1.27.3` |
| Node.js / npm | `24.22.1` / `11.19.1` |
| React / React DOM | `19.3.1` |
| TypeScript | `7.0.2` |
| Vite | `8.3.5` |
| Vitest | `5.0.5` |
| Playwright | `1.64.1`（使用本机 Chrome） |
| SQLite driver | `modernc.org/sqlite 1.61.0` |
| UUID | `github.com/google/uuid 1.6.0` |

工具版本以 `ProjectSettings/ProjectVersion.txt`、`backend/go.mod` 和
`web/package-lock.json` 为准。

## 2. 启动三个部分

### 2.1 准备本地工具链

首次使用或缺少 `.tools/` 时：

```powershell
.\scripts\install-toolchain.ps1
```

当前终端启用固定版本 Go 和 Node：

```powershell
. .\scripts\use-toolchain.ps1
```

### 2.2 启动 Go 后端

先在一个 PowerShell 窗口启动后端：

```powershell
. .\scripts\use-toolchain.ps1
cd backend
go run ./cmd/server
```

默认监听 `http://127.0.0.1:8080`，数据库为
`backend/data/issues.db`。健康检查：

```powershell
curl.exe http://127.0.0.1:8080/healthz
# {"status":"ok"}
```

### 2.3 启动 React 管理端

在另一个 PowerShell 窗口执行：

```powershell
. .\scripts\use-toolchain.ps1
cd web
npm ci
npm run dev
```

默认访问 `http://localhost:5173`。若后端地址不同，设置：

```powershell
$env:VITE_API_BASE_URL = "http://127.0.0.1:8080"
npm run dev
```

### 2.4 启动 Unity 现场

使用 Unity Hub 打开仓库根目录，选择 Unity `6000.0.30f1`；也可以从命令行打开：

```powershell
$unity = '<Unity 安装目录>\Editor\Unity.exe'
& $unity -projectPath (Get-Location)
```

打开 `Assets/Scenes/Inspection.unity` 后进入 Play Mode。操作方式：

- `W/A/S/D` 移动，`Shift` 加速，按住鼠标右键拖动视角。
- 左键点击场景表面创建问题草稿；点击已有标记查看详情。
- `F5` 从后端同步；`Esc` 关闭详情或取消草稿。
- 表单失败后保留草稿、输入和幂等键，可直接重试。

如需重新生成场景，使用 `Tools > Unity Inspection > Rebuild Inspection Scene`。

构建并启动 Windows Player：

```powershell
.\scripts\build-unity.ps1
.\Builds\Windows\UnityInspection.exe
```

Player 默认访问 `http://127.0.0.1:8080`，启动前应确保当前代码的后端已在运行。

## 3. 完成情况、未完成项和已知问题

### 已完成

- Unity 固定巡检场景、第一人称移动、射线放置、单草稿、详情查看和标记同步。
- 优先级/状态视觉区分，问题详情，设备编号与区域识别，历史标记恢复。
- Go REST API、请求校验、幂等创建、状态更新、删除、CORS 和错误响应。
- SQLite WAL、事务、旧库迁移、并发创建和重启持久化。
- React 问题列表、筛选、详情、状态修改、删除、错误提示和响应式界面。
- Windows Player 构建，以及 Unity → Go → SQLite → Unity/React 联调。
- Go、React、Playwright 和 12 个 Unity PlayMode 自动化测试。

### 未完成

- 真实 AR 设备上的平面检测、锚点恢复和 Tracking 异常处理。
- 演示视频、最终分享链接、访问码和有效期。
- 生产级鉴权、权限控制、部署和监控。

### 已知问题

- 当前是本地演示系统，API 没有鉴权，不要直接暴露到公网。
- `go test ./...` 和 `go vet ./...` 通过；Windows 下
  `go test -race` 仍可能以 `0xc0000139` 启动失败，需在 Linux CI 补跑竞态检查。
- 在较慢或冷启动环境中，`npm test` 的首个界面用例可能超过 Vitest 默认的
  5 秒。可用 `npm test -- --testTimeout=15000` 复跑，后续应在测试配置中调整超时。
- Unity Windows 构建成功，但仍有 5 条警告：1 条未使用变量，以及 4 条因
  `InspectionChineseFont`、`WorldLabelBillboard` 与主类共处一个脚本文件而出现的
  “脚本缺失”提示。建议将它们拆成同名独立脚本并重建场景。
- Unity 当前使用 `6000.0.30f1`，不是早期方案中的 `6000.3.27f1`。
- 本机可能使用 Microsoft Basic Render Driver，视频解码会回退到软件路径，不影响当前场景和 UI。

### 快速验证

```powershell
. .\scripts\use-toolchain.ps1

# 后端
cd backend
go test ./...
go vet ./...

# 前端
cd ..\web
npm run typecheck
npm run lint
npm test
npm run build
npm run test:e2e

# Unity
cd ..
.\scripts\test-unity.ps1
```

## 4. 后端地址与接口设计

默认 Base URL：

```text
http://127.0.0.1:8080
```

后端只监听本机回环地址。主要配置：

| 变量 | 默认值 | 说明 |
| --- | --- | --- |
| `PORT` | `8080` | HTTP 端口 |
| `DB_PATH` | `./data/issues.db` | SQLite 文件 |
| `CORS_ORIGINS` | `http://localhost:5173,http://127.0.0.1:5173` | 允许的管理端来源 |
| `VITE_API_BASE_URL` | `http://127.0.0.1:8080` | React 使用的 API 地址 |

### 接口

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| `GET` | `/healthz` | 服务和数据库健康检查 |
| `GET` | `/api/issues` | 按 `createdAt`、`id` 倒序返回问题列表 |
| `POST` | `/api/issues` | 创建问题 |
| `PATCH` | `/api/issues/{id}/status` | 更新状态 |
| `DELETE` | `/api/issues/{id}` | 删除问题 |

创建问题必须携带 UUID 格式的 `Idempotency-Key`：

```http
POST /api/issues
Content-Type: application/json
Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
```

```json
{
  "title": "1 号机柜温度异常",
  "description": "机柜右侧温度明显偏高",
  "priority": "high",
  "deviceCode": "CAB-A",
  "zone": "A区 · 配电区",
  "position": { "x": 1.25, "y": 0.8, "z": 2.4 }
}
```

`priority` 可为 `low`、`medium`、`high`；新问题状态默认为 `open`。
状态更新只接受 `open`、`in_progress`、`resolved`。

首次创建返回 `201`；相同幂等键和相同载荷的重试返回原问题及 `200`；
相同幂等键但载荷不同返回 `409 IDEMPOTENCY_CONFLICT`。删除成功返回
`204 No Content`。

统一错误格式：

```json
{
  "error": {
    "code": "INVALID_ARGUMENT",
    "message": "title is required"
  }
}
```

## 5. AR 基础问答

这是无 AR 设备备选版，以下回答来自方案说明，尚未在真实 AR 设备上验证。

1. **真实 AR 的平面检测与点击放置通常需要哪些能力？**

   通常使用 AR Foundation 的 `ARSession`、`XROrigin`、`ARPlaneManager`、
   `ARRaycastManager` 和 `ARAnchorManager`。平面管理器负责检测环境平面，
   屏幕射线通过 raycast manager 找到放置平面，锚点负责稳定后续标记位置。

2. **为什么只保存 Unity 的 `x/y/z` 不能保证下次启动后仍在真实世界同一位置？**

   这些坐标依赖当次会话的跟踪原点。设备重启、跟踪丢失、重新建图或原点变化后，
   相同坐标可能对应不同的真实位置。稳定恢复通常需要持久化锚点、世界地图重定位
   或云锚点。

3. **AR 初始化失败、设备不支持或 Tracking 异常时如何提示？**

   应检查 `ARSession.state` 和 `notTrackingReason`，暂停放置功能，明确显示原因
   和重试入口；设备不支持时提供普通 3D 场景，或提示稍后重试的降级方案。

完整设计过程见 [DEVELOPMENT_PLAN.md](./DEVELOPMENT_PLAN.md)，AI 辅助过程见
[AI_LOG.md](./AI_LOG.md)。
