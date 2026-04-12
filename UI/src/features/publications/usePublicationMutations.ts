import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/axios'
import { useToast } from '@/context/ToastContext'
import type { PublicationDetailDto, PublicationListItemDto } from './types'

export function usePublicationMutations(publicationId?: string) {
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const invalidateDetail = () => {
    queryClient.invalidateQueries({ queryKey: ['publication', publicationId] })
    queryClient.invalidateQueries({ queryKey: ['publications', 'by-event'] })
  }

  const generateContent = useMutation({
    mutationFn: (data: { eventId: string; publishTargetId: string }) =>
      apiClient
        .post<PublicationListItemDto>('/publications/generate', data)
        .then(r => r.data),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({
        queryKey: ['publications', 'by-event', variables.eventId],
      })
    },
    onError: () => toast('Failed to generate content', 'error'),
  })

  const updateContent = useMutation({
    mutationFn: (data: { content: string; selectedMediaFileIds: string[] }) =>
      apiClient
        .put<PublicationDetailDto>(`/publications/${publicationId}/content`, data)
        .then(r => r.data),
    onSuccess: () => {
      toast('Content updated', 'success')
      invalidateDetail()
    },
    onError: () => toast('Failed to update content', 'error'),
  })

  const approve = useMutation({
    mutationFn: () =>
      apiClient
        .post<PublicationDetailDto>(`/publications/${publicationId}/approve`)
        .then(r => r.data),
    onSuccess: () => {
      toast('Publication approved', 'success')
      invalidateDetail()
    },
    onError: () => toast('Failed to approve publication', 'error'),
  })

  const reject = useMutation({
    mutationFn: (reason: string) =>
      apiClient
        .post<PublicationDetailDto>(`/publications/${publicationId}/reject`, { reason })
        .then(r => r.data),
    onSuccess: () => {
      toast('Publication rejected', 'success')
      invalidateDetail()
    },
    onError: () => toast('Failed to reject publication', 'error'),
  })

  const send = useMutation({
    mutationFn: () =>
      apiClient
        .post<PublicationDetailDto>(`/publications/${publicationId}/send`)
        .then(r => r.data),
    onSuccess: () => {
      toast('Publication queued for sending', 'success')
      invalidateDetail()
    },
    onError: () => toast('Failed to send publication', 'error'),
  })

  return { generateContent, updateContent, approve, reject, send }
}
