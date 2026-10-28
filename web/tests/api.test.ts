import { afterEach, describe, expect, it, vi } from 'vitest'
import { listIssues } from '../src/api/issues'

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('issue API client', () => {
  it('aborts a request after ten seconds', async () => {
    vi.useFakeTimers()
    vi.spyOn(globalThis, 'fetch').mockImplementation((_input, init) => {
      return new Promise<Response>((_resolve, reject) => {
        init?.signal?.addEventListener(
          'abort',
          () => reject(new DOMException('Aborted', 'AbortError')),
          { once: true },
        )
      })
    })

    const pending = listIssues()
    const expectation = expect(pending).rejects.toThrow('请求超时')
    await vi.advanceTimersByTimeAsync(10_000)

    await expectation
  })
})
