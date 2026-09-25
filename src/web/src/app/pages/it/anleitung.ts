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
      title: 'Dall’alto, un po’ inclinato va bene',
      text: 'Tenere il telefono sopra il mazzetto, dritto o inclinato fino a circa 60°. Di lato l’app vede solo una striscia sottile della carta.',
    },
    {
      title: 'Una sola carta in cima',
      text: 'L’app conta sempre la carta che sta in cima. Se una carta in aria la copre in parte, l’app aspetta che la vista sia di nuovo libera.',
    },
    {
      title: 'Tutta la carta nell’immagine',
      text: 'Tutti e quattro gli angoli della carta in cima stanno nel riquadro, niente è tagliato al bordo.',
    },
    {
      title: 'Tenere fermo, lasciare la carta posata',
      text: 'Una carta che sta ancora volando non viene contata di proposito. L’app aspetta che la carta sia posata e l’immagine ferma.',
    },
    {
      title: 'Né troppo vicino, né troppo lontano',
      text: 'La carta in cima dovrebbe apparire sullo schermo grande circa come un pollice.',
    },
    {
      title: 'Basta la luce della stanza',
      text: 'Una luce normale basta. Il sole diretto e la penombra no – se è troppo buio aiuta la luce della schermata di conteggio, se il telefono ne ha una.',
    },
    {
      title: 'Un mazzo per mazzetto',
      text: 'Non mescolare carte francesi e tedesche. Il mazzo si sceglie prima di contare.',
    },
  ];
}
