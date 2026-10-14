<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { projects } from '../api/projects'
import { ApiError, errorMessage } from '../api/problems'
import type { Project } from '../types/contracts'
import { useAuthStore } from '../stores/auth'
import PageHeader from '../components/PageHeader.vue'
import { formatUtcDateTime as time } from '../utils/datetime'

const auth = useAuthStore()
const query = reactive({ pageNumber: 1, pageSize: 20, keyword: '', deleted: false })
const rows = ref<Project[]>([])
const total = ref(0)
const loading = ref(false)
const dialog = ref(false)
const saving = ref(false)
const uncertain = ref(false)
const form = reactive({ name: '' })
const editing = ref<Project | null>(null)
let operationKey = ''
const rowOperationKeys = new Map<string, string>()

async function load() {
  loading.value = true
  try {
    const page = await projects.list(query)
    rows.value = page.items
    total.value = page.totalCount
  } catch (error) {
    ElMessage.error(errorMessage(error))
  } finally {
    loading.value = false
  }
}
function open(project?: Project) {
  editing.value = project ?? null
  form.name = project?.name ?? ''
  operationKey = crypto.randomUUID()
  uncertain.value = false
  dialog.value = true
}
async function save() {
  if (!form.name.trim()) {
    ElMessage.warning('请输入项目名称。')
    return
  }
  saving.value = true
  try {
    if (editing.value) {
      await projects.update(editing.value, form.name, operationKey)
    } else {
      await projects.create(form.name, operationKey)
    }
    ElMessage.success(editing.value ? '项目已更新。' : '项目已创建。')
    uncertain.value = false
    dialog.value = false
    await load()
  } catch (error) {
    uncertain.value = error instanceof ApiError && error.uncertain
    ElMessage.error(errorMessage(error))
    if (!uncertain.value) {
      operationKey = crypto.randomUUID()
    }
    if (error instanceof ApiError && error.status === 412) {
      await load()
    }
  } finally {
    saving.value = false
  }
}
async function changeDeletion(project: Project) {
  const action = query.deleted ? 'restore' : 'delete'
  try {
    await ElMessageBox.confirm(
      query.deleted ? '将这个项目恢复到有效列表？' : '项目将移入回收站，之后可以恢复。',
      query.deleted ? '恢复项目' : '删除项目',
      { confirmButtonText: '确认', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  const identity = `${action}:${project.id}:${project.version}`
  const key = rowOperationKeys.get(identity) ?? crypto.randomUUID()
  rowOperationKeys.set(identity, key)
  try {
    if (query.deleted) {
      await projects.restore(project, key)
    } else {
      await projects.remove(project, key)
    }
    rowOperationKeys.delete(identity)
    ElMessage.success(query.deleted ? '项目已恢复。' : '项目已移入回收站。')
    await load()
  } catch (error) {
    ElMessage.error(errorMessage(error))
    if (error instanceof ApiError && !error.uncertain) {
      rowOperationKeys.delete(identity)
      await load()
    }
  }
}
async function search() {
  query.pageNumber = 1
  await load()
}
async function changeTab(deleted: boolean) {
  query.deleted = deleted
  await search()
}
onMounted(load)
</script>
<template>
  <PageHeader title="项目管理" description="维护业务项目。每次变更都有版本校验与操作记录。">
    <ElButton
      v-if="auth.can('projects.projects.create') && !query.deleted"
      type="primary"
      @click="open()"
      >＋ 新建项目</ElButton
    >
  </PageHeader>
  <section class="panel table-panel">
    <div class="table-toolbar">
      <div class="table-tabs">
        <button :class="{ selected: !query.deleted }" @click="changeTab(false)">有效项目</button
        ><button
          v-if="auth.can('projects.projects.restore')"
          :class="{ selected: query.deleted }"
          @click="changeTab(true)"
        >
          回收站
        </button>
      </div>
      <div class="search-controls">
        <ElInput
          v-model="query.keyword"
          clearable
          placeholder="搜索项目名称"
          maxlength="100"
          style="width: 240px"
          @keyup.enter="search"
        /><ElButton @click="search">查询</ElButton><ElButton text @click="load">刷新</ElButton>
      </div>
    </div>
    <ElTable
      v-loading="loading"
      :data="rows"
      row-key="id"
      empty-text="还没有项目，从新建一个项目开始。"
    >
      <ElTableColumn label="项目名称" min-width="240"
        ><template #default="{ row }"
          ><div class="project-name">
            <span class="project-icon">P</span>
            <div>
              <b>{{ row.name }}</b
              ><small>{{ row.id }}</small>
            </div>
          </div></template
        ></ElTableColumn
      >
      <ElTableColumn label="创建时间" min-width="170"
        ><template #default="{ row }">{{ time(row.createdAt) }}</template></ElTableColumn
      >
      <ElTableColumn label="最近更新" min-width="170"
        ><template #default="{ row }">{{ time(row.updatedAt) }}</template></ElTableColumn
      >
      <ElTableColumn label="状态" width="95"
        ><template #default="{ row }"
          ><ElTag :type="row.isDeleted ? 'info' : 'success'" effect="light">{{
            row.isDeleted ? '已删除' : '有效'
          }}</ElTag></template
        ></ElTableColumn
      >
      <ElTableColumn label="操作" width="170" fixed="right"
        ><template #default="{ row }"
          ><ElButton
            v-if="!query.deleted && auth.can('projects.projects.update')"
            link
            type="primary"
            @click="open(row)"
            >编辑</ElButton
          ><ElButton
            v-if="!query.deleted && auth.can('projects.projects.delete')"
            link
            type="danger"
            @click="changeDeletion(row)"
            >删除</ElButton
          ><ElButton
            v-if="query.deleted && auth.can('projects.projects.restore')"
            link
            type="primary"
            @click="changeDeletion(row)"
            >恢复</ElButton
          ></template
        ></ElTableColumn
      >
    </ElTable>
    <div class="table-pagination">
      <span class="muted">共 {{ total }} 个项目</span
      ><ElPagination
        v-model:current-page="query.pageNumber"
        v-model:page-size="query.pageSize"
        :total="total"
        :page-sizes="[20, 50, 100]"
        layout="sizes, prev, pager, next"
        @change="load"
      />
    </div>
  </section>
  <ElDialog
    v-model="dialog"
    :title="editing ? '编辑项目' : '新建项目'"
    width="480px"
    :close-on-click-modal="!saving && !uncertain"
    :close-on-press-escape="!saving && !uncertain"
    :show-close="!saving && !uncertain"
  >
    <ElAlert
      v-if="uncertain"
      title="上次请求的结果尚未确认，请保持数据不变并重试。"
      type="warning"
      :closable="false"
      class="space-bottom"
    />
    <ElForm label-position="top" @submit.prevent="save"
      ><ElFormItem label="项目名称" required
        ><ElInput
          v-model="form.name"
          :disabled="uncertain || saving"
          maxlength="100"
          show-word-limit
          placeholder="例如：客户关系管理" /></ElFormItem
    ></ElForm>
    <template #footer
      ><ElButton :disabled="saving || uncertain" @click="dialog = false">取消</ElButton
      ><ElButton type="primary" :loading="saving" @click="save">{{
        uncertain ? '重试保存' : '保存项目'
      }}</ElButton></template
    >
  </ElDialog>
</template>
