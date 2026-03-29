import { NavLink, useNavigate } from 'react-router-dom'
import { Newspaper, Database, Users, LogOut, ChevronLeft, ChevronRight, Send, Network } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useAuthStore } from '@/store/authStore'
import { usePermissions } from '@/hooks/usePermissions'
import { useCurrentUser } from '@/hooks/useCurrentUser'
import { Badge } from '@/components/ui/Badge'

interface SidebarProps {
  collapsed: boolean
  onToggle: () => void
}

const navItems = [
  { to: '/articles', icon: Newspaper, label: 'Articles', adminOnly: false },
  { to: '/events', icon: Network, label: 'Events', adminOnly: false },
  { to: '/sources', icon: Database, label: 'Sources', adminOnly: true },
  { to: '/publish-targets', icon: Send, label: 'Publish Targets', adminOnly: true },
  { to: '/users', icon: Users, label: 'Users', adminOnly: true },
]

export function Sidebar({ collapsed, onToggle }: SidebarProps) {
  const { isAdmin } = usePermissions()
  const user = useCurrentUser()
  const logout = useAuthStore(state => state.logout)
  const navigate = useNavigate()

  const handleLogout = () => {
    logout()
    navigate('/login')
  }

  const visibleItems = navItems.filter(item => !item.adminOnly || isAdmin)

  return (
    <div className={cn('flex flex-col bg-slate-900 text-white transition-all duration-200 shrink-0', collapsed ? 'w-16' : 'w-60')}>
      {/* Logo */}
      <div className="flex items-center justify-between h-16 px-4 border-b border-slate-700">
        {!collapsed && <span className="font-bold text-sm tracking-wide truncate">NewsParser CMS</span>}
        <button
          onClick={onToggle}
          className="text-slate-400 hover:text-white transition-colors ml-auto"
          title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
        >
          {collapsed ? <ChevronRight className="w-5 h-5" /> : <ChevronLeft className="w-5 h-5" />}
        </button>
      </div>

      {/* Nav */}
      <nav className="flex-1 py-4 px-2 space-y-1">
        {visibleItems.map(({ to, icon: Icon, label }) => (
          <NavLink
            key={to}
            to={to}
            title={label}
            className={({ isActive }) =>
              cn(
                'flex items-center gap-3 px-3 py-2.5 rounded-md transition-colors',
                isActive
                  ? 'bg-indigo-600 text-white'
                  : 'text-slate-300 hover:bg-slate-800 hover:text-white'
              )
            }
          >
            <Icon className="w-5 h-5 shrink-0" />
            {!collapsed && <span className="text-sm font-medium truncate">{label}</span>}
          </NavLink>
        ))}
      </nav>

      {/* User footer */}
      <div className="mt-auto border-t border-slate-700 p-3">
        {!collapsed && user && (
          <div className="mb-2 px-1">
            <p className="text-xs text-slate-400 truncate">{user.email}</p>
            <Badge variant={user.role === 'Admin' ? 'admin' : 'info'} className="mt-1">
              {user.role}
            </Badge>
          </div>
        )}
        <button
          onClick={handleLogout}
          title="Logout"
          className="flex items-center gap-3 w-full px-3 py-2 rounded-md text-slate-300 hover:bg-slate-800 hover:text-white transition-colors"
        >
          <LogOut className="w-5 h-5 shrink-0" />
          {!collapsed && <span className="text-sm font-medium">Logout</span>}
        </button>
      </div>
    </div>
  )
}
