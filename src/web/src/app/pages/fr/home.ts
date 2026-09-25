import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-home',
  imports: [RouterLink],
  templateUrl: './home.html',
})
export class Home {
  protected readonly features = [
    'Jeu français et jeu suisse allemand',
    'Atout dans chaque couleur, Obenabe, Undenufe, Slalom et Guschti',
    'Dernier pli et facteur ×1 à ×8',
    'Toucher une carte mal reconnue pour la retirer, ajouter à la main une carte manquante',
    'Reconnaissance directement sur l’appareil, sans Internet',
  ];

  protected readonly screens = [
    { src: 'screens/fr/start.jpg', alt: 'Écran d’accueil avec le dernier comptage' },
    { src: 'screens/fr/neue-zaehlung.jpg', alt: 'Nouveau comptage : jeu, variante, dernier pli et facteur' },
    { src: 'screens/fr/zaehlen.jpg', alt: 'Écran de comptage : la carte du dessus est reconnue, les points suivent' },
  ];
}
