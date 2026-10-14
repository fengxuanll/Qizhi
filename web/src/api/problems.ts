import type { ProblemDetails } from '../types/contracts'

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ProblemDetails,
  ) {
    const fieldError = Object.values(problem.errors ?? {}).flat()[0]
    super(fieldError ?? problem.detail ?? problem.title ?? '操作未完成，请稍后重试。')
  }
  get code() {
    return this.problem.code
  }
  get uncertain() {
    return this.status === 0 || this.status >= 500 || this.code === 'idempotency.in_progress'
  }
}
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError && error.problem.traceId && error.status >= 500) {
    return `${error.message}（追踪编号：${error.problem.traceId}）`
  }
  return error instanceof Error ? error.message : '操作未完成，请稍后重试。'
}
