import { Routes } from '@angular/router';

/**
 * Public routes are added phase by phase. Every one of them is server-rendered, because the
 * site exists to be found in search (NFR-SEO-01); the admin area is lazily loaded and rendered
 * in the browser.
 */
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./home/home').then((m) => m.Home),
    title: 'Software Management',
  },
];
