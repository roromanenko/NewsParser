import type { SourceDto } from '@/api/generated'

interface Props {
  sources: SourceDto[]
}

interface StatCardProps {
  label: string
  value: number
  valueColor?: string
}

function StatCard({ label, value, valueColor }: StatCardProps) {
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
    </div>
  )
}

export function SourceStatsCards({ sources }: Props) {
  const total = sources.length
  const active = sources.filter(s => s.isActive).length
  const inactive = sources.filter(s => !s.isActive).length
  const rss = sources.filter(s => s.type === 'Rss').length

  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
      <StatCard label="TOTAL SOURCES" value={total} />
      <StatCard label="ACTIVE" value={active} />
      <StatCard label="INACTIVE" value={inactive} valueColor={inactive > 0 ? 'var(--crimson)' : undefined} />
      <StatCard label="RSS FEEDS" value={rss} />
    </div>
  )
}
