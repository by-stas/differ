import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./compare/compare-page/compare-page').then((m) => m.ComparePage),
  },
  {
    path: 'compare/:id',
    loadComponent: () => import('./compare/compare-page/compare-page').then((m) => m.ComparePage),
  },
  { path: '**', redirectTo: '' },
];
