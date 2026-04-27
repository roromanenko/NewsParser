import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/axios'
import { ProjectsApi } from '@/api/generated'
import type { CreateProjectRequest, UpdateProjectRequest } from '@/api/generated'
import { useToast } from '@/context/ToastContext'

const projectsApi = new ProjectsApi(undefined, '', apiClient)

export function useCreateProject() {
  const queryClient = useQueryClient()
  const { toast } = useToast()

  return useMutation({
    mutationFn: (data: CreateProjectRequest) =>
      projectsApi.projectsPost(data).then(r => r.data),
    onSuccess: () => {
      toast('Project created', 'success')
      queryClient.invalidateQueries({ queryKey: ['projects'] })
    },
    onError: () => toast('Failed to create project', 'error'),
  })
}

export function useUpdateProject() {
  const queryClient = useQueryClient()
  const { toast } = useToast()

  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateProjectRequest }) =>
      projectsApi.projectsIdPut(id, data).then(r => r.data),
    onSuccess: () => {
      toast('Project updated', 'success')
      queryClient.invalidateQueries({ queryKey: ['projects'] })
    },
    onError: () => toast('Failed to update project', 'error'),
  })
}

export function useToggleProjectActive() {
  const queryClient = useQueryClient()
  const { toast } = useToast()

  return useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      projectsApi.projectsIdStatusPatch(id, isActive),
    onSuccess: () => {
      toast('Project status updated', 'success')
      queryClient.invalidateQueries({ queryKey: ['projects'] })
    },
    onError: () => toast('Failed to update project status', 'error'),
  })
}

export function useDeleteProject() {
  const queryClient = useQueryClient()
  const { toast } = useToast()

  return useMutation({
    mutationFn: (id: string) => projectsApi.projectsIdDelete(id),
    onSuccess: () => {
      toast('Project deleted', 'success')
      queryClient.invalidateQueries({ queryKey: ['projects'] })
    },
    onError: (error: unknown) => {
      const status = (error as { response?: { status?: number } })?.response?.status
      const message = status === 409
        ? 'Cannot delete project: it still has associated resources. Archive it instead.'
        : 'Failed to delete project'
      toast(message, 'error')
    },
  })
}
