import { useMutation, useQueryClient } from '@tanstack/react-query'
import { SourcesApi } from '@/api/generated'
import { apiClient } from '@/lib/axios'
import { useToast } from '@/context/ToastContext'

const sourcesApi = new SourcesApi(undefined, '', apiClient)

export function useSourceMutations() {
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const createSource = useMutation({
    mutationFn: (data: { name: string; url: string; type: string }) =>
      sourcesApi.sourcesPost(data),
    onSuccess: () => {
      toast('Source created successfully', 'success')
      queryClient.invalidateQueries({ queryKey: ['sources'] })
    },
    onError: () => toast('Failed to create source', 'error'),
  })

  const updateSource = useMutation({
    mutationFn: ({ id, data }: { id: string; data: { name: string; url: string; isActive: boolean } }) =>
      sourcesApi.sourcesIdPut(id, data),
    onSuccess: () => {
      toast('Source updated successfully', 'success')
      queryClient.invalidateQueries({ queryKey: ['sources'] })
    },
    onError: () => toast('Failed to update source', 'error'),
  })

  const deleteSource = useMutation({
    mutationFn: (id: string) => sourcesApi.sourcesIdDelete(id),
    onSuccess: () => {
      toast('Source deleted', 'success')
      queryClient.invalidateQueries({ queryKey: ['sources'] })
    },
    onError: () => toast('Failed to delete source', 'error'),
  })

  return { createSource, updateSource, deleteSource }
}
