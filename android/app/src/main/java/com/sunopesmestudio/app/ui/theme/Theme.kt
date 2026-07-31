package com.sunopesmestudio.app.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * All five desktop themes (section 18.2 of the build spec), adapted for
 * mobile rather than a straight port of the desktop layout. Each gets its
 * own ColorScheme + Shapes pair mirroring the structural language of its
 * app/web/style.css counterpart (angular vs rounded vs glass), not a single
 * shape set with swapped colors.
 */
enum class AppTheme(val id: String, val label: String) {
    ORIGINAL("default", "Original Suno"),
    NEON_DISTRICT("neon-district", "Neon District"),
    URBAN_CONCRETE("urban-concrete", "Urban Concrete"),
    MIDNIGHT_STUDIO("midnight-studio", "Midnight Studio"),
    AURORA_GLASS("aurora-glass", "Aurora Glass"),
}

private val OriginalColors = darkColorScheme(
    primary = Color(0xFF8B5CF6),
    onPrimary = Color.White,
    secondary = Color(0xFF6D42D8),
    background = Color(0xFF0B0D12),
    surface = Color(0xFF121620),
    surfaceVariant = Color(0xFF171C27),
    onBackground = Color(0xFFF5F7FB),
    onSurface = Color(0xFFF5F7FB),
    error = Color(0xFFFF5D6C),
)

private val NeonDistrictColors = darkColorScheme(
    primary = Color(0xFF00E5FF),
    onPrimary = Color(0xFF031018),
    secondary = Color(0xFFFF2BD6),
    background = Color(0xFF050914),
    surface = Color(0xFF0A1220),
    surfaceVariant = Color(0xFF0D1826),
    onBackground = Color(0xFFEAF6FF),
    onSurface = Color(0xFFEAF6FF),
    error = Color(0xFFFF4D7D),
)

private val UrbanConcreteColors = darkColorScheme(
    primary = Color(0xFFC6FF3A),
    onPrimary = Color(0xFF141412),
    secondary = Color(0xFFFF8A1E),
    background = Color(0xFF141412),
    surface = Color(0xFF1C1C19),
    surfaceVariant = Color(0xFF232320),
    onBackground = Color(0xFFF2F1EA),
    onSurface = Color(0xFFF2F1EA),
    error = Color(0xFFFF4D2E),
)

private val MidnightStudioColors = darkColorScheme(
    primary = Color(0xFF6C8CFF),
    onPrimary = Color.White,
    secondary = Color(0xFF4A63CC),
    background = Color(0xFF0A0A0C),
    surface = Color(0xFF131317),
    surfaceVariant = Color(0xFF17171C),
    onBackground = Color(0xFFE8E9EE),
    onSurface = Color(0xFFE8E9EE),
    error = Color(0xFFFF5470),
)

private val AuroraGlassColors = darkColorScheme(
    primary = Color(0xFF7EE8FA),
    onPrimary = Color(0xFF0A0A14),
    secondary = Color(0xFFC084FC),
    background = Color(0xFF080A14),
    surface = Color(0xFF12121F),
    surfaceVariant = Color(0xFF171728),
    onBackground = Color(0xFFF3F0FF),
    onSurface = Color(0xFFF3F0FF),
    error = Color(0xFFFB7185),
)

// Original: soft rounded surfaces, matching the desktop's rounded cards.
private val OriginalShapes = Shapes(
    extraSmall = RoundedCornerShape(6.dp),
    small = RoundedCornerShape(9.dp),
    medium = RoundedCornerShape(14.dp),
    large = RoundedCornerShape(18.dp),
    extraLarge = RoundedCornerShape(28.dp),
)

// Neon District: clipped/angular corners (asymmetric single-corner cut),
// mirroring the clip-path treatment used on the desktop CSS for this theme.
private val NeonDistrictShapes = Shapes(
    extraSmall = RoundedCornerShape(0.dp),
    small = RoundedCornerShape(topStart = 0.dp, topEnd = 10.dp, bottomEnd = 0.dp, bottomStart = 0.dp),
    medium = RoundedCornerShape(topStart = 0.dp, topEnd = 16.dp, bottomEnd = 0.dp, bottomStart = 16.dp),
    large = RoundedCornerShape(topStart = 0.dp, topEnd = 20.dp, bottomEnd = 0.dp, bottomStart = 20.dp),
    extraLarge = RoundedCornerShape(0.dp),
)

// Urban Concrete: hard square corners everywhere, matching the desktop
// theme's border-radius:0 + hard drop-shadow industrial look.
private val UrbanConcreteShapes = Shapes(
    extraSmall = RoundedCornerShape(0.dp),
    small = RoundedCornerShape(0.dp),
    medium = RoundedCornerShape(0.dp),
    large = RoundedCornerShape(0.dp),
    extraLarge = RoundedCornerShape(0.dp),
)

// Midnight Studio: small consistent radii, dense professional feel.
private val MidnightStudioShapes = Shapes(
    extraSmall = RoundedCornerShape(3.dp),
    small = RoundedCornerShape(6.dp),
    medium = RoundedCornerShape(8.dp),
    large = RoundedCornerShape(10.dp),
    extraLarge = RoundedCornerShape(14.dp),
)

// Aurora Glass: large, very rounded shapes for a soft glassmorphism feel.
private val AuroraGlassShapes = Shapes(
    extraSmall = RoundedCornerShape(10.dp),
    small = RoundedCornerShape(14.dp),
    medium = RoundedCornerShape(20.dp),
    large = RoundedCornerShape(26.dp),
    extraLarge = RoundedCornerShape(36.dp),
)

private val AppTypography = Typography(
    headlineMedium = TextStyle(fontWeight = FontWeight.Bold, fontSize = 26.sp),
    titleLarge = TextStyle(fontWeight = FontWeight.SemiBold, fontSize = 20.sp),
    bodyLarge = TextStyle(fontSize = 15.sp, lineHeight = 21.sp),
    labelLarge = TextStyle(fontWeight = FontWeight.Bold, fontSize = 13.sp),
)

// Urban Concrete swaps in a bolder, uppercase-leaning label style to match
// its "city signage" desktop typography (text-transform:uppercase,
// font-weight:900 on nav/buttons/badges).
private val UrbanConcreteTypography = AppTypography.copy(
    labelLarge = TextStyle(fontWeight = FontWeight.Black, fontSize = 13.sp),
    titleLarge = TextStyle(fontWeight = FontWeight.Black, fontSize = 20.sp),
)

@Composable
fun SunoPesmeStudioTheme(
    appTheme: AppTheme,
    useSystemDarkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    // All five themes are dark-first (matching the desktop originals, none
    // of which default to a light theme); useSystemDarkTheme is threaded
    // through for the eventual light Arctic palette-swap variant that
    // exists on desktop but hasn't been ported here.
    val colors = when (appTheme) {
        AppTheme.ORIGINAL -> OriginalColors
        AppTheme.NEON_DISTRICT -> NeonDistrictColors
        AppTheme.URBAN_CONCRETE -> UrbanConcreteColors
        AppTheme.MIDNIGHT_STUDIO -> MidnightStudioColors
        AppTheme.AURORA_GLASS -> AuroraGlassColors
    }
    val shapes = when (appTheme) {
        AppTheme.ORIGINAL -> OriginalShapes
        AppTheme.NEON_DISTRICT -> NeonDistrictShapes
        AppTheme.URBAN_CONCRETE -> UrbanConcreteShapes
        AppTheme.MIDNIGHT_STUDIO -> MidnightStudioShapes
        AppTheme.AURORA_GLASS -> AuroraGlassShapes
    }
    val typography = if (appTheme == AppTheme.URBAN_CONCRETE) UrbanConcreteTypography else AppTypography
    MaterialTheme(
        colorScheme = colors,
        shapes = shapes,
        typography = typography,
        content = content,
    )
}
