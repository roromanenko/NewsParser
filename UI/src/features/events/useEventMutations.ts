import { useMutation, useQueryClient } from '@tanstack/react-query'
import { EventsApi } from '@/api/generated'
import { apiClient } from '@/lib/axios'
import { useToast } from '@/context/ToastContext'

const eventsApi = new EventsApi(undefined, '', apiClient)

export function useEventMutations(eventId?: string) {
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const resolveContradiction = useMutation({
    mutationFn: (contradictionId: string) =>
      eventsApi.eventsIdResolveContradictionPost(eventId!, { contradictionId }),
    onSuccess: () => {
      toast('Contradiction resolved', 'success')
      queryClient.invalidateQueries({ queryKey: ['event', eventId] })
    },
    onError: () => toast('Failed to resolve contradiction', 'error'),
  })

  const mergeEvents = useMutation({
    mutationFn: (data: { sourceEventId: string; targetEventId: string }) =>
      eventsApi.eventsMergePost(data),
    onSuccess: () => {
      toast('Events merged successfully', 'success')
      queryClient.invalidateQueries({ queryKey: ['events'] })
    },
    onError: () => toast('Failed to merge events', 'error'),
  })

  const reclassifyArticle = useMutation({
    mutationFn: (data: { articleId: string; role: string; targetEventId?: string }) =>
      eventsApi.eventsIdReclassifyPost(eventId!, {
        articleId: data.articleId,
        targetEventId: data.targetEventId ?? eventId!,
        role: data.role,
      }),
    onSuccess: () => {
      toast('Article role updated', 'success')
      queryClient.invalidateQueries({ queryKey: ['event', eventId] })
    },
    onError: () => toast('Failed to update article role', 'error'),
  })

  const changeStatus = useMutation({
    mutationFn: (status: string) =>
      eventsApi.eventsIdStatusPatch(eventId!, status),
    onSuccess: () => {
      toast('Event status updated', 'success')
      queryClient.invalidateQueries({ queryKey: ['event', eventId] })
      queryClient.invalidateQueries({ queryKey: ['events'] })
    },
    onError: () => toast('Failed to update status', 'error'),
  })

  const approveEvent = useMutation({
    mutationFn: (publishTargetIds: string[]) =>
      eventsApi.eventsIdApprovePost(eventId!, { publishTargetIds }),
    onSuccess: () => {
      toast('Event approved successfully', 'success')
      queryClient.invalidateQueries({ queryKey: ['event', eventId] })
      queryClient.invalidateQueries({ queryKey: ['events'] })
    },
    onError: () => toast('Failed to approve event', 'error'),
  })

  const rejectEvent = useMutation({
    mutationFn: (reason: string) =>
      eventsApi.eventsIdRejectPost(eventId!, { reason }),
    onSuccess: () => {
      toast('Event rejected', 'success')
      queryClient.invalidateQueries({ queryKey: ['event', eventId] })
      queryClient.invalidateQueries({ queryKey: ['events'] })
    },
    onError: () => toast('Failed to reject event', 'error'),
  })

  return { resolveContradiction, mergeEvents, reclassifyArticle, changeStatus, approveEvent, rejectEvent }
}
