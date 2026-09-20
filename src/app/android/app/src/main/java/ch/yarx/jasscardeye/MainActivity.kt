package ch.yarx.jasscardeye

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import androidx.lifecycle.viewmodel.compose.viewModel

// Port of src/app/ios/Sources/JassCardEyeApp.swift and ContentView.swift.

/**
 * The app root. It owns the detection model for the whole app lifetime - the settings and the loaded model
 * live there - while the camera and per-frame inference only run inside a counting session (`ScanScreen`).
 */
class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        // Dark bars with light icons, whatever the system theme: the app is dark by design, as on iOS.
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.dark(android.graphics.Color.TRANSPARENT),
            navigationBarStyle = SystemBarStyle.dark(android.graphics.Color.TRANSPARENT),
        )
        super.onCreate(savedInstanceState)
        setContent {
            JassCardEyeTheme { ContentView() }
        }
    }
}

@Composable
private fun ContentView(model: LiveDetectionModel = viewModel(), store: Store = viewModel()) {
    /** What the last finished session counted; replaced by the next one. */
    var lastResult by remember { mutableStateOf<CountResult?>(null) }
    // The scanner is a session: it exists only while counting, and takes camera and model with it when it goes.
    var showScanner by remember { mutableStateOf(false) }

    // Play is asked again whenever the app comes to the foreground: a refund, a lapsed purchase and a pending payment
    // that went through all arrive that way.
    LifecycleEventEffect(Lifecycle.Event.ON_START) { store.refresh() }

    CompositionLocalProvider(LocalStore provides store) {
        if (showScanner) {
            ScanScreen(model) { result ->
                lastResult = result
                showScanner = false
            }
        } else {
            HomeScreen(model, lastResult, onStart = { showScanner = true })
        }
    }
}
