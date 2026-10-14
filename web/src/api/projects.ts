import { http } from './http'
import type { Project, Page } from '../types/contracts'

export const projects = {
  async list(params: {
    pageNumber: number
    pageSize: number
    keyword?: string
    deleted?: boolean
  }) {
    return (await http.get<Page<Project>>('/projects', { params })).data
  },
  async create(name: string, key: string) {
    return (
      await http.post<Project>('/projects', { name }, { headers: { 'Idempotency-Key': key } })
    ).data
  },
  async update(project: Project, name: string, key: string) {
    return (
      await http.put<Project>(
        `/projects/${project.id}`,
        { name },
        { headers: { 'Idempotency-Key': key, 'If-Match': `"${project.version}"` } },
      )
    ).data
  },
  async remove(project: Project, key: string) {
    await http.delete(`/projects/${project.id}`, {
      headers: { 'Idempotency-Key': key, 'If-Match': `"${project.version}"` },
    })
  },
  async restore(project: Project, key: string) {
    await http.post(
      `/projects/${project.id}/restore`,
      {},
      { headers: { 'Idempotency-Key': key, 'If-Match': `"${project.version}"` } },
    )
  },
}
