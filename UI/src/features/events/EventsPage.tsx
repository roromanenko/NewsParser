import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useEvents } from './useEvents'
import { MergeEventsSlideOver } from './MergeEventsSlideOver'
import { PageHeader } from '@/components/shared/PageHeader'
import { DataTable, type ColumnDef } from '@/components/shared/DataTable'
import { Pagination } from '@/components/shared/Pagination'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { usePermissions } from '@/hooks/usePermissions'
import type { EventListItemDto } from '@/api/generated'

const PAGE_SIZE = 20

function formatDate(iso?: string) {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

function statusVariant(status?: string | null): 'info' | 'neutral' | 'warning' {
  if (status === 'Active') return 'info'
  if (status === 'Archived') return 'neutral'
  return 'neutral'
}

export function EventsPage() {
  const navigate = useNavigate()
  const { isAdmin } = usePermissions()
  const [page, setPage] = useState(1)
  const [mergeOpen, setMergeOpen] = useState(false)

  const { data, isLoading } = useEvents(page, PAGE_SIZE)
  const events = data?.items ?? []

  const columns: ColumnDef<EventListItemDto>[] = [
    {
      key: 'title',
      header: 'Title',
      render: row => <span className="font-medium text-gray-900">{row.title}</span>,
    },
    {
      key: 'status',
      header: 'Status',
      render: row => <Badge variant={statusVariant(row.status)}>{row.status ?? '—'}</Badge>,
      className: 'w-28',
    },
    {
      key: 'articleCount',
      header: 'Articles',
      render: row => <span className="text-gray-700">{row.articleCount ?? 0}</span>,
      className: 'w-24 text-center',
    },
    {
      key: 'unresolvedContradictions',
      header: 'Contradictions',
      render: row =>
        (row.unresolvedContradictions ?? 0) > 0 ? (
          <Badge variant="warning">{row.unresolvedContradictions} unresolved</Badge>
        ) : (
          <span className="text-gray-400 text-xs">None</span>
        ),
      className: 'w-36',
    },
    {
      key: 'lastUpdatedAt',
      header: 'Last Updated',
      render: row => <span className="text-gray-500 text-sm">{formatDate(row.lastUpdatedAt)}</span>,
      className: 'w-36',
    },
  ]

  return (
    <div>
      <PageHeader
        title="Events"
        description={`${data?.totalCount ?? 0} events total`}
        action={
          isAdmin ? (
            <Button onClick={() => setMergeOpen(true)}>Merge Events</Button>
          ) : undefined
        }
      />

      <DataTable
        columns={columns}
        data={events}
        isLoading={isLoading}
        keyExtractor={row => row.id ?? ''}
        onRowClick={row => navigate(`/events/${row.id}`)}
        emptyMessage="No events found."
      />

      {(data?.totalPages ?? 1) > 1 && (
        <div className="mt-4">
          <Pagination
            page={page}
            totalPages={data?.totalPages ?? 1}
            hasNextPage={data?.hasNextPage ?? false}
            hasPreviousPage={data?.hasPreviousPage ?? false}
            onPageChange={setPage}
          />
        </div>
      )}

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
