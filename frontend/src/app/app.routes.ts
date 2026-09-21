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
    path: 'contact',
    loadComponent: () => import('./public/forms/contact-form').then((m) => m.ContactForm),
    title: 'Talk to us',
  },
  {
    path: 'request-demo',
    loadComponent: () => import('./public/forms/demo-request').then((m) => m.DemoRequest),
    title: 'See it working',
  },
  {
    path: 'request-quote',
    loadComponent: () => import('./public/forms/quote-request').then((m) => m.QuoteRequest),
    title: 'Get a price',
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
      {
        path: 'leads',
        loadComponent: () => import('./admin/leads/lead-inbox').then((m) => m.LeadInbox),
        title: 'Enquiries',
      },
      {
        path: 'leads/:id',
        loadComponent: () => import('./admin/leads/lead-detail').then((m) => m.LeadDetail),
        title: 'Enquiry',
      },
      {
        path: 'customers',
        loadComponent: () => import('./admin/customers/organisation-list').then((m) => m.OrganisationList),
        title: 'Customers',
      },
      {
        path: 'quotes',
        loadComponent: () => import('./admin/quotes/quote-list').then((m) => m.QuoteList),
        title: 'Quotes',
      },
      {
        path: 'quotes/:id',
        loadComponent: () => import('./admin/quotes/quote-editor').then((m) => m.QuoteEditor),
        title: 'Quote',
      },
      {
        path: 'subscriptions',
        loadComponent: () => import('./admin/billing/subscription-list').then((m) => m.SubscriptionList),
        title: 'Subscriptions',
      },
      {
        path: 'invoices',
        loadComponent: () => import('./admin/billing/invoice-list').then((m) => m.InvoiceList),
        title: 'Invoices',
      },
      {
        path: 'invoices/:id',
        loadComponent: () => import('./admin/billing/invoice-detail').then((m) => m.InvoiceDetail),
        title: 'Invoice',
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
