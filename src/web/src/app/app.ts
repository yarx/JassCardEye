import { Component, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
})
export class App {
  protected readonly menuOpen = signal(false);

  protected readonly navLinks = [
    { path: '/anleitung', label: 'Anleitung' },
    { path: '/support', label: 'Support' },
    { path: '/datenschutz', label: 'Datenschutz' },
    { path: '/impressum', label: 'Impressum' },
  ];

  protected readonly year = new Date().getFullYear();

  toggleMenu() {
    this.menuOpen.update((open) => !open);
  }

  closeMenu() {
    this.menuOpen.set(false);
  }
}
