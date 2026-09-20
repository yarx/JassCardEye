import { Component } from '@angular/core';

/**
 * The facts of the Über page in both apps (AboutView.swift, AboutScreen.kt), for reading on the web - but
 * not a copy of it: the library lines here are more complete, and the website adds its own. A licence or
 * credit that changes in the apps has to change here too, and the other way round.
 */
@Component({
  selector: 'app-lizenzen',
  templateUrl: './lizenzen.html',
})
export class Lizenzen {
  protected readonly marks = [
    { name: 'Kreuz', file: 'SuitClubs.svg', author: 'F l a n k e r', licence: 'gemeinfrei' },
    { name: 'Ecken', file: 'Ecke_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Herz', file: 'Herz_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Schaufel', file: 'Schaufel_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Eichel', file: 'Eichel_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Rosen', file: 'Rosen_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Schellen', file: 'Schellen_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Schilten', file: 'Schilten_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
  ];

  protected commonsPage(file: string): string {
    return `https://commons.wikimedia.org/wiki/File:${file}`;
  }
}
