import type { Issue, IssueStatus } from '../api/issues'

interface IssueTableProps {
  issues: Issue[]
  disabled: boolean
  updatingId: string | null
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
  dateStyle: 'medium',
  timeStyle: 'short',
})

export function IssueTable({
  issues,
  disabled,
  updatingId,
  onStatusChange,
}: IssueTableProps) {
  return (
    <div className="table-scroll">
      <table>
        <caption className="sr-only">巡检问题列表</caption>
        <thead>
          <tr>
            <th scope="col">问题</th>
            <th scope="col">优先级</th>
            <th scope="col">坐标</th>
            <th scope="col">上报时间</th>
            <th scope="col">状态</th>
          </tr>
        </thead>
        <tbody>
          {issues.map((issue) => (
            <tr key={issue.id}>
              <td>
                <div className="issue-title">{issue.title}</div>
                {issue.description ? (
                  <details>
                    <summary>查看描述</summary>
                    <p>{issue.description}</p>
                  </details>
                ) : (
                  <span className="muted">无描述</span>
                )}
                <code className="issue-id">{issue.id.slice(0, 8)}</code>
              </td>
              <td>
                <span className={`priority priority-${issue.priority}`}>
                  {priorityLabels[issue.priority]}
                </span>
              </td>
              <td>
                <span className="coordinates">
                  {issue.position.x.toFixed(2)}
                  <br />
                  {issue.position.y.toFixed(2)}
                  <br />
                  {issue.position.z.toFixed(2)}
                </span>
              </td>
              <td>
                <time dateTime={issue.createdAt}>
                  {formatDate(issue.createdAt)}
                </time>
              </td>
              <td>
                <select
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
                  <option value="open">待处理</option>
                  <option value="in_progress">处理中</option>
                  <option value="resolved">已完成</option>
                </select>
                <span className="status-label">
                  {updatingId === issue.id
                    ? '保存中…'
                    : statusLabels[issue.status]}
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
