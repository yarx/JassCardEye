import { Routes } from '@angular/router';

import { Anleitung } from './pages/anleitung';
import { Datenschutz } from './pages/datenschutz';
import { Home } from './pages/home';
import { Impressum } from './pages/impressum';
import { Lizenzen } from './pages/lizenzen';
import { Support } from './pages/support';

export const routes: Routes = [
  { path: '', component: Home, title: $localize`:@@title.home:JassCardEye – Jasspunkte zählen per Kamera` },
  { path: 'anleitung', component: Anleitung, title: $localize`:@@title.guide:Anleitung – JassCardEye` },
  { path: 'support', component: Support, title: $localize`:@@title.support:Support und Kontakt – JassCardEye` },
  { path: 'datenschutz', component: Datenschutz, title: $localize`:@@title.privacy:Datenschutz – JassCardEye` },
  { path: 'impressum', component: Impressum, title: $localize`:@@title.imprint:Impressum – JassCardEye` },
  { path: 'lizenzen', component: Lizenzen, title: $localize`:@@title.licences:Lizenzen und Bildnachweis – JassCardEye` },
  { path: '**', redirectTo: '' },
];
