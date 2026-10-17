import PhotosUI
import SwiftUI
import UniformTypeIdentifiers

/// A picked photo, copied to a file as soon as it is chosen so a big selection never sits in memory.
struct PickedPhoto: Transferable {
    let url: URL
    let name: String

    static var transferRepresentation: some TransferRepresentation {
        FileRepresentation(importedContentType: .image) { received in
            let folder = FileManager.default.temporaryDirectory.appendingPathComponent("picked-" + UUID().uuidString, isDirectory: true)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let copy = folder.appendingPathComponent(received.file.lastPathComponent)
            try FileManager.default.copyItem(at: received.file, to: copy)
            return PickedPhoto(url: copy, name: received.file.lastPathComponent)
        }
    }
}

struct SendView: View {
    @EnvironmentObject private var model: AppModel
    @State private var selection: [PhotosPickerItem] = []
    @State private var destinationID: UUID?
    @State private var loading = false

    private var destination: Destination? {
        model.config.destinations.first { $0.id == destinationID } ?? model.config.preferredDestination
    }

    var body: some View {
        NavigationStack {
            Form {
                if model.config.destinations.isEmpty {
                    Section {
                        Label("Pair a frame first: open your PC's settings, choose Show pairing code, then scan it on the Frames tab.", systemImage: "qrcode.viewfinder")
                    }
                } else {
                    Section("Send to") {
                        Picker("Frame", selection: Binding(
                            get: { destination?.id ?? model.config.destinations[0].id },
                            set: { destinationID = $0 })
                        ) {
                            ForEach(model.config.destinations) { item in
                                Text(item.name).tag(item.id)
                            }
                        }
                    }
                }

                if !model.config.smtp.isConfigured || !model.hasPassword {
                    Section {
                        Label("Set up the mail account to send through on the Settings tab.", systemImage: "envelope.badge")
                    }
                }

                Section("Photos") {
                    PhotosPicker(selection: $selection, maxSelectionCount: 50, matching: .images) {
                        Label(selection.isEmpty ? "Choose photos" : "\(selection.count) selected. Change", systemImage: "photo.stack")
                    }

                    if let progress = model.progress {
                        ProgressBanner(progress: progress)
                    } else if loading {
                        ProgressView("Reading your selection")
                    }

                    Button {
                        Task { await sendSelection() }
                    } label: {
                        Label("Send", systemImage: "paperplane.fill")
                    }
                    .disabled(selection.isEmpty || destination == nil || model.isSending || loading)
                }

                Section {
                    Text("You can also send from the Photos app: tap Share, then Photo Frame. Or use the Send Photos to a Frame action in Shortcuts.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }
            .navigationTitle("Photo Frame")
        }
    }

    private func sendSelection() async {
        guard let destination else { return }
        loading = true
        var staged: [StagedFile] = []
        let folder = try? BatchStore.shared.newStagingFolder()

        for item in selection {
            do {
                if let photo = try await item.loadTransferable(type: PickedPhoto.self), let folder {
                    let target = folder.appendingPathComponent(UUID().uuidString + "-" + photo.name)
                    try FileManager.default.moveItem(at: photo.url, to: target)
                    staged.append(StagedFile(url: target, name: photo.name))
                }
            } catch {
                model.message = "One photo could not be read: \(error.localizedDescription)"
            }
        }

        loading = false
        selection = []
        await model.send(files: staged, to: destination)
        if let folder {
            try? FileManager.default.removeItem(at: folder)
        }
    }
}
