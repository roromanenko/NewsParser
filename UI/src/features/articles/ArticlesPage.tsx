import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useArticles } from './useArticles'
import { PageHeader } from '@/components/shared/PageHeader'
import { DataTable, type ColumnDef } from '@/components/shared/DataTable'
import { Pagination } from '@/components/shared/Pagination'
import { Badge } from '@/components/ui/Badge'
import type { ArticleListItemDto } from '@/api/generated'

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

const PAGE_SIZE = 20

export function ArticlesPage() {
  const [page, setPage] = useState(1)
  const navigate = useNavigate()
  const { data, isLoading } = useArticles(page, PAGE_SIZE)

  const columns: ColumnDef<ArticleListItemDto>[] = [
    {
      key: 'title',
      header: 'Title',
      render: row => (
        <span className="font-medium text-gray-900 line-clamp-2 max-w-xs">{row.title || '—'}</span>
      ),
    },
    {
      key: 'category',
      header: 'Category',
      render: row => <span className="text-gray-600">{row.category || '—'}</span>,
    },
    {
      key: 'sentiment',
      header: 'Sentiment',
      render: row => (
        <Badge variant={sentimentVariant(row.sentiment)}>
          {row.sentiment || 'Unknown'}
        </Badge>
      ),
    },
    {
      key: 'language',
      header: 'Language',
      render: row => <span className="text-gray-600 uppercase text-xs">{row.language || '—'}</span>,
    },
    {
      key: 'processedAt',
      header: 'Date',
      render: row => <span className="text-gray-500 text-xs">{formatDate(row.processedAt)}</span>,
    },
  ]

  return (
    <div>
      <PageHeader
        title="Articles"
        description={data ? `${data.totalCount ?? 0} articles total` : undefined}
      />
      <DataTable<ArticleListItemDto>
        columns={columns}
        data={data?.items ?? []}
        isLoading={isLoading}
        emptyMessage="No articles found."
        keyExtractor={row => row.id ?? ''}
        onRowClick={row => row.id && navigate(`/articles/${row.id}`)}
      />
      {data && (
        <Pagination
          page={page}
          totalPages={data.totalPages ?? 1}
          hasNextPage={data.hasNextPage ?? false}
          hasPreviousPage={data.hasPreviousPage ?? false}
          onPageChange={setPage}
        />
      )}
    </div>
  )
}
