import { Component } from '@angular/core';

/** The Italian version of pages/de/lizenzen.ts, which says what has to stay in step with the apps. */
@Component({
  selector: 'app-lizenzen',
  templateUrl: './lizenzen.html',
})
export class Lizenzen {
  protected readonly marks = [
    { name: 'Fiori', file: 'SuitClubs.svg', author: 'F l a n k e r', licence: 'pubblico dominio' },
    { name: 'Quadri', file: 'Ecke_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Cuori', file: 'Herz_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Picche', file: 'Schaufel_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Ghiande', file: 'Eichel_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Rose', file: 'Rosen_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Sonagli', file: 'Schellen_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Scudi', file: 'Schilten_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
  ];

  protected commonsPage(file: string): string {
    return `https://commons.wikimedia.org/wiki/File:${file}`;
  }
}
