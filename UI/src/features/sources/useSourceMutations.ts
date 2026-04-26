import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import { useToast } from '@/context/ToastContext'

export function useSourceMutations() {
  const queryClient = useQueryClient()
  const { toast } = useToast()
  const { selectedProjectId } = useProjectStore()

  const createSource = useMutation({
    mutationFn: (data: { name: string; url: string; type: string }) =>
      apiClient.post(`/projects/${selectedProjectId}/sources`, data),
    onSuccess: () => {
      toast('Source created successfully', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'sources'] })
    },
    onError: () => toast('Failed to create source', 'error'),
  })

  const updateSource = useMutation({
    mutationFn: ({ id, data }: { id: string; data: { name: string; url: string; isActive: boolean } }) =>
      apiClient.put(`/projects/${selectedProjectId}/sources/${id}`, data),
    onSuccess: () => {
      toast('Source updated successfully', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'sources'] })
    },
    onError: () => toast('Failed to update source', 'error'),
  })

  const deleteSource = useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/projects/${selectedProjectId}/sources/${id}`),
    onSuccess: () => {
      toast('Source deleted', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'sources'] })
    },
    onError: () => toast('Failed to delete source', 'error'),
  })

  return { createSource, updateSource, deleteSource }
}
