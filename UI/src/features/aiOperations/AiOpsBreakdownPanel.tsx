import type { AiOpsBreakdownRow } from './types'

interface Props {
  title: string
  rows: AiOpsBreakdownRow[]
  isLoading: boolean
}

const TOP_N = 10
const MIN_MAX_COST = 1

function formatCost(value: number): string {
  return `$${value.toFixed(4)}`
}

function PlaceholderRow() {
  return (
    <div
      className="animate-pulse h-5 mb-2"
      style={{ background: 'rgba(61,15,15,0.6)' }}
    />
  )
}

export function AiOpsBreakdownPanel({ title, rows, isLoading }: Props) {
  const sorted = [...rows].sort((a, b) => b.costUsd - a.costUsd).slice(0, TOP_N)
  const maxCost = Math.max(sorted[0]?.costUsd ?? 0, MIN_MAX_COST)

  return (
    <div
      className="p-4"
      style={{ background: 'rgba(61,15,15,0.3)', border: '1px solid rgba(255,255,255,0.1)' }}
    >
      <p className="font-caps text-xs tracking-widest mb-3" style={{ color: 'var(--caramel)' }}>
        {title}
      </p>

      {isLoading ? (
        Array.from({ length: 5 }, (_, i) => <PlaceholderRow key={i} />)
      ) : sorted.length === 0 ? (
        <p className="font-mono text-sm text-gray-500 text-center py-4">No data.</p>
      ) : (
        sorted.map(row => (
          <div key={row.key} className="flex items-center gap-2 mb-2">
            <span className="font-mono text-sm text-gray-300 w-32 shrink-0 truncate">
              {row.key || '—'}
            </span>
            <div className="flex-1 h-2 relative" style={{ background: 'rgba(255,255,255,0.05)' }}>
              <div
                className="absolute inset-y-0 left-0"
                style={{
                  width: `${(row.costUsd / maxCost) * 100}%`,
                  backgroundColor: 'rgba(196,140,82,0.4)',
                }}
              />
            </div>
            <span className="font-mono text-xs text-gray-400 w-16 text-right shrink-0">
              {formatCost(row.costUsd)}
            </span>
          </div>
        ))
      )}
    </div>
  )
}
