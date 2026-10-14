import { describe, expect, it } from 'vitest'
import { formatUtcDateTime } from './datetime'

describe('UTC 时间展示', () => {
  it('相同时间点按访问时区显示，日期跨日正确', () => {
    const value = '2026-10-14T20:00:00.0000000+00:00'
    expect(formatUtcDateTime(value, 'Asia/Shanghai')).toBe('2026年10月15日 04:00')
    expect(formatUtcDateTime(value, 'UTC')).toBe('2026年10月14日 20:00')
    expect(formatUtcDateTime('2026-10-14T20:00:00Z', 'UTC')).toBe(formatUtcDateTime(value, 'UTC'))
  })
  it('拒绝没有时区的墙上时间，避免静默当成本地时间', () => {
    expect(() => formatUtcDateTime('2026-10-15T08:00:00')).toThrow('时间必须包含时区。')
  })
})
