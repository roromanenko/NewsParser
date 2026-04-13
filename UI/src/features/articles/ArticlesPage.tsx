import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { Search } from 'lucide-react'
import { useArticles } from './useArticles'
import { Pagination } from '@/components/shared/Pagination'
import type { ArticleListItemDto } from '@/api/generated'

const SENTIMENT_FILTERS = ['all', 'positive', 'negative', 'neutral'] as const
type SentimentFilter = (typeof SENTIMENT_FILTERS)[number]

const SORT_OPTIONS = ['newest', 'oldest'] as const
type SortOption = (typeof SORT_OPTIONS)[number]

function sentimentColor(sentiment?: string | null): string {
  switch (sentiment?.toLowerCase()) {
    case 'positive': return 'var(--caramel)'
    case 'negative': return 'var(--crimson)'
    default: return 'var(--rust)'
  }
}

function sentimentLabel(sentiment?: string | null): string {
  return sentiment?.toUpperCase() ?? 'UNKNOWN'
}

function formatTimestamp(iso?: string): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

const PAGE_SIZE = 20
const DEBOUNCE_MS = 300

export function ArticlesPage() {
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [sortBy, setSortBy] = useState<SortOption>('newest')
  const [filterSentiment, setFilterSentiment] = useState<SentimentFilter>('all')
  const navigate = useNavigate()

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), DEBOUNCE_MS)
    return () => clearTimeout(timer)
  }, [search])

  useEffect(() => {
    setPage(1)
  }, [debouncedSearch, sortBy])

  const { data, isLoading } = useArticles(page, PAGE_SIZE, debouncedSearch, sortBy)

  const items = data?.items ?? []

  const filtered = items.filter(article =>
    filterSentiment === 'all' || article.sentiment?.toLowerCase() === filterSentiment
  )

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
              placeholder="Search articles..."
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

        <div className="mb-6 space-y-1">
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

        <div className="space-y-1">
          <div className="font-caps text-xs tracking-widest mb-3" style={{ color: 'var(--caramel)' }}>
            SENTIMENT
          </div>
          {SENTIMENT_FILTERS.map(s => (
            <button
              key={s}
              onClick={() => setFilterSentiment(s)}
              className="w-full text-left px-3 py-2 font-mono text-xs transition-colors"
              style={{
                background: filterSentiment === s ? 'var(--burgundy)' : 'transparent',
                color: filterSentiment === s ? '#E8E8E8' : '#9ca3af',
              }}
              onMouseEnter={e => {
                if (filterSentiment !== s) e.currentTarget.style.color = 'var(--caramel)'
              }}
              onMouseLeave={e => {
                if (filterSentiment !== s) e.currentTarget.style.color = '#9ca3af'
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
            {data?.totalCount ?? 0} articles
          </div>
        </div>
      </aside>

      {/* Center panel – article list */}
      <div className="flex-1 overflow-y-auto">
        <div className="p-8">
          {/* Header */}
          <div
            className="mb-6 flex items-center justify-between border-b pb-4"
            style={{ borderColor: 'rgba(255,255,255,0.1)' }}
          >
            <div>
              <h1 className="font-display text-4xl mb-1">Articles</h1>
              <p className="font-mono text-sm" style={{ color: '#9ca3af' }}>
                {data?.totalCount ?? 0} items
              </p>
            </div>
          </div>

          {/* Article cards */}
          {isLoading ? (
            <div className="space-y-4">
              {Array.from({ length: 5 }).map((_, i) => (
                <div
                  key={i}
                  className="h-40 animate-pulse"
                  style={{ background: 'rgba(61,15,15,0.4)', border: '1px solid rgba(255,255,255,0.1)' }}
                />
              ))}
            </div>
          ) : filtered.length === 0 ? (
            <div className="font-mono text-sm text-center py-16" style={{ color: '#9ca3af' }}>
              No articles found.
            </div>
          ) : (
            <div className="space-y-4">
              {filtered.map(article => (
                <ArticleCard
                  key={article.id}
                  article={article}
                  onClick={() => article.id && navigate(`/articles/${article.id}`)}
                />
              ))}
            </div>
          )}

          {/* Pagination */}
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

interface ArticleCardProps {
  article: ArticleListItemDto
  onClick: () => void
}

function ArticleCard({ article, onClick }: ArticleCardProps) {
  const color = sentimentColor(article.sentiment)

  return (
    <article
      onClick={onClick}
      className="relative border p-6 cursor-pointer transition-all group"
      style={{
        background: 'rgba(61,15,15,0.4)',
        borderColor: 'rgba(255,255,255,0.1)',
      }}
      onMouseEnter={e => (e.currentTarget.style.borderColor = 'var(--caramel)')}
      onMouseLeave={e => (e.currentTarget.style.borderColor = 'rgba(255,255,255,0.1)')}
    >
      {/* Status bar */}
      <div
        className="absolute left-0 top-0 bottom-0 w-1"
        style={{ backgroundColor: color }}
      />

      {/* Top row */}
      <div className="flex items-start justify-between mb-3">
        <div className="flex items-center gap-3">
          <span className="font-caps text-xs tracking-widest" style={{ color }}>
            {sentimentLabel(article.sentiment)}
          </span>
          {article.category && (
            <span
              className="px-2 py-0.5 font-mono text-[10px]"
              style={{ background: 'var(--near-black)', color: '#9ca3af' }}
            >
              {article.category}
            </span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <span className="font-mono text-xs" style={{ color: '#6b7280' }}>
            {formatTimestamp(article.processedAt)}
          </span>
          {article.language && (
            <span
              className="px-2 py-0.5 font-mono text-[10px] uppercase"
              style={{ background: 'var(--near-black)', color: '#9ca3af' }}
            >
              {article.language}
            </span>
          )}
        </div>
      </div>

      {/* Title */}
      <h2
        className="font-display text-2xl mb-3 transition-colors"
        style={{ color: '#E8E8E8' }}
        onMouseEnter={e => (e.currentTarget.style.color = 'var(--caramel)')}
        onMouseLeave={e => (e.currentTarget.style.color = '#E8E8E8')}
      >
        {article.title || '—'}
      </h2>

      {/* Summary */}
      {article.summary && (
        <p className="font-mono text-sm leading-relaxed mb-4" style={{ color: '#9ca3af' }}>
          {article.summary}
        </p>
      )}

      {/* Tags */}
      {article.tags && article.tags.length > 0 && (
        <div className="flex flex-wrap gap-1 mb-4">
          {article.tags.slice(0, 4).map(tag => (
            <span
              key={tag}
              className="px-2 py-0.5 font-mono text-[10px]"
              style={{ background: 'var(--near-black)', color: '#9ca3af', border: '1px solid rgba(255,255,255,0.1)' }}
            >
              {tag}
            </span>
          ))}
        </div>
      )}

      {/* Footer */}
      <div
        className="flex items-center justify-end pt-4 border-t"
        style={{ borderColor: 'rgba(255,255,255,0.1)' }}
      >
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
          VIEW ARTICLE
        </button>
      </div>
    </article>
  )
}
