import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-home',
  imports: [RouterLink],
  templateUrl: './home.html',
})
export class Home {
  protected readonly features = [
    'French and Swiss German deck',
    'Trump in every suit, Obenabe, Undenufe, Slalom and Guschti',
    'Last trick and factor ×1 to ×8',
    'Tap a wrongly recognised card to remove it, add a missing one by hand',
    'Recognition right on the device, without internet',
  ];

  protected readonly screens = [
    { src: 'screens/en/start.jpg', alt: 'Home screen with the last count' },
    { src: 'screens/en/neue-zaehlung.jpg', alt: 'New count: deck, discipline, last trick and factor' },
    { src: 'screens/en/zaehlen.jpg', alt: 'Counting screen: the top card is recognised, the points keep up' },
  ];
}
