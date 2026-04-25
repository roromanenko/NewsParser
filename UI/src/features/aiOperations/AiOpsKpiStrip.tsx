import type { AiOpsKpis } from './types'

interface Props {
  kpis: AiOpsKpis | undefined
  isLoading: boolean
}

interface StatCardProps {
  label: string
  value: string
  valueColor?: string
  subtitle?: string
}

const LOW_SUCCESS_THRESHOLD = 0.95

const costFormatter = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })
const intFormatter = new Intl.NumberFormat('en-US')

function formatPercent(rate: number): string {
  return `${(rate * 100).toFixed(1)}%`
}

function StatCard({ label, value, valueColor, subtitle }: StatCardProps) {
  return (
    <div
      className="p-4"
      style={{ background: 'rgba(61,15,15,0.4)', border: '1px solid rgba(255,255,255,0.1)' }}
    >
      <p className="font-caps text-xs tracking-widest mb-2" style={{ color: 'var(--caramel)' }}>
        {label}
      </p>
      <p
        className="font-display text-3xl text-white"
        style={valueColor ? { color: valueColor } : undefined}
      >
        {value}
      </p>
      {subtitle && (
        <p className="font-mono text-xs text-gray-500 mt-1">{subtitle}</p>
      )}
    </div>
  )
}

export function AiOpsKpiStrip({ kpis, isLoading }: Props) {
  const dash = '—'

  const totalCost = isLoading || !kpis ? dash : costFormatter.format(kpis.totalCostUsd)
  const totalCalls = isLoading || !kpis ? dash : intFormatter.format(kpis.totalCalls)
  const successRate = isLoading || !kpis ? dash : formatPercent(kpis.successRate)
  const successRateColor = !isLoading && kpis && kpis.successRate < LOW_SUCCESS_THRESHOLD
    ? 'var(--crimson)'
    : undefined
  const avgLatency = isLoading || !kpis ? dash : `${intFormatter.format(Math.round(kpis.averageLatencyMs))} ms`
  const totalTokens = isLoading || !kpis ? dash : intFormatter.format(kpis.totalTokens)
  const cacheHit = isLoading || !kpis ? dash : formatPercent(kpis.cacheHitRate)

  return (
    <div className="grid grid-cols-2 sm:grid-cols-6 gap-4">
      <StatCard label="TOTAL COST" value={totalCost} />
      <StatCard label="TOTAL CALLS" value={totalCalls} />
      <StatCard label="SUCCESS RATE" value={successRate} valueColor={successRateColor} />
      <StatCard label="AVG LATENCY" value={avgLatency} />
      <StatCard label="TOTAL TOKENS" value={totalTokens} />
      <StatCard label="CACHE HIT" value={cacheHit} subtitle="Anthropic only" />
    </div>
  )
}
