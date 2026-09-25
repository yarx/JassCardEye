import { Component, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
})
export class App {
  protected readonly menuOpen = signal(false);

  // The labels are messages with stable ids, like the keys of the apps: the pages are written per language
  // as a whole, only navigation, titles and footer are translated one message at a time.
  protected readonly navLinks = [
    { path: '/anleitung', label: $localize`:@@nav.guide:Anleitung` },
    { path: '/support', label: $localize`:@@nav.support:Support` },
    { path: '/datenschutz', label: $localize`:@@nav.privacy:Datenschutz` },
    { path: '/impressum', label: $localize`:@@nav.imprint:Impressum` },
  ];

  /** The footer names every page but the start page. */
  protected readonly footerLinks = [...this.navLinks, { path: '/lizenzen', label: $localize`:@@nav.licences:Lizenzen` }];

  protected readonly year = new Date().getFullYear();

  toggleMenu() {
    this.menuOpen.update((open) => !open);
  }

  closeMenu() {
    this.menuOpen.set(false);
  }
}
