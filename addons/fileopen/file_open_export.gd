@tool
extends EditorExportPlugin

# Links the FileOpen AAR into Android builds. The receiving activity and its
# .fxhmap intent-filters live in the AAR's own manifest and arrive via the
# gradle manifest merge — nothing is injected into the engine activity, whose
# singleInstancePerTask launch mode duplicates the engine when addressed
# directly with a data-carrying intent.


func _get_name() -> String:
	return "FileOpen"


func _supports_platform(platform: EditorExportPlatform) -> bool:
	return platform is EditorExportPlatformAndroid


func _get_android_libraries(platform: EditorExportPlatform, debug: bool) -> PackedStringArray:
	var variant := "debug" if debug else "release"
	return PackedStringArray(["fileopen/bin/%s/FileOpen.aar" % variant])
