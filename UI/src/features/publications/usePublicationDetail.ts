import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import type { PublicationDetailDto } from './types'

export function usePublicationDetail(id: string) {
  const { selectedProjectId } = useProjectStore()

  const { data, isLoading, error } = useQuery({
    queryKey: ['project', selectedProjectId, 'publication', id],
    enabled: !!id && !!selectedProjectId,
    queryFn: () =>
      apiClient
        .get<PublicationDetailDto>(`/projects/${selectedProjectId}/publications/${id}`)
        .then(r => r.data),
    refetchInterval: (query) => {
      const status = query.state.data?.status
      return status === 'Created' || status === 'GenerationInProgress' ? 3000 : false
    },
  })

  return { publication: data, isLoading, error }
}
