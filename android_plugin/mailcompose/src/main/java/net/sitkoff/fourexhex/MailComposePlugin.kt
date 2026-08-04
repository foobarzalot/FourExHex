package net.sitkoff.fourexhex

import android.content.ActivityNotFoundException
import android.content.Intent
import android.net.Uri
import android.util.Log
import androidx.core.content.FileProvider
import java.io.File
import org.godotengine.godot.Godot
import org.godotengine.godot.plugin.GodotPlugin
import org.godotengine.godot.plugin.UsedByGodot

/**
 * The Android rung of the bug-report compose chain (see MailBridge.cs).
 *
 * The vendored SharePlugin fires a plain ACTION_SEND, which shows a generic
 * share sheet with no recipient field. This fires ACTION_SEND with EXTRA_EMAIL
 * *and* a mailto: selector, so the chooser offers only mail apps and they
 * honour the prefilled address — recipient and attachment in one step.
 */
class MailComposePlugin(godot: Godot) : GodotPlugin(godot) {

    companion object {
        private const val TAG = "FourExHex"
    }

    override fun getPluginName(): String = "MailCompose"

    /**
     * Open a mail composer prefilled with [to], [subject] and [body], carrying
     * the file at [attachmentPath]. Returns false if the attachment cannot be
     * published or no mail app exists, so the caller can fall back to the
     * share sheet rather than the player getting nothing.
     */
    @UsedByGodot
    fun compose(
        to: String,
        subject: String,
        body: String,
        attachmentPath: String,
    ): Boolean {
        val activity = activity ?: run {
            Log.w(TAG, "[report] no activity to compose from")
            return false
        }

        // Resolve the content:// URI up front, on this thread: it is the step
        // that can legitimately fail (a path outside mailcompose_paths.xml),
        // and we want that answer before returning.
        val uri: Uri = try {
            FileProvider.getUriForFile(
                activity,
                "${activity.packageName}.mailcompose.fileprovider",
                File(attachmentPath),
            )
        } catch (e: IllegalArgumentException) {
            Log.w(TAG, "[report] cannot publish '$attachmentPath': ${e.message}")
            return false
        }

        val packageManager = activity.packageManager

        // Which apps are mail clients. A "mailto:" SENDTO filter is the only
        // reliable marker; ACTION_SEND alone matches every share target.
        // The <queries> entry in our manifest is what makes this visible on
        // Android 11+ — without it the list is silently empty.
        val mailPackages = packageManager
            .queryIntentActivities(Intent(Intent.ACTION_SENDTO, Uri.parse("mailto:")), 0)
            .map { it.activityInfo.packageName }
            .distinct()

        val template = Intent(Intent.ACTION_SEND).apply {
            // Widest ACTION_SEND match. The attachment's real type comes from
            // the provider, not from here, and narrowing this to
            // application/zip excludes mail apps that only filter on */*.
            type = "*/*"
            putExtra(Intent.EXTRA_EMAIL, arrayOf(to))
            putExtra(Intent.EXTRA_SUBJECT, subject)
            putExtra(Intent.EXTRA_TEXT, body)
            putExtra(Intent.EXTRA_STREAM, uri)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }

        // Name each mail app with setPackage: a resolved activity has to match
        // the intent's own action and type, which package-targeting guarantees.
        // Resolving each candidate here is also what makes the return value
        // honest — startActivity runs on the UI thread, so an
        // ActivityNotFoundException there would land long after the caller had
        // been told this rung worked, leaving the player with nothing.
        val targeted = mailPackages
            .map { Intent(template).setPackage(it) }
            .filter { packageManager.resolveActivity(it, 0) != null }

        if (targeted.isEmpty()) {
            Log.w(TAG, "[report] no mail app can take the report — falling back")
            return false
        }

        val chooser = Intent.createChooser(targeted.first(), "Send bug report").apply {
            // Also on the chooser: the grant has to survive the hand-off to
            // whichever app the player picks, not just sit on the inner intent.
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            if (targeted.size > 1) {
                putExtra(
                    Intent.EXTRA_INITIAL_INTENTS,
                    targeted.drop(1).toTypedArray(),
                )
            }
        }

        // startActivity belongs on the UI thread; Godot calls plugin methods
        // from its own thread.
        activity.runOnUiThread {
            try {
                activity.startActivity(chooser)
                Log.i(TAG, "[report] android composer opened for $uri " +
                    "(${targeted.size} mail app(s))")
            } catch (e: ActivityNotFoundException) {
                Log.w(TAG, "[report] mail app vanished between resolve and " +
                    "launch: ${e.message}")
            }
        }
        return true
    }
}
