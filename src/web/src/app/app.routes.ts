import { Routes } from '@angular/router';

import { Anleitung } from './pages/anleitung';
import { Datenschutz } from './pages/datenschutz';
import { Home } from './pages/home';
import { Impressum } from './pages/impressum';
import { Lizenzen } from './pages/lizenzen';
import { Support } from './pages/support';

export const routes: Routes = [
  { path: '', component: Home, title: 'JassCardEye – Jasspunkte zählen per Kamera' },
  { path: 'anleitung', component: Anleitung, title: 'Anleitung – JassCardEye' },
  { path: 'support', component: Support, title: 'Support und Kontakt – JassCardEye' },
  { path: 'datenschutz', component: Datenschutz, title: 'Datenschutz – JassCardEye' },
  { path: 'impressum', component: Impressum, title: 'Impressum – JassCardEye' },
  { path: 'lizenzen', component: Lizenzen, title: 'Lizenzen und Bildnachweis – JassCardEye' },
  { path: '**', redirectTo: '' },
];
