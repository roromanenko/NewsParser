import { Pagination } from '@/components/shared/Pagination'
import type { AiOpsRequestPage } from './types'

interface Props {
  page: number
  data: AiOpsRequestPage | undefined
  isLoading: boolean
  isError: boolean
  onPageChange: (p: number) => void
  onRowClick: (id: string) => void
}

const SKELETON_ROW_COUNT = 8

function formatTimeAgo(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime()
  const mins = Math.floor(diff / 60_000)
  if (mins < 60) return `${mins}m ago`
  const hrs = Math.floor(mins / 60)
  if (hrs < 24) return `${hrs}h ago`
  return `${Math.floor(hrs / 24)}d ago`
}

function formatCost(value: number): string {
  return `$${value.toFixed(6)}`
}

function SkeletonRows() {
  return (
    <>
      {Array.from({ length: SKELETON_ROW_COUNT }, (_, i) => (
        <div key={i} className="grid grid-cols-12 px-4 py-3 gap-2">
          {Array.from({ length: 10 }, (__, j) => (
            <div
              key={j}
              className="animate-pulse h-4 rounded"
              style={{ background: 'rgba(61,15,15,0.6)', gridColumn: j === 0 ? 'span 2' : 'span 1' }}
            />
          ))}
        </div>
      ))}
    </>
  )
}

export function AiRequestsTable({ page, data, isLoading, isError, onPageChange, onRowClick }: Props) {
  const items = data?.items ?? []

  return (
    <div className="mb-6">
      <div style={{ border: '1px solid rgba(255,255,255,0.1)' }}>
        <div
          className="grid grid-cols-12 px-4 py-3"
          style={{ background: 'var(--burgundy)', borderBottom: '1px solid rgba(255,255,255,0.1)' }}
        >
          <div className="col-span-2 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>TIME</div>
          <div className="col-span-1 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>PROVIDER</div>
          <div className="col-span-2 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>WORKER</div>
          <div className="col-span-1 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>OPERATION</div>
          <div className="col-span-2 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>MODEL</div>
          <div className="col-span-1 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>TOKENS</div>
          <div className="col-span-1 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>LATENCY</div>
          <div className="col-span-1 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>COST</div>
          <div className="col-span-1 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>STATUS</div>
        </div>

        {isLoading ? (
          <SkeletonRows />
        ) : isError ? (
          <div className="px-4 py-8 text-center font-mono text-sm" style={{ color: 'var(--crimson)' }}>
            Failed to load AI operations data.
          </div>
        ) : items.length === 0 ? (
          <div className="px-4 py-8 text-center font-mono text-sm text-gray-500">
            No requests match your filters.
          </div>
        ) : (
          items.map((row, i) => (
            <div
              key={row.id}
              className="grid grid-cols-12 px-4 py-3 items-center cursor-pointer transition-colors"
              style={{ borderBottom: i < items.length - 1 ? '1px solid rgba(255,255,255,0.06)' : undefined }}
              onClick={() => onRowClick(row.id)}
              onMouseEnter={e => (e.currentTarget.style.background = 'rgba(61,15,15,0.3)')}
              onMouseLeave={e => (e.currentTarget.style.background = '')}
            >
              <div className="col-span-2 font-mono text-sm text-gray-300">
                {row.timestamp ? formatTimeAgo(row.timestamp) : '—'}
              </div>
              <div className="col-span-1 font-mono text-sm text-gray-300 truncate">
                {row.provider || '—'}
              </div>
              <div className="col-span-2 font-mono text-sm text-gray-300 truncate">
                {row.worker || '—'}
              </div>
              <div className="col-span-1 font-mono text-sm text-gray-300 truncate">
                {row.operation || '—'}
              </div>
              <div className="col-span-2 font-mono text-sm text-gray-300 truncate">
                {row.model || '—'}
              </div>
              <div className="col-span-1 font-mono text-sm text-gray-300">
                {row.totalTokens.toLocaleString()}
              </div>
              <div className="col-span-1 font-mono text-sm text-gray-300">
                {row.latencyMs}ms
              </div>
              <div className="col-span-1 font-mono text-xs text-gray-400">
                {formatCost(row.costUsd)}
              </div>
              <div className="col-span-1">
                <span
                  className="inline-block px-1.5 py-0.5 font-caps text-xs border"
                  style={{
                    borderColor: row.status === 'Error' ? 'var(--crimson)' : 'var(--caramel)',
                    color: row.status === 'Error' ? 'var(--crimson)' : 'var(--caramel)',
                  }}
                >
                  {row.status}
                </span>
              </div>
            </div>
          ))
        )}
      </div>

      {data && data.totalPages > 1 && (
        <Pagination
          page={page}
          totalPages={data.totalPages}
          hasNextPage={data.hasNextPage}
          hasPreviousPage={data.hasPreviousPage}
          onPageChange={onPageChange}
        />
      )}
    </div>
  )
}
