import { useState, useMemo } from 'react'
import { useParams, useNavigate, Link } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import { useEventDetail } from './useEventDetail'
import { useEventMutations } from './useEventMutations'
import { GenerateContentModal } from '@/features/publications/GenerateContentModal'
import { usePublications } from '@/features/publications/usePublications'
import { ConfirmDialog } from '@/components/shared/ConfirmDialog'
import { usePermissions } from '@/hooks/usePermissions'
import { MediaGallery } from '@/components/shared/MediaGallery'
import type { MediaItem } from '@/components/shared/MediaGallery'
import type { ContradictionDto, EventArticleDto } from '@/api/generated'

type Tab = 'timeline' | 'updates' | 'contradictions' | 'media' | 'publications'

const ROLES = ['Initiator', 'Update', 'Contradiction'] as const

function formatDate(iso?: string) {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

function statusColor(status?: string | null): string {
  if (status === 'Active') return 'var(--crimson)'
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

function roleColor(role?: string | null): string {
  if (role === 'Initiator') return 'var(--caramel)'
  if (role === 'Contradiction') return 'var(--rust)'
  return '#6b7280'
}

// ---- Timeline tab ----
function TimelineTab({
  articles,
  isAdmin,
  onReclassify,
  isPending,
}: {
  articles: EventArticleDto[]
  isAdmin: boolean
  onReclassify: (articleId: string, role: string) => void
  isPending: boolean
}) {
  const { projectSlug } = useParams<{ projectSlug: string }>()
  const [editingId, setEditingId] = useState<string | null>(null)
  const [roleValue, setRoleValue] = useState('')

  const startEdit = (article: EventArticleDto) => {
    setEditingId(article.articleId ?? null)
    setRoleValue(article.role ?? '')
  }

  const confirmEdit = (articleId: string) => {
    onReclassify(articleId, roleValue)
    setEditingId(null)
  }

  if (articles.length === 0) {
    return (
      <p className="font-mono text-sm text-center py-8" style={{ color: '#9ca3af' }}>
        No articles in this event.
      </p>
    )
  }

  return (
    <ul className="divide-y" style={{ borderColor: 'rgba(255,255,255,0.1)' }}>
      {articles.map(article => (
        <li
          key={article.articleId}
          className="py-4 flex items-start justify-between gap-4"
          style={{ borderColor: 'rgba(255,255,255,0.1)' }}
        >
          <div className="flex-1 min-w-0">
            <Link
              to={`/projects/${projectSlug}/articles/${article.articleId}`}
              className="font-display text-lg transition-colors line-clamp-2"
              style={{ color: '#E8E8E8' }}
              onMouseEnter={e => (e.currentTarget.style.color = 'var(--caramel)')}
              onMouseLeave={e => (e.currentTarget.style.color = '#E8E8E8')}
            >
              {article.title}
            </Link>
            <p className="font-mono text-xs mt-1" style={{ color: '#6b7280' }}>
              {formatDate(article.addedAt)}
            </p>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            {editingId === article.articleId ? (
              <>
                <select
                  value={roleValue}
                  onChange={e => setRoleValue(e.target.value)}
                  className="font-mono text-xs px-2 py-1 focus:outline-none"
                  style={{
                    background: 'var(--burgundy)',
                    border: '1px solid rgba(255,255,255,0.2)',
                    color: '#E8E8E8',
                  }}
                >
                  {ROLES.map(r => (
                    <option key={r} value={r}>{r}</option>
                  ))}
                </select>
                <button
                  onClick={() => confirmEdit(article.articleId!)}
                  disabled={isPending}
                  className="px-3 py-1.5 font-caps text-xs tracking-wider text-white transition-opacity hover:opacity-90 disabled:opacity-50"
                  style={{ background: 'var(--crimson)' }}
                >
                  SAVE
                </button>
                <button
                  onClick={() => setEditingId(null)}
                  className="px-3 py-1.5 font-caps text-xs tracking-wider border transition-colors"
                  style={{ borderColor: 'rgba(255,255,255,0.2)', color: '#9ca3af' }}
                >
                  CANCEL
                </button>
              </>
            ) : (
              <>
                <span
                  className="font-caps text-xs tracking-widest"
                  style={{ color: roleColor(article.role) }}
                >
                  {article.role?.toUpperCase() ?? '—'}
                </span>
                {isAdmin && (
                  <button
                    onClick={() => startEdit(article)}
                    className="font-mono text-xs transition-colors"
                    style={{ color: '#6b7280' }}
                    onMouseEnter={e => (e.currentTarget.style.color = 'var(--caramel)')}
                    onMouseLeave={e => (e.currentTarget.style.color = '#6b7280')}
                  >
                    CHANGE
                  </button>
                )}
              </>
            )}
          </div>
        </li>
      ))}
    </ul>
  )
}

// ---- Updates tab ----
function UpdatesTab({
  updates,
}: {
  updates: { id?: string; factSummary?: string | null; isPublished?: boolean; createdAt?: string }[]
}) {
  if (updates.length === 0) {
    return (
      <p className="font-mono text-sm text-center py-8" style={{ color: '#9ca3af' }}>
        No updates recorded yet.
      </p>
    )
  }

  return (
    <ul className="divide-y" style={{ borderColor: 'rgba(255,255,255,0.1)' }}>
      {updates.map(u => (
        <li key={u.id} className="py-4 flex items-start justify-between gap-4">
          <div className="flex-1 min-w-0">
            <p className="font-mono text-sm" style={{ color: '#E8E8E8' }}>
              {u.factSummary}
            </p>
            <p className="font-mono text-xs mt-1" style={{ color: '#6b7280' }}>
              {formatDate(u.createdAt)}
            </p>
          </div>
          <span
            className="font-caps text-xs tracking-widest shrink-0"
            style={{ color: u.isPublished ? 'var(--caramel)' : '#6b7280' }}
          >
            {u.isPublished ? 'PUBLISHED' : 'UNPUBLISHED'}
          </span>
        </li>
      ))}
    </ul>
  )
}

// ---- Contradictions tab ----
function ContradictionsTab({
  contradictions,
  canResolve,
  onResolve,
  isPending,
}: {
  contradictions: ContradictionDto[]
  canResolve: boolean
  onResolve: (id: string) => void
  isPending: boolean
}) {
  const { projectSlug } = useParams<{ projectSlug: string }>()
  const [resolvingId, setResolvingId] = useState<string | null>(null)

  if (contradictions.length === 0) {
    return (
      <p className="font-mono text-sm text-center py-8" style={{ color: '#9ca3af' }}>
        No contradictions detected.
      </p>
    )
  }

  return (
    <>
      <ul className="divide-y" style={{ borderColor: 'rgba(255,255,255,0.1)' }}>
        {contradictions.map(c => (
          <li key={c.id} className="py-4 space-y-2">
            <div className="flex items-start justify-between gap-4">
              <div className="flex-1 min-w-0">
                <p className="font-mono text-sm" style={{ color: '#E8E8E8' }}>
                  {c.description}
                </p>
                <p className="font-mono text-xs mt-1" style={{ color: '#6b7280' }}>
                  {formatDate(c.createdAt)}
                </p>
                {c.articleIds && c.articleIds.length > 0 && (
                  <div className="flex flex-wrap gap-2 mt-2">
                    {c.articleIds.map(aid => (
                      <Link
                        key={aid}
                        to={`/projects/${projectSlug}/articles/${aid}`}
                        className="font-mono text-xs transition-colors"
                        style={{ color: 'var(--caramel)' }}
                        onMouseEnter={e => (e.currentTarget.style.opacity = '0.7')}
                        onMouseLeave={e => (e.currentTarget.style.opacity = '1')}
                      >
                        Article {aid.slice(0, 8)}…
                      </Link>
                    ))}
                  </div>
                )}
              </div>
              <div className="flex items-center gap-3 shrink-0">
                <span
                  className="font-caps text-xs tracking-widest"
                  style={{ color: c.isResolved ? 'var(--caramel)' : 'var(--rust)' }}
                >
                  {c.isResolved ? 'RESOLVED' : 'UNRESOLVED'}
                </span>
                {!c.isResolved && canResolve && (
                  <button
                    onClick={() => setResolvingId(c.id!)}
                    className="px-3 py-1.5 font-caps text-xs tracking-wider border transition-colors"
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
                    RESOLVE
                  </button>
                )}
              </div>
            </div>
          </li>
        ))}
      </ul>

      <ConfirmDialog
        isOpen={!!resolvingId}
        title="Resolve Contradiction"
        message="Mark this contradiction as resolved? This action cannot be undone."
        confirmLabel="Resolve"
        isLoading={isPending}
        onConfirm={() => {
          if (resolvingId) {
            onResolve(resolvingId)
            setResolvingId(null)
          }
        }}
        onClose={() => setResolvingId(null)}
      />
    </>
  )
}

// ---- Media tab ----
function MediaTab({ items }: { items: MediaItem[] }) {
  if (items.length === 0) {
    return (
      <p className="font-mono text-sm text-center py-8" style={{ color: '#9ca3af' }}>
        No media attached to this event.
      </p>
    )
  }
  return <MediaGallery items={items} title="" />
}

// ---- Publications tab ----
function PublicationsTab({ eventId }: { eventId: string }) {
  const { projectSlug } = useParams<{ projectSlug: string }>()
  const { publications, isLoading } = usePublications(eventId)

  if (isLoading) {
    return (
      <p className="font-mono text-sm text-center py-8" style={{ color: '#9ca3af' }}>
        Loading publications…
      </p>
    )
  }

  if (publications.length === 0) {
    return (
      <p className="font-mono text-sm text-center py-8" style={{ color: '#9ca3af' }}>
        No publications yet. Click "Generate Content" to create one.
      </p>
    )
  }

  return (
    <ul className="divide-y" style={{ borderColor: 'rgba(255,255,255,0.1)' }}>
      {publications.map(pub => (
        <li key={pub.id} className="py-4 flex items-center justify-between gap-4">
          <div>
            <Link
              to={`/projects/${projectSlug}/publications/${pub.id}`}
              className="font-mono text-sm transition-colors"
              style={{ color: '#E8E8E8' }}
              onMouseEnter={e => (e.currentTarget.style.color = 'var(--caramel)')}
              onMouseLeave={e => (e.currentTarget.style.color = '#E8E8E8')}
            >
              {pub.targetName} · {pub.platform}
            </Link>
            <p className="font-mono text-xs mt-1" style={{ color: '#6b7280' }}>
              {formatDate(pub.createdAt)}
            </p>
          </div>
          <span className="font-caps text-xs tracking-widest shrink-0" style={{ color: '#9ca3af' }}>
            {(pub.status ?? '').toUpperCase()}
          </span>
        </li>
      ))}
    </ul>
  )
}

// ---- Main page ----
export function EventDetailPage() {
  const { projectSlug, id } = useParams<{ projectSlug: string; id: string }>()
  const navigate = useNavigate()
  const { isAdmin, isEditor } = usePermissions()
  const { data: event, isLoading } = useEventDetail(id!)
  const { resolveContradiction, reclassifyArticle, changeStatus } = useEventMutations(id)

  const [activeTab, setActiveTab] = useState<Tab>('timeline')
  const [archiveOpen, setArchiveOpen] = useState(false)
  const [generateOpen, setGenerateOpen] = useState(false)

  const articles = event?.articles ?? []
  const updates = event?.updates ?? []
  const contradictions = event?.contradictions ?? []

  const mediaItems = useMemo(() => {
    const seen = new Set<string>()
    const out: MediaItem[] = []
    for (const a of articles) {
      for (const m of a.media ?? []) {
        if (!m.id || seen.has(m.id)) continue
        seen.add(m.id)
        out.push({ id: m.id, url: m.url!, kind: m.kind as 'Image' | 'Video', contentType: m.contentType! })
      }
    }
    return out
  }, [articles])

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="font-mono text-sm animate-pulse" style={{ color: '#9ca3af' }}>
          Loading…
        </div>
      </div>
    )
  }

  if (!event) {
    return (
      <div className="font-mono text-sm text-center py-16" style={{ color: '#9ca3af' }}>
        Event not found.
      </div>
    )
  }

  const unresolvedCount = contradictions.filter(c => !c.isResolved).length
  const color = statusColor(event.status)
  const heroImage = mediaItems.find(m => m.kind === 'Image')

  const tabs: { key: Tab; label: string; count?: number }[] = [
    { key: 'timeline', label: 'TIMELINE', count: articles.length },
    { key: 'updates', label: 'UPDATES', count: updates.length },
    { key: 'contradictions', label: 'CONTRADICTIONS', count: unresolvedCount || undefined },
    { key: 'media', label: 'MEDIA', count: mediaItems.length || undefined },
    { key: 'publications', label: 'PUBLICATIONS' },
  ]

  return (
    <div className="max-w-4xl">
      {/* Back */}
      <button
        onClick={() => navigate(`/projects/${projectSlug}/events`)}
        className="flex items-center gap-2 font-mono text-xs mb-6 transition-colors"
        style={{ color: '#6b7280' }}
        onMouseEnter={e => (e.currentTarget.style.color = 'var(--caramel)')}
        onMouseLeave={e => (e.currentTarget.style.color = '#6b7280')}
      >
        <ArrowLeft className="w-4 h-4" />
        BACK TO EVENTS
      </button>

      {/* Header card */}
      <div
        className="relative border mb-6 overflow-hidden"
        style={{
          background: 'rgba(61,15,15,0.4)',
          borderColor: 'rgba(255,255,255,0.1)',
        }}
      >
        <div className="absolute left-0 top-0 bottom-0 w-1 z-10" style={{ backgroundColor: color }} />

        {/* Hero image */}
        {heroImage && (
          <div className="relative w-full" style={{ maxHeight: '280px', overflow: 'hidden' }}>
            <img
              src={heroImage.url}
              alt=""
              className="w-full object-cover"
              style={{ maxHeight: '280px', opacity: 0.8 }}
            />
            <div
              className="absolute inset-0"
              style={{ background: 'linear-gradient(to bottom, rgba(61,15,15,0) 30%, rgba(61,15,15,0.97) 100%)' }}
            />
          </div>
        )}

        <div className="p-6">
        <div className="flex items-start justify-between gap-4 flex-wrap mb-3">
          <div className="flex items-center gap-3 flex-wrap">
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
          </div>
          <div className="flex items-center gap-2 flex-wrap">
            {(isAdmin || isEditor) && event.status === 'Active' && (
              <button
                onClick={() => setGenerateOpen(true)}
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
                GENERATE CONTENT
              </button>
            )}
            {isAdmin && event.status !== 'Archived' && (
              <button
                onClick={() => setArchiveOpen(true)}
                className="px-4 py-2 font-caps text-xs tracking-wider border transition-colors"
                style={{ borderColor: 'rgba(255,255,255,0.2)', color: '#9ca3af' }}
                onMouseEnter={e => {
                  e.currentTarget.style.borderColor = 'var(--rust)'
                  e.currentTarget.style.color = 'var(--rust)'
                }}
                onMouseLeave={e => {
                  e.currentTarget.style.borderColor = 'rgba(255,255,255,0.2)'
                  e.currentTarget.style.color = '#9ca3af'
                }}
              >
                ARCHIVE EVENT
              </button>
            )}
          </div>
        </div>

        <h1 className="font-display text-4xl mb-3" style={{ color: '#E8E8E8' }}>
          {event.title}
        </h1>

        {event.summary && (
          <p className="font-mono text-sm leading-relaxed mb-4" style={{ color: '#9ca3af' }}>
            {event.summary}
          </p>
        )}

        {/* Stats */}
        <div
          className="flex gap-4 pt-4 border-t flex-wrap"
          style={{ borderColor: 'rgba(255,255,255,0.1)' }}
        >
          <div
            className="px-3 py-1.5"
            style={{ background: 'var(--near-black)' }}
          >
            <span className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>
              ARTICLES{' '}
            </span>
            <span className="font-mono text-sm" style={{ color: '#E8E8E8' }}>
              {articles.length}
            </span>
          </div>
          {unresolvedCount > 0 && (
            <div
              className="px-3 py-1.5"
              style={{ background: 'var(--near-black)' }}
            >
              <span className="font-caps text-[10px] tracking-widest" style={{ color: 'var(--rust)' }}>
                CONTRADICTIONS{' '}
              </span>
              <span className="font-mono text-sm" style={{ color: 'var(--rust)' }}>
                {unresolvedCount}
              </span>
            </div>
          )}
          {(event.reclassifiedCount ?? 0) > 0 && (
            <div
              className="px-3 py-1.5"
              style={{ background: 'var(--near-black)' }}
            >
              <span className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>
                RECLASSIFIED{' '}
              </span>
              <span className="font-mono text-sm" style={{ color: '#E8E8E8' }}>
                {event.reclassifiedCount}
              </span>
            </div>
          )}
          <div className="px-3 py-1.5" style={{ background: 'var(--near-black)' }}>
            <span className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>
              SOURCES{' '}
            </span>
            <span className="font-mono text-sm" style={{ color: '#E8E8E8' }}>
              {event.distinctSourceCount ?? 0}
            </span>
          </div>
          {event.importanceBaseScore != null && (
            <div className="px-3 py-1.5" style={{ background: 'var(--near-black)' }}>
              <span className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>
                BASE SCORE{' '}
              </span>
              <span className="font-mono text-sm" style={{ color: '#E8E8E8' }}>
                {event.importanceBaseScore.toFixed(1)}
              </span>
            </div>
          )}
        </div>
        </div>
      </div>

      {/* Tabs */}
      <div
        className="flex border-b mb-0"
        style={{ borderColor: 'rgba(255,255,255,0.1)' }}
      >
        {tabs.map(tab => (
          <button
            key={tab.key}
            onClick={() => setActiveTab(tab.key)}
            className="px-5 py-3 font-caps text-xs tracking-widest transition-colors relative"
            style={{
              color: activeTab === tab.key ? 'var(--caramel)' : '#6b7280',
              borderBottom: activeTab === tab.key ? '2px solid var(--caramel)' : '2px solid transparent',
              marginBottom: '-1px',
            }}
            onMouseEnter={e => {
              if (activeTab !== tab.key)
                (e.currentTarget as HTMLElement).style.color = '#9ca3af'
            }}
            onMouseLeave={e => {
              if (activeTab !== tab.key)
                (e.currentTarget as HTMLElement).style.color = '#6b7280'
            }}
          >
            {tab.label}
            {tab.count !== undefined && (
              <span
                className="ml-2 font-mono text-[10px] px-1.5 py-0.5"
                style={{ background: 'var(--near-black)', color: '#9ca3af' }}
              >
                {tab.count}
              </span>
            )}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div
        className="border border-t-0 p-6"
        style={{
          background: 'rgba(61,15,15,0.4)',
          borderColor: 'rgba(255,255,255,0.1)',
        }}
      >
        {activeTab === 'timeline' && (
          <TimelineTab
            articles={articles}
            isAdmin={isAdmin}
            onReclassify={(articleId, role) => reclassifyArticle.mutate({ articleId, role })}
            isPending={reclassifyArticle.isPending}
          />
        )}
        {activeTab === 'updates' && <UpdatesTab updates={updates} />}
        {activeTab === 'contradictions' && (
          <ContradictionsTab
            contradictions={contradictions}
            canResolve={isAdmin || isEditor}
            onResolve={cid => resolveContradiction.mutate(cid)}
            isPending={resolveContradiction.isPending}
          />
        )}
        {activeTab === 'media' && <MediaTab items={mediaItems} />}
        {activeTab === 'publications' && <PublicationsTab eventId={id!} />}
      </div>

      {/* Generate content modal */}
      <GenerateContentModal
        isOpen={generateOpen}
        onClose={() => setGenerateOpen(false)}
        eventId={id!}
      />

      {/* Archive confirm */}
      <ConfirmDialog
        isOpen={archiveOpen}
        title="Archive Event"
        message="Archive this event? It will no longer appear as active."
        confirmLabel="Archive"
        variant="danger"
        isLoading={changeStatus.isPending}
        onConfirm={() => {
          changeStatus.mutate('Archived', { onSuccess: () => setArchiveOpen(false) })
        }}
        onClose={() => setArchiveOpen(false)}
      />
    </div>
  )
}
