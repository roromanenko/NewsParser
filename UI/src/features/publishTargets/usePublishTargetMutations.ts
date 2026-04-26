import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import { useToast } from '@/context/ToastContext'

export function usePublishTargetMutations() {
  const queryClient = useQueryClient()
  const { toast } = useToast()
  const { selectedProjectId } = useProjectStore()

  const createTarget = useMutation({
    mutationFn: (data: { name: string; platform: string; identifier: string; systemPrompt: string }) =>
      apiClient.post(`/projects/${selectedProjectId}/publish-targets`, data),
    onSuccess: () => {
      toast('Publish target created', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'publishTargets'] })
    },
    onError: () => toast('Failed to create publish target', 'error'),
  })

  const updateTarget = useMutation({
    mutationFn: ({ id, data }: { id: string; data: { name: string; identifier: string; systemPrompt: string; isActive: boolean } }) =>
      apiClient.put(`/projects/${selectedProjectId}/publish-targets/${id}`, data),
    onSuccess: () => {
      toast('Publish target updated', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'publishTargets'] })
    },
    onError: () => toast('Failed to update publish target', 'error'),
  })

  const deleteTarget = useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/projects/${selectedProjectId}/publish-targets/${id}`),
    onSuccess: () => {
      toast('Publish target deleted', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'publishTargets'] })
    },
    onError: () => toast('Failed to delete publish target', 'error'),
  })

  return { createTarget, updateTarget, deleteTarget }
}
