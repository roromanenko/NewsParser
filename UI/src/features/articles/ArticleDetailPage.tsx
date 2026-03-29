import { useState } from 'react'
import { useParams, useNavigate, Link } from 'react-router-dom'
import { ArrowLeft, ExternalLink } from 'lucide-react'
import { useArticleDetail } from './useArticleDetail'
import { ApproveModal } from './ApproveModal'
import { RejectModal } from './RejectModal'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Spinner } from '@/components/ui/Spinner'
import { useSidebar } from '@/layouts/SidebarContext'
import { cn } from '@/lib/utils'

function eventStatusVariant(s?: string | null): 'info' | 'neutral' {
  return s === 'Active' ? 'info' : 'neutral'
}

function eventRoleVariant(r?: string | null): 'info' | 'neutral' | 'warning' {
  if (r === 'Initiator') return 'info'
  if (r === 'Contradiction') return 'warning'
  return 'neutral'
}

function sentimentVariant(s?: string | null): 'positive' | 'negative' | 'neutral' {
  const lower = s?.toLowerCase()
  if (lower === 'positive') return 'positive'
  if (lower === 'negative') return 'negative'
  return 'neutral'
}

function formatDate(iso?: string): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

export function ArticleDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: article, isLoading } = useArticleDetail(id!)
  const [approveOpen, setApproveOpen] = useState(false)
  const [rejectOpen, setRejectOpen] = useState(false)
  const { collapsed } = useSidebar()

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <Spinner size="lg" className="text-indigo-600" />
      </div>
    )
  }

  if (!article) {
    return (
      <div className="text-center py-16 text-gray-500">Article not found.</div>
    )
  }

  return (
    <div className="max-w-6xl">
      {/* Back button */}
      <button
        onClick={() => navigate('/articles')}
        className="flex items-center gap-2 text-sm text-gray-500 hover:text-gray-700 mb-4 transition-colors"
      >
        <ArrowLeft className="w-4 h-4" />
        Back to Articles
      </button>

      <h1 className="text-2xl font-bold text-gray-900 mb-6">{article.title}</h1>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 mb-24">
        {/* Left: Content */}
        <div className="lg:col-span-2 space-y-6">
          {article.summary && (
            <div className="bg-white rounded-lg border border-gray-200 p-6">
              <h3 className="text-sm font-semibold text-gray-500 uppercase tracking-wider mb-3">Summary</h3>
              <p className="text-gray-700 text-sm leading-relaxed">{article.summary}</p>
            </div>
          )}
          {article.content && (
            <div className="bg-white rounded-lg border border-gray-200 p-6">
              <h3 className="text-sm font-semibold text-gray-500 uppercase tracking-wider mb-3">Full Content</h3>
              <div className="text-gray-700 text-sm leading-relaxed whitespace-pre-wrap">{article.content}</div>
            </div>
          )}
        </div>

        {/* Right: Metadata */}
        <div className="space-y-4">
          <div className="bg-white rounded-lg border border-gray-200 p-4 space-y-4">
            <h3 className="text-sm font-semibold text-gray-500 uppercase tracking-wider">Metadata</h3>
            <div>
              <p className="text-xs text-gray-400 mb-1">Category</p>
              <p className="text-sm font-medium text-gray-800">{article.category || '—'}</p>
            </div>
            <div>
              <p className="text-xs text-gray-400 mb-1">Sentiment</p>
              <Badge variant={sentimentVariant(article.sentiment)}>{article.sentiment || 'Unknown'}</Badge>
            </div>
            <div>
              <p className="text-xs text-gray-400 mb-1">Language</p>
              <p className="text-sm font-medium text-gray-800 uppercase">{article.language || '—'}</p>
            </div>
            {article.tags && article.tags.length > 0 && (
              <div>
                <p className="text-xs text-gray-400 mb-2">Tags</p>
                <div className="flex flex-wrap gap-1">
                  {article.tags.map(tag => (
                    <span key={tag} className="px-2 py-0.5 bg-gray-100 text-gray-600 rounded text-xs">{tag}</span>
                  ))}
                </div>
              </div>
            )}
            <div>
              <p className="text-xs text-gray-400 mb-1">Processed At</p>
              <p className="text-sm text-gray-700">{formatDate(article.processedAt)}</p>
            </div>
            {article.modelVersion && (
              <div>
                <p className="text-xs text-gray-400 mb-1">Model Version</p>
                <p className="text-sm font-mono text-gray-700">{article.modelVersion}</p>
              </div>
            )}
          </div>

          {/* Source */}
          {article.source && (
            <div className="bg-white rounded-lg border border-gray-200 p-4 space-y-3">
              <h3 className="text-sm font-semibold text-gray-500 uppercase tracking-wider">Original Source</h3>
              {article.source.title && (
                <p className="text-sm font-medium text-gray-800">{article.source.title}</p>
              )}
              <p className="text-xs text-gray-500">{formatDate(article.source.publishedAt)}</p>
              {article.source.originalUrl && (
                <a
                  href={article.source.originalUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-1 text-xs text-indigo-600 hover:text-indigo-800 transition-colors"
                >
                  View original
                  <ExternalLink className="w-3 h-3" />
                </a>
              )}
            </div>
          )}

          {/* Event */}
          <div className="bg-white rounded-lg border border-gray-200 p-4 space-y-3">
            <h3 className="text-sm font-semibold text-gray-500 uppercase tracking-wider">Event</h3>
            {article.event ? (
              <>
                <p className="text-sm font-medium text-gray-800">{article.event.eventTitle}</p>
                <div className="flex gap-2 flex-wrap">
                  <Badge variant={eventStatusVariant(article.event.eventStatus)}>
                    {article.event.eventStatus}
                  </Badge>
                  <Badge variant={eventRoleVariant(article.event.role)}>
                    {article.event.role}
                  </Badge>
                </div>
                <Link
                  to={`/events/${article.event.eventId}`}
                  className="inline-block text-xs text-indigo-600 hover:text-indigo-800 transition-colors"
                >
                  View Event →
                </Link>
              </>
            ) : (
              <p className="text-sm text-gray-400 italic">Pending classification</p>
            )}
          </div>
        </div>
      </div>

      {/* Sticky action bar */}
      <div className={cn(
        'fixed bottom-0 right-0 bg-white border-t border-gray-200 px-6 py-4 flex items-center justify-end gap-3 z-10 transition-all duration-200',
        collapsed ? 'left-16' : 'left-60'
      )}>
      <Button
        variant="secondary"
        className="border-red-300 text-red-600 hover:bg-red-50"
        onClick={() => setRejectOpen(true)}
      >
        Reject
      </Button>
      <Button onClick={() => setApproveOpen(true)}>
        Approve
      </Button>
      </div>

      <ApproveModal isOpen={approveOpen} onClose={() => setApproveOpen(false)} articleId={id!} />
      <RejectModal isOpen={rejectOpen} onClose={() => setRejectOpen(false)} articleId={id!} />
    </div>
  )
}
