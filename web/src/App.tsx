import { useCallback, useEffect, useState } from 'react'
import './App.css'
import {
  deleteIssue,
  errorMessage,
  listIssues,
  updateIssueStatus,
} from './api/issues'
import type { Issue, IssueStatus } from './api/issues'
import { ErrorBanner } from './components/ErrorBanner'
import { IssueDetailDialog } from './components/IssueDetailDialog'
import { IssueFilters } from './components/IssueFilters'
import type { IssueFilterState } from './components/IssueFilters'
import { IssueTable } from './components/IssueTable'

const initialFilters: IssueFilterState = {
  query: '',
  priority: 'all',
  status: 'all',
  deviceCode: 'all',
  zone: 'all',
}

function App() {
  const [issues, setIssues] = useState<Issue[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [updatingId, setUpdatingId] = useState<string | null>(null)
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [filters, setFilters] = useState<IssueFilterState>(initialFilters)
  const [refreshToken, setRefreshToken] = useState(0)

  useEffect(() => {
    const controller = new AbortController()

    void listIssues(controller.signal)
      .then((items) => {
        if (!controller.signal.aborted) {
          setIssues(items)
        }
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setLoadError(`无法加载问题：${errorMessage(error)}`)
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setLoading(false)
        }
      })

    return () => controller.abort()
  }, [refreshToken])

  function reload() {
    setLoading(true)
    setLoadError(null)
    setActionError(null)
    setRefreshToken((value) => value + 1)
  }

  async function handleStatusChange(id: string, status: IssueStatus) {
    setUpdatingId(id)
    setActionError(null)
    try {
      const updated = await updateIssueStatus(id, status)
      setIssues((current) =>
        current.map((issue) => (issue.id === updated.id ? updated : issue)),
      )
    } catch (error) {
      setActionError(`状态更新失败：${errorMessage(error)}`)
    } finally {
      setUpdatingId(null)
    }
  }

  async function handleDelete(id: string) {
    setDeletingId(id)
    setActionError(null)
    try {
      await deleteIssue(id)
      setIssues((current) => current.filter((issue) => issue.id !== id))
      setSelectedId(null)
    } catch (error) {
      setActionError(`删除失败：${errorMessage(error)}`)
    } finally {
      setDeletingId(null)
    }
  }

  const closeDetail = useCallback(() => setSelectedId(null), [])
  const normalizedQuery = filters.query.trim().toLocaleLowerCase('zh-CN')
  const filteredIssues = issues.filter((issue) => {
    if (filters.priority !== 'all' && issue.priority !== filters.priority) {
      return false
    }
    if (filters.status !== 'all' && issue.status !== filters.status) {
      return false
    }
    if (filters.deviceCode !== 'all' && issue.deviceCode !== filters.deviceCode) {
      return false
    }
    if (filters.zone !== 'all' && issue.zone !== filters.zone) {
      return false
    }
    if (normalizedQuery === '') {
      return true
    }
    return [
      issue.title,
      issue.description,
      issue.id,
      issue.deviceCode,
      issue.zone,
    ].some((value) => value.toLocaleLowerCase('zh-CN').includes(normalizedQuery))
  })
  const deviceCodes = Array.from(
    new Set(issues.map((issue) => issue.deviceCode).filter(Boolean)),
  ).sort((left, right) => left.localeCompare(right, 'zh-CN'))
  const zones = Array.from(
    new Set(issues.map((issue) => issue.zone).filter(Boolean)),
  ).sort((left, right) => left.localeCompare(right, 'zh-CN'))
  const selectedIssue = issues.find((issue) => issue.id === selectedId) ?? null
  const openCount = issues.filter((issue) => issue.status === 'open').length
  const activeCount = issues.filter((issue) => issue.status === 'in_progress').length
  const resolvedCount = issues.filter((issue) => issue.status === 'resolved').length

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="header-content">
          <p className="eyebrow">INSPECTION CONTROL</p>
          <h1>巡检问题管理台</h1>
          <p className="header-copy">
            从固定 3D 现场恢复问题标记，核对设备、区域与处理进度。
          </p>
        </div>
        <button
          className="refresh-button"
          type="button"
          disabled={loading || updatingId !== null}
          onClick={reload}
        >
          {loading ? '刷新中…' : '刷新数据'}
        </button>
      </header>

      <main>
        <section className="summary-grid" aria-label="问题统计">
          <article className="summary-card summary-card-total">
            <div className="summary-card-heading">
              <span>全部问题</span>
              <span className="summary-card-note">现场记录</span>
            </div>
            <div className="summary-value">
              <strong>{issues.length}</strong>
              <span>条</span>
            </div>
          </article>
          <article className="summary-card summary-card-open">
            <div className="summary-card-heading">
              <span>
                <i className="summary-dot" aria-hidden="true" />
                待处理
              </span>
            </div>
            <div className="summary-value">
              <strong>{openCount}</strong>
              <span>条</span>
            </div>
          </article>
          <article className="summary-card summary-card-progress">
            <div className="summary-card-heading">
              <span>
                <i className="summary-dot" aria-hidden="true" />
                处理中
              </span>
            </div>
            <div className="summary-value">
              <strong>{activeCount}</strong>
              <span>条</span>
            </div>
          </article>
          <article className="summary-card summary-card-resolved">
            <div className="summary-card-heading">
              <span>
                <i className="summary-dot" aria-hidden="true" />
                已完成
              </span>
            </div>
            <div className="summary-value">
              <strong>{resolvedCount}</strong>
              <span>条</span>
            </div>
          </article>
        </section>

        {loadError ? (
          <ErrorBanner
            message={loadError}
            onRetry={reload}
          />
        ) : null}
        {actionError ? <ErrorBanner message={actionError} /> : null}

        <section className="issues-panel" aria-busy={loading}>
          <div className="panel-heading">
            <div>
              <p className="section-label">ISSUES</p>
              <h2>问题列表</h2>
              <p className="panel-description">按上报时间从新到旧排列</p>
            </div>
            <div className="panel-meta">
              {loading && issues.length > 0 ? (
                <span className="loading-label" role="status">
                  正在同步…
                </span>
              ) : null}
              <span className="record-count">
                显示 {filteredIssues.length} / 共 {issues.length} 条
              </span>
            </div>
          </div>

          <IssueFilters
            filters={filters}
            deviceCodes={deviceCodes}
            zones={zones}
            visibleCount={filteredIssues.length}
            totalCount={issues.length}
            onChange={setFilters}
            onReset={() => setFilters(initialFilters)}
          />

          {loading && issues.length === 0 ? (
            <div className="state-panel" role="status">
              <span className="state-icon" aria-hidden="true">◎</span>
              <p>正在加载巡检问题…</p>
            </div>
          ) : null}

          {!loading && !loadError && issues.length === 0 ? (
            <div className="state-panel">
              <span className="state-icon" aria-hidden="true">✓</span>
              <h3>当前没有问题</h3>
              <p>Unity 客户端上报问题后，会在这里显示。</p>
            </div>
          ) : null}

          {!loading && !loadError && issues.length > 0 && filteredIssues.length === 0 ? (
            <div className="state-panel state-panel-filtered">
              <span className="state-icon" aria-hidden="true">⌕</span>
              <h3>没有匹配的问题</h3>
              <p>调整筛选条件，或清除筛选查看全部现场记录。</p>
              <button
                type="button"
                className="secondary-button"
                onClick={() => setFilters(initialFilters)}
              >
                清除筛选
              </button>
            </div>
          ) : null}

          {filteredIssues.length > 0 ? (
            <IssueTable
              issues={filteredIssues}
              disabled={loading || updatingId !== null || deletingId !== null}
              updatingId={updatingId}
              selectedId={selectedId}
              onSelect={setSelectedId}
              onStatusChange={handleStatusChange}
            />
          ) : null}
        </section>
      </main>

      <footer>
        <span>数据源</span>
        <code>{import.meta.env.VITE_API_BASE_URL || 'http://127.0.0.1:8080'}</code>
      </footer>

      {selectedIssue ? (
        <IssueDetailDialog
          issue={selectedIssue}
          busy={loading || updatingId !== null}
          deleting={deletingId === selectedIssue.id}
          error={actionError}
          onClose={closeDetail}
          onStatusChange={handleStatusChange}
          onDelete={handleDelete}
        />
      ) : null}
    </div>
  )
}

export default App
