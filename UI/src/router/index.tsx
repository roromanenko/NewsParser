import { BrowserRouter, Routes, Route, Navigate, useParams } from 'react-router-dom'
import { useEffect } from 'react'
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
  const { projectSlug } = useParams<{ projectSlug: string }>()
  const { data: projects } = useProjects()
  const { selectedProjectId, setProject } = useProjectStore()

  const project = projects?.find(p => p.slug === projectSlug)

  useEffect(() => {
    if (project?.id && project.id !== selectedProjectId) {
      setProject(project.id)
    }
  }, [project?.id, selectedProjectId, setProject])

  if (!projects) return null

  if (!project) {
    const fallback = projects.find(p => p.id === selectedProjectId) ?? projects[0]
    if (fallback?.slug) {
      return <Navigate to={`/projects/${fallback.slug}/articles`} replace />
    }
    return null
  }

  return <>{children}</>
}

function RootRedirect() {
  const { selectedProjectId } = useProjectStore()
  const { data: projects } = useProjects()

  const project = projects?.find(p => p.id === selectedProjectId) ?? projects?.[0]
  if (!project?.slug) return null

  return <Navigate to={`/projects/${project.slug}/articles`} replace />
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
            <Route path="projects/:projectSlug" element={<ProjectRoute><RootRedirect /></ProjectRoute>} />
            <Route
              path="projects/:projectSlug/articles"
              element={
                <ProjectRoute>
                  <ArticlesPage />
                </ProjectRoute>
              }
            />
            <Route
              path="projects/:projectSlug/articles/:id"
              element={
                <ProjectRoute>
                  <ArticleDetailPage />
                </ProjectRoute>
              }
            />
            <Route
              path="projects/:projectSlug/events"
              element={
                <ProjectRoute>
                  <EventsPage />
                </ProjectRoute>
              }
            />
            <Route
              path="projects/:projectSlug/events/:id"
              element={
                <ProjectRoute>
                  <EventDetailPage />
                </ProjectRoute>
              }
            />
            <Route
              path="projects/:projectSlug/publications"
              element={
                <ProjectRoute>
                  <PublicationsPage />
                </ProjectRoute>
              }
            />
            <Route
              path="projects/:projectSlug/publications/:id"
              element={
                <ProjectRoute>
                  <PublicationDetailPage />
                </ProjectRoute>
              }
            />
            <Route
              path="projects/:projectSlug/sources"
              element={
                <ProjectRoute>
                  <AdminRoute>
                    <SourcesPage />
                  </AdminRoute>
                </ProjectRoute>
              }
            />
            <Route
              path="projects/:projectSlug/publish-targets"
              element={
                <ProjectRoute>
                  <AdminRoute>
                    <PublishTargetsPage />
                  </AdminRoute>
                </ProjectRoute>
              }
            />
            <Route
              path="ai-operations"
              element={
                <AdminRoute>
                  <AiOperationsPage />
                </AdminRoute>
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
