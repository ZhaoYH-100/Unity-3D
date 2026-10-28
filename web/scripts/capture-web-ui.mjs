import { mkdir } from 'node:fs/promises'
import { randomUUID } from 'node:crypto'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'
import { chromium } from 'playwright'

const baseUrl = 'http://127.0.0.1:15173'
const apiUrl = 'http://127.0.0.1:18080'
const scriptDirectory = dirname(fileURLToPath(import.meta.url))
const outputDirectory = resolve(scriptDirectory, '../../Logs')

async function createIssue(request, issue) {
  const response = await request.post(`${apiUrl}/api/issues`, {
    headers: { 'Idempotency-Key': randomUUID() },
    data: {
      description: '现场巡检发现异常，需要复核设备状态并记录处理结果。',
      position: { x: 1.25, y: 0.8, z: 2.4 },
      ...issue,
    },
  })
  if (!response.ok()) {
    throw new Error(`Create issue failed: ${response.status()} ${await response.text()}`)
  }
  return response.json()
}

await mkdir(outputDirectory, { recursive: true })
const browser = await chromium.launch({ headless: true, channel: 'chrome' })

try {
  const context = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
  })
  const request = context.request
  await createIssue(request, {
    title: '1 号配电柜温度异常',
    priority: 'high',
    deviceCode: 'CAB-A',
    zone: 'A区 · 配电区',
  })
  await createIssue(request, {
    title: '设备箱封条破损',
    priority: 'medium',
    deviceCode: 'BOX-01',
    zone: 'B区 · 暂存区',
  })
  const resolved = await createIssue(request, {
    title: '巡检台照明偏移',
    priority: 'low',
    deviceCode: 'TBL-01',
    zone: 'C区 · 作业区',
  })
  await request.patch(`${apiUrl}/api/issues/${resolved.id}/status`, {
    data: { status: 'resolved' },
  })

  const page = await context.newPage()
  const consoleErrors = []
  page.on('console', (message) => {
    if (message.type() === 'error') {
      consoleErrors.push(message.text())
    }
  })
  await page.goto(baseUrl, { waitUntil: 'networkidle' })
  await page.getByText('1 号配电柜温度异常').waitFor()
  await page.screenshot({
    path: resolve(outputDirectory, 'web-ui-dashboard.png'),
    fullPage: true,
  })

  await page.getByRole('button', { name: '1 号配电柜温度异常' }).click()
  await page.getByRole('dialog').waitFor()
  await page.screenshot({
    path: resolve(outputDirectory, 'web-ui-detail.png'),
    fullPage: true,
  })

  const mobileContext = await browser.newContext({
    viewport: { width: 390, height: 844 },
  })
  const mobilePage = await mobileContext.newPage()
  await mobilePage.goto(baseUrl, { waitUntil: 'networkidle' })
  await mobilePage.getByText('1 号配电柜温度异常').waitFor()
  await mobilePage.screenshot({
    path: resolve(outputDirectory, 'web-ui-mobile.png'),
    fullPage: true,
  })

  console.log(
    JSON.stringify({
      dashboard: resolve(outputDirectory, 'web-ui-dashboard.png'),
      detail: resolve(outputDirectory, 'web-ui-detail.png'),
      mobile: resolve(outputDirectory, 'web-ui-mobile.png'),
      consoleErrors,
    }),
  )

  await mobileContext.close()
  await context.close()
} finally {
  await browser.close()
}
