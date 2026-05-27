package net.sitkoff.fourexhex

import android.app.Activity
import android.content.Context
import android.graphics.Color
import android.graphics.PixelFormat
import android.hardware.SensorManager
import android.hardware.display.DisplayManager
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.util.Log
import android.view.Gravity
import android.view.OrientationEventListener
import android.view.View
import android.view.WindowManager
import org.godotengine.godot.Godot
import org.godotengine.godot.plugin.GodotPlugin

/**
 * Hides the Android rotation freeze-snapshot stretch behind a black overlay.
 *
 * On a portrait<->landscape change Android calls startFreezingDisplayLocked: it
 * snapshots the OLD-orientation frame and stretches that snapshot into the NEW
 * screen bounds until the app redraws (the single stretched frame). Critically,
 * that snapshot is taken BEFORE the app is notified (config change / surface
 * resize), so nothing on the Godot side can react in time. The window's rotation
 * animation modes can't remove the snapshot, and the only mode that can
 * (SEAMLESS) requires an opaque window, which Godot's translucent GL SurfaceView
 * prevents.
 *
 * The only signal that arrives BEFORE the freeze is the physical orientation
 * sensor. So this plugin watches it directly and, the moment the device tilts
 * past an orientation boundary, drops an opaque black panel window over the
 * surface. That black frame is what the OS snapshots, so the stretch is a
 * stretched black = invisible. The overlay is removed shortly after the display
 * actually rotates (a fresh frame is then on screen), with a fallback timeout so
 * a tilt that doesn't complete a rotation doesn't leave the screen stuck black.
 *
 * This is a heuristic: it trades the stretched frame for a brief blank, and may
 * blank on a tilt that doesn't become a rotation. It self-skips when auto-rotate
 * is off (no rotation will happen).
 */
class RotationFixPlugin(godot: Godot) : GodotPlugin(godot) {

    companion object {
        private const val TAG = "RotationFix"
        // How long after the display actually rotates to keep the blank up, so
        // the new-orientation frame is on screen before we reveal it. The OS
        // display freeze (which shows the stretched snapshot) outlasts the
        // onDisplayChanged callback by a few hundred ms, so this must comfortably
        // cover it or the tail of the stretch peeks out as the blank lifts.
        private const val DISPLAY_SETTLE_MS = 600L
        // Safety net: clear the blank if a predicted rotation never materializes
        // (e.g. the device tilted past the boundary and back).
        private const val FALLBACK_MS = 1000L
    }

    private val mainHandler = Handler(Looper.getMainLooper())
    private var orientationListener: OrientationEventListener? = null
    private var windowManager: WindowManager? = null
    private var contentResolver: android.content.ContentResolver? = null
    private var overlay: View? = null
    private var removeRunnable: Runnable? = null
    private var currentBand = -1
    private var lastDisplayRotation = -1

    override fun getPluginName(): String = "RotationFix"

    override fun onMainCreate(activity: Activity?): View? {
        activity ?: return null
        windowManager = activity.windowManager
        contentResolver = activity.contentResolver

        @Suppress("DEPRECATION")
        lastDisplayRotation = activity.windowManager.defaultDisplay.rotation

        val displayManager = activity.getSystemService(Context.DISPLAY_SERVICE) as DisplayManager
        displayManager.registerDisplayListener(object : DisplayManager.DisplayListener {
            override fun onDisplayChanged(displayId: Int) {
                @Suppress("DEPRECATION")
                val rot = activity.windowManager.defaultDisplay.rotation
                if (rot != lastDisplayRotation) {
                    lastDisplayRotation = rot
                    // Real rotation happened: keep the blank just past the new
                    // frame, then reveal.
                    scheduleRemoval(DISPLAY_SETTLE_MS)
                }
            }
            override fun onDisplayAdded(displayId: Int) {}
            override fun onDisplayRemoved(displayId: Int) {}
        }, mainHandler)

        orientationListener = object : OrientationEventListener(activity, SensorManager.SENSOR_DELAY_UI) {
            override fun onOrientationChanged(orientation: Int) {
                if (orientation == ORIENTATION_UNKNOWN) return
                val band = when {
                    orientation >= 315 || orientation < 45 -> 0
                    orientation < 135 -> 90
                    orientation < 225 -> 180
                    else -> 270
                }
                if (currentBand == -1) {
                    currentBand = band
                    return
                }
                if (band != currentBand) {
                    currentBand = band
                    onRotationImminent(activity)
                }
            }
        }
        if (orientationListener?.canDetectOrientation() == true) {
            orientationListener?.enable()
            Log.i(TAG, "OrientationEventListener enabled.")
        } else {
            Log.w(TAG, "Orientation cannot be detected; blank-overlay disabled.")
        }
        return null
    }

    private fun onRotationImminent(activity: Activity) {
        if (!isAutoRotateOn()) return
        Log.i(TAG, "Rotation imminent (band -> $currentBand); showing blank overlay.")
        showOverlay(activity)
        // Cleared sooner by the DisplayListener when the rotation lands; this is
        // the fallback for tilts that never become a rotation.
        scheduleRemoval(FALLBACK_MS)
    }

    private fun isAutoRotateOn(): Boolean {
        val resolver = contentResolver ?: return false
        return Settings.System.getInt(resolver, Settings.System.ACCELEROMETER_ROTATION, 0) == 1
    }

    private fun showOverlay(activity: Activity) {
        mainHandler.post {
            if (overlay != null) return@post
            val wm = windowManager ?: return@post
            val view = View(activity).apply { setBackgroundColor(Color.BLACK) }
            val params = WindowManager.LayoutParams(
                WindowManager.LayoutParams.MATCH_PARENT,
                WindowManager.LayoutParams.MATCH_PARENT,
                WindowManager.LayoutParams.TYPE_APPLICATION_PANEL,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                    WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL or
                    WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN or
                    WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
                PixelFormat.OPAQUE
            ).apply {
                token = activity.window.decorView.windowToken
                gravity = Gravity.TOP or Gravity.START
            }
            try {
                wm.addView(view, params)
                overlay = view
            } catch (e: Exception) {
                Log.w(TAG, "Failed to add blank overlay: ${e.message}")
            }
        }
    }

    private fun scheduleRemoval(delayMs: Long) {
        mainHandler.post {
            removeRunnable?.let { mainHandler.removeCallbacks(it) }
            val r = Runnable { removeOverlay() }
            removeRunnable = r
            mainHandler.postDelayed(r, delayMs)
        }
    }

    private fun removeOverlay() {
        val view = overlay ?: return
        overlay = null
        try {
            windowManager?.removeView(view)
            Log.i(TAG, "Blank overlay removed.")
        } catch (e: Exception) {
            Log.w(TAG, "Failed to remove blank overlay: ${e.message}")
        }
    }
}
