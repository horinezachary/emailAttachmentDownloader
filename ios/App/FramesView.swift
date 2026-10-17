import SwiftUI

struct FramesView: View {
    @EnvironmentObject private var model: AppModel
    @State private var scanning = false
    @State private var pasting = false
    @State private var pastedText = ""

    var body: some View {
        NavigationStack {
            List {
                if model.config.destinations.isEmpty {
                    Section {
                        Text("A frame is a Phone share account in Email Attachment Downloader on your PC. On the PC open Settings, set Account type to Phone share, press Save, then Show pairing code. Scan it here.")
                            .foregroundStyle(.secondary)
                    }
                }

                ForEach(model.config.destinations) { destination in
                    VStack(alignment: .leading, spacing: 2) {
                        Text(destination.name).font(.headline)
                        Text(destination.toAddress).font(.subheadline).foregroundStyle(.secondary)
                    }
                }
                .onDelete { model.removeDestinations(at: $0) }

                Section {
                    Button { scanning = true } label: {
                        Label("Scan code", systemImage: "qrcode.viewfinder")
                    }
                    Button { pasting = true } label: {
                        Label("Paste link", systemImage: "doc.on.clipboard")
                    }
                }
            }
            .navigationTitle("Frames")
            .toolbar { EditButton() }
            .sheet(isPresented: $scanning) {
                NavigationStack {
                    QRScannerView { text in
                        scanning = false
                        model.handle(pairingText: text)
                    }
                    .ignoresSafeArea()
                    .navigationTitle("Scan pairing code")
                    .navigationBarTitleDisplayMode(.inline)
                    .toolbar {
                        ToolbarItem(placement: .cancellationAction) {
                            Button("Cancel") { scanning = false }
                        }
                    }
                }
            }
            .alert("Paste pairing link", isPresented: $pasting) {
                TextField("ead://pair?...", text: $pastedText)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                Button("Add") {
                    model.handle(pairingText: pastedText)
                    pastedText = ""
                }
                Button("Cancel", role: .cancel) { pastedText = "" }
            } message: {
                Text("Copy the link from the pairing window on your PC.")
            }
        }
    }
}
