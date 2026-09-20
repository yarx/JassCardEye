import SwiftUI

/// The app root. It owns the detection model for the whole app lifetime - the settings and the
/// loaded CoreML model live there - while the camera and per-frame inference only run inside a
/// counting session (`ScanView`).
struct ContentView: View {
    @State private var model = LiveDetectionModel()
    /// The purchase, for the whole app lifetime. Every screen that locks reads it from the
    /// environment, so the one value is the same everywhere.
    @State private var store = Store()

    var body: some View {
        HomeView(model: model)
            .environment(store)
    }
}
