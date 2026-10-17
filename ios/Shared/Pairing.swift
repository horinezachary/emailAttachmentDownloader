import Foundation

enum PairingError: LocalizedError, Equatable {
    case notAPairingLink
    case unsupportedVersion
    case missing(String)
    case invalid(String)

    var errorDescription: String? {
        switch self {
        case .notAPairingLink:
            return "That is not a pairing code from Email Attachment Downloader."
        case .unsupportedVersion:
            return "This pairing code is from a newer version. Update the iPhone app."
        case .missing(let field):
            return "The pairing code has no \(field)."
        case .invalid(let field):
            return "The pairing code has an invalid \(field)."
        }
    }
}

/// Reads the pairing link the Windows app shows as a QR code: `ead://pair?v=1&name=...&to=...&tag=...`.
/// It carries the destination only, never a password.
enum Pairing {
    static let supportedVersion = 1

    static func parse(_ text: String) throws -> Destination {
        guard let components = URLComponents(string: text.trimmed),
              components.scheme?.lowercased() == AppConfig.urlScheme,
              components.host?.lowercased() == "pair"
        else {
            throw PairingError.notAPairingLink
        }

        func value(_ name: String) -> String? {
            components.queryItems?.first { $0.name.lowercased() == name }?.value
        }

        guard let versionText = value("v"), let version = Int(versionText), version >= 1 else {
            throw PairingError.notAPairingLink
        }
        guard version <= supportedVersion else {
            throw PairingError.unsupportedVersion
        }

        guard let name = value("name")?.trimmed, !name.isEmpty else { throw PairingError.missing("name") }
        guard let to = value("to")?.trimmed, !to.isEmpty else { throw PairingError.missing("address") }
        guard SmtpAddress.isSafe(to) else { throw PairingError.invalid("address") }
        guard let tag = value("tag"), !tag.isEmpty else { throw PairingError.missing("tag") }
        guard isValidTag(tag) else { throw PairingError.invalid("tag") }

        return Destination(name: name, toAddress: to, tag: tag)
    }

    /// Lowercase letters and digits, 6 to 32 of them. The Windows app applies the same rule.
    static func isValidTag(_ tag: String) -> Bool {
        guard (6...32).contains(tag.utf8.count) else { return false }
        return tag.utf8.allSatisfy { ($0 >= 0x61 && $0 <= 0x7A) || ($0 >= 0x30 && $0 <= 0x39) }
    }
}

/// Addresses go into SMTP commands and headers, so anything that could end a line or a command is refused.
enum SmtpAddress {
    static func isSafe(_ address: String) -> Bool {
        let text = address.trimmed
        guard text.count >= 3, text.count <= 254, text.contains("@"), !text.hasPrefix("@"), !text.hasSuffix("@") else {
            return false
        }

        let forbidden = CharacterSet.whitespacesAndNewlines
            .union(.controlCharacters)
            .union(CharacterSet(charactersIn: "<>,;\"\\"))
        return text.rangeOfCharacter(from: forbidden) == nil
            && text.filter({ $0 == "@" }).count == 1
    }
}
