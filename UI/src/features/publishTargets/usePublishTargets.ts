import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import { PublishTargetsApi } from '@/api/generated'

const publishTargetsApi = new PublishTargetsApi(undefined, '', apiClient)

export function usePublishTargets() {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'publishTargets'],
    enabled: !!selectedProjectId,
    queryFn: () =>
      publishTargetsApi.projectsProjectIdPublishTargetsGet(selectedProjectId!).then(r => r.data),
  })
}

export function useActivePublishTargets() {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'publishTargets', 'active'],
    enabled: !!selectedProjectId,
    queryFn: () =>
      publishTargetsApi.projectsProjectIdPublishTargetsActiveGet(selectedProjectId!).then(r => r.data),
  })
}
