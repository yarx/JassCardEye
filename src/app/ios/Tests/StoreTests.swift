import XCTest
import StoreKit
import StoreKitTest
@testable import JassCardEye

/// The purchase, held to account against JassCardEye.storekit - no App Store, no account.
///
/// Each case is one of the situations a purchase has to survive: a fresh install, a purchase, a
/// refund while the app runs, Ask to Buy approved and declined, an interrupted purchase, and a start
/// without the App Store. `Store` is created fresh in each, the way a launch creates it.
///
/// Run from Xcode (⌘U), after the app has been run once. Two limits of StoreKit testing on iOS 26
/// simulators decide what runs where:
///
/// - Under `xcodebuild test` the session can fail to save its configuration, depending on the
///   simulator runtime (`SKInternalErrorDomain Code=3`); every case then fails without reaching the
///   app's code.
/// - In Xcode, buying works, but changing transactions through the session - a refund, clearing
///   them, answering Ask to Buy, resolving an interrupted purchase - can fail with the same error.
///   The cases that need it skip with that reason instead of failing; they run wherever the session
///   can change transactions, and Xcode's Transaction Manager (Debug → StoreKit) covers them by hand.
@MainActor
final class StoreTests: XCTestCase {
    private var session: SKTestSession!

    override func setUp() async throws {
        session = try SKTestSession(configurationFileNamed: "JassCardEye")
        session.resetToDefaultState()
        session.disableDialogs = true
        session.clearTransactions()
    }

    // MARK: - Buying and reading: run wherever the session loads
    //
    // The offline case among them skips where the session cannot simulate a start without the App Store.

    func testPriceComesFromTheStore() async {
        let store = Store()
        await store.loadProduct()
        XCTAssertEqual(store.product?.id, Store.productID)
        XCTAssertNotNil(store.product?.displayPrice)
        XCTAssertFalse(store.productUnavailable)
    }

    func testPurchaseUnlocks() async {
        let store = Store()
        await store.purchase()
        XCTAssertTrue(store.unlocked)
        XCTAssertNil(store.problem)
    }

    /// A reinstall, or a second device: the purchase is an entitlement of the account, not of the
    /// store object that made it.
    func testPurchaseIsThereAtTheNextLaunch() async {
        await Store().purchase()
        let relaunched = Store()
        await relaunched.refresh()
        XCTAssertTrue(relaunched.unlocked)
    }

    /// Offline: the entitlement is read from the device, so the points stay unlocked; only the
    /// product does not load, and that is no error the app shows outside the purchase page.
    func testStartWithoutTheAppStoreKeepsThePurchase() async throws {
        await Store().purchase()
        try await simulateStartWithoutTheAppStore()
        let offline = Store()
        await offline.refresh()
        await offline.loadProduct()
        XCTAssertTrue(offline.unlocked)
        XCTAssertTrue(offline.productUnavailable)
        XCTAssertNil(offline.problem)
    }

    // MARK: - Changing transactions: need a session that can write them

    func testFreshInstallIsTheDemo() async throws {
        try await requireNoPurchase()
        let store = Store()
        await store.refresh()
        XCTAssertFalse(store.unlocked)
    }

    func testStartWithoutTheAppStoreAndNoPurchaseIsTheDemoWithoutAnError() async throws {
        try await requireNoPurchase()
        try await simulateStartWithoutTheAppStore()
        let offline = Store()
        await offline.refresh()
        await offline.loadProduct()
        XCTAssertFalse(offline.unlocked)
        XCTAssertNil(offline.problem)
    }

    func testRefundLocksWhileTheAppRuns() async throws {
        let store = Store()
        await store.purchase()
        XCTAssertTrue(store.unlocked)
        let latest = await Transaction.latest(for: Store.productID)
        guard case .verified(let transaction) = try XCTUnwrap(latest) else {
            return XCTFail("The purchase left no verified transaction")
        }
        try refund(transaction)
        try await waitUntil { !store.unlocked }
    }

    func testAskToBuyUnlocksOnceApprovedWithoutARestart() async throws {
        try await requireNoPurchase()
        session.askToBuyEnabled = true
        let store = Store()
        await store.purchase()
        let transaction = try await testTransaction()
        XCTAssertTrue(store.pending)
        XCTAssertFalse(store.unlocked)
        try session.approveAskToBuyTransaction(identifier: transaction.identifier)
        try await waitUntil { store.unlocked }
        XCTAssertFalse(store.pending)
    }

    func testAskToBuyDeclinedStaysTheDemo() async throws {
        try await requireNoPurchase()
        session.askToBuyEnabled = true
        let store = Store()
        await store.purchase()
        let transaction = try await testTransaction()
        try session.declineAskToBuyTransaction(identifier: transaction.identifier)
        try await Task.sleep(for: .seconds(1))
        await store.refresh()
        XCTAssertFalse(store.unlocked)
    }

    func testInterruptedPurchaseUnlocksOnceResolved() async throws {
        try await requireNoPurchase()
        session.interruptedPurchasesEnabled = true
        let store = Store()
        await store.purchase()
        let transaction = try await testTransaction()
        XCTAssertFalse(store.unlocked)
        try session.resolveIssueForTransaction(identifier: transaction.identifier)
        try await waitUntil { store.unlocked }
    }

    /// Not a test: keeps the StoreKit test session alive for ten minutes so the running app can be
    /// walked through by hand - the purchase page with its price, a purchase in the middle of a
    /// count, the score turning sharp. Only with TEST_RUNNER_JASSCARDEYE_WALKTHROUGH=1.
    func testWalkthroughByHand() async throws {
        try XCTSkipUnless(ProcessInfo.processInfo.environment["JASSCARDEYE_WALKTHROUGH"] == "1")
        try await Task.sleep(for: .seconds(600))
    }

    // MARK: - Helpers

    /// The demo cases need a device without the purchase. A purchase from an earlier case is refunded;
    /// where the session cannot do that, the case skips rather than measuring the wrong state.
    private func requireNoPurchase() async throws {
        for await result in Transaction.currentEntitlements {
            if case .verified(let transaction) = result, transaction.productID == Store.productID {
                try refund(transaction)
            }
        }
        let deadline = Date().addingTimeInterval(5)
        while await Self.ownsProduct() {
            guard Date() < deadline else {
                throw XCTSkip("A purchase from an earlier case could not be cleared by the test session")
            }
            try await Task.sleep(for: .milliseconds(100))
        }
    }

    /// Products fail to load, as without network. Where the session cannot simulate that, the product
    /// still loads and the case would measure an ordinary start - so it skips instead.
    private func simulateStartWithoutTheAppStore() async throws {
        try await session.setSimulatedError(.generic(.networkError(URLError(.notConnectedToInternet))),
                                            forAPI: .loadProducts)
        if let products = try? await Product.products(for: [Store.productID]), !products.isEmpty {
            throw XCTSkip("The test session cannot simulate a start without the App Store here")
        }
    }

    private func refund(_ transaction: Transaction) throws {
        do {
            try session.refundTransaction(identifier: UInt(transaction.id))
        } catch {
            throw XCTSkip("The test session cannot change transactions here: \(error)")
        }
    }

    private static func ownsProduct() async -> Bool {
        for await result in Transaction.currentEntitlements {
            if case .verified(let transaction) = result, transaction.productID == Store.productID {
                return true
            }
        }
        return false
    }

    /// The test session's own record of the latest purchase attempt - what Ask to Buy and an
    /// interrupted purchase are resolved by. It appears a moment after the purchase call returns;
    /// where the session records none, the case skips.
    private func testTransaction(timeout: TimeInterval = 5) async throws -> SKTestTransaction {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if let transaction = session.allTransactions().last(where: { $0.productIdentifier == Store.productID }) {
                return transaction
            }
            try await Task.sleep(for: .milliseconds(200))
        }
        throw XCTSkip("The test session records no transactions here")
    }

    private func waitUntil(timeout: TimeInterval = 10, _ condition: () async -> Bool) async throws {
        let deadline = Date().addingTimeInterval(timeout)
        while !(await condition()) {
            guard Date() < deadline else {
                XCTFail("Condition not met within \(timeout) s")
                return
            }
            try await Task.sleep(for: .milliseconds(100))
        }
    }
}
