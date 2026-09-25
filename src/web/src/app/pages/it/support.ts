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
      question: 'Perché i punti sono sfocati?',
      answer:
        'È la demo: l’app conta tutto, solo i punti calcolati sono sfocati. L’acquisto unico li rende leggibili – anche nel mezzo di un conteggio, senza scansionare di nuovo.',
    },
    {
      question: 'Ho già acquistato l’app – su un nuovo telefono i punti sono di nuovo sfocati.',
      answer:
        'Nelle impostazioni dell’app toccare «Ripristina acquisto». L’acquisto appartiene all’ID Apple o all’account Google con cui è stato fatto. Un acquisto sull’iPhone non vale su Android, e viceversa.',
    },
    {
      question: 'L’acquisto vale per la famiglia?',
      answer:
        'Sull’iPhone sì, tramite In famiglia di Apple. Su Google Play l’acquisto vale per l’account Google con cui è stato fatto.',
    },
    {
      question: 'Quali dispositivi sono supportati?',
      answer: 'iPhone da iOS 17 e telefoni Android da Android 10.',
    },
    {
      question: 'Una carta viene riconosciuta male o per niente.',
      answer:
        'Toccare nell’elenco le carte riconosciute male, aggiungere quelle che mancano con «Manca una carta?». Come riesce meglio il riconoscimento è spiegato nelle istruzioni.',
    },
    {
      question: 'L’app ha bisogno di Internet?',
      answer:
        'No. Il riconoscimento avviene interamente sul dispositivo, e l’app stessa non apre nessuna connessione. Solo per l’acquisto chiede all’App Store o a Google Play: all’avvio, se esiste, e quando si acquista o si ripristina.',
    },
  ];
}
