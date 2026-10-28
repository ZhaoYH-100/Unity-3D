# Unity 3D 虚拟现场巡检系统开发计划

> 用途：实施方案 · 读者：开发、测试、交付评审人员
> 版本：v0.7 · 状态：三端增强功能与自动化验证已完成，演示视频和分享链接待完成 · 负责人：项目开发负责人
> 修订日期：2026-10-28
> 状态口径：全文的“已实现 / 规划中 / 待核对”逐项区分；“已验证”仅用于已有运行证据的内容
> 依据：`Unity-3D巡检实操题-候选人任务书-无AR设备备选版.md`（一手需求）
> 变更记录：v0.2 关闭首轮审查 P1–P12，新增环境门禁、提交幂等、CORS 预检、AI 日志脱敏、数据库并发、交付链接和回归验收
> 变更记录：v0.3 完成 Go 后端、SQLite、React、组件测试和真实浏览器端到端测试；Unity、视频和分享链接仍待完成
> 变更记录：v0.4 按用户选择锁定 Unity 6.3 LTS（6000.3.27f1）；该版本仍在 Hub 下载队列中，安装与运行验证待完成
> 变更记录：v0.5 根据最新 Hub 截图和本机路径检查，Editor 正在下载 `0%`、整体进度 `0/3`；VS 2026 与简体中文仍在排队
> 变更记录：v0.6 按实际可用 Unity `6000.0.30f1` 完成场景、Prefab、玩家控制、射线放置、表单、幂等提交、错误重试、PlayMode 测试和 Windows Player 构建
> 变更记录：v0.7 增加设备/区域字段、后端删除、历史标记恢复、点击详情、优先级/状态样式、React 筛选/详情/删除和旧库迁移

## 1. 目标与范围

### 1.1 要解决的问题

建立一个最小但完整的三端闭环：巡检人员在固定 Unity 3D 场景中移动并点击真实场景表面，在射线命中位置创建问题标记；填写问题后由 Go 后端校验、生成 ID 并持久化；管理人员在 React 页面中查看同一份数据并更新处理状态。

本项目的成功标准不是“有三个能启动的目录”，而是：

1. Unity 的标记位置来自 `Physics.Raycast` 命中点，而不是屏幕中心或预置点。
2. Unity 上报、Go 保存、React 展示和状态修改使用同一套字段与数据。
3. Go 服务重启后，已创建问题仍可读取。
4. UI 点击不会误触场景标记，取消草稿会清除标记，网络失败有明确反馈。
5. README、AI 使用记录和两段演示视频能够证明上述流程真实可复现。

### 1.2 文档契约

- 本文档类型：从零开始的实施方案，不是现状报告。
- 当前事实：Unity、Go、SQLite 和 React 三端闭环已实现；12 个 Unity PlayMode 测试、后端测试、React 测试、真实 Chrome 端到端测试和 Windows Player 构建均已通过。
- 适用版本：本题“无 AR 设备备选版”，以 Unity Editor/桌面运行和浏览器管理端为交付形态。
- 明确不做：真实手机 AR、账号体系、多人协作、公网部署、复杂美术、离线同步。
- 执行原则：先完成并验收 P0 必做闭环，再按剩余时间选择 P1 加分项。

### 1.3 关键质量属性

| 质量属性 | 可观察场景 | 阈值 | 当前证据 |
| --- | --- | --- | --- |
| 位置准确性 | 连续点击地面、墙面、机柜的不同位置 | 标记误差不超过点击命中点 0.02 个场景单位；上报坐标等于命中点，不含视觉偏移 | B（PlayMode 断言世界坐标与视觉偏移） |
| 端到端一致性 | Unity 创建一条问题后在 React 查看并修改状态 | 同一 ID 的字段一致；三个状态均能往返更新 | B（Unity→Go→GET 已真实联调；React 状态接口已验证） |
| 持久性与容错 | 创建问题后停止并重启 Go 服务；提交时停掉后端 | 重启后记录仍存在；客户端不崩溃、保留草稿并显示可重试错误；重试不重复创建 | B（连接失败、错误态、原草稿重试和同键重放均验证） |
| 交互可靠性 | 在 Unity 表单输入、取消、重复点击 | 表单操作不放置标记；待提交标记始终不超过 1 个；取消后为 0 个；表单打开时键盘不移动角色 | B（状态机、输入锁定和 UI 边界测试） |
| 可执行性 | 新环境按 README 启动三端 | 环境门禁通过；三端启动命令、版本、地址和已知问题齐全，按文档可复现主流程 | B（Windows Player 已构建并启动） |

## 2. 术语与状态语义

| 术语/状态 | 含义 | 运行时处理 |
| --- | --- | --- |
| 巡检现场 | 固定 Unity 场景，至少包含地面、墙、机柜和设备 | 场景对象位于 `Inspectable` 层，只有该层参与放置射线检测 |
| 命中点 | `Physics.Raycast` 返回的 `RaycastHit.point` | 使用世界坐标原值作为 `position` 上报 |
| 问题标记 | 表示问题空间位置的可视对象 | 草稿标记尚未入库；提交成功后移除草稿标记，后续可从后端恢复为只读标记 |
| 设备编号 / 区域 | 由场景中的 `InspectionLocation` 提供 | 点击场景表面时随问题一起上报；React 可筛选和展示 |
| 历史标记 | 从 `GET /api/issues` 恢复的持久化标记 | 每 8 秒自动同步；`F5` 手动刷新；点击打开只读详情 |
| `open` | 新问题，尚未开始处理 | 创建时由后端强制写入；React 将其作为合法目标状态 |
| `in_progress` | 已开始处理 | 可由 React 通过状态接口修改 |
| `resolved` | 已处理完成 | 可由 React 通过状态接口修改；不自动删除 |
| `low` / `medium` / `high` | 问题优先级 | 创建时必须为三个枚举值之一；React 默认选择 `medium` |

Unity 端“待提交标记”状态：

| 状态 | 含义 | 生成/运行时处理 |
| --- | --- | --- |
| `Idle` | 没有待提交标记 | 允许一次有效场景点击进入 `Drafting` |
| `Drafting` | 已命中场景并显示表单 | 不再响应场景放置点击；保留一个草稿标记；冻结玩家移动和视角旋转 |
| `Submitting` | 正在调用创建接口 | 禁用提交/取消按钮，保留草稿、输入和幂等键 |
| `Error` | 创建失败或超时 | 保留草稿、只读输入和幂等键，显示重试与取消；重试使用同一幂等键。超时/5xx 可能已在服务端保存，取消前提示优先重试核验 |
| `Saved` | 后端返回成功 | 清除草稿标记并恢复 `Idle`，显示成功提示 |
| `Cancelled` | 用户取消填写 | 立即销毁草稿标记、清空输入并恢复 `Idle` |

## 3. 技术方案

### 3.1 版本与依赖基线

| 部分 | 选型 | 说明 |
| --- | --- | --- |
| Unity | Unity `6000.0.30f1`，C# | 使用实际安装并验证的版本、内置渲染管线和旧 Input Manager；基础对象、射线和 UI 不依赖付费素材 |
| Go | Go 1.22+ 当前受支持稳定版 | 使用标准库 `net/http`、`database/sql`、`log/slog`；不把 Go 称为 LTS，实际版本写入 `go.mod` 和 README |
| 数据库 | SQLite，纯 Go 驱动 `modernc.org/sqlite` | 避免 CGO；启动时自动建表；数据库文件可配置 |
| React | React + TypeScript + Vite | 使用 Node 24 LTS，记录安装的精确补丁版本；提交 `package-lock.json` 固定前端依赖 |
| Web 测试 | Vitest 5.x、React Testing Library、MSW | 2026-10-27 的稳定 Vitest 5 要求 Node `^22.12`、`^24` 或更高，不能用当前 Node 20.15 运行 |
| 端到端测试 | Playwright（P0 必做） | 验证真实 Go + React 闭环，不作为可选加分项 |

版本相关事项在环境门禁和脚手架阶段写入 `README.md`；不得把“计划版本”当作“已验证版本”。

#### 3.1.1 2026-10-28 环境核验与处理结果

本计划形成时对当前 Windows 开发环境做了只读检查，并已处理非 Unity 依赖：

| 依赖 | 当前结果 | 是否满足计划 | 开工处理 |
| --- | --- | --- | --- |
| Unity | `6000.0.30f1`（revision `62b05ba0686a`）已安装；工程导入、PlayMode 和 Windows Player 构建通过 | 是 | 按实际可用版本推进，并同步计划和 README |
| Go | `go1.27.3 windows/amd64`，通过仓库内便携工具链校验 | 是 | 已完成；通过 `. .\scripts\use-toolchain.ps1` 启用 |
| Node | `v24.22.1` | 是 | 已完成；满足当前 Vite/Vitest 引擎要求 |
| npm | `11.19.1` | 是 | 已完成；`package-lock.json` 已生成 |
| Git | `2.45.1.windows.1` | 是 | 继续使用，保留完整提交历史 |

初始 Node 20.15.0 不满足 Vite 8.3.5/Vitest 5.0.5 的引擎要求，现已升级到 Node 24.22.1。Unity 早期选定的 `6000.3.27f1` 与实际安装版本不同；本轮以已安装、已编译并已构建的 `6000.0.30f1` 为准。

### 3.2 仓库结构

```text
Unity-3D/
├─ Assets/
│  ├─ Scenes/Inspection.unity
│  ├─ Prefabs/IssueMarker.prefab
│  └─ Scripts/
│     ├─ Core/IssueModels.cs
│     ├─ Core/IssueApiClient.cs
│     ├─ Player/FirstPersonController.cs
│     ├─ Inspection/IssuePlacementController.cs
│     ├─ Inspection/IssueFormController.cs
│     └─ UI/InspectionHud.cs
├─ Packages/
├─ ProjectSettings/
├─ backend/
│  ├─ cmd/server/main.go
│  ├─ internal/config/config.go
│  ├─ internal/httpapi/router.go
│  ├─ internal/httpapi/issues_handler.go
│  ├─ internal/httpapi/middleware.go
│  ├─ internal/httpapi/issues_handler_test.go
│  ├─ internal/issue/model.go
│  ├─ internal/issue/service.go
│  ├─ internal/issue/service_test.go
│  ├─ internal/store/sqlite.go
│  ├─ internal/store/sqlite_test.go
│  ├─ internal/store/schema.sql
│  ├─ data/.gitkeep
│  ├─ go.mod
│  └─ go.sum
├─ web/
│  ├─ src/api/issues.ts
│  ├─ src/components/IssueTable.tsx
│  ├─ src/components/ErrorBanner.tsx
│  ├─ src/App.tsx
│  ├─ src/main.tsx
│  ├─ src/styles.css
│  ├─ tests/issues.test.tsx
│  ├─ e2e/issues.spec.ts
│  ├─ playwright.config.ts
│  ├─ package.json
│  └─ package-lock.json
├─ README.md
├─ AI_LOG.md
├─ .gitignore
└─ DEVELOPMENT_PLAN.md
```

Unity 项目位于仓库根目录，便于直接用 Unity Hub 打开；Go 和 React 作为同仓库子目录，使用同一份 Git 历史。

### 3.3 数据流

```text
Unity UI 表单
  → POST /api/issues
  → Go 校验、生成 ID/时间/默认 open
  → SQLite
  → GET /api/issues
  → React 列表
  → PATCH /api/issues/{id}/status
  → SQLite
  → React 更新当前行
```

## 4. 数据与接口契约

### 4.1 Issue 模型

| 字段 | 类型 | 必填 | 默认值/生成方式 | 约束 |
| --- | --- | --- | --- | --- |
| `id` | string | 响应必填 | 后端 UUID | 全局唯一，客户端不可指定 |
| `title` | string | 是 | 无 | 去除首尾空格后不得为空；长度不超过 200 字符 |
| `description` | string | 否 | `""` | 长度不超过 4000 字符 |
| `priority` | enum string | 是 | Unity 表单默认 `medium` | `low`、`medium`、`high` |
| `status` | enum string | 响应必填 | 创建时强制 `open` | `open`、`in_progress`、`resolved` |
| `deviceCode` | string | 否 | 场景设备编号，旧数据为 `""` | 去首尾空格；不超过 64 字符 |
| `zone` | string | 否 | 场景区域，旧数据为 `""` | 去首尾空格；不超过 64 字符 |
| `position.x/y/z` | number | 是 | Unity 射线命中点 | 有限浮点数 |
| `createdAt` | string | 响应必填 | 后端 UTC 时间 | RFC 3339，固定 9 位小数秒，例如 `2026-10-27T11:30:00.000000000Z` |
| `updatedAt` | string | 响应必填 | 后端 UTC 时间 | 同上；状态更新时刷新 |

`request_key` 和 `request_hash` 只用于服务端幂等控制，不加入 Issue 响应，避免改变任务书规定的三端字段结构。

示例：

```json
{
  "id": "0f2c7b23-7dff-4f1e-a6ab-52ef441b25c8",
  "title": "1 号机柜温度异常",
  "description": "机柜右侧温度明显偏高",
  "priority": "high",
  "status": "open",
  "deviceCode": "CAB-A",
  "zone": "A区 · 配电区",
  "position": {
    "x": 1.25,
    "y": 0.8,
    "z": 2.4
  },
  "createdAt": "2026-10-27T11:30:00.000000000Z",
  "updatedAt": "2026-10-27T11:30:00.000000000Z"
}
```

### 4.2 HTTP 接口

| ID | 方法与路径 | 请求体 | 成功响应 | 失败行为 |
| --- | --- | --- | --- | --- |
| API-001 | `GET /healthz` | 无 | `200 {"status":"ok"}` | 数据库不可用时返回 `503` |
| API-002 | `POST /api/issues` | 必填请求头 `Idempotency-Key`；创建字段：`title`、`description`、`priority`、`deviceCode`、`zone`、`position` | 首次 `201`；相同键和相同载荷的重放 `200`，均返回完整 Issue | 参数错误 `400 INVALID_ARGUMENT`；相同键不同载荷 `409 IDEMPOTENCY_CONFLICT` |
| API-003 | `GET /api/issues` | 无 | `200 {"items":[Issue...]}`，按 `createdAt` 倒序 | 存储错误 `500 INTERNAL` |
| API-004 | `PATCH /api/issues/{id}/status` | `Content-Type: application/json`；`{"status":"in_progress"}` | `200`，返回更新后的 Issue | 非法状态或多余字段 `400`；不存在 `404 NOT_FOUND` |
| API-005 | `DELETE /api/issues/{id}` | 无 | `204 No Content` | 不存在 `404 NOT_FOUND` |

统一错误格式：

```json
{
  "error": {
    "code": "INVALID_ARGUMENT",
    "message": "title is required"
  }
}
```

实现约束：

- 请求体限制为 1 MiB，JSON 必须为单一对象。
- `POST` 和 `PATCH` 要求 `Content-Type: application/json`；不匹配返回 `415 UNSUPPORTED_MEDIA_TYPE`。
- 请求体超过限制返回 `413 PAYLOAD_TOO_LARGE`；JSON 为空、畸形或存在尾随值返回 `400 INVALID_ARGUMENT`。
- 使用严格 JSON 解码拒绝未知字段。`POST` 只能接收 `title`、`description`、`priority`、`deviceCode`、`zone`、`position`；客户端提交 `id`、`status`、`createdAt` 或 `updatedAt` 必须返回 `400`。
- `title`、`priority`、`position` 缺失或为 `null` 时拒绝；`description` 缺失或为 `null` 时归一化为空字符串；位置三个分量必须均存在且为有限数值。
- `priority` 和 `status` 使用严格枚举校验，大小写不做模糊兼容。
- `POST` 由后端生成 `id`、`status=open`、`createdAt`、`updatedAt`。
- `PATCH` 只允许修改状态，不允许用请求体改写标题、坐标或创建时间。
- GET 使用 `{"items":[]}` 包装，避免 Unity `JsonUtility` 无法直接解析顶层数组。
- GET 使用 `ORDER BY created_at DESC, id DESC`，确保创建时间相同时仍有稳定顺序。
- React 开发地址默认为 `http://localhost:5173`，后端默认监听 `127.0.0.1:8080`。

#### 4.2.1 创建请求幂等契约

Unity 在进入 `Drafting` 时生成一个 UUID 作为 `Idempotency-Key`，同一草稿的首次提交和后续重试必须复用该值。后端收到请求后：

1. 验证键是非空 UUID。
2. 计算规范化创建载荷的哈希并保存在 `request_hash`。
3. 若 `request_key` 不存在，则在单一事务中写入 Issue 与幂等元数据，返回 `201`。
4. 若键存在且载荷哈希相同，返回原 Issue 和 `200`，不得再插入第二行。
5. 若键存在但载荷哈希不同，返回 `409 IDEMPOTENCY_CONFLICT`。
6. 并发请求使用相同键时，最多一个事务插入成功；其余请求读取并返回同一条 Issue。

该约束保证：客户端在“服务端已提交但响应丢失”的情况下重试，不会生成重复问题。

### 4.3 SQLite 表结构

```sql
CREATE TABLE IF NOT EXISTS issues (
  id TEXT PRIMARY KEY,
  title TEXT NOT NULL,
  description TEXT NOT NULL DEFAULT '',
  priority TEXT NOT NULL CHECK (priority IN ('low', 'medium', 'high')),
  status TEXT NOT NULL CHECK (status IN ('open', 'in_progress', 'resolved')),
  x REAL NOT NULL,
  y REAL NOT NULL,
  z REAL NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  request_key TEXT NOT NULL UNIQUE,
  request_hash TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_issues_created_order
  ON issues(created_at DESC, id DESC);
```

时间字段统一以 UTC、固定 9 位小数秒写入，确保字符串排序与时间顺序一致。启动时执行幂等建表脚本；测试使用独立临时数据库。数据库连接执行以下设置：

- `PRAGMA journal_mode=WAL`
- `PRAGMA busy_timeout=5000`
- `PRAGMA synchronous=NORMAL`
- `PRAGMA foreign_keys=ON`

MVP 将 `sql.DB.SetMaxOpenConns(1)` 固定为单一连接，串行化读写，避免 `database/sql` 连接池在 SQLite 写锁下产生不可控的 `SQLITE_BUSY`。每次创建和状态更新必须在一个事务中完成；唯一约束冲突必须显式处理为幂等重放或冲突错误，不得静默吞掉。

### 4.4 配置契约

| 环境变量 | 默认值 | 使用方 |
| --- | --- | --- |
| `PORT` | `8080` | Go 监听端口 |
| `DB_PATH` | `./data/issues.db` | SQLite 文件路径 |
| `CORS_ORIGINS` | `http://localhost:5173,http://127.0.0.1:5173` | React 开发地址白名单 |
| `VITE_API_BASE_URL` | `http://127.0.0.1:8080` | React 构建时 API 地址 |
| Unity API Base URL | `http://127.0.0.1:8080` | Inspector 可配置，提交前写入 README |

CORS 契约：

- 只允许 `CORS_ORIGINS` 中精确列出的来源，不使用通配符，不允许凭据。
- 允许方法为 `GET, POST, PATCH, DELETE, OPTIONS`。
- 允许请求头为 `Content-Type, Idempotency-Key`。
- 对 `OPTIONS` 预检返回 `204`，并带有对应 `Access-Control-Allow-Origin/Methods/Headers`。
- 响应带 `Vary: Origin`；未知来源不返回允许来源头。
- React 的 JSON `PATCH` 必须通过真实浏览器的预检验证，不只用 GET 验证。

## 5. 端到端交互设计

### 5.1 Unity 虚拟现场

场景包含：

- `12 × 12` 地面、背墙、两个不同尺寸机柜和一个设备箱。
- 至少 4 个可点击对象，均带 Collider 并位于 `Inspectable` 层。
- 主摄像机、方向光、环境光。
- HUD：操作说明、准星、状态提示、实时统计和同步时间。
- 问题标记 Prefab：球体、设备编号标签和可点击 Collider。
- 详情面板：点击历史标记查看标题、描述、状态、设备、区域和坐标。

控制方式：

- `W/A/S/D` 平移，`Shift` 加速。
- 按住鼠标右键拖动旋转视角；松开右键后恢复 UI 操作。
- 在非 UI 区域按左键发起一次射线检测。
- 左键优先命中历史标记并打开详情；未命中标记时才创建新草稿。
- `F5` 同步问题列表，`Esc` 关闭详情或取消草稿。
- 表单处于 `Drafting`、`Submitting` 或 `Error` 时，玩家控制器暂停移动和视角旋转，防止在输入框中键入 `W/A/S/D` 时改变场景。

放置规则：

1. 若 `EventSystem.current.IsPointerOverGameObject()` 为真，直接忽略。
2. 若当前状态不是 `Idle`，直接忽略场景放置。
3. 从摄像机经鼠标位置发射 `Physics.Raycast`，只命中 `Inspectable` 层。
4. 命中后在 `hit.point + hit.normal * 0.02` 显示标记，避免与表面共面闪烁。
5. 保存“未加视觉偏移的 `hit.point`”作为上报坐标。
6. 创建唯一草稿标记并打开表单；表单打开期间不再产生新标记。
7. 取消时销毁草稿标记；提交成功后销毁草稿标记。
8. 提交失败时保留草稿、坐标和输入内容，展示错误和“重试”操作。
9. 启动时从后端恢复历史标记，并每 8 秒同步；标记按优先级着色，按处理状态改变脉冲和弱化样式。
10. 命中的场景对象通过父级 `InspectionLocation` 解析 `deviceCode` 和 `zone`。

表单规则：

- 标题去除首尾空格后为空时，本地拒绝提交并聚焦标题输入框。
- 描述可选，空值发送空字符串。
- 优先级使用下拉框，默认 `medium`。
- 创建草稿时生成 `Idempotency-Key`；提交中按钮禁用；网络超时设为 10 秒。
- 首次提交或失败后重试均发送相同幂等键。首次提交后输入进入只读状态；用户要修改内容时必须取消草稿并重新开始。
- 通过 `UnityWebRequest.SetRequestHeader("Idempotency-Key", key)` 发送幂等键，设置 JSON Content-Type。
- 只有收到 `200` 或 `201` 且解析成功才算保存成功；`409`、`4xx`、`5xx` 和超时均保留草稿并显示错误。

### 5.2 React 管理端

页面结构：

- 标题与简短说明。
- “刷新”按钮。
- 错误提示区；网络失败时显示“无法连接后端”和重试按钮。
- 问题列表：标题、优先级、设备、区域、状态、坐标和创建时间。
- 筛选台：全文搜索以及状态、优先级、设备、区域筛选。
- 详情侧栏：展示完整描述、设备、区域、坐标，支持修改状态和确认删除。
- 每行一个状态下拉框；保存中禁用该行控件，失败时恢复旧值并显示错误。

加载行为：

1. 首次进入显示加载态。
2. 成功但无数据时显示空态，而不是空白页。
3. 网络失败或非 2xx 响应显示可读错误，不能白屏。
4. 最外层增加 Error Boundary，处理渲染异常。
5. 状态修改成功后使用后端返回对象更新该行；失败时保持原状态。

### 5.3 Go 服务

实现职责：

- 启动时读取配置、创建数据目录、打开 SQLite、建表。
- 统一 JSON 编解码、错误响应、请求日志、CORS、请求体大小限制。
- 创建接口负责 UUID、默认状态、UTC 时间与字段校验。
- 查询接口负责稳定排序和空列表返回。
- 状态接口先验证 ID 和状态，再在事务中更新并返回完整对象。
- 提供优雅关闭和服务器读写超时。

建议只引入 `github.com/google/uuid` 与 `modernc.org/sqlite` 两个非标准库依赖。

## 6. 边界与不做的事

### 6.1 P0 必做

- 三端完整闭环和任务书第 3 节全部功能。
- 后端重启持久化。
- UI 点击隔离、单草稿标记限制、取消清理和网络失败提示。
- README 中的启动说明、接口说明、版本、完成情况、已知问题和三个 AR 基础问答。
- `AI_LOG.md` 中全部实际主动 Prompt 原文和验证记录。
- 两段 3～5 分钟桌面演示视频。

### 6.2 P1 已完成的增强项

1. 启动时读取后端问题，在固定坐标系中恢复只读标记，并定时同步。
2. 点击标记显示标题、描述、优先级、状态、设备编号、区域和坐标。
3. 按优先级改变标记颜色和大小，按状态改变脉冲和弱化样式。
4. React 全文/状态/优先级/设备/区域筛选、详情侧栏和确认删除。
5. 场景设备和区域元数据自动随问题上报。

未完成的可选方向：AR Foundation XR Simulation 迁移示例。

### 6.3 本期不做

- 真实 ARCore/ARKit 真机功能。
- 用户登录、权限和审核流。
- 历史审计、附件上传和软删除回收站。
- 离线队列、多人实时同步和推送。
- 生产部署、HTTPS、域名和云数据库。
- 大量美术资产、动画和音效。

## 7. 兼容、安全与失败降级

- 新增字段必须提供客户端默认值；前端遇到缺失可选字段时显示 `-`，不因 `undefined` 崩溃。
- API 地址均通过配置注入，不把开发机 IP 散落在多个源文件中。
- 默认只监听 `127.0.0.1`，CORS 使用明确白名单；若改为局域网访问，需同步更新 CORS 和 Unity 地址并记录安全影响。
- React 使用正常文本渲染，不使用 `dangerouslySetInnerHTML`。
- SQL 全部使用参数绑定；错误响应不返回数据库路径、SQL 或堆栈。
- 服务不可用：Unity 保留草稿并允许重试；React 显示错误和重试，不清空已有列表。
- 提交超时或响应丢失：Unity 使用原幂等键重试；后端相同键和相同载荷只返回原 Issue，不产生重复记录。
- 数据库不可打开：后端启动失败并输出可操作错误，不进入“健康”状态。
- 重复点击：Unity 由状态机阻断；React 状态保存在途状态并禁用重复提交。
- 非法枚举、空标题、缺失坐标和非有限数值：拒绝并返回明确字段错误。
- 超时：Unity 10 秒；React 请求使用 `AbortController` 并在 10 秒后中止，显示错误。

## 8. 实施阶段

### 当前实施快照

| 范围 | 状态 | 证据 |
| --- | --- | --- |
| 本地 Go/Node 工具链 | 已完成 | 版本校验、SHA-256 下载脚本、`npm ci` |
| Go API 与 SQLite | 已完成 | `go test ./...`、`go vet ./...`、进程重启持久化手工验证 |
| React 管理端 | 已完成 | 类型检查、Lint、9 个前端测试、生产构建 |
| Go + React 浏览器闭环 | 已完成 | `npm run test:e2e`，2 个真实 Chrome 用例通过 |
| Unity | 已完成 | 场景、历史标记恢复、详情、样式、设备/区域、表单和 12 个 PlayMode 测试通过 |
| 视频与分享链接 | 未开始 | 依赖发布渠道和最终录制 |

### 阶段 -1：环境门禁

预计：0.5 个专注日，不含工具下载时间。

改动：

- 安装并激活可用的 Unity Editor，创建空白工程验证可进入 Play Mode。
- 安装 Go 1.22+ 的当前受支持稳定版。
- 将 Node 升级到 Node 24 LTS，并记录精确补丁版本。
- 记录 Unity 完整版本、Go、Node、npm、Git 和操作系统版本。

验收：

- `go version` 可执行且版本不低于 1.22。
- `node --version` 满足最终锁定的 Vite 和 Vitest `engines` 要求。
- Unity Hub 能打开并运行实际版本 `6000.0.30f1` 的工程。
- 环境信息写入 README 草稿；任一项未满足时不得进入阶段 0。

当前状态：Go、Node、npm 和 Unity `6000.0.30f1` 均已通过；可以继续实施。

### 阶段 0：仓库与工程骨架

预计：0.5 个专注日。

改动：

- 创建 `.gitignore`，至少排除：
  - Unity：`Library/`、`Temp/`、`Logs/`、`Obj/`、`UserSettings/`、`Builds/`、生成的 `*.csproj`、`*.sln`。
  - Go：运行时数据库 `backend/data/*.db`、`*.db-wal`、`*.db-shm`，保留 `.gitkeep`。
  - Web：`web/node_modules/`、`web/dist/`、`coverage/`、`test-results/`、`playwright-report/`。
  - 本地配置与凭据：`.env`、`.env.*`，使用 `!.env.example` 保留无凭据示例；忽略本地 IDE 用户文件。
- 创建 Unity `6000.0.30f1` 工程骨架、`backend/` 和 `web/`。
- 创建 `README.md` 初始版本与 `AI_LOG.md` 记录框架。
- 按实际发生时间，将项目开始至今由用户主动发给 AI 的全部问题或指令原文登记到 `AI_LOG.md`，再按序续记；不得只写摘要，也不得固定假设“当前请求”就是 `P-001`。
- 在 `AI_LOG.md` 中记录目标、采用状态（采用/修改后采用/未采用）、验证方式和人工复核。
- 建立首个可恢复 Git 提交。

验收：

- 环境门禁记录完整。
- Unity Hub 可打开工程且无导入错误。
- `go test ./...` 可执行。
- `npm install && npm run build` 可执行。
- `git status` 不包含 Unity 自动生成目录、运行时数据库和本地凭据。
- `git ls-files` 中不存在 `*.db`、`*.db-wal`、`*.db-shm`、`.env` 或 Unity 自动目录。

### 阶段 1：Go 后端与数据契约

预计：1 个专注日。

改动：

- 实现 `backend/internal/issue`、`backend/internal/store`、`backend/internal/httpapi` 和 `cmd/server/main.go`。
- 实现 API-001 至 API-004、严格 JSON 解码、SQLite 初始化、繁忙超时、幂等键、CORS 预检、错误格式和配置。
- 编写 handler、service、store 测试，包括表驱动参数校验、幂等重放、并发同键和并发写入。

验收：

- 健康检查成功。
- 创建问题后返回完整对象，默认 `open` 且 ID 唯一。
- GET 能读到刚创建的问题。
- PATCH 的三个合法状态均成功。
- 空标题、错误枚举、缺失/`null` 坐标、未知字段、非法 JSON、错误 Content-Type 分别返回明确错误码。
- 同一幂等键和相同载荷返回同一 ID；相同键不同载荷返回 `409`。
- 并发创建不出现重复 ID、静默丢失或未处理的 `SQLITE_BUSY`。
- OPTIONS 预检返回 `204` 和正确 CORS 响应头。
- 数据库关闭并重新打开后记录仍存在。

当前状态：已完成。

### 阶段 2：Unity 场景与巡检交互

预计：1.5 个专注日。

改动：

- 搭建 `Inspection.unity`、`IssueMarker.prefab` 和玩家控制。
- 实现射线放置、单草稿状态机、表单、幂等键和 `IssueApiClient`。
- 设置 `Inspectable` 层、Collider、Canvas、EventSystem 和网络配置。

验收：

- 可在场景中移动、旋转并观察至少两个对象的不同表面。
- 点击实际命中点出现标记。
- UI 输入/按钮操作不会新增标记。
- 一次只存在一个草稿标记，取消后消失。
- 表单打开时，键盘和右键不会移动/旋转玩家。
- 成功提交显示成功；后端停止时显示失败并保留草稿；恢复后使用同一幂等键重试。

当前状态：已完成。场景与 UI 由 `InspectionSceneBuilder` 生成；PlayMode 覆盖精确命中点、单草稿、取消、空标题、连接失败、错误后重试和成功提交。

### 阶段 3：React 管理端

预计：1 个专注日。

改动：

- 实现 API 客户端、列表、状态选择器、加载/空/错误状态和 Error Boundary。
- 添加 Vitest + React Testing Library + MSW 测试。
- 配置 `VITE_API_BASE_URL`。
- 状态修改请求使用 10 秒 `AbortController` 超时，失败保留旧状态和可读错误。

验收：

- 列表显示标题、优先级、状态。
- 刷新后能看到 Unity 上报的问题。
- 三种状态都能修改，并更新为后端返回值。
- 后端不可用时不白屏，显示错误与重试。
- 状态修改超时或失败时，旧状态和列表内容不丢失。
- 前端测试和生产构建通过。

当前状态：已完成。

### 阶段 4：三端联调与可靠性加固

预计：0.75 个专注日。

改动：

- 修复字段、时间、CORS、坐标和错误反馈问题。
- 增加真实 Go 服务与 React 的 Playwright 冒烟测试，覆盖加载、状态修改、后端停机错误和恢复。
- Playwright 使用独立临时 `DB_PATH` 和非默认端口，不得污染开发数据库，也不得依赖已有记录。
- 按失败矩阵逐项手工验证 Unity 网络中断、恢复和重启场景。
- 执行数据库并发、相同幂等键重放和进程重启测试。

验收：

- Unity → Go → React → 状态修改 → 再刷新形成完整闭环。
- 后端停止并重新启动后同一条数据仍存在。
- 提交超时后重试，React 和数据库中仍只有一条对应问题。
- `npm run test:e2e` 通过且未跳过关键用例。
- 日志中无敏感信息。
- 自动化测试和手工清单均通过。

当前状态：Unity → Go → SQLite → GET 的真实闭环已通过；PlayMode 验证创建、读取、同键重放和失败后重试唯一记录。React 端闭环此前已通过 Playwright。

### 阶段 5：文档、AI 记录、演示与分享

预计：1 个专注日。

改动：

- 完成 `README.md`：版本、启动命令、地址、API、完成/未完成、已知问题、AR 三问。
- 完成 `AI_LOG.md`，补齐每次 Prompt 原文、采用结果和验证方式；对 API Key、Token、账号、PII 和本地隐私路径脱敏。
- 录制并检查两段 3～5 分钟视频。
- 选择一个可被评审人员访问的分享方式（GitHub、Gitee、百度网盘或其他无需特殊权限的方式），上传源码、文档和视频。
- 在 README 中记录分享链接、访问权限、提取码（如有）和有效期（如有）。
- 最终清理、全量测试、提交 Git 历史。

验收：

- 新环境按 README 能启动三端。
- AI_LOG 至少包含一次“检查、修改或放弃 AI 结果”的记录。
- 对仓库执行 secret/PII 扫描，未发现未脱敏凭据、Token、账号或个人隐私。
- 视频实际展示要求流程，无敏感信息。
- 仓库不包含 Unity `Library` 等自动目录。
- 使用无痕窗口且不登录评审账号，可打开并下载分享链接中的仓库/源码/README/两段视频。
- 若链接有权限、提取码或有效期，README 中的信息与实际访问条件一致。

总计预计为 6.0～6.5 个专注日，不含工具下载时间；关键路径为环境门禁 → Go 契约与幂等 → Unity 上报 → React 读取 → 持久化联调 → 分享链接验证。

## 9. 测试与验收标准

| 验收 ID | 验收标准 | 验证方式/命令 | 当前证据 | 状态 |
| --- | --- | --- | --- | --- |
| AC-001 | 场景至少有地面、墙面和两个独立设备对象 | Scene 结构与 `Inspectable` 层 PlayMode 断言 | B | 通过 |
| AC-002 | 用户可以移动、旋转并观察不同场景位置 | 玩家/摄像机组件和输入锁定 PlayMode 断言；Editor Play Mode | B（移动手感需视频/人工复核） | 通过 |
| AC-003 | 点击设备或场景表面后，标记在射线命中点显示 | 世界坐标与视觉偏移 PlayMode 断言 | B | 通过 |
| AC-004 | UI 点击不触发放置；待提交标记最多一个；取消后消失 | 状态机、重复放置和取消 PlayMode 断言 | B | 通过 |
| AC-005 | 必填标题、可选描述、优先级及 x/y/z 正确进入请求 | 真实 Go 记录对照标题、描述、优先级和精确坐标 | B | 通过 |
| AC-006 | 提交成功和失败均有明确提示，失败保留草稿 | 连接失败、只读输入、草稿保留、原键重试和成功 PlayMode 用例 | B | 通过 |
| AC-007 | `GET /healthz` 返回 200 和 `status=ok` | Playwright WebServer 健康等待 + 手工启动服务 | B（真实 HTTP） | 通过（非 Unity） |
| AC-008 | 创建问题由后端生成唯一 ID、UTC 时间和默认 `open` | `go test ./...` + Create/List/PATCH 集成用例 | B（真实 SQLite） | 通过 |
| AC-009 | 问题列表包含已保存记录且按创建时间稳定倒序 | 列表测试 + `TestListOrderAtEqualTimestamp` | B（真实 SQLite） | 通过 |
| AC-010 | `open`、`in_progress`、`resolved` 三种状态均可更新 | 集成测试循环 PATCH 三个目标状态 | B | 通过 |
| AC-011 | 空标题、错误枚举、缺失/`null` 坐标、未知字段、非法 JSON、错误 Content-Type 均返回明确错误 | 表驱动 POST/PATCH 校验用例 + 超大请求体用例 | B | 通过 |
| AC-012 | Go 进程停止并重新启动后数据仍存在 | 同一 `DB_PATH` 启动、POST、停止进程、重启、GET | B（进程级） | 通过 |
| AC-013 | Unity 和 React 访问同一后端、同一数据库 | Unity 通过真实 Go/SQLite 创建并读取；React 通过 Playwright 使用同一 API 契约 | B | 通过 |
| AC-014 | 浏览器可跨域完成 JSON PATCH | Chrome 从 15173 端口向 18080 发真实 PATCH + OPTIONS 单元测试 | B | 通过 |
| AC-015 | React 列表显示标题、优先级、状态 | Vitest 组件测试 + Chrome 端到端测试 | B | 通过 |
| AC-016 | React 状态修改成功更新页面，失败保留旧状态并提示 | 组件成功/失败用例 + 10 秒 AbortController 超时用例 | B | 通过 |
| AC-017 | 后端不可用时 React 显示错误且不白屏 | Vitest 503 用例 + Chrome 请求中断用例 | B | 通过 |
| AC-018 | README 包含版本、启动、接口、完成情况、已知问题和 AR 三问 | 按 README 执行本地工具链、构建、测试 | B（非 Unity） | 通过（非 Unity） |
| AC-019 | AI_LOG 保留实际 Prompt 原文、采用结果和至少一次纠错记录，并完成敏感信息脱敏 | `AI_LOG.md` 对照 + secret 扫描无命中 | B（当前会话） | 通过（后续 Prompt 需续记） |
| AC-020 | 两段 3～5 分钟视频覆盖指定流程 | 视频计时并逐项打勾 | E（规划） | 待验证 |
| AC-021 | Unity、Go、Node 环境均满足锁定版本 | Go `1.27.3`、Node `24.22.1`、Unity `6000.0.30f1` 编译、测试和 Player 构建通过 | B | 通过 |
| AC-022 | 超时后使用同一幂等键重试不会创建重复问题 | 相同键/相同载荷两次 POST；相同键/不同载荷 POST；并发同键 POST | B | 通过 |
| AC-023 | SQLite 并发写入有明确结果，无静默丢失或未处理 busy error | 20 个同键请求和 20 个不同键并发写入测试；`-race` 因运行库入口点错误未执行 | B（未含 race） | 部分通过 |
| AC-024 | 表单打开时键盘和右键不会移动或旋转玩家 | PlayMode 断言 `FirstPersonController.InputLocked` 在 Drafting/Error 为 true、取消后为 false | B（实际键鼠手感需演示复核） | 通过 |
| AC-025 | 运行时数据库、WAL/SHM、环境文件和 Unity 自动目录不进入 Git | `git check-ignore -v` 已验证 `.tools`、DB、WAL、`.env`、缓存 | B | 通过 |
| AC-026 | 分享链接可被无登录、无特殊权限的评审人员访问 | 无痕窗口验证仓库/源码/README/两段视频；核对权限、提取码和有效期 | E（规划） | 待验证 |
| AC-027 | 真实 Go + React 端到端冒烟测试可复现 | `npm run test:e2e`：2 个 Chrome 用例通过 | B | 通过 |

说明：Unity 主路径和主要失败路径已使用真实 Go/SQLite 验证到 B；Windows Player 已构建并启动。移动/视角的实际操作手感、两段视频和分享链接仍需人工验收，因此本计划整体不能宣称最终交付完成。

## 10. AR 基础问答的交付要点

README 中需要简要回答：

1. 真实 AR 放置通常涉及 `ARSession`、`ARRaycastManager`、`ARPlaneManager`、`XROrigin` 和 `ARAnchorManager`；通过平面检测生成可交互平面，再以屏幕射线在检测平面上命中并创建锚点。
2. 仅保存 Unity `x/y/z` 不能保证跨会话重新定位，因为该坐标依赖当次会话的跟踪原点和世界坐标；设备重启、跟踪丢失或重新建图后坐标原点可能变化。稳定恢复通常需要持久化锚点或世界地图重定位。
3. 初始化失败、设备不支持或 Tracking 异常时，应检查 `ARSession.state` 和 `notTrackingReason`，暂停放置交互，显示可理解的失败原因、重试入口和不支持设备时的非 AR 降级说明。

这些是 README 的解释性内容，不要求实现 AR 功能。完成后仍需人工复核用词与当前 AR Foundation 版本一致。

## 11. 交付物检查

| 交付物 | 完成条件 |
| --- | --- |
| Unity 源码 | 保留 `Assets`、`Packages`、`ProjectSettings`，不提交 `Library`、`Temp`、`Logs` |
| Go 源码 | 可 `go test -race ./...`，数据库目录首次启动自动创建，运行时数据库不提交 |
| React 源码 | 可 `npm ci`、测试、构建并由 Vite 启动；`npm run test:e2e` 通过 |
| `README.md` | 版本、三端启动、完成/未完成/已知问题、API、AR 三问 |
| `AI_LOG.md` | 实际主动 Prompt 原文、采用结果、验证或放弃原因；敏感信息已脱敏 |
| 演示视频 1 | 桌面场景移动、观察、点击、放置、填写和提交 |
| 演示视频 2 | React 查看、状态修改、错误降级、重启持久化 |
| Git 历史 | 按阶段形成可理解提交，不包含密钥或自动生成目录 |
| 分享链接 | 无登录/无特殊权限可访问；权限、提取码和有效期与 README 一致 |

## 12. 风险与决策

| 风险/歧义 | 处理决策 | 证据/说明 |
| --- | --- | --- |
| 任务书视频段落仍写“真机” | 按标题和第 1 节的无 AR 备选版要求，用 Unity Editor/桌面演示，并在 README 明确选择的是备选版 | 解释性判断，任务书前后文存在措辞不一致 |
| Unity 版本或输入系统差异 | 使用实际可用的 Unity `6000.0.30f1`；工程固定旧 Input Manager 和内置渲染管线，避免新旧输入 API 混用 | 工程导入、PlayMode 和 Windows Player 构建均通过 |
| Unity 的 `JsonUtility` 对顶层数组支持弱 | 后端 GET 统一返回 `{"items":[]}` | 已纳入 API 契约 |
| SQLite 原生驱动增加编译难度 | 使用纯 Go 的 `modernc.org/sqlite` | 降低 Windows 环境 CGO 风险 |
| 实际 Unity 版本与早期锁定值不同 | 按 `6000.0.30f1` 完成实现和验证，并在 README、计划和 AI 记录中明确版本差异 | 2026-10-28 本机版本、编译、测试和 Player 构建证据 |
| 服务端已保存但客户端超时后重试 | 同一草稿复用 `Idempotency-Key`，后端唯一约束并比较载荷哈希 | 对应 AC-022/AC-023 |
| SQLite 并发写锁冲突 | WAL + busy_timeout + 单写连接 + 事务；并发测试 | 对应 AC-023 |
| AI_LOG 或运行数据泄露本地信息 | Prompt 脱敏、secret/PII 扫描、忽略数据库和环境文件 | 对应 AC-019/AC-025 |
| 加分项抢占必做时间 | P0 全部通过后才进入 P1，每完成一个加分项单独提交 | 防止范围膨胀 |
| 视频或分享链接遗漏失败场景/访问条件 | 预先编写演示脚本和时间点，录制后逐项回看；无痕验证分享链接 | 与 AC-006/012/017/020/026 对应 |

## 13. 追溯矩阵

| 需求/结论 ID | 设计位置 | 规划代码/数据落点 | 测试/验证 | 证据等级 | 状态 |
| --- | --- | --- | --- | --- | --- |
| R-E01 环境门禁 | §3.1.1、§8 阶段 -1 | README 环境记录 | AC-021 | A/B | 已完成 |
| R-U01 场景对象 | §5.1 | `Assets/Scenes/Inspection.unity` | AC-001 | A/B | 已实现 |
| R-U02 移动视角 | §5.1 | `FirstPersonController.cs` | AC-002 | A/B | 已实现 |
| R-U03 射线放置 | §5.1 | `IssuePlacementController.cs` | AC-003 | A/B | 已实现 |
| R-U04 单草稿与 UI 隔离 | §5.1、§2 | `IssuePlacementController.cs`、`InspectionHud.cs` | AC-004 | A/B | 已实现 |
| R-U05 表单字段 | §4.1、§5.1 | `IssueFormController.cs`、`IssueModels.cs` | AC-005 | A/B | 已实现 |
| R-U06 成功/失败提示 | §2、§7 | `IssueApiClient.cs`、`IssueFormController.cs` | AC-006 | A/B | 已实现 |
| R-U07 表单期间冻结玩家输入 | §5.1、§2 | `FirstPersonController.cs`、`InspectionHud.cs` | AC-024 | A/B | 已实现 |
| R-B01 健康检查 | §4.2 | `backend/internal/httpapi/router.go` | AC-007 | A/B | 已实现 |
| R-B02 创建/ID/默认状态/时间 | §4.1、§4.2 | `internal/issue/service.go`、`schema.sql` | AC-008 | A/B | 已实现 |
| R-B03 问题列表 | §4.2 | `issues_handler.go`、`sqlite.go` | AC-009 | A/B | 已实现 |
| R-B04 状态更新 | §4.2 | `issues_handler.go`、`service.go` | AC-010 | A/B | 已实现 |
| R-B05 严格参数校验 | §4.2、§7 | `internal/issue/model.go`、`middleware.go` | AC-011 | A/B | 已实现 |
| R-B06 重启持久化 | §4.3、§5.3 | `internal/store/sqlite.go` | AC-012 | A/B | 已实现 |
| R-B07 同源数据 | §3.3 | Go 数据库 + Unity/React 配置 | AC-013 | A/B | 已实现 |
| R-B08 浏览器跨域与预检 | §4.2、§4.4 | `middleware.go` | AC-014 | A/B | 已实现 |
| R-B09 创建幂等 | §4.2.1、§4.3 | `service.go`、`sqlite.go`、`schema.sql` | AC-022 | A/B | 已实现 |
| R-B10 SQLite 并发与事务 | §4.3 | `internal/store/sqlite.go` | AC-023 | A/B（未含 race） | 部分完成 |
| R-W01 列表展示 | §5.2 | `IssueTable.tsx` | AC-015 | A/B | 已实现 |
| R-W02 状态修改 | §5.2 | `App.tsx`、`api/issues.ts` | AC-016 | A/B | 已实现 |
| R-W03 错误降级 | §5.2、§7 | `ErrorBanner.tsx`、Error Boundary | AC-017 | A/B | 已实现 |
| R-W04 真实端到端测试 | §8 阶段 4 | Playwright 测试文件 | AC-027 | A/B | 已实现 |
| R-S01 仓库数据与凭据隔离 | §8 阶段 0 | `.gitignore` | AC-025 | A/B | 已实现 |
| R-D01 README | §10、§11 | `README.md` | AC-018 | A/B | 已实现 |
| R-D02 AI 记录与脱敏 | §8 阶段 0/5 | `AI_LOG.md` | AC-019 | A/B | 已实现，持续续记 |
| R-D03 演示视频 | §8 阶段 5 | `README.md` 中视频链接/说明 | AC-020 | A/E | 规划中 |
| R-D04 可访问分享链接 | §8 阶段 5、§11 | `README.md` 分享信息 | AC-026 | A/E | 规划中 |

## 14. 自审

- 是否解决真实痛点：是。计划围绕“在场景实际位置记录问题、跨端查看并更新状态”建立最小闭环。
- 是否过度设计：鉴权、真实 AR、多人实时同步和复杂美术已明确排除；增强项保持单一数据契约。
- 是否与现有结构兼容：Unity、Go 和 Web 已按同仓库结构落地；SQLite 使用增量迁移兼容旧表。
- 关键质量属性是否满足：三端主路径和主要失败路径达到 B 级证据；Windows Player 已实际构建和启动。
- 首轮审查问题是否关闭：P1–P12 已分别落入环境门禁、幂等契约、交付链接、严格 JSON、CORS 预检、参数验收、证据分级、AI 日志脱敏、Git 忽略、SQLite 并发、Unity 输入冻结和 Playwright 范围。
- 剩余风险与未核实项：
  - Unity 实际版本为 `6000.0.30f1`；功能验证通过，但移动/视角手感仍需通过最终演示视频人工复核。
  - `go test -race` 的测试进程在当前 Windows 环境启动时返回 `0xc0000139`；普通并发测试通过，但 race 证据缺失。
  - AR Foundation 问答内容需按最终 Unity/AR Foundation 版本人工复核。
  - 任务书“真机演示”措辞按无 AR 备选版整体语义解释，建议在 README 中明确说明。
  - 演示视频、分享链接、访问权限和有效期尚未生成。
- AI 生成内容：本计划及三端实现由 Codex 根据用户提供的一手任务书和评审结果生成；`AI_LOG.md` 已记录 P-001 至 P-008，人工验收仍需项目负责人完成。
