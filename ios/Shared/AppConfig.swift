import Foundation

/// Identifiers and locations shared by the app and the share extension.
///
/// The App Group and keychain group must match the entitlements in `project.yml`. If you change the bundle
/// identifier prefix there, change `appGroupID` and `backgroundTaskID` here to match.
enum AppConfig {
    static let appGroupID = "group.dev.horine.ead"
    static let urlScheme = "ead"
    static let backgroundTaskID = "dev.horine.ead.send-queue"
    static let keychainService = "dev.horine.ead.smtp"

    /// The shared keychain group, read from Info.plist (`KeychainAccessGroup`, filled in from the build settings so the
    /// team prefix is never hard-coded). Nil in unsigned builds, where the default keychain group is used instead.
    static var keychainAccessGroup: String? {
        let value = Bundle.main.object(forInfoDictionaryKey: "KeychainAccessGroup") as? String
        guard let value, !value.isEmpty, !value.contains("$(") else { return nil }
        return value
    }

    /// The folder the app and the share extension both read and write.
    static var containerURL: URL {
        if let url = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: appGroupID) {
            return url
        }

        // Unsigned simulator and test builds have no group container.
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("EADFallback", isDirectory: true)
    }
}

extension String {
    var trimmed: String { trimmingCharacters(in: .whitespacesAndNewlines) }
}
