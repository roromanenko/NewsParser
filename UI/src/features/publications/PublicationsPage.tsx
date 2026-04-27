import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useAllPublications } from './useAllPublications'
import { Pagination } from '@/components/shared/Pagination'
import type { PublicationListItemDto } from './types'

const PAGE_SIZE = 20

const STATUS_COLORS: Record<string, string> = {
  ContentReady: 'var(--caramel)',
  Approved: '#22c55e',
  Published: '#3b82f6',
  Rejected: 'var(--rust)',
  Failed: 'var(--crimson)',
  Created: '#9ca3af',
  GenerationInProgress: '#9ca3af',
}

function statusColor(status?: string | null): string {
  return STATUS_COLORS[status ?? ''] ?? '#9ca3af'
}

function formatDate(iso?: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

const ALL_STATUSES = ['ContentReady', 'Approved', 'Published', 'Rejected', 'Failed', 'Created', 'GenerationInProgress']

export function PublicationsPage() {
  const [page, setPage] = useState(1)
  const [filterStatus, setFilterStatus] = useState<string>('all')
  const { data, isLoading } = useAllPublications(page, PAGE_SIZE)

  const items = data?.items ?? []
  const filtered = filterStatus === 'all'
    ? items
    : items.filter(p => p.status === filterStatus)

  return (
    <div className="flex -m-6" style={{ minHeight: 'calc(100vh - 5rem)' }}>
      {/* Left panel – filters */}
      <aside
        className="w-56 shrink-0 border-r p-6"
        style={{ borderColor: 'rgba(255,255,255,0.1)', background: 'rgba(61,15,15,0.3)' }}
      >
        <div className="font-caps text-xs tracking-widest mb-3" style={{ color: 'var(--caramel)' }}>
          STATUS
        </div>
        <div className="space-y-1">
          {['all', ...ALL_STATUSES].map(s => (
            <button
              key={s}
              onClick={() => { setFilterStatus(s); setPage(1) }}
              className="w-full text-left px-3 py-2 font-mono text-xs transition-colors"
              style={{
                background: filterStatus === s ? 'var(--burgundy)' : 'transparent',
                color: filterStatus === s ? '#E8E8E8' : '#9ca3af',
              }}
              onMouseEnter={e => {
                if (filterStatus !== s) e.currentTarget.style.color = 'var(--caramel)'
              }}
              onMouseLeave={e => {
                if (filterStatus !== s) e.currentTarget.style.color = '#9ca3af'
              }}
            >
              {s === 'all' ? 'ALL' : s.replace(/([A-Z])/g, ' $1').trim().toUpperCase()}
            </button>
          ))}
        </div>

        <div className="mt-8 pt-6 border-t" style={{ borderColor: 'rgba(255,255,255,0.1)' }}>
          <div className="font-caps text-xs tracking-widest mb-3" style={{ color: 'var(--caramel)' }}>
            TOTAL
          </div>
          <div className="font-mono text-sm" style={{ color: '#9ca3af' }}>
            {data?.totalCount ?? 0} publications
          </div>
        </div>
      </aside>

      {/* Center panel */}
      <div className="flex-1 overflow-y-auto">
        <div className="p-8">
          <div
            className="mb-6 flex items-center justify-between border-b pb-4"
            style={{ borderColor: 'rgba(255,255,255,0.1)' }}
          >
            <div>
              <h1 className="font-display text-4xl mb-1">Publications</h1>
              <p className="font-mono text-sm" style={{ color: '#9ca3af' }}>
                {filtered.length} items
              </p>
            </div>
          </div>

          {isLoading ? (
            <div className="space-y-3">
              {Array.from({ length: 8 }).map((_, i) => (
                <div
                  key={i}
                  className="h-20 animate-pulse"
                  style={{ background: 'rgba(61,15,15,0.4)', border: '1px solid rgba(255,255,255,0.1)' }}
                />
              ))}
            </div>
          ) : filtered.length === 0 ? (
            <div className="font-mono text-sm text-center py-16" style={{ color: '#9ca3af' }}>
              No publications found.
            </div>
          ) : (
            <div className="space-y-3">
              {filtered.map(pub => (
                <PublicationRow key={pub.id} pub={pub} />
              ))}
            </div>
          )}

          {data && (
            <div className="mt-8">
              <Pagination
                page={page}
                totalPages={data.totalPages ?? 1}
                hasNextPage={data.hasNextPage ?? false}
                hasPreviousPage={data.hasPreviousPage ?? false}
                onPageChange={setPage}
              />
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

function PublicationRow({ pub }: { pub: PublicationListItemDto }) {
  const color = statusColor(pub.status)

  return (
    <Link
      to={`/publications/${pub.id}`}
      className="relative flex items-center gap-4 border p-5 transition-colors block"
      style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
      onMouseEnter={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
      onMouseLeave={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
    >
      {/* Status bar */}
      <div className="absolute left-0 top-0 bottom-0 w-1" style={{ backgroundColor: color }} />

      {/* Content */}
      <div className="flex-1 min-w-0 pl-2">
        <p className="font-display text-lg truncate" style={{ color: '#E8E8E8' }}>
          {pub.eventTitle ?? '—'}
        </p>
        <p className="font-mono text-xs mt-1" style={{ color: '#6b7280' }}>
          {pub.targetName} · {pub.platform} · {formatDate(pub.createdAt)}
        </p>
      </div>

      {/* Status + published date */}
      <div className="flex flex-col items-end gap-1 shrink-0">
        <span className="font-caps text-xs tracking-widest" style={{ color }}>
          {(pub.status ?? '').replace(/([A-Z])/g, ' $1').trim().toUpperCase()}
        </span>
        {pub.publishedAt && (
          <span className="font-mono text-[10px]" style={{ color: '#6b7280' }}>
            published {formatDate(pub.publishedAt)}
          </span>
        )}
      </div>
    </Link>
  )
}
