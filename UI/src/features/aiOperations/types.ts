export interface AiOpsFilters {
  from: string
  to: string
  provider: '' | 'Anthropic' | 'Gemini'
  worker: string
  model: string
  status: '' | 'Success' | 'Error'
  search: string
}

export interface AiOpsKpis {
  totalCostUsd: number
  totalCalls: number
  successCalls: number
  errorCalls: number
  successRate: number
  averageLatencyMs: number
  totalTokens: number
  cacheHitRate: number
}

export interface AiOpsTimeBucket {
  bucket: string
  anthropicCost: number
  geminiCost: number
  anthropicTokens: number
  geminiTokens: number
  anthropicCalls: number
  geminiCalls: number
}

export interface AiOpsBreakdownRow {
  key: string
  calls: number
  costUsd: number
  tokens: number
}

export interface AiOpsRequestRow {
  id: string
  timestamp: string
  worker: string
  provider: string
  operation: string
  model: string
  inputTokens: number
  outputTokens: number
  cacheCreationInputTokens: number
  cacheReadInputTokens: number
  totalTokens: number
  costUsd: number
  latencyMs: number
  status: 'Success' | 'Error'
  errorMessage: string | null
  correlationId: string
  articleId: string | null
}

export interface AiOpsMetricsView {
  kpis: AiOpsKpis
  timeSeries: AiOpsTimeBucket[]
  byModel: AiOpsBreakdownRow[]
  byWorker: AiOpsBreakdownRow[]
  byProvider: AiOpsBreakdownRow[]
}

export interface AiOpsRequestPage {
  items: AiOpsRequestRow[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasNextPage: boolean
  hasPreviousPage: boolean
}
