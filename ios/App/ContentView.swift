import SwiftUI

struct ContentView: View {
    @EnvironmentObject private var model: AppModel

    var body: some View {
        TabView {
            SendView()
                .tabItem { Label("Send", systemImage: "paperplane") }

            OutboxView()
                .tabItem { Label("Outbox", systemImage: "tray.full") }
                .badge(model.unfinishedCount)

            FramesView()
                .tabItem { Label("Frames", systemImage: "photo.on.rectangle.angled") }

            SettingsView()
                .tabItem { Label("Settings", systemImage: "gearshape") }
        }
        .alert("Photo Frame", isPresented: Binding(
            get: { model.message != nil },
            set: { if !$0 { model.message = nil } })
        ) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(model.message ?? "")
        }
        .sheet(item: $model.pendingPairing) { destination in
            PairingConfirmView(destination: destination)
        }
    }
}

/// Asks before a scanned or opened pairing code is added, because anyone can make a link that opens this app.
struct PairingConfirmView: View {
    @EnvironmentObject private var model: AppModel
    let destination: Destination

    var body: some View {
        NavigationStack {
            VStack(alignment: .leading, spacing: 16) {
                Label(destination.name, systemImage: "photo.on.rectangle.angled")
                    .font(.title2.bold())
                Text("Photos you send to this frame are e-mailed to \(destination.toAddress), with a code in the subject that tells your PC to save them.")
                Text("Only add a code you got from your own PC's Email Attachment Downloader.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
                Spacer()
            }
            .padding()
            .navigationTitle("Add this frame?")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { model.pendingPairing = nil }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Add") { model.confirmPairing(destination) }
                }
            }
        }
        .presentationDetents([.medium])
    }
}

/// A bar that shows what the sender is doing.
struct ProgressBanner: View {
    let progress: SendProgress

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(progress.text).font(.subheadline)
            ProgressView(value: min(max(progress.fraction, 0), 1))
        }
        .padding(.vertical, 4)
    }
}
