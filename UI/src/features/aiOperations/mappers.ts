import type { AiRequestLogDto } from '@/api/generated'
import type { AiOpsRequestRow } from './types'

export function mapRequestRow(dto: AiRequestLogDto): AiOpsRequestRow {
  const rawStatus = dto.status ?? 'Success'
  const status: 'Success' | 'Error' = rawStatus === 'Error' ? 'Error' : 'Success'

  return {
    id: dto.id ?? '',
    timestamp: dto.timestamp ?? '',
    worker: dto.worker ?? '',
    provider: dto.provider ?? '',
    operation: dto.operation ?? '',
    model: dto.model ?? '',
    inputTokens: dto.inputTokens ?? 0,
    outputTokens: dto.outputTokens ?? 0,
    cacheCreationInputTokens: dto.cacheCreationInputTokens ?? 0,
    cacheReadInputTokens: dto.cacheReadInputTokens ?? 0,
    totalTokens: dto.totalTokens ?? 0,
    costUsd: dto.costUsd ?? 0,
    latencyMs: dto.latencyMs ?? 0,
    status,
    errorMessage: dto.errorMessage ?? null,
    correlationId: dto.correlationId ?? '',
    articleId: dto.articleId ?? null,
  }
}
