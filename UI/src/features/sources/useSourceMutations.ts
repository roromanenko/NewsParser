import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import { SourcesApi } from '@/api/generated'
import { useToast } from '@/context/ToastContext'

const sourcesApi = new SourcesApi(undefined, '', apiClient)

export function useSourceMutations() {
  const queryClient = useQueryClient()
  const { toast } = useToast()
  const { selectedProjectId } = useProjectStore()

  const createSource = useMutation({
    mutationFn: (data: { name: string; url: string; type: string }) =>
      sourcesApi.projectsProjectIdSourcesPost(selectedProjectId!, data),
    onSuccess: () => {
      toast('Source created successfully', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'sources'] })
    },
    onError: () => toast('Failed to create source', 'error'),
  })

  const updateSource = useMutation({
    mutationFn: ({ id, data }: { id: string; data: { name: string; url: string; isActive: boolean } }) =>
      sourcesApi.projectsProjectIdSourcesIdPut(id, selectedProjectId!, data),
    onSuccess: () => {
      toast('Source updated successfully', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'sources'] })
    },
    onError: () => toast('Failed to update source', 'error'),
  })

  const deleteSource = useMutation({
    mutationFn: (id: string) =>
      sourcesApi.projectsProjectIdSourcesIdDelete(id, selectedProjectId!),
    onSuccess: () => {
      toast('Source deleted', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'sources'] })
    },
    onError: () => toast('Failed to delete source', 'error'),
  })

  return { createSource, updateSource, deleteSource }
}
