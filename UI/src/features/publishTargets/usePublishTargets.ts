import { useQuery } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'

export function usePublishTargets() {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'publishTargets'],
    enabled: !!selectedProjectId,
    queryFn: () =>
      apiClient.get(`/projects/${selectedProjectId}/publish-targets`).then(r => r.data),
  })
}

export function useActivePublishTargets() {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'publishTargets', 'active'],
    enabled: !!selectedProjectId,
    queryFn: () =>
      apiClient.get(`/projects/${selectedProjectId}/publish-targets/active`).then(r => r.data),
  })
}
