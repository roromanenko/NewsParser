import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/axios'
import { useToast } from '@/context/ToastContext'

interface CreateProjectData {
  name: string
  slug?: string
  analyzerPromptText: string
  categories: string[]
  outputLanguage: string
  outputLanguageName: string
}

interface UpdateProjectData {
  name: string
  analyzerPromptText: string
  categories: string[]
  outputLanguage: string
  outputLanguageName: string
  isActive: boolean
}

export function useCreateProject() {
  const queryClient = useQueryClient()
  const { toast } = useToast()

  return useMutation({
    mutationFn: (data: CreateProjectData) =>
      apiClient.post('/projects', data).then(r => r.data),
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
    mutationFn: ({ id, data }: { id: string; data: UpdateProjectData }) =>
      apiClient.put(`/projects/${id}`, data).then(r => r.data),
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
      apiClient.patch(`/projects/${id}/status`, isActive, {
        headers: { 'Content-Type': 'application/json' },
      }),
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
    mutationFn: (id: string) => apiClient.delete(`/projects/${id}`),
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
