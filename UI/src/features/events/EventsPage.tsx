import { useState, useEffect } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { Search } from 'lucide-react'
import { useEvents } from './useEvents'
import { MergeEventsSlideOver } from './MergeEventsSlideOver'
import { Pagination } from '@/components/shared/Pagination'
import { usePermissions } from '@/hooks/usePermissions'
import type { EventListItemDto } from '@/api/generated'

const SORT_OPTIONS = ['newest', 'oldest', 'importance'] as const
type SortOption = (typeof SORT_OPTIONS)[number]

const TIER_FILTERS = ['all', 'Breaking', 'High', 'Normal', 'Low'] as const
type TierFilter = (typeof TIER_FILTERS)[number]

const PAGE_SIZE = 20
const DEBOUNCE_MS = 300

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

function tierColor(tier?: string | null): string {
  if (tier === 'Breaking') return 'var(--crimson)'
  if (tier === 'High') return 'var(--rust)'
  if (tier === 'Normal') return '#6b7280'
  if (tier === 'Low') return '#4b5563'
  return '#6b7280'
}

export function EventsPage() {
  const navigate = useNavigate()
  const { projectSlug } = useParams<{ projectSlug: string }>()
  const { isAdmin } = usePermissions()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [sortBy, setSortBy] = useState<SortOption>('newest')
  const [tier, setTier] = useState<TierFilter>('all')
  const [mergeOpen, setMergeOpen] = useState(false)

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), DEBOUNCE_MS)
    return () => clearTimeout(timer)
  }, [search])

  useEffect(() => {
    setPage(1)
  }, [debouncedSearch, sortBy, tier])

  const { data, isLoading } = useEvents(page, PAGE_SIZE, debouncedSearch, sortBy, tier === 'all' ? undefined : tier)
  const events = data?.items ?? []

  return (
    <div className="flex -m-6" style={{ minHeight: 'calc(100vh - 5rem)' }}>
      {/* Left panel – filters */}
      <aside
        className="w-64 shrink-0 border-r p-6"
        style={{ borderColor: 'rgba(255,255,255,0.1)', background: 'rgba(61,15,15,0.3)' }}
      >
        <div className="mb-6">
          <div className="font-caps text-xs tracking-widest mb-3" style={{ color: 'var(--caramel)' }}>
            FILTER
          </div>
          <div className="relative">
            <Search
              className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4"
              style={{ color: '#6b7280' }}
            />
            <input
              type="text"
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Search events..."
              className="w-full pl-10 pr-4 py-2 font-mono text-xs border focus:outline-none transition-colors"
              style={{
                background: 'var(--burgundy)',
                borderColor: 'rgba(255,255,255,0.1)',
                color: '#E8E8E8',
              }}
              onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
              onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
            />
          </div>
        </div>

        <div className="space-y-1">
          <div className="font-caps text-xs tracking-widest mb-3" style={{ color: 'var(--caramel)' }}>
            SORT
          </div>
          {SORT_OPTIONS.map(option => (
            <button
              key={option}
              onClick={() => setSortBy(option)}
              className="w-full text-left px-3 py-2 font-mono text-xs transition-colors"
              style={{
                background: sortBy === option ? 'var(--burgundy)' : 'transparent',
                color: sortBy === option ? '#E8E8E8' : '#9ca3af',
              }}
              onMouseEnter={e => {
                if (sortBy !== option) e.currentTarget.style.color = 'var(--caramel)'
              }}
              onMouseLeave={e => {
                if (sortBy !== option) e.currentTarget.style.color = '#9ca3af'
              }}
            >
              {option.toUpperCase()}
            </button>
          ))}
        </div>

        <div className="mt-6 space-y-1">
          <div className="font-caps text-xs tracking-widest mb-3" style={{ color: 'var(--caramel)' }}>
            IMPORTANCE
          </div>
          {TIER_FILTERS.map(s => (
            <button
              key={s}
              onClick={() => setTier(s)}
              className="w-full text-left px-3 py-2 font-mono text-xs transition-colors"
              style={{
                background: tier === s ? 'var(--burgundy)' : 'transparent',
                color: tier === s ? '#E8E8E8' : '#9ca3af',
              }}
              onMouseEnter={e => {
                if (tier !== s) e.currentTarget.style.color = 'var(--caramel)'
              }}
              onMouseLeave={e => {
                if (tier !== s) e.currentTarget.style.color = '#9ca3af'
              }}
            >
              {s.toUpperCase()}
            </button>
          ))}
        </div>

        <div
          className="mt-8 pt-6 border-t"
          style={{ borderColor: 'rgba(255,255,255,0.1)' }}
        >
          <div className="font-caps text-xs tracking-widest mb-3" style={{ color: 'var(--caramel)' }}>
            TOTAL
          </div>
          <div className="font-mono text-sm" style={{ color: '#9ca3af' }}>
            {data?.totalCount ?? 0} events
          </div>
        </div>
      </aside>

      {/* Center panel – events list */}
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
                  onClick={() => event.id && navigate(`/projects/${projectSlug}/events/${event.id}`)}
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
          {event.importanceTier && (
            <span
              className="font-caps text-xs tracking-widest"
              style={{ color: tierColor(event.importanceTier) }}
            >
              {event.importanceTier.toUpperCase()}
            </span>
          )}
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
