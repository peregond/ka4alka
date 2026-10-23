import AppKit
import SwiftUI

// MARK: - Ka4alka design system ("KD")
//
// One place for spacing, radii, type, colour roles and the reusable pieces
// every screen shares. All names are prefixed KD / kd so they cannot collide
// with existing types (note: the module already defines `Section`, so never
// refer to SwiftUI.Section from here).
//
// Rules of the system
//  1. System semantic colours for surfaces and text, so Increase Contrast,
//     vibrancy and both appearances work. Hard-code only the brand accent,
//     status colours, rating marks and the dark banner base.
//  2. Glass (macOS 26) only on controls that float above content: toolbar-like
//     groups, toasts, buttons over artwork. Never on whole pages or on every
//     poster. Everything glass is behind `#if compiler(>=6.2)` AND
//     `#available(macOS 26.0, *)` and has a material / opaque fallback.
//  3. Standard actions delegate to native SwiftUI button styles. Never draw
//     pressed/hover/focus states or control borders ourselves.
//  4. Continuous corners everywhere (`.continuous`).

// MARK: Tokens

enum KD {
    enum Space {
        static let xxs: CGFloat = 4
        static let xs: CGFloat = 8
        static let s: CGFloat = 12
        static let m: CGFloat = 16
        static let l: CGFloat = 24
        static let xl: CGFloat = 32
        static let xxl: CGFloat = 48
    }
    enum Radius {
        static let chip: CGFloat = 8
        static let thumb: CGFloat = 10
        static let control: CGFloat = 12
        static let poster: CGFloat = 12
        static let card: CGFloat = 16
        static let hero: CGFloat = 24
    }
    enum Size {
        /// Smallest acceptable pointer target.
        static let minHit: CGFloat = 24
        static let iconButton: CGFloat = 28
        static let posterWidth: CGFloat = 155
        static let wideCardWidth: CGFloat = 230
        static let rowPoster: CGFloat = 48
        static let digestPoster: CGFloat = 92
        static let heroHeight: CGFloat = 300
    }
    enum Width {
        static let minWindow: CGFloat = 760
        static let minWindowHeight: CGFloat = 520
        /// Reading column (descriptions, biographies).
        static let reading: CGFloat = 760
        static let settings: CGFloat = 820
        static let page: CGFloat = 1280
        /// Detail pane widths (not window widths).
        static let compactContent: CGFloat = 640
        static let wideContent: CGFloat = 1100
    }
    static let gutter: CGFloat = 24
    static let compactGutter: CGFloat = 16
    static let posterSpacing: CGFloat = 16
    static let posterRowSpacing: CGFloat = 22
    static let titleBlockHeight: CGFloat = 36

    static func gutter(forWidth width: CGFloat) -> CGFloat { width < Width.compactContent ? compactGutter : gutter }
    /// Tall posters are 2:3, Steam capsules are 460:215.
    static func posterAspect(wide: Bool) -> CGFloat { wide ? 460.0 / 215.0 : 2.0 / 3.0 }
    static func posterColumns(wide: Bool) -> [GridItem] {
        [GridItem(.adaptive(minimum: wide ? 220 : 145, maximum: wide ? 360 : 220), spacing: posterSpacing)]
    }
}

enum KDWidthClass {
    case compact, regular, wide
    init(width: CGFloat) {
        if width < KD.Width.compactContent { self = .compact }
        else if width < KD.Width.wideContent { self = .regular }
        else { self = .wide }
    }
}

enum KDFont {
    /// Hero banner title (banners are fixed size, they do not scale).
    static let display = Font.system(size: 34, weight: .bold)
    static let displayCompact = Font.system(size: 26, weight: .bold)
    static let pageTitle = Font.largeTitle.weight(.bold)
    static let section = Font.title2.weight(.semibold)
    static let cardTitle = Font.title3.weight(.semibold)
    static let rowTitle = Font.body.weight(.semibold)
    static let secondary = Font.callout
    /// Information that matters (year, genre, speeds). Never smaller than this.
    static let meta = Font.subheadline
    static let badge = Font.caption2.weight(.bold)
    static let kicker = Font.caption.weight(.bold)
    static func rank(_ size: CGFloat) -> Font { Font.system(size: size, weight: .heavy, design: .rounded) }
}

// MARK: Colour roles

private enum KDHex {
    static func ns(_ hex: UInt32, alpha: CGFloat = 1) -> NSColor {
        NSColor(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255, blue: CGFloat(hex & 0xFF) / 255, alpha: alpha)
    }
}

/// The accent colour the person picked in System Settings → Appearance. Nil while it is "Multicolor", the default,
/// where the app's own green applies: the rule the system itself uses for an app's AccentColor asset. Sidebar
/// icons, links and selection show the colour the person chose.
private enum KDChosenAccent {
    static var color: NSColor? {
        guard UserDefaults.standard.object(forKey: "AppleAccentColor") != nil else { return nil }
        return NSColor.controlAccentColor
    }
}

private func kdBrandColor(_ appearance: NSAppearance, light: UInt32, dark: UInt32, lightHC: UInt32?, darkHC: UInt32?) -> NSColor {
    let match = appearance.bestMatch(from: [.darkAqua, .aqua, .accessibilityHighContrastDarkAqua, .accessibilityHighContrastAqua])
    switch match {
    case .some(.darkAqua): return KDHex.ns(dark)
    case .some(.accessibilityHighContrastDarkAqua): return KDHex.ns(darkHC ?? dark)
    case .some(.accessibilityHighContrastAqua): return KDHex.ns(lightHC ?? light)
    default: return KDHex.ns(light)
    }
}

extension Color {
    /// Appearance-adaptive colour. `lightHC` / `darkHC` apply under Increase Contrast.
    init(kdLight light: UInt32, dark: UInt32, lightHC: UInt32? = nil, darkHC: UInt32? = nil) {
        self.init(nsColor: NSColor(name: nil) { appearance in
            kdBrandColor(appearance, light: light, dark: dark, lightHC: lightHC, darkHC: darkHC)
        })
    }
    /// An accent role: the app's green, replaced by the person's own system accent colour when they chose one.
    init(kdAccentLight light: UInt32, dark: UInt32, lightHC: UInt32? = nil, darkHC: UInt32? = nil) {
        self.init(nsColor: NSColor(name: nil) { appearance in
            KDChosenAccent.color ?? kdBrandColor(appearance, light: light, dark: dark, lightHC: lightHC, darkHC: darkHC)
        })
    }
    /// Text and icons on an accent fill: the given ink, or white / near-black chosen for contrast on the system accent.
    init(kdAccentInk ink: UInt32) {
        self.init(nsColor: NSColor(name: nil) { appearance in
            guard let chosen = KDChosenAccent.color else { return KDHex.ns(ink) }
            var brightness: CGFloat = 1
            appearance.performAsCurrentDrawingAppearance {
                if let rgb = chosen.usingColorSpace(.sRGB) { brightness = 0.299 * rgb.redComponent + 0.587 * rgb.greenComponent + 0.114 * rgb.blueComponent }
            }
            return brightness > 0.6 ? KDHex.ns(0x06140F) : NSColor.white
        })
    }
}

enum KDPalette {
    /// The interface is neutral, like the Apple TV app, and green is the one colour it adds. Text, icons, progress and
    /// links: a vivid grass green, 5.1:1 on white and 4.7:1 on the light tile, high contrast on dark. Follows the accent
    /// colour chosen in System Settings; the app's green applies while that is "Multicolor".
    static let accent = Color(kdAccentLight: 0x057F34, dark: 0x5BE3B5, lightHC: 0x04652A, darkHC: 0x8CF0CB)
    /// Fill of primary buttons and progress: a bright emerald-grass in light. Always paired with `accentInk` (7.8:1).
    static let accentFill = Color(kdAccentLight: 0x1ABF62, dark: 0x5BE3B5)
    static let accentInk = Color(kdAccentInk: 0x06140F)
    static let accentSoft = accent.opacity(0.14)
    static let accentLine = accent.opacity(0.45)

    /// A filled "saved" heart: the red of a Like.
    static let like = Color(kdLight: 0xFF3B30, dark: 0xFF453A)
    static let danger = Color(kdLight: 0xC2362B, dark: 0xFF8A7A)
    static let warning = Color(kdLight: 0x9A5B00, dark: 0xF2B35B)
    static let success = Color(kdLight: 0x0A7A3C, dark: 0x5FD98F)
    static let info = Color(kdLight: 0x2F62D6, dark: 0x7FA9FF)

    /// Rating source marks (same values as the Windows app).
    static let kpMark = Color(red: 1, green: 0.333, blue: 0)
    static let imdbMark = Color(red: 0.961, green: 0.773, blue: 0.094)

    /// Theme-independent dark base under banners, so white text is always legible.
    static let bannerTop = Color(red: 0.11, green: 0.11, blue: 0.12)
    static let bannerBottom = Color(red: 0, green: 0, blue: 0)

    /// Neutral surfaces, as in the Apple TV app: a black page with dark-grey tiles, or a white page with light-grey
    /// tiles. Nothing is tinted, so the artwork carries the colour.
    private static func adaptive(dark: UInt32, light: NSColor) -> Color {
        Color(nsColor: NSColor(name: nil) { appearance in
            let match = appearance.bestMatch(from: [.darkAqua, .aqua, .accessibilityHighContrastDarkAqua, .accessibilityHighContrastAqua])
            if match == .darkAqua || match == .accessibilityHighContrastDarkAqua { return KDHex.ns(dark) }
            return light
        })
    }
    static let pageBackground = adaptive(dark: 0x000000, light: KDHex.ns(0xFFFFFF))
    static let surface = adaptive(dark: 0x1C1C1E, light: KDHex.ns(0xF5F5F7))
    static let hairline = adaptive(dark: 0x2C2C2E, light: .separatorColor)
    static let skeleton = Color.primary.opacity(0.08)
}

enum KDTone: Equatable {
    case neutral, accent, success, warning, danger, info
    var foreground: Color {
        switch self {
        case .neutral: return Color.secondary
        case .accent: return KDPalette.accent
        case .success: return KDPalette.success
        case .warning: return KDPalette.warning
        case .danger: return KDPalette.danger
        case .info: return KDPalette.info
        }
    }
    var background: Color {
        switch self {
        case .neutral: return Color.primary.opacity(0.07)
        default: return foreground.opacity(0.14)
        }
    }
}

// MARK: Environment

/// Height of the strip under the window toolbar at the top of the content column. A full-bleed banner starts at the
/// top of the window, under the toolbar, and adds this to its height so the part below the toolbar keeps its size.
private struct KDSafeTopKey: EnvironmentKey { static let defaultValue: CGFloat = 0 }
extension EnvironmentValues {
    var kdSafeTop: CGFloat {
        get { self[KDSafeTopKey.self] }
        set { self[KDSafeTopKey.self] = newValue }
    }
}

/// Set by a page that opens with a full-bleed banner. The banner runs up under the toolbar, so such a page has no
/// toolbar bar and no title there; every other page gets the system's toolbar bar with the page name.
struct KDHeroPageKey: PreferenceKey {
    static let defaultValue = false
    static func reduce(value: inout Bool, nextValue: () -> Bool) { value = value || nextValue() }
}

private struct KDLiteKey: EnvironmentKey { static let defaultValue = false }
extension EnvironmentValues {
    /// Set once at the root from `settings.liteMode`; slows or removes decorative motion.
    var kdLite: Bool {
        get { self[KDLiteKey.self] }
        set { self[KDLiteKey.self] = newValue }
    }
}

// MARK: Formatting (follows the app language, not the system locale)

enum KDFormat {
    static var isEnglish: Bool { UserDefaults.standard.string(forKey: "UiLanguage") == "en" }
    static var locale: Locale { Locale(identifier: isEnglish ? "en_US" : "ru_RU") }

    static func bytes(_ value: Double) -> String {
        var amount = max(0, value.isFinite ? value : 0)
        let units = isEnglish ? ["B", "KB", "MB", "GB", "TB"] : ["Б", "КБ", "МБ", "ГБ", "ТБ"]
        var index = 0
        while index < units.count - 1, amount >= 999.5 { amount /= 1000; index += 1 }
        let digits: ClosedRange<Int> = index == 0 ? 0...0 : (amount >= 100 ? 0...0 : 0...1)
        let number = amount.formatted(.number.precision(.fractionLength(digits)).locale(locale))
        return number + " " + units[index]
    }
    static func rate(_ bytesPerSecond: Double) -> String { bytes(bytesPerSecond) + tr("/с", "/s") }
    /// Floors instead of rounding, so 99.96 never reads as a finished 100.
    static func percent(_ value: Double, fractionDigits: Int = 0) -> String {
        let clamped = min(100, max(0, value.isFinite ? value : 0))
        let scale = pow(10.0, Double(fractionDigits))
        let floored = (clamped * scale).rounded(.down) / scale
        return floored.formatted(.number.precision(.fractionLength(fractionDigits)).locale(locale)) + "%"
    }
}

// MARK: Surfaces

enum KDCardLevel { case raised, sunken }

struct KDCardModifier: ViewModifier {
    @Environment(\.colorSchemeContrast) private var contrast
    var radius: CGFloat
    var level: KDCardLevel
    @ViewBuilder func body(content: Content) -> some View {
        let shape = RoundedRectangle(cornerRadius: radius, style: .continuous)
        content
            .background(level == .raised ? KDPalette.surface : Color.primary.opacity(0.04), in: shape)
            // A tile is a fill, not an outlined box; the outline returns only under Increase Contrast.
            .overlay { if contrast == .increased { shape.strokeBorder(KDPalette.hairline, lineWidth: 1.5).allowsHitTesting(false) } }
    }
}

/// Floating chrome: glass on macOS 26, material on 14/15, opaque with Reduce Transparency.
struct KDSurfaceModifier<S: InsettableShape>: ViewModifier {
    @Environment(\.kdLite) private var lite
    @Environment(\.accessibilityReduceTransparency) private var reduceTransparency
    @Environment(\.colorSchemeContrast) private var contrast
    var shape: S
    var tint: Color?
    var interactive: Bool
    var onArtwork: Bool

    @ViewBuilder func body(content: Content) -> some View {
        if lite || reduceTransparency { solid(content) } else { glassOrMaterial(content) }
    }

    private var lineWidth: CGFloat { contrast == .increased ? 1.5 : 0.5 }

    @ViewBuilder private func solid(_ content: Content) -> some View {
        content
            .background(onArtwork ? Color.black.opacity(0.78) : KDPalette.surface, in: shape)
            .overlay(shape.strokeBorder(KDPalette.hairline, lineWidth: lineWidth).allowsHitTesting(false))
    }

    @ViewBuilder private func material(_ content: Content) -> some View {
        let style: Material = onArtwork ? .ultraThinMaterial : .regularMaterial
        content
            .background(style, in: shape)
            .overlay { if let tint { shape.fill(tint.opacity(0.18)).allowsHitTesting(false) } }
            .overlay(shape.strokeBorder(KDPalette.hairline, lineWidth: lineWidth).allowsHitTesting(false))
    }

    #if compiler(>=6.2)
    @available(macOS 26.0, *)
    private var glassStyle: Glass {
        var glass: Glass = .regular
        if let tint { glass = glass.tint(tint) }
        if interactive { glass = glass.interactive() }
        return glass
    }
    #endif

    @ViewBuilder private func glassOrMaterial(_ content: Content) -> some View {
        #if compiler(>=6.2)
        if #available(macOS 26.0, *) {
            content.glassEffect(glassStyle, in: shape)
        } else {
            material(content)
        }
        #else
        material(content)
        #endif
    }
}

extension View {
    /// Content-layer card: opaque control background with a hairline that
    /// strengthens under Increase Contrast.
    func kdCard(radius: CGFloat = KD.Radius.card, level: KDCardLevel = .raised) -> some View {
        modifier(KDCardModifier(radius: radius, level: level))
    }
    /// Floating glass / material / opaque surface. Use `onArtwork` for controls over posters and banners.
    func kdSurface<S: InsettableShape>(in shape: S, tint: Color? = nil, interactive: Bool = false, onArtwork: Bool = false) -> some View {
        modifier(KDSurfaceModifier(shape: shape, tint: tint, interactive: interactive, onArtwork: onArtwork))
    }
    /// White text on the always-dark banner base.
    func kdOnBanner() -> some View {
        foregroundStyle(Color.white).environment(\.colorScheme, .dark)
    }
    /// Pinned header that content scrolls under (page title + filters).
    func kdPinnedHeader<H: View>(@ViewBuilder _ header: () -> H) -> some View {
        safeAreaInset(edge: .top, spacing: 0) {
            header()
                // The page colour, not the bar material: over the near-black dark page the material was a light grey slab.
                .background(KDPalette.pageBackground)
                .overlay(alignment: .bottom) { Rectangle().fill(KDPalette.hairline).frame(height: 1).allowsHitTesting(false) }
        }
    }
    /// Hides or shows the window toolbar's own bar (macOS 15 and later; before it the system bar stays).
    @ViewBuilder func kdToolbarBarHidden(_ hidden: Bool) -> some View {
        if #available(macOS 15.0, *) { self.toolbarBackgroundVisibility(hidden ? .hidden : .automatic, for: .windowToolbar) } else { self }
    }
    /// Hides the scroll-edge effect at the top of a page. At the top the banner runs under the toolbar and the system's
    /// edge effect would draw a flat darker strip across the picture; it is shown again once the page is scrolled.
    @ViewBuilder func kdTopScrollEdgeHidden(_ hidden: Bool) -> some View {
        #if compiler(>=6.2)
        if #available(macOS 26.0, *) { self.scrollEdgeEffectHidden(hidden, for: .top) } else { self }
        #else
        self
        #endif
    }
    /// macOS 26 soft scroll-edge effect under the toolbar; no-op before.
    @ViewBuilder func kdSoftScrollEdge(_ on: Bool = true) -> some View {
        #if compiler(>=6.2)
        if #available(macOS 26.0, *) { self.scrollEdgeEffectStyle(on ? .soft : .hard, for: .top) } else { self }
        #else
        self
        #endif
    }
    /// macOS 26 backdrop extension under toolbar and sidebar; no-op before.
    @ViewBuilder func kdBackdropExtension() -> some View {
        #if compiler(>=6.2)
        if #available(macOS 26.0, *) { self.backgroundExtensionEffect() } else { self }
        #else
        self
        #endif
    }
    /// Measures the top safe-area inset of this view: the strip under the window toolbar.
    func kdReadSafeTop(_ inset: Binding<CGFloat>) -> some View {
        background {
            GeometryReader { proxy in
                Color.clear.onChange(of: proxy.safeAreaInsets.top, initial: true) { _, value in
                    if abs(inset.wrappedValue - value) > 0.5 { inset.wrappedValue = value }
                }
            }
        }
    }
    /// Measures the height of this view (the sidebar column, to fit as many download rows as the window allows).
    func kdReadHeight(_ height: Binding<CGFloat>) -> some View {
        background {
            GeometryReader { proxy in
                Color.clear.onChange(of: proxy.size.height, initial: true) { _, value in
                    if abs(height.wrappedValue - value) > 0.5 { height.wrappedValue = value }
                }
            }
        }
    }
    /// Measures the width of this view (use on the detail column, not the window).
    func kdReadWidth(_ width: Binding<CGFloat>) -> some View {
        background {
            GeometryReader { proxy in
                Color.clear.onChange(of: proxy.size.width, initial: true) { _, value in
                    if abs(width.wrappedValue - value) > 0.5 { width.wrappedValue = value }
                }
            }
        }
    }
}


/// Groups nearby glass controls so they morph and sample together on macOS 26.
struct KDGlassGroup<Content: View>: View {
    var spacing: CGFloat
    let content: Content
    init(spacing: CGFloat = KD.Space.s, @ViewBuilder content: () -> Content) {
        self.spacing = spacing
        self.content = content()
    }
    var body: some View {
        #if compiler(>=6.2)
        if #available(macOS 26.0, *) {
            GlassEffectContainer(spacing: spacing) { content }
        } else {
            content
        }
        #else
        content
        #endif
    }
}

// MARK: Banner base

struct KDBannerBase: View {
    var body: some View {
        LinearGradient(colors: [KDPalette.bannerTop, KDPalette.bannerBottom], startPoint: .topTrailing, endPoint: .bottomLeading)
    }
}

struct KDBannerScrim: View {
    var body: some View {
        ZStack {
            LinearGradient(colors: [Color.black.opacity(0.05), Color.black.opacity(0.78)], startPoint: .top, endPoint: .bottom)
            LinearGradient(colors: [Color.black.opacity(0.55), Color.clear], startPoint: .leading, endPoint: .trailing)
        }.allowsHitTesting(false)
    }
}

// MARK: Buttons

// MARK: Liquid Glass controls

enum KDGlass {
    /// True when `glassEffect` exists at run time. Everything glass is also behind `#if compiler(>=6.2)`.
    static var available: Bool {
        #if compiler(>=6.2)
        if #available(macOS 26.0, *) { return true }
        #endif
        return false
    }
}

/// Capsule (or circle) Liquid Glass drawn with `glassEffect`, the way the buttons over artwork are. The system glass
/// styles draw a rounded rectangle at regular size on macOS 27, which differed from those capsules: drawing every
/// control through the same path keeps one shape and one material. `tint` makes the prominent, filled variant. Lite
/// mode, Reduce Transparency, Increase Contrast and macOS before 26 get a flat fill of the same shape.
private struct KDGlassControlChrome<S: InsettableShape>: ViewModifier {
    var shape: S
    var tint: Color?
    /// A solid fill, for the filled primary button: tinted glass showed no colour at all on a plain page, so the
    /// button looked like every other one. The rim highlight keeps it glassy.
    var fill: Color? = nil
    @Environment(\.kdLite) private var lite
    @Environment(\.accessibilityReduceTransparency) private var reduceTransparency
    @Environment(\.colorSchemeContrast) private var contrast
    @ViewBuilder func body(content: Content) -> some View {
        if let fill {
            content
                .background(fill, in: shape)
                .overlay(
                    shape.strokeBorder(LinearGradient(colors: [Color.white.opacity(0.5), Color.white.opacity(0.1)], startPoint: .top, endPoint: .bottom), lineWidth: 1)
                        .allowsHitTesting(false)
                )
                .shadow(color: fill.opacity(0.35), radius: 5, y: 2)
                .contentShape(shape)
        } else if lite || reduceTransparency || contrast == .increased || !KDGlass.available {
            if let tint {
                content
                    .background(tint, in: shape)
                    .overlay(shape.strokeBorder(Color.white.opacity(0.2), lineWidth: 1).allowsHitTesting(false))
                    .contentShape(shape)
            } else {
                content
                    .background(Color.primary.opacity(0.06), in: shape)
                    .overlay(shape.strokeBorder(KDPalette.hairline, lineWidth: contrast == .increased ? 1.5 : 1).allowsHitTesting(false))
                    .contentShape(shape)
            }
        } else {
            content.kdSurface(in: shape, tint: tint, interactive: true).contentShape(shape)
        }
    }
}

/// The shared body of the filled and the quiet glass button.
private struct KDGlassButtonBody: View {
    let configuration: ButtonStyleConfiguration
    let prominent: Bool
    @Environment(\.isEnabled) private var enabled
    @Environment(\.controlSize) private var controlSize
    private var metrics: (h: CGFloat, v: CGFloat, font: Font) {
        switch controlSize {
        case .mini, .small: return (10, 4, Font.callout)
        case .large, .extraLarge: return (18, 8, Font.body)
        default: return (14, 6, Font.body)
        }
    }
    var body: some View {
        configuration.label
            .font(metrics.font.weight(prominent ? .semibold : .regular))
            .foregroundStyle(prominent ? KDPalette.accentInk : Color.primary)
            .padding(.horizontal, metrics.h)
            .padding(.vertical, metrics.v)
            .modifier(KDGlassControlChrome(shape: Capsule(), tint: nil, fill: prominent ? KDPalette.accentFill : nil))
            .opacity(configuration.isPressed ? 0.85 : (enabled ? 1 : 0.45))
    }
}

/// The one filled action of a screen or card: green Liquid Glass with dark text.
struct KDPrimaryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View { KDGlassButtonBody(configuration: configuration, prominent: true) }
}

/// Every other button: clear Liquid Glass with the primary text colour.
struct KDSecondaryButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View { KDGlassButtonBody(configuration: configuration, prominent: false) }
}

/// A round glass button for an icon, at least as large as the minimum pointer target.
struct KDGlassCircleButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View { KDGlassCircleBody(configuration: configuration) }
}
private struct KDGlassCircleBody: View {
    let configuration: ButtonStyleConfiguration
    @Environment(\.isEnabled) private var enabled
    var body: some View {
        configuration.label
            .font(.system(size: 13, weight: .semibold))
            .frame(minWidth: KD.Size.iconButton, minHeight: KD.Size.iconButton)
            .modifier(KDGlassControlChrome(shape: Circle(), tint: nil))
            .opacity(configuration.isPressed ? 0.85 : (enabled ? 1 : 0.45))
    }
}

/// Controls over artwork (hero, poster actions): glass on 26, dark material before. `prominent` is the one filled
/// action of a row: the same capsule and size as its neighbours, white with black text like the Play button of the
/// Apple TV app and the hero's Details button, so it never differs in shape or height from the buttons beside it.
struct KDOverlayButtonStyle: ButtonStyle {
    var circular = false
    var solid = false
    var prominent = false
    func makeBody(configuration: Configuration) -> some View { KDOverlayButtonBody(configuration: configuration, circular: circular, solid: solid, prominent: prominent) }
}
private struct KDOverlayButtonBody: View {
    let configuration: ButtonStyleConfiguration
    let circular: Bool
    let solid: Bool
    let prominent: Bool
    @Environment(\.isEnabled) private var enabled
    var body: some View {
        let label = configuration.label
            .font(.callout.weight(.semibold))
            .foregroundStyle(prominent ? Color.black : Color.white)
            .padding(.horizontal, circular ? 0 : 14)
            .padding(.vertical, circular ? 0 : 7)
            .frame(minWidth: circular ? KD.Size.iconButton : nil, minHeight: circular ? KD.Size.iconButton : nil)
        Group {
            if circular { chrome(label, shape: Circle()) } else { chrome(label, shape: Capsule()) }
        }
        .opacity(configuration.isPressed ? 0.8 : (enabled ? 1 : 0.45))
    }
    @ViewBuilder private func chrome<V: View, S: InsettableShape>(_ view: V, shape: S) -> some View {
        if prominent {
            view
                .background(Color.white, in: shape)
                .contentShape(shape)
        } else if solid {
            view
                .background(Color.black.opacity(0.78), in: shape)
                .overlay(shape.strokeBorder(Color.white.opacity(0.22), lineWidth: 1).allowsHitTesting(false))
                .contentShape(shape)
        } else {
            view.kdSurface(in: shape, interactive: true, onArtwork: true)
                .contentShape(shape)
                .environment(\.colorScheme, .dark)
        }
    }
}

/// System icon buttons keep native focus and activation; only the minimum label size is supplied.
struct KDIconButtonStyle: PrimitiveButtonStyle {
    var surface = true
    func makeBody(configuration: Configuration) -> some View {
        let button = Button(role: configuration.role) { configuration.trigger() } label: {
            configuration.label.frame(minWidth: KD.Size.minHit, minHeight: KD.Size.minHit)
        }
        if surface { button.buttonStyle(KDGlassCircleButtonStyle()) }
        else { button.buttonStyle(.borderless) }
    }
}

/// Always provides a tooltip and a VoiceOver label.
struct KDIconButton: View {
    var systemImage: String
    var label: String
    var tone: KDTone = .neutral
    var action: () -> Void
    var body: some View {
        Button(role: tone == .danger ? .destructive : nil, action: action) { Image(systemName: systemImage) }
            .buttonStyle(KDIconButtonStyle())
            .help(label)
            .accessibilityLabel(label)
    }
}

/// Poster / hero / tile button: hover ring, focus ring, pressed dim. No cursor games.
struct KDPosterButtonStyle: ButtonStyle {
    var radius: CGFloat = KD.Radius.poster
    func makeBody(configuration: Configuration) -> some View { KDPosterButtonBody(configuration: configuration, radius: radius) }
}
private struct KDPosterButtonBody: View {
    let configuration: ButtonStyleConfiguration
    let radius: CGFloat
    @Environment(\.isFocused) private var focused
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var hovered = false
    /// The ring marks keyboard focus. With the mouse the first control of the window holds focus from the start, and
    /// its ring around the whole hero banner looked like a stray green outline.
    private var showsFocus: Bool { focused && NSApp.isFullKeyboardAccessEnabled }
    var body: some View {
        configuration.label
            .opacity(configuration.isPressed ? 0.85 : 1)
            .overlay {
                RoundedRectangle(cornerRadius: radius + 2, style: .continuous)
                    .strokeBorder(KDPalette.accent.opacity(showsFocus || hovered ? 0.95 : 0), lineWidth: showsFocus ? 3 : 2)
                    .padding(-3)
                    .allowsHitTesting(false)
            }
            .focusEffectDisabled()
            .onHover { hovered = $0 }
            .animation(reduceMotion ? nil : .easeOut(duration: 0.12), value: hovered)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.12), value: showsFocus)
    }
}

/// Toolbar button: icon only, tooltip, VoiceOver title (survives toolbar overflow) and optional shortcut.
struct KDToolbarButton: View {
    var title: String
    var systemImage: String
    var shortcut: KeyEquivalent? = nil
    var modifiers: EventModifiers = .command
    var isDisabled = false
    var action: () -> Void
    var body: some View {
        let button = Button(action: action) { Label(title, systemImage: systemImage).labelStyle(.iconOnly) }
            .buttonStyle(KDIconButtonStyle(surface: false))
            .help(title)
            .disabled(isDisabled)
        if let shortcut { button.keyboardShortcut(shortcut, modifiers: modifiers) } else { button }
    }
}

// MARK: Chips, badges, pills

/// Labels carry content and selection only; native buttons and menus draw their own chrome.
struct KDChipLabel: View {
    var text: String
    var systemImage: String? = nil
    var isActive = false
    var body: some View {
        HStack(spacing: 5) {
            if let systemImage { Image(systemName: systemImage) }
            if isActive { Image(systemName: "checkmark").accessibilityHidden(true) }
            Text(text).lineLimit(1)
        }
    }
}

/// A filter chip's capsule. A `Menu` in the `.button` menu style takes its look from the button style, so the chip
/// gets the same glass as every other button; the borderless and bordered menu styles hand the label to AppKit, which
/// drops any background and reorders the label's image to the front.
struct KDChipButtonStyle: ButtonStyle {
    var isActive = false
    @Environment(\.isEnabled) private var enabled
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.callout.weight(isActive ? .semibold : .regular))
            .foregroundStyle(isActive ? KDPalette.accent : Color.primary)
            .padding(.horizontal, 12)
            .padding(.vertical, 5)
            .modifier(KDGlassControlChrome(shape: Capsule(), tint: isActive ? KDPalette.accent.opacity(0.3) : nil))
            .opacity(configuration.isPressed ? 0.85 : (enabled ? 1 : 0.45))
    }
}

/// The content of a chip that opens a menu: the label and a chevron.
struct KDChipMenuContent: View {
    var text: String
    var systemImage: String? = nil
    var isActive = false
    var body: some View {
        HStack(spacing: 6) {
            KDChipLabel(text: text, systemImage: systemImage, isActive: isActive)
            Image(systemName: "chevron.down").font(.system(size: 9, weight: .bold)).opacity(0.7).accessibilityHidden(true)
        }
    }
}

extension View {
    /// Draws a `Menu` as a glass chip.
    func kdChipMenu(active: Bool = false) -> some View {
        menuStyle(.button).menuIndicator(.hidden).buttonStyle(KDChipButtonStyle(isActive: active))
    }
}

/// A filter as a menu chip: the title when unset, "Title: value" tint when set.
struct KDChipMenu<Content: View>: View {
    var title: String
    var value: String?
    let content: Content
    init(title: String, value: String? = nil, @ViewBuilder content: () -> Content) {
        self.title = title
        self.value = value
        self.content = content()
    }
    private var active: Bool { !(value ?? "").isEmpty }
    var body: some View {
        Menu { content } label: { KDChipMenuContent(text: active ? (value ?? title) : title, isActive: active) }
            .kdChipMenu(active: active)
            .frame(minHeight: 24)
            // Not fixedSize: a long value (a release group name) has to be able to truncate instead of pushing
            // the row past the window edge. The cap keeps an extreme value from taking the whole row.
            .frame(maxWidth: 320, alignment: .leading)
            .contentShape(Rectangle())
            .accessibilityLabel(title)
            .accessibilityValue(value ?? "")
            .settingsLayoutProbe("menu." + title, kind: .control)
    }
}

/// Dark rounded tag drawn on posters (quality, price, "Скоро"). One shape for all.
struct KDPosterBadge: View {
    var text: String
    var systemImage: String? = nil
    var tint: Color = .white
    var body: some View {
        HStack(spacing: 3) {
            if let systemImage { Image(systemName: systemImage).font(.system(size: 9, weight: .bold)) }
            Text(text).font(KDFont.badge).lineLimit(1)
        }
        .foregroundStyle(tint)
        .padding(.horizontal, 7)
        .padding(.vertical, 4)
        .background(Color.black.opacity(0.72), in: RoundedRectangle(cornerRadius: 7, style: .continuous))
    }
}

/// Small status pill (Победитель, Качается, Ошибка).
struct KDStatusPill: View {
    var text: String
    var tone: KDTone = .neutral
    var systemImage: String? = nil
    init(_ text: String, tone: KDTone = .neutral, systemImage: String? = nil) {
        self.text = text
        self.tone = tone
        self.systemImage = systemImage
    }
    var body: some View {
        HStack(spacing: 4) {
            if let systemImage { Image(systemName: systemImage).font(.system(size: 10, weight: .semibold)) }
            Text(text).font(.caption.weight(.semibold)).lineLimit(1)
        }
        .foregroundStyle(tone.foreground)
        .padding(.horizontal, 8)
        .padding(.vertical, 3)
        .background(tone.background, in: Capsule())
    }
}

// MARK: Ratings

enum KDRatingSource {
    case kinopoisk, imdb, other
    /// Accepts the engine's source names ("Кинопоиск", "КП", "kp", "IMDb").
    init(name: String?) {
        let text = (name ?? "").lowercased()
        if text.contains("кино") || text == "кп" || text == "kp" || text.contains("kinopoisk") { self = .kinopoisk }
        else if text.contains("imdb") { self = .imdb }
        else { self = .other }
    }
    var tag: String? {
        switch self { case .kinopoisk: return "КП"; case .imdb: return "IMDb"; case .other: return nil }
    }
    var fill: Color {
        switch self { case .kinopoisk: return KDPalette.kpMark; case .imdb: return KDPalette.imdbMark; case .other: return Color.clear }
    }
    var ink: Color {
        switch self { case .kinopoisk: return Color.white; case .imdb: return Color.black; case .other: return Color.white }
    }
    var spokenName: String {
        switch self { case .kinopoisk: return tr("Кинопоиск", "Kinopoisk"); case .imdb: return "IMDb"; case .other: return tr("Рейтинг", "Rating") }
    }
}

/// Orange КП / yellow IMDb mark followed by the value. Unknown source falls back to a star.
struct KDRatingMark: View {
    var source: KDRatingSource
    var value: String
    var valueColor: Color = .white
    var compact = false
    var body: some View {
        HStack(spacing: 4) {
            if let tag = source.tag {
                Text(tag)
                    .font(.system(size: compact ? 8 : 9, weight: .heavy))
                    .foregroundStyle(source.ink)
                    .fixedSize(horizontal: true, vertical: true)
                    .padding(.horizontal, 4)
                    .padding(.vertical, 1.5)
                    .background(source.fill, in: RoundedRectangle(cornerRadius: 3, style: .continuous))
            } else {
                Image(systemName: "star.fill").font(.system(size: compact ? 10 : 11)).foregroundStyle(KDPalette.imdbMark)
            }
            Text(value).font((compact ? Font.caption : Font.callout).weight(.bold).monospacedDigit()).foregroundStyle(valueColor)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(source.spokenName + " " + value)
    }
}

/// Dark rating chip on a poster.
struct KDRatingChip: View {
    var source: KDRatingSource
    var value: String
    var body: some View {
        KDRatingMark(source: source, value: value, compact: true)
            .padding(.horizontal, 6)
            .padding(.vertical, 4)
            .background(Color.black.opacity(0.75), in: RoundedRectangle(cornerRadius: 7, style: .continuous))
    }
}

struct KDRating: Identifiable {
    var source: KDRatingSource
    var value: String
    var id: String { source.tag ?? "other" + value }
    /// Hides empty and "—" values.
    var isUsable: Bool { !value.isEmpty && value != "—" }
}

/// Both ratings side by side (hero, detail banner).
struct KDRatingRow: View {
    var ratings: [KDRating]
    var valueColor: Color = .white
    var body: some View {
        HStack(spacing: KD.Space.s) {
            ForEach(ratings.filter { $0.isUsable }) { rating in
                KDRatingMark(source: rating.source, value: rating.value, valueColor: valueColor)
            }
        }
    }
}

struct KDRankNumeral: View {
    var rank: Int
    var size: CGFloat = 88
    var body: some View {
        Text(String(rank))
            .font(KDFont.rank(size))
            .monospacedDigit()
            .foregroundStyle(Color.primary.opacity(0.85))
            .shadow(color: Color.black.opacity(0.25), radius: 4, y: 1)
            .accessibilityHidden(true)
    }
}

// MARK: Headers

struct KDPageHeader<Trailing: View>: View {
    var title: String
    var subtitle: String?
    let trailing: Trailing
    init(_ title: String, subtitle: String? = nil, @ViewBuilder trailing: () -> Trailing) {
        self.title = title
        self.subtitle = subtitle
        self.trailing = trailing()
    }
    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: KD.Space.m) {
            VStack(alignment: .leading, spacing: 4) {
                Text(title).font(KDFont.pageTitle).lineLimit(2).accessibilityAddTraits(.isHeader)
                if let subtitle, !subtitle.isEmpty {
                    Text(subtitle).font(KDFont.secondary).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer(minLength: KD.Space.m)
            trailing
        }
    }
}
extension KDPageHeader where Trailing == EmptyView {
    init(_ title: String, subtitle: String? = nil) { self.init(title, subtitle: subtitle) { EmptyView() } }
}

/// Shelf / card heading: optional grey kicker, title as a VoiceOver heading, one "more" action.
struct KDSectionHeader: View {
    var title: String
    var kicker: String? = nil
    var subtitle: String? = nil
    var actionTitle: String? = nil
    var action: (() -> Void)? = nil
    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            if let kicker, !kicker.isEmpty {
                Text(kicker).font(KDFont.kicker).textCase(.uppercase).foregroundStyle(Color.secondary)
            }
            HStack(alignment: .firstTextBaseline) {
                Text(title).font(KDFont.section).lineLimit(2).accessibilityAddTraits(.isHeader)
                Spacer(minLength: KD.Space.s)
                if let actionTitle, let action {
                    Button(action: action) {
                        HStack(spacing: 4) {
                            Text(actionTitle)
                            Image(systemName: "chevron.forward").font(.system(size: 11, weight: .semibold)).accessibilityHidden(true)
                        }
                        .font(.callout.weight(.semibold))
                        .foregroundStyle(KDPalette.accent)
                        .padding(.vertical, 4)
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel(actionTitle + ": " + title)
                }
            }
            if let subtitle, !subtitle.isEmpty {
                Text(subtitle).font(KDFont.secondary).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

// MARK: Empty / error / toast

struct KDEmptyState: View {
    var title: String
    var systemImage: String
    var message: String? = nil
    var actionTitle: String? = nil
    var action: (() -> Void)? = nil
    var body: some View {
        ContentUnavailableView {
            Label(title, systemImage: systemImage)
        } description: {
            if let message { Text(message) }
        } actions: {
            if let actionTitle, let action { Button(actionTitle, action: action) }
        }
    }
}

/// Error / notice card. Selectable text, labelled dismiss, optional retry.
struct KDBanner: View {
    var message: String
    var tone: KDTone = .danger
    var systemImage = "exclamationmark.triangle.fill"
    var actionTitle: String? = nil
    var onAction: (() -> Void)? = nil
    var onDismiss: (() -> Void)? = nil
    var body: some View {
        let shape = RoundedRectangle(cornerRadius: KD.Radius.control, style: .continuous)
        HStack(alignment: .top, spacing: KD.Space.s) {
            Image(systemName: systemImage).foregroundStyle(tone.foreground).accessibilityHidden(true).padding(.top, 2)
            Text(message).font(.callout).lineLimit(3).textSelection(.enabled).help(message)
                .frame(maxWidth: .infinity, alignment: .leading)
            if let actionTitle, let onAction { Button(actionTitle, action: onAction).buttonStyle(KDSecondaryButtonStyle()).controlSize(.small) }
            if let onDismiss { KDIconButton(systemImage: "xmark", label: tr("Закрыть", "Dismiss"), action: onDismiss) }
        }
        .padding(KD.Space.s)
        // A later background sits behind the earlier one, so the tint is added first and the surface goes behind it.
        .background(shape.fill(tone.foreground.opacity(0.08)))
        .background(KDPalette.surface, in: shape)
        .overlay(shape.strokeBorder(tone.foreground.opacity(0.4), lineWidth: 1).allowsHitTesting(false))
        .accessibilityElement(children: .contain)
    }
}

struct KDToast: View {
    var text: String
    var onDismiss: () -> Void
    var body: some View {
        HStack(spacing: KD.Space.xs) {
            Text(text).font(.callout).lineLimit(2).fixedSize(horizontal: false, vertical: true)
            KDIconButton(systemImage: "xmark", label: tr("Закрыть", "Dismiss"), action: onDismiss)
        }
        .padding(.leading, KD.Space.m)
        .padding(.trailing, KD.Space.xxs)
        .padding(.vertical, KD.Space.xxs)
        .frame(maxWidth: 560)
        .kdSurface(in: Capsule(), interactive: false)
        .shadow(color: Color.black.opacity(0.18), radius: 10, y: 3)
    }
}

/// Floating, centred, self-dismissing toast that VoiceOver announces. Put it on the detail column:
/// `.kdToast($model.toast)`.
struct KDToastModifier: ViewModifier {
    @Binding var text: String?
    var seconds: Double
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    /// Changes on every new toast, so showing the same text twice restarts the dismiss timer.
    @State private var stamp = UUID()
    func body(content: Content) -> some View {
        content
            .overlay(alignment: .bottom) {
                if let value = text {
                    KDToast(text: value) { text = nil }
                        .padding(.bottom, KD.Space.l)
                        .padding(.horizontal, KD.Space.m)
                        .transition(reduceMotion ? AnyTransition.opacity : AnyTransition.move(edge: .bottom).combined(with: .opacity))
                }
            }
            .animation(reduceMotion ? nil : .easeOut(duration: 0.2), value: text)
            .onChange(of: text) { _, next in
                if next != nil { stamp = UUID() }
            }
            .task(id: stamp) {
                guard let value = text else { return }
                AccessibilityNotification.Announcement(value).post()
                try? await Task.sleep(for: .seconds(seconds))
                if !Task.isCancelled, text == value { text = nil }
            }
    }
}
extension View {
    func kdToast(_ text: Binding<String?>, seconds: Double = 4) -> some View {
        modifier(KDToastModifier(text: text, seconds: seconds))
    }
}

// MARK: Skeletons and shimmer

private struct KDShimmerSweep: View {
    @Environment(\.colorScheme) private var scheme
    var time: TimeInterval
    var lane: Int
    var body: some View {
        // 1.1 s sweep inside a 1.9 s cycle, lanes offset by 70 ms (same cadence as Windows).
        let cycle = 1.9
        let t = (time + Double(lane) * 0.07).truncatingRemainder(dividingBy: cycle)
        let progress = CGFloat(min(1, t / 1.1))
        GeometryReader { proxy in
            LinearGradient(colors: [Color.clear, Color.white.opacity(scheme == .dark ? 0.10 : 0.65), Color.clear], startPoint: .leading, endPoint: .trailing)
                .frame(width: proxy.size.width * 0.55)
                .offset(x: -proxy.size.width * 0.55 + progress * proxy.size.width * 1.55)
        }
        .allowsHitTesting(false)
    }
}

/// A placeholder block with a soft left-to-right shimmer. Still under Reduce Motion;
/// 12 fps in Lite mode.
struct KDSkeletonBlock: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.kdLite) private var lite
    @Environment(\.kdShimmerTime) private var sharedTime
    var radius: CGFloat = KD.Radius.chip
    var lane = 0

    private var shape: RoundedRectangle {
        RoundedRectangle(cornerRadius: radius, style: .continuous)
    }

    private func shimmering(time: TimeInterval) -> some View {
        shape.fill(KDPalette.skeleton)
            .overlay { KDShimmerSweep(time: time, lane: lane).clipShape(shape) }
    }

    @ViewBuilder
    private var animated: some View {
        if let time = sharedTime {
            shimmering(time: time)
        } else {
            TimelineView(.animation(minimumInterval: lite ? 1.0 / 12.0 : 1.0 / 30.0)) { context in
                shimmering(time: context.date.timeIntervalSinceReferenceDate)
            }
        }
    }

    var body: some View {
        if reduceMotion {
            shape.fill(KDPalette.skeleton)
        } else {
            animated
        }
    }
}

private struct KDShimmerTimeKey: EnvironmentKey { static let defaultValue: TimeInterval? = nil }
extension EnvironmentValues {
    /// Shared animation clock for skeleton blocks. When set by `KDShimmerClock`, every block under it
    /// reads this time instead of owning its own TimelineView.
    var kdShimmerTime: TimeInterval? {
        get { self[KDShimmerTimeKey.self] }
        set { self[KDShimmerTimeKey.self] = newValue }
    }
}

/// One TimelineView for a whole skeleton (30 fps, 12 fps in Lite mode). Still under Reduce Motion.
private struct KDShimmerClock<Content: View>: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.kdLite) private var lite
    private let content: Content
    init(@ViewBuilder content: () -> Content) { self.content = content() }
    var body: some View {
        if reduceMotion {
            content
        } else {
            TimelineView(.animation(minimumInterval: lite ? 1.0 / 12.0 : 1.0 / 30.0)) { context in
                content.environment(\.kdShimmerTime, context.date.timeIntervalSinceReferenceDate)
            }
        }
    }
}

/// Same metrics as a real PosterCard (poster, 7 pt gap, 36 pt title block, meta line).
struct KDPosterSkeleton: View {
    var wide = false
    var lane = 0
    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            KDSkeletonBlock(radius: KD.Radius.poster, lane: lane).aspectRatio(KD.posterAspect(wide: wide), contentMode: .fit)
            VStack(alignment: .leading, spacing: 6) {
                KDSkeletonBlock(radius: 4, lane: lane).frame(height: 14)
                KDSkeletonBlock(radius: 4, lane: lane).frame(width: 90, height: 14)
            }.frame(height: KD.titleBlockHeight, alignment: .top)
            KDSkeletonBlock(radius: 4, lane: lane).frame(width: 110, height: 11)
        }
    }
}

struct KDPosterGridSkeleton: View {
    var wide = false
    var count = 12
    var body: some View {
        KDShimmerClock {
            LazyVGrid(columns: KD.posterColumns(wide: wide), alignment: .leading, spacing: KD.posterRowSpacing) {
                ForEach(0..<count, id: \.self) { index in KDPosterSkeleton(wide: wide, lane: index % 8) }
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(tr("Загрузка…", "Loading…"))
    }
}

/// A shelf: heading bar and one row of posters.
struct KDShelfSkeleton: View {
    var wide = false
    var count = 6
    var body: some View {
        KDShimmerClock {
            VStack(alignment: .leading, spacing: KD.Space.s) {
                KDSkeletonBlock(radius: 5).frame(width: 180, height: 22)
                posterRow
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(tr("Загрузка…", "Loading…"))
    }

    /// A non-interactive scroll view takes the proposed width and cuts off the overflow,
    /// so a narrow window does not widen the skeleton.
    private var posterRow: some View {
        let cardWidth: CGFloat = wide ? KD.Size.wideCardWidth : KD.Size.posterWidth
        return ScrollView(.horizontal, showsIndicators: false) {
            HStack(alignment: .top, spacing: KD.posterSpacing) {
                ForEach(0..<count, id: \.self) { index in
                    KDPosterSkeleton(wide: wide, lane: index).frame(width: cardWidth)
                }
            }
        }
        .scrollDisabled(true)
    }
}

/// Hero placeholder on the same dark base as the real banner, so nothing flashes.
struct KDHeroSkeleton: View {
    var height: CGFloat = KD.Size.heroHeight
    /// False for a banner that runs edge to edge.
    var rounded = true
    var body: some View {
        let shape = RoundedRectangle(cornerRadius: rounded ? KD.Radius.hero : 0, style: .continuous)
        KDShimmerClock {
            ZStack(alignment: .bottomLeading) {
                KDBannerBase()
                VStack(alignment: .leading, spacing: KD.Space.s) {
                    KDSkeletonBlock(radius: 5).frame(width: 110, height: 12)
                    KDSkeletonBlock(radius: 7).frame(maxWidth: 320).frame(height: 30)
                    KDSkeletonBlock(radius: 5).frame(width: 180, height: 13)
                }.padding(28)
            }
            .environment(\.colorScheme, .dark)
        }
        .frame(height: height)
        .clipShape(shape)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(tr("Загрузка…", "Loading…"))
    }
}

// MARK: Layout

/// Wrapping row layout for filter chips and tag rows (no hidden horizontal scroll).
struct KDFlowLayout: Layout {
    var spacing: CGFloat = 8
    var lineSpacing: CGFloat = 8
    var verticalAlignment: VerticalAlignment = .top

    /// The size a child takes. A child wider than the whole row is offered the row width instead,
    /// so a long label truncates rather than pushing the row past the window edge.
    private func childSize(_ subview: LayoutSubview, maxWidth: CGFloat) -> CGSize {
        let ideal: CGSize = subview.sizeThatFits(.unspecified)
        if maxWidth.isFinite, ideal.width > maxWidth {
            let narrowed: CGSize = subview.sizeThatFits(ProposedViewSize(width: maxWidth, height: nil))
            return CGSize(width: min(narrowed.width, maxWidth), height: narrowed.height)
        }
        return ideal
    }

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let maxWidth: CGFloat = proposal.width ?? .infinity
        var x: CGFloat = 0
        var y: CGFloat = 0
        var rowHeight: CGFloat = 0
        var usedWidth: CGFloat = 0
        for subview in subviews {
            let size: CGSize = childSize(subview, maxWidth: maxWidth)
            if x > 0, x + size.width > maxWidth {
                y += rowHeight + lineSpacing
                x = 0
                rowHeight = 0
            }
            usedWidth = max(usedWidth, x + size.width)
            x += size.width + spacing
            rowHeight = max(rowHeight, size.height)
        }
        return CGSize(width: usedWidth, height: y + rowHeight)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let sizes = subviews.map { childSize($0, maxWidth: bounds.width) }
        var rowStart = 0
        var rowWidth: CGFloat = 0
        var rowHeight: CGFloat = 0
        var y = bounds.minY
        func placeRow(endingAt end: Int) {
            var x = bounds.minX
            for index in rowStart..<end {
                let size = sizes[index]
                let offset: CGFloat = verticalAlignment == .center ? (rowHeight - size.height) / 2
                    : verticalAlignment == .bottom ? rowHeight - size.height : 0
                subviews[index].place(at: CGPoint(x: x, y: y + offset), anchor: .topLeading, proposal: ProposedViewSize(size))
                x += size.width + spacing
            }
        }
        for index in sizes.indices {
            let size = sizes[index]
            if index > rowStart, rowWidth + spacing + size.width > bounds.width {
                placeRow(endingAt: index)
                y += rowHeight + lineSpacing
                rowStart = index; rowWidth = 0; rowHeight = 0
            }
            rowWidth += (index > rowStart ? spacing : 0) + size.width
            rowHeight = max(rowHeight, size.height)
        }
        placeRow(endingAt: sizes.count)
    }
}

/// Horizontal shelf with edge-to-edge scrolling, snapping and hover arrows for mouse users.
struct KDShelf<Item: Identifiable, Cell: View>: View {
    var items: [Item]
    var spacing: CGFloat
    var step: Int
    let cell: (Item) -> Cell
    @State private var position: Item.ID?
    @State private var hovering = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    init(_ items: [Item], spacing: CGFloat = KD.posterSpacing, step: Int = 3, @ViewBuilder cell: @escaping (Item) -> Cell) {
        self.items = items
        self.spacing = spacing
        self.step = step
        self.cell = cell
    }

    var body: some View {
        ScrollView(.horizontal) {
            LazyHStack(alignment: .top, spacing: spacing) {
                ForEach(items) { item in cell(item) }
            }
            .scrollTargetLayout()
            .padding(.vertical, 4)
        }
        .scrollIndicators(.hidden)
        .scrollPosition(id: $position)
        .scrollTargetBehavior(.viewAligned)
        .contentMargins(.horizontal, KD.gutter, for: .scrollContent)
        .scrollClipDisabled()
        .onHover { hovering = $0 }
        .overlay(alignment: .leading) { arrow(direction: -1) }
        .overlay(alignment: .trailing) { arrow(direction: 1) }
    }

    private var currentIndex: Int {
        guard let position, let index = items.firstIndex(where: { $0.id == position }) else { return 0 }
        return index
    }

    @ViewBuilder private func arrow(direction: Int) -> some View {
        let target = currentIndex + direction * step
        let available = direction < 0 ? currentIndex > 0 : currentIndex < items.count - step
        if hovering, available, items.count > step {
            Button {
                let clamped = min(max(0, target), items.count - 1)
                if reduceMotion { position = items[clamped].id } else { withAnimation(.easeInOut(duration: 0.25)) { position = items[clamped].id } }
            } label: {
                Image(systemName: direction < 0 ? "chevron.backward" : "chevron.forward")
            }
            .buttonStyle(KDOverlayButtonStyle(circular: true))
            .padding(.horizontal, KD.Space.xs)
            .help(direction < 0 ? tr("Назад", "Back") : tr("Дальше", "Next"))
            .accessibilityLabel(direction < 0 ? tr("Прокрутить назад", "Scroll back") : tr("Прокрутить дальше", "Scroll forward"))
        }
    }
}

// MARK: Sidebar symbols

enum KDNavSymbol {
    /// `key` is Section.rawValue: featured, movies, series, games, saved, downloads, settings.
    /// The open section gets a solid icon, as on Windows; Актуальное uses a star.
    static func symbol(for key: String, selected: Bool) -> String {
        switch key {
        case "featured": return selected ? "star.fill" : "star"
        case "movies": return selected ? "film.fill" : "film"
        case "series": return selected ? "tv.fill" : "tv"
        case "games": return selected ? "gamecontroller.fill" : "gamecontroller"
        case "saved": return selected ? "heart.fill" : "heart"
        case "downloads": return selected ? "arrow.down.circle.fill" : "arrow.down.circle"
        case "settings": return selected ? "gearshape.fill" : "gearshape"
        default: return "circle"
        }
    }
}
