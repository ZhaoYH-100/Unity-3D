import { expect, test } from '@playwright/test'
import { randomUUID } from 'node:crypto'

const apiBase = 'http://127.0.0.1:18080'

test('loads an issue and persists a status update across reload', async ({
  page,
  request,
}) => {
  const title = `E2E 温度异常 ${Date.now()}`
  const createResponse = await request.post(`${apiBase}/api/issues`, {
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': randomUUID(),
    },
    data: {
      title,
      description: 'Playwright 端到端测试记录',
      priority: 'high',
      position: { x: 1.25, y: 0.8, z: 2.4 },
    },
  })
  expect(createResponse.status()).toBe(201)

  await page.goto('/')
  await expect(page.getByText(title)).toBeVisible()

  const statusSelect = page.getByLabel(`修改 ${title} 的状态`)
  await expect(statusSelect).toHaveValue('open')
  await statusSelect.selectOption('in_progress')
  await expect(statusSelect).toHaveValue('in_progress')

  await page.reload()
  await expect(page.getByLabel(`修改 ${title} 的状态`)).toHaveValue('in_progress')
})

test('shows an error banner when the backend request fails', async ({ page }) => {
  await page.route('**/api/issues', (route) => route.abort('failed'))

  await page.goto('/')

  await expect(page.getByRole('alert')).toContainText('无法加载问题')
  await expect(page.getByText('操作未完成')).toBeVisible()
})
