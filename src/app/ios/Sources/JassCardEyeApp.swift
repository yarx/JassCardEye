import SwiftUI

// One SwiftUI code base for iOS and macOS; iOS ships, and the macOS build is a compile guard built
// by hand. The heavy lifting - the Core ML model - is identical on both anyway; see CardRecognizer.
@main
struct JassCardEyeApp: App {
    var body: some Scene {
        WindowGroup {
            ContentView()
                #if os(macOS)
                .frame(minWidth: 640, minHeight: 560)
                #endif
        }
    }
}
