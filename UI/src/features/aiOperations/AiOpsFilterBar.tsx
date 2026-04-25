import { Search } from 'lucide-react'
import type { AiOpsFilters } from './types'
import { todayIso, daysAgoIso } from './dateUtils'

interface Props {
  filters: AiOpsFilters
  workerOptions: string[]
  modelOptions: string[]
  onChange: (patch: Partial<AiOpsFilters>) => void
  onRefresh: () => void
}

const RANGE_24H = 1
const RANGE_7D = 7
const RANGE_30D = 30
const RANGE_90D = 90

const selectStyle: React.CSSProperties = {
  background: 'var(--near-black)',
  border: '1px solid rgba(255,255,255,0.1)',
}

const inputBaseClass = 'px-3 py-2.5 font-mono text-sm text-gray-300 focus:outline-none transition-colors'

export function AiOpsFilterBar({ filters, workerOptions, modelOptions, onChange, onRefresh }: Props) {
  const handleQuickRange = (days: number) => {
    onChange({ from: daysAgoIso(days), to: todayIso() })
  }

  return (
    <div className="flex flex-wrap gap-3 mb-6 items-center">
      <input
        type="date"
        value={filters.from}
        onChange={e => onChange({ from: e.target.value })}
        className={inputBaseClass}
        style={selectStyle}
        onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
        onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
      />
      <input
        type="date"
        value={filters.to}
        onChange={e => onChange({ to: e.target.value })}
        className={inputBaseClass}
        style={selectStyle}
        onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
        onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
      />

      <div className="flex gap-1">
        {[
          { label: '24H', days: RANGE_24H },
          { label: '7D', days: RANGE_7D },
          { label: '30D', days: RANGE_30D },
          { label: '90D', days: RANGE_90D },
        ].map(({ label, days }) => (
          <button
            key={label}
            onClick={() => handleQuickRange(days)}
            className="px-2 py-1.5 font-caps text-xs tracking-wider transition-colors"
            style={{
              background: 'var(--near-black)',
              border: '1px solid rgba(255,255,255,0.1)',
              color: '#9ca3af',
            }}
            onMouseEnter={e => (e.currentTarget.style.color = 'var(--caramel)')}
            onMouseLeave={e => (e.currentTarget.style.color = '#9ca3af')}
          >
            {label}
          </button>
        ))}
      </div>

      <select
        value={filters.provider}
        onChange={e => onChange({ provider: e.target.value as AiOpsFilters['provider'] })}
        className={inputBaseClass}
        style={selectStyle}
        onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
        onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
      >
        <option value="">All Providers</option>
        <option value="Anthropic">Anthropic</option>
        <option value="Gemini">Gemini</option>
      </select>

      <select
        value={filters.worker}
        onChange={e => onChange({ worker: e.target.value })}
        className={inputBaseClass}
        style={selectStyle}
        onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
        onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
      >
        <option value="">All Workers</option>
        {workerOptions.map(w => (
          <option key={w} value={w}>{w}</option>
        ))}
      </select>

      <select
        value={filters.model}
        onChange={e => onChange({ model: e.target.value })}
        className={inputBaseClass}
        style={selectStyle}
        onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
        onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
      >
        <option value="">All Models</option>
        {modelOptions.map(m => (
          <option key={m} value={m}>{m}</option>
        ))}
      </select>

      <select
        value={filters.status}
        onChange={e => onChange({ status: e.target.value as AiOpsFilters['status'] })}
        className={inputBaseClass}
        style={selectStyle}
        onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
        onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
      >
        <option value="">All Statuses</option>
        <option value="Success">Success</option>
        <option value="Error">Error</option>
      </select>

      <div className="relative flex-1 min-w-48">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500 pointer-events-none" />
        <input
          type="text"
          placeholder="Search..."
          value={filters.search}
          onChange={e => onChange({ search: e.target.value })}
          maxLength={200}
          className="w-full pl-9 pr-3 py-2.5 font-mono text-sm text-gray-300 placeholder-gray-600 focus:outline-none transition-colors"
          style={selectStyle}
          onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
          onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
        />
      </div>

      <button
        onClick={onRefresh}
        className="px-4 py-2.5 font-caps text-xs tracking-wider text-white transition-colors"
        style={{ background: 'var(--crimson)' }}
        onMouseEnter={e => (e.currentTarget.style.background = 'rgba(139,26,26,0.8)')}
        onMouseLeave={e => (e.currentTarget.style.background = 'var(--crimson)')}
      >
        REFRESH
      </button>
    </div>
  )
}
