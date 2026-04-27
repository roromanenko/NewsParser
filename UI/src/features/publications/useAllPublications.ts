import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import { PublicationsApi } from '@/api/generated'

const publicationsApi = new PublicationsApi(undefined, '', apiClient)

export function useAllPublications(page: number, pageSize: number) {
  const { selectedProjectId } = useProjectStore()

  const { data, isLoading, error } = useQuery({
    queryKey: ['project', selectedProjectId, 'publications', 'all', page, pageSize],
    enabled: !!selectedProjectId,
    queryFn: () =>
      publicationsApi
        .projectsProjectIdPublicationsGet(selectedProjectId!, page, pageSize)
        .then(r => r.data),
  })

  return { data, isLoading, error }
}
