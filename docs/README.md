# 项目资料库

本资料库用于统一管理由 AI 或人工生成的项目文档，覆盖需求、方案、设计、测试、交付和调研资料。目标是让任何一份资料都能被稳定定位、判断是否现行、追溯生成与核验过程，并在修订后保留旧版本。

## 先看这里

- 自动总目录：[INDEX.md](INDEX.md)
- 机器可读目录：[catalog.json](catalog.json)
- 管理规范：[POLICY.md](POLICY.md)
- 元数据契约：[schema/document.schema.json](schema/document.schema.json)
- 新资料模板：[templates/document.md](templates/document.md)

## 核心模型

每份资料占用一个不可重命名的条目目录：

```text
docs/
├─ entries/
│  └─ <domain>/
│     └─ DOC-YYYYMMDD-NNN/
│        ├─ meta.json
│        ├─ primary.md
│        └─ versions/          # 修订后才有
├─ INDEX.md                    # 自动生成
├─ catalog.json                # 自动生成
├─ templates/
└─ schema/
```

`DOC-YYYYMMDD-NNN` 是稳定资料 ID。标题、状态、版本和标签放在 `meta.json`，不塞进目录名；`primary.*` 是当前正文；旧版正文在 `versions/` 留存。手工修改正文会使哈希校验失败，因此正文变更必须通过 `revise` 登记。

## 状态

| 状态 | 含义 | 使用规则 |
| --- | --- | --- |
| `draft` | 草稿 | 可修改；不得作为项目事实依据 |
| `review` | 评审中 | 等待负责人或人工核验 |
| `active` | 现行 | 可作为当前实施依据；AI 资料必须先完成人工核验 |
| `superseded` | 已被替代 | 保留追溯价值，不得继续作为当前依据 |
| `archived` | 已归档 | 默认不出现在列表和检索中；不物理删除 |

合法流转为：`draft → review → active`。`review` 可退回 `draft`；`active` 只能通过显式替代变为 `superseded`，任何非终止状态都可归档。状态变化会写入 `history`。

## 快速开始

在仓库根目录运行：

```powershell
python tools\doclib.py list
python tools\doclib.py search Unity
python tools\doclib.py show DOC-20261027-001
python tools\doclib.py validate
python -B -m unittest discover -s tests -v
```

导入一份新资料：

```powershell
python tools\doclib.py add .\path\to\generated.md `
  --title "巡检交互设计" `
  --type design `
  --domain unity `
  --status draft `
  --owner "项目开发负责人" `
  --version 0.1 `
  --summary "描述射线放置与单草稿状态机" `
  --tag unity --tag interaction `
  --origin ai `
  --generator Codex `
  --model-version "实际使用的模型" `
  --prompt-ref "P-001"
```

`add` 只复制来源文件，不移动或改写原文件。默认拒绝重复内容；文件上限为 100 MiB。

评审通过后：

```powershell
python tools\doclib.py status DOC-20261027-001 active `
  --actor "核验人" `
  --note "已逐项对照接口与任务书，差异记录见评审意见"
```

修订现有资料：

```powershell
python tools\doclib.py revise DOC-20261027-001 .\path\to\generated-v2.md `
  --actor "项目开发负责人" `
  --note "补充失败降级路径"
```

`revise` 会把旧正文存入 `versions/`，递增修订号，并把资料退回 `draft`，防止未经复核的内容继续以现行版出现。

替代一份现行资料：

```powershell
python tools\doclib.py supersede DOC-20261027-001 DOC-20261028-001 `
  --actor "项目开发负责人" `
  --note "新方案覆盖旧方案"
```

## 日常规则

1. 生成结果先落到工作区临时文件，再通过 `add` 或 `revise` 入库；不要直接创建 `entries/` 下的文件。
2. 一份资料只表达一个主题。方案、测试报告、会议记录和交付清单不要塞进同一文件。
3. 每份 AI 资料必须记录生成工具、模型版本、Prompt 引用和人工核验结果；模型未知时写 `not-recorded`，不要猜测。
4. 新版本不覆盖旧结论。现行资料需要替换时，两份都先为 `active`，再运行 `supersede` 建立双向关联。
5. 管理端只允许通过命令修改元数据；`INDEX.md` 与 `catalog.json` 禁止手改。
6. 每次批量入库、状态变更或修订后运行 `validate`，并把校验结果作为本次交付证据。

## 检索边界

`search` 检索标题、摘要、标签、负责人、版本，以及不超过 2 MiB 的 UTF-8 文本正文。Markdown、TXT、JSON、CSV 和常见源码可直接搜；PDF、DOCX、图片等二进制文件只按元数据检索，当前版本不提供 OCR 或全文索引。

## 备份与恢复

资料库是普通文件目录，应随 Git 一起提交。`revise` 产生的 `versions/` 快照与 `history` 共同支持追溯；`archived` 只改变可见性，不删除文件。若 `catalog.json` 或 `INDEX.md` 受损，可从各条目的 `meta.json` 重建：

```powershell
python tools\doclib.py rebuild
python tools\doclib.py validate
```

## 当前限制

- 本版本面向单机、单仓库使用，不提供账号、权限、在线协作和远程服务。
- 不提供 DOCX/PDF 正文抽取、附件管理、OCR、版本差异渲染和自动审阅。
- 不自动判断资料内容是否事实正确；“active”只表示约定的人工评审已完成。
- 不替代项目 Git 历史、任务管理系统或正式文档审批流。
