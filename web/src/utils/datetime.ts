/** 服务端日期必须携带 Z 或 +00:00；默认按当前访问人的浏览器时区显示。 */
export function formatUtcDateTime(value: string, timeZone?: string): string {
  if (!/(Z|[+-]\d{2}:\d{2})$/.test(value)) throw new Error('时间必须包含时区。')
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) throw new Error('时间格式不正确。')
  return new Intl.DateTimeFormat('zh-CN', {
    dateStyle: 'medium',
    timeStyle: 'short',
    ...(timeZone ? { timeZone } : {}),
  }).format(date)
}
export function visitorTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone
}
