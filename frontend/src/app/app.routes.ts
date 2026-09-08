import { Routes } from '@angular/router';
import { adminGuard } from './core/auth/auth.guard';

/**
 * Public routes are server-rendered because the site exists to be found in search
 * (NFR-SEO-01). The admin area is lazily loaded and rendered in the browser: it is behind a
 * sign-in, so there is nothing for a crawler to index and no reason to pay for its bundle on a
 * marketing page.
 */
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./home/home').then((m) => m.Home),
    title: 'Software Management',
  },
  {
    path: 'about',
    loadComponent: () => import('./public/about/about').then((m) => m.About),
    title: 'About us',
  },
  {
    path: 'services',
    loadComponent: () => import('./public/services/services-page').then((m) => m.ServicesPage),
    title: 'Services',
  },
  {
    path: 'products',
    loadComponent: () => import('./public/products/product-catalog').then((m) => m.ProductCatalog),
    title: 'Software we build',
  },
  {
    path: 'products/:slug',
    loadComponent: () => import('./public/products/product-detail').then((m) => m.ProductDetail),
  },
  {
    path: 'admin/login',
    loadComponent: () => import('./admin/login/login').then((m) => m.Login),
    title: 'Sign in',
  },
  {
    path: 'admin',
    canActivate: [adminGuard],
    loadComponent: () => import('./admin/admin-shell/admin-shell').then((m) => m.AdminShell),
    children: [
      {
        path: '',
        loadComponent: () => import('./admin/dashboard/dashboard').then((m) => m.Dashboard),
        title: 'Dashboard',
      },
      {
        path: 'products',
        loadComponent: () => import('./admin/products/product-list').then((m) => m.ProductList),
        title: 'Products',
      },
      {
        path: 'products/new',
        loadComponent: () => import('./admin/products/product-editor').then((m) => m.ProductEditor),
        title: 'New product',
      },
      {
        path: 'products/:id',
        loadComponent: () => import('./admin/products/product-editor').then((m) => m.ProductEditor),
        title: 'Product',
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
