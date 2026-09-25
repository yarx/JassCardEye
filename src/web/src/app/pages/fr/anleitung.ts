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
      title: 'D’en haut, un peu incliné, c’est bien',
      text: 'Tenir le téléphone au-dessus du tas, droit ou incliné jusqu’à environ 60°. De côté, l’app ne voit qu’une mince bande de la carte.',
    },
    {
      title: 'Une seule carte tout en haut',
      text: 'L’app compte toujours la carte qui est dessus. Si une carte en l’air la cache en partie, l’app attend que la vue soit de nouveau libre.',
    },
    {
      title: 'Toute la carte dans l’image',
      text: 'Les quatre coins de la carte du dessus sont dans le cadre, rien n’est coupé au bord.',
    },
    {
      title: 'Tenir immobile, laisser la carte posée',
      text: 'Une carte qui vole encore n’est volontairement pas comptée. L’app attend que la carte soit posée et que l’image soit stable.',
    },
    {
      title: 'Ni trop près, ni trop loin',
      text: 'La carte du dessus doit apparaître à peu près de la taille d’un pouce sur l’écran.',
    },
    {
      title: 'La lumière de la pièce suffit',
      text: 'Une lumière normale suffit. Le soleil direct et la pénombre, non – s’il fait trop sombre, la lumière de l’écran de comptage aide, si le téléphone en a une.',
    },
    {
      title: 'Un jeu par tas',
      text: 'Ne pas mélanger cartes françaises et allemandes. Le jeu est choisi avant de compter.',
    },
  ];
}
