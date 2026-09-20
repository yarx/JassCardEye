package ch.yarx.jasscardeye

import android.app.Activity
import android.app.Application
import android.content.Context
import android.content.ContextWrapper
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.lifecycle.AndroidViewModel
import com.android.billingclient.api.AcknowledgePurchaseParams
import com.android.billingclient.api.BillingClient
import com.android.billingclient.api.BillingClient.BillingResponseCode
import com.android.billingclient.api.BillingClient.ProductType
import com.android.billingclient.api.BillingClientStateListener
import com.android.billingclient.api.BillingFlowParams
import com.android.billingclient.api.BillingResult
import com.android.billingclient.api.PendingPurchasesParams
import com.android.billingclient.api.ProductDetails
import com.android.billingclient.api.Purchase
import com.android.billingclient.api.PurchasesUpdatedListener
import com.android.billingclient.api.QueryProductDetailsParams
import com.android.billingclient.api.QueryPurchasesParams

// Port of src/app/ios/Sources/Store.swift.

/**
 * The one purchase the app sells: the counted points, unlocked for good.
 *
 * The purchase itself is the source of truth, not a flag of the app's own. Whether it is owned is asked of Google
 * Play at launch and every time the app comes to the foreground - Play keeps the answer on the phone, so a start
 * without network still knows - and the [PurchasesUpdatedListener] hears a purchase that completes, including a
 * pending one that completes later. There is no server: the library's answer is all the app needs.
 *
 * A purchase has to be acknowledged within three days, or Google refunds it by itself. It is acknowledged the
 * moment the app sees it purchased, wherever that happens.
 *
 * The price is never written down anywhere in the app: it comes from the product, as Play formats it for the
 * account.
 */
class Store(application: Application) : AndroidViewModel(application), PurchasesUpdatedListener {

    /** Whether the points are readable. Everything that locks reads this one value. */
    var unlocked by mutableStateOf(false); private set

    /** The price as Play formats it for this account, "CHF 5.00". Null until the product has loaded. */
    var price by mutableStateOf<String?>(null); private set

    /** Play did not answer with the product: offline, no Play Store, or no account. The demo goes on regardless. */
    var productUnavailable by mutableStateOf(false); private set

    /** True from launching Play's purchase dialog until it reports back. */
    var purchasing by mutableStateOf(false); private set

    /** A purchase paid by a slow method - cash at a shop, say. It unlocks once Play reports it purchased. */
    var pending by mutableStateOf(false); private set

    /** Why the last purchase or restore did not go through. Shown where it was asked for, nowhere else. */
    var problem by mutableStateOf<String?>(null); private set

    private var product: ProductDetails? = null

    private val client = BillingClient.newBuilder(application)
        .setListener(this)
        .enablePendingPurchases(PendingPurchasesParams.newBuilder().enableOneTimeProducts().build())
        // Every call reconnects on its own when the Play service went away, so nothing here has to.
        .enableAutoServiceReconnection()
        .build()

    init {
        client.startConnection(object : BillingClientStateListener {
            override fun onBillingSetupFinished(result: BillingResult) {
                if (result.responseCode == BillingResponseCode.OK) {
                    refresh()
                    loadProduct()
                } else {
                    productUnavailable = true
                }
            }

            override fun onBillingServiceDisconnected() {}
        })
    }

    /**
     * Asks Play what is owned right now. A refunded purchase, and one that was never acknowledged and has lapsed,
     * is no longer in the answer - so this is also what locks the points again. When Play cannot answer at all,
     * what the app already knows stays.
     */
    fun refresh(onDone: () -> Unit = {}) {
        val params = QueryPurchasesParams.newBuilder().setProductType(ProductType.INAPP).build()
        client.queryPurchasesAsync(params) { result, purchases ->
            if (result.responseCode == BillingResponseCode.OK) apply(purchases)
            onDone()
        }
    }

    fun loadProduct() {
        if (product != null) return
        val params = QueryProductDetailsParams.newBuilder()
            .setProductList(listOf(
                QueryProductDetailsParams.Product.newBuilder().setProductId(PRODUCT_ID).setProductType(ProductType.INAPP).build()
            ))
            .build()
        client.queryProductDetailsAsync(params) { result, details ->
            val loaded = details.productDetailsList.firstOrNull { it.productId == PRODUCT_ID }
                .takeIf { result.responseCode == BillingResponseCode.OK }
            product = loaded
            price = loaded?.oneTimePurchaseOfferDetailsList?.firstOrNull()?.formattedPrice
                ?: loaded?.oneTimePurchaseOfferDetails?.formattedPrice
            productUnavailable = loaded == null
        }
    }

    fun purchase(context: Context) {
        val details = product
        val activity = context.findActivity()
        if (details == null || activity == null || purchasing) {
            loadProduct()
            return
        }
        val offer = details.oneTimePurchaseOfferDetailsList?.firstOrNull()
        val productParams = BillingFlowParams.ProductDetailsParams.newBuilder()
            .setProductDetails(details)
            .apply { offer?.offerToken?.let { setOfferToken(it) } }
            .build()
        problem = null
        val result = client.launchBillingFlow(activity, BillingFlowParams.newBuilder().setProductDetailsParamsList(listOf(productParams)).build())
        purchasing = result.responseCode == BillingResponseCode.OK
        if (!purchasing) problem = PURCHASE_FAILED
    }

    override fun onPurchasesUpdated(result: BillingResult, purchases: MutableList<Purchase>?) {
        purchasing = false
        when (result.responseCode) {
            BillingResponseCode.OK, BillingResponseCode.ITEM_ALREADY_OWNED -> refresh()
            BillingResponseCode.USER_CANCELED -> {}
            else -> problem = PURCHASE_FAILED
        }
    }

    /** Restoring is asking again: Play holds the purchases of the account, on this phone or a new one. */
    fun restore() {
        problem = null
        val params = QueryPurchasesParams.newBuilder().setProductType(ProductType.INAPP).build()
        client.queryPurchasesAsync(params) { result, purchases ->
            if (result.responseCode != BillingResponseCode.OK) {
                problem = "Die Käufe konnten gerade nicht abgefragt werden."
                return@queryPurchasesAsync
            }
            apply(purchases)
            if (!unlocked) problem = "Mit diesem Google-Konto ist kein Kauf vorhanden."
        }
    }

    private fun apply(purchases: List<Purchase>) {
        val ours = purchases.filter { PRODUCT_ID in it.products }
        unlocked = ours.any { it.purchaseState == Purchase.PurchaseState.PURCHASED }
        pending = ours.any { it.purchaseState == Purchase.PurchaseState.PENDING }
        for (purchase in ours) {
            if (purchase.purchaseState == Purchase.PurchaseState.PURCHASED && !purchase.isAcknowledged) {
                // Not retried here: an acknowledgement that fails is tried again at the next refresh, and Play
                // gives three days.
                client.acknowledgePurchase(
                    AcknowledgePurchaseParams.newBuilder().setPurchaseToken(purchase.purchaseToken).build()
                ) {}
            }
        }
    }

    override fun onCleared() {
        client.endConnection()
    }

    companion object {
        const val PRODUCT_ID = "ch.yarx.jasscardeye.counting"
        private const val PURCHASE_FAILED = "Der Kauf ist nicht zustande gekommen. Bitte versuche es später nochmals."
    }
}

/** The app's one [Store], handed to every screen that locks - the counterpart of `.environment(store)` on iOS. */
val LocalStore = staticCompositionLocalOf<Store> { error("No Store provided") }

private tailrec fun Context.findActivity(): Activity? = when (this) {
    is Activity -> this
    is ContextWrapper -> baseContext.findActivity()
    else -> null
}
