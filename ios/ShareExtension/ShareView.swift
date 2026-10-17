import SwiftUI

struct ShareView: View {
    @ObservedObject var model: ShareModel

    var body: some View {
        NavigationStack {
            content
                .padding()
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
                .navigationTitle("Photo Frame")
                .navigationBarTitleDisplayMode(.inline)
                .toolbar {
                    ToolbarItem(placement: .cancellationAction) {
                        Button(closeTitle) { model.cancel() }
                    }
                }
        }
    }

    private var closeTitle: String {
        switch model.phase {
        case .sending, .done, .problem, .needsSetup, .nothingToSend: return "Close"
        default: return "Cancel"
        }
    }

    @ViewBuilder
    private var content: some View {
        switch model.phase {
        case .loading:
            ProgressView("Reading your photos")

        case .ready:
            VStack(alignment: .leading, spacing: 16) {
                Label(model.photoCount == 1 ? "1 photo" : "\(model.photoCount) photos", systemImage: "photo.stack")
                    .font(.headline)

                if model.destinations.count > 1 {
                    Picker("Send to", selection: Binding(
                        get: { model.selectedDestination?.id ?? model.destinations[0].id },
                        set: { model.selectedID = $0 })
                    ) {
                        ForEach(model.destinations) { destination in
                            Text(destination.name).tag(destination.id)
                        }
                    }
                    .pickerStyle(.menu)
                } else if let destination = model.selectedDestination {
                    Label(destination.name, systemImage: "paperplane")
                        .foregroundStyle(.secondary)
                }

                Button {
                    Task { await model.send() }
                } label: {
                    Text("Send").frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
            }

        case .sending(let fraction, let text):
            VStack(alignment: .leading, spacing: 12) {
                Text(text)
                ProgressView(value: min(max(fraction, 0), 1))
                Text("Keep this open until it finishes.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            }

        case .done(let text):
            Label(text, systemImage: "checkmark.circle.fill")
                .font(.headline)
                .foregroundStyle(.green)

        case .problem(let text), .needsSetup(let text):
            Label(text, systemImage: "exclamationmark.triangle")
                .foregroundStyle(.orange)

        case .nothingToSend:
            Label("None of the shared items could be read as photos.", systemImage: "photo")
                .foregroundStyle(.secondary)
        }
    }
}
