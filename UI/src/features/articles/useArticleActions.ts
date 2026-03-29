import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ArticlesApi } from '@/api/generated'
import { apiClient } from '@/lib/axios'
import { useToast } from '@/context/ToastContext'

const articlesApi = new ArticlesApi(undefined, '', apiClient)

export function useArticleActions(articleId: string) {
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const approveMutation = useMutation({
    mutationFn: (publishTargetIds: string[]) =>
      articlesApi.articlesIdApprovePost(articleId, { publishTargetIds }),
    onSuccess: () => {
      toast('Article approved successfully', 'success')
      queryClient.invalidateQueries({ queryKey: ['article', articleId] })
      queryClient.invalidateQueries({ queryKey: ['articles'] })
    },
    onError: () => {
      toast('Failed to approve article', 'error')
    },
  })

  const rejectMutation = useMutation({
    mutationFn: (reason: string) =>
      articlesApi.articlesIdRejectPost(articleId, { reason }),
    onSuccess: () => {
      toast('Article rejected', 'success')
      queryClient.invalidateQueries({ queryKey: ['article', articleId] })
      queryClient.invalidateQueries({ queryKey: ['articles'] })
    },
    onError: () => {
      toast('Failed to reject article', 'error')
    },
  })

  return { approveMutation, rejectMutation }
}
