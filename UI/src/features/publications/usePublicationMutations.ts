import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import { PublicationsApi } from '@/api/generated'
import { useToast } from '@/context/ToastContext'

const publicationsApi = new PublicationsApi(undefined, '', apiClient)

export function usePublicationMutations(publicationId?: string) {
  const queryClient = useQueryClient()
  const { toast } = useToast()
  const { selectedProjectId } = useProjectStore()

  const invalidateDetail = () => {
    queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'publication', publicationId] })
    queryClient.invalidateQueries({ queryKey: ['project', selectedProjectId, 'publications', 'by-event'] })
  }

  const generateContent = useMutation({
    mutationFn: (data: { eventId: string; publishTargetId: string }) =>
      publicationsApi
        .projectsProjectIdPublicationsGeneratePost(selectedProjectId!, data)
        .then(r => r.data),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({
        queryKey: ['project', selectedProjectId, 'publications', 'by-event', variables.eventId],
      })
    },
    onError: () => toast('Failed to generate content', 'error'),
  })

  const updateContent = useMutation({
    mutationFn: (data: { content: string; selectedMediaFileIds: string[] }) =>
      publicationsApi
        .projectsProjectIdPublicationsIdContentPut(publicationId!, selectedProjectId!, data)
        .then(r => r.data),
    onSuccess: () => {
      toast('Content updated', 'success')
      invalidateDetail()
    },
    onError: () => toast('Failed to update content', 'error'),
  })

  const approve = useMutation({
    mutationFn: () =>
      publicationsApi
        .projectsProjectIdPublicationsIdApprovePost(publicationId!, selectedProjectId!)
        .then(r => r.data),
    onSuccess: () => {
      toast('Publication approved', 'success')
      invalidateDetail()
    },
    onError: () => toast('Failed to approve publication', 'error'),
  })

  const reject = useMutation({
    mutationFn: (reason: string) =>
      publicationsApi
        .projectsProjectIdPublicationsIdRejectPost(publicationId!, selectedProjectId!, { reason })
        .then(r => r.data),
    onSuccess: () => {
      toast('Publication rejected', 'success')
      invalidateDetail()
    },
    onError: () => toast('Failed to reject publication', 'error'),
  })

  const regenerate = useMutation({
    mutationFn: (feedback: string) =>
      publicationsApi
        .projectsProjectIdPublicationsIdRegeneratePost(publicationId!, selectedProjectId!, { feedback })
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
    mutationFn: (file: File) =>
      publicationsApi
        .projectsProjectIdPublicationsIdMediaPost(publicationId!, selectedProjectId!, file)
        .then(r => r.data),
    onSuccess: () => {
      toast('Media uploaded', 'success')
      invalidateDetail()
    },
    onError: () => toast('Failed to upload media', 'error'),
  })

  const deleteMedia = useMutation({
    mutationFn: (mediaId: string) =>
      publicationsApi
        .projectsProjectIdPublicationsIdMediaMediaIdDelete(publicationId!, mediaId, selectedProjectId!),
    onSuccess: () => {
      toast('Media deleted', 'success')
      invalidateDetail()
    },
    onError: () => toast('Failed to delete media', 'error'),
  })

  return { generateContent, updateContent, approve, reject, regenerate, uploadMedia, deleteMedia }
}
