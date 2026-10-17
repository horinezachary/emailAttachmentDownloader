import Foundation

/// One Phone share account on a PC, as learned from its pairing code.
struct Destination: Codable, Identifiable, Equatable, Hashable {
    var id = UUID()
    var name: String
    var toAddress: String
    var tag: String

    /// What goes in the subject so the PC picks the message up. Must stay identical to `ShareTags.MarkerFor` in the
    /// Windows app (`[ead:TAG]`).
    var subjectMarker: String { "[ead:\(tag)]" }
}

/// The mail account the phone sends through. The password is not here; it lives in the keychain (`PasswordStore`).
struct SmtpSettings: Codable, Equatable {
    enum Security: String, Codable, CaseIterable, Identifiable {
        case implicitTLS
        case startTLS

        var id: String { rawValue }

        var label: String {
            switch self {
            case .implicitTLS: return "SSL/TLS (usually port 465)"
            case .startTLS: return "STARTTLS (usually port 587)"
            }
        }
    }

    var host: String = "smtp.gmail.com"
    var port: Int = 465
    var security: Security = .implicitTLS
    var username: String = ""

    /// Blank means the user name.
    var fromAddress: String = ""

    var effectiveFrom: String {
        let from = fromAddress.trimmed
        return from.isEmpty ? username.trimmed : from
    }

    var isConfigured: Bool {
        !host.trimmed.isEmpty
            && (1...65535).contains(port)
            && !username.trimmed.isEmpty
            && SmtpAddress.isSafe(effectiveFrom)
    }
}

/// How photos are prepared before they are e-mailed.
struct ImageOptions: Codable, Equatable {
    /// Windows cannot show HEIC without an add-on, so iPhone photos are sent as JPEG by default.
    var convertHEICToJPEG = true

    /// Longest side in pixels. 0 means never shrink. 3840 keeps a 4K frame sharp and a photo near 2 to 4 MB.
    var maxDimension = 3840

    var jpegQuality = 0.85
}

enum ItemState: String, Codable {
    case pending
    case sent
    case failed
}

struct OutgoingItem: Codable, Identifiable, Equatable {
    var id = UUID()

    /// The name the person would recognise.
    var fileName: String

    /// The name of the copy inside the batch's folder.
    var storedName: String

    var state: ItemState = .pending
    var error: String?
}

/// Photos waiting to be mailed to one destination. Kept on disk until every photo is sent.
struct OutgoingBatch: Codable, Identifiable, Equatable {
    var id = UUID()
    var createdAt = Date()
    var destination: Destination
    var items: [OutgoingItem]
    var lastError: String?

    var unsentCount: Int { items.filter { $0.state != .sent }.count }
    var isDone: Bool { unsentCount == 0 }
}

/// The file the app and the extension both keep their settings in.
struct StoredConfig: Codable, Equatable {
    var destinations: [Destination] = []
    var smtp = SmtpSettings()
    var options = ImageOptions()
    var lastDestinationID: UUID?

    private enum CodingKeys: String, CodingKey {
        case destinations, smtp, options, lastDestinationID
    }

    init() {}

    /// Tolerates missing keys, so a later version that adds a setting can still read an older file.
    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        destinations = try container.decodeIfPresent([Destination].self, forKey: .destinations) ?? []
        smtp = try container.decodeIfPresent(SmtpSettings.self, forKey: .smtp) ?? SmtpSettings()
        options = try container.decodeIfPresent(ImageOptions.self, forKey: .options) ?? ImageOptions()
        lastDestinationID = try container.decodeIfPresent(UUID.self, forKey: .lastDestinationID)
    }

    /// The destination to preselect: the last one used, else the only one.
    var preferredDestination: Destination? {
        if let id = lastDestinationID, let match = destinations.first(where: { $0.id == id }) {
            return match
        }
        return destinations.first
    }
}

/// A photo copied into a staging folder, waiting to become part of a batch.
struct StagedFile: Equatable {
    var url: URL
    var name: String
}
