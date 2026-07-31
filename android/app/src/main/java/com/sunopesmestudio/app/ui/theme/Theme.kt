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
 * Two of the five desktop themes (section 18.2 of the build spec), adapted
 * for mobile rather than a straight port of the desktop layout: Original
 * Suno (rounded, soft-purple, matches the desktop default) and Neon
 * District (angular corners, cyan/magenta HUD accents, matching the
 * structural redesign in app/web/style.css's neon-district block, not
 * just a recolor of the same shapes).
 */
enum class AppTheme(val id: String, val label: String) {
    ORIGINAL("default", "Original Suno"),
    NEON_DISTRICT("neon-district", "Neon District"),
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

private val AppTypography = Typography(
    headlineMedium = TextStyle(fontWeight = FontWeight.Bold, fontSize = 26.sp),
    titleLarge = TextStyle(fontWeight = FontWeight.SemiBold, fontSize = 20.sp),
    bodyLarge = TextStyle(fontSize = 15.sp, lineHeight = 21.sp),
    labelLarge = TextStyle(fontWeight = FontWeight.Bold, fontSize = 13.sp),
)

@Composable
fun SunoPesmeStudioTheme(
    appTheme: AppTheme,
    useSystemDarkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    // Both current themes are dark-first (matching the desktop originals);
    // useSystemDarkTheme is threaded through for the eventual light Arctic
    // variant this project already has on desktop and hasn't ported yet.
    val colors = when (appTheme) {
        AppTheme.ORIGINAL -> OriginalColors
        AppTheme.NEON_DISTRICT -> NeonDistrictColors
    }
    val shapes = when (appTheme) {
        AppTheme.ORIGINAL -> OriginalShapes
        AppTheme.NEON_DISTRICT -> NeonDistrictShapes
    }
    MaterialTheme(
        colorScheme = colors,
        shapes = shapes,
        typography = AppTypography,
        content = content,
    )
}
