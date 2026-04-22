import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/axios'
import { useToast } from '@/context/ToastContext'
import type { MediaFileDto, PublicationDetailDto, PublicationListItemDto } from './types'

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

  const regenerate = useMutation({
    mutationFn: (feedback: string) =>
      apiClient
        .post<PublicationDetailDto>(`/publications/${publicationId}/regenerate`, { feedback })
        .then(r => r.data),
    onSuccess: () => {
      toast('Regeneration requested', 'success')
      invalidateDetail()
    },
    onError: (error: unknown) => {
      const status = (error as { response?: { status?: number } })?.response?.status
      const message = status === 409
        ? 'Cannot regenerate: publication is no longer in a regeneratable state.'
        : 'Failed to request regeneration'
      toast(message, 'error')
    },
  })

  const uploadMedia = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return apiClient
        .post<MediaFileDto>(`/publications/${publicationId}/media`, form, {
          headers: { 'Content-Type': 'multipart/form-data' },
        })
        .then(r => r.data)
    },
    onSuccess: () => {
      toast('Media uploaded', 'success')
      invalidateDetail()
    },
    onError: () => toast('Failed to upload media', 'error'),
  })

  const deleteMedia = useMutation({
    mutationFn: (mediaId: string) =>
      apiClient
        .delete(`/publications/${publicationId}/media/${mediaId}`)
        .then(r => r.data),
    onSuccess: () => {
      toast('Media deleted', 'success')
      invalidateDetail()
    },
    onError: () => toast('Failed to delete media', 'error'),
  })

  return { generateContent, updateContent, approve, reject, regenerate, uploadMedia, deleteMedia }
}
