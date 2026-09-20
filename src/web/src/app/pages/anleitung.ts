import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-anleitung',
  imports: [RouterLink],
  templateUrl: './anleitung.html',
})
export class Anleitung {
  /** Each rule is a fact of the data the recognition was trained on, written for a person at the table. */
  protected readonly rules = [
    {
      title: 'Von oben, leicht schräg ist gut',
      text: 'Das Telefon über den Stapel halten, gerade oder bis etwa 60° geneigt. Von der Seite sieht die App nur einen schmalen Streifen der Karte.',
    },
    {
      title: 'Nur eine Karte liegt ganz oben',
      text: 'Die App zählt immer die Karte, die zuoberst liegt. Deckt eine Karte in der Luft sie teilweise zu, wartet die App, bis die Sicht wieder frei ist.',
    },
    {
      title: 'Die ganze Karte im Bild',
      text: 'Alle vier Ecken der obersten Karte liegen im Rahmen, nichts ist am Rand abgeschnitten.',
    },
    {
      title: 'Ruhig halten, Karte liegen lassen',
      text: 'Eine Karte, die noch fliegt, zählt die App absichtlich nicht. Sie wartet, bis die Karte liegt und das Bild ruhig ist.',
    },
    {
      title: 'Nicht zu nah, nicht zu weit',
      text: 'Die oberste Karte soll etwa daumengross auf dem Bildschirm erscheinen.',
    },
    {
      title: 'Zimmerlicht reicht',
      text: 'Normales Licht genügt. Direkte Sonne und tiefe Dämmerung nicht – ist es zu dunkel, hilft das Licht im Zählbildschirm, sofern das Telefon eines hat.',
    },
    {
      title: 'Ein Blatt pro Stapel',
      text: 'Französische und deutsche Karten nicht mischen. Das Blatt wird vor dem Zählen gewählt.',
    },
  ];
}
