import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useEvents } from './useEvents'
import { MergeEventsSlideOver } from './MergeEventsSlideOver'
import { Pagination } from '@/components/shared/Pagination'
import { usePermissions } from '@/hooks/usePermissions'
import type { EventListItemDto } from '@/api/generated'

const PAGE_SIZE = 20

function formatDate(iso?: string) {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

function statusColor(status?: string | null): string {
  if (status === 'Active') return 'var(--crimson)'
  if (status === 'Approved') return 'var(--caramel)'
  if (status === 'Rejected') return 'var(--rust)'
  if (status === 'Archived') return '#4b5563'
  return '#6b7280'
}

export function EventsPage() {
  const navigate = useNavigate()
  const { isAdmin } = usePermissions()
  const [page, setPage] = useState(1)
  const [mergeOpen, setMergeOpen] = useState(false)

  const { data, isLoading } = useEvents(page, PAGE_SIZE)
  const events = data?.items ?? []

  return (
    <div className="flex -m-6" style={{ minHeight: 'calc(100vh - 5rem)' }}>
      <div className="flex-1 overflow-y-auto">
        <div className="p-8">
          {/* Header */}
          <div
            className="mb-6 flex items-center justify-between border-b pb-4"
            style={{ borderColor: 'rgba(255,255,255,0.1)' }}
          >
            <div>
              <h1 className="font-display text-4xl mb-1">Events</h1>
              <p className="font-mono text-sm" style={{ color: '#9ca3af' }}>
                {data?.totalCount ?? 0} events total
              </p>
            </div>
            {isAdmin && (
              <button
                onClick={() => setMergeOpen(true)}
                className="px-4 py-2 font-caps text-xs tracking-wider text-white transition-opacity hover:opacity-90"
                style={{ background: 'var(--crimson)' }}
              >
                MERGE EVENTS
              </button>
            )}
          </div>

          {/* Events list */}
          {isLoading ? (
            <div className="space-y-4">
              {Array.from({ length: 5 }).map((_, i) => (
                <div
                  key={i}
                  className="h-32 animate-pulse"
                  style={{ background: 'rgba(61,15,15,0.4)', border: '1px solid rgba(255,255,255,0.1)' }}
                />
              ))}
            </div>
          ) : events.length === 0 ? (
            <div className="font-mono text-sm text-center py-16" style={{ color: '#9ca3af' }}>
              No events found.
            </div>
          ) : (
            <div className="space-y-4">
              {events.map(event => (
                <EventCard
                  key={event.id}
                  event={event}
                  onClick={() => event.id && navigate(`/events/${event.id}`)}
                />
              ))}
            </div>
          )}

          {(data?.totalPages ?? 1) > 1 && (
            <div className="mt-8">
              <Pagination
                page={page}
                totalPages={data?.totalPages ?? 1}
                hasNextPage={data?.hasNextPage ?? false}
                hasPreviousPage={data?.hasPreviousPage ?? false}
                onPageChange={setPage}
              />
            </div>
          )}
        </div>
      </div>

      {isAdmin && (
        <MergeEventsSlideOver
          isOpen={mergeOpen}
          onClose={() => setMergeOpen(false)}
          events={events}
        />
      )}
    </div>
  )
}

interface EventCardProps {
  event: EventListItemDto
  onClick: () => void
}

function EventCard({ event, onClick }: EventCardProps) {
  const color = statusColor(event.status)

  return (
    <article
      onClick={onClick}
      className="relative border p-6 cursor-pointer transition-all"
      style={{
        background: 'rgba(61,15,15,0.4)',
        borderColor: 'rgba(255,255,255,0.1)',
      }}
      onMouseEnter={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
      onMouseLeave={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
    >
      {/* Status bar */}
      <div className="absolute left-0 top-0 bottom-0 w-1" style={{ backgroundColor: color }} />

      {/* Top row */}
      <div className="flex items-start justify-between mb-3">
        <div className="flex items-center gap-3">
          <span className="font-caps text-xs tracking-widest" style={{ color }}>
            {event.status?.toUpperCase() ?? 'UNKNOWN'}
          </span>
          <span
            className="px-2 py-0.5 font-mono text-[10px]"
            style={{ background: 'var(--near-black)', color: '#9ca3af' }}
          >
            {event.articleCount ?? 0} ARTICLES
          </span>
        </div>
        <span className="font-mono text-xs" style={{ color: '#6b7280' }}>
          {formatDate(event.lastUpdatedAt)}
        </span>
      </div>

      {/* Title */}
      <h2
        className="font-display text-2xl mb-3 transition-colors"
        style={{ color: '#E8E8E8' }}
        onMouseEnter={e => (e.currentTarget.style.color = 'var(--caramel)')}
        onMouseLeave={e => (e.currentTarget.style.color = '#E8E8E8')}
      >
        {event.title || '—'}
      </h2>

      {/* Footer */}
      <div
        className="flex items-center justify-between pt-4 border-t"
        style={{ borderColor: 'rgba(255,255,255,0.1)' }}
      >
        <div>
          {(event.unresolvedContradictions ?? 0) > 0 && (
            <span className="font-mono text-xs" style={{ color: 'var(--rust)' }}>
              {event.unresolvedContradictions} UNRESOLVED CONTRADICTIONS
            </span>
          )}
        </div>
        <button
          onClick={e => { e.stopPropagation(); onClick() }}
          className="px-4 py-2 font-caps text-xs tracking-wider border transition-colors"
          style={{ borderColor: 'rgba(255,255,255,0.2)', color: '#9ca3af' }}
          onMouseEnter={e => {
            e.currentTarget.style.borderColor = 'var(--caramel)'
            e.currentTarget.style.color = 'var(--caramel)'
          }}
          onMouseLeave={e => {
            e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)'
            e.currentTarget.style.color = '#9ca3af'
          }}
        >
          VIEW EVENT
        </button>
      </div>
    </article>
  )
}
