import { useParams, useNavigate, Link } from 'react-router-dom'
import { ArrowLeft, ExternalLink } from 'lucide-react'
import { useArticleDetail } from './useArticleDetail'
import { MediaGallery } from '@/components/shared/MediaGallery'
import type { MediaItem } from '@/components/shared/MediaGallery'

function sentimentColor(s?: string | null): string {
  const lower = s?.toLowerCase()
  if (lower === 'positive') return 'var(--caramel)'
  if (lower === 'negative') return 'var(--rust)'
  return '#6b7280'
}

function roleColor(role?: string | null): string {
  if (role === 'Initiator') return 'var(--caramel)'
  if (role === 'Contradiction') return 'var(--rust)'
  return '#6b7280'
}

function formatDate(iso?: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

export function ArticleDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: article, isLoading } = useArticleDetail(id!)

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="font-mono text-sm animate-pulse" style={{ color: '#9ca3af' }}>
          Loading…
        </div>
      </div>
    )
  }

  if (!article) {
    return (
      <div className="font-mono text-sm text-center py-16" style={{ color: '#9ca3af' }}>
        Article not found.
      </div>
    )
  }

  const keyFacts = article.keyFacts ?? []
  const tags = article.tags ?? []
  const mediaItems: MediaItem[] = (article.media ?? []).map(m => ({
    id: m.id!,
    url: m.url!,
    kind: m.kind as 'Image' | 'Video',
    contentType: m.contentType!,
  }))

  const heroImage = mediaItems.find(m => m.kind === 'Image')

  return (
    <div className="max-w-5xl">
      {/* Back */}
      <button
        onClick={() => navigate('/articles')}
        className="flex items-center gap-2 font-mono text-xs mb-6 transition-colors"
        style={{ color: '#6b7280' }}
        onMouseEnter={e => (e.currentTarget.style.color = 'var(--caramel)')}
        onMouseLeave={e => (e.currentTarget.style.color = '#6b7280')}
      >
        <ArrowLeft className="w-4 h-4" />
        BACK TO ARTICLES
      </button>

      {/* Header card */}
      <div
        className="relative border mb-6 overflow-hidden"
        style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
      >
        <div className="absolute left-0 top-0 bottom-0 w-1 z-10" style={{ backgroundColor: 'var(--caramel)' }} />

        {/* Hero image */}
        {heroImage && (
          <div className="relative w-full" style={{ maxHeight: '360px', overflow: 'hidden' }}>
            <img
              src={heroImage.url}
              alt=""
              className="w-full object-cover"
              style={{ maxHeight: '360px', opacity: 0.85 }}
            />
            <div
              className="absolute inset-0"
              style={{ background: 'linear-gradient(to bottom, rgba(61,15,15,0) 40%, rgba(61,15,15,0.95) 100%)' }}
            />
          </div>
        )}

        <div className="p-6">
          {/* Top row: source date + external link */}
          <div className="flex items-center justify-between gap-4 mb-4">
            <span className="font-mono text-xs" style={{ color: '#6b7280' }}>
              {formatDate(article.publishedAt ?? article.processedAt)}
            </span>
            {article.originalUrl && (
              <a
                href={article.originalUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="flex items-center gap-1 font-mono text-xs transition-colors"
                style={{ color: '#6b7280' }}
                onMouseEnter={e => (e.currentTarget.style.color = 'var(--caramel)')}
                onMouseLeave={e => (e.currentTarget.style.color = '#6b7280')}
              >
                SOURCE
                <ExternalLink className="w-3 h-3" />
              </a>
            )}
          </div>

          <h1 className="font-display text-4xl mb-4" style={{ color: '#E8E8E8' }}>
            {article.title}
          </h1>

          {/* Stats row */}
          <div
            className="flex gap-3 pt-4 border-t flex-wrap"
            style={{ borderColor: 'rgba(255,255,255,0.1)' }}
          >
            {article.category && (
              <div className="px-3 py-1.5" style={{ background: 'var(--near-black)' }}>
                <span className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>
                  CATEGORY{' '}
                </span>
                <span className="font-mono text-sm" style={{ color: '#E8E8E8' }}>
                  {article.category}
                </span>
              </div>
            )}
            {article.language && (
              <div className="px-3 py-1.5" style={{ background: 'var(--near-black)' }}>
                <span className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>
                  LANG{' '}
                </span>
                <span className="font-mono text-sm uppercase" style={{ color: '#E8E8E8' }}>
                  {article.language}
                </span>
              </div>
            )}
            {article.sentiment && (
              <div className="px-3 py-1.5" style={{ background: 'var(--near-black)' }}>
                <span className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>
                  SENTIMENT{' '}
                </span>
                <span
                  className="font-caps text-xs tracking-widest"
                  style={{ color: sentimentColor(article.sentiment) }}
                >
                  {article.sentiment.toUpperCase()}
                </span>
              </div>
            )}
          </div>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Left: Content */}
        <div className="lg:col-span-2 space-y-4">
          {/* Summary */}
          {article.summary && (
            <div
              className="border p-5"
              style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
            >
              <p className="font-caps text-[10px] tracking-widest mb-3" style={{ color: '#6b7280' }}>
                SUMMARY
              </p>
              <p className="font-mono text-sm leading-relaxed" style={{ color: '#9ca3af' }}>
                {article.summary}
              </p>
            </div>
          )}

          {/* Media gallery */}
          {mediaItems.length > 0 && (
            <MediaGallery items={mediaItems} />
          )}

          {/* Key Facts */}
          {keyFacts.length > 0 && (
            <div
              className="border p-5"
              style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
            >
              <p className="font-caps text-[10px] tracking-widest mb-3" style={{ color: '#6b7280' }}>
                KEY FACTS
              </p>
              <ul className="space-y-2">
                {keyFacts.map((fact, i) => (
                  <li key={i} className="flex items-start gap-3">
                    <span
                      className="font-mono text-xs shrink-0 mt-0.5"
                      style={{ color: 'var(--caramel)' }}
                    >
                      {String(i + 1).padStart(2, '0')}
                    </span>
                    <span className="font-mono text-sm leading-relaxed" style={{ color: '#E8E8E8' }}>
                      {fact}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          )}

        </div>

        {/* Right: Sidebar */}
        <div className="space-y-4">
          {/* Tags */}
          {tags.length > 0 && (
            <div
              className="border p-4"
              style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
            >
              <p className="font-caps text-[10px] tracking-widest mb-3" style={{ color: '#6b7280' }}>
                TAGS
              </p>
              <div className="flex flex-wrap gap-2">
                {tags.map(tag => (
                  <span
                    key={tag}
                    className="px-2 py-1 font-mono text-xs"
                    style={{
                      background: 'var(--near-black)',
                      color: '#9ca3af',
                      border: '1px solid rgba(255,255,255,0.1)',
                    }}
                  >
                    {tag}
                  </span>
                ))}
              </div>
            </div>
          )}

          {/* AI metadata */}
          <div
            className="border p-4 space-y-3"
            style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
          >
            <p className="font-caps text-[10px] tracking-widest" style={{ color: '#6b7280' }}>
              AI ANALYSIS
            </p>
            <div className="space-y-2">
              <div>
                <p className="font-caps text-[10px] tracking-widest mb-0.5" style={{ color: '#6b7280' }}>
                  PROCESSED
                </p>
                <p className="font-mono text-xs" style={{ color: '#9ca3af' }}>
                  {formatDate(article.processedAt)}
                </p>
              </div>
              {article.modelVersion && (
                <div>
                  <p className="font-caps text-[10px] tracking-widest mb-0.5" style={{ color: '#6b7280' }}>
                    MODEL
                  </p>
                  <p className="font-mono text-xs" style={{ color: '#9ca3af' }}>
                    {article.modelVersion}
                  </p>
                </div>
              )}
            </div>
          </div>

          {/* Event */}
          <div
            className="border p-4"
            style={{ background: 'rgba(61,15,15,0.4)', borderColor: 'rgba(255,255,255,0.1)' }}
          >
            <p className="font-caps text-[10px] tracking-widest mb-3" style={{ color: '#6b7280' }}>
              EVENT
            </p>
            {article.event ? (
              <div className="space-y-2">
                <p className="font-mono text-sm" style={{ color: '#E8E8E8' }}>
                  {article.event.eventTitle}
                </p>
                <div className="flex items-center gap-3">
                  <span
                    className="font-caps text-xs tracking-widest"
                    style={{ color: roleColor(article.event.role) }}
                  >
                    {article.event.role?.toUpperCase() ?? '—'}
                  </span>
                  <span className="font-mono text-xs" style={{ color: '#6b7280' }}>
                    {article.event.eventStatus?.toUpperCase()}
                  </span>
                </div>
                <Link
                  to={`/events/${article.event.eventId}`}
                  className="inline-flex items-center gap-1 font-mono text-xs transition-colors"
                  style={{ color: '#6b7280' }}
                  onMouseEnter={e => (e.currentTarget.style.color = 'var(--caramel)')}
                  onMouseLeave={e => (e.currentTarget.style.color = '#6b7280')}
                >
                  VIEW EVENT →
                </Link>
              </div>
            ) : (
              <p className="font-mono text-xs" style={{ color: '#6b7280' }}>
                Pending classification
              </p>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
