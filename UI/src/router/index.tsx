import { BrowserRouter, Routes, Route, Navigate, useParams } from 'react-router-dom'
import { useAuthStore } from '@/store/authStore'
import { useProjectStore } from '@/store/projectStore'
import { ToastProvider } from '@/context/ToastContext'
import { AuthLayout } from '@/layouts/AuthLayout'
import { DashboardLayout } from '@/layouts/DashboardLayout'
import { LoginPage } from '@/features/auth/LoginPage'
import { RegisterPage } from '@/features/auth/RegisterPage'
import { ArticlesPage } from '@/features/articles/ArticlesPage'
import { ArticleDetailPage } from '@/features/articles/ArticleDetailPage'
import { SourcesPage } from '@/features/sources/SourcesPage'
import { UsersPage } from '@/features/users/UsersPage'
import { PublishTargetsPage } from '@/features/publishTargets/PublishTargetsPage'
import { EventsPage } from '@/features/events/EventsPage'
import { EventDetailPage } from '@/features/events/EventDetailPage'
import { PublicationDetailPage } from '@/features/publications/PublicationDetailPage'
import { PublicationsPage } from '@/features/publications/PublicationsPage'
import { AiOperationsPage } from '@/features/aiOperations/AiOperationsPage'
import { ProjectsPage } from '@/features/projects/ProjectsPage'
import { useProjects } from '@/features/projects/useProjects'

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const user = useAuthStore(state => state.user)
  return user ? <>{children}</> : <Navigate to="/login" replace />
}

function AdminRoute({ children }: { children: React.ReactNode }) {
  const user = useAuthStore(state => state.user)
  if (!user) return <Navigate to="/login" replace />
  if (user.role !== 'Admin') return <Navigate to="/projects" replace />
  return <>{children}</>
}

function ProjectRoute({ children }: { children: React.ReactNode }) {
  const { projectId } = useParams<{ projectId: string }>()
  const { data: projects } = useProjects()
  const { selectedProjectId } = useProjectStore()

  if (!projects) return null

  const isValid = projects.some(p => p.id === projectId)
  if (!isValid) {
    const fallbackId = selectedProjectId ?? projects[0]?.id
    if (fallbackId) {
      return <Navigate to={`/projects/${fallbackId}/articles`} replace />
    }
  }

  return <>{children}</>
}

function RootRedirect() {
  const { selectedProjectId } = useProjectStore()
  const { data: projects } = useProjects()

  const defaultId = selectedProjectId ?? projects?.[0]?.id
  if (!defaultId) return null

  return <Navigate to={`/projects/${defaultId}/articles`} replace />
}

export function AppRouter() {
  return (
    <ToastProvider>
      <BrowserRouter>
        <Routes>
          <Route
            path="/login"
            element={
              <AuthLayout>
                <LoginPage />
              </AuthLayout>
            }
          />
          <Route
            path="/register"
            element={
              <AuthLayout>
                <RegisterPage />
              </AuthLayout>
            }
          />
          <Route
            path="/"
            element={
              <ProtectedRoute>
                <DashboardLayout />
              </ProtectedRoute>
            }
          >
            <Route index element={<RootRedirect />} />
            <Route
              path="projects"
              element={
                <AdminRoute>
                  <ProjectsPage />
                </AdminRoute>
              }
            />
            <Route path="projects/:projectId" element={<ProjectRoute><RootRedirect /></ProjectRoute>} />
            <Route
              path="projects/:projectId/articles"
              element={
                <ProjectRoute>
                  <ArticlesPage />
                </ProjectRoute>
              }
            />
            <Route
              path="projects/:projectId/articles/:id"
              element={
                <ProjectRoute>
                  <ArticleDetailPage />
                </ProjectRoute>
              }
            />
            <Route
              path="projects/:projectId/events"
              element={
                <ProjectRoute>
                  <EventsPage />
                </ProjectRoute>
              }
            />
            <Route
              path="projects/:projectId/events/:id"
              element={
                <ProjectRoute>
                  <EventDetailPage />
                </ProjectRoute>
              }
            />
            <Route
              path="projects/:projectId/publications"
              element={
                <ProjectRoute>
                  <PublicationsPage />
                </ProjectRoute>
              }
            />
            <Route
              path="projects/:projectId/publications/:id"
              element={
                <ProjectRoute>
                  <PublicationDetailPage />
                </ProjectRoute>
              }
            />
            <Route
              path="projects/:projectId/sources"
              element={
                <ProjectRoute>
                  <AdminRoute>
                    <SourcesPage />
                  </AdminRoute>
                </ProjectRoute>
              }
            />
            <Route
              path="projects/:projectId/publish-targets"
              element={
                <ProjectRoute>
                  <AdminRoute>
                    <PublishTargetsPage />
                  </AdminRoute>
                </ProjectRoute>
              }
            />
            <Route
              path="projects/:projectId/ai-operations"
              element={
                <ProjectRoute>
                  <AdminRoute>
                    <AiOperationsPage />
                  </AdminRoute>
                </ProjectRoute>
              }
            />
            <Route
              path="users"
              element={
                <AdminRoute>
                  <UsersPage />
                </AdminRoute>
              }
            />
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </ToastProvider>
  )
}
