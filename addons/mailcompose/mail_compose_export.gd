@tool
extends EditorExportPlugin

# Links the MailCompose AAR into Android builds. The FileProvider (and its
# authority) plus the <queries> entry that makes mail apps visible on
# Android 11+ live in the AAR's own manifest and arrive via the gradle
# manifest merge.


func _get_name() -> String:
	return "MailCompose"


func _supports_platform(platform: EditorExportPlatform) -> bool:
	return platform is EditorExportPlatformAndroid


func _get_android_libraries(platform: EditorExportPlatform, debug: bool) -> PackedStringArray:
	var variant := "debug" if debug else "release"
	return PackedStringArray(["mailcompose/bin/%s/MailCompose.aar" % variant])
