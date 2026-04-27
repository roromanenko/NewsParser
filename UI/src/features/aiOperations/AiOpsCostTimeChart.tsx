import { useState } from 'react'
import {
  ResponsiveContainer,
  LineChart,
  Line,
  XAxis,
  YAxis,
  Tooltip,
  Legend,
} from 'recharts'
import type { AiOpsTimeBucket } from './types'

interface Props {
  series: AiOpsTimeBucket[]
  isLoading: boolean
  isError: boolean
}

type MetricMode = 'COST' | 'TOKENS' | 'CALLS'

const CHART_HEIGHT = 288

const METRIC_MODES: MetricMode[] = ['COST', 'TOKENS', 'CALLS']

const ANTHROPIC_KEYS: Record<MetricMode, keyof AiOpsTimeBucket> = {
  COST: 'anthropicCost',
  TOKENS: 'anthropicTokens',
  CALLS: 'anthropicCalls',
}

const GEMINI_KEYS: Record<MetricMode, keyof AiOpsTimeBucket> = {
  COST: 'geminiCost',
  TOKENS: 'geminiTokens',
  CALLS: 'geminiCalls',
}

const dayFormatter = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric' })

function formatXAxis(value: string): string {
  const d = new Date(value)
  return isNaN(d.getTime()) ? value : dayFormatter.format(d)
}

function formatYAxis(value: number, mode: MetricMode): string {
  if (mode === 'COST') return `$${value.toFixed(2)}`
  return value.toLocaleString()
}

function formatTooltipValue(value: number, mode: MetricMode, label: string): [string, string] {
  const formatted = mode === 'COST' ? `$${Number(value).toFixed(2)}` : Number(value).toLocaleString()
  return [formatted, label]
}

export function AiOpsCostTimeChart({ series, isLoading, isError }: Props) {
  const [mode, setMode] = useState<MetricMode>('COST')

  const anthropicKey = ANTHROPIC_KEYS[mode]
  const geminiKey = GEMINI_KEYS[mode]

  return (
    <div
      className="p-4 mb-6"
      style={{ background: 'rgba(61,15,15,0.3)', border: '1px solid rgba(255,255,255,0.1)' }}
    >
      <div className="flex gap-1 mb-4">
        {METRIC_MODES.map(m => (
          <button
            key={m}
            onClick={() => setMode(m)}
            className="px-3 py-1 font-caps text-xs tracking-wider transition-colors"
            style={{
              background: mode === m ? 'var(--burgundy)' : 'transparent',
              color: mode === m ? '#E8E8E8' : '#9ca3af',
            }}
          >
            {m}
          </button>
        ))}
      </div>

      {isLoading ? (
        <div
          className="animate-pulse h-72"
          style={{ background: 'rgba(61,15,15,0.6)' }}
        />
      ) : isError ? (
        <div className="flex items-center justify-center h-72 font-mono text-sm" style={{ color: 'var(--crimson)' }}>
          Failed to load AI operations data.
        </div>
      ) : series.length === 0 ? (
        <div className="flex items-center justify-center h-72 font-mono text-sm text-gray-500">
          No data in selected range.
        </div>
      ) : (
        <ResponsiveContainer width="100%" height={CHART_HEIGHT}>
          <LineChart data={series}>
            <XAxis
              dataKey="bucket"
              tickFormatter={formatXAxis}
              tick={{ fill: '#9ca3af', fontSize: 11, fontFamily: 'monospace' }}
            />
            <YAxis
              tickFormatter={(v: number) => formatYAxis(v, mode)}
              tick={{ fill: '#9ca3af', fontSize: 11, fontFamily: 'monospace' }}
            />
            <Tooltip
              formatter={(value: number | string | ReadonlyArray<number | string> | undefined, name: string | number | undefined) =>
                formatTooltipValue(Number(value ?? 0), mode, String(name ?? '') === String(anthropicKey) ? 'Anthropic' : 'Gemini')
              }
              contentStyle={{ background: 'var(--burgundy)', border: '1px solid rgba(255,255,255,0.1)', fontFamily: 'monospace' }}
              labelStyle={{ color: '#9ca3af' }}
            />
            <Legend
              formatter={(value: string) => (value === String(anthropicKey) ? 'Anthropic' : 'Gemini')}
            />
            <Line
              type="monotone"
              dataKey={anthropicKey}
              stroke="var(--crimson)"
              dot={false}
              strokeWidth={2}
            />
            <Line
              type="monotone"
              dataKey={geminiKey}
              stroke="var(--caramel)"
              dot={false}
              strokeWidth={2}
            />
          </LineChart>
        </ResponsiveContainer>
      )}
    </div>
  )
}
