import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-home',
  imports: [RouterLink],
  templateUrl: './home.html',
})
export class Home {
  protected readonly features = [
    'Französisches und deutsches Blatt',
    'Trumpf in jeder Farbe, Obenabe, Undenufe, Slalom und Guschti',
    'Letzter Stich und Faktor ×1 bis ×8',
    'Falsch erkannte Karte antippen und entfernen, fehlende von Hand hinzufügen',
    'Erkennung direkt auf dem Gerät, ohne Internet',
  ];

  protected readonly screens = [
    { src: 'screens/start.jpg', alt: 'Startbildschirm mit der letzten Zählung' },
    { src: 'screens/neue-zaehlung.jpg', alt: 'Neue Zählung: Blatt, Spielart, letzter Stich und Faktor' },
    { src: 'screens/zaehlen.jpg', alt: 'Zählbildschirm: die oberste Karte ist erkannt, die Punkte laufen mit' },
  ];
}
