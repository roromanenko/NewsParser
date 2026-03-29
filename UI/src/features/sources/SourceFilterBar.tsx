import { Search } from 'lucide-react'

interface Props {
  search: string
  onSearchChange: (v: string) => void
  typeFilter: string
  onTypeFilterChange: (v: string) => void
  statusFilter: string
  onStatusFilterChange: (v: string) => void
}

const selectClass =
  'rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-700 focus:outline-none focus:ring-1 focus:border-indigo-500 focus:ring-indigo-500'

export function SourceFilterBar({
  search,
  onSearchChange,
  typeFilter,
  onTypeFilterChange,
  statusFilter,
  onStatusFilterChange,
}: Props) {
  return (
    <div className="flex flex-wrap gap-3 mb-4 items-center">
      <div className="relative flex-1 min-w-48">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400 pointer-events-none" />
        <input
          type="text"
          placeholder="Search by name or URL..."
          value={search}
          onChange={e => onSearchChange(e.target.value)}
          className="w-full rounded-md border border-gray-300 pl-9 pr-3 py-2 text-sm text-gray-900 placeholder-gray-400 focus:outline-none focus:ring-1 focus:border-indigo-500 focus:ring-indigo-500"
        />
      </div>
      <select value={typeFilter} onChange={e => onTypeFilterChange(e.target.value)} className={selectClass}>
        <option value="all">All Types</option>
        <option value="Rss">RSS</option>
        <option value="Telegram">Telegram</option>
      </select>
      <select value={statusFilter} onChange={e => onStatusFilterChange(e.target.value)} className={selectClass}>
        <option value="all">All Statuses</option>
        <option value="active">Active</option>
        <option value="inactive">Inactive</option>
      </select>
    </div>
  )
}
