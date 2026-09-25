import { Component } from '@angular/core';

/** The French version of pages/de/lizenzen.ts, which says what has to stay in step with the apps. */
@Component({
  selector: 'app-lizenzen',
  templateUrl: './lizenzen.html',
})
export class Lizenzen {
  protected readonly marks = [
    { name: 'Trèfle', file: 'SuitClubs.svg', author: 'F l a n k e r', licence: 'domaine public' },
    { name: 'Carreau', file: 'Ecke_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Cœur', file: 'Herz_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Pique', file: 'Schaufel_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Glands', file: 'Eichel_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Roses', file: 'Rosen_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Grelots', file: 'Schellen_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
    { name: 'Écus', file: 'Schilten_Neu.svg', author: 'Jensche', licence: 'CC BY-SA 4.0' },
  ];

  protected commonsPage(file: string): string {
    return `https://commons.wikimedia.org/wiki/File:${file}`;
  }
}
