import StoreKit
import Observation

/// The one purchase the app sells: the counted points, unlocked for good.
///
/// The purchase itself is the source of truth, not a flag of the app's own. Whether it is owned is
/// read from `Transaction.currentEntitlements`, which StoreKit keeps signed on the device - so a
/// start without network still knows, and nothing in `UserDefaults` could be edited into a
/// purchase. `Transaction.updates` is listened to for the app's whole life: a refund, a purchase a
/// family member shares, and an Ask to Buy approved hours later all arrive there, and each has to
/// change the screen without a restart.
///
/// The price is never written down anywhere in the app: it comes from the product, in the buyer's
/// currency.
@MainActor
@Observable
final class Store {
    static let productID = "ch.yarx.jasscardeye.counting"

    /// Whether the points are readable. Everything that locks reads this one value.
    private(set) var unlocked = false

    /// The product as the App Store describes it - name and price. Nil until it has loaded.
    private(set) var product: Product?

    /// The App Store did not answer with the product: offline, or not reachable. The demo goes on
    /// regardless; only the purchase page says so.
    private(set) var productUnavailable = false

    /// True while the App Store's own purchase sheet is up.
    private(set) var purchasing = false

    /// A purchase that waits for a parent's approval (Ask to Buy). It unlocks through
    /// `Transaction.updates` once approved.
    private(set) var pending = false

    /// Why the last purchase or restore did not go through. Shown where it was asked for, nowhere else.
    private(set) var problem: String?

    @ObservationIgnored private var updates: Task<Void, Never>?

    init() {
        updates = Task { [weak self] in
            for await result in Transaction.updates {
                await self?.apply(result)
            }
        }
        Task { await refresh() }
        Task { await loadProduct() }
    }

    /// Asks StoreKit what is owned right now. A refunded purchase is no longer an entitlement, so
    /// this is also what locks the points again after a refund.
    func refresh() async {
        var owned = false
        for await result in Transaction.currentEntitlements {
            if case .verified(let transaction) = result,
               transaction.productID == Self.productID, transaction.revocationDate == nil {
                owned = true
            }
        }
        unlocked = owned
    }

    func loadProduct() async {
        guard product == nil else { return }
        let loaded = try? await Product.products(for: [Self.productID]).first
        product = loaded
        productUnavailable = loaded == nil
    }

    func purchase() async {
        await loadProduct()
        guard let product, !purchasing else { return }
        purchasing = true
        problem = nil
        defer { purchasing = false }
        do {
            switch try await product.purchase() {
            case .success(let result):
                await apply(result)
            case .pending:
                pending = true
            case .userCancelled:
                break
            @unknown default:
                break
            }
        } catch {
            problem = "Der Kauf ist nicht zustande gekommen. Bitte versuche es später nochmals."
        }
    }

    /// Apple asks for a visible way to restore. `AppStore.sync()` makes the device fetch the
    /// purchases of this Apple ID again; the entitlements are read afterwards like at every start.
    func restore() async {
        problem = nil
        do {
            try await AppStore.sync()
        } catch StoreKitError.userCancelled {
            return
        } catch {
            problem = "Die Käufe konnten gerade nicht abgefragt werden."
            return
        }
        await refresh()
        if !unlocked {
            problem = "Mit dieser Apple-ID ist kein Kauf vorhanden."
        }
    }

    private func apply(_ result: VerificationResult<Transaction>) async {
        // A transaction StoreKit cannot verify unlocks nothing. It is not finished either, so the
        // App Store offers it again rather than losing a real purchase to a bad moment.
        guard case .verified(let transaction) = result else { return }
        if transaction.productID == Self.productID {
            pending = false
        }
        await transaction.finish()
        await refresh()
    }
}
