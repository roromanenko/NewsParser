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
    <div
      className={cn('flex flex-col text-white transition-all duration-200 shrink-0', collapsed ? 'w-16' : 'w-60')}
      style={{ backgroundColor: 'var(--burgundy)' }}
    >
      {/* Logo */}
      <div
        className="flex items-center justify-between h-16 px-4 border-b"
        style={{ borderColor: 'rgba(255,255,255,0.1)' }}
      >
        {!collapsed && (
          <span className="font-caps text-lg tracking-wide truncate" style={{ color: 'var(--crimson)' }}>
            Panoptis
          </span>
        )}
        {collapsed && (
          <span className="font-caps text-lg mx-auto" style={{ color: 'var(--crimson)' }}>
            P
          </span>
        )}
        {!collapsed && (
          <button
            onClick={onToggle}
            className="text-gray-400 hover:text-white transition-colors ml-auto"
            title="Collapse sidebar"
          >
            <ChevronLeft className="w-5 h-5" />
          </button>
        )}
        {collapsed && (
          <button
            onClick={onToggle}
            className="text-gray-400 hover:text-white transition-colors"
            title="Expand sidebar"
          >
            <ChevronRight className="w-5 h-5" />
          </button>
        )}
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
                'relative flex items-center gap-3 px-3 py-2.5 transition-colors',
                isActive
                  ? 'text-white border-l-2'
                  : 'text-gray-400 hover:text-white border-l-2 border-transparent'
              )
            }
            style={({ isActive }) => ({
              borderLeftColor: isActive ? 'var(--crimson)' : 'transparent',
              backgroundColor: isActive ? 'rgba(255,255,255,0.05)' : undefined,
            })}
          >
            {({ isActive }) => (
              <>
                <Icon className="w-5 h-5 shrink-0" />
                {!collapsed && (
                  <span
                    className="text-sm font-mono truncate"
                    style={{ color: isActive ? '#fff' : undefined }}
                  >
                    {label}
                  </span>
                )}
              </>
            )}
          </NavLink>
        ))}
      </nav>

      {/* User footer */}
      <div
        className="mt-auto border-t p-3"
        style={{ borderColor: 'rgba(255,255,255,0.1)' }}
      >
        {!collapsed && user && (
          <div className="mb-2 px-1">
            <p className="text-xs text-gray-400 truncate font-mono">{user.email}</p>
            <Badge variant={user.role === 'Admin' ? 'admin' : 'info'} className="mt-1">
              {user.role}
            </Badge>
          </div>
        )}
        <button
          onClick={handleLogout}
          title="Logout"
          className="flex items-center gap-3 w-full px-3 py-2 text-gray-400 hover:text-white transition-colors"
        >
          <LogOut className="w-5 h-5 shrink-0" />
          {!collapsed && <span className="text-sm font-mono">Logout</span>}
        </button>
      </div>
    </div>
  )
}
