import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useAiRequestMetrics } from './useAiRequestMetrics'
import { useAiRequestList, DEFAULT_PAGE_SIZE } from './useAiRequestList'
import { AiOpsFilterBar } from './AiOpsFilterBar'
import { AiOpsKpiStrip } from './AiOpsKpiStrip'
import { AiOpsCostTimeChart } from './AiOpsCostTimeChart'
import { AiOpsBreakdownPanel } from './AiOpsBreakdownPanel'
import { AiRequestsTable } from './AiRequestsTable'
import { AiRequestDetailSlideOver } from './AiRequestDetailSlideOver'
import type { AiOpsFilters } from './types'
import { todayIso, daysAgoIso } from './dateUtils'

const DEFAULT_RANGE_DAYS = 7

const defaultFilters: AiOpsFilters = {
  from: daysAgoIso(DEFAULT_RANGE_DAYS),
  to: todayIso(),
  provider: '',
  worker: '',
  model: '',
  status: '',
  search: '',
}

function pickMetricsFilters(f: AiOpsFilters) {
  return { from: f.from, to: f.to, provider: f.provider, worker: f.worker, model: f.model }
}

export function AiOperationsPage() {
  const queryClient = useQueryClient()

  const [filters, setFilters] = useState<AiOpsFilters>(defaultFilters)
  const [page, setPage] = useState(1)
  const [pageSize] = useState(DEFAULT_PAGE_SIZE)
  const [detailId, setDetailId] = useState<string | null>(null)

  const metrics = useAiRequestMetrics(pickMetricsFilters(filters))
  const list = useAiRequestList(page, pageSize, filters)

  const handleFilterChange = (patch: Partial<AiOpsFilters>) => {
    setFilters(prev => ({ ...prev, ...patch }))
    setPage(1)
  }

  const handleRefresh = () => {
    queryClient.invalidateQueries({ queryKey: ['ai-ops'] })
  }

  const workerOptions = metrics.data?.byWorker.map(r => r.key).filter(Boolean) as string[] ?? []
  const modelOptions = metrics.data?.byModel.map(r => r.key).filter(Boolean) as string[] ?? []

  const subtitleText = metrics.isLoading
    ? 'Loading…'
    : `${metrics.data?.kpis.totalCalls.toLocaleString() ?? 0} calls · $${(metrics.data?.kpis.totalCostUsd ?? 0).toFixed(2)} total cost`

  return (
    <div className="p-8">
      <div className="mb-8">
        <h1 className="font-display text-5xl text-white mb-2">AI Operations</h1>
        <p className="font-mono text-sm text-gray-400">{subtitleText}</p>
      </div>

      <AiOpsFilterBar
        filters={filters}
        workerOptions={workerOptions}
        modelOptions={modelOptions}
        onChange={handleFilterChange}
        onRefresh={handleRefresh}
      />

      <div className="mb-6">
        <AiOpsKpiStrip kpis={metrics.data?.kpis} isLoading={metrics.isLoading} />
      </div>

      <AiOpsCostTimeChart
        series={metrics.data?.timeSeries ?? []}
        isLoading={metrics.isLoading}
        isError={metrics.isError}
      />

      <div className="grid grid-cols-2 gap-4 mb-6">
        <AiOpsBreakdownPanel
          title="COST BY MODEL"
          rows={metrics.data?.byModel ?? []}
          isLoading={metrics.isLoading}
        />
        <AiOpsBreakdownPanel
          title="COST BY WORKER"
          rows={metrics.data?.byWorker ?? []}
          isLoading={metrics.isLoading}
        />
      </div>

      <AiRequestsTable
        page={page}
        data={list.data}
        isLoading={list.isLoading}
        isError={list.isError}
        onPageChange={p => setPage(p)}
        onRowClick={id => setDetailId(id)}
      />

      <AiRequestDetailSlideOver
        isOpen={!!detailId}
        requestId={detailId}
        onClose={() => setDetailId(null)}
      />
    </div>
  )
}
