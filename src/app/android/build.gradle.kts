// The Android app. It changes together with the iOS app in ../ios, in the same change, and its sources
// mirror ../ios/Sources file for file.
plugins {
    alias(libs.plugins.android.application) apply false
    alias(libs.plugins.kotlin.compose) apply false
}
