import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-support',
  imports: [RouterLink],
  templateUrl: './support.html',
})
export class Support {
  protected readonly faq = [
    {
      question: 'Why are the points blurred?',
      answer:
        'That is the demo: the app counts completely, only the calculated points are out of focus. The one-time purchase makes them readable – even in the middle of a count, without scanning again.',
    },
    {
      question: 'I already bought the app – on a new phone the points are blurred again.',
      answer:
        'Tap “Restore purchase” in the settings of the app. The purchase belongs to the Apple ID or the Google account it was made with. A purchase on the iPhone does not apply on Android, and the other way round.',
    },
    {
      question: 'Does the purchase cover the family?',
      answer:
        'On the iPhone it does, through Apple’s Family Sharing. On Google Play the purchase applies to the Google account it was made with.',
    },
    {
      question: 'Which devices are supported?',
      answer: 'iPhones from iOS 17 and Android phones from Android 10.',
    },
    {
      question: 'A card is recognised wrongly or not at all.',
      answer:
        'Tap wrongly recognised cards in the list, add missing ones with “Card missing?”. How the recognition works best is in the guide.',
    },
    {
      question: 'Does the app need the internet?',
      answer:
        'No. The recognition runs entirely on the device, and the app itself opens no connection. Only for the purchase does it ask the App Store or Google Play: at start, whether it exists, and when buying and restoring.',
    },
  ];
}
