package ch.yarx.jasscardeye

import android.os.Build

// Port of src/app/ios/Sources/AppInfo.swift.

/**
 * What a tester is asked to quote in a report: which build this is.
 *
 * Read out of the build rather than written down anywhere by hand. Version and build number are passed into
 * Gradle by the release workflow, so they cannot drift from what actually shipped.
 */
object AppInfo {

    val version: String get() = BuildConfig.VERSION_NAME
    val build: String get() = BuildConfig.VERSION_CODE.toString()

    /** "0.1.0 (42)" - the two numbers the Play Console uses to tell builds apart. */
    val versionLine: String get() = "$version ($build)"

    /**
     * Whether this runs on the Android emulator rather than a phone. It decides two things: a debug build
     * plays the test video instead of the camera (see [VideoFrameSource]), and LiteRT runs without XNNPACK.
     */
    val isEmulator: Boolean by lazy {
        Build.HARDWARE in setOf("ranchu", "goldfish") || Build.FINGERPRINT.startsWith("generic") || Build.PRODUCT.contains("sdk")
    }
}
