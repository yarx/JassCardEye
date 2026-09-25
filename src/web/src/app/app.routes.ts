import { Routes } from '@angular/router';

// Every language is a build of its own (see "Languages" in src/web/README.md), and its pages are prose written
// for that language: pages/<language>/. The routes load the pages of the language being built, as one chunk;
// a development build without a language is German.
const pagesByLanguage = {
  de: () => import('./pages/de'),
  fr: () => import('./pages/fr'),
  it: () => import('./pages/it'),
  en: () => import('./pages/en'),
};
const pages = pagesByLanguage[($localize.locale ?? 'de') as keyof typeof pagesByLanguage] ?? pagesByLanguage.de;

// The addresses are the same in every language, so a page and its translations differ only in the language path.
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => pages().then((m) => m.Home),
    title: $localize`:@@title.home:JassCardEye – Jasspunkte zählen per Kamera`,
  },
  {
    path: 'anleitung',
    loadComponent: () => pages().then((m) => m.Anleitung),
    title: $localize`:@@title.guide:Anleitung – JassCardEye`,
  },
  {
    path: 'support',
    loadComponent: () => pages().then((m) => m.Support),
    title: $localize`:@@title.support:Support und Kontakt – JassCardEye`,
  },
  {
    path: 'datenschutz',
    loadComponent: () => pages().then((m) => m.Datenschutz),
    title: $localize`:@@title.privacy:Datenschutz – JassCardEye`,
  },
  {
    path: 'impressum',
    loadComponent: () => pages().then((m) => m.Impressum),
    title: $localize`:@@title.imprint:Impressum – JassCardEye`,
  },
  {
    path: 'lizenzen',
    loadComponent: () => pages().then((m) => m.Lizenzen),
    title: $localize`:@@title.licences:Lizenzen und Bildnachweis – JassCardEye`,
  },
  { path: '**', redirectTo: '' },
];
