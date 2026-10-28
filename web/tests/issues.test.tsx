import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import App from '../src/App'
import type { Issue } from '../src/api/issues'

const apiBase = 'http://127.0.0.1:8080'
const issue: Issue = {
  id: '0f2c7b23-7dff-4f1e-a6ab-52ef441b25c8',
  title: '1 号机柜温度异常',
  description: '机柜右侧温度明显偏高',
  priority: 'high',
  status: 'open',
  position: { x: 1.25, y: 0.8, z: 2.4 },
  createdAt: '2026-10-27T11:30:00.000000000Z',
  updatedAt: '2026-10-27T11:30:00.000000000Z',
}

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('inspection issue dashboard', () => {
  it('loads and renders issue fields', async () => {
    server.use(
      http.get(`${apiBase}/api/issues`, () =>
        HttpResponse.json({ items: [issue] }),
      ),
    )

    render(<App />)

    expect(await screen.findByText(issue.title)).toBeInTheDocument()
    expect(screen.getByText('高')).toBeInTheDocument()
    expect(screen.getByLabelText(`修改 ${issue.title} 的状态`)).toHaveValue('open')
    expect(screen.getByText(issue.description)).toBeInTheDocument()
  })

  it('renders an empty state when the API has no issues', async () => {
    server.use(
      http.get(`${apiBase}/api/issues`, () => HttpResponse.json({ items: [] })),
    )

    render(<App />)

    expect(await screen.findByText('当前没有问题')).toBeInTheDocument()
    expect(screen.getByText(/Unity 客户端上报问题后/)).toBeInTheDocument()
  })

  it('shows a readable error instead of a blank page', async () => {
    server.use(
      http.get(`${apiBase}/api/issues`, () =>
        HttpResponse.json(
          { error: { code: 'INTERNAL', message: 'backend unavailable' } },
          { status: 503 },
        ),
      ),
    )

    render(<App />)

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('无法加载问题')
    expect(alert).toHaveTextContent('HTTP 503')
    expect(screen.getByRole('button', { name: '重试' })).toBeInTheDocument()
  })

  it('updates status from the backend response', async () => {
    server.use(
      http.get(`${apiBase}/api/issues`, () =>
        HttpResponse.json({ items: [issue] }),
      ),
      http.patch(`${apiBase}/api/issues/:id/status`, async ({ request }) => {
        const body = (await request.json()) as { status: Issue['status'] }
        return HttpResponse.json({
          ...issue,
          status: body.status,
          updatedAt: '2026-10-27T12:00:00.000000000Z',
        })
      }),
    )

    const user = userEvent.setup()
    render(<App />)

    const select = await screen.findByLabelText(`修改 ${issue.title} 的状态`)
    await user.selectOptions(select, 'in_progress')

    await waitFor(() => expect(select).toHaveValue('in_progress'))
    expect(screen.getByText('处理中', { selector: '.status-label' })).toBeInTheDocument()
  })

  it('retains the original status when update fails', async () => {
    server.use(
      http.get(`${apiBase}/api/issues`, () =>
        HttpResponse.json({ items: [issue] }),
      ),
      http.patch(`${apiBase}/api/issues/:id/status`, () =>
        HttpResponse.json(
          { error: { code: 'INTERNAL', message: 'write failed' } },
          { status: 503 },
        ),
      ),
    )

    const user = userEvent.setup()
    render(<App />)

    const select = await screen.findByLabelText(`修改 ${issue.title} 的状态`)
    await user.selectOptions(select, 'resolved')

    expect(await screen.findByRole('alert')).toHaveTextContent('状态更新失败')
    await waitFor(() => expect(select).toHaveValue('open'))
  })
})
