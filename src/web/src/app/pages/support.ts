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
      question: 'Warum sind die Punkte verwischt?',
      answer:
        'Das ist die Demo: Die App zählt vollständig, nur die berechneten Punkte sind unscharf. Mit dem einmaligen Kauf «Punkte zählen» werden sie lesbar – auch mitten in einer Zählung, ohne neu zu scannen.',
    },
    {
      question: 'Ich habe die App schon gekauft – auf einem neuen Telefon sind die Punkte wieder verwischt.',
      answer:
        'In den Einstellungen der App «Kauf wiederherstellen» antippen. Der Kauf gehört zur Apple-ID bzw. zum Google-Konto, mit dem gekauft wurde. Ein Kauf auf dem iPhone gilt nicht auf Android und umgekehrt.',
    },
    {
      question: 'Gilt der Kauf für die Familie?',
      answer:
        'Auf dem iPhone ja, über die Familienfreigabe von Apple. Bei Google Play gilt der Kauf für das Google-Konto, mit dem gekauft wurde.',
    },
    {
      question: 'Welche Geräte werden unterstützt?',
      answer: 'iPhones ab iOS 17 und Android-Telefone ab Android 10.',
    },
    {
      question: 'Eine Karte wird falsch oder gar nicht erkannt.',
      answer:
        'Falsch erkannte Karten in der Liste antippen, fehlende über «Karte fehlt?» hinzufügen. Wie die Erkennung am besten gelingt, steht in der Anleitung.',
    },
    {
      question: 'Braucht die App Internet?',
      answer:
        'Nein. Die Erkennung läuft ganz auf dem Gerät, und die App selbst baut keine Verbindung auf. Nur für den Kauf fragt sie beim App Store bzw. bei Google Play nach: beim Start, ob er besteht, und beim Kaufen und Wiederherstellen.',
    },
  ];
}
