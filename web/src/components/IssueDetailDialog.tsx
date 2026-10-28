import { useEffect, useRef, useState } from 'react'
import type { Issue, IssueStatus } from '../api/issues'

interface IssueDetailDialogProps {
  issue: Issue
  busy: boolean
  deleting: boolean
  error: string | null
  onClose: () => void
  onStatusChange: (id: string, status: IssueStatus) => Promise<void>
  onDelete: (id: string) => Promise<void>
}

const statusLabels: Record<IssueStatus, string> = {
  open: '待处理',
  in_progress: '处理中',
  resolved: '已完成',
}

const priorityLabels: Record<Issue['priority'], string> = {
  low: '低',
  medium: '中',
  high: '高',
}

const dateFormatter = new Intl.DateTimeFormat('zh-CN', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
})

export function IssueDetailDialog({
  issue,
  busy,
  deleting,
  error,
  onClose,
  onStatusChange,
  onDelete,
}: IssueDetailDialogProps) {
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const closeButtonRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    closeButtonRef.current?.focus()
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape' && !busy && !deleting) {
        onClose()
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [busy, deleting, onClose])

  return (
    <div
      className="detail-backdrop"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !busy && !deleting) {
          onClose()
        }
      }}
    >
      <section
        className="detail-panel"
        role="dialog"
        aria-modal="true"
        aria-labelledby="issue-detail-title"
      >
        <div className="detail-topline">
          <span>FIELD NOTE / {issue.id.slice(0, 8).toUpperCase()}</span>
          <button
            ref={closeButtonRef}
            type="button"
            className="icon-button"
            aria-label="关闭问题详情"
            disabled={busy || deleting}
            onClick={onClose}
          >
            ×
          </button>
        </div>

        <div className="detail-heading">
          <span className={`priority priority-${issue.priority}`}>
            {priorityLabels[issue.priority]}优先级
          </span>
          <h2 id="issue-detail-title">{issue.title}</h2>
          <p>{issue.description || '未填写补充描述。'}</p>
        </div>

        <dl className="detail-facts">
          <div>
            <dt>设备编号</dt>
            <dd>{issue.deviceCode || '未关联设备'}</dd>
          </div>
          <div>
            <dt>现场区域</dt>
            <dd>{issue.zone || '未划分区域'}</dd>
          </div>
          <div>
            <dt>空间坐标</dt>
            <dd className="coordinate-inline">
              X {issue.position.x.toFixed(2)} · Y {issue.position.y.toFixed(2)} · Z{' '}
              {issue.position.z.toFixed(2)}
            </dd>
          </div>
          <div>
            <dt>上报时间</dt>
            <dd>{formatDate(issue.createdAt)}</dd>
          </div>
        </dl>

        <label className="detail-status">
          <span>处理状态</span>
          <select
            value={issue.status}
            disabled={busy || deleting}
            onChange={(event) => {
              void onStatusChange(issue.id, event.target.value as IssueStatus)
            }}
          >
            {Object.entries(statusLabels).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>

        {error ? (
          <p className="detail-error" role="alert">
            {error}
          </p>
        ) : null}

        <div className="detail-actions">
          {confirmingDelete ? (
            <div className="delete-confirm" role="group" aria-label="确认删除问题">
              <p>删除后无法恢复，确认移除这条现场记录？</p>
              <div>
                <button
                  type="button"
                  className="danger-button"
                  disabled={deleting}
                  onClick={() => void onDelete(issue.id)}
                >
                  {deleting ? '正在删除…' : '确认删除'}
                </button>
                <button
                  type="button"
                  className="secondary-button"
                  disabled={deleting}
                  onClick={() => setConfirmingDelete(false)}
                >
                  保留记录
                </button>
              </div>
            </div>
          ) : (
            <button
              type="button"
              className="delete-button"
              disabled={busy || deleting}
              onClick={() => setConfirmingDelete(true)}
            >
              删除问题
            </button>
          )}
        </div>
      </section>
    </div>
  )
}

function formatDate(value: string): string {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : dateFormatter.format(parsed)
}
