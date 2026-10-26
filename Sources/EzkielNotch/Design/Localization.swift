import Foundation
import NotchCore

/// UI text in English or Indonesian. English is the key; a missing translation shows the English.
/// The table lives in code (no resource bundle to ship), and the language can differ from macOS's.
enum Lang {
    static var choice: AppLanguage = .auto

    static var isIndonesian: Bool {
        switch choice {
        case .id: return true
        case .en: return false
        case .auto: return Locale.preferredLanguages.first?.hasPrefix("id") ?? false
        }
    }

    static var locale: Locale { isIndonesian ? Locale(identifier: "id_ID") : Locale.current }
}

func L(_ key: String) -> String {
    Lang.isIndonesian ? (Indonesian.strings[key] ?? key) : key
}

/// `L("%d unread", 5)`: the key is a format string.
func L(_ key: String, _ args: CVarArg...) -> String {
    String(format: L(key), locale: Lang.locale, arguments: args)
}
