import { NgTemplateOutlet } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Meta } from '@angular/platform-browser';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';

import { currentLanguage, languages } from './languages';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NgTemplateOutlet],
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

  protected readonly languages = languages;
  protected readonly currentLanguage = currentLanguage;

  /** The page on screen, without the language: a language link leads to the same page in the other build. */
  private readonly router = inject(Router);
  private readonly page = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly year = new Date().getFullYear();

  constructor() {
    inject(Meta).updateTag({
      name: 'description',
      content: $localize`:@@meta.description:JassCardEye zählt nach dem Jass die Punkte: Karten der Reihe nach vor die Kamera legen, die App erkennt die oberste Karte und rechnet laufend zusammen.`,
    });
  }

  /** The address of the page on screen in another language - a full page load, since every language is its own build. */
  protected languageLink(code: string): string {
    const path = this.page().split(/[?#]/)[0];
    return `/${code}${path === '/' ? '' : path}`;
  }

  toggleMenu() {
    this.menuOpen.update((open) => !open);
  }

  closeMenu() {
    this.menuOpen.set(false);
  }
}
