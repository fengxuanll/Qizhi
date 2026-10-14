/** 服务端 long / bigint 雪花 ID 的十进制字符串；不能转换为 JavaScript Number。 */
export type EntityId = string

export interface TenantSummary {
  id: EntityId
  name: string
  slug: string
  state: string
}
export interface UserProfile {
  id: EntityId
  userName: string
  displayName: string
  twoFactorEnabled: boolean
  tenantId: EntityId | null
  tenants: TenantSummary[]
  permissions: string[]
  mfaSetupRequired: boolean
}
export interface Project {
  id: EntityId
  name: string
  /** UTC ISO 8601 时间点，展示时由公共 formatter 转为访问人时区。 */
  createdAt: string
  updatedAt: string
  version: string
  isDeleted: boolean
}
export interface Page<T> {
  items: T[]
  totalCount: number
  pageNumber: number
  pageSize: number
}
export interface ProblemDetails {
  status?: number
  title?: string
  detail?: string
  code?: string
  traceId?: string
  errors?: Record<string, string[]>
}
