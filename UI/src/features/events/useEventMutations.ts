import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import { useToast } from '@/context/ToastContext'

export function useEventMutations(eventId?: string) {
  const queryClient = useQueryClient()
  const { toast } = useToast()
  const { selectedProjectId } = useProjectStore()

  const resolveContradiction = useMutation({
    mutationFn: (contradictionId: string) =>
      apiClient.post(`/projects/${selectedProjectId}/events/${eventId}/resolve-contradiction`, { contradictionId }),
    onSuccess: () => {
      toast('Contradiction resolved', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'event', eventId] })
    },
    onError: () => toast('Failed to resolve contradiction', 'error'),
  })

  const mergeEvents = useMutation({
    mutationFn: (data: { sourceEventId: string; targetEventId: string }) =>
      apiClient.post(`/projects/${selectedProjectId}/events/merge`, data),
    onSuccess: () => {
      toast('Events merged successfully', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'events'] })
    },
    onError: () => toast('Failed to merge events', 'error'),
  })

  const reclassifyArticle = useMutation({
    mutationFn: (data: { articleId: string; role: string; targetEventId?: string }) =>
      apiClient.post(`/projects/${selectedProjectId}/events/${eventId}/reclassify`, {
        articleId: data.articleId,
        targetEventId: data.targetEventId ?? eventId,
        role: data.role,
      }),
    onSuccess: () => {
      toast('Article role updated', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'event', eventId] })
    },
    onError: () => toast('Failed to update article role', 'error'),
  })

  const changeStatus = useMutation({
    mutationFn: (status: string) =>
      apiClient.patch(`/projects/${selectedProjectId}/events/${eventId}/status`, status, {
        headers: { 'Content-Type': 'application/json' },
      }),
    onSuccess: () => {
      toast('Event status updated', 'success')
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'event', eventId] })
      queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'events'] })
    },
    onError: () => toast('Failed to update status', 'error'),
  })

  return { resolveContradiction, mergeEvents, reclassifyArticle, changeStatus }
}
