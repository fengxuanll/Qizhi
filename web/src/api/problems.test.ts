import { describe, expect, it } from 'vitest'
import { ApiError, errorMessage } from './problems'

describe('API 错误解释', () => {
  it('优先展示字段校验错误', () => {
    expect(new ApiError(400, { title: '请求错误', errors: { name: ['请输入名称'] } }).message).toBe(
      '请输入名称',
    )
  })
  it('仅服务故障追加追踪编号', () => {
    expect(errorMessage(new ApiError(500, { detail: '暂不可用', traceId: 'trace-123' }))).toContain(
      'trace-123',
    )
  })
  it('网络与服务故障保留不确定状态', () => {
    expect(new ApiError(0, {}).uncertain).toBe(true)
    expect(new ApiError(412, {}).uncertain).toBe(false)
  })
})
