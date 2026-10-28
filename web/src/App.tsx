import { useEffect, useState } from 'react'
import './App.css'
import { errorMessage, listIssues, updateIssueStatus } from './api/issues'
import type { Issue, IssueStatus } from './api/issues'
import { ErrorBanner } from './components/ErrorBanner'
import { IssueTable } from './components/IssueTable'

function App() {
  const [issues, setIssues] = useState<Issue[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [updatingId, setUpdatingId] = useState<string | null>(null)
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

  const openCount = issues.filter((issue) => issue.status === 'open').length
  const activeCount = issues.filter((issue) => issue.status === 'in_progress').length
  const resolvedCount = issues.filter((issue) => issue.status === 'resolved').length

  return (
    <div className="app-shell">
      <header className="app-header">
        <div>
          <p className="eyebrow">VIRTUAL INSPECTION</p>
          <h1>巡检问题管理台</h1>
          <p className="header-copy">
            查看 Unity 现场上报的问题，并更新处理状态。
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
          <article className="summary-card">
            <span>全部问题</span>
            <strong>{issues.length}</strong>
          </article>
          <article className="summary-card">
            <span>待处理</span>
            <strong>{openCount}</strong>
          </article>
          <article className="summary-card">
            <span>处理中</span>
            <strong>{activeCount}</strong>
          </article>
          <article className="summary-card">
            <span>已完成</span>
            <strong>{resolvedCount}</strong>
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
            </div>
            {loading && issues.length > 0 ? (
              <span className="loading-label" role="status">
                正在同步…
              </span>
            ) : null}
          </div>

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

          {issues.length > 0 ? (
            <IssueTable
              issues={issues}
              disabled={loading || updatingId !== null}
              updatingId={updatingId}
              onStatusChange={handleStatusChange}
            />
          ) : null}
        </section>
      </main>

      <footer>
        <span>Go API</span>
        <code>{import.meta.env.VITE_API_BASE_URL || 'http://127.0.0.1:8080'}</code>
      </footer>
    </div>
  )
}

export default App
