package com.sunopesmestudio.app.ui.suno

import android.annotation.SuppressLint
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.webkit.CookieManager
import android.webkit.JavascriptInterface
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ListItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import com.sunopesmestudio.app.network.SunoClip

/**
 * Real Suno account login, not a stub: an embedded WebView loads the
 * actual suno.com login page, the user authenticates directly with Suno
 * (this app never sees their password), and once Clerk (Suno's auth
 * provider) has an active session, a small JS snippet mirroring
 * app/cdp.py's refresh_session_activity() mints a bearer token via
 * window.Clerk.session.getToken() and hands it back through a
 * JavascriptInterface bridge.
 *
 * Known limitation, surfaced to the user below rather than hidden:
 * Google's own sign-in policy blocks its OAuth page inside generic
 * embedded WebViews ("This browser or app may not be secure"). Suno's
 * own email/password (or any non-Google) login is not affected.
 */
@SuppressLint("SetJavaScriptEnabled")
@Composable
fun SunoConnectScreen(viewModel: SunoViewModel, modifier: Modifier = Modifier) {
    val state by viewModel.state.collectAsState()

    if (state.connected) {
        SunoLibraryContent(state = state, viewModel = viewModel, modifier = modifier)
    } else {
        SunoLoginContent(onTokenCaptured = viewModel::onTokenCaptured, modifier = modifier)
    }
}

private fun isTrustedSunoPage(url: String?): Boolean {
    if (url.isNullOrBlank()) return false
    val uri = runCatching { Uri.parse(url) }.getOrNull() ?: return false
    if (!uri.scheme.equals("https", ignoreCase = true)) return false
    val host = uri.host?.lowercase() ?: return false
    return host == "suno.com" || host.endsWith(".suno.com")
}

private class SunoOnlyWebViewClient : WebViewClient() {
    override fun shouldOverrideUrlLoading(view: WebView?, request: WebResourceRequest?): Boolean {
        val url = request?.url?.toString()
        // Only main-frame Suno HTTPS navigation belongs inside the credential
        // surface. Subresources continue to load normally; unrelated top-level
        // destinations are blocked instead of inheriting AndroidBridge.
        return request?.isForMainFrame == true && !isTrustedSunoPage(url)
    }
}

@Composable
private fun SunoLoginContent(onTokenCaptured: (String) -> Unit, modifier: Modifier = Modifier) {
    var webViewRef by remember { mutableStateOf<WebView?>(null) }
    var checking by remember { mutableStateOf(false) }

    Column(modifier = modifier.fillMaxSize()) {
        Text(
            "Prijavi se na svoj Suno nalog ispod, pa dodirni „Proveri prijavu“. " +
                "Lozinku unosiš direktno na Suno stranici, ova aplikacija je nikad ne vidi. " +
                "Napomena: dugme „Nastavi sa Google“ možda neće raditi u ovom prozoru " +
                "(Google iz bezbednosnih razloga blokira svoju prijavu unutar ugrađenih " +
                "prikaza) -- ako se to desi, koristi imejl/lozinku prijavu.",
            style = MaterialTheme.typography.bodyMedium,
            modifier = Modifier.padding(16.dp),
        )
        AndroidView(
            modifier = Modifier.fillMaxWidth().weight(1f),
            factory = { context ->
                CookieManager.getInstance().setAcceptCookie(true)
                WebView(context).apply {
                    settings.javaScriptEnabled = true
                    settings.domStorageEnabled = true
                    CookieManager.getInstance().setAcceptThirdPartyCookies(this, true)
                    webViewClient = SunoOnlyWebViewClient()
                    addJavascriptInterface(
                        object {
                            @JavascriptInterface
                            fun onToken(token: String?) {
                                Handler(Looper.getMainLooper()).post {
                                    val sourceIsTrusted = isTrustedSunoPage(webViewRef?.url)
                                    checking = false
                                    if (sourceIsTrusted && !token.isNullOrBlank()) {
                                        onTokenCaptured(token)
                                    }
                                }
                            }
                        },
                        "AndroidBridge",
                    )
                    loadUrl("https://suno.com")
                    webViewRef = this
                }
            },
        )
        Row(modifier = Modifier.fillMaxWidth().padding(16.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(
                onClick = {
                    val webView = webViewRef
                    if (webView != null && isTrustedSunoPage(webView.url)) {
                        checking = true
                        webView.evaluateJavascript(TOKEN_CAPTURE_JS, null)
                    } else {
                        checking = false
                        webView?.loadUrl("https://suno.com")
                    }
                },
                modifier = Modifier.weight(1f),
            ) {
                if (checking) {
                    CircularProgressIndicator(modifier = Modifier.height(20.dp), strokeWidth = 2.dp)
                } else {
                    Text("Proveri prijavu")
                }
            }
            OutlinedButton(onClick = { webViewRef?.loadUrl("https://suno.com") }) {
                Text("Osveži stranicu")
            }
        }
    }
}

@Composable
private fun SunoLibraryContent(state: SunoUiState, viewModel: SunoViewModel, modifier: Modifier = Modifier) {
    Column(modifier = modifier.fillMaxSize().padding(16.dp)) {
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            Text("Moj Suno nalog", style = MaterialTheme.typography.headlineMedium)
            TextButton(onClick = viewModel::disconnect) { Text("Odjavi se") }
        }
        state.error?.let {
            Text(it, color = MaterialTheme.colorScheme.error, modifier = Modifier.padding(vertical = 8.dp))
        }
        Row(modifier = Modifier.fillMaxWidth().padding(bottom = 8.dp), horizontalArrangement = Arrangement.End) {
            TextButton(onClick = viewModel::refreshFeed, enabled = !state.loadingFeed) { Text("Osveži listu") }
        }
        if (state.loadingFeed && state.songs.isEmpty()) {
            CircularProgressIndicator()
        } else if (state.songs.isEmpty()) {
            Text("Nema pesama na tvom Suno nalogu (ili Suno API još nije vratio rezultat).")
        } else {
            LazyColumn(
                verticalArrangement = Arrangement.spacedBy(8.dp),
                contentPadding = PaddingValues(bottom = 24.dp),
            ) {
                items(state.songs, key = { it.id }) { clip ->
                    SunoClipRow(
                        clip = clip,
                        downloading = clip.id in state.downloadingIds,
                        downloaded = clip.id in state.downloadedIds,
                        onDownload = { viewModel.downloadSong(clip) },
                    )
                }
            }
        }
    }
}

@Composable
private fun SunoClipRow(clip: SunoClip, downloading: Boolean, downloaded: Boolean, onDownload: () -> Unit) {
    Card(modifier = Modifier.fillMaxWidth()) {
        ListItem(
            headlineContent = { Text(clip.title) },
            supportingContent = { Text(clip.status ?: clip.modelVersion ?: "") },
            trailingContent = {
                when {
                    downloading -> CircularProgressIndicator(modifier = Modifier.height(20.dp), strokeWidth = 2.dp)
                    downloaded -> Text("Preuzeto", style = MaterialTheme.typography.labelMedium)
                    clip.audioUrl != null -> TextButton(onClick = onDownload) { Text("Preuzmi") }
                    else -> Text("Nije spremno", style = MaterialTheme.typography.labelMedium)
                }
            },
        )
    }
}

// Mirrors app/cdp.py's refresh_session_activity() polling loop: Clerk can
// take a moment to attach to window after page load, so retry briefly
// instead of failing on the first check.
private const val TOKEN_CAPTURE_JS = """
(function() {
  (async () => {
    for (let i = 0; i < 20; i++) {
      try {
        const clerk = window.Clerk;
        const session = clerk && clerk.session;
        if (session) {
          let token = null;
          try { token = await session.getToken({skipCache: true}); } catch (e) {}
          if (!token) { try { token = await session.getToken(); } catch (e) {} }
          if (token) { AndroidBridge.onToken(token); return; }
        }
      } catch (e) {}
      await new Promise((r) => setTimeout(r, 300));
    }
    AndroidBridge.onToken(null);
  })();
})();
"""
