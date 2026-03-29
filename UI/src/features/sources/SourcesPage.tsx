import { useState, useMemo } from 'react'
import { Settings, Trash2, Plus, Globe, Radio, CheckCircle, XCircle, Search } from 'lucide-react'
import { useSources } from './useSources'
import { useSourceMutations } from './useSourceMutations'
import { SourceFormSlideOver } from './SourceFormSlideOver'
import { SourceStatsCards } from './SourceStatsCards'
import { ConfirmDialog } from '@/components/shared/ConfirmDialog'
import type { SourceDto } from '@/api/generated'

function formatTimeAgo(iso?: string | null): string {
  if (!iso) return 'Never'
  const diff = Date.now() - new Date(iso).getTime()
  const mins = Math.floor(diff / 60000)
  if (mins < 60) return `${mins}m ago`
  const hrs = Math.floor(mins / 60)
  if (hrs < 24) return `${hrs}h ago`
  return `${Math.floor(hrs / 24)}d ago`
}

const typeBadgeStyle: Record<string, string> = {
  Rss: 'border-[var(--caramel)] text-[var(--caramel)]',
  Telegram: 'border-gray-500 text-gray-400',
}

export function SourcesPage() {
  const { data: sources = [], isLoading } = useSources()
  const { deleteSource } = useSourceMutations()
  const [slideOpen, setSlideOpen] = useState(false)
  const [editingSource, setEditingSource] = useState<SourceDto | undefined>()
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [typeFilter, setTypeFilter] = useState('all')
  const [statusFilter, setStatusFilter] = useState('all')

  const openAdd = () => { setEditingSource(undefined); setSlideOpen(true) }
  const openEdit = (source: SourceDto) => { setEditingSource(source); setSlideOpen(true) }
  const closeSlide = () => { setSlideOpen(false); setEditingSource(undefined) }

  const handleDelete = async () => {
    if (!deletingId) return
    await deleteSource.mutateAsync(deletingId)
    setDeletingId(null)
  }

  const filteredSources = useMemo(() => {
    const q = search.toLowerCase()
    return sources.filter(s => {
      const matchesSearch = !q || s.name?.toLowerCase().includes(q) || s.url?.toLowerCase().includes(q)
      const matchesType = typeFilter === 'all' || s.type === typeFilter
      const matchesStatus =
        statusFilter === 'all' || (statusFilter === 'active' ? s.isActive : !s.isActive)
      return matchesSearch && matchesType && matchesStatus
    })
  }, [sources, search, typeFilter, statusFilter])

  return (
    <div className="p-8">
      {/* Page Header */}
      <div className="flex items-start justify-between mb-8">
        <div>
          <h1 className="font-display text-5xl text-white mb-2">Source Registry</h1>
          <p className="font-mono text-sm text-gray-400">
            {isLoading ? 'Loading…' : `${sources.length} source${sources.length !== 1 ? 's' : ''} configured`}
          </p>
        </div>
        <button
          onClick={openAdd}
          className="flex items-center gap-2 px-4 py-2.5 font-caps text-xs tracking-wider text-white transition-colors"
          style={{ background: 'var(--crimson)' }}
          onMouseEnter={e => (e.currentTarget.style.background = 'rgba(139,26,26,0.8)')}
          onMouseLeave={e => (e.currentTarget.style.background = 'var(--crimson)')}
        >
          <Plus className="w-4 h-4" />
          ADD SOURCE
        </button>
      </div>

      {/* Filter Bar */}
      <div className="flex flex-wrap gap-3 mb-6 items-center">
        <div className="relative flex-1 min-w-48">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500 pointer-events-none" />
          <input
            type="text"
            placeholder="Search sources..."
            value={search}
            onChange={e => setSearch(e.target.value)}
            className="w-full pl-9 pr-3 py-2.5 font-mono text-sm text-gray-300 placeholder-gray-600 focus:outline-none transition-colors"
            style={{
              background: 'var(--near-black)',
              border: '1px solid rgba(255,255,255,0.1)',
            }}
            onFocus={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
            onBlur={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
          />
        </div>
        <select
          value={typeFilter}
          onChange={e => setTypeFilter(e.target.value)}
          className="px-3 py-2.5 font-mono text-sm text-gray-300 focus:outline-none transition-colors"
          style={{
            background: 'var(--near-black)',
            border: '1px solid rgba(255,255,255,0.1)',
          }}
        >
          <option value="all">All Types</option>
          <option value="Rss">RSS</option>
          <option value="Telegram">Telegram</option>
        </select>
        <select
          value={statusFilter}
          onChange={e => setStatusFilter(e.target.value)}
          className="px-3 py-2.5 font-mono text-sm text-gray-300 focus:outline-none transition-colors"
          style={{
            background: 'var(--near-black)',
            border: '1px solid rgba(255,255,255,0.1)',
          }}
        >
          <option value="all">All Statuses</option>
          <option value="active">Active</option>
          <option value="inactive">Inactive</option>
        </select>
      </div>

      {/* Table */}
      <div style={{ border: '1px solid rgba(255,255,255,0.1)' }}>
        {/* Table Header */}
        <div
          className="grid grid-cols-12 px-4 py-3"
          style={{ background: 'var(--burgundy)', borderBottom: '1px solid rgba(255,255,255,0.1)' }}
        >
          <div className="col-span-1 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>ID</div>
          <div className="col-span-3 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>SOURCE</div>
          <div className="col-span-2 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>TYPE</div>
          <div className="col-span-2 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>LAST FETCHED</div>
          <div className="col-span-1 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>STATUS</div>
          <div className="col-span-2 font-caps text-xs tracking-widest" style={{ color: 'var(--caramel)' }}>FEED</div>
          <div className="col-span-1" />
        </div>

        {/* Table Rows */}
        {isLoading ? (
          <div className="px-4 py-12 text-center font-mono text-sm text-gray-500">
            Loading sources…
          </div>
        ) : filteredSources.length === 0 ? (
          <div className="px-4 py-12 text-center font-mono text-sm text-gray-500">
            {search || typeFilter !== 'all' || statusFilter !== 'all'
              ? 'No sources match your filters.'
              : 'No sources configured. Add your first source.'}
          </div>
        ) : (
          filteredSources.map((source, i) => (
            <div
              key={source.id ?? i}
              className="group grid grid-cols-12 px-4 py-4 items-center transition-colors"
              style={{
                borderBottom: i < filteredSources.length - 1 ? '1px solid rgba(255,255,255,0.06)' : undefined,
              }}
              onMouseEnter={e => (e.currentTarget.style.background = 'rgba(61,15,15,0.3)')}
              onMouseLeave={e => (e.currentTarget.style.background = '')}
            >
              {/* ID */}
              <div className="col-span-1 font-mono text-sm text-gray-500 truncate">
                {source.id?.slice(0, 6) ?? '—'}
              </div>

              {/* Source Name + URL */}
              <div className="col-span-3 min-w-0">
                <div className="font-mono text-sm text-white truncate">{source.name}</div>
                <a
                  href={source.url ?? '#'}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-1 font-mono text-xs text-gray-500 hover:text-gray-300 transition-colors truncate max-w-full"
                  onClick={e => e.stopPropagation()}
                >
                  <Globe className="w-3 h-3 flex-shrink-0" />
                  <span className="truncate">{source.url}</span>
                </a>
              </div>

              {/* Type badge */}
              <div className="col-span-2">
                <span
                  className={`inline-block px-2 py-0.5 font-caps text-xs border ${typeBadgeStyle[source.type ?? ''] ?? 'border-gray-600 text-gray-500'}`}
                >
                  {source.type ?? '—'}
                </span>
              </div>

              {/* Last Fetched */}
              <div className="col-span-2">
                <div className="font-mono text-sm text-gray-300">{formatTimeAgo(source.lastFetchedAt)}</div>
              </div>

              {/* Status */}
              <div className="col-span-1 flex items-center gap-1.5">
                {source.isActive ? (
                  <>
                    <CheckCircle className="w-4 h-4 flex-shrink-0" style={{ color: 'var(--caramel)' }} />
                    <span className="font-caps text-xs" style={{ color: 'var(--caramel)' }}>ON</span>
                  </>
                ) : (
                  <>
                    <XCircle className="w-4 h-4 flex-shrink-0 text-gray-500" />
                    <span className="font-caps text-xs text-gray-500">OFF</span>
                  </>
                )}
              </div>

              {/* Articles / Feed */}
              <div className="col-span-2 flex items-center gap-1.5">
                <Radio className="w-4 h-4 text-gray-500" />
                <span className="font-mono text-sm text-gray-300">
                  {source.type === 'Rss' ? 'RSS Feed' : source.type ?? '—'}
                </span>
              </div>

              {/* Actions */}
              <div
                className="col-span-1 flex items-center gap-2 justify-end opacity-0 group-hover:opacity-100 transition-opacity"
                onClick={e => e.stopPropagation()}
              >
                <button
                  onClick={() => openEdit(source)}
                  className="p-1.5 transition-colors"
                  style={{ color: 'var(--caramel)' }}
                  title="Edit"
                >
                  <Settings className="w-4 h-4" />
                </button>
                <button
                  onClick={() => source.id && setDeletingId(source.id)}
                  className="p-1.5 transition-colors"
                  style={{ color: 'var(--crimson)' }}
                  title="Delete"
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </div>
            </div>
          ))
        )}
      </div>

      {/* Stats Bar */}
      <div className="mt-6">
        <SourceStatsCards sources={sources} />
      </div>

      <SourceFormSlideOver isOpen={slideOpen} onClose={closeSlide} source={editingSource} />
      <ConfirmDialog
        isOpen={!!deletingId}
        onClose={() => setDeletingId(null)}
        onConfirm={handleDelete}
        title="Delete Source"
        message="Are you sure you want to delete this source? This action cannot be undone."
        confirmLabel="Delete"
        variant="danger"
        isLoading={deleteSource.isPending}
      />
    </div>
  )
}
