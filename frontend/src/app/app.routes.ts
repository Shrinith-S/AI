import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', title: 'Dashboard · Chitra Studio', loadComponent: () => import('./pages/dashboard').then((m) => m.DashboardPage) },
  { path: 'create', title: 'Create · Chitra Studio', loadComponent: () => import('./pages/create-hub').then((m) => m.CreateHubPage) },
  { path: 'create/image', title: 'Image Generator · Chitra Studio', loadComponent: () => import('./pages/image-generator').then((m) => m.ImageGeneratorPage) },
  {
    path: 'create/video',
    title: 'Video Generator · Chitra Studio',
    data: { mode: 't2v' },
    loadComponent: () => import('./pages/video-generator').then((m) => m.VideoGeneratorPage),
  },
  {
    path: 'create/image-to-video',
    title: 'Image to Video · Chitra Studio',
    data: { mode: 'i2v' },
    loadComponent: () => import('./pages/video-generator').then((m) => m.VideoGeneratorPage),
  },
  { path: 'create/story', title: 'Story Video · Chitra Studio', loadComponent: () => import('./pages/story-create').then((m) => m.StoryCreatePage) },
  { path: 'characters', title: 'Characters · Chitra Studio', loadComponent: () => import('./pages/characters').then((m) => m.CharactersPage) },
  { path: 'styles', title: 'Styles · Chitra Studio', loadComponent: () => import('./pages/styles').then((m) => m.StylesPage) },
  { path: 'projects', title: 'Projects · Chitra Studio', loadComponent: () => import('./pages/projects').then((m) => m.ProjectsPage) },
  { path: 'projects/:id', title: 'Project · Chitra Studio', loadComponent: () => import('./pages/project-detail').then((m) => m.ProjectDetailPage) },
  { path: 'history', title: 'History · Chitra Studio', loadComponent: () => import('./pages/history').then((m) => m.HistoryPage) },
  { path: 'models', title: 'Models · Chitra Studio', loadComponent: () => import('./pages/models').then((m) => m.ModelsPage) },
  { path: 'settings', title: 'Settings · Chitra Studio', loadComponent: () => import('./pages/settings').then((m) => m.SettingsPage) },
  { path: '**', redirectTo: '' },
];
