package net.sitkoff.fourexhex

import androidx.core.content.FileProvider

/**
 * Empty [FileProvider] subclass whose only job is to have a distinct class
 * name.
 *
 * The Android manifest merger keys `<provider>` elements by `android:name`,
 * and Godot's own template already registers `androidx.core.content.FileProvider`
 * (authority `${applicationId}.fileprovider`). Declaring ours with that same
 * class name collides at merge time even though the authority differs:
 *
 *     Attribute provider#androidx.core.content.FileProvider@authorities
 *       value=(...fileprovider) from [godot-lib...aar]
 *       is also present at [MailCompose.aar] value=(...mailcompose.fileprovider)
 *
 * The vendored SharePlugin ships a `ShareFileProvider` for the same reason.
 * Subclassing is preferred over piggybacking on the engine's provider, whose
 * declared paths are its own business and could change under us.
 */
class MailComposeFileProvider : FileProvider()
