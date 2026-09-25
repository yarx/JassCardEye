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
      title: 'From above, slightly tilted is fine',
      text: 'Hold the phone over the pile, straight or tilted up to about 60°. From the side the app sees only a thin strip of the card.',
    },
    {
      title: 'Only one card lies on top',
      text: 'The app always counts the card that lies on top. If a card in the air partly covers it, the app waits until the view is clear again.',
    },
    {
      title: 'The whole card in the picture',
      text: 'All four corners of the top card lie inside the frame, nothing is cut off at the edge.',
    },
    {
      title: 'Hold still, let the card lie',
      text: 'A card that is still flying is deliberately not counted. The app waits until the card lies and the picture is steady.',
    },
    {
      title: 'Not too close, not too far',
      text: 'The top card should appear about the size of a thumb on the screen.',
    },
    {
      title: 'Room light is enough',
      text: 'Normal light will do. Direct sun and deep dusk will not – if it is too dark, the light on the counting screen helps, if the phone has one.',
    },
    {
      title: 'One deck per pile',
      text: 'Do not mix French and German cards. The deck is chosen before counting.',
    },
  ];
}
