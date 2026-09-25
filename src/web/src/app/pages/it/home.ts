import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-home',
  imports: [RouterLink],
  templateUrl: './home.html',
})
export class Home {
  protected readonly features = [
    'Mazzo francese e mazzo svizzero tedesco',
    'Briscola in ogni seme, Obenabe, Undenufe, Slalom e Guschti',
    'Ultima presa e fattore da ×1 a ×8',
    'Toccare una carta riconosciuta male per toglierla, aggiungere a mano quella che manca',
    'Riconoscimento direttamente sul dispositivo, senza Internet',
  ];

  protected readonly screens = [
    { src: 'screens/start.jpg', alt: 'Schermata iniziale con l’ultimo conteggio' },
    { src: 'screens/neue-zaehlung.jpg', alt: 'Nuovo conteggio: mazzo, variante, ultima presa e fattore' },
    { src: 'screens/zaehlen.jpg', alt: 'Schermata di conteggio: la carta in cima è riconosciuta, i punti si aggiornano' },
  ];
}
