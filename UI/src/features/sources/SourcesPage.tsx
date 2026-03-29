import { useState, useMemo } from 'react'
import { Pencil, Trash2, Plus, ExternalLink } from 'lucide-react'
import { useSources } from './useSources'
import { useSourceMutations } from './useSourceMutations'
import { SourceFormSlideOver } from './SourceFormSlideOver'
import { SourceStatsCards } from './SourceStatsCards'
import { SourceFilterBar } from './SourceFilterBar'
import { PageHeader } from '@/components/shared/PageHeader'
import { DataTable, type ColumnDef } from '@/components/shared/DataTable'
import { ConfirmDialog } from '@/components/shared/ConfirmDialog'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Toggle } from '@/components/ui/Toggle'
import type { SourceDto } from '@/api/generated'

function formatDate(iso?: string | null): string {
  if (!iso) return 'Never'
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

export function SourcesPage() {
  const { data: sources = [], isLoading } = useSources()
  const { updateSource, deleteSource } = useSourceMutations()
  const [slideOpen, setSlideOpen] = useState(false)
  const [editingSource, setEditingSource] = useState<SourceDto | undefined>()
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [typeFilter, setTypeFilter] = useState('all')
  const [statusFilter, setStatusFilter] = useState('all')

  const openAdd = () => { setEditingSource(undefined); setSlideOpen(true) }
  const openEdit = (source: SourceDto) => { setEditingSource(source); setSlideOpen(true) }
  const closeSlide = () => { setSlideOpen(false); setEditingSource(undefined) }

  const handleToggleActive = (source: SourceDto) => {
    if (!source.id) return
    updateSource.mutate({
      id: source.id,
      data: { name: source.name ?? '', url: source.url ?? '', isActive: !source.isActive },
    })
  }

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

  const isFiltered = search !== '' || typeFilter !== 'all' || statusFilter !== 'all'
  const description = isLoading
    ? undefined
    : isFiltered
    ? `${filteredSources.length} of ${sources.length} sources`
    : `${sources.length} source${sources.length !== 1 ? 's' : ''}`

  const columns: ColumnDef<SourceDto>[] = [
    {
      key: 'name',
      header: 'Name',
      render: row => <span className="font-medium text-gray-900">{row.name}</span>,
    },
    {
      key: 'url',
      header: 'URL',
      render: row => (
        <a
          href={row.url ?? '#'}
          target="_blank"
          rel="noopener noreferrer"
          className="inline-flex items-center gap-1 text-indigo-600 hover:text-indigo-800 text-xs"
          onClick={e => e.stopPropagation()}
        >
          <span className="truncate max-w-[200px]">{row.url}</span>
          <ExternalLink className="w-3 h-3 flex-shrink-0" />
        </a>
      ),
    },
    {
      key: 'type',
      header: 'Type',
      render: row => (
        <Badge variant={row.type === 'Rss' ? 'info' : 'neutral'}>{row.type ?? '—'}</Badge>
      ),
    },
    {
      key: 'isActive',
      header: 'Active',
      render: row => (
        <Toggle
          checked={row.isActive ?? false}
          onChange={() => handleToggleActive(row)}
          disabled={updateSource.isPending}
        />
      ),
    },
    {
      key: 'lastFetchedAt',
      header: 'Last Fetched',
      render: row => <span className="text-xs text-gray-500">{formatDate(row.lastFetchedAt)}</span>,
    },
    {
      key: 'actions',
      header: '',
      render: row => (
        <div className="flex items-center gap-2" onClick={e => e.stopPropagation()}>
          <button
            onClick={() => openEdit(row)}
            className="p-1.5 text-gray-400 hover:text-indigo-600 hover:bg-indigo-50 rounded transition-colors"
            title="Edit"
          >
            <Pencil className="w-4 h-4" />
          </button>
          <button
            onClick={() => row.id && setDeletingId(row.id)}
            className="p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded transition-colors"
            title="Delete"
          >
            <Trash2 className="w-4 h-4" />
          </button>
        </div>
      ),
    },
  ]

  return (
    <div>
      <PageHeader
        title="Sources"
        description={description}
        action={
          <Button leftIcon={<Plus className="w-4 h-4" />} onClick={openAdd}>
            Add Source
          </Button>
        }
      />
      <SourceStatsCards sources={sources} />
      <SourceFilterBar
        search={search}
        onSearchChange={setSearch}
        typeFilter={typeFilter}
        onTypeFilterChange={setTypeFilter}
        statusFilter={statusFilter}
        onStatusFilterChange={setStatusFilter}
      />
      <DataTable<SourceDto>
        columns={columns}
        data={filteredSources}
        isLoading={isLoading}
        emptyMessage={
          isFiltered
            ? 'No sources match your filters. Try adjusting your search.'
            : 'No sources configured. Add your first source.'
        }
        keyExtractor={row => row.id ?? ''}
      />
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
