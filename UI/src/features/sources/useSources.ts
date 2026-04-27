import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import { SourcesApi } from '@/api/generated'

const sourcesApi = new SourcesApi(undefined, '', apiClient)

export function useSources() {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'sources'],
    enabled: !!selectedProjectId,
    queryFn: async () => {
      const res = await sourcesApi.projectsProjectIdSourcesGet(selectedProjectId!)
      return res.data
    },
  })
}
