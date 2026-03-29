import { useState } from 'react'
import { useParams, useNavigate, Link } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import { useEventDetail } from './useEventDetail'
import { useEventMutations } from './useEventMutations'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Spinner } from '@/components/ui/Spinner'
import { ConfirmDialog } from '@/components/shared/ConfirmDialog'
import { usePermissions } from '@/hooks/usePermissions'
import type { ContradictionDto, EventArticleDto } from '@/api/generated'

type Tab = 'timeline' | 'updates' | 'contradictions'

const ROLES = ['Initiator', 'Update', 'Contradiction'] as const

function formatDate(iso?: string) {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

function statusVariant(status?: string | null): 'info' | 'neutral' {
  return status === 'Active' ? 'info' : 'neutral'
}

function roleVariant(role?: string | null): 'info' | 'neutral' | 'warning' {
  if (role === 'Initiator') return 'info'
  if (role === 'Contradiction') return 'warning'
  return 'neutral'
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
    return <p className="text-gray-400 text-sm py-8 text-center">No articles in this event.</p>
  }

  return (
    <ul className="divide-y divide-gray-100">
      {articles.map(article => (
        <li key={article.articleId} className="py-4 flex items-start justify-between gap-4">
          <div className="flex-1 min-w-0">
            <Link
              to={`/articles/${article.articleId}`}
              className="text-sm font-medium text-gray-900 hover:text-indigo-600 transition-colors line-clamp-2"
            >
              {article.title}
            </Link>
            <p className="text-xs text-gray-400 mt-1">{formatDate(article.addedAt)}</p>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            {editingId === article.articleId ? (
              <>
                <select
                  value={roleValue}
                  onChange={e => setRoleValue(e.target.value)}
                  className="text-xs rounded border border-gray-300 px-2 py-1 focus:outline-none focus:ring-1 focus:ring-indigo-500"
                >
                  {ROLES.map(r => (
                    <option key={r} value={r}>{r}</option>
                  ))}
                </select>
                <Button size="sm" onClick={() => confirmEdit(article.articleId!)} isLoading={isPending}>
                  Save
                </Button>
                <Button size="sm" variant="ghost" onClick={() => setEditingId(null)}>
                  Cancel
                </Button>
              </>
            ) : (
              <>
                <Badge variant={roleVariant(article.role)}>{article.role}</Badge>
                {isAdmin && (
                  <Button size="sm" variant="ghost" onClick={() => startEdit(article)}>
                    Change Role
                  </Button>
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
function UpdatesTab({ updates }: { updates: { id?: string; factSummary?: string | null; isPublished?: boolean; createdAt?: string }[] }) {
  if (updates.length === 0) {
    return <p className="text-gray-400 text-sm py-8 text-center">No updates recorded yet.</p>
  }

  return (
    <ul className="divide-y divide-gray-100">
      {updates.map(u => (
        <li key={u.id} className="py-4 flex items-start justify-between gap-4">
          <div className="flex-1 min-w-0">
            <p className="text-sm text-gray-800">{u.factSummary}</p>
            <p className="text-xs text-gray-400 mt-1">{formatDate(u.createdAt)}</p>
          </div>
          <Badge variant={u.isPublished ? 'positive' : 'neutral'}>
            {u.isPublished ? 'Published' : 'Unpublished'}
          </Badge>
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
  const [resolvingId, setResolvingId] = useState<string | null>(null)

  if (contradictions.length === 0) {
    return <p className="text-gray-400 text-sm py-8 text-center">No contradictions detected.</p>
  }

  return (
    <>
      <ul className="divide-y divide-gray-100">
        {contradictions.map(c => (
          <li key={c.id} className="py-4 space-y-2">
            <div className="flex items-start justify-between gap-4">
              <div className="flex-1 min-w-0">
                <p className="text-sm text-gray-800">{c.description}</p>
                <p className="text-xs text-gray-400 mt-1">{formatDate(c.createdAt)}</p>
                {c.articleIds && c.articleIds.length > 0 && (
                  <div className="flex flex-wrap gap-2 mt-2">
                    {c.articleIds.map(aid => (
                      <Link
                        key={aid}
                        to={`/articles/${aid}`}
                        className="text-xs text-indigo-600 hover:text-indigo-800 font-mono"
                      >
                        Article {aid.slice(0, 8)}…
                      </Link>
                    ))}
                  </div>
                )}
              </div>
              <div className="flex items-center gap-2 shrink-0">
                <Badge variant={c.isResolved ? 'positive' : 'warning'}>
                  {c.isResolved ? 'Resolved' : 'Unresolved'}
                </Badge>
                {!c.isResolved && canResolve && (
                  <Button size="sm" variant="secondary" onClick={() => setResolvingId(c.id!)}>
                    Resolve
                  </Button>
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

// ---- Main page ----
export function EventDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { isAdmin, isEditor } = usePermissions()
  const { data: event, isLoading } = useEventDetail(id!)
  const { resolveContradiction, reclassifyArticle, changeStatus } = useEventMutations(id)

  const [activeTab, setActiveTab] = useState<Tab>('timeline')
  const [archiveOpen, setArchiveOpen] = useState(false)

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <Spinner size="lg" className="text-indigo-600" />
      </div>
    )
  }

  if (!event) {
    return <div className="text-center py-16 text-gray-500">Event not found.</div>
  }

  const articles = event.articles ?? []
  const updates = event.updates ?? []
  const contradictions = event.contradictions ?? []
  const unresolvedCount = contradictions.filter(c => !c.isResolved).length

  const tabs: { key: Tab; label: string; count?: number }[] = [
    { key: 'timeline', label: 'Timeline', count: articles.length },
    { key: 'updates', label: 'Updates', count: updates.length },
    { key: 'contradictions', label: 'Contradictions', count: unresolvedCount || undefined },
  ]

  return (
    <div className="max-w-4xl">
      {/* Back */}
      <button
        onClick={() => navigate('/events')}
        className="flex items-center gap-2 text-sm text-gray-500 hover:text-gray-700 mb-4 transition-colors"
      >
        <ArrowLeft className="w-4 h-4" />
        Back to Events
      </button>

      {/* Header */}
      <div className="mb-6">
        <div className="flex items-start justify-between gap-4 flex-wrap">
          <div className="flex items-center gap-3 flex-wrap">
            <h1 className="text-2xl font-bold text-gray-900">{event.title}</h1>
            <Badge variant={statusVariant(event.status)}>{event.status}</Badge>
          </div>
          {isAdmin && event.status !== 'Archived' && (
            <Button variant="secondary" size="sm" onClick={() => setArchiveOpen(true)}>
              Archive Event
            </Button>
          )}
        </div>

        {event.summary && (
          <p className="mt-3 text-sm text-gray-600 leading-relaxed">{event.summary}</p>
        )}

        {/* Stat pills */}
        <div className="flex gap-4 mt-4 flex-wrap">
          <div className="bg-gray-50 rounded-md px-3 py-1.5 text-sm">
            <span className="text-gray-500">Articles</span>{' '}
            <span className="font-semibold text-gray-900">{articles.length}</span>
          </div>
          {unresolvedCount > 0 && (
            <div className="bg-yellow-50 rounded-md px-3 py-1.5 text-sm">
              <span className="text-yellow-700">Unresolved Contradictions</span>{' '}
              <span className="font-semibold text-yellow-900">{unresolvedCount}</span>
            </div>
          )}
          {(event.reclassifiedCount ?? 0) > 0 && (
            <div className="bg-gray-50 rounded-md px-3 py-1.5 text-sm">
              <span className="text-gray-500">Reclassified</span>{' '}
              <span className="font-semibold text-gray-900">{event.reclassifiedCount}</span>
            </div>
          )}
        </div>
      </div>

      {/* Tabs */}
      <div className="border-b border-gray-200 mb-6">
        <nav className="flex gap-0 -mb-px">
          {tabs.map(tab => (
            <button
              key={tab.key}
              onClick={() => setActiveTab(tab.key)}
              className={[
                'px-4 py-2.5 text-sm font-medium border-b-2 transition-colors',
                activeTab === tab.key
                  ? 'border-indigo-600 text-indigo-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300',
              ].join(' ')}
            >
              {tab.label}
              {tab.count !== undefined && (
                <span className="ml-1.5 bg-gray-100 text-gray-600 rounded-full px-1.5 py-0.5 text-xs">
                  {tab.count}
                </span>
              )}
            </button>
          ))}
        </nav>
      </div>

      {/* Tab content */}
      <div className="bg-white rounded-lg border border-gray-200 p-6">
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
      </div>

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
