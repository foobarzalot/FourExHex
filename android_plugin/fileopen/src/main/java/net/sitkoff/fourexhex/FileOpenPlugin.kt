package net.sitkoff.fourexhex

import org.godotengine.godot.Godot
import org.godotengine.godot.plugin.GodotPlugin
import org.godotengine.godot.plugin.UsedByGodot

/**
 * Managed-code side of the incoming-file surface. [FileOpenActivity] receives
 * the OS intents (tap-to-open / share-to-app), copies the content into the
 * cache, and parks the absolute path here; the game polls
 * [get_pending_open_path] at startup and after every resume — the activity
 * always foregrounds the app, so a resume poll always follows.
 */
class FileOpenPlugin(godot: Godot) : GodotPlugin(godot) {

    companion object {
        private val lock = Any()
        private var pendingPath: String? = null

        @JvmStatic
        fun parkPendingPath(path: String) {
            synchronized(lock) { pendingPath = path }
        }
    }

    override fun getPluginName(): String = "FileOpen"

    /** Consume-once: absolute path of the most recent received file, or ""
     * when nothing is pending. */
    @UsedByGodot
    fun get_pending_open_path(): String {
        synchronized(lock) {
            val path = pendingPath
            pendingPath = null
            return path ?: ""
        }
    }
}
