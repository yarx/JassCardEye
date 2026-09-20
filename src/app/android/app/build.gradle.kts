import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

// Version and build number come from outside, like the iOS release: the marketing version is decided
// by a person, the build number by the release workflow, and src/scripts/lib/release.sh decides both for
// iOS and Android alike.
//   ./gradlew bundleRelease -Pjasscardeye.versionName=0.2.0 -Pjasscardeye.versionCode=1142
// Without a property the version is the iOS app's MARKETING_VERSION, read by that same script instead
// of being parsed a second time here: one app, one version, and the About section of both shows the
// same number.
val appVersionName = providers.gradleProperty("jasscardeye.versionName").orElse(
    providers.exec {
        commandLine("bash", rootProject.file("../../scripts/lib/release.sh").path, "marketing-version")
    }.standardOutput.asText.map { it.trim() }
).get()
val appVersionCodeProperty = providers.gradleProperty("jasscardeye.versionCode").orNull
// 1 for a debug build, which no store ever sees; a release build without one is stopped below.
val appVersionCode = appVersionCodeProperty?.toInt() ?: 1

// The emulator's stand-in for the camera: a video file that is never bundled. Pass a path on the
// device (see src/app/android/README.md, "Testing in the emulator"); empty means the app's own external files
// folder, where `adb push` can put it without any permission.
val testVideo = providers.gradleProperty("jasscardeye.testVideo").orElse("").get()
// The other way round: the emulator's own emulated camera instead of the video, to exercise the CameraX path,
// the permission flow and the camera-refused notice without a phone. Off unless asked for.
//   ./gradlew assembleDebug -Pjasscardeye.emulatorCamera=true
val emulatorCamera = providers.gradleProperty("jasscardeye.emulatorCamera").orElse("false").get().toBoolean()
// For store screenshots taken on the emulator: the test video without its "Testvideo statt Kamera" hint, which is
// not part of the app a buyer sees. The counterpart of JASSCARDEYE_SCREENSHOTS=1 on the iOS simulator.
//   ./gradlew assembleDebug -Pjasscardeye.screenshots=true
val screenshots = providers.gradleProperty("jasscardeye.screenshots").orElse("false").get().toBoolean()

// The upload key for Play. Read from the environment (CI secrets) or from a keystore.properties that
// is never committed; without either, a release build stops - see checkReleaseInputs.
val keystoreProperties = Properties().apply {
    val file = rootProject.file("keystore.properties")
    if (file.exists()) file.inputStream().use { load(it) }
}
fun signingValue(env: String, key: String): String? = System.getenv(env) ?: keystoreProperties.getProperty(key)

// A release bundle Play would refuse is not built by accident. Without this check a build with no key
// would finish and leave the bundle unsigned, and one with no versionCode would quietly carry 1 - both
// noticed only at the upload. The check hangs on R8 and packaging rather than on configuration, so a
// debug build and the release unit tests need neither. The CI job that builds the release only to hold
// R8 and resource shrinking to account says so explicitly:
//   ./gradlew bundleRelease -Pjasscardeye.allowUnsigned=true
val allowUnsigned = providers.gradleProperty("jasscardeye.allowUnsigned").orElse("false").get().toBoolean()
val releaseProblems = buildList {
    if (appVersionCodeProperty == null) add("no versionCode: pass -Pjasscardeye.versionCode=<n>")
    if (signingValue("ANDROID_KEYSTORE_PATH", "storeFile") == null) {
        add("no upload key: set ANDROID_KEYSTORE_PATH, ANDROID_KEYSTORE_PASSWORD, ANDROID_KEY_ALIAS and ANDROID_KEY_PASSWORD, or write src/app/android/keystore.properties")
    }
}.takeUnless { allowUnsigned }.orEmpty()
val checkReleaseInputs = tasks.register("checkReleaseInputs") {
    description = "Stops a release build Google Play would refuse: no upload key, or no versionCode."
    val problems = releaseProblems
    doFirst {
        if (problems.isNotEmpty()) {
            throw GradleException(
                "This release build would not be accepted by Google Play:\n" +
                    problems.joinToString("\n") { "  - $it" } +
                    "\nsrc/scripts/release_android.sh passes both. To build without them on purpose: -Pjasscardeye.allowUnsigned=true"
            )
        }
    }
}
tasks.named { it in setOf("minifyReleaseWithR8", "packageRelease", "packageReleaseBundle", "packageReleaseUniversalApk") }
    .configureEach { dependsOn(checkReleaseInputs) }

android {
    namespace = "ch.yarx.jasscardeye"
    // 37.2 because the Compose BOM requires it; targetSdk and minSdk are decided separately below.
    compileSdk {
        version = release(37) { minorApiLevel = 2 }
    }
    ndkVersion = libs.versions.ndk.get()

    defaultConfig {
        applicationId = "ch.yarx.jasscardeye"
        // Android 10: the first release where a capture can be written to Pictures/ through MediaStore
        // without a storage permission, which is what keeps the captures reachable without adb - and it
        // is also where the device floor of the iOS app (A12, 2018) roughly lands on Android.
        minSdk = 29
        targetSdk = 36
        versionCode = appVersionCode
        versionName = appVersionName
        buildConfigField("String", "TEST_VIDEO", "\"$testVideo\"")
        buildConfigField("boolean", "EMULATOR_CAMERA", "$emulatorCamera")
        buildConfigField("boolean", "SCREENSHOTS", "$screenshots")
    }

    signingConfigs {
        create("upload") {
            val store = signingValue("ANDROID_KEYSTORE_PATH", "storeFile")
            if (store != null) {
                storeFile = file(store)
                storePassword = signingValue("ANDROID_KEYSTORE_PASSWORD", "storePassword")
                keyAlias = signingValue("ANDROID_KEY_ALIAS", "keyAlias")
                keyPassword = signingValue("ANDROID_KEY_PASSWORD", "keyPassword")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            val upload = signingConfigs.getByName("upload")
            if (upload.storeFile != null) signingConfig = upload
            // The native code is LiteRT's, not ours, and arrives stripped. Its symbol table is what turns a
            // raw address in a native crash into a function name. AGP packs it into the bundle's
            // BUNDLE-METADATA, but Play does not list it from there after an upload through the API, so
            // src/scripts/release_android.sh also has AGP write native-debug-symbols.zip and the Release
            // workflow uploads that next to mapping.txt. None of it reaches the phones. SYMBOL_TABLE rather
            // than FULL: a stripped library has no debug info that FULL could add.
            ndk { debugSymbolLevel = "SYMBOL_TABLE" }
        }
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    androidResources {
        // Memory-mapped straight out of the APK; a compressed model would have to be copied first.
        noCompress += "tflite"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(platform(libs.compose.bom))
    implementation(libs.compose.ui)
    implementation(libs.compose.ui.graphics)
    implementation(libs.compose.foundation)
    implementation(libs.compose.material3)
    implementation(libs.compose.material.icons.extended)
    implementation(libs.camera.core)
    implementation(libs.camera.camera2)
    implementation(libs.camera.lifecycle)
    implementation(libs.camera.view)
    implementation(libs.litert)
    implementation(libs.litert.gpu)
    implementation(libs.litert.gpu.api)
    implementation(libs.billing)

    testImplementation(libs.junit)
}
