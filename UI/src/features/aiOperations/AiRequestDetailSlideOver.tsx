import { Link } from 'react-router-dom'
import { SlideOver } from '@/components/shared/SlideOver'
import { useAiRequestDetail } from './useAiRequestDetail'
import type { AiOpsRequestRow } from './types'

interface Props {
  isOpen: boolean
  requestId: string | null
  onClose: () => void
}

interface FieldRowProps {
  label: string
  value: React.ReactNode
  labelColor?: string
}

function FieldRow({ label, value, labelColor }: FieldRowProps) {
  return (
    <div className="mb-4">
      <p
        className="font-caps text-xs tracking-widest mb-1"
        style={{ color: labelColor ?? 'var(--caramel)' }}
      >
        {label}
      </p>
      <div className="font-mono text-sm text-gray-200">{value}</div>
    </div>
  )
}

function CopyButton({ text }: { text: string }) {
  return (
    <button
      onClick={() => navigator.clipboard.writeText(text)}
      className="ml-2 font-caps text-xs tracking-wider px-2 py-0.5 transition-colors"
      style={{ color: 'var(--caramel)', border: '1px solid var(--caramel)' }}
    >
      COPY ID
    </button>
  )
}

function DetailContent({ row }: { row: AiOpsRequestRow }) {
  return (
    <div className="px-6 py-5">
      <FieldRow
        label="ID"
        value={
          <span className="flex items-center gap-1">
            <span className="break-all">{row.id}</span>
            <CopyButton text={row.id} />
          </span>
        }
      />
      <FieldRow label="TIMESTAMP" value={row.timestamp} />
      <FieldRow label="WORKER" value={row.worker || '—'} />
      <FieldRow label="PROVIDER" value={row.provider || '—'} />
      <FieldRow label="OPERATION" value={row.operation || '—'} />
      <FieldRow label="MODEL" value={row.model || '—'} />
      <FieldRow label="INPUT TOKENS" value={row.inputTokens.toLocaleString()} />
      <FieldRow label="OUTPUT TOKENS" value={row.outputTokens.toLocaleString()} />
      <FieldRow label="CACHE CREATION TOKENS" value={row.cacheCreationInputTokens.toLocaleString()} />
      <FieldRow label="CACHE READ TOKENS" value={row.cacheReadInputTokens.toLocaleString()} />
      <FieldRow label="TOTAL TOKENS" value={row.totalTokens.toLocaleString()} />
      <FieldRow label="LATENCY" value={`${row.latencyMs} ms`} />
      <FieldRow label="COST" value={`$${row.costUsd.toFixed(8)}`} />
      <FieldRow label="STATUS" value={row.status} />
      <FieldRow
        label="ARTICLE ID"
        value={
          row.articleId ? (
            <Link
              to={`/articles/${row.articleId}`}
              className="underline"
              style={{ color: 'var(--caramel)' }}
            >
              {row.articleId}
            </Link>
          ) : (
            '—'
          )
        }
      />
      <FieldRow label="CORRELATION ID" value={row.correlationId || '—'} />

      {row.status === 'Error' && (
        <div className="mt-4 pt-4" style={{ borderTop: '1px solid rgba(255,255,255,0.1)' }}>
          <p className="font-caps text-xs tracking-widest mb-2" style={{ color: 'var(--crimson)' }}>
            ERROR MESSAGE
          </p>
          <p className="font-mono text-xs text-red-300 whitespace-pre-wrap break-words">
            {row.errorMessage || '—'}
          </p>
        </div>
      )}
    </div>
  )
}

export function AiRequestDetailSlideOver({ isOpen, requestId, onClose }: Props) {
  const { data, isLoading, isError } = useAiRequestDetail(requestId)

  return (
    <SlideOver isOpen={isOpen} onClose={onClose} title="REQUEST DETAIL">
      {isLoading && (
        <div className="px-6 py-5 space-y-4">
          {Array.from({ length: 8 }, (_, i) => (
            <div
              key={i}
              className="animate-pulse h-6 rounded"
              style={{ background: 'rgba(61,15,15,0.6)' }}
            />
          ))}
        </div>
      )}
      {isError && (
        <div className="px-6 py-5 font-mono text-sm" style={{ color: 'var(--crimson)' }}>
          Failed to load request detail.
        </div>
      )}
      {data && !isLoading && <DetailContent row={data} />}
    </SlideOver>
  )
}
