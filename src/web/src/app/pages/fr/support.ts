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
      question: 'Pourquoi les points sont-ils flous ?',
      answer:
        'C’est la démo : l’app compte entièrement, seuls les points calculés sont flous. L’achat unique les rend lisibles – même au milieu d’un comptage, sans scanner à nouveau.',
    },
    {
      question: 'J’ai déjà acheté l’app – sur un nouveau téléphone, les points sont de nouveau flous.',
      answer:
        'Dans les réglages de l’app, toucher «Restaurer l’achat». L’achat appartient à l’identifiant Apple ou au compte Google avec lequel il a été fait. Un achat sur l’iPhone ne vaut pas sur Android, et inversement.',
    },
    {
      question: 'L’achat vaut-il pour la famille ?',
      answer:
        'Sur l’iPhone oui, par le partage familial d’Apple. Sur Google Play, l’achat vaut pour le compte Google avec lequel il a été fait.',
    },
    {
      question: 'Quels appareils sont pris en charge ?',
      answer: 'Les iPhone à partir d’iOS 17 et les téléphones Android à partir d’Android 10.',
    },
    {
      question: 'Une carte est mal reconnue ou pas du tout.',
      answer:
        'Toucher dans la liste les cartes mal reconnues, ajouter celles qui manquent avec «Carte manquante ?». Comment la reconnaissance marche le mieux est expliqué dans le mode d’emploi.',
    },
    {
      question: 'L’app a-t-elle besoin d’Internet ?',
      answer:
        'Non. La reconnaissance se fait entièrement sur l’appareil, et l’app elle-même n’établit aucune connexion. Seulement pour l’achat, elle demande à l’App Store ou à Google Play : au démarrage, s’il existe, puis lors de l’achat et de la restauration.',
    },
  ];
}
