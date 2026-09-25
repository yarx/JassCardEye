import { Component } from '@angular/core';

/** The English version of pages/de/lizenzen.ts, which says what has to stay in step with the apps. */
@Component({
  selector: 'app-lizenzen',
  templateUrl: './lizenzen.html',
})
export class Lizenzen {
  protected readonly marks = [
    { name: 'Clubs', file: 'SuitClubs.svg', author: 'F l a n k e r', licence: 'public domain' },
    { name: 'Diamonds', file: 'Ecke_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Hearts', file: 'Herz_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Spades', file: 'Schaufel_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Acorns', file: 'Eichel_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Roses', file: 'Rosen_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Bells', file: 'Schellen_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Shields', file: 'Schilten_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
  ];

  protected commonsPage(file: string): string {
    return `https://commons.wikimedia.org/wiki/File:${file}`;
  }
}
