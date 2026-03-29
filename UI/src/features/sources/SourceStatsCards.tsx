import { Database, Wifi, Radio, CheckCircle } from 'lucide-react'
import type { SourceDto } from '@/api/generated'

interface Props {
  sources: SourceDto[]
}

interface StatCardProps {
  label: string
  value: number
  icon: React.ReactNode
  color: string
}

function StatCard({ label, value, icon, color }: StatCardProps) {
  return (
    <div className="bg-white rounded-lg border border-gray-200 px-5 py-4 flex items-center gap-4">
      <div className={`flex-shrink-0 w-10 h-10 rounded-lg flex items-center justify-center ${color}`}>
        {icon}
      </div>
      <div>
        <p className="text-2xl font-semibold text-gray-900">{value}</p>
        <p className="text-xs text-gray-500 mt-0.5">{label}</p>
      </div>
    </div>
  )
}

export function SourceStatsCards({ sources }: Props) {
  const total = sources.length
  const active = sources.filter(s => s.isActive).length
  const rss = sources.filter(s => s.type === 'Rss').length
  const telegram = sources.filter(s => s.type === 'Telegram').length

  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 mb-6">
      <StatCard
        label="Total Sources"
        value={total}
        icon={<Database className="w-5 h-5 text-indigo-600" />}
        color="bg-indigo-50"
      />
      <StatCard
        label="Active"
        value={active}
        icon={<CheckCircle className="w-5 h-5 text-green-600" />}
        color="bg-green-50"
      />
      <StatCard
        label="RSS Feeds"
        value={rss}
        icon={<Wifi className="w-5 h-5 text-blue-600" />}
        color="bg-blue-50"
      />
      <StatCard
        label="Telegram"
        value={telegram}
        icon={<Radio className="w-5 h-5 text-gray-600" />}
        color="bg-gray-100"
      />
    </div>
  )
}
