import type { IssuePriority, IssueStatus } from '../api/issues'

export interface IssueFilterState {
  query: string
  priority: IssuePriority | 'all'
  status: IssueStatus | 'all'
  deviceCode: string
  zone: string
}

interface IssueFiltersProps {
  filters: IssueFilterState
  deviceCodes: string[]
  zones: string[]
  visibleCount: number
  totalCount: number
  onChange: (filters: IssueFilterState) => void
  onReset: () => void
}

export function IssueFilters({
  filters,
  deviceCodes,
  zones,
  visibleCount,
  totalCount,
  onChange,
  onReset,
}: IssueFiltersProps) {
  const hasFilters =
    filters.query !== '' ||
    filters.priority !== 'all' ||
    filters.status !== 'all' ||
    filters.deviceCode !== 'all' ||
    filters.zone !== 'all'

  function update<K extends keyof IssueFilterState>(
    key: K,
    value: IssueFilterState[K],
  ) {
    onChange({ ...filters, [key]: value })
  }

  return (
    <div className="filter-console" aria-label="问题筛选">
      <label className="search-field">
        <span>搜索现场记录</span>
        <input
          type="search"
          value={filters.query}
          placeholder="标题、描述、ID 或设备编号"
          onChange={(event) => update('query', event.target.value)}
        />
      </label>

      <label>
        <span>状态</span>
        <select
          value={filters.status}
          onChange={(event) =>
            update('status', event.target.value as IssueFilterState['status'])
          }
        >
          <option value="all">全部状态</option>
          <option value="open">待处理</option>
          <option value="in_progress">处理中</option>
          <option value="resolved">已完成</option>
        </select>
      </label>

      <label>
        <span>优先级</span>
        <select
          value={filters.priority}
          onChange={(event) =>
            update('priority', event.target.value as IssueFilterState['priority'])
          }
        >
          <option value="all">全部优先级</option>
          <option value="high">高优先级</option>
          <option value="medium">中优先级</option>
          <option value="low">低优先级</option>
        </select>
      </label>

      <label>
        <span>设备</span>
        <select
          value={filters.deviceCode}
          onChange={(event) => update('deviceCode', event.target.value)}
        >
          <option value="all">全部设备</option>
          {deviceCodes.map((deviceCode) => (
            <option key={deviceCode} value={deviceCode}>
              {deviceCode}
            </option>
          ))}
        </select>
      </label>

      <label>
        <span>区域</span>
        <select
          value={filters.zone}
          onChange={(event) => update('zone', event.target.value)}
        >
          <option value="all">全部区域</option>
          {zones.map((zone) => (
            <option key={zone} value={zone}>
              {zone}
            </option>
          ))}
        </select>
      </label>

      <div className="filter-result">
        <span>
          显示 <strong>{visibleCount}</strong> / {totalCount}
        </span>
        {hasFilters ? (
          <button type="button" className="text-button" onClick={onReset}>
            清除筛选
          </button>
        ) : (
          <span className="filter-hint">全部现场记录</span>
        )}
      </div>
    </div>
  )
}
