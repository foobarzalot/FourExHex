package net.sitkoff.fourexhex

import android.app.Activity
import android.content.Intent
import android.net.Uri
import android.os.Bundle
import android.provider.OpenableColumns
import android.util.Log
import java.io.File

/**
 * Invisible entry point for .fxhmap deliveries (ACTION_VIEW tap-to-open and
 * ACTION_SEND share-to-app; the intent-filters live in this module's
 * manifest). Copies the delivered content into cacheDir/open_received/, parks
 * the path via [FileOpenPlugin.parkPendingPath], then re-launches the app
 * through its normal launcher intent and finishes.
 *
 * The launcher-intent hop is the point: the engine activity is
 * singleInstancePerTask, and addressing it (or its launcher alias) directly
 * with a data-carrying intent can spawn a second engine instance next to a
 * running one — a black screen, with the delivery lost in the doomed
 * instance. A plain launcher intent has icon-tap semantics: reuse the running
 * task or cold-start it, never duplicate it.
 */
class FileOpenActivity : Activity() {

    companion object {
        private const val TAG = "FileOpen"
        private const val CACHE_SUBDIR = "open_received"
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        receive(intent)
        val launch: Intent? = packageManager.getLaunchIntentForPackage(packageName)
        if (launch != null) startActivity(launch)
        finish()
    }

    private fun receive(intent: Intent?) {
        if (intent == null) return
        val uri: Uri? = when (intent.action) {
            Intent.ACTION_VIEW -> intent.data
            Intent.ACTION_SEND ->
                intent.clipData?.takeIf { it.itemCount > 0 }?.getItemAt(0)?.uri
                    ?: @Suppress("DEPRECATION") intent.getParcelableExtra(Intent.EXTRA_STREAM)
            else -> null
        }
        if (uri == null) {
            Log.w(TAG, "no usable uri on ${intent.action}")
            return
        }

        try {
            val name = sanitizeFilename(resolveDisplayName(uri))
            val dir = File(cacheDir, CACHE_SUBDIR)
            dir.mkdirs()
            val out = File(dir, name)
            val input = contentResolver.openInputStream(uri)
            if (input == null) {
                Log.e(TAG, "openInputStream returned null for $uri")
                return
            }
            input.use { source -> out.outputStream().use { source.copyTo(it) } }
            Log.i(TAG, "copied received file to ${out.absolutePath}")
            FileOpenPlugin.parkPendingPath(out.absolutePath)
        } catch (e: Exception) {
            Log.e(TAG, "failed to receive file from $uri", e)
        }
    }

    private fun resolveDisplayName(uri: Uri): String {
        if (uri.scheme == "content") {
            contentResolver
                .query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)
                ?.use { cursor ->
                    if (cursor.moveToFirst()) {
                        val index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                        if (index >= 0) {
                            val name = cursor.getString(index)
                            if (!name.isNullOrBlank()) return name
                        }
                    }
                }
        }
        val segment = uri.lastPathSegment
        if (!segment.isNullOrBlank()) return segment
        return "opened_" + System.currentTimeMillis()
    }

    private fun sanitizeFilename(name: String): String =
        name.replace(Regex("[^a-zA-Z0-9._-]"), "_")
}
