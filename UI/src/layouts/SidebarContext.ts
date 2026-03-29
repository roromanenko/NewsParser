import { createContext, useContext } from 'react'

interface SidebarContextValue {
  collapsed: boolean
}

export const SidebarContext = createContext<SidebarContextValue>({ collapsed: false })
export const useSidebar = () => useContext(SidebarContext)