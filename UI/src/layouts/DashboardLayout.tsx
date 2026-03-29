import { useState } from 'react'
import { Outlet } from 'react-router-dom'
import { Sidebar } from './Sidebar'
import { SidebarContext } from './SidebarContext'
import { useCurrentUser } from '@/hooks/useCurrentUser'

export function DashboardLayout() {
  const [collapsed, setCollapsed] = useState(false)
  const user = useCurrentUser()

  const today = new Date().toLocaleDateString('en-US', {
    month: 'long',
    day: 'numeric',
    year: 'numeric',
  })

  return (
    <SidebarContext.Provider value={{ collapsed }}>
      <div className="flex h-screen overflow-hidden" style={{ backgroundColor: 'var(--near-black)' }}>
        <Sidebar collapsed={collapsed} onToggle={() => setCollapsed(c => !c)} />
        <div className="flex-1 flex flex-col overflow-hidden">
          {/* Top navbar */}
          <header
            className="h-20 shrink-0 sticky top-0 z-30 backdrop-blur-sm border-b"
            style={{
              backgroundColor: 'rgba(61, 15, 15, 0.5)',
              borderColor: 'rgba(255, 255, 255, 0.1)',
            }}
          >
            <div className="h-full px-8 flex items-center justify-between">
              <div className="flex items-center gap-6">
                <div>
                  <div className="font-caps text-xs tracking-wider" style={{ color: 'var(--caramel)' }}>
                    EDITOR'S DESK
                  </div>
                  <div className="font-mono text-sm text-gray-400">
                    {user?.role ?? 'Editor'}
                  </div>
                </div>
                <div className="h-8 w-px" style={{ backgroundColor: 'rgba(255,255,255,0.1)' }} />
                <div>
                  <div className="font-caps text-xs tracking-wider" style={{ color: 'var(--caramel)' }}>
                    ISSUE DATE
                  </div>
                  <div className="font-mono text-sm text-gray-400">{today}</div>
                </div>
              </div>

              <div className="flex items-center gap-6">
                <div className="text-right">
                  <div className="font-caps text-xs tracking-wider" style={{ color: 'var(--caramel)' }}>
                    PENDING REVIEW
                  </div>
                  <div className="font-display text-2xl" style={{ color: 'var(--crimson)' }}>
                    —
                  </div>
                </div>
              </div>
            </div>
          </header>

          <main className="flex-1 overflow-y-auto p-6">
            <Outlet />
          </main>
        </div>
      </div>
    </SidebarContext.Provider>
  )
}
