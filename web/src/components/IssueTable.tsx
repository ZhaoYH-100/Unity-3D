import type { Issue, IssueStatus } from '../api/issues'

interface IssueTableProps {
  issues: Issue[]
  disabled: boolean
  updatingId: string | null
  selectedId: string | null
  onSelect: (id: string) => void
  onStatusChange: (id: string, status: IssueStatus) => Promise<void>
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
})

const timeFormatter = new Intl.DateTimeFormat('zh-CN', {
  hour: '2-digit',
  minute: '2-digit',
  hour12: false,
})

export function IssueTable({
  issues,
  disabled,
  updatingId,
  selectedId,
  onSelect,
  onStatusChange,
}: IssueTableProps) {
  return (
    <div className="table-scroll" aria-busy={disabled}>
      <table>
        <caption className="sr-only">巡检问题列表</caption>
        <colgroup>
          <col className="issue-column" />
          <col className="location-column" />
          <col className="position-column" />
          <col className="time-column" />
          <col className="status-column" />
        </colgroup>
        <thead>
          <tr>
            <th scope="col">问题</th>
            <th scope="col">设备与区域</th>
            <th scope="col">场景坐标</th>
            <th scope="col">上报时间</th>
            <th scope="col">处理状态</th>
          </tr>
        </thead>
        <tbody>
          {issues.map((issue) => (
            <tr
              key={issue.id}
              className={selectedId === issue.id ? 'issue-row selected' : 'issue-row'}
            >
              <td className="issue-cell">
                <div className="issue-title-line">
                  <button
                    type="button"
                    className="issue-title"
                    onClick={() => onSelect(issue.id)}
                  >
                    {issue.title}
                  </button>
                  <span
                    className={`priority priority-${issue.priority}`}
                    aria-label={`优先级：${priorityLabels[issue.priority]}`}
                  >
                    {priorityLabels[issue.priority]}
                  </span>
                </div>
                <div className="issue-details">
                  {issue.description ? (
                    <p className="issue-excerpt">{issue.description}</p>
                  ) : (
                    <span className="muted">未填写问题描述</span>
                  )}
                  <code className="issue-id" title={issue.id}>
                    ID {issue.id.slice(0, 8).toUpperCase()}
                  </code>
                </div>
              </td>
              <td className="location-cell" data-label="设备与区域">
                <strong className="device-code">
                  {issue.deviceCode || '未关联设备'}
                </strong>
                <span>{issue.zone || '未划分区域'}</span>
              </td>
              <td className="position-cell" data-label="场景坐标">
                <div
                  className="coordinate-grid"
                  aria-label={`场景坐标 X ${issue.position.x.toFixed(2)}，Y ${issue.position.y.toFixed(2)}，Z ${issue.position.z.toFixed(2)}`}
                >
                  <span className="coordinate">
                    <b>X</b>
                    {issue.position.x.toFixed(2)}
                  </span>
                  <span className="coordinate">
                    <b>Y</b>
                    {issue.position.y.toFixed(2)}
                  </span>
                  <span className="coordinate">
                    <b>Z</b>
                    {issue.position.z.toFixed(2)}
                  </span>
                </div>
              </td>
              <td className="time-cell" data-label="上报时间">
                <time
                  className="issue-time"
                  dateTime={issue.createdAt}
                  aria-label={formatFullDate(issue.createdAt)}
                >
                  <strong>{formatDate(issue.createdAt)}</strong>
                  <span>{formatTime(issue.createdAt)}</span>
                </time>
              </td>
              <td className="status-cell" data-label="处理状态">
                <div
                  className={`status-control status-control-${issue.status}`}
                >
                  <i className="status-dot" aria-hidden="true" />
                  <select
                    className="status-select"
                    aria-label={`修改 ${issue.title} 的状态`}
                    value={issue.status}
                    disabled={disabled}
                    onChange={(event) => {
                      void onStatusChange(
                        issue.id,
                        event.target.value as IssueStatus,
                      )
                    }}
                  >
                    <option value="open">{statusLabels.open}</option>
                    <option value="in_progress">
                      {statusLabels.in_progress}
                    </option>
                    <option value="resolved">{statusLabels.resolved}</option>
                  </select>
                </div>
                <span className="status-feedback" role="status">
                  {updatingId === issue.id ? '正在保存状态…' : ''}
                </span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function formatDate(value: string): string {
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) {
    return value
  }
  return dateFormatter.format(parsed)
}

function formatTime(value: string): string {
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) {
    return ''
  }
  return timeFormatter.format(parsed)
}

function formatFullDate(value: string): string {
  const date = formatDate(value)
  const time = formatTime(value)
  return time ? `${date} ${time}` : date
}
