export const issueStatuses = ['open', 'in_progress', 'resolved'] as const
export type IssueStatus = (typeof issueStatuses)[number]
export type IssuePriority = 'low' | 'medium' | 'high'

export interface Position {
  x: number
  y: number
  z: number
}

export interface Issue {
  id: string
  title: string
  description: string
  priority: IssuePriority
  status: IssueStatus
  deviceCode: string
  zone: string
  position: Position
  createdAt: string
  updatedAt: string
}

interface APIErrorBody {
  error?: {
    code?: string
    message?: string
  }
}

const API_BASE_URL = (
  import.meta.env.VITE_API_BASE_URL || 'http://127.0.0.1:8080'
).replace(/\/$/, '')
const REQUEST_TIMEOUT_MS = 10_000

export class ApiError extends Error {
  readonly status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

export async function listIssues(signal?: AbortSignal): Promise<Issue[]> {
  const body = await request<{ items: Issue[] }>('/api/issues', { method: 'GET' }, signal)
  return body.items
}

export async function updateIssueStatus(
  id: string,
  status: IssueStatus,
  signal?: AbortSignal,
): Promise<Issue> {
  return request<Issue>(
    `/api/issues/${encodeURIComponent(id)}/status`,
    {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ status }),
    },
    signal,
  )
}

export async function deleteIssue(id: string, signal?: AbortSignal): Promise<void> {
  await request<void>(
    `/api/issues/${encodeURIComponent(id)}`,
    { method: 'DELETE' },
    signal,
  )
}

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return error.message
  }
  if (error instanceof Error) {
    return error.message
  }
  return '发生未知错误'
}

async function request<T>(
  path: string,
  init: RequestInit,
  externalSignal?: AbortSignal,
): Promise<T> {
  const controller = new AbortController()
  let timedOut = false
  const timeout = window.setTimeout(() => {
    timedOut = true
    controller.abort()
  }, REQUEST_TIMEOUT_MS)
  const abortFromCaller = () => controller.abort()
  externalSignal?.addEventListener('abort', abortFromCaller, { once: true })

  if (externalSignal?.aborted) {
    controller.abort()
  }

  try {
    const response = await fetch(`${API_BASE_URL}${path}`, {
      ...init,
      signal: controller.signal,
    })

    if (!response.ok) {
      const message = await readErrorMessage(response)
      throw new ApiError(message, response.status)
    }

    if (response.status === 204) {
      return undefined as T
    }
    return (await response.json()) as T
  } catch (error) {
    if (error instanceof ApiError) {
      throw error
    }
    if (externalSignal?.aborted) {
      throw error
    }
    if (timedOut) {
      throw new ApiError('请求超时，请检查后端服务后重试')
    }
    throw new ApiError('无法连接后端，请确认 Go 服务正在运行')
  } finally {
    window.clearTimeout(timeout)
    externalSignal?.removeEventListener('abort', abortFromCaller)
  }
}

async function readErrorMessage(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as APIErrorBody
    if (body.error?.message) {
      return `请求失败（HTTP ${response.status}）：${body.error.message}`
    }
  } catch {
    // The status code still provides a useful error when the body is not JSON.
  }
  return `请求失败（HTTP ${response.status}）`
}
