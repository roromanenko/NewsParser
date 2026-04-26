import { useQuery } from '@tanstack/react-query'
import type { UseQueryResult } from '@tanstack/react-query'
import { useProjectStore } from '@/store/projectStore'
import { apiClient } from '@/lib/axios'
import type { AiOpsMetricsView, AiOpsTimeBucket, AiOpsBreakdownRow, AiOpsKpis, AiOpsFilters } from './types'
import type { AiMetricsTimeBucketDto, AiMetricsBreakdownRowDto, AiOperationsMetricsDto } from '@/api/generated'

const STALE_MS = 30_000

type MetricsFilters = Pick<AiOpsFilters, 'from' | 'to' | 'provider' | 'worker' | 'model'>

function mapKpis(dto: AiOperationsMetricsDto): AiOpsKpis {
  const actualTotalCalls = dto.totalCalls ?? 0
  const successCalls = dto.successCalls ?? 0
  const cacheRead = dto.totalCacheReadInputTokens ?? 0
  const cacheCreation = dto.totalCacheCreationInputTokens ?? 0
  const totalInput = dto.totalInputTokens ?? 0
  const nonCached = totalInput - cacheRead - cacheCreation
  const cacheDenominator = cacheRead + cacheCreation + (nonCached > 0 ? nonCached : 0)

  return {
    totalCostUsd: dto.totalCostUsd ?? 0,
    totalCalls: actualTotalCalls,
    successCalls,
    errorCalls: dto.errorCalls ?? 0,
    successRate: actualTotalCalls > 0 ? successCalls / actualTotalCalls : 0,
    averageLatencyMs: dto.averageLatencyMs ?? 0,
    totalTokens: (dto.totalInputTokens ?? 0) + (dto.totalOutputTokens ?? 0),
    cacheHitRate: cacheDenominator > 0 ? cacheRead / cacheDenominator : 0,
  }
}

function mapTimeSeries(rows: AiMetricsTimeBucketDto[]): AiOpsTimeBucket[] {
  const byBucket = new Map<string, AiOpsTimeBucket>()

  for (const row of rows) {
    const bucket = row.bucket ?? ''
    if (!byBucket.has(bucket)) {
      byBucket.set(bucket, {
        bucket,
        anthropicCost: 0,
        geminiCost: 0,
        anthropicTokens: 0,
        geminiTokens: 0,
        anthropicCalls: 0,
        geminiCalls: 0,
      })
    }
    const entry = byBucket.get(bucket)!
    const provider = (row.provider ?? '').toLowerCase()
    if (provider === 'anthropic') {
      entry.anthropicCost += row.costUsd ?? 0
      entry.anthropicTokens += row.tokens ?? 0
      entry.anthropicCalls += row.calls ?? 0
    } else if (provider === 'gemini') {
      entry.geminiCost += row.costUsd ?? 0
      entry.geminiTokens += row.tokens ?? 0
      entry.geminiCalls += row.calls ?? 0
    }
  }

  return Array.from(byBucket.values())
}

function mapBreakdownRows(rows: AiMetricsBreakdownRowDto[]): AiOpsBreakdownRow[] {
  return rows.map(r => ({
    key: r.key ?? '',
    calls: r.calls ?? 0,
    costUsd: r.costUsd ?? 0,
    tokens: r.tokens ?? 0,
  }))
}

export function useAiRequestMetrics(
  filters: MetricsFilters,
): UseQueryResult<AiOpsMetricsView> {
  const { selectedProjectId } = useProjectStore()

  return useQuery({
    queryKey: ['project', selectedProjectId, 'ai-ops', 'metrics', filters],
    enabled: !!selectedProjectId,
    staleTime: STALE_MS,
    queryFn: async () => {
      const res = await apiClient.get(`/projects/${selectedProjectId}/ai-operations/metrics`, {
        params: {
          from: filters.from || undefined,
          to: filters.to || undefined,
          provider: filters.provider || undefined,
          worker: filters.worker || undefined,
          model: filters.model || undefined,
        },
      })
      const dto = res.data as AiOperationsMetricsDto

      return {
        kpis: mapKpis(dto),
        timeSeries: mapTimeSeries(dto.timeSeries ?? []),
        byModel: mapBreakdownRows(dto.byModel ?? []),
        byWorker: mapBreakdownRows(dto.byWorker ?? []),
        byProvider: mapBreakdownRows(dto.byProvider ?? []),
      } satisfies AiOpsMetricsView
    },
  })
}
