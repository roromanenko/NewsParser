import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import { PublishTargetsApi } from '@/api/generated'
import { useToast } from '@/context/ToastContext'

const publishTargetsApi = new PublishTargetsApi(undefined, '', apiClient)

export function usePublishTargetMutations() {
  const queryClient = useQueryClient()
  const { toast } = useToast()
  const { selectedProjectId } = useProjectStore()

  const createTarget = useMutation({
    mutationFn: (data: { name: string; platform: string; identifier: string; systemPrompt: string }) =>
      publishTargetsApi.projectsProjectIdPublishTargetsPost(selectedProjectId!, data),
    onSuccess: () => {
      toast('Publish target created', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'publishTargets'] })
    },
    onError: () => toast('Failed to create publish target', 'error'),
  })

  const updateTarget = useMutation({
    mutationFn: ({ id, data }: { id: string; data: { name: string; identifier: string; systemPrompt: string; isActive: boolean } }) =>
      publishTargetsApi.projectsProjectIdPublishTargetsIdPut(id, selectedProjectId!, data),
    onSuccess: () => {
      toast('Publish target updated', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'publishTargets'] })
    },
    onError: () => toast('Failed to update publish target', 'error'),
  })

  const deleteTarget = useMutation({
    mutationFn: (id: string) =>
      publishTargetsApi.projectsProjectIdPublishTargetsIdDelete(id, selectedProjectId!),
    onSuccess: () => {
      toast('Publish target deleted', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'publishTargets'] })
    },
    onError: () => toast('Failed to delete publish target', 'error'),
  })

  return { createTarget, updateTarget, deleteTarget }
}
