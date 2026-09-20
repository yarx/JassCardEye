import SwiftUI
import StoreKit

/// The purchase page: what the purchase unlocks, its price, buying and restoring.
///
/// Deliberately nothing else - no other way to pay, no link out of the app (App Review 3.1.1). It
/// closes by itself once the points are unlocked, so a purchase made in the middle of a count lands
/// straight back on the score, which is now readable.
struct PurchaseView: View {
    @Environment(Store.self) private var store
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 24) {
                    VStack(spacing: 8) {
                        Image(systemName: "lock.open.fill")
                            .font(.system(size: 42))
                            .foregroundStyle(.green)
                        Text("Punkte freischalten")
                            .font(.title2.weight(.bold))
                    }
                    .padding(.top, 8)

                    VStack(alignment: .leading, spacing: 14) {
                        benefit("eye", "Die gezählten Punkte werden lesbar - deine und die des Gegners, mit der Aufschlüsselung.")
                        benefit("clock.arrow.circlepath", "Auch «Letzte Zählung» auf dem Startbildschirm zeigt die Punkte.")
                        benefit("checkmark.seal", "Einmal kaufen, für immer. Kein Abo, kein Konto.")
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)

                    VStack(spacing: 12) {
                        Button {
                            Task { await store.purchase() }
                        } label: {
                            Text(buyTitle)
                                .font(.headline)
                                .frame(maxWidth: .infinity)
                                .padding(.vertical, 12)
                        }
                        .buttonStyle(.borderedProminent)
                        .tint(.green)
                        .disabled(store.product == nil || store.purchasing || store.pending)

                        if store.pending {
                            note("Der Kauf wartet auf eine Bestätigung. Die Punkte werden frei, sobald er bestätigt ist.")
                        }
                        if store.productUnavailable {
                            note("Der App Store ist gerade nicht erreichbar. Die Demo zählt weiter wie bisher.")
                        }
                        if let problem = store.problem {
                            note(problem)
                        }

                        Button("Kauf wiederherstellen") {
                            Task { await store.restore() }
                        }
                        .font(.callout)
                        .tint(.green)
                    }
                }
                .padding(24)
            }
            .navigationTitle("Freischalten")
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Schliessen") { dismiss() }
                }
            }
        }
        .task { await store.loadProduct() }
        .onChange(of: store.unlocked) { _, unlocked in
            if unlocked { dismiss() }
        }
    }

    private var buyTitle: String {
        if let price = store.product?.displayPrice { return "Kaufen für \(price)" }
        return store.productUnavailable ? "Nicht verfügbar" : "Preis wird geladen …"
    }

    private func benefit(_ symbol: String, _ text: String) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 12) {
            Image(systemName: symbol)
                .foregroundStyle(.green)
                .frame(width: 24)
            Text(text)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func note(_ text: String) -> some View {
        Text(text)
            .font(.footnote)
            .foregroundStyle(.secondary)
            .multilineTextAlignment(.center)
    }
}

/// The way to the purchase page wherever the points are blurred: "Punkte freischalten – CHF 5.00",
/// with the price from the App Store and without one until it has loaded.
struct UnlockButton: View {
    @Environment(Store.self) private var store
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Label(title, systemImage: "lock.open")
                .font(.callout.weight(.semibold))
                .frame(maxWidth: .infinity)
                .padding(.vertical, 6)
        }
        .buttonStyle(.bordered)
        .tint(.green)
    }

    private var title: String {
        guard let price = store.product?.displayPrice else { return "Punkte freischalten" }
        return "Punkte freischalten – \(price)"
    }
}
